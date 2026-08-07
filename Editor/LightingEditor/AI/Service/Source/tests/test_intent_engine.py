from __future__ import annotations

import copy
import json

import pytest

from dawn_tod_ai_service.contracts import AnalyzeRequest
from dawn_tod_ai_service.constants import SCHEMA_VERSION, VALIDATION_PROBE_DATA
from dawn_tod_ai_service.intent_engine import (
    IntentEngineError,
    WeatherIntentEngine,
    validate_model_json,
)
from dawn_tod_ai_service.resources import load_resources
from conftest import AI_ROOT, FakeProvider, make_request


@pytest.mark.asyncio
async def test_single_turn_prompt_contains_snapshot_skill_schema_and_untrusted_input():
    provider = FakeProvider()
    engine = WeatherIntentEngine(provider, load_resources(AI_ROOT))

    result = await engine.analyze(AnalyzeRequest.model_validate(make_request("让太阳亮一点")))

    assert result.data == VALIDATION_PROBE_DATA
    assert result.repair_count == 0
    assert len(provider.calls) == 1
    assert "weather-intent-v1.schema.json" in provider.calls[0][0]["content"]
    assert "Required JSON Schema" in provider.calls[0][0]["content"]
    assert "让太阳亮一点" in provider.calls[0][1]["content"]
    assert '"time_hour":10.5' in provider.calls[0][1]["content"]


@pytest.mark.asyncio
async def test_invalid_model_output_is_repaired_exactly_once():
    provider = FakeProvider([
        json.dumps({"schema_version": SCHEMA_VERSION}),
        json.dumps(VALIDATION_PROBE_DATA),
    ])
    engine = WeatherIntentEngine(provider, load_resources(AI_ROOT))

    result = await engine.analyze(AnalyzeRequest.model_validate(make_request()))

    assert result.data == VALIDATION_PROBE_DATA
    assert result.repair_count == 1
    assert len(provider.calls) == 2
    assert "validation_errors" in provider.calls[1][-1]["content"]


@pytest.mark.asyncio
async def test_second_invalid_output_returns_no_partial_result():
    provider = FakeProvider([
        json.dumps({"schema_version": SCHEMA_VERSION}),
        '{"extra":true}',
    ])
    engine = WeatherIntentEngine(provider, load_resources(AI_ROOT))

    with pytest.raises(IntentEngineError) as captured:
        await engine.analyze(AnalyzeRequest.model_validate(make_request()))

    assert captured.value.repair_count == 1
    assert len(provider.calls) == 2


def test_duplicate_fields_and_fields_outside_request_capabilities_are_rejected():
    resources = load_resources(AI_ROOT)
    duplicate, duplicate_errors = validate_model_json(
        (
            '{"schema_version":"%s","schema_version":"%s"}'
            % (SCHEMA_VERSION, SCHEMA_VERSION)
        ),
        resources,
    )
    restricted = copy.deepcopy(VALIDATION_PROBE_DATA)
    restricted_value, restricted_errors = validate_model_json(
        json.dumps(restricted),
        resources,
        frozenset({"time"}),
    )

    assert duplicate is None
    assert duplicate_errors[0].rule == "json"
    assert restricted_value is None
    assert restricted_errors
    assert all(error.rule == "capability" for error in restricted_errors)
