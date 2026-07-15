"""Local HTTP and MCP backend for Rushframe intelligence."""

from __future__ import annotations

import json
import os
import secrets
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import BoundedSemaphore
from typing import Any
from urllib.parse import parse_qs, urlparse
from urllib.request import Request, urlopen

from rushframe_intelligence.capabilities import discover_capabilities
from rushframe_intelligence.sound_library import SoundLibraryCatalog, default_catalog_path

PROTOCOL_VERSION = "2025-06-18"
MAX_REQUEST_BYTES = 1024 * 1024
DEFAULT_EDITOR_BRIDGE = "http://127.0.0.1:7320"
EDITOR_SESSION_TOKEN = os.environ.get("RUSHFRAME_EDITOR_SESSION_TOKEN", "").strip()
MAX_BACKEND_THREADS = max(2, min(16, int(os.environ.get("RUSHFRAME_BACKEND_MAX_THREADS", "6"))))
REQUEST_IO_TIMEOUT_SECONDS = max(5, min(120, int(os.environ.get("RUSHFRAME_REQUEST_IO_TIMEOUT_SECONDS", "30"))))
BRIDGE_OPERATION_TIMEOUT_SECONDS = max(
    30,
    min(7200, int(os.environ.get("RUSHFRAME_BRIDGE_TIMEOUT_SECONDS", "1800"))),
)


def _edit_plan_input_schema(*, include_review_options: bool = False) -> dict[str, Any]:
    properties: dict[str, Any] = {
        "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
        "base_revision": {"type": "integer", "minimum": 0},
        "plan_id": {"type": "string"},
        "summary": {"type": "string"},
        "prompt_id": {"type": "string"},
        "prompt_version": {"type": "string"},
        "creative_plan": {"type": "object"},
        "operations": {"type": "array", "items": {"type": "object"}, "minItems": 1, "maxItems": 100},
    }
    if include_review_options:
        properties.update({
            "render_draft": {"type": "boolean", "default": True},
            "review_width": {"type": "integer", "minimum": 320, "maximum": 1920, "default": 960},
        })
    return {
        "type": "object",
        "properties": properties,
        "required": ["base_revision", "operations"],
        "additionalProperties": False,
    }


