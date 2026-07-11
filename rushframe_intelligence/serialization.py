"""JSON serialization helpers with v1 backward compatibility."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from rushframe_intelligence.models import (
    AnalysisManifest,
    AudioAnalysis,
    AudioEvent,
    DuplicateTakeCandidate,
    DuplicateTakeGroup,
    EditingMoment,
    MediaAnalysis,
    MomentScores,
    MusicAnalysis,
    QualityScores,
    SceneAnalysis,
    SilenceRange,
    TechnicalMetadata,
    TranscriptSegment,
    WordTiming,
)


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if hasattr(value, "to_dict"):
        value = value.to_dict()
    elif isinstance(value, list):
        value = [item.to_dict() if hasattr(item, "to_dict") else _as_dict(item) for item in value]
    else:
        value = _as_dict(value)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def _as_dict(value: Any) -> Any:
    from dataclasses import asdict, is_dataclass

    return asdict(value) if is_dataclass(value) else value


def load_analysis(path: Path | str) -> MediaAnalysis:
    payload = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("Media analysis root must be an object")
    return analysis_from_dict(payload)


def analysis_from_dict(payload: dict[str, Any]) -> MediaAnalysis:
    metadata_payload = _dict(payload.get("metadata"))
    audio_payload = _dict(payload.get("audio"))
    if not audio_payload and payload.get("music"):
        audio_payload["music"] = payload.get("music")

    scenes = [_scene(_dict(item), index) for index, item in enumerate(_list(payload.get("scenes")), start=1)]
    transcript = [
        _transcript(_dict(item), index)
        for index, item in enumerate(_list(payload.get("transcript")), start=1)
    ]
    moments = [_moment(_dict(item), index) for index, item in enumerate(_list(payload.get("moments")), start=1)]
    groups = [_duplicate_group(_dict(item), index) for index, item in enumerate(_list(payload.get("duplicate_take_groups")), start=1)]
    manifest_payload = _dict(payload.get("manifest"))
    manifest = AnalysisManifest(**manifest_payload) if manifest_payload else None
    return MediaAnalysis(
        source_path=str(payload.get("source_path") or ""),
        source_checksum=str(payload.get("source_checksum") or ""),
        metadata=TechnicalMetadata(**_known(metadata_payload, TechnicalMetadata)),
        scenes=scenes,
        transcript=transcript,
        audio=_audio(audio_payload),
        moments=moments,
        duplicate_take_groups=groups,
        warnings=[str(item) for item in _list(payload.get("warnings"))],
        schema_version=str(payload.get("schema_version") or "1.0"),
        analysis_version=int(payload.get("analysis_version") or 1),
        manifest=manifest,
    )


def _scene(value: dict[str, Any], index: int) -> SceneAnalysis:
    quality = QualityScores(**_known(_dict(value.get("quality")), QualityScores))
    return SceneAnalysis(
        scene_id=str(value.get("scene_id") or f"scene_{index:04d}"),
        start=float(value.get("start") or 0.0),
        end=float(value.get("end") or 0.0),
        frame_path=_optional_string(value.get("frame_path")),
        frame_paths=[str(item) for item in _list(value.get("frame_paths"))],
        description=_optional_string(value.get("description")),
        summary=_optional_string(value.get("summary")),
        tags=_strings(value.get("tags")),
        subjects=_strings(value.get("subjects")),
        actions=_strings(value.get("actions")),
        visible_text=_strings(value.get("visible_text")),
        location=_optional_string(value.get("location")),
        shot_type=_optional_string(value.get("shot_type")),
        camera_motion=_optional_string(value.get("camera_motion")),
        mood=_optional_string(value.get("mood")),
        visual_energy=_optional_float(value.get("visual_energy")),
        usable=bool(value.get("usable", True)),
        confidence=_optional_float(value.get("confidence")),
        editing_roles=_strings(value.get("editing_roles")),
        quality=quality,
    )


def _transcript(value: dict[str, Any], index: int) -> TranscriptSegment:
    words = [
        WordTiming(
            start=float(item.get("start") or value.get("start") or 0.0),
            end=float(item.get("end") or value.get("end") or 0.0),
            text=str(item.get("text") or item.get("word") or "").strip(),
            confidence=_optional_float(item.get("confidence") or item.get("probability")),
        )
        for item in (_dict(raw) for raw in _list(value.get("words")))
    ]
    return TranscriptSegment(
        segment_id=str(value.get("segment_id") or f"transcript_{index:04d}"),
        start=float(value.get("start") or 0.0),
        end=float(value.get("end") or 0.0),
        text=str(value.get("text") or ""),
        words=words,
        speaker=_optional_string(value.get("speaker")),
        confidence=_optional_float(value.get("confidence")),
        emotion=_optional_string(value.get("emotion")),
        language=_optional_string(value.get("language")),
        contains_filler=bool(value.get("contains_filler", False)),
        repeated_take=bool(value.get("repeated_take", False)),
        hook_score=_optional_float(value.get("hook_score")),
        recommended_use=_strings(value.get("recommended_use")),
    )


def _audio(value: dict[str, Any]) -> AudioAnalysis:
    music_payload = _dict(value.get("music"))
    music = MusicAnalysis(**_known(music_payload, MusicAnalysis)) if music_payload else None
    silence = [SilenceRange(**_known(_dict(item), SilenceRange)) for item in _list(value.get("silence"))]
    events = [
        AudioEvent(
            event_id=str(item.get("event_id") or f"audio_{index:04d}"),
            start=float(item.get("start") or 0.0),
            end=float(item.get("end") or 0.0),
            event_type=str(item.get("event_type") or "unknown"),
            label=_optional_string(item.get("label")),
            confidence=_optional_float(item.get("confidence")),
            speaker=_optional_string(item.get("speaker")),
            clarity=_optional_float(item.get("clarity")),
            attributes=_dict(item.get("attributes")),
        )
        for index, item in enumerate((_dict(raw) for raw in _list(value.get("events"))), start=1)
    ]
    return AudioAnalysis(
        integrated_loudness_lufs=_optional_float(value.get("integrated_loudness_lufs")),
        true_peak_db=_optional_float(value.get("true_peak_db")),
        mean_volume_db=_optional_float(value.get("mean_volume_db")),
        max_volume_db=_optional_float(value.get("max_volume_db")),
        clipping_detected=bool(value.get("clipping_detected", False)),
        silence=silence,
        events=events,
        music=music,
    )


def _moment(value: dict[str, Any], index: int) -> EditingMoment:
    score_payload = _dict(value.get("scores"))
    scores = MomentScores(**_known(score_payload, MomentScores))
    return EditingMoment(
        moment_id=str(value.get("moment_id") or f"moment_{index:04d}"),
        start=float(value.get("start") or 0.0),
        end=float(value.get("end") or 0.0),
        summary=str(value.get("summary") or ""),
        scene_ids=_strings(value.get("scene_ids")),
        transcript_segment_ids=_strings(value.get("transcript_segment_ids")),
        audio_event_ids=_strings(value.get("audio_event_ids")),
        visual=_optional_string(value.get("visual")),
        speech=_optional_string(value.get("speech")),
        audio=_optional_string(value.get("audio")),
        editing_roles=_strings(value.get("editing_roles")),
        tags=_strings(value.get("tags")),
        scores=scores,
        confidence=float(value.get("confidence") or 0.0),
        evidence=_strings(value.get("evidence")),
        facts=_dict(value.get("facts")),
        interpretation=_dict(value.get("interpretation")),
    )


def _duplicate_group(value: dict[str, Any], index: int) -> DuplicateTakeGroup:
    candidates = [
        DuplicateTakeCandidate(
            moment_id=str(item.get("moment_id") or ""),
            score=float(item.get("score") or 0.0),
            recommended=bool(item.get("recommended", False)),
        )
        for item in (_dict(raw) for raw in _list(value.get("candidates")))
    ]
    return DuplicateTakeGroup(
        group_id=str(value.get("group_id") or f"take_group_{index:04d}"),
        purpose=str(value.get("purpose") or "Repeated take"),
        candidates=candidates,
    )


def _known(value: dict[str, Any], model_type: type[Any]) -> dict[str, Any]:
    from dataclasses import fields

    names = {item.name for item in fields(model_type)}
    return {key: item for key, item in value.items() if key in names}


def _dict(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


def _list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def _strings(value: Any) -> list[str]:
    return [str(item) for item in _list(value) if str(item).strip()]


def _optional_string(value: Any) -> str | None:
    return str(value).strip() if value is not None and str(value).strip() else None


def _optional_float(value: Any) -> float | None:
    try:
        return float(value) if value is not None else None
    except (TypeError, ValueError):
        return None
