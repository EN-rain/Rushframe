"""Source fingerprinting and analysis cache validation."""

from __future__ import annotations

import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path

from rushframe_intelligence.models import ANALYSIS_VERSION, AnalysisManifest

_HASH_BLOCK_SIZE = 4 * 1024 * 1024
_FAST_SAMPLE_SIZE = 1024 * 1024


def source_checksum(path: Path | str) -> str:
    source = Path(path)
    digest = hashlib.sha256()
    with source.open("rb") as stream:
        while block := stream.read(_HASH_BLOCK_SIZE):
            digest.update(block)
    return f"sha256:{digest.hexdigest()}"


def source_fast_fingerprint(path: Path | str) -> str:
    source = Path(path)
    stat = source.stat()
    digest = hashlib.blake2b(digest_size=20)
    digest.update(str(stat.st_size).encode("ascii"))
    digest.update(str(stat.st_mtime_ns).encode("ascii"))
    with source.open("rb") as stream:
        offsets = {
            0,
            max(0, (stat.st_size // 2) - (_FAST_SAMPLE_SIZE // 2)),
            max(0, stat.st_size - _FAST_SAMPLE_SIZE),
        }
        for offset in sorted(offsets):
            stream.seek(offset)
            digest.update(offset.to_bytes(8, "little", signed=False))
            digest.update(stream.read(_FAST_SAMPLE_SIZE))
    return f"blake2b-sampled:{digest.hexdigest()}"


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
        source_fast_fingerprint=source_fast_fingerprint(source),
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


def is_fast_cache_valid(
    manifest: AnalysisManifest | None,
    source: Path,
    fast_fingerprint: str,
    required_features: list[str],
) -> bool:
    if manifest is None or manifest.analysis_version != ANALYSIS_VERSION:
        return False
    stat = source.stat()
    return (
        bool(manifest.source_fast_fingerprint)
        and manifest.source_fast_fingerprint == fast_fingerprint
        and manifest.source_size == stat.st_size
        and set(required_features).issubset(manifest.enabled_features)
    )


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
