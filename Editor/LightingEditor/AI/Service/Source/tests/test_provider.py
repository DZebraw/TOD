from __future__ import annotations

import json

import httpx
import pytest

from dawn_tod_ai_service.constants import (
    DEEPSEEK_CHAT_COMPLETIONS_URL,
    DEEPSEEK_MODEL,
    VALIDATION_PROBE_DATA,
)
from dawn_tod_ai_service.provider import DeepSeekProvider, ProviderError


def completion_response(status_code: int = 200):
    return httpx.Response(
        status_code,
        json={
            "choices": [
                {
                    "finish_reason": "stop",
                    "message": {"content": json.dumps(VALIDATION_PROBE_DATA)},
                }
            ]
        },
    )


@pytest.mark.asyncio
async def test_request_uses_fixed_official_non_thinking_json_configuration():
    captured = {}

    def handler(request: httpx.Request):
        captured["url"] = str(request.url)
        captured["authorization"] = request.headers["Authorization"]
        captured["body"] = json.loads(request.content)
        return completion_response()

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    provider = DeepSeekProvider("sk-test", client=client)
    result = await provider.complete([{"role": "system", "content": "return json"}])
    await client.aclose()

    assert captured["url"] == DEEPSEEK_CHAT_COMPLETIONS_URL
    assert captured["authorization"] == "Bearer sk-test"
    assert "sk-test" not in json.dumps(captured["body"])
    assert captured["body"]["model"] == DEEPSEEK_MODEL
    assert captured["body"]["thinking"] == {"type": "disabled"}
    assert captured["body"]["response_format"] == {"type": "json_object"}
    assert captured["body"]["temperature"] == 0
    assert captured["body"]["stream"] is False
    assert result.retry_count == 0


@pytest.mark.asyncio
async def test_401_does_not_retry():
    calls = 0

    def handler(_: httpx.Request):
        nonlocal calls
        calls += 1
        return httpx.Response(401, json={"error": {"message": "invalid key"}})

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    provider = DeepSeekProvider("sk-test", client=client, sleep=_no_sleep)
    with pytest.raises(ProviderError) as captured:
        await provider.complete([{"role": "system", "content": "json"}])
    await client.aclose()

    assert captured.value.code == "API_KEY_INVALID"
    assert captured.value.retry_count == 0
    assert calls == 1


@pytest.mark.asyncio
async def test_429_retries_twice_then_succeeds():
    calls = 0
    delays = []

    def handler(_: httpx.Request):
        nonlocal calls
        calls += 1
        return httpx.Response(429) if calls < 3 else completion_response()

    async def record_sleep(delay: float):
        delays.append(delay)

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    provider = DeepSeekProvider("sk-test", client=client, sleep=record_sleep)
    result = await provider.complete([{"role": "system", "content": "json"}])
    await client.aclose()

    assert calls == 3
    assert delays == [0.5, 1.0]
    assert result.retry_count == 2


@pytest.mark.asyncio
async def test_connection_or_timeout_failure_is_bounded_to_three_attempts():
    calls = 0

    def handler(request: httpx.Request):
        nonlocal calls
        calls += 1
        raise httpx.ReadTimeout("timed out", request=request)

    client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    provider = DeepSeekProvider("sk-test", client=client, sleep=_no_sleep)
    with pytest.raises(ProviderError) as captured:
        await provider.complete([{"role": "system", "content": "json"}])
    await client.aclose()

    assert captured.value.code == "DEEPSEEK_TIMEOUT"
    assert captured.value.retry_count == 2
    assert calls == 3


async def _no_sleep(_: float):
    return None
