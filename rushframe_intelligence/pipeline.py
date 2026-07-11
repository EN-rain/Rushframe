"""Orchestrates local media analysis and builds agent-ready editing context."""

from __future__ import annotations

import json
import os
from dataclasses import asdict
from pathlib import Path

from rushframe_intelligence.audio_analyzer import analyze_audio_metrics
from rushframe_intelligence.cache import create_manifest, is_cache_valid, load_manifest, source_checksum
from rushframe_intelligence.context_index import MediaContextIndex
from rushframe_intelligence.duplicate_detector import find_duplicate_takes
from rushframe_intelligence.ffmpeg_tools import resolve_tool_path, run_tool
from rushframe_intelligence.frame_sampler import extract_scene_frames
from rushframe_intelligence.models import AudioAnalysis, MediaAnalysis, SceneAnalysis
from rushframe_intelligence.moment_builder import build_editing_moments
from rushframe_intelligence.music_analyzer import analyze_music
from rushframe_intelligence.optional_adapters import (
    OptionalFeatureUnavailable,
    apply_forced_alignment,
    apply_ocr,
    apply_speaker_diarization,
    detect_semantic_audio_events,
)
from rushframe_intelligence.probe import probe_media
from rushframe_intelligence.scene_detector import detect_scenes
from rushframe_intelligence.serialization import load_analysis
from rushframe_intelligence.transcriber import transcribe
from rushframe_intelligence.visual_analyzer import (
    GeminiFrameProvider,
    QwenLocalProvider,
    VisualProvider,
    apply_visual_result,
)
from rushframe_intelligence.visual_quality import score_scenes


