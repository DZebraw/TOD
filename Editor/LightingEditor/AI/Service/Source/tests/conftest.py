from __future__ import annotations

import copy
import json
import asyncio
from pathlib import Path
from uuid import uuid4

import httpx
import pytest
import pytest_asyncio

from dawn_tod_ai_service.app import create_app
from dawn_tod_ai_service.constants import (
    SCHEMA_VERSION,
    SUPPORTED_NON_NULL_FIELDS,
    VALIDATION_PROBE_DATA,
)
from dawn_tod_ai_service.provider import ProviderCompletion

AI_ROOT = Path(__file__).resolve().parents[3]
TOKEN = "test-session-token"
HEADERS = {"X-DawnTOD-Session-Token": TOKEN}


class FakeProvider:
    def __init__(self, responses=None, delay_seconds: float = 0.0):
        self.responses = list(responses or [json.dumps(VALIDATION_PROBE_DATA)])
        self.delay_seconds = delay_seconds
        self.calls = []
        self.closed = False

    async def complete(self, messages):
        self.calls.append(copy.deepcopy(messages))
        if self.delay_seconds:
            await asyncio.sleep(self.delay_seconds)
        response = self.responses.pop(0) if len(self.responses) > 1 else self.responses[0]
        return ProviderCompletion(response, 0)

    async def aclose(self):
        self.closed = True


def make_request(user_input: str = "正午晴天") -> dict:
    return {
        "request_id": str(uuid4()),
        "schema_version": SCHEMA_VERSION,
        "pipeline": "URP",
        "user_input": user_input,
        "capabilities": {
            "supported_non_null_fields": list(SUPPORTED_NON_NULL_FIELDS),
        },
        "snapshot": {
            "time_hour": 10.5,
            "sun": {
                "azimuth_deg": 247.5,
                "elevation_deg": 52.0,
                "intensity": 1.6,
                "color": {"r": 1.0, "g": 0.9, "b": 0.8, "a": 1.0},
            },
            "moon": {
                "azimuth_deg": 67.5,
                "elevation_deg": -52.0,
                "intensity": 0.2,
                "color": {"r": 0.7, "g": 0.8, "b": 1.0, "a": 1.0},
            },
        },
    }


@pytest.fixture
def request_payload() -> dict:
    return copy.deepcopy(make_request())


@pytest_asyncio.fixture
async def client():
    app = create_app(AI_ROOT, TOKEN, provider=FakeProvider())
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as value:
        yield value
