"""Fixed DeepSeek Chat Completions provider with bounded retries."""

from __future__ import annotations

import asyncio
from collections.abc import Awaitable, Callable
from dataclasses import dataclass
from typing import Any, Protocol

import httpx

from .constants import (
    DEEPSEEK_CHAT_COMPLETIONS_URL,
    DEEPSEEK_MAX_RETRIES,
    DEEPSEEK_MAX_TOKENS,
    DEEPSEEK_MODEL,
    DEEPSEEK_TIMEOUT_SECONDS,
)


@dataclass(frozen=True)
class ProviderCompletion:
    content: str
    retry_count: int


class ProviderError(RuntimeError):
    def __init__(
        self,
        code: str,
        message: str,
        http_status: int = 502,
        retry_count: int = 0,
    ) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.http_status = http_status
        self.retry_count = retry_count


class CompletionProvider(Protocol):
    async def complete(self, messages: list[dict[str, str]]) -> ProviderCompletion: ...

    async def aclose(self) -> None: ...


class DeepSeekProvider:
    def __init__(
        self,
        api_key: str,
        client: httpx.AsyncClient | None = None,
        sleep: Callable[[float], Awaitable[None]] = asyncio.sleep,
        timeout_seconds: float = DEEPSEEK_TIMEOUT_SECONDS,
        max_retries: int = DEEPSEEK_MAX_RETRIES,
    ) -> None:
        if not api_key.strip():
            raise ValueError("A non-empty DeepSeek API key is required.")
        if timeout_seconds <= 0 or max_retries < 0:
            raise ValueError("Invalid provider retry configuration.")
        self._api_key = api_key
        self._client = client or httpx.AsyncClient()
        self._owns_client = client is None
        self._sleep = sleep
        self._timeout_seconds = timeout_seconds
        self._max_retries = max_retries

    async def complete(self, messages: list[dict[str, str]]) -> ProviderCompletion:
        request_body = {
            "model": DEEPSEEK_MODEL,
            "messages": messages,
            "thinking": {"type": "disabled"},
            "response_format": {"type": "json_object"},
            "temperature": 0,
            "stream": False,
            "max_tokens": DEEPSEEK_MAX_TOKENS,
        }
        headers = {
            "Authorization": f"Bearer {self._api_key}",
            "Content-Type": "application/json",
        }
        retry_count = 0
        for attempt in range(self._max_retries + 1):
            try:
                response = await self._client.post(
                    DEEPSEEK_CHAT_COMPLETIONS_URL,
                    headers=headers,
                    json=request_body,
                    timeout=self._timeout_seconds,
                )
            except httpx.TimeoutException as exception:
                if attempt < self._max_retries:
                    retry_count += 1
                    await self._sleep(_backoff_seconds(retry_count))
                    continue
                raise ProviderError(
                    "DEEPSEEK_TIMEOUT",
                    "The DeepSeek request timed out.",
                    504,
                    retry_count,
                ) from exception
            except httpx.TransportError as exception:
                if attempt < self._max_retries:
                    retry_count += 1
                    await self._sleep(_backoff_seconds(retry_count))
                    continue
                raise ProviderError(
                    "DEEPSEEK_CONNECTION_FAILED",
                    "The DeepSeek service could not be reached.",
                    502,
                    retry_count,
                ) from exception

            if response.status_code in (401, 403):
                raise ProviderError(
                    "API_KEY_INVALID",
                    "The configured DeepSeek API key is invalid.",
                    401,
                    retry_count,
                )

            if response.status_code == 429 or response.status_code >= 500:
                if attempt < self._max_retries:
                    retry_count += 1
                    await self._sleep(_backoff_seconds(retry_count))
                    continue
                code = (
                    "DEEPSEEK_RATE_LIMITED"
                    if response.status_code == 429
                    else "DEEPSEEK_UNAVAILABLE"
                )
                message = (
                    "The DeepSeek request was rate limited."
                    if response.status_code == 429
                    else "The DeepSeek service is temporarily unavailable."
                )
                raise ProviderError(code, message, 503, retry_count)

            if response.status_code < 200 or response.status_code >= 300:
                raise ProviderError(
                    "DEEPSEEK_REQUEST_REJECTED",
                    "The DeepSeek request was rejected.",
                    502,
                    retry_count,
                )

            return ProviderCompletion(_read_completion_content(response), retry_count)

        raise AssertionError("unreachable provider retry state")

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()


def _read_completion_content(response: httpx.Response) -> str:
    try:
        body: Any = response.json()
        choices = body["choices"]
        choice = choices[0]
        finish_reason = choice["finish_reason"]
        content = choice["message"]["content"]
    except (ValueError, TypeError, KeyError, IndexError) as exception:
        raise ProviderError(
            "DEEPSEEK_RESPONSE_INVALID",
            "DeepSeek returned an invalid response envelope.",
        ) from exception

    if finish_reason == "length":
        raise ProviderError(
            "DEEPSEEK_RESPONSE_TRUNCATED",
            "DeepSeek truncated the JSON response.",
        )
    if finish_reason != "stop" or not isinstance(content, str) or not content.strip():
        raise ProviderError(
            "DEEPSEEK_RESPONSE_INVALID",
            "DeepSeek returned no usable JSON response.",
        )
    return content


def _backoff_seconds(retry_count: int) -> float:
    return 0.5 * (2 ** (retry_count - 1))
