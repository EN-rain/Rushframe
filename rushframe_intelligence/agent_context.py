"""Compact layered context bundles for editing agents."""

from __future__ import annotations

from collections import Counter
from dataclasses import asdict
from typing import Any

from rushframe_intelligence.models import MediaAnalysis


def build_agent_context(
    analysis: MediaAnalysis,
    *,
    query: str = "",
    roles: list[str] | None = None,
    limit: int = 20,
) -> dict[str, Any]:
    """Build a bounded context document without exposing the entire analysis."""
    normalized_roles = {role.lower() for role in roles or []}
    query_tokens = {token.lower() for token in query.split() if token.strip()}

    def matches(moment: Any) -> bool:
        if normalized_roles and not normalized_roles.intersection(
            role.lower() for role in moment.editing_roles
        ):
            return False
        if not query_tokens:
            return True
        haystack = " ".join(
            filter(None, [
                moment.summary,
                moment.speech,
                moment.visual,
                moment.audio,
                " ".join(moment.tags),
                " ".join(moment.editing_roles),
            ])
        ).lower()
        return any(token in haystack for token in query_tokens)

    ranked = sorted(
        (moment for moment in analysis.moments if matches(moment)),
        key=lambda moment: (moment.scores.overall, moment.scores.hook_potential),
        reverse=True,
    )[: max(1, min(limit, 100))]
    tag_counts = Counter(tag for moment in analysis.moments for tag in moment.tags)
    role_counts = Counter(role for moment in analysis.moments for role in moment.editing_roles)
    hooks = sorted(
        analysis.moments,
        key=lambda moment: moment.scores.hook_potential,
        reverse=True,
    )[:5]

    return {
        "context_schema_version": "1.0",
        "source": {
            "path": analysis.source_path,
            "checksum": analysis.source_checksum,
            "duration_seconds": analysis.metadata.duration,
            "orientation": analysis.metadata.orientation,
            "resolution": (
                [analysis.metadata.width, analysis.metadata.height]
                if analysis.metadata.width and analysis.metadata.height
                else None
            ),
            "fps": analysis.metadata.fps,
            "has_video": analysis.metadata.has_video,
            "has_audio": analysis.metadata.has_audio,
        },
        "summary": {
            "scene_count": len(analysis.scenes),
            "transcript_segment_count": len(analysis.transcript),
            "editing_moment_count": len(analysis.moments),
            "duplicate_take_group_count": len(analysis.duplicate_take_groups),
            "top_tags": [tag for tag, _ in tag_counts.most_common(15)],
            "role_counts": dict(role_counts.most_common()),
            "best_hook_ids": [moment.moment_id for moment in hooks if moment.scores.hook_potential > 0.35],
        },
        "request": {
            "query": query,
            "roles": sorted(normalized_roles),
            "limit": limit,
        },
        "moments": [
            {
                "moment_id": moment.moment_id,
                "start": moment.start,
                "end": moment.end,
                "summary": moment.summary,
                "visual": moment.visual,
                "speech": moment.speech,
                "audio": moment.audio,
                "editing_roles": moment.editing_roles,
                "tags": moment.tags,
                "scores": asdict(moment.scores),
                "confidence": moment.confidence,
                "evidence": moment.evidence,
            }
            for moment in ranked
        ],
        "duplicate_take_groups": [asdict(group) for group in analysis.duplicate_take_groups],
        "warnings": analysis.warnings,
    }
