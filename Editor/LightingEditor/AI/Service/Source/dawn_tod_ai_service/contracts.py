"""Strict HTTP request models shared by all service providers."""

from typing import Annotated, Literal
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, field_validator

from .constants import SUPPORTED_NON_NULL_FIELDS

FiniteUnitFloat = Annotated[float, Field(ge=0.0, le=1.0, allow_inf_nan=False)]
FiniteHour = Annotated[float, Field(ge=0.0, lt=24.0, allow_inf_nan=False)]
FiniteAzimuth = Annotated[float, Field(ge=0.0, lt=360.0, allow_inf_nan=False)]
FiniteElevation = Annotated[float, Field(ge=-90.0, le=90.0, allow_inf_nan=False)]
FiniteIntensity = Annotated[float, Field(ge=0.0, le=8.0, allow_inf_nan=False)]


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class ColorSnapshot(StrictModel):
    r: FiniteUnitFloat
    g: FiniteUnitFloat
    b: FiniteUnitFloat
    a: FiniteUnitFloat


class LightSnapshot(StrictModel):
    azimuth_deg: FiniteAzimuth
    elevation_deg: FiniteElevation
    intensity: FiniteIntensity
    color: ColorSnapshot


class WeatherSnapshot(StrictModel):
    time_hour: FiniteHour
    sun: LightSnapshot
    moon: LightSnapshot


class Capabilities(StrictModel):
    supported_non_null_fields: list[str]

    @field_validator("supported_non_null_fields")
    @classmethod
    def require_supported_capability_set(cls, value: list[str]) -> list[str]:
        if len(value) != len(set(value)):
            raise ValueError("capabilities must not contain duplicates")
        if set(value) != set(SUPPORTED_NON_NULL_FIELDS):
            raise ValueError("capabilities do not match the service allowlist")
        return value


class AnalyzeRequest(StrictModel):
    request_id: UUID
    schema_version: Annotated[str, Field(min_length=1)]
    pipeline: Literal["URP"]
    user_input: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1)]
    capabilities: Capabilities
    snapshot: WeatherSnapshot
