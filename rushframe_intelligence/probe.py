"""Technical media metadata extraction with ffprobe and FFmpeg fallback."""

from __future__ import annotations

import json
import re
from fractions import Fraction
from pathlib import Path
from typing import Any

from rushframe_intelligence.ffmpeg_tools import resolve_tool_path, run_tool
from rushframe_intelligence.models import TechnicalMetadata


class ProbeError(RuntimeError):
    pass


_DURATION = re.compile(r"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")
_BIT_RATE = re.compile(r"bitrate:\s*(\d+)\s*kb/s", re.IGNORECASE)
_VIDEO_LINE = re.compile(r"Video:\s*([^,\s]+).*?(\d{2,5})x(\d{2,5}).*?(\d+(?:\.\d+)?)\s*fps", re.IGNORECASE)
_AUDIO_LINE = re.compile(r"Audio:\s*([^,\s]+).*?(\d{4,6})\s*Hz(?:,\s*([^,\r\n]+))?", re.IGNORECASE)


def _number(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _integer(value: Any) -> int | None:
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _fps(value: str | None) -> float | None:
    if not value or value in {"0/0", "N/A"}:
        return None
    try:
        return float(Fraction(value))
    except (ValueError, ZeroDivisionError):
        return None


def probe_media(
    media_path: Path | str,
    ffprobe_path: Path | str | None = None,
    ffmpeg_path: Path | str | None = None,
) -> TechnicalMetadata:
    source = Path(media_path)
    if not source.is_file():
        raise FileNotFoundError(f"Media file does not exist: {source}")

    try:
        return _probe_with_ffprobe(source, ffprobe_path)
    except (OSError, ProbeError):
        return _probe_with_ffmpeg(source, ffmpeg_path)


def _probe_with_ffprobe(source: Path, ffprobe_path: Path | str | None) -> TechnicalMetadata:
    ffprobe = resolve_tool_path("ffprobe", ffprobe_path)
    process = run_tool(
        ffprobe,
        [
            "-v", "error",
            "-show_format",
            "-show_streams",
            "-of", "json",
            source,
        ],
        check=False,
        capture_output=True,
        text=True,
    )
    if process.returncode != 0:
        raise ProbeError(process.stderr.strip() or "ffprobe failed")

    try:
        payload = json.loads(process.stdout)
    except json.JSONDecodeError as exc:
        raise ProbeError("ffprobe returned invalid JSON") from exc

    streams = payload.get("streams") or []
    video = next((stream for stream in streams if stream.get("codec_type") == "video"), None)
    audio = next((stream for stream in streams if stream.get("codec_type") == "audio"), None)
    fmt = payload.get("format") or {}

    width = _integer(video.get("width")) if video else None
    height = _integer(video.get("height")) if video else None
    average_fps = _fps(video.get("avg_frame_rate")) if video else None
    nominal_fps = _fps(video.get("r_frame_rate")) if video else None
    duration = max(
        _number(fmt.get("duration")),
        _number(video.get("duration")) if video else 0.0,
        _number(audio.get("duration")) if audio else 0.0,
    )
    return TechnicalMetadata(
        duration=max(0.0, duration),
        width=width,
        height=height,
        fps=average_fps or nominal_fps,
        video_codec=str(video.get("codec_name")) if video and video.get("codec_name") else None,
        audio_codec=str(audio.get("codec_name")) if audio and audio.get("codec_name") else None,
        audio_channels=_integer(audio.get("channels")) if audio else None,
        sample_rate=_integer(audio.get("sample_rate")) if audio else None,
        bit_rate=_integer(fmt.get("bit_rate")),
        orientation=_orientation(width, height),
        variable_frame_rate=(
            abs(average_fps - nominal_fps) > 0.01
            if average_fps and nominal_fps
            else None
        ),
        has_video=video is not None,
        has_audio=audio is not None,
    )


def _probe_with_ffmpeg(source: Path, ffmpeg_path: Path | str | None) -> TechnicalMetadata:
    ffmpeg = resolve_tool_path("ffmpeg", ffmpeg_path)
    process = run_tool(
        ffmpeg,
        ["-hide_banner", "-i", source, "-map", "0", "-f", "null", "-"],
        check=False,
        capture_output=True,
        text=True,
    )
    diagnostics = process.stderr or ""
    duration_match = _DURATION.search(diagnostics)
    video_match = _VIDEO_LINE.search(diagnostics)
    audio_match = _AUDIO_LINE.search(diagnostics)
    bitrate_match = _BIT_RATE.search(diagnostics)
    if not duration_match and not video_match and not audio_match:
        raise ProbeError(diagnostics.strip() or "FFmpeg could not read media metadata")

    duration = 0.0
    if duration_match:
        hours, minutes, seconds = duration_match.groups()
        duration = int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    width = int(video_match.group(2)) if video_match else None
    height = int(video_match.group(3)) if video_match else None
    channel_text = audio_match.group(3).lower() if audio_match and audio_match.group(3) else ""
    channels = 1 if "mono" in channel_text else 2 if "stereo" in channel_text else None
    return TechnicalMetadata(
        duration=duration,
        width=width,
        height=height,
        fps=float(video_match.group(4)) if video_match else None,
        video_codec=video_match.group(1) if video_match else None,
        audio_codec=audio_match.group(1) if audio_match else None,
        audio_channels=channels,
        sample_rate=int(audio_match.group(2)) if audio_match else None,
        bit_rate=int(bitrate_match.group(1)) * 1000 if bitrate_match else None,
        orientation=_orientation(width, height),
        variable_frame_rate=None,
        has_video=video_match is not None,
        has_audio=audio_match is not None,
    )


def _orientation(width: int | None, height: int | None) -> str | None:
    if not width or not height:
        return None
    return "portrait" if height > width else "square" if height == width else "landscape"