TOOLS = [
    {
        "name": "rushframe.capabilities",
        "description": "Report installed intelligence capabilities and live editor actions when Rushframe is open.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.search_moments",
        "description": "Search an analyzed media context index for useful editing moments.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "media_asset_id": {"type": "string", "description": "Registered media asset ID from the open Rushframe project."},
                "query": {"type": "string"},
                "limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 12},
                "roles": {"type": "array", "items": {"type": "string"}},
                "min_score": {"type": "number", "minimum": 0, "maximum": 1, "default": 0},
                "max_duration": {"type": "number", "exclusiveMinimum": 0},
            },
            "required": ["query"],
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.search_sfx",
        "description": "Search user-approved local Rushframe sound-library roots. Results are read-only; only results already registered in the open project can be used in timeline edits.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "query": {"type": "string", "default": ""},
                "limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 20},
                "max_duration": {"type": "number", "exclusiveMinimum": 0},
                "min_lufs": {"type": "number"},
                "max_lufs": {"type": "number"},
                "min_tempo": {"type": "number", "minimum": 0},
                "max_tempo": {"type": "number", "minimum": 0},
                "category": {"type": "string"},
                "mood": {"type": "string"},
                "license": {"type": "string"},
                "favorites_only": {"type": "boolean", "default": False},
                "include_offline": {"type": "boolean", "default": False},
                "lexical_only": {"type": "boolean", "default": False},
                "similar_to_sound_id": {"type": "string"},
                "recently_used": {"type": "boolean", "default": False},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.get_agent_context",
        "description": "Build a compact media-analysis context bundle for one registered analyzed asset in the open project.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "media_asset_id": {"type": "string", "description": "Registered media asset ID from the open Rushframe project."},
                "query": {"type": "string", "default": ""},
                "limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 20},
                "roles": {"type": "array", "items": {"type": "string"}},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.get_editing_context",
        "description": "Read a bounded, path-safe editing snapshot with campaign intent, brief, tasks, playhead, selection, locks, media readiness, quality issues, and edit-skill categories. Use this before planning edits.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "media_asset_id": {"type": "string", "description": "Optional registered analyzed asset to include as focused media context."},
                "query": {"type": "string", "default": ""},
                "roles": {"type": "array", "items": {"type": "string"}},
                "item_limit": {"type": "integer", "minimum": 25, "maximum": 500, "default": 250},
                "media_asset_limit": {"type": "integer", "minimum": 1, "maximum": 500, "default": 200},
                "moment_limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 12},
                "min_score": {"type": "number", "minimum": 0, "maximum": 1, "default": 0},
                "include_completed_tasks": {"type": "boolean", "default": False},
                "include_media_context": {"type": "boolean", "default": True},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.get_timeline_state",
        "description": "Read the live Rushframe timeline while the desktop editor is open.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
            },
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.apply_timeline_edit",
        "description": "Preview or apply one approved live timeline edit. Read rushframe.get_editing_context and rushframe.capabilities first; prefer an edit plan for coordinated changes.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "base_revision": {"type": "integer", "minimum": 0},
                "action": {
                    "type": "string",
                    "description": "Editor action ID. Call rushframe.capabilities for the authoritative live action list.",
                },
                "preview_only": {"type": "boolean", "default": True},
                "track_id": {"type": "string"},
                "item_id": {"type": "string"},
                "media_asset_id": {"type": "string"},
                "left_item_id": {"type": "string"},
                "right_item_id": {"type": "string"},
                "effect_type_id": {"type": "string"},
                "parameters": {"type": "object"},
                "text": {"type": "string"},
                "kind": {"type": "string"},
                "start": {"type": "number"},
                "duration": {"type": "number"},
                "source_start": {"type": "number"},
                "time": {"type": "number"},
                "alignment": {"type": "number"},
                "font_size": {"type": "number"},
                "fill_color": {"type": "string"},
                "outline_color": {"type": "string"},
                "outline_width": {"type": "number"},
            },
            "required": ["action"],
            "additionalProperties": True,
        },
    },
    {
        "name": "rushframe.preview_edit_plan",
        "description": "Validate a coordinated edit plan against the current revision and return affected ranges, quality scores, warnings, and the approval preview without mutating the project.",
        "inputSchema": _edit_plan_input_schema(),
    },
    {
        "name": "rushframe.review_edit_plan",
        "description": "Apply an edit plan only to an isolated project snapshot, optionally render a low-resolution rough cut, and return quality issues for a corrective second pass. The live project is not modified.",
        "inputSchema": _edit_plan_input_schema(include_review_options=True),
    },
    {
        "name": "rushframe.apply_edit_plan",
        "description": "Show the plan for user approval, then apply all operations atomically as one undoable revision-safe edit. No operation is applied when validation or approval fails.",
        "inputSchema": _edit_plan_input_schema(),
    },
    {
        "name": "rushframe.render_timeline",
        "description": "Render/export the live desktop timeline after user approval.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "base_revision": {"type": "integer", "minimum": 0},
                "output_path": {"type": "string"},
            },
            "required": ["output_path"],
            "additionalProperties": False,
        },
    },
]


class BoundedThreadingHTTPServer(ThreadingHTTPServer):
    def __init__(self, *args: Any, max_workers: int = MAX_BACKEND_THREADS, **kwargs: Any) -> None:
        self._worker_gate = BoundedSemaphore(max_workers)
        super().__init__(*args, **kwargs)

    def process_request(self, request: Any, client_address: Any) -> None:
        self._worker_gate.acquire()
        try:
            super().process_request(request, client_address)
        except Exception:
            self._worker_gate.release()
            raise

    def process_request_thread(self, request: Any, client_address: Any) -> None:
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._worker_gate.release()


