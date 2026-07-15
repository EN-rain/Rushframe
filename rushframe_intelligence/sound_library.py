"""Local-first SQLite catalog and semantic search for Rushframe sound assets."""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import shutil
import sqlite3
import subprocess
import sys
import uuid
import wave
from array import array
from contextlib import contextmanager
from dataclasses import asdict, dataclass, field
from datetime import UTC, datetime
from functools import lru_cache
from pathlib import Path
from threading import Lock
from typing import Any, Iterable, Iterator, Sequence

CATALOG_SCHEMA_VERSION = 4
ANALYSIS_VERSION = "sound-library-v4"
HASH_EMBEDDING_PROVIDER = "builtin-hash-v1"
CLAP_EMBEDDING_PROVIDER = "laion-clap/630k-audioset-best"
KNOWN_AUDIO_EXTENSIONS = {
    ".wav", ".mp3", ".aac", ".m4a", ".flac", ".ogg", ".oga", ".opus",
    ".wma", ".aif", ".aiff", ".ac3", ".amr", ".caf",
}
MAX_SEARCH_RESULTS = 50
MAX_CANDIDATES = 2000


@dataclass(slots=True)
class SoundLibraryRoot:
    root_id: str
    path: str
    watch_enabled: bool
    added_utc: str


@dataclass(slots=True)
class SoundLibraryCollection:
    collection_id: str
    name: str
    project_id: str
    created_utc: str
    item_count: int


@dataclass(slots=True)
class SoundLibraryStatus:
    catalog_path: str
    schema_version: int
    sound_count: int
    online_count: int
    favorite_count: int
    root_count: int
    roots: list[SoundLibraryRoot]
    embedding_providers: list[str]
    preferred_embedding_provider: str | None


@dataclass(slots=True)
class SoundIndexWarning:
    path: str
    message: str


@dataclass(slots=True)
class SoundIndexResult:
    indexed: list[str] = field(default_factory=list)
    duplicates: list[str] = field(default_factory=list)
    skipped: list[str] = field(default_factory=list)
    warnings: list[SoundIndexWarning] = field(default_factory=list)
    roots: list[SoundLibraryRoot] = field(default_factory=list)
    embedding_provider: str = HASH_EMBEDDING_PROVIDER


@dataclass(slots=True)
class SoundSearchResult:
    sound_id: str
    name: str
    path: str
    content_hash: str
    duration: float
    codec: str
    channels: int | None
    sample_rate: int | None
    lufs: float | None
    peak_db: float | None
    leading_silence: float | None
    trailing_silence: float | None
    category: str
    mood: str
    tempo_bpm: float | None
    license_name: str
    attribution: str
    requires_attribution: bool
    favorite: bool
    offline: bool
    tags: list[str]
    derivative_path: str | None
    waveform_path: str | None
    completed_features: list[str]
    score: float
    lexical_score: float
    semantic_score: float
    embedding_provider: str | None
    indexed_utc: str


@dataclass(slots=True)
class SoundSearchResponse:
    results: list[SoundSearchResult]
    mode: str
    embedding_provider: str | None
    semantic_available: bool
    warning: str | None = None


class _CachedClapModel:
    def __init__(self, model: Any) -> None:
        self.model = model
        self.lock = Lock()


@lru_cache(maxsize=1)
def _get_clap_model(checkpoint_path: str) -> _CachedClapModel:
    original_argv = sys.argv
    try:
        sys.argv = original_argv[:1]
        import laion_clap
    finally:
        sys.argv = original_argv
    import numpy as np
    import torch

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

        state = load_state_dict(checkpoint_path, skip_params=True)
    state.pop("text_branch.embeddings.position_ids", None)
    model.model.load_state_dict(state)
    return _CachedClapModel(model)


