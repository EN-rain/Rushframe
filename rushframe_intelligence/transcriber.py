"""CPU-oriented speech transcription using optional faster-whisper."""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from threading import Lock
from typing import Any

from rushframe_intelligence.models import TranscriptSegment, WordTiming


class TranscriptionUnavailable(RuntimeError):
    pass


class _CachedWhisperModel:
    def __init__(self, model: Any) -> None:
        self.model = model
        self.lock = Lock()


@lru_cache(maxsize=4)
def _get_whisper_model(model_size: str, device: str, compute_type: str) -> _CachedWhisperModel:
    from faster_whisper import WhisperModel

    return _CachedWhisperModel(
        WhisperModel(model_size, device=device, compute_type=compute_type)
    )


_FILLER_WORDS = {
    "um", "uh", "erm", "hmm", "like", "actually", "basically", "literally",
}


def transcribe(
    media_path: Path | str,
    model_size: str = "small",
    language: str | None = None,
    compute_type: str = "int8",
    device: str = "cpu",
) -> list[TranscriptSegment]:
    try:
        from faster_whisper import WhisperModel
    except ImportError as exc:
        raise TranscriptionUnavailable(
            "faster-whisper is not installed. Install Rushframe's intelligence dependencies."
        ) from exc

    cached_model = _get_whisper_model(model_size, device, compute_type)
    with cached_model.lock:
        segments, info = cached_model.model.transcribe(
            str(Path(media_path)),
            language=language,
            word_timestamps=True,
            vad_filter=True,
            condition_on_previous_text=False,
        )
        segments = list(segments)
    detected_language = getattr(info, "language", None) or language
    output: list[TranscriptSegment] = []
    for index, segment in enumerate(segments, start=1):
        words = [
            WordTiming(
                start=float(word.start if word.start is not None else segment.start),
                end=float(word.end if word.end is not None else segment.end),
                text=str(word.word).strip(),
                confidence=float(word.probability) if getattr(word, "probability", None) is not None else None,
            )
            for word in (segment.words or [])
            if str(word.word).strip()
        ]
        text = segment.text.strip()
        normalized_words = {token.strip(".,!?;:\"'()[]{}").lower() for token in text.split()}
        average_confidence = None
        confidences = [word.confidence for word in words if word.confidence is not None]
        if confidences:
            average_confidence = sum(confidences) / len(confidences)
        output.append(
            TranscriptSegment(
                segment_id=f"transcript_{index:04d}",
                start=float(segment.start),
                end=float(segment.end),
                text=text,
                words=words,
                confidence=average_confidence,
                language=detected_language,
                contains_filler=bool(normalized_words & _FILLER_WORDS),
            )
        )
    return output
