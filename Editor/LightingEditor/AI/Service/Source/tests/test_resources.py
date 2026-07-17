from __future__ import annotations

import copy
import json
import shutil

import pytest
from jsonschema import ValidationError

from dawn_tod_ai_service.constants import VALIDATION_PROBE_DATA
from dawn_tod_ai_service.resources import load_resources
from conftest import AI_ROOT


def copy_resources(destination):
    shutil.copytree(AI_ROOT / "Schemas", destination / "Schemas")
    shutil.copytree(AI_ROOT / "Skills", destination / "Skills")
    shutil.copytree(AI_ROOT / "Prompts", destination / "Prompts")


@pytest.mark.parametrize(
    ("relative_path", "error_code"),
    [
        ("Schemas/weather-intent-v1.schema.json", "SCHEMA_MISSING"),
        ("Skills/weather-intent/SKILL.md", "SKILL_MISSING"),
        ("Prompts/weather-intent-system.md", "PROMPT_MISSING"),
    ],
)
def test_missing_versioned_resource_is_not_ready(tmp_path, relative_path: str, error_code: str):
    copy_resources(tmp_path)
    (tmp_path / relative_path).unlink()

    resources = load_resources(tmp_path)

    assert resources.ready is False
    assert error_code in resources.errors


@pytest.mark.parametrize(
    ("relative_path", "error_code"),
    [
        ("Skills/weather-intent/SKILL.md", "SKILL_VERSION_MISMATCH"),
        ("Prompts/weather-intent-system.md", "PROMPT_VERSION_MISMATCH"),
    ],
)
def test_resource_version_mismatch_is_not_ready(tmp_path, relative_path: str, error_code: str):
    copy_resources(tmp_path)
    path = tmp_path / relative_path
    path.write_text(path.read_text(encoding="utf-8").replace('schema_version: "1.0"', 'schema_version: "2.0"'), encoding="utf-8")

    resources = load_resources(tmp_path)

    assert resources.ready is False
    assert error_code in resources.errors


def test_schema_version_mismatch_is_not_ready(tmp_path):
    copy_resources(tmp_path)
    path = tmp_path / "Schemas" / "weather-intent-v1.schema.json"
    schema = json.loads(path.read_text(encoding="utf-8"))
    schema["properties"]["schema_version"]["const"] = "2.0"
    path.write_text(json.dumps(schema), encoding="utf-8")

    resources = load_resources(tmp_path)

    assert resources.ready is False
    assert "SCHEMA_VERSION_MISMATCH" in resources.errors


@pytest.mark.parametrize(
    "mutate",
    [
        lambda value: value.pop("rain"),
        lambda value: value.update(extra=True),
        lambda value: value["sun"].update(intensity="2.0"),
        lambda value: value["sun"].update(intensity=8.01),
        lambda value: value["fog"].update(color={"r": 1, "g": 1, "b": 1, "a": 1}),
    ],
)
def test_python_schema_validator_rejects_invalid_or_reserved_data(mutate):
    resources = load_resources(AI_ROOT)
    value = copy.deepcopy(VALIDATION_PROBE_DATA)
    mutate(value)

    with pytest.raises(ValidationError):
        resources.validator.validate(value)