class RushframeBackendHandler(BaseHTTPRequestHandler):
    server_version = "RushframeIntelligence/2.0"

    def setup(self) -> None:
        super().setup()
        self.connection.settimeout(REQUEST_IO_TIMEOUT_SECONDS)

    def _write_json(self, payload: object, status: HTTPStatus = HTTPStatus.OK) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status.value)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def _write_empty(self, status: HTTPStatus) -> None:
        self.send_response(status.value)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        parsed = urlparse(self.path)
        query = parse_qs(parsed.query)
        try:
            if parsed.path == "/health":
                self._write_json({"status": "ok", "service": "rushframe-intelligence", "mcp": "/mcp", "sessionRequired": bool(EDITOR_SESSION_TOKEN)})
                return
            if EDITOR_SESSION_TOKEN and not _request_is_authorized(self.headers):
                self._write_json({"error": "invalid agent session"}, HTTPStatus.UNAUTHORIZED)
                return
            if parsed.path == "/capabilities":
                self._write_json(discover_capabilities())
                return
            if parsed.path == "/search":
                results = _bridge_post({
                    "media_asset_id": query.get("media_asset_id", [""])[0],
                    "query": _required(query, "q"),
                    "limit": int(query.get("limit", ["12"])[0]),
                    "roles": query.get("role", []),
                }, "search-context")
                self._write_json(results)
                return
            if parsed.path == "/sound-library/search":
                self._write_json(_search_sfx({
                    "query": query.get("q", [""])[0],
                    "limit": int(query.get("limit", ["20"])[0]),
                    "max_duration": _optional_float(query, "max_duration"),
                    "category": query.get("category", [""])[0],
                    "mood": query.get("mood", [""])[0],
                    "favorites_only": query.get("favorites_only", ["false"])[0].lower() == "true",
                    "lexical_only": query.get("lexical_only", ["false"])[0].lower() == "true",
                }))
                return
            if parsed.path == "/context":
                bundle = _bridge_post({
                    "media_asset_id": query.get("media_asset_id", [""])[0],
                    "query": query.get("q", [""])[0],
                    "limit": int(query.get("limit", ["20"])[0]),
                    "roles": query.get("role", []),
                }, "agent-context")
                self._write_json(bundle)
                return
            if parsed.path == "/mcp":
                self._write_json({"error": "Use POST for MCP JSON-RPC."}, HTTPStatus.METHOD_NOT_ALLOWED)
                return
            self._write_json({"error": "not_found"}, HTTPStatus.NOT_FOUND)
        except (ValueError, FileNotFoundError) as exc:
            self._write_json({"error": str(exc)}, HTTPStatus.BAD_REQUEST)
        except Exception as exc:
            self._write_json({"error": str(exc)}, HTTPStatus.INTERNAL_SERVER_ERROR)

    def do_POST(self) -> None:  # noqa: N802
        if urlparse(self.path).path != "/mcp":
            self._write_json({"error": "not_found"}, HTTPStatus.NOT_FOUND)
            return
        if EDITOR_SESSION_TOKEN and not _request_is_authorized(self.headers):
            self._write_json({"error": "invalid agent session"}, HTTPStatus.UNAUTHORIZED)
            return
        try:
            content_length = int(self.headers.get("Content-Length", "0"))
            if content_length <= 0 or content_length > MAX_REQUEST_BYTES:
                raise ValueError("Invalid MCP request size")
            request = json.loads(self.rfile.read(content_length))
            if not isinstance(request, dict):
                raise ValueError("MCP request must be a JSON object")
            response = _handle_mcp(request)
            if response is None:
                self._write_empty(HTTPStatus.ACCEPTED)
            else:
                self._write_json(response)
        except json.JSONDecodeError:
            self._write_json(_rpc_error(None, -32700, "Parse error"), HTTPStatus.BAD_REQUEST)
        except ValueError as exc:
            self._write_json(_rpc_error(None, -32600, str(exc)), HTTPStatus.BAD_REQUEST)
        except Exception as exc:
            self._write_json(_rpc_error(None, -32603, str(exc)), HTTPStatus.INTERNAL_SERVER_ERROR)

    def log_message(self, format: str, *args: object) -> None:
        return


def _handle_mcp(request: dict[str, Any]) -> dict[str, Any] | None:
    request_id = request.get("id")
    method = request.get("method")
    if request.get("jsonrpc") != "2.0" or not isinstance(method, str):
        return _rpc_error(request_id, -32600, "Invalid Request")
    if request_id is None:
        return None
    params = request.get("params") or {}
    try:
        if method == "initialize":
            return _rpc_result(request_id, {
                "protocolVersion": PROTOCOL_VERSION,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": "rushframe", "version": "0.1.0"},
                "instructions": "Use Rushframe tools to retrieve analyzed video context and editing moments.",
            })
        if method == "ping":
            return _rpc_result(request_id, {})
        if method == "tools/list":
            return _rpc_result(request_id, {"tools": TOOLS})
        if method == "tools/call":
            name = params.get("name")
            arguments = params.get("arguments") or {}
            result = _call_tool(name, arguments)
            return _rpc_result(request_id, {
                "content": [{"type": "text", "text": json.dumps(result, ensure_ascii=False)}],
                "structuredContent": result,
                "isError": False,
            })
        return _rpc_error(request_id, -32601, f"Method not found: {method}")
    except (ValueError, FileNotFoundError) as exc:
        return _rpc_result(request_id, {
            "content": [{"type": "text", "text": str(exc)}],
            "isError": True,
        })
    except Exception as exc:
        return _rpc_result(request_id, {
            "content": [{"type": "text", "text": f"Rushframe tool failed: {exc}"}],
            "isError": True,
        })