class SoundLibraryCatalog:
    def __init__(self, database_path: Path | str, *, derivatives_directory: Path | str | None = None) -> None:
        self.database_path = Path(database_path).expanduser().resolve()
        self.database_path.parent.mkdir(parents=True, exist_ok=True)
        self.derivatives_directory = (
            Path(derivatives_directory).expanduser().resolve()
            if derivatives_directory is not None
            else self.database_path.parent / "derivatives"
        )
        self.derivatives_directory.mkdir(parents=True, exist_ok=True)
        self._ensure_schema()

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path, timeout=30)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=NORMAL")
        connection.execute("PRAGMA foreign_keys=ON")
        connection.execute("PRAGMA busy_timeout=30000")
        return connection

    @contextmanager
    def _connection(self) -> Iterator[sqlite3.Connection]:
        connection = self._connect()
        try:
            with connection:
                yield connection
        finally:
            connection.close()

    def _ensure_schema(self) -> None:
        with self._connection() as connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS catalog_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS library_roots (
                    root_id TEXT PRIMARY KEY,
                    canonical_path TEXT NOT NULL UNIQUE,
                    watch_enabled INTEGER NOT NULL DEFAULT 1,
                    added_utc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS sounds (
                    sound_id TEXT PRIMARY KEY,
                    canonical_path TEXT NOT NULL UNIQUE,
                    content_hash TEXT NOT NULL,
                    file_size INTEGER NOT NULL,
                    modified_ns INTEGER NOT NULL,
                    duration REAL NOT NULL,
                    codec TEXT NOT NULL,
                    channels INTEGER,
                    sample_rate INTEGER,
                    lufs REAL,
                    peak_db REAL,
                    leading_silence REAL,
                    trailing_silence REAL,
                    category TEXT NOT NULL,
                    mood TEXT NOT NULL,
                    tempo_bpm REAL,
                    license_name TEXT NOT NULL DEFAULT '',
                    attribution TEXT NOT NULL DEFAULT '',
                    requires_attribution INTEGER NOT NULL DEFAULT 0,
                    favorite INTEGER NOT NULL DEFAULT 0,
                    offline INTEGER NOT NULL DEFAULT 0,
                    tags_json TEXT NOT NULL DEFAULT '[]',
                    derivative_path TEXT,
                    waveform_path TEXT,
                    source_root_id TEXT,
                    indexed_utc TEXT NOT NULL,
                    analysis_version TEXT NOT NULL,
                    completed_features_json TEXT NOT NULL DEFAULT '[]',
                    FOREIGN KEY(source_root_id) REFERENCES library_roots(root_id) ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS ix_sounds_content_hash ON sounds(content_hash);
                CREATE INDEX IF NOT EXISTS ix_sounds_filters ON sounds(offline, category, mood, favorite, duration);
                CREATE VIRTUAL TABLE IF NOT EXISTS sounds_fts USING fts5(
                    sound_id UNINDEXED,
                    name,
                    path,
                    category,
                    mood,
                    tags,
                    license_name,
                    attribution,
                    tokenize='unicode61 remove_diacritics 2'
                );
                CREATE TABLE IF NOT EXISTS embeddings (
                    sound_id TEXT NOT NULL,
                    provider TEXT NOT NULL,
                    dimensions INTEGER NOT NULL,
                    vector_blob BLOB NOT NULL,
                    generated_utc TEXT NOT NULL,
                    PRIMARY KEY(sound_id, provider),
                    FOREIGN KEY(sound_id) REFERENCES sounds(sound_id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_embeddings_provider ON embeddings(provider);
                CREATE TABLE IF NOT EXISTS collections (
                    collection_id TEXT PRIMARY KEY,
                    scope_project_id TEXT NOT NULL DEFAULT '',
                    name TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    UNIQUE(scope_project_id, name)
                );
                CREATE TABLE IF NOT EXISTS collection_items (
                    collection_id TEXT NOT NULL,
                    sound_id TEXT NOT NULL,
                    added_utc TEXT NOT NULL,
                    PRIMARY KEY(collection_id, sound_id),
                    FOREIGN KEY(collection_id) REFERENCES collections(collection_id) ON DELETE CASCADE,
                    FOREIGN KEY(sound_id) REFERENCES sounds(sound_id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS project_usage (
                    project_id TEXT NOT NULL,
                    media_asset_id TEXT NOT NULL,
                    sound_id TEXT NOT NULL,
                    last_used_utc TEXT NOT NULL,
                    use_count INTEGER NOT NULL DEFAULT 1,
                    PRIMARY KEY(project_id, media_asset_id, sound_id),
                    FOREIGN KEY(sound_id) REFERENCES sounds(sound_id) ON DELETE CASCADE
                );
                """
            )
            sound_columns = {
                str(row[1]) for row in connection.execute("PRAGMA table_info(sounds)").fetchall()
            }
            if "waveform_path" not in sound_columns:
                connection.execute("ALTER TABLE sounds ADD COLUMN waveform_path TEXT")
            if "completed_features_json" not in sound_columns:
                connection.execute(
                    "ALTER TABLE sounds ADD COLUMN completed_features_json TEXT NOT NULL DEFAULT '[]'"
                )
            collection_columns = {
                str(row[1]) for row in connection.execute("PRAGMA table_info(collections)").fetchall()
            }
            if "scope_project_id" not in collection_columns:
                connection.execute(
                    "ALTER TABLE collections ADD COLUMN scope_project_id TEXT NOT NULL DEFAULT ''"
                )
            connection.execute(
                "CREATE INDEX IF NOT EXISTS ix_collections_scope_name ON collections(scope_project_id, name)"
            )
            connection.execute(
                "INSERT INTO catalog_meta(key, value) VALUES('schema_version', ?) "
                "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                (str(CATALOG_SCHEMA_VERSION),),
            )

    def status(self) -> SoundLibraryStatus:
        self.refresh_offline_state()
        with self._connection() as connection:
            counts = connection.execute(
                """
                SELECT COUNT(*) AS sound_count,
                       SUM(CASE WHEN offline = 0 THEN 1 ELSE 0 END) AS online_count,
                       SUM(CASE WHEN favorite = 1 THEN 1 ELSE 0 END) AS favorite_count
                FROM sounds
                """
            ).fetchone()
            roots = [self._row_to_root(row) for row in connection.execute(
                "SELECT * FROM library_roots ORDER BY canonical_path COLLATE NOCASE"
            ).fetchall()]
            providers = [
                str(row[0])
                for row in connection.execute(
                    "SELECT DISTINCT provider FROM embeddings ORDER BY provider"
                ).fetchall()
            ]
            preferred_row = connection.execute(
                "SELECT value FROM catalog_meta WHERE key='preferred_embedding_provider'"
            ).fetchone()
        return SoundLibraryStatus(
            catalog_path=str(self.database_path),
            schema_version=CATALOG_SCHEMA_VERSION,
            sound_count=int(counts["sound_count"] or 0),
            online_count=int(counts["online_count"] or 0),
            favorite_count=int(counts["favorite_count"] or 0),
            root_count=len(roots),
            roots=roots,
            embedding_providers=providers,
            preferred_embedding_provider=str(preferred_row[0]) if preferred_row else None,
        )

    def add_root(self, root: Path | str, *, watch_enabled: bool = True) -> SoundLibraryRoot:
        canonical = _canonical_local_directory(root)
        now = _utc_now()
        with self._connection() as connection:
            row = connection.execute(
                "SELECT * FROM library_roots WHERE canonical_path = ? COLLATE NOCASE",
                (str(canonical),),
            ).fetchone()
            if row:
                connection.execute(
                    "UPDATE library_roots SET watch_enabled=? WHERE root_id=?",
                    (1 if watch_enabled else 0, str(row["root_id"])),
                )
                return SoundLibraryRoot(
                    root_id=str(row["root_id"]),
                    path=str(row["canonical_path"]),
                    watch_enabled=watch_enabled,
                    added_utc=str(row["added_utc"]),
                )
            root_id = str(uuid.uuid4())
            connection.execute(
                "INSERT INTO library_roots(root_id, canonical_path, watch_enabled, added_utc) VALUES (?, ?, ?, ?)",
                (root_id, str(canonical), 1 if watch_enabled else 0, now),
            )
        return SoundLibraryRoot(root_id, str(canonical), watch_enabled, now)

    def list_roots(self) -> list[SoundLibraryRoot]:
        with self._connection() as connection:
            return [self._row_to_root(row) for row in connection.execute(
                "SELECT * FROM library_roots ORDER BY canonical_path COLLATE NOCASE"
            ).fetchall()]

    def set_root_watch(self, root_id: str, enabled: bool) -> None:
        with self._connection() as connection:
            cursor = connection.execute(
                "UPDATE library_roots SET watch_enabled=? WHERE root_id=?",
                (1 if enabled else 0, root_id),
            )
            if cursor.rowcount == 0:
                raise ValueError("Sound-library root was not found")

    def remove_root(self, root_id: str) -> None:
        with self._connection() as connection:
            connection.execute("DELETE FROM library_roots WHERE root_id=?", (root_id,))

    def index_root(
        self,
        root: Path | str,
        *,
        recursive: bool = True,
        watch_enabled: bool = True,
        ffprobe: str | None = None,
        ffmpeg: str | None = None,
        enable_clap: bool = True,
        enable_essentia: bool = True,
        create_normalized_derivative: bool = False,
        trim_silence: bool = False,
    ) -> SoundIndexResult:
        root_record = self.add_root(root, watch_enabled=watch_enabled)
        canonical_root = Path(root_record.path)
        iterator = canonical_root.rglob("*") if recursive else canonical_root.glob("*")
        files = [path for path in iterator if path.is_file() and path.suffix.lower() in KNOWN_AUDIO_EXTENSIONS]
        result = self.index_paths(
            files,
            ffprobe=ffprobe,
            ffmpeg=ffmpeg,
            enable_clap=enable_clap,
            enable_essentia=enable_essentia,
            create_normalized_derivative=create_normalized_derivative,
            trim_silence=trim_silence,
            approved_root=canonical_root,
        )
        result.roots = self.list_roots()
        self._mark_missing_under_root_offline(canonical_root)
        return result

    def index_paths(
        self,
        paths: Iterable[Path | str],
        *,
        ffprobe: str | None = None,
        ffmpeg: str | None = None,
        enable_clap: bool = True,
        enable_essentia: bool = True,
        create_normalized_derivative: bool = False,
        trim_silence: bool = False,
        approved_root: Path | None = None,
    ) -> SoundIndexResult:
        result = SoundIndexResult()
        unique_paths: list[Path] = []
        seen: set[str] = set()
        for requested in paths:
            try:
                canonical = _canonical_local_file(requested)
                if approved_root is not None and not _is_within(canonical, approved_root):
                    raise ValueError("File resolves outside the approved sound-library root")
            except (OSError, ValueError) as exc:
                result.skipped.append(str(requested))
                result.warnings.append(SoundIndexWarning(str(requested), str(exc)))
                continue
            key = os.path.normcase(str(canonical))
            if key in seen:
                continue
            seen.add(key)
            unique_paths.append(canonical)

        clap_available = enable_clap and _clap_is_available()
        result.embedding_provider = HASH_EMBEDDING_PROVIDER
        for path in unique_paths:
            try:
                root_record = self._resolve_or_add_parent_root(path, approved_root)
                indexed_id, duplicate = self._index_one(
                    path,
                    root_record=root_record,
                    ffprobe=ffprobe,
                    ffmpeg=ffmpeg,
                    enable_clap=clap_available,
                    enable_essentia=enable_essentia,
                    create_normalized_derivative=create_normalized_derivative,
                    trim_silence=trim_silence,
                )
                (result.duplicates if duplicate else result.indexed).append(indexed_id)
            except Exception as exc:
                result.skipped.append(str(path))
                result.warnings.append(SoundIndexWarning(str(path), str(exc)))
        result.roots = self.list_roots()
        with self._connection() as connection:
            if connection.execute(
                "SELECT 1 FROM embeddings WHERE provider=? LIMIT 1",
                (CLAP_EMBEDDING_PROVIDER,),
            ).fetchone():
                result.embedding_provider = CLAP_EMBEDDING_PROVIDER
        return result

    def _resolve_or_add_parent_root(self, path: Path, approved_root: Path | None) -> SoundLibraryRoot:
        roots = self.list_roots()
        for root in roots:
            root_path = Path(root.path)
            if _is_within(path, root_path):
                return root
        return self.add_root(approved_root or path.parent, watch_enabled=False)

    def _index_one(
        self,
        path: Path,
        *,
        root_record: SoundLibraryRoot,
        ffprobe: str | None,
        ffmpeg: str | None,
        enable_clap: bool,
        enable_essentia: bool,
        create_normalized_derivative: bool,
        trim_silence: bool,
    ) -> tuple[str, bool]:
        stat = path.stat()
        ffmpeg_available = _resolve_executable(ffmpeg, "ffmpeg") is not None
        essentia_available = enable_essentia and _essentia_is_available()
        required_features = {"metadata", "hash", "hash_embedding"}
        if ffmpeg_available:
            required_features.update({"loudness", "waveform"})
        if essentia_available:
            required_features.add("essentia")
        if enable_clap:
            required_features.add("clap")
        if ffmpeg_available and (create_normalized_derivative or trim_silence):
            required_features.add("derivative")

        with self._connection() as connection:
            unchanged = connection.execute(
                """
                SELECT sound_id, content_hash, analysis_version, completed_features_json,
                       derivative_path, waveform_path
                FROM sounds
                WHERE canonical_path=? COLLATE NOCASE AND file_size=? AND modified_ns=?
                """,
                (str(path), stat.st_size, stat.st_mtime_ns),
            ).fetchone()
        if unchanged:
            completed = set(json.loads(unchanged["completed_features_json"] or "[]"))
            derivative_ready = (
                "derivative" not in required_features
                or bool(unchanged["derivative_path"] and Path(str(unchanged["derivative_path"])).is_file())
            )
            waveform_ready = (
                "waveform" not in required_features
                or bool(unchanged["waveform_path"] and Path(str(unchanged["waveform_path"])).is_file())
            )
            if (
                str(unchanged["analysis_version"]) == ANALYSIS_VERSION
                and required_features.issubset(completed)
                and derivative_ready
                and waveform_ready
            ):
                with self._connection() as connection:
                    connection.execute(
                        "UPDATE sounds SET offline=0 WHERE sound_id=?",
                        (str(unchanged["sound_id"]),),
                    )
                return str(unchanged["sound_id"]), False

        completed_features = {"metadata", "hash", "hash_embedding"}
        probe = _probe_audio(path, ffprobe)
        content_hash = _sha256(path)
        category, mood, tags = _classify_filename(path)
        tempo_bpm: float | None = None
        if essentia_available:
            try:
                tempo_bpm = _measure_tempo_essentia(path)
                completed_features.add("essentia")
                if tempo_bpm is not None:
                    tags.append("tempo-detected")
            except Exception:
                tempo_bpm = None
        lufs, peak_db, leading_silence, trailing_silence, loudness_completed = _measure_audio(
            ffmpeg,
            path,
            probe["duration"],
        )
        if loudness_completed:
            completed_features.add("loudness")
        waveform = self._create_waveform(path, content_hash, ffmpeg=ffmpeg)
        waveform_path = str(waveform) if waveform else None
        if waveform_path:
            completed_features.add("waveform")
        derivative_path: str | None = None
        if create_normalized_derivative or trim_silence:
            derivative = self._create_derivative(
                path,
                content_hash,
                ffmpeg=ffmpeg,
                normalize=create_normalized_derivative,
                trim_silence=trim_silence,
                leading_silence=leading_silence,
                trailing_silence=trailing_silence,
                duration=probe["duration"],
            )
            derivative_path = str(derivative) if derivative else None
            if derivative_path:
                completed_features.add("derivative")

        hash_vector = _hash_embedding(" ".join([path.stem, category, mood, *tags]))
        clap_vector: list[float] | None = None
        if enable_clap:
            try:
                clap_vector = _clap_audio_embedding(path)
                if clap_vector:
                    completed_features.add("clap")
            except Exception:
                clap_vector = None

        now = _utc_now()
        duplicate = False
        with self._connection() as connection:
            same_hash = connection.execute(
                "SELECT sound_id, canonical_path FROM sounds WHERE content_hash=? ORDER BY indexed_utc LIMIT 1",
                (content_hash,),
            ).fetchone()
            existing_path = connection.execute(
                "SELECT sound_id, favorite, license_name, attribution, requires_attribution FROM sounds WHERE canonical_path=? COLLATE NOCASE",
                (str(path),),
            ).fetchone()
            if existing_path:
                sound_id = str(existing_path["sound_id"])
                favorite = int(existing_path["favorite"])
                license_name = str(existing_path["license_name"])
                attribution = str(existing_path["attribution"])
                requires_attribution = int(existing_path["requires_attribution"])
            elif same_hash:
                sound_id = str(uuid.uuid4())
                favorite = 0
                license_name = ""
                attribution = ""
                requires_attribution = 0
                duplicate = True
            else:
                sound_id = str(uuid.uuid4())
                favorite = 0
                license_name = ""
                attribution = ""
                requires_attribution = 0

            connection.execute(
                    """
                    INSERT INTO sounds(
                        sound_id, canonical_path, content_hash, file_size, modified_ns, duration,
                        codec, channels, sample_rate, lufs, peak_db, leading_silence,
                        trailing_silence, category, mood, tempo_bpm, license_name,
                        attribution, requires_attribution, favorite, offline, tags_json,
                        derivative_path, waveform_path, source_root_id, indexed_utc, analysis_version,
                        completed_features_json)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(sound_id) DO UPDATE SET
                        canonical_path=excluded.canonical_path,
                        content_hash=excluded.content_hash,
                        file_size=excluded.file_size,
                        modified_ns=excluded.modified_ns,
                        duration=excluded.duration,
                        codec=excluded.codec,
                        channels=excluded.channels,
                        sample_rate=excluded.sample_rate,
                        lufs=excluded.lufs,
                        peak_db=excluded.peak_db,
                        leading_silence=excluded.leading_silence,
                        trailing_silence=excluded.trailing_silence,
                        category=excluded.category,
                        mood=excluded.mood,
                        tempo_bpm=excluded.tempo_bpm,
                        offline=0,
                        tags_json=excluded.tags_json,
                        derivative_path=excluded.derivative_path,
                        waveform_path=excluded.waveform_path,
                        source_root_id=excluded.source_root_id,
                        indexed_utc=excluded.indexed_utc,
                        analysis_version=excluded.analysis_version,
                        completed_features_json=excluded.completed_features_json
                    """,
                    (
                        sound_id, str(path), content_hash, stat.st_size, stat.st_mtime_ns,
                        probe["duration"], probe["codec"], probe["channels"], probe["sample_rate"],
                        lufs, peak_db, leading_silence, trailing_silence, category, mood,
                        tempo_bpm, license_name, attribution, requires_attribution, favorite,
                        json.dumps(sorted(set(tags)), ensure_ascii=False), derivative_path,
                        waveform_path, root_record.root_id, now, ANALYSIS_VERSION,
                        json.dumps(sorted(completed_features)),
                    ),
                )
            connection.execute("DELETE FROM sounds_fts WHERE sound_id=?", (sound_id,))
            connection.execute(
                "INSERT INTO sounds_fts(sound_id, name, path, category, mood, tags, license_name, attribution) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    sound_id, path.name, str(path), category, mood, " ".join(sorted(set(tags))),
                    license_name, attribution,
                ),
            )
            self._upsert_embedding(connection, sound_id, HASH_EMBEDDING_PROVIDER, hash_vector, now)
            if clap_vector:
                self._upsert_embedding(connection, sound_id, CLAP_EMBEDDING_PROVIDER, clap_vector, now)
                connection.execute(
                    "INSERT INTO catalog_meta(key, value) VALUES('preferred_embedding_provider', ?) "
                    "ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                    (CLAP_EMBEDDING_PROVIDER,),
                )
            elif not connection.execute(
                "SELECT 1 FROM catalog_meta WHERE key='preferred_embedding_provider'"
            ).fetchone():
                connection.execute(
                    "INSERT INTO catalog_meta(key, value) VALUES('preferred_embedding_provider', ?)",
                    (HASH_EMBEDDING_PROVIDER,),
                )
        return sound_id, duplicate

    @staticmethod
    def _upsert_embedding(
        connection: sqlite3.Connection,
        sound_id: str,
        provider: str,
        vector: Sequence[float],
        generated_utc: str,
    ) -> None:
        connection.execute(
            """
            INSERT INTO embeddings(sound_id, provider, dimensions, vector_blob, generated_utc)
            VALUES (?, ?, ?, ?, ?)
            ON CONFLICT(sound_id, provider) DO UPDATE SET
                dimensions=excluded.dimensions,
                vector_blob=excluded.vector_blob,
                generated_utc=excluded.generated_utc
            """,
            (
                sound_id,
                provider,
                len(vector),
                sqlite3.Binary(_vector_to_blob(vector)),
                generated_utc,
            ),
        )

    def _create_waveform(
        self,
        source: Path,
        content_hash: str,
        *,
        ffmpeg: str | None,
    ) -> Path | None:
        executable = _resolve_executable(ffmpeg, "ffmpeg")
        if not executable:
            return None
        directory = self.derivatives_directory / "waveforms"
        directory.mkdir(parents=True, exist_ok=True)
        output = directory / f"{content_hash[:24]}.png"
        if output.exists() and output.stat().st_size > 0:
            return output
        temporary = directory / f"{content_hash[:24]}.tmp-{uuid.uuid4().hex}.png"
        arguments = [
            executable,
            "-v", "error", "-y", "-i", str(source),
            "-filter_complex", "aformat=channel_layouts=mono,showwavespic=s=1200x180:colors=white",
            "-frames:v", "1", str(temporary),
        ]
        try:
            completed = subprocess.run(arguments, capture_output=True, text=True, timeout=180, check=False)
            if completed.returncode != 0 or not temporary.exists() or temporary.stat().st_size == 0:
                temporary.unlink(missing_ok=True)
                return None
            os.replace(temporary, output)
            return output
        finally:
            temporary.unlink(missing_ok=True)

    def _create_derivative(
        self,
        source: Path,
        content_hash: str,
        *,
        ffmpeg: str | None,
        normalize: bool,
        trim_silence: bool,
        leading_silence: float | None,
        trailing_silence: float | None,
        duration: float,
    ) -> Path | None:
        executable = _resolve_executable(ffmpeg, "ffmpeg")
        if not executable:
            return None
        output = self.derivatives_directory / f"{content_hash[:24]}.wav"
        if output.exists() and output.stat().st_size > 0:
            return output
        filters: list[str] = []
        if normalize:
            filters.append("loudnorm=I=-16:TP=-1.5:LRA=11")
        start = max(0.0, leading_silence or 0.0) if trim_silence else 0.0
        end = duration - max(0.0, trailing_silence or 0.0) if trim_silence else duration
        if end <= start + 0.01:
            start, end = 0.0, duration
        temp = output.with_suffix(f".tmp-{uuid.uuid4().hex}.wav")
        arguments = [executable, "-v", "error", "-y", "-ss", f"{start:.6f}", "-i", str(source)]
        if end < duration - 0.001:
            arguments.extend(["-t", f"{end - start:.6f}"])
        if filters:
            arguments.extend(["-af", ",".join(filters)])
        arguments.extend(["-vn", "-acodec", "pcm_s16le", str(temp)])
        try:
            completed = subprocess.run(arguments, capture_output=True, text=True, timeout=300, check=False)
            if completed.returncode != 0 or not temp.exists() or temp.stat().st_size == 0:
                temp.unlink(missing_ok=True)
                return None
            os.replace(temp, output)
            return output
        finally:
            temp.unlink(missing_ok=True)

    def search(
        self,
        query: str = "",
        *,
        max_results: int = 20,
        max_duration: float | None = None,
        min_lufs: float | None = None,
        max_lufs: float | None = None,
        min_tempo: float | None = None,
        max_tempo: float | None = None,
        category: str | None = None,
        mood: str | None = None,
        license_name: str | None = None,
        favorites_only: bool = False,
        online_only: bool = True,
        semantic: bool = True,
        similar_to_sound_id: str | None = None,
        collection_id: str | None = None,
        project_id: str | None = None,
        recently_used: bool = False,
    ) -> SoundSearchResponse:
        self.refresh_offline_state()
        limit = max(1, min(MAX_SEARCH_RESULTS, int(max_results)))
        clauses: list[str] = []
        parameters: list[object] = []
        if online_only:
            clauses.append("s.offline=0")
        if max_duration is not None:
            clauses.append("s.duration <= ?")
            parameters.append(max(0.001, float(max_duration)))
        if min_lufs is not None:
            clauses.append("(s.lufs IS NOT NULL AND s.lufs >= ?)")
            parameters.append(float(min_lufs))
        if max_lufs is not None:
            clauses.append("(s.lufs IS NOT NULL AND s.lufs <= ?)")
            parameters.append(float(max_lufs))
        if min_tempo is not None:
            clauses.append("(s.tempo_bpm IS NOT NULL AND s.tempo_bpm >= ?)")
            parameters.append(max(0.0, float(min_tempo)))
        if max_tempo is not None:
            clauses.append("(s.tempo_bpm IS NOT NULL AND s.tempo_bpm <= ?)")
            parameters.append(max(0.0, float(max_tempo)))
        if category:
            clauses.append("s.category = ? COLLATE NOCASE")
            parameters.append(category.strip())
        if mood:
            clauses.append("s.mood = ? COLLATE NOCASE")
            parameters.append(mood.strip())
        if license_name:
            clauses.append("s.license_name LIKE ? COLLATE NOCASE")
            parameters.append(f"%{license_name.strip()}%")
        if favorites_only:
            clauses.append("s.favorite=1")
        joins = ""
        join_parameters: list[object] = []
        if collection_id:
            joins += " JOIN collection_items ci ON ci.sound_id=s.sound_id AND ci.collection_id=?"
            join_parameters.append(collection_id)
        usage_order = ""
        if project_id:
            joins += (
                " JOIN (SELECT sound_id, MAX(last_used_utc) AS last_used_utc, SUM(use_count) AS use_count "
                "FROM project_usage WHERE project_id=? GROUP BY sound_id) pu ON pu.sound_id=s.sound_id"
            )
            join_parameters.append(project_id)
            usage_order = "pu.last_used_utc DESC, "
        elif recently_used:
            joins += (
                " JOIN (SELECT sound_id, MAX(last_used_utc) AS last_used_utc, SUM(use_count) AS use_count "
                "FROM project_usage GROUP BY sound_id) pu ON pu.sound_id=s.sound_id"
            )
            usage_order = "pu.last_used_utc DESC, "
        where = f"WHERE {' AND '.join(clauses)}" if clauses else ""
        with self._connection() as connection:
            rows = connection.execute(
                f"""
                SELECT s.* FROM sounds s {joins} {where}
                ORDER BY {usage_order}s.favorite DESC, s.indexed_utc DESC, s.canonical_path COLLATE NOCASE
                LIMIT ?
                """,
                [*join_parameters, *parameters, MAX_CANDIDATES],
            ).fetchall()
            providers = {
                str(row[0])
                for row in connection.execute("SELECT DISTINCT provider FROM embeddings").fetchall()
            }

        normalized_query = query.strip()
        provider: str | None = None
        query_vector: list[float] | None = None
        warning: str | None = None
        if semantic and rows and (normalized_query or similar_to_sound_id):
            if similar_to_sound_id:
                provider, query_vector = self._load_similar_vector(similar_to_sound_id, providers)
            elif CLAP_EMBEDDING_PROVIDER in providers:
                try:
                    query_vector = _clap_text_embedding(normalized_query)
                    provider = CLAP_EMBEDDING_PROVIDER
                except Exception:
                    warning = "CLAP is unavailable; using deterministic lexical/hash fallback."
            if query_vector is None:
                provider = HASH_EMBEDDING_PROVIDER if HASH_EMBEDDING_PROVIDER in providers else None
                query_vector = _hash_embedding(normalized_query) if provider else None

        vectors: dict[str, list[float]] = {}
        if provider and query_vector:
            ids = [str(row["sound_id"]) for row in rows]
            vectors = self._load_vectors(ids, provider)
        scored: list[SoundSearchResult] = []
        for row in rows:
            lexical_score = _lexical_score(normalized_query, row)
            semantic_score = 0.0
            vector = vectors.get(str(row["sound_id"]))
            if query_vector is not None and vector:
                semantic_score = max(0.0, min(1.0, _cosine(query_vector, vector)))
            if normalized_query and not vector and lexical_score <= 0:
                continue
            if query_vector is not None and vector:
                score = semantic_score * 0.72 + lexical_score * 0.28
            else:
                score = lexical_score
            if not normalized_query and not similar_to_sound_id:
                score = 0.25 + (0.15 if bool(row["favorite"]) else 0.0)
            if (normalized_query or similar_to_sound_id) and score <= 0:
                continue
            scored.append(self._row_to_search_result(
                row,
                score=max(0.0, min(1.0, score)),
                lexical_score=lexical_score,
                semantic_score=semantic_score,
                provider=provider if vector else None,
            ))
        scored.sort(key=lambda item: (-item.score, item.name.casefold(), item.sound_id))
        mode = "semantic" if provider and query_vector else "lexical"
        return SoundSearchResponse(
            results=scored[:limit],
            mode=mode,
            embedding_provider=provider,
            semantic_available=bool(provider and query_vector),
            warning=warning,
        )

    def _load_similar_vector(
        self,
        sound_id: str,
        providers: set[str],
    ) -> tuple[str | None, list[float] | None]:
        provider_order = [CLAP_EMBEDDING_PROVIDER, HASH_EMBEDDING_PROVIDER]
        with self._connection() as connection:
            for provider in provider_order:
                if provider not in providers:
                    continue
                row = connection.execute(
                    "SELECT vector_blob, dimensions FROM embeddings WHERE sound_id=? AND provider=?",
                    (sound_id, provider),
                ).fetchone()
                if row:
                    return provider, _blob_to_vector(row["vector_blob"], int(row["dimensions"]))
        return None, None

    def _load_vectors(self, sound_ids: list[str], provider: str) -> dict[str, list[float]]:
        if not sound_ids:
            return {}
        placeholders = ",".join("?" for _ in sound_ids)
        with self._connection() as connection:
            rows = connection.execute(
                f"SELECT sound_id, vector_blob, dimensions FROM embeddings WHERE provider=? AND sound_id IN ({placeholders})",
                [provider, *sound_ids],
            ).fetchall()
        return {
            str(row["sound_id"]): _blob_to_vector(row["vector_blob"], int(row["dimensions"]))
            for row in rows
        }

    @staticmethod
    def _row_to_search_result(
        row: sqlite3.Row,
        *,
        score: float,
        lexical_score: float,
        semantic_score: float,
        provider: str | None,
    ) -> SoundSearchResult:
        return SoundSearchResult(
            sound_id=str(row["sound_id"]),
            name=Path(str(row["canonical_path"])).name,
            path=str(row["canonical_path"]),
            content_hash=str(row["content_hash"]),
            duration=float(row["duration"]),
            codec=str(row["codec"]),
            channels=int(row["channels"]) if row["channels"] is not None else None,
            sample_rate=int(row["sample_rate"]) if row["sample_rate"] is not None else None,
            lufs=float(row["lufs"]) if row["lufs"] is not None else None,
            peak_db=float(row["peak_db"]) if row["peak_db"] is not None else None,
            leading_silence=float(row["leading_silence"]) if row["leading_silence"] is not None else None,
            trailing_silence=float(row["trailing_silence"]) if row["trailing_silence"] is not None else None,
            category=str(row["category"]),
            mood=str(row["mood"]),
            tempo_bpm=float(row["tempo_bpm"]) if row["tempo_bpm"] is not None else None,
            license_name=str(row["license_name"]),
            attribution=str(row["attribution"]),
            requires_attribution=bool(row["requires_attribution"]),
            favorite=bool(row["favorite"]),
            offline=bool(row["offline"]),
            tags=json.loads(row["tags_json"] or "[]"),
            derivative_path=str(row["derivative_path"]) if row["derivative_path"] else None,
            waveform_path=str(row["waveform_path"]) if row["waveform_path"] else None,
            completed_features=json.loads(row["completed_features_json"] or "[]"),
            score=score,
            lexical_score=lexical_score,
            semantic_score=semantic_score,
            embedding_provider=provider,
            indexed_utc=str(row["indexed_utc"]),
        )

    def get_sound(
        self,
        *,
        sound_id: str | None = None,
        path: Path | str | None = None,
    ) -> SoundSearchResult | None:
        if not sound_id and path is None:
            raise ValueError("sound_id or path is required")
        with self._connection() as connection:
            if sound_id:
                row = connection.execute(
                    "SELECT * FROM sounds WHERE sound_id=?",
                    (sound_id,),
                ).fetchone()
            else:
                canonical = _canonical_local_file(path or "")
                row = connection.execute(
                    "SELECT * FROM sounds WHERE canonical_path=? COLLATE NOCASE",
                    (str(canonical),),
                ).fetchone()
        if row is None:
            return None
        return self._row_to_search_result(
            row,
            score=1.0,
            lexical_score=1.0,
            semantic_score=0.0,
            provider=None,
        )

    def set_favorite(self, sound_id: str, favorite: bool) -> None:
        with self._connection() as connection:
            cursor = connection.execute(
                "UPDATE sounds SET favorite=? WHERE sound_id=?",
                (1 if favorite else 0, sound_id),
            )
            if cursor.rowcount == 0:
                raise ValueError("Sound was not found")

    def update_license(
        self,
        sound_id: str,
        *,
        license_name: str,
        attribution: str = "",
        requires_attribution: bool = False,
    ) -> None:
        with self._connection() as connection:
            cursor = connection.execute(
                "UPDATE sounds SET license_name=?, attribution=?, requires_attribution=? WHERE sound_id=?",
                (license_name.strip(), attribution.strip(), 1 if requires_attribution else 0, sound_id),
            )
            if cursor.rowcount == 0:
                raise ValueError("Sound was not found")
            row = connection.execute("SELECT * FROM sounds WHERE sound_id=?", (sound_id,)).fetchone()
            if row:
                connection.execute("DELETE FROM sounds_fts WHERE sound_id=?", (sound_id,))
                connection.execute(
                    "INSERT INTO sounds_fts(sound_id, name, path, category, mood, tags, license_name, attribution) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    (
                        sound_id,
                        Path(str(row["canonical_path"])).name,
                        str(row["canonical_path"]),
                        str(row["category"]),
                        str(row["mood"]),
                        " ".join(json.loads(row["tags_json"] or "[]")),
                        license_name.strip(),
                        attribution.strip(),
                    ),
                )

    def list_collections(self, project_id: str | None = None) -> list[SoundLibraryCollection]:
        scope = (project_id or "").strip()
        with self._connection() as connection:
            rows = connection.execute(
                """
                SELECT c.collection_id, c.name, c.scope_project_id, c.created_utc,
                       COUNT(ci.sound_id) AS item_count
                FROM collections c
                LEFT JOIN collection_items ci ON ci.collection_id=c.collection_id
                WHERE c.scope_project_id IN ('', ?)
                GROUP BY c.collection_id, c.name, c.scope_project_id, c.created_utc
                ORDER BY CASE WHEN c.scope_project_id='' THEN 0 ELSE 1 END, c.name COLLATE NOCASE
                """,
                (scope,),
            ).fetchall()
        return [
            SoundLibraryCollection(
                collection_id=str(row["collection_id"]),
                name=str(row["name"]),
                project_id=str(row["scope_project_id"]),
                created_utc=str(row["created_utc"]),
                item_count=int(row["item_count"] or 0),
            )
            for row in rows
        ]

    def create_collection(self, name: str, project_id: str | None = None) -> str:
        normalized = name.strip()
        if not normalized:
            raise ValueError("Collection name is required")
        scope = (project_id or "").strip()
        collection_id = str(uuid.uuid4())
        with self._connection() as connection:
            connection.execute(
                "INSERT INTO collections(collection_id, scope_project_id, name, created_utc) VALUES (?, ?, ?, ?)",
                (collection_id, scope, normalized, _utc_now()),
            )
        return collection_id

    def delete_collection(self, collection_id: str) -> None:
        with self._connection() as connection:
            cursor = connection.execute(
                "DELETE FROM collections WHERE collection_id=?",
                (collection_id,),
            )
            if cursor.rowcount == 0:
                raise ValueError("Collection was not found")

    def add_to_collection(self, collection_id: str, sound_id: str) -> None:
        with self._connection() as connection:
            connection.execute(
                "INSERT OR IGNORE INTO collection_items(collection_id, sound_id, added_utc) VALUES (?, ?, ?)",
                (collection_id, sound_id, _utc_now()),
            )

    def remove_from_collection(self, collection_id: str, sound_id: str) -> None:
        with self._connection() as connection:
            connection.execute(
                "DELETE FROM collection_items WHERE collection_id=? AND sound_id=?",
                (collection_id, sound_id),
            )

    def record_project_usage(self, project_id: str, media_asset_id: str, sound_id: str) -> None:
        with self._connection() as connection:
            connection.execute(
                """
                INSERT INTO project_usage(project_id, media_asset_id, sound_id, last_used_utc, use_count)
                VALUES (?, ?, ?, ?, 1)
                ON CONFLICT(project_id, media_asset_id, sound_id) DO UPDATE SET
                    last_used_utc=excluded.last_used_utc,
                    use_count=project_usage.use_count + 1
                """,
                (project_id, media_asset_id, sound_id, _utc_now()),
            )

    def refresh_offline_state(self) -> None:
        with self._connection() as connection:
            rows = connection.execute("SELECT sound_id, canonical_path, offline FROM sounds").fetchall()
            for row in rows:
                offline = 0 if Path(str(row["canonical_path"])).is_file() else 1
                if offline != int(row["offline"]):
                    connection.execute(
                        "UPDATE sounds SET offline=? WHERE sound_id=?",
                        (offline, str(row["sound_id"])),
                    )

    def _mark_missing_under_root_offline(self, root: Path) -> None:
        with self._connection() as connection:
            rows = connection.execute(
                "SELECT sound_id, canonical_path FROM sounds WHERE source_root_id=(SELECT root_id FROM library_roots WHERE canonical_path=? COLLATE NOCASE)",
                (str(root),),
            ).fetchall()
            for row in rows:
                if not Path(str(row["canonical_path"])).is_file():
                    connection.execute(
                        "UPDATE sounds SET offline=1 WHERE sound_id=?",
                        (str(row["sound_id"]),),
                    )

    @staticmethod
    def _row_to_root(row: sqlite3.Row) -> SoundLibraryRoot:
        return SoundLibraryRoot(
            root_id=str(row["root_id"]),
            path=str(row["canonical_path"]),
            watch_enabled=bool(row["watch_enabled"]),
            added_utc=str(row["added_utc"]),
        )


def default_catalog_path() -> Path:
    configured = os.environ.get("RUSHFRAME_SOUND_LIBRARY_CATALOG", "").strip()
    if configured:
        return Path(configured).expanduser().resolve()
    local_app_data = os.environ.get("LOCALAPPDATA", "").strip()
    if local_app_data:
        return Path(local_app_data) / "Rushframe" / "SoundLibrary" / "catalog.sqlite"
    return Path.home() / ".rushframe" / "SoundLibrary" / "catalog.sqlite"


def serialize(value: Any) -> Any:
    if hasattr(value, "__dataclass_fields__"):
        return {key: serialize(item) for key, item in asdict(value).items()}
    if isinstance(value, list):
        return [serialize(item) for item in value]
    if isinstance(value, dict):
        return {key: serialize(item) for key, item in value.items()}
    return value


def _canonical_local_directory(value: Path | str) -> Path:
    path = Path(value).expanduser()
    if _looks_like_unc(path):
        raise ValueError("Network and UNC sound-library roots are not supported")
    resolved = path.resolve(strict=True)
    if not resolved.is_dir():
        raise ValueError("Sound-library root must be an existing directory")
    return resolved


def _canonical_local_file(value: Path | str) -> Path:
    path = Path(value).expanduser()
    if _looks_like_unc(path):
        raise ValueError("Network and UNC sound files are not supported")
    resolved = path.resolve(strict=True)
    if not resolved.is_file():
        raise ValueError("Sound path must be an existing file")
    return resolved


def _looks_like_unc(path: Path) -> bool:
    text = str(path)
    return text.startswith("\\\\") or text.startswith("//")


def _is_within(path: Path, root: Path) -> bool:
    try:
        canonical_path = path.resolve(strict=True)
        canonical_root = root.resolve(strict=True)
        canonical_path.relative_to(canonical_root)
        return True
    except (OSError, ValueError):
        return False


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _probe_audio(path: Path, ffprobe: str | None) -> dict[str, Any]:
    executable = _resolve_executable(ffprobe, "ffprobe")
    if executable:
        completed = subprocess.run(
            [
                executable,
                "-v", "error",
                "-show_entries", "format=duration:stream=index,codec_type,codec_name,channels,sample_rate",
                "-of", "json",
                str(path),
            ],
            capture_output=True,
            text=True,
            timeout=60,
            check=False,
        )
        if completed.returncode == 0:
            payload = json.loads(completed.stdout or "{}")
            streams = payload.get("streams") or []
            audio_streams = [stream for stream in streams if stream.get("codec_type") == "audio"]
            video_streams = [stream for stream in streams if stream.get("codec_type") == "video"]
            if not audio_streams or video_streams:
                raise ValueError("Sound library accepts audio-only files")
            stream = audio_streams[0]
            duration = float((payload.get("format") or {}).get("duration") or 0.0)
            if duration <= 0:
                raise ValueError("Audio duration could not be determined")
            return {
                "duration": duration,
                "codec": str(stream.get("codec_name") or "unknown"),
                "channels": int(stream["channels"]) if stream.get("channels") else None,
                "sample_rate": int(stream["sample_rate"]) if stream.get("sample_rate") else None,
            }
        raise ValueError((completed.stderr or "FFprobe could not read the audio file").strip())
    if path.suffix.lower() == ".wav":
        with wave.open(str(path), "rb") as source:
            frames = source.getnframes()
            rate = source.getframerate()
            duration = frames / rate if rate else 0.0
            if duration <= 0:
                raise ValueError("Audio duration could not be determined")
            return {
                "duration": duration,
                "codec": f"pcm-{source.getsampwidth() * 8}",
                "channels": source.getnchannels(),
                "sample_rate": rate,
            }
    raise ValueError("FFprobe is required to index this audio format")


def _measure_audio(
    ffmpeg: str | None,
    path: Path,
    duration: float,
) -> tuple[float | None, float | None, float | None, float | None, bool]:
    executable = _resolve_executable(ffmpeg, "ffmpeg")
    if not executable:
        return None, None, None, None, False
    completed = subprocess.run(
        [
            executable,
            "-hide_banner", "-nostats", "-i", str(path),
            "-af", "ebur128=peak=true,silencedetect=noise=-45dB:d=0.05",
            "-f", "null", "-",
        ],
        capture_output=True,
        text=True,
        timeout=max(60, min(600, int(duration * 2 + 30))),
        check=False,
    )
    diagnostics = completed.stderr or ""
    lufs_matches = re.findall(r"\bI:\s*(-?(?:inf|\d+(?:\.\d+)?))\s*LUFS", diagnostics, flags=re.IGNORECASE)
    peak_matches = re.findall(r"\bPeak:\s*(-?(?:inf|\d+(?:\.\d+)?))\s*dBFS", diagnostics, flags=re.IGNORECASE)
    lufs = _parse_finite(lufs_matches[-1]) if lufs_matches else None
    peak = _parse_finite(peak_matches[-1]) if peak_matches else None
    silence_starts = [float(value) for value in re.findall(r"silence_start:\s*([\d.]+)", diagnostics)]
    silence_ends = [float(value) for value in re.findall(r"silence_end:\s*([\d.]+)", diagnostics)]
    leading = silence_ends[0] if silence_starts and silence_starts[0] <= 0.01 and silence_ends else 0.0
    trailing = 0.0
    if silence_starts:
        last_start = silence_starts[-1]
        last_end = silence_ends[-1] if len(silence_ends) >= len(silence_starts) else duration
        if last_end >= duration - 0.05:
            trailing = max(0.0, duration - last_start)
    return lufs, peak, leading, trailing, completed.returncode == 0


def _parse_finite(value: str) -> float | None:
    try:
        parsed = float(value)
        return parsed if math.isfinite(parsed) else None
    except ValueError:
        return None


def _essentia_is_available() -> bool:
    try:
        import essentia.standard  # noqa: F401
        return True
    except ImportError:
        return False


def _measure_tempo_essentia(path: Path) -> float | None:
    import essentia.standard as es

    audio = es.MonoLoader(filename=str(path), sampleRate=44100)()
    bpm, _beats, _confidence, _estimates, _intervals = es.RhythmExtractor2013(method="multifeature")(audio)
    value = float(bpm)
    return value if math.isfinite(value) and value > 0 else None


def _classify_filename(path: Path) -> tuple[str, str, list[str]]:
    text = re.sub(r"[_\-.]+", " ", path.stem.lower())
    tokens = set(re.findall(r"[a-z0-9]+", text))
    categories = [
        ("transition", {"whoosh", "swoosh", "transition", "riser", "sweep", "swish"}),
        ("impact", {"impact", "hit", "slam", "boom", "thud", "punch", "drop"}),
        ("ambience", {"ambience", "ambient", "roomtone", "atmosphere", "background"}),
        ("music", {"music", "song", "beat", "loop", "stinger", "jingle", "score"}),
        ("voice", {"voice", "speech", "dialogue", "vocal", "narration"}),
        ("ui", {"ui", "click", "notification", "beep", "menu", "interface"}),
        ("animal", {"animal", "dog", "cat", "bird", "roar", "bark"}),
        ("nature", {"rain", "wind", "water", "forest", "thunder", "ocean"}),
        ("mechanical", {"engine", "machine", "motor", "gear", "metal", "mechanical"}),
    ]
    category = next((name for name, keywords in categories if tokens & keywords), "other")
    moods = [
        ("tense", {"tense", "dark", "suspense", "horror", "ominous"}),
        ("energetic", {"energetic", "fast", "upbeat", "power", "action"}),
        ("calm", {"calm", "soft", "peaceful", "gentle", "relax"}),
        ("happy", {"happy", "bright", "fun", "cheerful", "positive"}),
        ("sad", {"sad", "melancholy", "emotional", "somber"}),
        ("dramatic", {"dramatic", "cinematic", "epic", "trailer"}),
    ]
    mood = next((name for name, keywords in moods if tokens & keywords), "neutral")
    tags = sorted(tokens | {category, mood})
    return category, mood, tags


def _configured_clap_checkpoint() -> Path | None:
    configured = os.environ.get("RUSHFRAME_CLAP_CHECKPOINT", "").strip()
    if not configured:
        local_app_data = os.environ.get("LOCALAPPDATA", "").strip()
        if not local_app_data:
            return None
        configured = str(
            Path(local_app_data)
            / "Rushframe"
            / "Models"
            / "clap"
            / "music_audioset_epoch_15_esc_90.14.pt"
        )
    checkpoint = Path(configured).expanduser()
    try:
        resolved = checkpoint.resolve(strict=True)
    except OSError:
        return None
    return resolved if resolved.is_file() else None


def _clap_is_available() -> bool:
    if _configured_clap_checkpoint() is None:
        return False
    try:
        original_argv = sys.argv
        try:
            sys.argv = original_argv[:1]
            import laion_clap  # noqa: F401
        finally:
            sys.argv = original_argv
        import numpy  # noqa: F401
        return True
    except ImportError:
        return False


def _clap_audio_embedding(path: Path) -> list[float]:
    checkpoint = _configured_clap_checkpoint()
    if checkpoint is None:
        raise RuntimeError("A local RUSHFRAME_CLAP_CHECKPOINT is required for CLAP embeddings")
    cached = _get_clap_model(str(checkpoint))
    with cached.lock:
        values = cached.model.get_audio_embedding_from_filelist([str(path)])[0]
    return _normalize_vector(values)


def _clap_text_embedding(text: str) -> list[float]:
    checkpoint = _configured_clap_checkpoint()
    if checkpoint is None:
        raise RuntimeError("A local RUSHFRAME_CLAP_CHECKPOINT is required for CLAP embeddings")
    cached = _get_clap_model(str(checkpoint))
    with cached.lock:
        values = cached.model.get_text_embedding([text])[0]
    return _normalize_vector(values)


def _normalize_vector(values: Iterable[Any]) -> list[float]:
    vector = [float(value) for value in values]
    norm = math.sqrt(sum(value * value for value in vector))
    return [value / norm for value in vector] if norm else vector


def _hash_embedding(text: str, dimensions: int = 384) -> list[float]:
    tokens = re.findall(r"[\w']+", text.lower(), flags=re.UNICODE)
    features = [*tokens, *(f"{left}_{right}" for left, right in zip(tokens, tokens[1:]))]
    vector = [0.0] * dimensions
    for feature in features:
        digest = hashlib.blake2b(feature.encode("utf-8"), digest_size=8).digest()
        index = int.from_bytes(digest[:4], "little") % dimensions
        sign = 1.0 if digest[4] & 1 else -1.0
        vector[index] += sign
    return _normalize_vector(vector)


def _lexical_score(query: str, row: sqlite3.Row) -> float:
    if not query:
        return 0.0
    tokens = set(re.findall(r"[\w']+", query.casefold(), flags=re.UNICODE))
    if not tokens:
        return 0.0
    name = Path(str(row["canonical_path"])).stem.casefold()
    category = str(row["category"]).casefold()
    mood = str(row["mood"]).casefold()
    tags = {str(value).casefold() for value in json.loads(row["tags_json"] or "[]")}
    path = str(row["canonical_path"]).casefold()
    score = 0.0
    for token in tokens:
        if token in name:
            score += 0.45
        if token == category:
            score += 0.25
        if token == mood:
            score += 0.18
        if token in tags:
            score += 0.18
        if token in path:
            score += 0.08
    phrase = query.casefold()
    if phrase in name:
        score += 0.25
    return max(0.0, min(1.0, score / max(1, len(tokens))))


def _vector_to_blob(vector: Sequence[float]) -> bytes:
    return array("f", (float(value) for value in vector)).tobytes()


def _blob_to_vector(blob: bytes, dimensions: int) -> list[float]:
    values = array("f")
    values.frombytes(blob)
    if len(values) != dimensions:
        return []
    return values.tolist()


def _cosine(left: Sequence[float], right: Sequence[float]) -> float:
    if len(left) != len(right) or not left:
        return 0.0
    numerator = sum(a * b for a, b in zip(left, right))
    left_norm = math.sqrt(sum(value * value for value in left))
    right_norm = math.sqrt(sum(value * value for value in right))
    if not left_norm or not right_norm:
        return 0.0
    return numerator / (left_norm * right_norm)


def _resolve_executable(configured: str | None, fallback: str) -> str | None:
    if configured:
        candidate = Path(configured)
        if candidate.is_file():
            return str(candidate.resolve())
        discovered = shutil.which(configured)
        if discovered:
            return discovered
    return shutil.which(fallback)


def _utc_now() -> str:
    return datetime.now(UTC).isoformat()
