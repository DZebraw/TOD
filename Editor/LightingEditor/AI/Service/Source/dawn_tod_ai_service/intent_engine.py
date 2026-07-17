"""Prompt construction, strict model-output validation, and one repair attempt."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any

from .contracts import AnalyzeRequest
from .provider import CompletionProvider, ProviderCompletion
from .resources import ResourceBundle


@dataclass(frozen=True)
class ValidationIssue:
    path: str
    rule: str
    message: str


@dataclass(frozen=True)
class IntentEngineResult:
    data: dict[str, Any]
    retry_count: int
    repair_count: int


class IntentEngineError(RuntimeError):
    def __init__(self, issues: tuple[ValidationIssue, ...], retry_count: int) -> None:
        super().__init__("The model output failed validation after one repair attempt.")
        self.code = "MODEL_OUTPUT_INVALID"
        self.message = "The model output failed validation after one repair attempt."
        self.http_status = 422
        self.issues = issues
        self.retry_count = retry_count
        self.repair_count = 1


class WeatherIntentEngine:
    def __init__(self, provider: CompletionProvider, resources: ResourceBundle) -> None:
        self._provider = provider
        self._resources = resources

    async def analyze(self, request: AnalyzeRequest) -> IntentEngineResult:
        messages = _analysis_messages(request, self._resources)
        first = await self._provider.complete(messages)
        data, issues = validate_model_json(first.content, self._resources)
        if data is not None:
            return IntentEngineResult(data, first.retry_count, 0)

        repair_messages = messages + [
            {"role": "assistant", "content": first.content},
            {
                "role": "user",
                "content": json.dumps(
                    {
                        "operation": "repair_weather_intent_json",
                        "validation_errors": [issue.__dict__ for issue in issues],
                        "instruction": (
                            "Correct only the invalid JSON response. Preserve the original "
                            "user intent, Schema, snapshot, and capability allowlist. Return JSON only."
                        ),
                    },
                    ensure_ascii=False,
                    separators=(",", ":"),
                ),
            },
        ]
        repaired = await self._provider.complete(repair_messages)
        repaired_data, repaired_issues = validate_model_json(
            repaired.content,
            self._resources,
        )
        retry_count = first.retry_count + repaired.retry_count
        if repaired_data is None:
            raise IntentEngineError(repaired_issues, retry_count)
        return IntentEngineResult(repaired_data, retry_count, 1)


def validate_model_json(
    raw: str,
    resources: ResourceBundle,
) -> tuple[dict[str, Any] | None, tuple[ValidationIssue, ...]]:
    try:
        value = json.loads(
            raw,
            object_pairs_hook=_object_without_duplicates,
            parse_constant=_reject_non_finite,
        )
    except (json.JSONDecodeError, ValueError, TypeError):
        return None, (
            ValidationIssue("$", "json", "The response is not one strict JSON object."),
        )

    if not isinstance(value, dict):
        return None, (
            ValidationIssue("$", "type", "The response root must be an object."),
        )
    if resources.validator is None:
        return None, (
            ValidationIssue("$", "schema", "The packaged Schema is unavailable."),
        )

    schema_errors = sorted(
        resources.validator.iter_errors(value),
        key=lambda error: tuple(str(part) for part in error.absolute_path),
    )
    if not schema_errors:
        return value, ()

    issues = tuple(
        ValidationIssue(
            _json_path(error.absolute_path),
            str(error.validator),
            error.message,
        )
        for error in schema_errors[:20]
    )
    return None, issues


def _analysis_messages(
    request: AnalyzeRequest,
    resources: ResourceBundle,
) -> list[dict[str, str]]:
    system_content = (
        resources.system_prompt.rstrip()
        + "\n\n# Versioned domain skill\n"
        + resources.skill_text.rstrip()
        + "\n\n# Required JSON Schema\n"
        + resources.schema_text.rstrip()
    )
    request_content = json.dumps(
        {
            "operation": "create_weather_intent_json",
            "schema_version": request.schema_version,
            "pipeline": request.pipeline,
            "untrusted_user_input": request.user_input,
            "capabilities": request.capabilities.model_dump(mode="json"),
            "snapshot": request.snapshot.model_dump(mode="json"),
        },
        ensure_ascii=False,
        separators=(",", ":"),
    )
    return [
        {"role": "system", "content": system_content},
        {"role": "user", "content": request_content},
    ]


def _object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("duplicate JSON field")
        value[key] = item
    return value


def _reject_non_finite(value: str) -> None:
    raise ValueError(f"non-finite JSON number: {value}")


def _json_path(parts: Any) -> str:
    path = "$"
    for part in parts:
        path += f"[{part}]" if isinstance(part, int) else "." + str(part)
    return path
