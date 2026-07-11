"""Optional heavyweight adapters. Imports occur only when a feature is enabled."""

from __future__ import annotations

import math
from pathlib import Path
from typing import Any

from rushframe_intelligence.models import AudioEvent, SceneAnalysis, TranscriptSegment, WordTiming


class OptionalFeatureUnavailable(RuntimeError):
    pass


def apply_ocr(scenes: list[SceneAnalysis]) -> list[str]:
    """Read visible text from representative frames with optional PaddleOCR."""
    try:
        from paddleocr import PaddleOCR
    except ImportError as exc:
        raise OptionalFeatureUnavailable("paddleocr is required for OCR analysis.") from exc

    ocr = PaddleOCR(use_doc_orientation_classify=False, use_doc_unwarping=False)
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

    kwargs: dict[str, Any] = {}
    if auth_token:
        kwargs["token"] = auth_token
    pipeline = Pipeline.from_pretrained(model_name, **kwargs)
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
        import laion_clap
        import numpy as np
    except ImportError as exc:
        raise OptionalFeatureUnavailable("laion-clap and numpy are required for audio-event analysis.") from exc

    model = laion_clap.CLAP_Module(enable_fusion=False)
    model.load_ckpt()
    text_embeddings = model.get_text_embedding(labels, use_tensor=False)
    audio_embeddings = model.get_audio_embedding_from_filelist(
        x=[str(audio_path)],
        use_tensor=False,
    )
    audio_vector = audio_embeddings[0]
    scores = np.matmul(text_embeddings, audio_vector)
    events: list[AudioEvent] = []
    for index, (label, raw_score) in enumerate(zip(labels, scores), start=1):
        score = 1.0 / (1.0 + math.exp(-float(raw_score)))
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
