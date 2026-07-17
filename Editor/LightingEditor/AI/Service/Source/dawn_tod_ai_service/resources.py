"""Load and validate the versioned Schema, Skill, and System Prompt."""

from __future__ import annotations

import hashlib
import json
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator

from .constants import SCHEMA_VERSION, VALIDATION_PROBE_DATA

_FRONT_MATTER = re.compile(r"\A---\s*\r?\n(?P<body>.*?)\r?\n---(?:\r?\n|\Z)", re.DOTALL)
_VERSION_LINE = re.compile(r'^schema_version\s*:\s*["\']?(?P<value>[^"\'\s]+)["\']?\s*$')


@dataclass(frozen=True)
class ResourceBundle:
    schema: dict[str, Any] | None
    validator: Draft202012Validator | None
    schema_text: str
    skill_text: str
    system_prompt: str
    skill_hash: str
    errors: tuple[str, ...]

    @property
    def ready(self) -> bool:
        return not self.errors and self.validator is not None


def _read_bytes(path: Path, missing_code: str, invalid_code: str, errors: list[str]) -> bytes | None:
    try:
        return path.read_bytes()
    except FileNotFoundError:
        errors.append(missing_code)
    except OSError:
        errors.append(invalid_code)
    return None


def _decode_utf8(raw: bytes | None, invalid_code: str, errors: list[str]) -> str | None:
    if raw is None:
        return None
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        errors.append(invalid_code)
        return None


def _front_matter_version(text: str | None, invalid_code: str, errors: list[str]) -> str | None:
    if text is None:
        return None
    match = _FRONT_MATTER.match(text)
    if match is None:
        errors.append(invalid_code)
        return None
    for line in match.group("body").splitlines():
        version_match = _VERSION_LINE.match(line.strip())
        if version_match is not None:
            return version_match.group("value")
    errors.append(invalid_code)
    return None


def load_resources(ai_root: Path) -> ResourceBundle:
    errors: list[str] = []
    schema_path = ai_root / "Schemas" / "weather-intent-v1.schema.json"
    skill_path = ai_root / "Skills" / "weather-intent" / "SKILL.md"
    prompt_path = ai_root / "Prompts" / "weather-intent-system.md"

    schema_raw = _read_bytes(schema_path, "SCHEMA_MISSING", "SCHEMA_UNREADABLE", errors)
    skill_raw = _read_bytes(skill_path, "SKILL_MISSING", "SKILL_UNREADABLE", errors)
    prompt_raw = _read_bytes(prompt_path, "PROMPT_MISSING", "PROMPT_UNREADABLE", errors)

    schema: dict[str, Any] | None = None
    validator: Draft202012Validator | None = None
    if schema_raw is not None:
        try:
            decoded = json.loads(schema_raw.decode("utf-8"))
            if not isinstance(decoded, dict):
                raise ValueError("schema root is not an object")
            Draft202012Validator.check_schema(decoded)
            schema = decoded
        except (UnicodeDecodeError, json.JSONDecodeError, ValueError, TypeError):
            errors.append("SCHEMA_INVALID")
        except Exception:
            errors.append("SCHEMA_INVALID")

    if schema is not None:
        declared_version = schema.get("properties", {}).get("schema_version", {}).get("const")
        if declared_version != SCHEMA_VERSION:
            errors.append("SCHEMA_VERSION_MISMATCH")
        else:
            validator = Draft202012Validator(schema)
            if not validator.is_valid(VALIDATION_PROBE_DATA):
                errors.append("VALIDATION_PROBE_SCHEMA_INVALID")
                validator = None

    skill_text = _decode_utf8(skill_raw, "SKILL_INVALID", errors)
    prompt_text = _decode_utf8(prompt_raw, "PROMPT_INVALID", errors)
    skill_version = _front_matter_version(skill_text, "SKILL_INVALID", errors)
    prompt_version = _front_matter_version(prompt_text, "PROMPT_INVALID", errors)
    if skill_version is not None and skill_version != SCHEMA_VERSION:
        errors.append("SKILL_VERSION_MISMATCH")
    if prompt_version is not None and prompt_version != SCHEMA_VERSION:
        errors.append("PROMPT_VERSION_MISMATCH")

    skill_hash = hashlib.sha256(skill_raw).hexdigest() if skill_raw is not None else ""
    return ResourceBundle(
        schema=schema,
        validator=validator,
        schema_text=(schema_raw or b"").decode("utf-8", errors="replace"),
        skill_text=skill_text or "",
        system_prompt=prompt_text or "",
        skill_hash=skill_hash,
        errors=tuple(dict.fromkeys(errors)),
    )
