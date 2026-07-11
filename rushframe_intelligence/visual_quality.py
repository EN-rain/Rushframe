"""Optional frame-quality scoring backed by OpenCV."""

from __future__ import annotations

from pathlib import Path

from rushframe_intelligence.models import QualityScores, SceneAnalysis


class VisualQualityUnavailable(RuntimeError):
    pass


def _clamp(value: float) -> float:
    return max(0.0, min(1.0, value))


def score_scene_quality(scene: SceneAnalysis) -> QualityScores:
    try:
        import cv2
        import numpy as np
    except ImportError as exc:
        raise VisualQualityUnavailable(
            "opencv-python and numpy are required for visual quality scoring."
        ) from exc

    frames = []
    for raw_path in scene.frame_paths or ([scene.frame_path] if scene.frame_path else []):
        if not raw_path:
            continue
        image = cv2.imread(str(Path(raw_path)))
        if image is not None:
            frames.append(image)
    if not frames:
        return QualityScores()

    sharpness_values: list[float] = []
    exposure_values: list[float] = []
    brightness_values: list[float] = []
    for frame in frames:
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        variance = float(cv2.Laplacian(gray, cv2.CV_64F).var())
        sharpness_values.append(_clamp(variance / 600.0))
        mean = float(np.mean(gray)) / 255.0
        brightness_values.append(mean)
        # Best score near mid exposure; penalize crushed blacks/highlights.
        exposure_values.append(_clamp(1.0 - abs(mean - 0.5) * 2.0))

    sharpness = sum(sharpness_values) / len(sharpness_values)
    exposure = sum(exposure_values) / len(exposure_values)
    stability = None
    if len(frames) >= 2:
        differences: list[float] = []
        for left, right in zip(frames, frames[1:]):
            left_gray = cv2.resize(cv2.cvtColor(left, cv2.COLOR_BGR2GRAY), (160, 90))
            right_gray = cv2.resize(cv2.cvtColor(right, cv2.COLOR_BGR2GRAY), (160, 90))
            differences.append(float(np.mean(cv2.absdiff(left_gray, right_gray))) / 255.0)
        stability = _clamp(1.0 - (sum(differences) / len(differences)) * 1.8)

    values = [sharpness, exposure]
    if stability is not None:
        values.append(stability)
    visual_quality = sum(values) / len(values)
    return QualityScores(
        visual_quality=visual_quality,
        sharpness=sharpness,
        exposure=exposure,
        stability=stability,
    )


def score_scenes(scenes: list[SceneAnalysis]) -> list[str]:
    warnings: list[str] = []
    for scene in scenes:
        try:
            scene.quality = score_scene_quality(scene)
            scene.usable = (scene.quality.visual_quality or 0.5) >= 0.2
        except VisualQualityUnavailable as exc:
            warnings.append(str(exc))
            break
        except Exception as exc:
            warnings.append(f"quality scoring failed for {scene.scene_id}: {exc}")
    return warnings
