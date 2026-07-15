"""Runtime capability discovery for the desktop app and agent bridge."""

from __future__ import annotations

import importlib.util
from pathlib import Path
from typing import Any

from rushframe_intelligence.ffmpeg_tools import resolve_tool_path, run_tool

_MODULES = {
    "scene_detection": "scenedetect",
    "transcription": "faster_whisper",
    "music_analysis": "librosa",
    "visual_quality": "cv2",
    "neural_semantic_search": "sentence_transformers",
    "precise_word_alignment": "whisperx",
    "speaker_diarization": "pyannote.audio",
    "ocr": "paddleocr",
    "sound_events": "laion_clap",
    "groq_visual_understanding": "httpx",
    "cloudflare_visual_understanding": "httpx",
}


def discover_capabilities(
    ffmpeg_path: Path | str | None = None,
    ffprobe_path: Path | str | None = None,
) -> dict[str, Any]:
    capabilities = {
        name: _module_available(module)
        for name, module in _MODULES.items()
    }
    capabilities["semantic_search"] = True
    capabilities["ffmpeg"] = _tool_available(resolve_tool_path("ffmpeg", ffmpeg_path))
    capabilities["ffprobe"] = _tool_available(resolve_tool_path("ffprobe", ffprobe_path))
    capabilities["technical_probe"] = capabilities["ffprobe"] or capabilities["ffmpeg"]
    capabilities["core_ready"] = all(
        capabilities[name]
        for name in ("ffmpeg", "technical_probe", "scene_detection", "transcription")
    )
    return capabilities


def _module_available(module: str) -> bool:
    try:
        return importlib.util.find_spec(module) is not None
    except (ImportError, ModuleNotFoundError, ValueError):
        return False


def _tool_available(path: Path | str) -> bool:
    try:
        result = run_tool(
            path,
            ["-version"],
            check=False,
            capture_output=True,
            text=True,
        )
        return result.returncode == 0
    except OSError:
        return False
