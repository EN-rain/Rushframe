"""Cross-modal alignment and editing-role scoring."""

from __future__ import annotations

import re
from collections import Counter

from rushframe_intelligence.models import (
    AudioAnalysis,
    EditingMoment,
    MomentScores,
    SceneAnalysis,
    TranscriptSegment,
)

_HOOK_WORDS = {
    "secret", "never", "best", "worst", "fastest", "mistake", "surprising",
    "unexpected", "stop", "warning", "truth", "why", "how", "watch",
}
_CTA_WORDS = {"follow", "subscribe", "comment", "share", "buy", "download", "try", "click", "visit"}
_PROOF_WORDS = {"result", "proof", "tested", "test", "compare", "before", "after", "data", "works"}
_PROBLEM_WORDS = {"problem", "issue", "hard", "difficult", "broken", "fail", "wrong", "struggle"}
_PAYOFF_WORDS = {"finally", "finished", "done", "result", "reveal", "here it is", "this is it"}
_FILLER_PATTERN = re.compile(r"\b(?:um+|uh+|erm|you know|kind of|sort of)\b", re.IGNORECASE)


def _overlaps(start: float, end: float, other_start: float, other_end: float) -> bool:
    return min(end, other_end) > max(start, other_start)


def _clamp(value: float) -> float:
    return max(0.0, min(1.0, value))


def _words(text: str) -> set[str]:
    return set(re.findall(r"[a-z0-9']+", text.lower()))


def _roles(scene: SceneAnalysis | None, transcript: str, duration: float) -> list[str]:
    words = _words(transcript)
    roles: list[str] = list(scene.editing_roles if scene else [])
    if words & _HOOK_WORDS or ("?" in transcript and duration <= 8):
        roles.append("hook")
    if words & _CTA_WORDS:
        roles.append("call_to_action")
    if words & _PROOF_WORDS:
        roles.append("proof")
    if words & _PROBLEM_WORDS:
        roles.append("problem")
    if words & _PAYOFF_WORDS:
        roles.append("payoff")
    if scene:
        if scene.actions:
            roles.append("demonstration")
        if scene.shot_type and any(value in scene.shot_type.lower() for value in ("close", "detail", "insert")):
            roles.append("b-roll")
        if scene.mood and any(value in scene.mood.lower() for value in ("fun", "happy", "surpris", "excited")):
            roles.append("reaction")
        if not transcript.strip() and scene.description:
            roles.append("b-roll")
    if _FILLER_PATTERN.search(transcript) or (transcript and len(words) < 3 and duration > 4):
        roles.append("filler")
    if not roles:
        roles.append("context" if transcript else "b-roll")
    return list(dict.fromkeys(roles))


def _scores(
    scene: SceneAnalysis | None,
    transcript_segments: list[TranscriptSegment],
    roles: list[str],
    duration: float,
) -> MomentScores:
    speech = " ".join(segment.text for segment in transcript_segments)
    words = _words(speech)
    quality = scene.quality.visual_quality if scene else None
    visual_energy = scene.visual_energy if scene else None
    confidence_values = [
        value for value in [
            scene.confidence if scene else None,
            *(segment.confidence for segment in transcript_segments),
        ]
        if value is not None
    ]
    confidence = sum(confidence_values) / len(confidence_values) if confidence_values else 0.55
    hook = 0.15
    if "hook" in roles:
        hook += 0.55
    if duration <= 5:
        hook += 0.12
    if words & _HOOK_WORDS:
        hook += 0.12
    emotional = visual_energy or 0.25
    if scene and scene.mood and scene.mood.lower() not in {"neutral", "calm"}:
        emotional += 0.25
    novelty = 0.45 if scene and (scene.actions or scene.subjects) else 0.25
    broll = 0.65 if "b-roll" in roles else 0.2
    continuity = 0.55 if duration >= 1.0 else 0.25
    brand = 0.35
    importance = 0.35
    for role in ("hook", "proof", "payoff", "call_to_action", "demonstration"):
        if role in roles:
            importance += 0.1
    if "filler" in roles:
        importance -= 0.3
    if quality is not None:
        importance += (quality - 0.5) * 0.2
    overall = (
        _clamp(importance) * 0.30
        + _clamp(hook) * 0.18
        + _clamp(emotional) * 0.12
        + _clamp(novelty) * 0.10
        + _clamp(broll) * 0.10
        + _clamp(continuity) * 0.08
        + _clamp(brand) * 0.04
        + _clamp(confidence) * 0.08
    )
    return MomentScores(
        importance=_clamp(importance),
        hook_potential=_clamp(hook),
        emotional_intensity=_clamp(emotional),
        novelty=_clamp(novelty),
        broll_usefulness=_clamp(broll),
        continuity=_clamp(continuity),
        brand_relevance=_clamp(brand),
        overall=_clamp(overall),
    )


