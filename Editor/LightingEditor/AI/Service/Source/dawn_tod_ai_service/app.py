"""FastAPI application for the versioned DeepSeek weather-intent service."""

from __future__ import annotations

import asyncio
import hmac
import logging
import time
from collections.abc import Callable
from contextlib import asynccontextmanager, suppress
from pathlib import Path
from typing import Any

from fastapi import BackgroundTasks, Depends, FastAPI, Header, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from .constants import MODE, SCHEMA_VERSION, SERVICE_VERSION, SESSION_TOKEN_HEADER
from .contracts import AnalyzeRequest
from .intent_engine import IntentEngineError, WeatherIntentEngine
from .parent_monitor import monitor_parent_process
from .provider import CompletionProvider, ProviderError
from .resources import ResourceBundle, load_resources
from .service_logging import create_null_logger
from .tasks import SingleTaskRegistry, TaskBusyError


class ServiceHttpError(Exception):
    def __init__(self, status_code: int, code: str, message: str, request_id: str = "") -> None:
        super().__init__(message)
        self.status_code = status_code
        self.code = code
        self.message = message
        self.request_id = request_id


def _error_envelope(request_id: str, code: str, message: str) -> dict[str, Any]:
    return {
        "request_id": request_id,
        "status": "error",
        "mode": MODE,
        "data": None,
        "error": {"code": code, "message": message},
    }


