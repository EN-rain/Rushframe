"""Versioned, serializable media-intelligence models."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Any

SCHEMA_VERSION = "2.0"
ANALYSIS_VERSION = 2


@dataclass(slots=True)
class TechnicalMetadata:
    duration: float = 0.0
    width: int | None = None
    height: int | None = None
    fps: float | None = None
    video_codec: str | None = None
    audio_codec: str | None = None
    audio_channels: int | None = None
    sample_rate: int | None = None
    bit_rate: int | None = None
    orientation: str | None = None
    variable_frame_rate: bool | None = None
    has_video: bool = False
    has_audio: bool = False


@dataclass(slots=True)
class QualityScores:
    visual_quality: float | None = None
    audio_clarity: float | None = None
    sharpness: float | None = None
    exposure: float | None = None
    stability: float | None = None
    face_visibility: float | None = None
    text_readability: float | None = None


@dataclass(slots=True)
class WordTiming:
    start: float
    end: float
    text: str
    confidence: float | None = None


@dataclass(slots=True)
class SceneAnalysis:
    scene_id: str
    start: float
    end: float
    frame_path: str | None = None
    frame_paths: list[str] = field(default_factory=list)
    description: str | None = None
    summary: str | None = None
    tags: list[str] = field(default_factory=list)
    subjects: list[str] = field(default_factory=list)
    actions: list[str] = field(default_factory=list)
    visible_text: list[str] = field(default_factory=list)
    location: str | None = None
    shot_type: str | None = None
    camera_motion: str | None = None
    mood: str | None = None
    visual_energy: float | None = None
    usable: bool = True
    confidence: float | None = None
    editing_roles: list[str] = field(default_factory=list)
    quality: QualityScores = field(default_factory=QualityScores)


@dataclass(slots=True)
class TranscriptSegment:
    segment_id: str
    start: float
    end: float
    text: str
    words: list[WordTiming] = field(default_factory=list)
    speaker: str | None = None
    confidence: float | None = None
    emotion: str | None = None
    language: str | None = None
    contains_filler: bool = False
    repeated_take: bool = False
    hook_score: float | None = None
    recommended_use: list[str] = field(default_factory=list)


@dataclass(slots=True)
class AudioEvent:
    event_id: str
    start: float
    end: float
    event_type: str
    label: str | None = None
    confidence: float | None = None
    speaker: str | None = None
    clarity: float | None = None
    attributes: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class SilenceRange:
    start: float
    end: float
    duration: float


@dataclass(slots=True)
class MusicAnalysis:
    tempo_bpm: float | None = None
    beat_times: list[float] = field(default_factory=list)
    onset_times: list[float] = field(default_factory=list)
    rms_times: list[float] = field(default_factory=list)
    rms_energy: list[float] = field(default_factory=list)
    key: str | None = None
    energy: float | None = None


@dataclass(slots=True)
class AudioAnalysis:
    integrated_loudness_lufs: float | None = None
    true_peak_db: float | None = None
    mean_volume_db: float | None = None
    max_volume_db: float | None = None
    clipping_detected: bool = False
    silence: list[SilenceRange] = field(default_factory=list)
    events: list[AudioEvent] = field(default_factory=list)
    music: MusicAnalysis | None = None


@dataclass(slots=True)
class MomentScores:
    importance: float = 0.0
    hook_potential: float = 0.0
    emotional_intensity: float = 0.0
    novelty: float = 0.0
    broll_usefulness: float = 0.0
    continuity: float = 0.0
    brand_relevance: float = 0.0
    overall: float = 0.0


@dataclass(slots=True)
class EditingMoment:
    moment_id: str
    start: float
    end: float
    summary: str
    scene_ids: list[str] = field(default_factory=list)
    transcript_segment_ids: list[str] = field(default_factory=list)
    audio_event_ids: list[str] = field(default_factory=list)
    visual: str | None = None
    speech: str | None = None
    audio: str | None = None
    editing_roles: list[str] = field(default_factory=list)
    tags: list[str] = field(default_factory=list)
    scores: MomentScores = field(default_factory=MomentScores)
    confidence: float = 0.0
    evidence: list[str] = field(default_factory=list)
    facts: dict[str, Any] = field(default_factory=dict)
    interpretation: dict[str, Any] = field(default_factory=dict)


@dataclass(slots=True)
class DuplicateTakeCandidate:
    moment_id: str
    score: float
    recommended: bool = False


@dataclass(slots=True)
class DuplicateTakeGroup:
    group_id: str
    purpose: str
    candidates: list[DuplicateTakeCandidate] = field(default_factory=list)


@dataclass(slots=True)
class AnalysisManifest:
    source_checksum: str
    source_size: int
    source_modified_utc: str
    source_fast_fingerprint: str = ""
    analysis_version: int = ANALYSIS_VERSION
    schema_version: str = SCHEMA_VERSION
    pipeline_version: str = "rushframe-intelligence-2"
    enabled_features: list[str] = field(default_factory=list)
    generated_at_utc: str = ""


@dataclass(slots=True)
class MediaAnalysis:
    source_path: str
    source_checksum: str = ""
    metadata: TechnicalMetadata = field(default_factory=TechnicalMetadata)
    scenes: list[SceneAnalysis] = field(default_factory=list)
    transcript: list[TranscriptSegment] = field(default_factory=list)
    audio: AudioAnalysis = field(default_factory=AudioAnalysis)
    moments: list[EditingMoment] = field(default_factory=list)
    duplicate_take_groups: list[DuplicateTakeGroup] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    schema_version: str = SCHEMA_VERSION
    analysis_version: int = ANALYSIS_VERSION
    manifest: AnalysisManifest | None = None

    @property
    def music(self) -> MusicAnalysis | None:
        """Backward-compatible alias used by the original importer."""
        return self.audio.music

    @music.setter
    def music(self, value: MusicAnalysis | None) -> None:
        self.audio.music = value

    def to_dict(self) -> dict[str, Any]:
        result = asdict(self)
        # Keep the v1 top-level field so older clients can still read beat data.
        result["music"] = result["audio"].get("music")
        return result
