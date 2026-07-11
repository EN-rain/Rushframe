"""Replaceable visual-understanding providers for sampled scene frames."""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any, Protocol

from rushframe_intelligence.gemini_analyzer import analyze_frame
from rushframe_intelligence.models import SceneAnalysis

_JSON_BLOCK = re.compile(r"\{.*\}", re.DOTALL)


class VisualUnderstandingUnavailable(RuntimeError):
    pass


class VisualProvider(Protocol):
    def analyze(self, frame_paths: list[Path], transcript: str = "") -> dict[str, Any]: ...


class GeminiFrameProvider:
    """Cloud fallback. Each invocation is explicit and requires GEMINI_API_KEY."""

    def analyze(self, frame_paths: list[Path], transcript: str = "") -> dict[str, Any]:
        if not frame_paths:
            return {}
        return analyze_frame(
            frame_paths[len(frame_paths) // 2],
            prompt=_prompt(transcript),
        )


class QwenLocalProvider:
    """Optional local Qwen2.5-VL adapter loaded only when selected."""

    def __init__(self, model_name: str = "Qwen/Qwen2.5-VL-3B-Instruct") -> None:
        self.model_name = model_name
        self._model: Any = None
        self._processor: Any = None

    def _load(self) -> None:
        if self._model is not None:
            return
        try:
            from transformers import AutoProcessor, Qwen2_5_VLForConditionalGeneration
        except ImportError as exc:
            raise VisualUnderstandingUnavailable(
                "transformers is required for the local Qwen visual provider."
            ) from exc
        self._model = Qwen2_5_VLForConditionalGeneration.from_pretrained(
            self.model_name,
            torch_dtype="auto",
            device_map="auto",
        )
        self._processor = AutoProcessor.from_pretrained(self.model_name)

    def analyze(self, frame_paths: list[Path], transcript: str = "") -> dict[str, Any]:
        self._load()
        try:
            from qwen_vl_utils import process_vision_info
        except ImportError as exc:
            raise VisualUnderstandingUnavailable("qwen-vl-utils is required for Qwen visual input.") from exc

        content: list[dict[str, Any]] = [
            {"type": "image", "image": str(path)} for path in frame_paths
        ]
        content.append({"type": "text", "text": _prompt(transcript)})
        messages = [{"role": "user", "content": content}]
        text = self._processor.apply_chat_template(messages, tokenize=False, add_generation_prompt=True)
        image_inputs, video_inputs = process_vision_info(messages)
        inputs = self._processor(
            text=[text],
            images=image_inputs,
            videos=video_inputs,
            padding=True,
            return_tensors="pt",
        ).to(self._model.device)
        generated = self._model.generate(**inputs, max_new_tokens=512)
        trimmed = [output[len(source):] for source, output in zip(inputs.input_ids, generated)]
        response = self._processor.batch_decode(trimmed, skip_special_tokens=True)[0]
        return _parse_json(response)


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