def create_app(
    ai_root: Path,
    session_token: str,
    request_shutdown: Callable[[], None] | None = None,
    parent_pid: int | None = None,
    parent_poll_seconds: float = 0.5,
    provider: CompletionProvider | None = None,
    configuration_errors: tuple[str, ...] = (),
    event_logger: logging.Logger | None = None,
) -> FastAPI:
    if not session_token:
        raise ValueError("A non-empty session token is required.")

    resources: ResourceBundle = load_resources(ai_root)
    tasks = SingleTaskRegistry()
    shutdown_callback = request_shutdown or (lambda: None)
    logger = event_logger or create_null_logger()
    readiness_errors = list(resources.errors)
    readiness_errors.extend(configuration_errors)
    if provider is None and not configuration_errors:
        readiness_errors.append("DEEPSEEK_PROVIDER_UNAVAILABLE")
    readiness_errors = list(dict.fromkeys(readiness_errors))
    engine = (
        WeatherIntentEngine(provider, resources)
        if provider is not None and resources.ready and not readiness_errors
        else None
    )

    @asynccontextmanager
    async def lifespan(_: FastAPI):
        monitor: asyncio.Task[None] | None = None
        logger.info("service_started mode=%s version=%s", MODE, SERVICE_VERSION)
        if parent_pid is not None:
            monitor = asyncio.create_task(
                monitor_parent_process(parent_pid, shutdown_callback, parent_poll_seconds)
            )
        try:
            yield
        finally:
            if monitor is not None:
                monitor.cancel()
                with suppress(asyncio.CancelledError):
                    await monitor
            if provider is not None:
                close = getattr(provider, "aclose", None)
                if close is not None:
                    with suppress(Exception):
                        await close()
            logger.info("service_stopped")

    app = FastAPI(
        title="DawnTOD AI Service",
        version=SERVICE_VERSION,
        docs_url=None,
        redoc_url=None,
        openapi_url=None,
        lifespan=lifespan,
    )
    app.state.resources = resources
    app.state.tasks = tasks
    app.state.readiness_errors = tuple(readiness_errors)

    async def authenticate(
        supplied_token: str | None = Header(default=None, alias=SESSION_TOKEN_HEADER),
    ) -> None:
        if supplied_token is None or not hmac.compare_digest(supplied_token, session_token):
            raise ServiceHttpError(401, "UNAUTHORIZED", "The session token is missing or invalid.")

    @app.exception_handler(ServiceHttpError)
    async def handle_service_error(_: Request, exc: ServiceHttpError) -> JSONResponse:
        return JSONResponse(
            status_code=exc.status_code,
            content=_error_envelope(exc.request_id, exc.code, exc.message),
        )

    @app.exception_handler(RequestValidationError)
    async def handle_validation_error(request: Request, _: RequestValidationError) -> JSONResponse:
        request_id = ""
        try:
            body = await request.json()
            if isinstance(body, dict) and isinstance(body.get("request_id"), str):
                request_id = body["request_id"]
        except Exception:
            pass
        return JSONResponse(
            status_code=400,
            content=_error_envelope(request_id, "INVALID_REQUEST", "Request validation failed."),
        )

    @app.get("/status", dependencies=[Depends(authenticate)])
    async def status() -> dict[str, Any]:
        ready = engine is not None and not readiness_errors
        return {
            "status": "ready" if ready else "error",
            "ready": ready,
            "mode": MODE,
            "service_version": SERVICE_VERSION,
            "schema_version": SCHEMA_VERSION,
            "skill_hash": resources.skill_hash,
            "errors": readiness_errors,
        }

    @app.post("/analyze", dependencies=[Depends(authenticate)])
    async def analyze(payload: AnalyzeRequest) -> dict[str, Any]:
        request_id = str(payload.request_id)
        if engine is None or readiness_errors:
            raise ServiceHttpError(
                503,
                "SERVICE_NOT_READY",
                "The versioned service resources or API key are unavailable.",
                request_id,
            )
        if payload.schema_version != SCHEMA_VERSION:
            raise ServiceHttpError(
                409,
                "SCHEMA_VERSION_MISMATCH",
                "The request schema version is not supported.",
                request_id,
            )

        try:
            await tasks.begin(request_id)
        except TaskBusyError:
            raise ServiceHttpError(
                409,
                "TASK_BUSY",
                "Another analysis task is already active.",
                request_id,
            )

        started = time.perf_counter()
        logger.info("request_started request_id=%s", request_id)
        try:
            result = await engine.analyze(payload)
        except asyncio.CancelledError:
            await tasks.abandon(request_id)
            logger.info("request_abandoned request_id=%s", request_id)
            raise
        except ProviderError as exception:
            if not await tasks.finish_and_can_publish(request_id):
                raise _cancelled_error(request_id)
            logger.warning(
                "request_failed request_id=%s code=%s retries=%d duration_ms=%d",
                request_id,
                exception.code,
                exception.retry_count,
                _elapsed_ms(started),
            )
            raise ServiceHttpError(
                exception.http_status,
                exception.code,
                exception.message,
                request_id,
            )
        except IntentEngineError as exception:
            if not await tasks.finish_and_can_publish(request_id):
                raise _cancelled_error(request_id)
            logger.warning(
                "request_failed request_id=%s code=%s retries=%d repairs=%d duration_ms=%d",
                request_id,
                exception.code,
                exception.retry_count,
                exception.repair_count,
                _elapsed_ms(started),
            )
            raise ServiceHttpError(
                exception.http_status,
                exception.code,
                exception.message,
                request_id,
            )
        except Exception:
            if not await tasks.finish_and_can_publish(request_id):
                raise _cancelled_error(request_id)
            logger.error(
                "request_failed request_id=%s code=INTERNAL_ERROR duration_ms=%d",
                request_id,
                _elapsed_ms(started),
            )
            raise ServiceHttpError(
                500,
                "INTERNAL_ERROR",
                "The analysis request failed internally.",
                request_id,
            )

        if not await tasks.finish_and_can_publish(request_id):
            logger.info(
                "request_cancelled request_id=%s duration_ms=%d",
                request_id,
                _elapsed_ms(started),
            )
            raise _cancelled_error(request_id)

        logger.info(
            "request_completed request_id=%s retries=%d repairs=%d duration_ms=%d",
            request_id,
            result.retry_count,
            result.repair_count,
            _elapsed_ms(started),
        )
        return {
            "request_id": request_id,
            "status": "ok",
            "mode": MODE,
            "data": result.data,
            "error": None,
        }

    @app.post("/tasks/{request_id}/cancel", dependencies=[Depends(authenticate)])
    async def cancel(request_id: str) -> dict[str, Any]:
        if not await tasks.cancel(request_id):
            raise ServiceHttpError(
                404,
                "TASK_NOT_FOUND",
                "No active task matches the request id.",
                request_id,
            )
        logger.info("request_cancel_requested request_id=%s", request_id)
        return {
            "request_id": request_id,
            "status": "cancelled",
            "mode": MODE,
            "data": None,
            "error": None,
        }

    @app.post("/shutdown", dependencies=[Depends(authenticate)])
    async def shutdown(background_tasks: BackgroundTasks) -> dict[str, str]:
        logger.info("shutdown_requested")
        background_tasks.add_task(shutdown_callback)
        return {"status": "stopping"}

    return app


def _cancelled_error(request_id: str) -> ServiceHttpError:
    return ServiceHttpError(
        409,
        "TASK_CANCELLED",
        "The analysis task was cancelled.",
        request_id,
    )


def _elapsed_ms(started: float) -> int:
    return max(0, round((time.perf_counter() - started) * 1000))
