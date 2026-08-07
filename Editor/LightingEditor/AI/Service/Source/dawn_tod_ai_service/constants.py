"""Versioned protocol constants for the DawnTOD DeepSeek service."""

from typing import Final

HOST: Final = "127.0.0.1"
PORT: Final = 13296
MODE: Final = "deepseek"
SERVICE_VERSION: Final = "2.1.0"
SCHEMA_VERSION: Final = "1.1"
SESSION_TOKEN_HEADER: Final = "X-DawnTOD-Session-Token"
SESSION_TOKEN_ENV: Final = "DAWN_TOD_AI_SESSION_TOKEN"
PARENT_PID_ENV: Final = "DAWN_TOD_AI_PARENT_PID"
CONFIG_PATH_ENV: Final = "DAWN_TOD_AI_CONFIG_PATH"

DEEPSEEK_BASE_URL: Final = "https://api.deepseek.com"
DEEPSEEK_CHAT_COMPLETIONS_URL: Final = f"{DEEPSEEK_BASE_URL}/chat/completions"
DEEPSEEK_MODEL: Final = "deepseek-v4-flash"
DEEPSEEK_TIMEOUT_SECONDS: Final = 60.0
DEEPSEEK_MAX_RETRIES: Final = 2
DEEPSEEK_MAX_TOKENS: Final = 4096

CONFIG_VERSION: Final = 1
CONFIG_PROTECTION: Final = "dpapi-current-user"
LOG_DIRECTORY_NAME: Final = "logs"
LOG_FILE_NAME: Final = "service.log"
LOG_MAX_BYTES: Final = 5 * 1024 * 1024
LOG_BACKUP_COUNT: Final = 9

CORE_NON_NULL_FIELDS: Final = (
    "time",
    "sun.azimuth_deg",
    "sun.elevation_deg",
    "sun.intensity",
    "sun.color",
    "moon.azimuth_deg",
    "moon.elevation_deg",
    "moon.intensity",
    "moon.color",
)

URP_NON_NULL_FIELDS: Final = CORE_NON_NULL_FIELDS + (
    "sky.star_emission",
    "fog.mean_free_path_m",
    "fog.base_height_m",
    "fog.color",
    "rain.enabled",
    "rain.precipitation_amount",
    "rain.fall_speed",
    "rain.density",
    "rain.wind_z_rotation_deg",
)

HDRP_NON_NULL_FIELDS: Final = URP_NON_NULL_FIELDS + (
    "exposure.compensation_ev",
)

SUPPORTED_NON_NULL_FIELDS: Final = HDRP_NON_NULL_FIELDS
PIPELINE_NON_NULL_FIELDS: Final = {
    "URP": frozenset(URP_NON_NULL_FIELDS),
    "HDRP": frozenset(HDRP_NON_NULL_FIELDS),
}

# A known-valid document used only to verify the packaged Schema at startup and in tests.
VALIDATION_PROBE_DATA: Final = {
    "schema_version": SCHEMA_VERSION,
    "time": {"mode": "explicit", "hour": 12.0},
    "sun": {
        "azimuth_deg": 270.0,
        "elevation_deg": 60.0,
        "intensity": 2.0,
        "color": {"r": 1.0, "g": 0.98, "b": 0.92, "a": 1.0},
    },
    "moon": {
        "azimuth_deg": 90.0,
        "elevation_deg": -60.0,
        "intensity": 0.2,
        "color": {"r": 0.7, "g": 0.8, "b": 1.0, "a": 1.0},
    },
    "sky": {"star_emission": 1000.0},
    "fog": {
        "mean_free_path_m": 500.0,
        "base_height_m": 0.02,
        "color": {"r": 0.7, "g": 0.8, "b": 0.9, "a": 1.0},
    },
    "exposure": {"compensation_ev": 0.0},
    "rain": {
        "enabled": True,
        "precipitation_amount": 0.6,
        "fall_speed": 40.0,
        "density": 1.0,
        "wind_z_rotation_deg": 15.0,
    },
}