def _call_tool(name: object, arguments: object) -> object:
    if not isinstance(arguments, dict):
        raise ValueError("Tool arguments must be an object")
    if name == "rushframe.capabilities":
        intelligence = discover_capabilities()
        try:
            editor = _bridge_get(arguments, "capabilities")
        except Exception as exc:
            editor = {"ok": False, "unavailable": True, "error": str(exc)}
        return {"intelligence": intelligence, "editor": editor}
    if name == "rushframe.search_moments":
        return _bridge_post(arguments, "search-context")
    if name == "rushframe.search_sfx":
        return _search_sfx(arguments)
    if name == "rushframe.get_agent_context":
        return _bridge_post(arguments, "agent-context")
    if name == "rushframe.get_editing_context":
        return _bridge_post(arguments, "editing-context")
    if name == "rushframe.preview_edit_plan":
        return _bridge_post(arguments, "plan")
    if name == "rushframe.review_edit_plan":
        return _bridge_post(arguments, "review-plan")
    if name == "rushframe.apply_edit_plan":
        return _bridge_post(arguments, "apply-plan")
    if name == "rushframe.get_timeline_state":
        return _bridge_get(arguments, "timeline")
    if name == "rushframe.apply_timeline_edit":
        return _bridge_post(arguments, "edit")
    if name == "rushframe.render_timeline":
        return _bridge_post(arguments, "render")
    raise ValueError(f"Unknown tool: {name}")


def _bridge_url(arguments: dict[str, Any], endpoint: str) -> str:
    base = str(arguments.get("bridge_url") or DEFAULT_EDITOR_BRIDGE).rstrip("/")
    parsed = urlparse(base)
    expected = urlparse(DEFAULT_EDITOR_BRIDGE)
    if parsed.scheme != "http" or parsed.hostname not in {"127.0.0.1", "localhost"}:
        raise ValueError("Editor bridge must be loopback-only HTTP.")
    if parsed.port != expected.port:
        raise ValueError(f"Editor bridge must use the configured Rushframe port {expected.port}.")
    if parsed.username or parsed.password or parsed.query or parsed.fragment or parsed.path not in {"", "/"}:
        raise ValueError("Editor bridge URL must not contain credentials, a path, query, or fragment.")
    return f"http://127.0.0.1:{expected.port}/{endpoint.lstrip('/')}"


def _bridge_headers() -> dict[str, str]:
    if not EDITOR_SESSION_TOKEN:
        return {}
    return {"X-Rushframe-Session": EDITOR_SESSION_TOKEN}


def _request_is_authorized(headers: Any) -> bool:
    supplied = str(headers.get("X-Rushframe-Session", "")).strip()
    if not supplied:
        authorization = str(headers.get("Authorization", "")).strip()
        if authorization.lower().startswith("bearer "):
            supplied = authorization[7:].strip()
    return bool(supplied) and secrets.compare_digest(supplied, EDITOR_SESSION_TOKEN)


def _bridge_get(arguments: dict[str, Any], endpoint: str) -> object:
    request = Request(_bridge_url(arguments, endpoint), headers=_bridge_headers(), method="GET")
    with urlopen(request, timeout=5) as response:
        return json.loads(response.read().decode("utf-8"))


def _bridge_post(arguments: dict[str, Any], endpoint: str) -> object:
    payload = dict(arguments)
    payload.pop("bridge_url", None)
    data = json.dumps(payload).encode("utf-8")
    request = Request(
        _bridge_url(arguments, endpoint),
        data=data,
        headers={"Content-Type": "application/json", **_bridge_headers()},
        method="POST",
    )
    with urlopen(request, timeout=BRIDGE_OPERATION_TIMEOUT_SECONDS) as response:
        return json.loads(response.read().decode("utf-8"))


