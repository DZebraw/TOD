from __future__ import annotations

import asyncio
import os

import pytest

from dawn_tod_ai_service.parent_monitor import is_process_alive, monitor_parent_process


def test_current_process_is_alive():
    assert is_process_alive(os.getpid()) is True


def test_invalid_process_is_not_alive():
    assert is_process_alive(-1) is False


@pytest.mark.asyncio
async def test_missing_parent_requests_shutdown():
    called = asyncio.Event()

    await asyncio.wait_for(
        monitor_parent_process(-1, called.set, interval_seconds=0.001),
        timeout=0.5,
    )

    assert called.is_set()
