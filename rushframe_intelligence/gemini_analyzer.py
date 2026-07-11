"""Optional Gemini frame-understanding adapter.

The model name and API key are environment-configurable so Rushframe is not
locked to a specific free-tier model. Frames, not entire videos, are sent.
"""

from __future__ import annotations

import base64
import json
import os
import time
from pathlib import Path
from typing import Any

import httpx


class GeminiAnalysisError(RuntimeError):
    pass


FALLBACK_MODELS = [
    "gemini-flash-lite-latest",
    "gemini-2.5-flash-lite",
    "gemini-2.0-flash-lite",
    "gemini-2.0-flash",
]


def validate_gemini_model() -> dict[str, Any]:
    """Check whether any candidate Gemini model responds.

    Returns a dict with keys: ok, model, error.
    Does not throw; returns error details on failure.
    """
    api_key = os.getenv("GEMINI_API_KEY")
    if not api_key:
        return {"ok": False, "model": None, "error": "GEMINI_API_KEY not set"}
    model_name = os.getenv("RUSHFRAME_GEMINI_MODEL", "gemini-flash-lite-latest")
    candidates = [model_name, *FALLBACK_MODELS]
    seen: set[str] = set()
    last_detail = ""
    with httpx.Client(timeout=15.0) as client:
        for candidate in candidates:
            if candidate in seen:
                continue
            seen.add(candidate)
            endpoint = (
                "https://generativelanguage.googleapis.com/v1beta/models/"
                f"{candidate}:generateContent"
            )
            payload = {"contents": [{"parts": [{"text": "respond OK"}]}]}
            try:
                resp = client.post(
                    endpoint,
                    headers={"X-goog-api-key": api_key},
                    json=payload,
                )
                if resp.is_success:
                    return {"ok": True, "model": candidate, "error": None}
                last_detail = f"{candidate} HTTP {resp.status_code}: {resp.text[:200]}"
            except httpx.HTTPError as exc:
                last_detail = f"{candidate}: {exc}"
    return {"ok": False, "model": None, "error": last_detail or "No Gemini model responded"}


def analyze_frame(
    frame_path: Path | str,
    *,
    prompt: str,
    api_key: str | None = None,
    model: str | None = None,
    timeout: float = 60.0,
) -> dict[str, Any]:
    key = api_key or os.getenv("GEMINI_API_KEY")
    if not key:
        raise GeminiAnalysisError("GEMINI_API_KEY is not configured")

    model_name = model or os.getenv("RUSHFRAME_GEMINI_MODEL", "gemini-flash-lite-latest")
    image = Path(frame_path)
    mime_type = "image/png" if image.suffix.lower() == ".png" else "image/jpeg"
    encoded = base64.b64encode(image.read_bytes()).decode("ascii")
    requested = (
        prompt
        + "\nReturn only a valid JSON object. Preserve every key requested by the prompt. "
        + "Use arrays for list fields, booleans for flags, and numbers from 0 to 1 for confidence or score fields."
    )
    payload = {
        "contents": [{
            "parts": [
                {"text": requested},
                {"inline_data": {"mime_type": mime_type, "data": encoded}},
            ]
        }],
        "generationConfig": {"responseMimeType": "application/json"},
    }
    candidates = [model_name, *FALLBACK_MODELS]
    models_to_try = list(dict.fromkeys(candidates))
    last_error = ""
    with httpx.Client(timeout=timeout) as client:
        response: httpx.Response | None = None
        for selected_model in models_to_try:
            endpoint = (
                "https://generativelanguage.googleapis.com/v1beta/models/"
                f"{selected_model}:generateContent"
            )
            for attempt in range(3):
                response = client.post(
                    endpoint,
                    headers={"X-goog-api-key": key},
                    json=payload,
                )
                if not response.is_error:
                    break
                last_error = f"{selected_model} ({response.status_code}): {response.text[:500]}"
                if response.status_code not in {429, 500, 502, 503, 504}:
                    break
                if attempt < 2:
                    time.sleep(1.5 * (attempt + 1))
            if response is not None and not response.is_error:
                break
        else:
            raise GeminiAnalysisError(f"Gemini request failed: {last_error}")
    try:
        text = response.json()["candidates"][0]["content"]["parts"][0]["text"]
        result = json.loads(text)
    except (KeyError, IndexError, TypeError, json.JSONDecodeError) as exc:
        raise GeminiAnalysisError("Gemini returned an unexpected response") from exc
    if not isinstance(result, dict):
        raise GeminiAnalysisError("Gemini result must be a JSON object")
    return result
