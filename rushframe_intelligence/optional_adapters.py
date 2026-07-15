"""Optional heavyweight adapters. Imports occur only when a feature is enabled."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
from contextlib import contextmanager
from pathlib import Path
from typing import Any

from rushframe_intelligence.models import AudioEvent, SceneAnalysis, TranscriptSegment, WordTiming
from rushframe_intelligence.ffmpeg_tools import resolve_tool_path


class OptionalFeatureUnavailable(RuntimeError):
    pass


def apply_ocr(scenes: list[SceneAnalysis]) -> list[str]:
    """Read visible text from representative frames with optional PaddleOCR."""
    try:
        from paddleocr import PaddleOCR
    except ImportError as exc:
        raise OptionalFeatureUnavailable("paddleocr is required for OCR analysis.") from exc

    ocr = PaddleOCR(
        use_doc_orientation_classify=False,
        use_doc_unwarping=False,
        use_textline_orientation=False,
        enable_mkldnn=False,
    )
    warnings: list[str] = []
    for scene in scenes:
        path = scene.frame_path
        if not path:
            continue
        try:
            result = ocr.predict(str(path))
            text: list[str] = []
            for entry in result or []:
                payload = getattr(entry, "json", None)
                if callable(payload):
                    payload = payload()
                if isinstance(payload, dict):
                    values = payload.get("res", {}).get("rec_texts", [])
                    text.extend(str(value).strip() for value in values if str(value).strip())
            scene.visible_text = list(dict.fromkeys([*scene.visible_text, *text]))
        except Exception as exc:
            warnings.append(f"OCR failed for {scene.scene_id}: {exc}")
    return warnings


def apply_forced_alignment(
    media_path: Path | str,
    transcript: list[TranscriptSegment],
    *,
    language: str | None = None,
    device: str = "cpu",
) -> None:
    """Refine faster-whisper segment/word timings with optional WhisperX alignment."""
    if not transcript:
        return
    try:
        import whisperx
    except ImportError as exc:
        raise OptionalFeatureUnavailable("whisperx is required for precise word alignment.") from exc

    detected_language = language or next(
        (segment.language for segment in transcript if segment.language),
        None,
    )
    if not detected_language:
        raise OptionalFeatureUnavailable("WhisperX alignment requires a detected or selected language.")

    with _bundled_ffmpeg_on_path():
        audio = whisperx.load_audio(str(media_path))
    model, metadata = whisperx.load_align_model(
        language_code=detected_language,
        device=device,
    )
    source_segments = [
        {"start": segment.start, "end": segment.end, "text": segment.text}
        for segment in transcript
    ]
    aligned = whisperx.align(
        source_segments,
        model,
        metadata,
        audio,
        device,
        return_char_alignments=False,
    )
    aligned_segments = aligned.get("segments") or []
    for original, refined in zip(transcript, aligned_segments):
        refined_start = refined.get("start")
        refined_end = refined.get("end")
        if refined_start is not None:
            original.start = float(refined_start)
        if refined_end is not None:
            original.end = float(refined_end)
        words: list[WordTiming] = []
        for raw_word in refined.get("words") or []:
            text = str(raw_word.get("word") or raw_word.get("text") or "").strip()
            start = raw_word.get("start")
            end = raw_word.get("end")
            if not text or start is None or end is None:
                continue
            words.append(
                WordTiming(
                    start=float(start),
                    end=float(end),
                    text=text,
                    confidence=float(raw_word["score"]) if raw_word.get("score") is not None else None,
                )
            )
        if words:
            original.words = words
            confidences = [word.confidence for word in words if word.confidence is not None]
            if confidences:
                original.confidence = sum(confidences) / len(confidences)


def apply_speaker_diarization(
    media_path: Path | str,
    transcript: list[TranscriptSegment],
    *,
    auth_token: str | None = None,
    model_name: str = "pyannote/speaker-diarization-community-1",
) -> list[AudioEvent]:
    """Assign speaker labels and return speech-region events."""
    try:
        from pyannote.audio import Pipeline
    except ImportError as exc:
        raise OptionalFeatureUnavailable("pyannote.audio is required for speaker diarization.") from exc

    with _pyannote_hub_auth_compatibility():
        pipeline = Pipeline.from_pretrained(model_name, use_auth_token=auth_token)
    if pipeline is None:
        raise OptionalFeatureUnavailable(
            "Speaker detection requires HF_TOKEN and accepted access to the pyannote diarization model."
        )
    output = pipeline(str(media_path))
    annotation = getattr(output, "speaker_diarization", output)
    regions: list[tuple[float, float, str]] = []
    for turn, _, speaker in annotation.itertracks(yield_label=True):
        regions.append((float(turn.start), float(turn.end), str(speaker)))

    events = [
        AudioEvent(
            event_id=f"speech_{index:04d}",
            start=start,
            end=end,
            event_type="speech",
            label="speech",
            speaker=speaker,
        )
        for index, (start, end, speaker) in enumerate(regions, start=1)
    ]
    for segment in transcript:
        overlaps = [
            (min(segment.end, end) - max(segment.start, start), speaker)
            for start, end, speaker in regions
            if min(segment.end, end) > max(segment.start, start)
        ]
        if overlaps:
            segment.speaker = max(overlaps, key=lambda item: item[0])[1]
    return events


def detect_semantic_audio_events(
    audio_path: Path | str,
    *,
    labels: list[str] | None = None,
    confidence_threshold: float = 0.35,
) -> list[AudioEvent]:
    """Classify a whole source with optional LAION-CLAP.

    The adapter intentionally emits one source-level event per strong label. A future
    windowed implementation can reuse the same output model without changing callers.
    """
    labels = labels or [
        "speech", "music", "laughter", "applause", "crowd", "typing",
        "door slam", "impact", "car engine", "wind", "background noise",
    ]
    try:
        original_argv = sys.argv
        try:
            sys.argv = original_argv[:1]
            import laion_clap
        finally:
            sys.argv = original_argv
        import numpy as np
        import torch
    except ImportError as exc:
        raise OptionalFeatureUnavailable("laion-clap and numpy are required for audio-event analysis.") from exc

    checkpoint = os.environ.get("RUSHFRAME_CLAP_CHECKPOINT", "").strip()
    if not checkpoint:
        local_app_data = os.environ.get("LOCALAPPDATA", "").strip()
        if local_app_data:
            checkpoint = str(
                Path(local_app_data)
                / "Rushframe"
                / "Models"
                / "clap"
                / "music_audioset_epoch_15_esc_90.14.pt"
            )
    if not checkpoint or not Path(checkpoint).is_file():
        raise OptionalFeatureUnavailable(
            "RUSHFRAME_CLAP_CHECKPOINT must point to an installed local CLAP checkpoint."
        )
    model = laion_clap.CLAP_Module(enable_fusion=False, amodel="HTSAT-base")
    with torch.serialization.safe_globals(
        [
            (np.core.multiarray.scalar, "numpy.core.multiarray.scalar"),
            np.dtype,
            np.dtypes.Float32DType,
            np.dtypes.Float64DType,
        ]
    ):
        from laion_clap.hook import load_state_dict

        state = load_state_dict(checkpoint, skip_params=True)
    state.pop("text_branch.embeddings.position_ids", None)
    model.model.load_state_dict(state)
    text_embeddings = model.get_text_embedding(labels)
    with _clap_audio_input(audio_path) as clap_audio_path:
        audio_embeddings = model.get_audio_embedding_from_filelist([str(clap_audio_path)])
    audio_vector = audio_embeddings[0]
    scores = np.matmul(text_embeddings, audio_vector)
    logits = (scores - np.max(scores)) * 100.0
    probabilities = np.exp(logits) / np.exp(logits).sum()
    events: list[AudioEvent] = []
    for index, (label, raw_score) in enumerate(zip(labels, probabilities), start=1):
        score = float(raw_score)
        if score < confidence_threshold:
            continue
        events.append(
            AudioEvent(
                event_id=f"semantic_audio_{index:04d}",
                start=0.0,
                end=0.001,
                event_type="semantic_audio",
                label=label,
                confidence=score,
            )
        )
    return events


@contextmanager
def _bundled_ffmpeg_on_path():
    """WhisperX shells out to ffmpeg without accepting Rushframe's path argument."""
    executable = resolve_tool_path("ffmpeg")
    directory = Path(executable).parent if Path(executable).is_file() else None
    if directory is None:
        yield
        return

    original = os.environ.get("PATH", "")
    if str(directory) not in original.split(os.pathsep):
        os.environ["PATH"] = str(directory) + os.pathsep + original
    try:
        yield
    finally:
        os.environ["PATH"] = original


