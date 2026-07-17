"""Local rotating service logs with API-key and Authorization redaction."""

from __future__ import annotations

import logging
import os
import re
from logging.handlers import RotatingFileHandler
from pathlib import Path

from .constants import (
    LOG_BACKUP_COUNT,
    LOG_DIRECTORY_NAME,
    LOG_FILE_NAME,
    LOG_MAX_BYTES,
)

_AUTHORIZATION = re.compile(
    r"(?i)(authorization\s*[:=]\s*bearer\s+)([^\s,;]+)"
)


class RedactingFormatter(logging.Formatter):
    def __init__(self, api_key: str | None) -> None:
        super().__init__(
            fmt="%(asctime)sZ %(levelname)s %(message)s",
            datefmt="%Y-%m-%dT%H:%M:%S",
        )
        self._api_key = api_key or ""
        self.converter = __import__("time").gmtime

    def format(self, record: logging.LogRecord) -> str:
        rendered = super().format(record)
        if self._api_key:
            rendered = rendered.replace(self._api_key, "[REDACTED]")
        return _AUTHORIZATION.sub(r"\1[REDACTED]", rendered)


def default_log_directory() -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA", "").strip()
    if not local_app_data:
        raise OSError("LOCALAPPDATA is unavailable")
    return Path(local_app_data) / "DawnTODAI" / LOG_DIRECTORY_NAME


def create_service_logger(
    api_key: str | None,
    log_directory: Path | None = None,
    max_bytes: int = LOG_MAX_BYTES,
    backup_count: int = LOG_BACKUP_COUNT,
    logger_name: str = "dawn_tod_ai_service",
) -> logging.Logger:
    if max_bytes <= 0 or backup_count < 0:
        raise ValueError("invalid log rotation settings")

    directory = log_directory or default_log_directory()
    directory.mkdir(parents=True, exist_ok=True)
    logger = logging.getLogger(logger_name)
    logger.setLevel(logging.INFO)
    logger.propagate = False
    for existing in tuple(logger.handlers):
        existing.close()
        logger.removeHandler(existing)

    handler = RotatingFileHandler(
        directory / LOG_FILE_NAME,
        maxBytes=max_bytes,
        backupCount=backup_count,
        encoding="utf-8",
        delay=True,
    )
    handler.setFormatter(RedactingFormatter(api_key))
    logger.addHandler(handler)
    return logger


def create_null_logger(logger_name: str = "dawn_tod_ai_service.null") -> logging.Logger:
    logger = logging.getLogger(logger_name)
    logger.setLevel(logging.CRITICAL + 1)
    logger.propagate = False
    if not logger.handlers:
        logger.addHandler(logging.NullHandler())
    return logger
