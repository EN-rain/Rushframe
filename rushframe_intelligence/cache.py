"""Source fingerprinting and analysis cache validation."""

from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

from rushframe_intelligence.models import ANALYSIS_VERSION, AnalysisManifest

_HASH_BLOCK_SIZE = 4 * 1024 * 1024


def source_checksum(path: Path | str) -> str:
    source = Path(path)
    digest = hashlib.sha256()
    with source.open("rb") as stream:
        while block := stream.read(_HASH_BLOCK_SIZE):
            digest.update(block)
    return f"sha256:{digest.hexdigest()}"


def create_manifest(
    source: Path,
    checksum: str,
    enabled_features: list[str],
) -> AnalysisManifest:
    stat = source.stat()
    modified = datetime.fromtimestamp(stat.st_mtime, tz=timezone.utc).isoformat()
    return AnalysisManifest(
        source_checksum=checksum,
        source_size=stat.st_size,
        source_modified_utc=modified,
        enabled_features=sorted(set(enabled_features)),
        generated_at_utc=datetime.now(timezone.utc).isoformat(),
    )


def load_manifest(path: Path | str) -> AnalysisManifest | None:
    manifest_path = Path(path)
    if not manifest_path.is_file():
        return None
    try:
        payload = json.loads(manifest_path.read_text(encoding="utf-8"))
        return AnalysisManifest(**payload)
    except (OSError, TypeError, ValueError, json.JSONDecodeError):
        return None


def is_cache_valid(
    manifest: AnalysisManifest | None,
    source: Path,
    checksum: str,
    required_features: list[str],
) -> bool:
    if manifest is None or manifest.analysis_version != ANALYSIS_VERSION:
        return False
    stat = source.stat()
    return (
        manifest.source_checksum == checksum
        and manifest.source_size == stat.st_size
        and set(required_features).issubset(manifest.enabled_features)
    )
