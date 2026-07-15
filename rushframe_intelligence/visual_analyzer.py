"""Replaceable visual-understanding providers for sampled scene frames."""

from __future__ import annotations

import base64
import json
import os
import re
from pathlib import Path
from typing import Any, Protocol
from urllib.parse import quote

import httpx

from rushframe_intelligence.models import SceneAnalysis

_JSON_BLOCK = re.compile(r"\{.*\}", re.DOTALL)


class VisualUnderstandingUnavailable(RuntimeError):
    pass


class VisualProvider(Protocol):
    def analyze(self, frame_paths: list[Path], transcript: str = "") -> dict[str, Any]: ...


class GroqFrameProvider:
    """GroqCloud vision provider using the OpenAI-compatible chat endpoint."""

    def __init__(self, model: str | None = None) -> None:
        self.model = model or os.getenv(
            "RUSHFRAME_GROQ_MODEL",
            "meta-llama/llama-4-scout-17b-16e-instruct",
        )

    def analyze(self, frame_paths: list[Path], transcript: str = "") -> dict[str, Any]:
        api_key = os.getenv("GROQ_API_KEY", "").strip()
        if not api_key:
            raise VisualUnderstandingUnavailable("GROQ_API_KEY is not configured")
        return _analyze_openai_compatible_frames(
            endpoint="https://api.groq.com/openai/v1/chat/completions",
            api_key=api_key,
            model=self.model,
            frame_paths=frame_paths,
            transcript=transcript,
            provider_name="GroqCloud",
        )


class CloudflareFrameProvider:
    """Cloudflare Workers AI vision provider using its OpenAI-compatible endpoint."""

    def __init__(self, model: str | None = None) -> None:
        self.model = model or os.getenv(
            "RUSHFRAME_CLOUDFLARE_MODEL",
            "@cf/meta/llama-4-scout-17b-16e-instruct",
        )

    def analyze(self, frame_paths: list[Path], transcript: str = "") -> dict[str, Any]:
        account_id = os.getenv("CLOUDFLARE_ACCOUNT_ID", "").strip()
        api_token = (
            os.getenv("CLOUDFLARE_API_TOKEN")
            or os.getenv("CLOUDFLARE_AUTH_TOKEN")
            or ""
        ).strip()
        if not account_id or not api_token:
            raise VisualUnderstandingUnavailable(
                "CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN are required"
            )
        endpoint = (
            "https://api.cloudflare.com/client/v4/accounts/"
            f"{quote(account_id, safe='')}/ai/v1/chat/completions"
        )
        return _analyze_openai_compatible_frames(
            endpoint=endpoint,
            api_key=api_token,
            model=self.model,
            frame_paths=frame_paths,
            transcript=transcript,
            provider_name="Cloudflare Workers AI",
        )


def _analyze_openai_compatible_frames(
    *,
    endpoint: str,
    api_key: str,
    model: str,
    frame_paths: list[Path],
    transcript: str,
    provider_name: str,
) -> dict[str, Any]:
    if not frame_paths:
        return {}

    content: list[dict[str, Any]] = [{"type": "text", "text": _prompt(transcript)}]
    for path in frame_paths[:5]:
        image = Path(path)
        mime_type = "image/png" if image.suffix.lower() == ".png" else "image/jpeg"
        encoded = base64.b64encode(image.read_bytes()).decode("ascii")
        content.append(
            {
                "type": "image_url",
                "image_url": {"url": f"data:{mime_type};base64,{encoded}"},
            }
        )

    payload = {
        "model": model,
        "messages": [{"role": "user", "content": content}],
        "temperature": 0.1,
        "max_tokens": 768,
        "stream": False,
        "response_format": {"type": "json_object"},
    }
    try:
        with httpx.Client(timeout=90.0) as client:
            response = client.post(
                endpoint,
                headers={
                    "Authorization": f"Bearer {api_key}",
                    "Content-Type": "application/json",
                },
                json=payload,
            )
    except httpx.HTTPError as exc:
        raise VisualUnderstandingUnavailable(
            f"{provider_name} request failed: {exc}"
        ) from exc

    if response.is_error:
        detail = response.text[:500].strip()
        raise VisualUnderstandingUnavailable(
            f"{provider_name} request failed with HTTP {response.status_code}: {detail}"
        )

    try:
        body = response.json()
        raw_content = body["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError, ValueError) as exc:
        raise VisualUnderstandingUnavailable(
            f"{provider_name} returned an unexpected response"
        ) from exc

    if isinstance(raw_content, list):
        raw_content = "\n".join(
            str(item.get("text", ""))
            for item in raw_content
            if isinstance(item, dict) and item.get("text")
        )
    if not isinstance(raw_content, str):
        raise VisualUnderstandingUnavailable(
            f"{provider_name} returned non-text content"
        )
    return _parse_json(raw_content)


def _prompt(transcript: str) -> str:
    transcript_context = transcript.strip() or "No speech transcript is available."
    return (
        "Analyze these sampled frames as a video editor. Return JSON only with keys: "
        "description, summary, tags, subjects, actions, visible_text, location, shot_type, "
        "camera_motion, mood, visual_energy, usable, confidence, editing_roles. "
        f"Speech during this scene: {transcript_context}"
    )


def _parse_json(text: str) -> dict[str, Any]:
    match = _JSON_BLOCK.search(text)
    if not match:
        raise ValueError("Visual provider did not return a JSON object")
    value = json.loads(match.group(0))
    if not isinstance(value, dict):
        raise ValueError("Visual provider JSON must be an object")
    return value


def apply_visual_result(scene: SceneAnalysis, value: dict[str, Any]) -> None:
    scene.description = _string(value.get("description"))
    scene.summary = _string(value.get("summary")) or scene.description
    scene.tags = _strings(value.get("tags"))
    scene.subjects = _strings(value.get("subjects"))
    scene.actions = _strings(value.get("actions"))
    scene.visible_text = _strings(value.get("visible_text"))
    scene.location = _string(value.get("location"))
    scene.shot_type = _string(value.get("shot_type"))
    scene.camera_motion = _string(value.get("camera_motion"))
    scene.mood = _string(value.get("mood"))
    scene.visual_energy = _float(value.get("visual_energy"))
    scene.confidence = _float(value.get("confidence"))
    scene.usable = bool(value.get("usable", True))
    scene.editing_roles = _strings(value.get("editing_roles"))


def _string(value: Any) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _strings(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    return list(dict.fromkeys(str(item).strip() for item in value if str(item).strip()))


def _float(value: Any) -> float | None:
    try:
        return max(0.0, min(1.0, float(value)))
    except (TypeError, ValueError):
        return None
