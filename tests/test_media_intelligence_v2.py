from __future__ import annotations

import json
from pathlib import Path

from rushframe_intelligence.agent_context import build_agent_context
from rushframe_intelligence.context_index import MediaContextIndex
from rushframe_intelligence.duplicate_detector import find_duplicate_takes
from rushframe_intelligence.models import (
    AudioAnalysis,
    AudioEvent,
    EditingMoment,
    MomentScores,
    QualityScores,
    SceneAnalysis,
    TranscriptSegment,
    MediaAnalysis,
    TechnicalMetadata,
)
from rushframe_intelligence.moment_builder import build_editing_moments
from rushframe_intelligence.serialization import load_analysis


def test_build_moments_aligns_visual_speech_and_audio() -> None:
    scenes = [
        SceneAnalysis(
            scene_id="scene_0001",
            start=0,
            end=4,
            description="Presenter reveals a finished product",
            actions=["shows product"],
            mood="excited",
            visual_energy=0.8,
            quality=QualityScores(visual_quality=0.9),
        )
    ]
    transcript = [
        TranscriptSegment(
            segment_id="transcript_0001",
            start=0.5,
            end=3.5,
            text="This is the best result I have tested.",
            confidence=0.95,
        )
    ]
    audio = AudioAnalysis(events=[
        AudioEvent("audio_0001", 2.5, 4, "music", label="music rise", confidence=0.9)
    ])

    moments = build_editing_moments(scenes, transcript, audio)

    assert len(moments) == 1
    assert "hook" in moments[0].editing_roles
    assert "proof" in moments[0].editing_roles
    assert moments[0].speech == "This is the best result I have tested."
    assert moments[0].audio == "music rise"
    assert moments[0].scores.overall > 0.5


def test_duplicate_take_detector_recommends_best_take() -> None:
    moments = [
        EditingMoment(
            moment_id="moment_0001",
            start=0,
            end=4,
            summary="First take",
            speech="Here is the finished product and how it works",
            tags=["demonstration"],
            scores=MomentScores(overall=0.55),
            confidence=0.8,
        ),
        EditingMoment(
            moment_id="moment_0002",
            start=10,
            end=14,
            summary="Second take",
            speech="Here is the finished product and how it works",
            tags=["demonstration"],
            scores=MomentScores(overall=0.91),
            confidence=0.95,
        ),
    ]

    groups = find_duplicate_takes(moments)

    assert len(groups) == 1
    recommended = [candidate for candidate in groups[0].candidates if candidate.recommended]
    assert [candidate.moment_id for candidate in recommended] == ["moment_0002"]


def test_context_index_searches_roles_and_text(tmp_path: Path) -> None:
    moments = [
        EditingMoment(
            moment_id="moment_hook",
            start=0,
            end=3,
            summary="Surprising product reveal",
            speech="You will not expect this result",
            editing_roles=["hook"],
            tags=["product", "reveal"],
            scores=MomentScores(overall=0.9),
        ),
        EditingMoment(
            moment_id="moment_context",
            start=4,
            end=10,
            summary="Background explanation",
            speech="This explains the setup",
            editing_roles=["context"],
            tags=["setup"],
            scores=MomentScores(overall=0.5),
        ),
    ]
    index = MediaContextIndex(tmp_path / "context.sqlite")
    index.rebuild(moments, build_embeddings=True)

    results = index.search("product reveal", roles=["hook"], semantic=True)

    assert results
    assert results[0].moment_id == "moment_hook"


def test_agent_context_is_bounded_and_role_filtered() -> None:
    analysis = MediaAnalysis(
        source_path="video.mp4",
        metadata=TechnicalMetadata(duration=20, has_video=True, has_audio=True),
        moments=[
            EditingMoment(
                moment_id="hook",
                start=0,
                end=3,
                summary="Strong opening",
                editing_roles=["hook"],
                tags=["opening"],
                scores=MomentScores(overall=0.9, hook_potential=0.95),
            ),
            EditingMoment(
                moment_id="context",
                start=4,
                end=10,
                summary="Long explanation",
                editing_roles=["context"],
                scores=MomentScores(overall=0.6),
            ),
        ],
    )

    bundle = build_agent_context(analysis, roles=["hook"], limit=1)

    assert bundle["summary"]["editing_moment_count"] == 2
    assert [moment["moment_id"] for moment in bundle["moments"]] == ["hook"]


def test_v1_analysis_is_backward_compatible(tmp_path: Path) -> None:
    analysis_path = tmp_path / "media-analysis.json"
    analysis_path.write_text(
        json.dumps(
            {
                "source_path": "video.mp4",
                "schema_version": "1.0",
                "scenes": [
                    {
                        "scene_id": "scene_0001",
                        "start": 0,
                        "end": 2,
                        "description": "A scene",
                        "tags": ["test"],
                        "visual_energy": 0.5,
                    }
                ],
                "transcript": [
                    {"start": 0, "end": 2, "text": "Hello world", "words": []}
                ],
                "music": {"tempo_bpm": 120, "beat_times": [0.5, 1.0]},
                "warnings": [],
            }
        ),
        encoding="utf-8",
    )

    analysis = load_analysis(analysis_path)

    assert analysis.schema_version == "1.0"
    assert analysis.scenes[0].scene_id == "scene_0001"
    assert analysis.transcript[0].segment_id == "transcript_0001"
    assert analysis.audio.music is not None
    assert analysis.audio.music.tempo_bpm == 120
