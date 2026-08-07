"""Strict HTTP request models shared by all service providers."""

from typing import Annotated, Literal
from uuid import UUID

from pydantic import (
    BaseModel,
    ConfigDict,
    Field,
    StringConstraints,
    field_validator,
    model_validator,
)

from .constants import (
    CORE_NON_NULL_FIELDS,
    PIPELINE_NON_NULL_FIELDS,
    SUPPORTED_NON_NULL_FIELDS,
)

FiniteUnitFloat = Annotated[float, Field(ge=0.0, le=1.0, allow_inf_nan=False)]
FiniteHour = Annotated[float, Field(ge=0.0, lt=24.0, allow_inf_nan=False)]
FiniteAzimuth = Annotated[float, Field(ge=0.0, lt=360.0, allow_inf_nan=False)]
FiniteElevation = Annotated[float, Field(ge=-90.0, le=90.0, allow_inf_nan=False)]
FiniteNonNegative = Annotated[float, Field(ge=0.0, allow_inf_nan=False)]
FiniteFogDistance = Annotated[float, Field(ge=0.01, allow_inf_nan=False)]
FiniteFloat = Annotated[float, Field(allow_inf_nan=False)]
FiniteWindAngle = Annotated[float, Field(ge=-45.0, le=45.0, allow_inf_nan=False)]


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
    intensity: FiniteNonNegative
    color: ColorSnapshot


class SkySnapshot(StrictModel):
    star_emission: Annotated[float, Field(ge=0.0, le=1000.0, allow_inf_nan=False)] | None


class FogSnapshot(StrictModel):
    mean_free_path_m: FiniteFogDistance | None
    base_height_m: FiniteFloat | None
    color: ColorSnapshot | None


class ExposureSnapshot(StrictModel):
    compensation_ev: FiniteFloat | None


class RainSnapshot(StrictModel):
    enabled: bool
    precipitation_amount: FiniteUnitFloat | None
    fall_speed: FiniteNonNegative | None
    density: FiniteNonNegative | None
    wind_z_rotation_deg: FiniteWindAngle | None


class WeatherSnapshot(StrictModel):
    time_hour: FiniteHour
    sun: LightSnapshot
    moon: LightSnapshot
    sky: SkySnapshot
    fog: FogSnapshot
    exposure: ExposureSnapshot
    rain: RainSnapshot


class Capabilities(StrictModel):
    supported_non_null_fields: list[str]

    @field_validator("supported_non_null_fields")
    @classmethod
    def require_supported_capability_set(cls, value: list[str]) -> list[str]:
        if len(value) != len(set(value)):
            raise ValueError("capabilities must not contain duplicates")
        if not set(value).issubset(SUPPORTED_NON_NULL_FIELDS):
            raise ValueError("capabilities contain a field outside the service allowlist")
        return value


class AnalyzeRequest(StrictModel):
    request_id: UUID
    schema_version: Annotated[str, Field(min_length=1)]
    pipeline: Literal["URP", "HDRP"]
    user_input: Annotated[str, StringConstraints(strip_whitespace=True, min_length=1)]
    capabilities: Capabilities
    snapshot: WeatherSnapshot

    @model_validator(mode="after")
    def require_pipeline_capabilities(self) -> "AnalyzeRequest":
        actual = set(self.capabilities.supported_non_null_fields)
        if not set(CORE_NON_NULL_FIELDS).issubset(actual):
            raise ValueError("capabilities are missing required time or light fields")
        if not actual.issubset(PIPELINE_NON_NULL_FIELDS[self.pipeline]):
            raise ValueError("capabilities do not match the selected render pipeline")
        return self
