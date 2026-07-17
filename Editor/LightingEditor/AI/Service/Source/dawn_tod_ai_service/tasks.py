"""Concurrency-safe single-task cancellation state."""

from __future__ import annotations

import asyncio
from dataclasses import dataclass


class TaskBusyError(RuntimeError):
    pass


@dataclass
class _TaskState:
    request_id: str
    cancelled: bool = False


class SingleTaskRegistry:
    def __init__(self) -> None:
        self._lock = asyncio.Lock()
        self._active: _TaskState | None = None

    async def begin(self, request_id: str) -> None:
        async with self._lock:
            if self._active is not None:
                raise TaskBusyError("another analysis task is active")
            self._active = _TaskState(request_id)

    async def cancel(self, request_id: str) -> bool:
        async with self._lock:
            if self._active is None or self._active.request_id != request_id:
                return False
            self._active.cancelled = True
            return True

    async def finish_and_can_publish(self, request_id: str) -> bool:
        async with self._lock:
            if self._active is None or self._active.request_id != request_id:
                return False
            can_publish = not self._active.cancelled
            self._active = None
            return can_publish

    async def abandon(self, request_id: str) -> None:
        async with self._lock:
            if self._active is not None and self._active.request_id == request_id:
                self._active = None