def _search_sfx(arguments: dict[str, Any]) -> dict[str, Any]:
    catalog = SoundLibraryCatalog(default_catalog_path())
    response = catalog.search(
        str(arguments.get("query") or ""),
        max_results=max(1, min(50, int(arguments.get("limit") or 20))),
        max_duration=_coerce_optional_float(arguments.get("max_duration")),
        min_lufs=_coerce_optional_float(arguments.get("min_lufs")),
        max_lufs=_coerce_optional_float(arguments.get("max_lufs")),
        min_tempo=_coerce_optional_float(arguments.get("min_tempo")),
        max_tempo=_coerce_optional_float(arguments.get("max_tempo")),
        category=_optional_text(arguments.get("category")),
        mood=_optional_text(arguments.get("mood")),
        license_name=_optional_text(arguments.get("license")),
        favorites_only=bool(arguments.get("favorites_only", False)),
        online_only=not bool(arguments.get("include_offline", False)),
        semantic=not bool(arguments.get("lexical_only", False)),
        similar_to_sound_id=_optional_text(arguments.get("similar_to_sound_id")),
        recently_used=bool(arguments.get("recently_used", False)),
    )
    registrations: dict[str, str] = {}
    registration_warning: str | None = None
    try:
        state = _bridge_get(arguments, "sound-library-registrations")
        if isinstance(state, dict):
            for item in state.get("assets", []):
                if not isinstance(item, dict):
                    continue
                path = item.get("path")
                media_asset_id = item.get("mediaAssetId") or item.get("media_asset_id")
                if isinstance(path, str) and isinstance(media_asset_id, str):
                    registrations[os.path.normcase(os.path.abspath(path))] = media_asset_id
    except Exception as exc:
        registration_warning = f"Editor registration state unavailable: {exc}"

    safe_results: list[dict[str, Any]] = []
    for result in response.results:
        registered_id = registrations.get(os.path.normcase(os.path.abspath(result.path)))
        safe_results.append({
            "sound_id": result.sound_id,
            "name": result.name,
            "duration": result.duration,
            "codec": result.codec,
            "channels": result.channels,
            "sample_rate": result.sample_rate,
            "lufs": result.lufs,
            "peak_db": result.peak_db,
            "category": result.category,
            "mood": result.mood,
            "tempo_bpm": result.tempo_bpm,
            "license_name": result.license_name,
            "attribution": result.attribution,
            "requires_attribution": result.requires_attribution,
            "favorite": result.favorite,
            "offline": result.offline,
            "tags": result.tags,
            "score": result.score,
            "embedding_provider": result.embedding_provider,
            "registered_media_asset_id": registered_id,
            "can_use": registered_id is not None and not result.offline,
        })
    warnings = [value for value in [response.warning, registration_warning] if value]
    return {
        "ok": True,
        "mode": response.mode,
        "embedding_provider": response.embedding_provider,
        "semantic_available": response.semantic_available,
        "results": safe_results,
        "warnings": warnings,
        "registration_required_for_unregistered_results": True,
    }


def _optional_text(value: Any) -> str | None:
    text = str(value or "").strip()
    return text or None


def _coerce_optional_float(value: Any) -> float | None:
    if value is None or value == "":
        return None
    return float(value)


def _optional_float(query: dict[str, list[str]], name: str) -> float | None:
    value = query.get(name, [""])[0].strip()
    return float(value) if value else None


def _rpc_result(request_id: object, result: object) -> dict[str, Any]:
    return {"jsonrpc": "2.0", "id": request_id, "result": result}


def _rpc_error(request_id: object, code: int, message: str) -> dict[str, Any]:
    return {"jsonrpc": "2.0", "id": request_id, "error": {"code": code, "message": message}}


def _required(query: dict[str, list[str]], name: str) -> str:
    value = query.get(name, [""])[0].strip()
    if not value:
        raise ValueError(f"Missing query parameter: {name}")
    return value


def serve(host: str = "127.0.0.1", port: int = 7319) -> None:
    if host.strip().lower() not in {"127.0.0.1", "localhost", "::1"}:
        raise ValueError("Rushframe intelligence backend must bind to a loopback host.")
    server = BoundedThreadingHTTPServer((host, port), RushframeBackendHandler)
    print(json.dumps({"status": "ready", "host": host, "port": port, "mcp": f"http://{host}:{port}/mcp"}), flush=True)
    try:
        server.serve_forever(poll_interval=0.5)
    finally:
        server.server_close()