@contextmanager
def _clap_audio_input(media_path: Path | str):
    source = Path(media_path)
    if source.suffix.lower() in {".wav", ".flac", ".ogg"}:
        yield source
        return

    executable = resolve_tool_path("ffmpeg")
    if not Path(executable).is_file():
        raise OptionalFeatureUnavailable("FFmpeg is required to prepare audio for CLAP analysis.")
    with tempfile.TemporaryDirectory(prefix="rushframe-clap-") as directory:
        output = Path(directory) / "audio.wav"
        completed = subprocess.run(
            [
                str(executable), "-v", "error", "-y", "-i", str(source),
                "-vn", "-ac", "1", "-ar", "48000", str(output),
            ],
            capture_output=True,
            text=True,
            check=False,
        )
        if completed.returncode != 0 or not output.is_file():
            detail = completed.stderr.strip()[:300]
            raise OptionalFeatureUnavailable(f"FFmpeg could not prepare CLAP audio: {detail}")
        yield output


@contextmanager
def _pyannote_hub_auth_compatibility():
    """Bridge pyannote 3.x auth arguments to current huggingface_hub."""
    import pyannote.audio.core.model as model_module
    import pyannote.audio.core.pipeline as pipeline_module
    import pyannote.audio.pipelines.speaker_verification as verification_module

    modules = (pipeline_module, model_module, verification_module)
    originals = [module.hf_hub_download for module in modules]
    for module, download in zip(modules, originals):
        def compatible_download(*args: Any, _download=download, **kwargs: Any):
            token = kwargs.pop("use_auth_token", None)
            if token is not None:
                kwargs["token"] = token
            return _download(*args, **kwargs)

        module.hf_hub_download = compatible_download
    try:
        yield
    finally:
        for module, download in zip(modules, originals):
            module.hf_hub_download = download
