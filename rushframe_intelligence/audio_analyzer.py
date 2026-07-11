"""Dependency-light audio measurements using FFmpeg filters."""

from __future__ import annotations

import re
from pathlib import Path

from rushframe_intelligence.ffmpeg_tools import resolve_tool_path, run_tool
from rushframe_intelligence.models import AudioAnalysis, AudioEvent, SilenceRange

_SILENCE_START = re.compile(r"silence_start:\s*([0-9.]+)")
_SILENCE_END = re.compile(r"silence_end:\s*([0-9.]+)\s*\|\s*silence_duration:\s*([0-9.]+)")
_MEAN_VOLUME = re.compile(r"mean_volume:\s*(-?[0-9.]+)\s*dB")
_MAX_VOLUME = re.compile(r"max_volume:\s*(-?[0-9.]+)\s*dB")
_I_LOUDNESS = re.compile(r"\bI:\s*(-?[0-9.]+)\s*LUFS")
_TRUE_PEAK = re.compile(r"\bPeak:\s*(-?[0-9.]+)\s*dBFS")


def _run_filter(ffmpeg: Path | str, source: Path, audio_filter: str) -> str:
    process = run_tool(
        ffmpeg,
        ["-hide_banner", "-nostats", "-i", source, "-vn", "-af", audio_filter, "-f", "null", "-"],
        check=False,
        capture_output=True,
        text=True,
    )
    # FFmpeg writes filter diagnostics to stderr even for a successful run.
    if process.returncode not in {0, 1}:
        raise RuntimeError(process.stderr.strip() or "FFmpeg audio analysis failed")
    return process.stderr


def detect_silence(
    media_path: Path | str,
    *,
    ffmpeg_path: Path | str | None = None,
    threshold_db: float = -38.0,
    minimum_duration: float = 0.45,
    media_duration: float | None = None,
) -> list[SilenceRange]:
    source = Path(media_path)
    ffmpeg = resolve_tool_path("ffmpeg", ffmpeg_path)
    stderr = _run_filter(
        ffmpeg,
        source,
        f"silencedetect=noise={threshold_db:g}dB:d={minimum_duration:g}",
    )

    ranges: list[SilenceRange] = []
    pending_start: float | None = None
    for line in stderr.splitlines():
        start_match = _SILENCE_START.search(line)
        if start_match:
            pending_start = float(start_match.group(1))
            continue
        end_match = _SILENCE_END.search(line)
        if end_match:
            end = float(end_match.group(1))
            duration = float(end_match.group(2))
            start = pending_start if pending_start is not None else max(0.0, end - duration)
            ranges.append(SilenceRange(start=start, end=end, duration=duration))
            pending_start = None

    if pending_start is not None and media_duration and media_duration > pending_start:
        ranges.append(
            SilenceRange(
                start=pending_start,
                end=media_duration,
                duration=media_duration - pending_start,
            )
        )
    return ranges


def analyze_audio_metrics(
    media_path: Path | str,
    *,
    ffmpeg_path: Path | str | None = None,
    media_duration: float | None = None,
) -> AudioAnalysis:
    source = Path(media_path)
    if not source.is_file():
        raise FileNotFoundError(f"Media file does not exist: {source}")

    ffmpeg = resolve_tool_path("ffmpeg", ffmpeg_path)
    volume_log = _run_filter(ffmpeg, source, "volumedetect")
    loudness_log = _run_filter(ffmpeg, source, "ebur128=peak=true")
    silence = detect_silence(
        source,
        ffmpeg_path=ffmpeg,
        media_duration=media_duration,
    )

    mean_match = _MEAN_VOLUME.search(volume_log)
    max_match = _MAX_VOLUME.search(volume_log)
    loudness_matches = _I_LOUDNESS.findall(loudness_log)
    peak_matches = _TRUE_PEAK.findall(loudness_log)
    mean_volume = float(mean_match.group(1)) if mean_match else None
    max_volume = float(max_match.group(1)) if max_match else None
    integrated = float(loudness_matches[-1]) if loudness_matches else None
    true_peak = float(peak_matches[-1]) if peak_matches else max_volume

    events = [
        AudioEvent(
            event_id=f"silence_{index:04d}",
            start=item.start,
            end=item.end,
            event_type="silence",
            label="silence",
            confidence=1.0,
        )
        for index, item in enumerate(silence, start=1)
    ]
    return AudioAnalysis(
        integrated_loudness_lufs=integrated,
        true_peak_db=true_peak,
        mean_volume_db=mean_volume,
        max_volume_db=max_volume,
        clipping_detected=true_peak is not None and true_peak >= -0.1,
        silence=silence,
        events=events,
    )
