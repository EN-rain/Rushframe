"""CPU-friendly shot detection backed by optional PySceneDetect."""

from __future__ import annotations

from pathlib import Path

from rushframe_intelligence.models import SceneAnalysis


class SceneDetectionUnavailable(RuntimeError):
    pass


def detect_scenes(video_path: Path | str, threshold: float = 27.0) -> list[SceneAnalysis]:
    """Return content-cut scene ranges.

    PySceneDetect is imported lazily so Rushframe still starts without the
    optional intelligence dependency group installed.
    """
    try:
        from scenedetect import ContentDetector, detect
    except ImportError as exc:
        raise SceneDetectionUnavailable(
            "PySceneDetect is not installed. Install Rushframe's 'intelligence' extras."
        ) from exc

    path = Path(video_path)
    if not path.is_file():
        raise FileNotFoundError(f"Video file does not exist: {path}")

    scenes = detect(
        str(path),
        ContentDetector(threshold=threshold),
        show_progress=False,
        start_in_scene=True,
    )
    result: list[SceneAnalysis] = []
    for index, (start, end) in enumerate(scenes, start=1):
        start_seconds = max(0.0, start.get_seconds())
        end_seconds = max(start_seconds, end.get_seconds())
        if end_seconds <= start_seconds:
            continue
        result.append(
            SceneAnalysis(
                scene_id=f"scene_{index:04d}",
                start=start_seconds,
                end=end_seconds,
            )
        )
    return result
