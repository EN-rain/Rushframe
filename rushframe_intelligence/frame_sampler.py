"""Representative frame extraction for scene-level analysis."""

from __future__ import annotations

from pathlib import Path

from rushframe_intelligence.ffmpeg_tools import run_tool
from rushframe_intelligence.models import SceneAnalysis


def _sample_times(scene: SceneAnalysis) -> list[float]:
    duration = max(0.0, scene.end - scene.start)
    if duration <= 0.2:
        return [scene.start]
    return [
        scene.start + min(0.08, duration * 0.1),
        scene.start + duration * 0.5,
        max(scene.start, scene.end - min(0.08, duration * 0.1)),
    ]


def extract_scene_frames(
    source: Path,
    scenes: list[SceneAnalysis],
    destination: Path,
    *,
    ffmpeg_path: Path | str,
    width: int = 960,
) -> list[str]:
    destination.mkdir(parents=True, exist_ok=True)
    warnings: list[str] = []
    for scene in scenes:
        scene_dir = destination / scene.scene_id
        scene_dir.mkdir(parents=True, exist_ok=True)
        extracted: list[str] = []
        for index, timestamp in enumerate(_sample_times(scene), start=1):
            frame_path = scene_dir / f"frame_{index}.jpg"
            process = run_tool(
                ffmpeg_path,
                [
                    "-y", "-ss", f"{timestamp:.3f}", "-i", source,
                    "-frames:v", "1", "-vf", f"scale='min({width},iw)':-2",
                    "-q:v", "3", frame_path,
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            if process.returncode != 0 or not frame_path.is_file():
                warnings.append(f"frame extraction failed for {scene.scene_id} at {timestamp:.3f}s")
                continue
            extracted.append(str(frame_path))
        scene.frame_paths = extracted
        scene.frame_path = extracted[len(extracted) // 2] if extracted else None
    return warnings
