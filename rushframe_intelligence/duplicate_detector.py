"""Near-duplicate take grouping using transcript and scene similarity."""

from __future__ import annotations

import re
from difflib import SequenceMatcher

from rushframe_intelligence.models import (
    DuplicateTakeCandidate,
    DuplicateTakeGroup,
    EditingMoment,
)


def _normalize(text: str) -> str:
    return " ".join(re.findall(r"[a-z0-9']+", text.lower()))


def _similarity(left: EditingMoment, right: EditingMoment) -> float:
    left_text = _normalize(left.speech or left.summary)
    right_text = _normalize(right.speech or right.summary)
    text_score = SequenceMatcher(None, left_text, right_text).ratio() if left_text and right_text else 0.0
    left_tags = set(left.tags)
    right_tags = set(right.tags)
    union = left_tags | right_tags
    tag_score = len(left_tags & right_tags) / len(union) if union else 0.0
    duration_left = max(0.001, left.end - left.start)
    duration_right = max(0.001, right.end - right.start)
    duration_score = min(duration_left, duration_right) / max(duration_left, duration_right)
    return text_score * 0.65 + tag_score * 0.20 + duration_score * 0.15


def find_duplicate_takes(
    moments: list[EditingMoment],
    *,
    threshold: float = 0.72,
) -> list[DuplicateTakeGroup]:
    candidates = [moment for moment in moments if moment.speech and len(_normalize(moment.speech)) >= 12]
    consumed: set[str] = set()
    groups: list[DuplicateTakeGroup] = []
    for seed in candidates:
        if seed.moment_id in consumed:
            continue
        matches = [seed]
        for other in candidates:
            if other.moment_id == seed.moment_id or other.moment_id in consumed:
                continue
            if _similarity(seed, other) >= threshold:
                matches.append(other)
        if len(matches) < 2:
            continue
        best = max(matches, key=lambda item: (item.scores.overall, item.confidence))
        purpose = seed.speech or seed.summary
        if len(purpose) > 80:
            purpose = purpose[:77].rstrip() + "..."
        groups.append(
            DuplicateTakeGroup(
                group_id=f"take_group_{len(groups) + 1:04d}",
                purpose=purpose,
                candidates=[
                    DuplicateTakeCandidate(
                        moment_id=moment.moment_id,
                        score=moment.scores.overall,
                        recommended=moment.moment_id == best.moment_id,
                    )
                    for moment in sorted(matches, key=lambda item: item.start)
                ],
            )
        )
        consumed.update(moment.moment_id for moment in matches)
    return groups