def build_editing_moments(
    scenes: list[SceneAnalysis],
    transcript: list[TranscriptSegment],
    audio: AudioAnalysis,
) -> list[EditingMoment]:
    ranges: list[tuple[float, float, SceneAnalysis | None]] = []
    if scenes:
        ranges.extend((scene.start, scene.end, scene) for scene in scenes)
    else:
        ranges.extend((segment.start, segment.end, None) for segment in transcript)

    moments: list[EditingMoment] = []
    for index, (start, end, scene) in enumerate(ranges, start=1):
        matching_transcript = [
            segment for segment in transcript
            if _overlaps(start, end, segment.start, segment.end)
        ]
        matching_audio = [
            event for event in audio.events
            if _overlaps(start, end, event.start, event.end)
        ]
        speech = " ".join(segment.text for segment in matching_transcript).strip()
        duration = max(0.001, end - start)
        roles = _roles(scene, speech, duration)
        visual = None
        if scene:
            visual = scene.summary or scene.description
            if not visual:
                parts = [*scene.subjects, *scene.actions]
                visual = ", ".join(parts) or None
        audio_summary = ", ".join(
            dict.fromkeys(event.label or event.event_type for event in matching_audio)
        ) or None
        summary_parts = [part for part in (visual, speech, audio_summary) if part]
        summary = " — ".join(summary_parts) if summary_parts else f"Source moment {start:.2f}-{end:.2f}s"
        scores = _scores(scene, matching_transcript, roles, duration)
        evidence: list[str] = []
        if speech:
            evidence.append("contains aligned speech")
        if scene and scene.quality.visual_quality is not None:
            evidence.append(f"visual quality {scene.quality.visual_quality:.2f}")
        if "hook" in roles:
            evidence.append("language or duration suggests an opening hook")
        if matching_audio:
            evidence.append("contains aligned audio events")
        confidence_values = [
            value for value in [
                scene.confidence if scene else None,
                *(segment.confidence for segment in matching_transcript),
            ]
            if value is not None
        ]
        confidence = sum(confidence_values) / len(confidence_values) if confidence_values else 0.55
        tags = list(dict.fromkeys([
            *(scene.tags if scene else []),
            *roles,
            *(event.label or event.event_type for event in matching_audio),
        ]))
        moments.append(
            EditingMoment(
                moment_id=f"moment_{index:04d}",
                start=start,
                end=end,
                summary=summary,
                scene_ids=[scene.scene_id] if scene else [],
                transcript_segment_ids=[segment.segment_id for segment in matching_transcript],
                audio_event_ids=[event.event_id for event in matching_audio],
                visual=visual,
                speech=speech or None,
                audio=audio_summary,
                editing_roles=roles,
                tags=tags,
                scores=scores,
                confidence=_clamp(confidence),
                evidence=evidence,
                facts={
                    "duration": duration,
                    "visible_text": scene.visible_text if scene else [],
                    "subjects": scene.subjects if scene else [],
                    "actions": scene.actions if scene else [],
                    "speaker_count": len({segment.speaker for segment in matching_transcript if segment.speaker}),
                },
                interpretation={
                    "primary_role": roles[0],
                    "usable": scene.usable if scene else True,
                },
            )
        )

    # Add orphan transcript ranges that do not overlap a detected scene.
    covered_ids = {segment_id for moment in moments for segment_id in moment.transcript_segment_ids}
    for segment in transcript:
        if segment.segment_id in covered_ids:
            continue
        index = len(moments) + 1
        roles = _roles(None, segment.text, segment.end - segment.start)
        scores = _scores(None, [segment], roles, segment.end - segment.start)
        moments.append(
            EditingMoment(
                moment_id=f"moment_{index:04d}",
                start=segment.start,
                end=segment.end,
                summary=segment.text,
                transcript_segment_ids=[segment.segment_id],
                speech=segment.text,
                editing_roles=roles,
                tags=roles.copy(),
                scores=scores,
                confidence=segment.confidence or 0.55,
                evidence=["transcript segment outside detected visual scenes"],
            )
        )

    role_counts = Counter(role for moment in moments for role in moment.editing_roles)
    for moment in moments:
        if role_counts.get(moment.editing_roles[0], 0) == 1:
            moment.scores.novelty = _clamp(moment.scores.novelty + 0.15)
            moment.scores.overall = _clamp(moment.scores.overall + 0.04)
    return sorted(moments, key=lambda item: (item.start, item.end))
