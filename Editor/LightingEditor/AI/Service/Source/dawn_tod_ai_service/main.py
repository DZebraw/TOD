"""Frozen and source entry point for the loopback-only service."""

from __future__ import annotations

import os
import sys
from pathlib import Path

import uvicorn

from .app import create_app
from .configuration import load_api_key
from .constants import HOST, PARENT_PID_ENV, PORT, SESSION_TOKEN_ENV
from .provider import DeepSeekProvider
from .service_logging import create_null_logger, create_service_logger


def resolve_ai_root() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parents[2]
    return Path(__file__).resolve().parents[3]


def main() -> int:
    session_token = os.environ.pop(SESSION_TOKEN_ENV, "")
    parent_pid_text = os.environ.pop(PARENT_PID_ENV, "")
    if not session_token:
        return 2
    try:
        parent_pid = int(parent_pid_text)
    except ValueError:
        return 2
    if parent_pid <= 0:
        return 2

    server_holder: dict[str, uvicorn.Server] = {}

    def request_shutdown() -> None:
        server = server_holder.get("server")
        if server is not None:
            server.should_exit = True

    api_key_result = load_api_key()
    logging_errors: tuple[str, ...] = ()
    try:
        event_logger = create_service_logger(api_key_result.api_key)
    except (OSError, ValueError):
        event_logger = create_null_logger()
        logging_errors = ("LOG_UNAVAILABLE",)

    provider = (
        DeepSeekProvider(api_key_result.api_key)
        if api_key_result.ready
        else None
    )
    app = create_app(
        resolve_ai_root(),
        session_token,
        request_shutdown=request_shutdown,
        parent_pid=parent_pid,
        provider=provider,
        configuration_errors=api_key_result.errors + logging_errors,
        event_logger=event_logger,
    )
    config = uvicorn.Config(
        app,
        host=HOST,
        port=PORT,
        access_log=False,
        log_level="warning",
        loop="asyncio",
        http="h11",
        lifespan="on",
    )
    server = uvicorn.Server(config)
    server_holder["server"] = server
    server.run()
    return 0