class MediaIntelligencePipeline:
    def __init__(
        self,
        ffmpeg_path: Path | str | None = None,
        ffprobe_path: Path | str | None = None,
    ) -> None:
        self.ffmpeg_path = resolve_tool_path("ffmpeg", ffmpeg_path)
        self.ffprobe_path = resolve_tool_path("ffprobe", ffprobe_path)

    def run(
        self,
        media_path: Path | str,
        output_dir: Path | str,
        *,
        detect_visual_scenes: bool = True,
        transcribe_speech: bool = True,
        analyze_audio: bool = True,
        understand_frames: bool = False,
        visual_provider: str = "gemini",
        whisper_model: str = "small",
        language: str | None = None,
        max_input_seconds: float = 900.0,
        enable_ocr: bool = False,
        enable_alignment: bool = False,
        enable_diarization: bool = False,
        enable_audio_events: bool = False,
        enable_embeddings: bool = False,
        force: bool = False,
    ) -> MediaAnalysis:
        source = Path(media_path).resolve()
        if not source.is_file():
            raise FileNotFoundError(f"Media file does not exist: {source}")
        destination = Path(output_dir).resolve()
        destination.mkdir(parents=True, exist_ok=True)

        max_input_seconds = max(30.0, min(float(max_input_seconds), 1800.0))
        enabled_features = ["probe", "moments", "duplicates", "context_index", f"max_input:{max_input_seconds:.3f}"]
        if detect_visual_scenes:
            enabled_features.extend(["scenes", "frames", "visual_quality"])
        if transcribe_speech:
            enabled_features.append("transcript")
        if analyze_audio:
            enabled_features.extend(["audio_metrics", "music"])
        if understand_frames:
            enabled_features.append(f"visual_understanding:{visual_provider}")
        if enable_ocr:
            enabled_features.append("ocr")
        if enable_alignment:
            enabled_features.append("word_alignment")
        if enable_diarization:
            enabled_features.append("diarization")
        if enable_audio_events:
            enabled_features.append("semantic_audio_events")
        if enable_embeddings:
            enabled_features.append("embeddings")

        checksum = source_checksum(source)
        manifest_path = destination / "manifest.json"
        analysis_path = destination / "media-analysis.json"
        existing_manifest = load_manifest(manifest_path)
        if (
            not force
            and analysis_path.is_file()
            and is_cache_valid(existing_manifest, source, checksum, enabled_features)
        ):
            return load_analysis(analysis_path)

        result = MediaAnalysis(source_path=str(source), source_checksum=checksum)
        analysis_source = source
        try:
            result.metadata = probe_media(source, self.ffprobe_path, self.ffmpeg_path)
        except Exception as exc:
            result.warnings.append(f"technical probe failed: {exc}")

        if result.metadata.duration > max_input_seconds:
            clipped_source = destination / "analysis-input.mkv"
            process = run_tool(
                self.ffmpeg_path,
                [
                    "-y", "-i", source, "-t", f"{max_input_seconds:.3f}",
                    "-map", "0:v?", "-map", "0:a?", "-c", "copy",
                    "-avoid_negative_ts", "make_zero", clipped_source,
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            if process.returncode == 0 and clipped_source.is_file():
                analysis_source = clipped_source
                result.metadata.duration = max_input_seconds
                result.warnings.append(
                    f"source limited to the first {max_input_seconds:.0f} seconds for AI analysis"
                )
            else:
                result.warnings.append(
                    "AI input limit could not be applied; the full source may be analyzed"
                )

        if detect_visual_scenes and result.metadata.has_video:
            try:
                result.scenes = detect_scenes(analysis_source)
            except Exception as exc:
                result.warnings.append(f"scene detection skipped: {exc}")
        if result.metadata.has_video and not result.scenes and result.metadata.duration > 0:
            result.scenes = [
                SceneAnalysis(
                    scene_id="scene_0001",
                    start=0.0,
                    end=result.metadata.duration,
                    summary="Full source clip",
                    tags=["fallback_scene"],
                )
            ]

        if transcribe_speech and result.metadata.has_audio:
            try:
                result.transcript = transcribe(
                    analysis_source,
                    model_size=whisper_model,
                    language=language,
                    device=os.getenv("RUSHFRAME_WHISPER_DEVICE", "cpu"),
                    compute_type=os.getenv("RUSHFRAME_WHISPER_COMPUTE_TYPE", "int8"),
                )
            except Exception as exc:
                result.warnings.append(f"transcription skipped: {exc}")

        if analyze_audio and result.metadata.has_audio:
            try:
                result.audio = analyze_audio_metrics(
                    analysis_source,
                    ffmpeg_path=self.ffmpeg_path,
                    media_duration=result.metadata.duration,
                )
            except Exception as exc:
                result.warnings.append(f"audio metrics skipped: {exc}")
                result.audio = AudioAnalysis()
            try:
                result.audio.music = analyze_music(analysis_source, ffmpeg_path=self.ffmpeg_path)
            except Exception as exc:
                result.warnings.append(f"music analysis skipped: {exc}")

        should_sample_frames = bool(result.scenes) and (
            detect_visual_scenes or understand_frames or enable_ocr
        )
        if should_sample_frames:
            result.warnings.extend(
                extract_scene_frames(
                    analysis_source,
                    result.scenes,
                    destination / "frames",
                    ffmpeg_path=self.ffmpeg_path,
                )
            )
            result.warnings.extend(score_scenes(result.scenes))

        if enable_alignment and result.transcript:
            try:
                apply_forced_alignment(
                    analysis_source,
                    result.transcript,
                    language=language,
                    device=os.getenv("RUSHFRAME_WHISPERX_DEVICE", "cpu"),
                )
            except OptionalFeatureUnavailable as exc:
                result.warnings.append(f"word alignment skipped: {exc}")
            except Exception as exc:
                result.warnings.append(f"word alignment failed: {exc}")

        if enable_ocr and result.scenes:
            try:
                result.warnings.extend(apply_ocr(result.scenes))
            except OptionalFeatureUnavailable as exc:
                result.warnings.append(f"OCR skipped: {exc}")
            except Exception as exc:
                result.warnings.append(f"OCR failed: {exc}")

        if enable_diarization and result.metadata.has_audio:
            try:
                speech_events = apply_speaker_diarization(
                    analysis_source,
                    result.transcript,
                    auth_token=os.getenv("HF_TOKEN") or os.getenv("HUGGINGFACE_TOKEN"),
                )
                result.audio.events.extend(speech_events)
            except OptionalFeatureUnavailable as exc:
                result.warnings.append(f"diarization skipped: {exc}")
            except Exception as exc:
                result.warnings.append(f"diarization failed: {exc}")

        if enable_audio_events and result.metadata.has_audio:
            try:
                semantic_events = detect_semantic_audio_events(analysis_source)
                for event in semantic_events:
                    event.end = max(0.001, result.metadata.duration)
                result.audio.events.extend(semantic_events)
            except OptionalFeatureUnavailable as exc:
                result.warnings.append(f"semantic audio analysis skipped: {exc}")
            except Exception as exc:
                result.warnings.append(f"semantic audio analysis failed: {exc}")

        if understand_frames and result.scenes:
            provider = self._create_visual_provider(visual_provider)
            for scene in result.scenes:
                frame_paths = [Path(path) for path in scene.frame_paths if Path(path).is_file()]
                if not frame_paths:
                    continue
                transcript_text = " ".join(
                    segment.text for segment in result.transcript
                    if min(scene.end, segment.end) > max(scene.start, segment.start)
                )
                try:
                    apply_visual_result(scene, provider.analyze(frame_paths, transcript_text))
                except Exception as exc:
                    result.warnings.append(f"visual analysis failed for {scene.scene_id}: {exc}")

        result.moments = build_editing_moments(result.scenes, result.transcript, result.audio)
        result.duplicate_take_groups = find_duplicate_takes(result.moments)
        duplicate_ids = {
            candidate.moment_id
            for group in result.duplicate_take_groups
            for candidate in group.candidates
        }
        transcript_by_id = {segment.segment_id: segment for segment in result.transcript}
        for moment in result.moments:
            if moment.moment_id in duplicate_ids:
                for segment_id in moment.transcript_segment_ids:
                    if segment_id in transcript_by_id:
                        transcript_by_id[segment_id].repeated_take = True

        manifest = create_manifest(source, checksum, enabled_features)
        result.manifest = manifest
        self._write_outputs(destination, result, enable_embeddings=enable_embeddings)
        return result

    @staticmethod
    def _create_visual_provider(name: str) -> VisualProvider:
        normalized = name.strip().lower()
        if normalized in {"qwen", "qwen-local", "local"}:
            return QwenLocalProvider(os.getenv("RUSHFRAME_QWEN_MODEL", "Qwen/Qwen2.5-VL-3B-Instruct"))
        if normalized in {"gemini", "cloud"}:
            return GeminiFrameProvider()
        raise ValueError(f"Unknown visual provider: {name}")

    @staticmethod
    def _write_outputs(destination: Path, result: MediaAnalysis, *, enable_embeddings: bool) -> None:
        payload = result.to_dict()
        (destination / "media-analysis.json").write_text(
            json.dumps(payload, ensure_ascii=False, indent=2),
            encoding="utf-8",
        )
        (destination / "summary.json").write_text(
            json.dumps(
                {
                    "schema_version": result.schema_version,
                    "analysis_version": result.analysis_version,
                    "source_path": result.source_path,
                    "source_checksum": result.source_checksum,
                    "metadata": asdict(result.metadata),
                    "scene_count": len(result.scenes),
                    "transcript_segment_count": len(result.transcript),
                    "moment_count": len(result.moments),
                    "duplicate_take_group_count": len(result.duplicate_take_groups),
                    "best_hooks": [
                        moment.moment_id
                        for moment in sorted(
                            result.moments,
                            key=lambda item: item.scores.hook_potential,
                            reverse=True,
                        )[:5]
                        if moment.scores.hook_potential > 0.4
                    ],
                    "warnings": result.warnings,
                },
                ensure_ascii=False,
                indent=2,
            ),
            encoding="utf-8",
        )
        for filename, value in (
            ("scenes.json", [asdict(item) for item in result.scenes]),
            ("transcript.json", [asdict(item) for item in result.transcript]),
            ("audio-events.json", [asdict(item) for item in result.audio.events]),
            ("moments.json", [asdict(item) for item in result.moments]),
            ("duplicate-takes.json", [asdict(item) for item in result.duplicate_take_groups]),
            ("manifest.json", asdict(result.manifest) if result.manifest else {}),
        ):
            (destination / filename).write_text(
                json.dumps(value, ensure_ascii=False, indent=2),
                encoding="utf-8",
            )

        index = MediaContextIndex(destination / "context.sqlite")
        try:
            index.rebuild(result.moments, build_embeddings=enable_embeddings)
        except RuntimeError as exc:
            result.warnings.append(f"embedding index skipped: {exc}")
            index.rebuild(result.moments, build_embeddings=False)
            (destination / "media-analysis.json").write_text(
                json.dumps(result.to_dict(), ensure_ascii=False, indent=2),
                encoding="utf-8",
            )
