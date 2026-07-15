from __future__ import annotations

import json
import sys
import threading
from pathlib import Path
from types import ModuleType
from urllib.error import HTTPError
from urllib.request import Request, urlopen

import pytest

from rushframe_intelligence import backend
from rushframe_intelligence.agent_context import build_agent_context
from rushframe_intelligence.context_index import MediaContextIndex
from rushframe_intelligence.duplicate_detector import find_duplicate_takes
from rushframe_intelligence.pipeline import MediaIntelligencePipeline
from rushframe_intelligence.models import (
    AnalysisManifest,
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
from rushframe_intelligence.scene_detector import detect_scenes
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


def test_backend_tool_schemas_use_registered_media_ids_instead_of_local_paths() -> None:
    tools = {tool["name"]: tool for tool in backend.TOOLS}
    search_properties = tools["rushframe.search_moments"]["inputSchema"]["properties"]
    context_properties = tools["rushframe.get_agent_context"]["inputSchema"]["properties"]
    editing_context_properties = tools["rushframe.get_editing_context"]["inputSchema"]["properties"]

    assert "media_asset_id" in search_properties
    assert "index" not in search_properties
    assert "media_asset_id" in context_properties
    assert "analysis" not in context_properties
    assert "media_asset_id" in editing_context_properties
    assert "path" not in editing_context_properties
    edit_action = tools["rushframe.apply_timeline_edit"]["inputSchema"]["properties"]["action"]
    assert "enum" not in edit_action
    assert "rushframe.capabilities" in edit_action["description"]
    for name in ("rushframe.preview_edit_plan", "rushframe.review_edit_plan", "rushframe.apply_edit_plan"):
        schema = tools[name]["inputSchema"]
        assert schema["required"] == ["base_revision", "operations"]
        assert schema["properties"]["operations"]["maxItems"] == 100


@pytest.mark.parametrize(
    ("tool_name", "bridge_endpoint"),
    [
        ("rushframe.get_editing_context", "editing-context"),
        ("rushframe.preview_edit_plan", "plan"),
        ("rushframe.review_edit_plan", "review-plan"),
        ("rushframe.apply_edit_plan", "apply-plan"),
    ],
)
def test_editing_context_and_plan_tools_forward_to_controlled_editor_routes(monkeypatch, tool_name: str, bridge_endpoint: str) -> None:
    calls: list[tuple[dict, str]] = []
    monkeypatch.setattr(backend, "_bridge_post", lambda arguments, endpoint: calls.append((arguments, endpoint)) or {"ok": True})

    result = backend._call_tool(tool_name, {"base_revision": 0, "operations": [{"action": "add_text"}]})

    assert result == {"ok": True}
    assert calls == [({"base_revision": 0, "operations": [{"action": "add_text"}]}, bridge_endpoint)]


def test_capabilities_returns_intelligence_and_editor_availability(monkeypatch) -> None:
    monkeypatch.setattr(backend, "_bridge_get", lambda *_args, **_kwargs: {"ok": True, "editPlan": {"actions": ["add_text"]}})
    result = backend._call_tool("rushframe.capabilities", {})

    assert "intelligence" in result
    assert result["editor"]["ok"] is True
    assert result["editor"]["editPlan"]["actions"] == ["add_text"]


def test_backend_rejects_non_loopback_bind() -> None:
    with pytest.raises(ValueError, match="loopback"):
        backend.serve("0.0.0.0", 0)


def test_bridge_url_rejects_other_loopback_ports_and_url_components() -> None:
    with pytest.raises(ValueError, match="configured Rushframe port"):
        backend._bridge_url({"bridge_url": "http://127.0.0.1:9999"}, "timeline")
    with pytest.raises(ValueError, match="must not contain"):
        backend._bridge_url({"bridge_url": "http://localhost:7320/other?token=1"}, "timeline")
    assert backend._bridge_url({"bridge_url": "http://localhost:7320"}, "timeline") == "http://127.0.0.1:7320/timeline"


def test_backend_requires_session_for_every_non_health_get(monkeypatch) -> None:
    monkeypatch.setattr(backend, "EDITOR_SESSION_TOKEN", "test-secret")
    server = backend.BoundedThreadingHTTPServer(("127.0.0.1", 0), backend.RushframeBackendHandler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    base = f"http://127.0.0.1:{server.server_address[1]}"
    try:
        with pytest.raises(HTTPError) as unauthorized:
            urlopen(f"{base}/capabilities", timeout=2)
        assert unauthorized.value.code == 401

        request = Request(f"{base}/capabilities", headers={"X-Rushframe-Session": "test-secret"})
        with urlopen(request, timeout=2) as response:
            assert response.status == 200

        with urlopen(f"{base}/health", timeout=2) as response:
            assert response.status == 200
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=2)


def test_pipeline_bundle_publish_replaces_complete_directory(tmp_path: Path) -> None:
    destination = tmp_path / "analysis"
    destination.mkdir()
    (destination / "old.txt").write_text("old", encoding="utf-8")
    staging = tmp_path / ".analysis.staging-test"
    staging.mkdir()
    (staging / "media-analysis.json").write_text("new", encoding="utf-8")
    (staging / "manifest.json").write_text("complete", encoding="utf-8")

    MediaIntelligencePipeline._publish_output_bundle(staging, destination)

    assert not staging.exists()
    assert not (destination / "old.txt").exists()
    assert (destination / "media-analysis.json").read_text(encoding="utf-8") == "new"
    assert (destination / "manifest.json").read_text(encoding="utf-8") == "complete"


def test_pipeline_publish_failure_restores_previous_bundle(tmp_path: Path, monkeypatch) -> None:
    destination = tmp_path / "analysis"
    destination.mkdir()
    (destination / "old.txt").write_text("old", encoding="utf-8")
    staging = tmp_path / ".analysis.staging-test"
    staging.mkdir()
    (staging / "new.txt").write_text("new", encoding="utf-8")
    original_replace = backend.os.replace if hasattr(backend, "os") else None
    import rushframe_intelligence.pipeline as pipeline_module
    original_replace = pipeline_module.os.replace
    calls = 0

    def fail_second_replace(source, target):
        nonlocal calls
        calls += 1
        if calls == 2:
            raise OSError("publication failed")
        return original_replace(source, target)

    monkeypatch.setattr(pipeline_module.os, "replace", fail_second_replace)
    with pytest.raises(OSError, match="publication failed"):
        MediaIntelligencePipeline._publish_output_bundle(staging, destination)

    assert (destination / "old.txt").read_text(encoding="utf-8") == "old"
    assert staging.exists()


def test_pipeline_writes_manifest_only_after_index_is_complete(tmp_path: Path, monkeypatch) -> None:
    result = MediaAnalysis(source_path="video.mp4")

    def fail_index(*_args, **_kwargs):
        raise ValueError("index failed")

    monkeypatch.setattr(MediaContextIndex, "rebuild", fail_index)
    with pytest.raises(ValueError, match="index failed"):
        MediaIntelligencePipeline._write_outputs(tmp_path, result, enable_embeddings=False)

    assert (tmp_path / "media-analysis.json").exists()
    assert not (tmp_path / "manifest.json").exists()


def test_embedding_fallback_does_not_advertise_embeddings_as_complete(tmp_path: Path, monkeypatch) -> None:
    result = MediaAnalysis(
        source_path="video.mp4",
        manifest=AnalysisManifest(
            source_checksum="sha256:test",
            source_size=4,
            source_modified_utc="2026-07-15T00:00:00+00:00",
            enabled_features=["moments"],
        ),
    )
    calls: list[bool] = []

    def rebuild(_self, _moments, *, build_embeddings: bool) -> None:
        calls.append(build_embeddings)
        if build_embeddings:
            raise RuntimeError("embedding model unavailable")

    monkeypatch.setattr(MediaContextIndex, "rebuild", rebuild)

    MediaIntelligencePipeline._write_outputs(tmp_path, result, enable_embeddings=True)

    manifest = json.loads((tmp_path / "manifest.json").read_text(encoding="utf-8"))
    assert calls == [True, False]
    assert "context_index" in manifest["enabled_features"]
    assert "embeddings" not in manifest["enabled_features"]


def test_destination_lock_rejects_concurrent_analysis_and_recovers_after_close(tmp_path: Path) -> None:
    from rushframe_intelligence.pipeline import _AnalysisDestinationLock

    destination = tmp_path / "analysis"
    first = _AnalysisDestinationLock(destination)
    try:
        with pytest.raises(RuntimeError, match="already running"):
            _AnalysisDestinationLock(destination)
    finally:
        first.close()

    second = _AnalysisDestinationLock(destination)
    second.close()
    assert not (tmp_path / ".analysis.analysis.lock").exists()


def test_scene_detector_clamps_negative_first_frame_time(tmp_path: Path, monkeypatch) -> None:
    class FakeTime:
        def __init__(self, seconds: float) -> None:
            self._seconds = seconds

        def get_seconds(self) -> float:
            return self._seconds

    fake_module = ModuleType("scenedetect")
    fake_module.ContentDetector = lambda threshold: object()
    fake_module.detect = lambda *args, **kwargs: [
        (FakeTime(-0.033333), FakeTime(4.0)),
    ]
    monkeypatch.setitem(sys.modules, "scenedetect", fake_module)
    source = tmp_path / "video.mp4"
    source.write_bytes(b"test")

    scenes = detect_scenes(source)

    assert len(scenes) == 1
    assert scenes[0].start == 0.0
    assert scenes[0].end == 4.0


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
