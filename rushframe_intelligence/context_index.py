"""SQLite-backed retrieval index for agent-facing media context."""

from __future__ import annotations

import hashlib
import json
import math
import re
import sqlite3
from array import array
from functools import lru_cache
from threading import Lock
from typing import Any
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable

from rushframe_intelligence.models import EditingMoment


@dataclass(slots=True)
class SearchResult:
    moment_id: str
    start: float
    end: float
    summary: str
    score: float
    roles: list[str]
    tags: list[str]


class _CachedEmbeddingModel:
    def __init__(self, model: Any) -> None:
        self.model = model
        self.lock = Lock()


@lru_cache(maxsize=2)
def _get_embedding_model(model_name: str) -> _CachedEmbeddingModel:
    from sentence_transformers import SentenceTransformer

    return _CachedEmbeddingModel(SentenceTransformer(model_name))


class MediaContextIndex:
    def __init__(self, database_path: Path | str) -> None:
        self.database_path = Path(database_path)
        self.database_path.parent.mkdir(parents=True, exist_ok=True)

    def _connect(self) -> sqlite3.Connection:
        connection = sqlite3.connect(self.database_path)
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=NORMAL")
        return connection

    def rebuild(self, moments: Iterable[EditingMoment], *, build_embeddings: bool = False) -> None:
        moment_list = list(moments)
        with self._connect() as connection:
            connection.executescript(
                """
                DROP TABLE IF EXISTS moments;
                DROP TABLE IF EXISTS moments_fts;
                DROP TABLE IF EXISTS embeddings;
                DROP TABLE IF EXISTS embedding_meta;
                CREATE TABLE moments (
                    moment_id TEXT PRIMARY KEY,
                    start_seconds REAL NOT NULL,
                    end_seconds REAL NOT NULL,
                    summary TEXT NOT NULL,
                    speech TEXT,
                    visual TEXT,
                    audio TEXT,
                    roles_json TEXT NOT NULL,
                    tags_json TEXT NOT NULL,
                    overall_score REAL NOT NULL,
                    payload_json TEXT NOT NULL
                );
                CREATE VIRTUAL TABLE moments_fts USING fts5(
                    moment_id UNINDEXED,
                    summary,
                    speech,
                    visual,
                    audio,
                    roles,
                    tags,
                    tokenize='unicode61 remove_diacritics 2'
                );
                CREATE TABLE embeddings (
                    moment_id TEXT PRIMARY KEY,
                    vector_blob BLOB NOT NULL,
                    dimensions INTEGER NOT NULL,
                    FOREIGN KEY(moment_id) REFERENCES moments(moment_id) ON DELETE CASCADE
                );
                CREATE TABLE embedding_meta (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                """
            )
            for moment in moment_list:
                roles_json = json.dumps(moment.editing_roles, ensure_ascii=False)
                tags_json = json.dumps(moment.tags, ensure_ascii=False)
                connection.execute(
                    """
                    INSERT INTO moments VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        moment.moment_id,
                        moment.start,
                        moment.end,
                        moment.summary,
                        moment.speech,
                        moment.visual,
                        moment.audio,
                        roles_json,
                        tags_json,
                        moment.scores.overall,
                        json.dumps(asdict(moment), ensure_ascii=False),
                    ),
                )
                connection.execute(
                    "INSERT INTO moments_fts VALUES (?, ?, ?, ?, ?, ?, ?)",
                    (
                        moment.moment_id,
                        moment.summary,
                        moment.speech or "",
                        moment.visual or "",
                        moment.audio or "",
                        " ".join(moment.editing_roles),
                        " ".join(moment.tags),
                    ),
                )
            if build_embeddings and moment_list:
                self._build_embeddings(connection, moment_list)

    @staticmethod
    def _build_embeddings(connection: sqlite3.Connection, moments: list[EditingMoment]) -> None:
        texts = [
            "\n".join(filter(None, [moment.summary, moment.speech, moment.visual, " ".join(moment.tags)]))
            for moment in moments
        ]
        provider = "builtin-hash-v1"
        try:
            cached_model = _get_embedding_model("all-MiniLM-L6-v2")
            with cached_model.lock:
                vectors = cached_model.model.encode(
                    texts,
                    normalize_embeddings=True,
                    show_progress_bar=False,
                )
            provider = "sentence-transformers/all-MiniLM-L6-v2"
        except Exception:
            vectors = [_hash_embedding(text) for text in texts]

        connection.execute(
            "INSERT INTO embedding_meta(key, value) VALUES ('provider', ?)",
            (provider,),
        )
        connection.executemany(
            "INSERT INTO embeddings(moment_id, vector_blob, dimensions) VALUES (?, ?, ?)",
            [
                (
                    moment.moment_id,
                    sqlite3.Binary(_vector_to_blob(vector)),
                    len(vector),
                )
                for moment, vector in zip(moments, vectors)
            ],
        )

    def search(
        self,
        query: str,
        *,
        limit: int = 12,
        roles: list[str] | None = None,
        min_overall_score: float = 0.0,
        max_duration: float | None = None,
        semantic: bool = True,
    ) -> list[SearchResult]:
        normalized_query = query.strip()
        if not normalized_query:
            return self.top_moments(limit=limit, roles=roles, min_overall_score=min_overall_score)

        lexical = self._search_fts(
            normalized_query,
            limit=max(limit * 3, 30),
            roles=roles,
            min_overall_score=min_overall_score,
            max_duration=max_duration,
        )
        if not semantic:
            return lexical[:limit]
        semantic_results = self._search_embeddings(
            normalized_query,
            limit=max(limit * 3, 30),
            roles=roles,
            min_overall_score=min_overall_score,
            max_duration=max_duration,
        )
        combined: dict[str, SearchResult] = {}
        for result in lexical:
            combined[result.moment_id] = result
        for result in semantic_results:
            existing = combined.get(result.moment_id)
            if existing:
                existing.score = min(1.0, existing.score * 0.55 + result.score * 0.65)
            else:
                combined[result.moment_id] = result
        return sorted(combined.values(), key=lambda item: item.score, reverse=True)[:limit]

    def _search_fts(
        self,
        query: str,
        *,
        limit: int,
        roles: list[str] | None,
        min_overall_score: float,
        max_duration: float | None,
    ) -> list[SearchResult]:
        safe_query = " OR ".join(
            f'"{token.replace(chr(34), "")}"'
            for token in query.split()
            if token.strip()
        )
        if not safe_query:
            return []
        clauses = ["moments_fts MATCH ?", "m.overall_score >= ?"]
        parameters: list[object] = [safe_query, min_overall_score]
        if max_duration is not None:
            clauses.append("(m.end_seconds - m.start_seconds) <= ?")
            parameters.append(max_duration)
        parameters.append(limit)
        sql = f"""
            SELECT m.*, bm25(moments_fts, 0, 6, 5, 3, 2, 1, 1) AS rank
            FROM moments_fts
            JOIN moments m ON m.moment_id = moments_fts.moment_id
            WHERE {' AND '.join(clauses)}
            ORDER BY rank ASC, m.overall_score DESC
            LIMIT ?
        """
        with self._connect() as connection:
            rows = connection.execute(sql, parameters).fetchall()
        results = [self._row_to_result(row, score=_bm25_score(float(row["rank"]))) for row in rows]
        return _filter_roles(results, roles)

    def _search_embeddings(
        self,
        query: str,
        *,
        limit: int,
        roles: list[str] | None,
        min_overall_score: float,
        max_duration: float | None,
    ) -> list[SearchResult]:
        with self._connect() as connection:
            count = connection.execute("SELECT COUNT(*) FROM embeddings").fetchone()[0]
            if not count:
                return []
            provider_row = connection.execute(
                "SELECT value FROM embedding_meta WHERE key = 'provider'"
            ).fetchone()
            provider = str(provider_row[0]) if provider_row else "builtin-hash-v1"
            if provider.startswith("sentence-transformers/"):
                try:
                    cached_model = _get_embedding_model(provider.split("/", 1)[1])
                except ImportError:
                    return []
                with cached_model.lock:
                    query_vector = [
                        float(value)
                        for value in cached_model.model.encode([query], normalize_embeddings=True)[0]
                    ]
            else:
                query_vector = _hash_embedding(query)
            clauses = ["m.overall_score >= ?"]
            parameters: list[object] = [min_overall_score]
            if max_duration is not None:
                clauses.append("(m.end_seconds - m.start_seconds) <= ?")
                parameters.append(max_duration)
            candidate_limit = max(limit * 20, 200)
            parameters.append(candidate_limit)
            rows = connection.execute(
                f"""
                SELECT m.*, e.vector_blob, e.dimensions
                FROM moments m JOIN embeddings e ON e.moment_id = m.moment_id
                WHERE {' AND '.join(clauses)}
                ORDER BY m.overall_score DESC
                LIMIT ?
                """,
                parameters,
            ).fetchall()
        results = [
            self._row_to_result(
                row,
                score=max(
                    0.0,
                    _cosine(
                        query_vector,
                        _blob_to_vector(row["vector_blob"], int(row["dimensions"])),
                    ),
                ),
            )
            for row in rows
        ]
        meaningful = [result for result in results if result.score >= 0.05]
        return _filter_roles(sorted(meaningful, key=lambda item: item.score, reverse=True), roles)[:limit]

    def top_moments(
        self,
        *,
        limit: int = 12,
        roles: list[str] | None = None,
        min_overall_score: float = 0.0,
    ) -> list[SearchResult]:
        with self._connect() as connection:
            rows = connection.execute(
                "SELECT * FROM moments WHERE overall_score >= ? ORDER BY overall_score DESC LIMIT ?",
                (min_overall_score, max(limit * 3, 30)),
            ).fetchall()
        results = [self._row_to_result(row, score=float(row["overall_score"])) for row in rows]
        return _filter_roles(results, roles)[:limit]

    @staticmethod
    def _row_to_result(row: sqlite3.Row, *, score: float) -> SearchResult:
        return SearchResult(
            moment_id=str(row["moment_id"]),
            start=float(row["start_seconds"]),
            end=float(row["end_seconds"]),
            summary=str(row["summary"]),
            score=max(0.0, min(1.0, score)),
            roles=json.loads(row["roles_json"]),
            tags=json.loads(row["tags_json"]),
        )


def _filter_roles(results: list[SearchResult], roles: list[str] | None) -> list[SearchResult]:
    if not roles:
        return results
    required = {role.lower() for role in roles}
    return [result for result in results if required.intersection(role.lower() for role in result.roles)]


def _bm25_score(rank: float) -> float:
    return 1.0 / (1.0 + max(0.0, rank)) if rank >= 0 else 1.0 / (1.0 + abs(rank) * 0.15)


def _hash_embedding(text: str, dimensions: int = 384) -> list[float]:
    tokens = re.findall(r"[\w']+", text.lower(), flags=re.UNICODE)
    features = [*tokens, *(f"{left}_{right}" for left, right in zip(tokens, tokens[1:]))]
    vector = [0.0] * dimensions
    for feature in features:
        digest = hashlib.blake2b(feature.encode("utf-8"), digest_size=8).digest()
        index = int.from_bytes(digest[:4], "little") % dimensions
        sign = 1.0 if digest[4] & 1 else -1.0
        vector[index] += sign
    norm = math.sqrt(sum(value * value for value in vector))
    return [value / norm for value in vector] if norm else vector


def _vector_to_blob(vector: Any) -> bytes:
    values = array("f", (float(value) for value in vector))
    return values.tobytes()


def _blob_to_vector(blob: bytes, dimensions: int) -> list[float]:
    values = array("f")
    values.frombytes(blob)
    if len(values) != dimensions:
        return []
    return values.tolist()


def _cosine(left: list[float], right: list[float]) -> float:
    if len(left) != len(right) or not left:
        return 0.0
    numerator = sum(a * b for a, b in zip(left, right))
    left_norm_squared = sum(value * value for value in left)
    right_norm_squared = sum(value * value for value in right)
    if not left_norm_squared or not right_norm_squared:
        return 0.0
    if abs(left_norm_squared - 1.0) < 0.001 and abs(right_norm_squared - 1.0) < 0.001:
        return numerator
    return numerator / math.sqrt(left_norm_squared * right_norm_squared)
