"""Local HTTP and MCP backend for Rushframe intelligence."""

from __future__ import annotations

import json
from dataclasses import asdict
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, urlparse
from urllib.request import Request, urlopen

from rushframe_intelligence.agent_context import build_agent_context
from rushframe_intelligence.capabilities import discover_capabilities
from rushframe_intelligence.context_index import MediaContextIndex
from rushframe_intelligence.serialization import load_analysis

PROTOCOL_VERSION = "2025-06-18"
MAX_REQUEST_BYTES = 1024 * 1024
DEFAULT_EDITOR_BRIDGE = "http://127.0.0.1:7320"

TOOLS = [
    {
        "name": "rushframe.capabilities",
        "description": "Report installed Rushframe media-intelligence capabilities.",
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
    },
    {
        "name": "rushframe.search_moments",
        "description": "Search an analyzed media context index for useful editing moments.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "index": {"type": "string", "description": "Absolute path to context.sqlite."},
                "query": {"type": "string"},
                "limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 12},
                "roles": {"type": "array", "items": {"type": "string"}},
                "min_score": {"type": "number", "minimum": 0, "maximum": 1, "default": 0},
                "max_duration": {"type": "number", "exclusiveMinimum": 0},
            },
            "required": ["index", "query"],
            "additionalProperties": False,
        },
    },
    {
        "name": "rushframe.get_agent_context",
        "description": "Build a compact agent context bundle from media-analysis.json.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "analysis": {"type": "string", "description": "Absolute path to media-analysis.json."},
                "query": {"type": "string", "default": ""},
                "limit": {"type": "integer", "minimum": 1, "maximum": 50, "default": 20},
                "roles": {"type": "array", "items": {"type": "string"}},
            },
            "required": ["analysis"],
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
        "description": "Preview or apply an approved live timeline edit in the desktop editor.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "action": {
                    "type": "string",
                    "enum": [
                        "add_clip", "add_music", "add_text", "add_caption",
                        "move_clip", "trim_clip", "split_clip", "delete_clip",
                        "add_transition", "add_effect",
                    ],
                },
                "preview_only": {"type": "boolean", "default": True},
                "require_approval": {"type": "boolean", "default": True},
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
        "name": "rushframe.render_timeline",
        "description": "Render/export the live desktop timeline after user approval.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "bridge_url": {"type": "string", "default": DEFAULT_EDITOR_BRIDGE},
                "output_path": {"type": "string"},
                "require_approval": {"type": "boolean", "default": True},
            },
            "required": ["output_path"],
            "additionalProperties": False,
        },
    },
]


class RushframeBackendHandler(BaseHTTPRequestHandler):
    server_version = "RushframeIntelligence/2.0"

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
                self._write_json({"status": "ok", "service": "rushframe-intelligence", "mcp": "/mcp"})
                return
            if parsed.path == "/capabilities":
                self._write_json(discover_capabilities())
                return
            if parsed.path == "/search":
                results = _search({
                    "index": _required(query, "index"),
                    "query": _required(query, "q"),
                    "limit": int(query.get("limit", ["12"])[0]),
                    "roles": query.get("role", []),
                })
                self._write_json(results)
                return
            if parsed.path == "/context":
                bundle = _context({
                    "analysis": _required(query, "analysis"),
                    "query": query.get("q", [""])[0],
                    "limit": int(query.get("limit", ["20"])[0]),
                    "roles": query.get("role", []),
                })
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
        return discover_capabilities()
    if name == "rushframe.search_moments":
        return _search(arguments)
    if name == "rushframe.get_agent_context":
        return _context(arguments)
    if name == "rushframe.get_timeline_state":
        return _bridge_get(arguments, "timeline")
    if name == "rushframe.apply_timeline_edit":
        return _bridge_post(arguments, "edit")
    if name == "rushframe.render_timeline":
        return _bridge_post(arguments, "render")
    raise ValueError(f"Unknown tool: {name}")


def _search(arguments: dict[str, Any]) -> list[dict[str, Any]]:
    index_path = str(arguments.get("index", "")).strip()
    query = str(arguments.get("query", "")).strip()
    if not index_path or not query:
        raise ValueError("index and query are required")
    limit = max(1, min(50, int(arguments.get("limit", 12))))
    roles = [str(value) for value in arguments.get("roles", [])]
    results = MediaContextIndex(index_path).search(
        query,
        limit=limit,
        roles=roles,
        min_overall_score=float(arguments.get("min_score", 0)),
        max_duration=float(arguments["max_duration"]) if arguments.get("max_duration") is not None else None,
    )
    return [asdict(result) for result in results]


def _context(arguments: dict[str, Any]) -> dict[str, Any]:
    analysis_path = str(arguments.get("analysis", "")).strip()
    if not analysis_path:
        raise ValueError("analysis is required")
    limit = max(1, min(50, int(arguments.get("limit", 20))))
    roles = [str(value) for value in arguments.get("roles", [])]
    return build_agent_context(
        load_analysis(Path(analysis_path)),
        query=str(arguments.get("query", "")),
        roles=roles,
        limit=limit,
    )


def _bridge_url(arguments: dict[str, Any], endpoint: str) -> str:
    base = str(arguments.get("bridge_url") or DEFAULT_EDITOR_BRIDGE).rstrip("/")
    parsed = urlparse(base)
    if parsed.hostname not in {"127.0.0.1", "localhost"}:
        raise ValueError("Editor bridge must be loopback-only.")
    return f"{base}/{endpoint.lstrip('/')}"


def _bridge_get(arguments: dict[str, Any], endpoint: str) -> object:
    request = Request(_bridge_url(arguments, endpoint), method="GET")
    with urlopen(request, timeout=5) as response:
        return json.loads(response.read().decode("utf-8"))


def _bridge_post(arguments: dict[str, Any], endpoint: str) -> object:
    payload = dict(arguments)
    payload.pop("bridge_url", None)
    data = json.dumps(payload).encode("utf-8")
    request = Request(
        _bridge_url(arguments, endpoint),
        data=data,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    with urlopen(request, timeout=None) as response:
        return json.loads(response.read().decode("utf-8"))


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
    server = ThreadingHTTPServer((host, port), RushframeBackendHandler)
    print(json.dumps({"status": "ready", "host": host, "port": port, "mcp": f"http://{host}:{port}/mcp"}), flush=True)
    try:
        server.serve_forever(poll_interval=0.5)
    finally:
        server.server_close()
