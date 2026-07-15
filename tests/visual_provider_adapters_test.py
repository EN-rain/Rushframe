from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

from rushframe_intelligence import visual_analyzer
from rushframe_intelligence.visual_analyzer import (
    CloudflareFrameProvider,
    GroqFrameProvider,
    VisualUnderstandingUnavailable,
)


class _FakeResponse:
    is_error = False
    status_code = 200
    text = ""

    @staticmethod
    def json() -> dict[str, Any]:
        return {
            "choices": [
                {
                    "message": {
                        "content": json.dumps(
                            {
                                "description": "Two people at a card table",
                                "summary": "A tense conversation",
                                "tags": ["dialogue"],
                                "subjects": ["two people"],
                                "actions": ["talking"],
                                "visible_text": [],
                                "location": "room",
                                "shot_type": "medium shot",
                                "camera_motion": "static",
                                "mood": "tense",
                                "visual_energy": 0.6,
                                "usable": True,
                                "confidence": 0.9,
                                "editing_roles": ["conflict"],
                            }
                        )
                    }
                }
            ]
        }


class _FakeClient:
    last_endpoint: str | None = None
    last_headers: dict[str, str] | None = None
    last_payload: dict[str, Any] | None = None

    def __init__(self, *, timeout: float) -> None:
        assert timeout == 90.0

    def __enter__(self) -> _FakeClient:
        return self

    def __exit__(self, *_: object) -> None:
        return None

    def post(
        self,
        endpoint: str,
        *,
        headers: dict[str, str],
        json: dict[str, Any],
    ) -> _FakeResponse:
        type(self).last_endpoint = endpoint
        type(self).last_headers = headers
        type(self).last_payload = json
        return _FakeResponse()


def _frames(tmp_path: Path) -> list[Path]:
    first = tmp_path / "first.jpg"
    second = tmp_path / "second.jpg"
    first.write_bytes(b"first-image")
    second.write_bytes(b"second-image")
    return [first, second]


def test_groq_provider_sends_multiple_local_frames_as_openai_image_parts(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("GROQ_API_KEY", "groq-secret")
    monkeypatch.delenv("RUSHFRAME_GROQ_MODEL", raising=False)
    monkeypatch.setattr(visual_analyzer.httpx, "Client", _FakeClient)

    result = GroqFrameProvider().analyze(_frames(tmp_path), "Batman enters the room")

    assert result["editing_roles"] == ["conflict"]
    assert _FakeClient.last_endpoint == "https://api.groq.com/openai/v1/chat/completions"
    assert _FakeClient.last_headers == {
        "Authorization": "Bearer groq-secret",
        "Content-Type": "application/json",
    }
    assert _FakeClient.last_payload is not None
    assert _FakeClient.last_payload["model"] == "meta-llama/llama-4-scout-17b-16e-instruct"
    content = _FakeClient.last_payload["messages"][0]["content"]
    assert len(content) == 3
    assert content[1]["image_url"]["url"].startswith("data:image/jpeg;base64,")
    assert _FakeClient.last_payload["response_format"] == {"type": "json_object"}


def test_cloudflare_provider_uses_account_scoped_openai_endpoint(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("CLOUDFLARE_ACCOUNT_ID", "account/id")
    monkeypatch.setenv("CLOUDFLARE_API_TOKEN", "cloudflare-secret")
    monkeypatch.delenv("CLOUDFLARE_AUTH_TOKEN", raising=False)
    monkeypatch.delenv("RUSHFRAME_CLOUDFLARE_MODEL", raising=False)
    monkeypatch.setattr(visual_analyzer.httpx, "Client", _FakeClient)

    result = CloudflareFrameProvider().analyze(_frames(tmp_path), "")

    assert result["description"] == "Two people at a card table"
    assert _FakeClient.last_endpoint == (
        "https://api.cloudflare.com/client/v4/accounts/account%2Fid/ai/v1/chat/completions"
    )
    assert _FakeClient.last_headers is not None
    assert _FakeClient.last_headers["Authorization"] == "Bearer cloudflare-secret"
    assert _FakeClient.last_payload is not None
    assert _FakeClient.last_payload["model"] == "@cf/meta/llama-4-scout-17b-16e-instruct"


@pytest.mark.parametrize("provider", [GroqFrameProvider, CloudflareFrameProvider])
def test_remote_provider_rejects_missing_credentials(
    provider: type[GroqFrameProvider] | type[CloudflareFrameProvider],
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    for name in (
        "GROQ_API_KEY",
        "CLOUDFLARE_ACCOUNT_ID",
        "CLOUDFLARE_API_TOKEN",
        "CLOUDFLARE_AUTH_TOKEN",
    ):
        monkeypatch.delenv(name, raising=False)

    with pytest.raises(VisualUnderstandingUnavailable):
        provider().analyze(_frames(tmp_path))
