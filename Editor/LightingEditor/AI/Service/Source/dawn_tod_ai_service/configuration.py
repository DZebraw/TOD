"""Load the current Windows user's DPAPI-protected DeepSeek API key."""

from __future__ import annotations

import base64
import ctypes
import json
import os
from collections.abc import Callable
from ctypes import wintypes
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .constants import (
    CONFIG_PATH_ENV,
    CONFIG_PROTECTION,
    CONFIG_VERSION,
)


@dataclass(frozen=True)
class ApiKeyLoadResult:
    api_key: str | None
    errors: tuple[str, ...]

    @property
    def ready(self) -> bool:
        return self.api_key is not None and not self.errors


class _DataBlob(ctypes.Structure):
    _fields_ = [
        ("cbData", wintypes.DWORD),
        ("pbData", ctypes.POINTER(ctypes.c_ubyte)),
    ]


def default_config_path() -> Path:
    override = os.environ.get(CONFIG_PATH_ENV, "").strip()
    if override:
        return Path(override).expanduser().resolve()

    local_app_data = os.environ.get("LOCALAPPDATA", "").strip()
    if not local_app_data:
        raise OSError("LOCALAPPDATA is unavailable")
    return Path(local_app_data) / "DawnTODAI" / "config.json"


def dpapi_unprotect(ciphertext: bytes) -> bytes:
    if os.name != "nt" or not ciphertext:
        raise OSError("DPAPI is unavailable")

    encrypted_buffer = (ctypes.c_ubyte * len(ciphertext)).from_buffer_copy(ciphertext)
    encrypted_blob = _DataBlob(
        len(ciphertext),
        ctypes.cast(encrypted_buffer, ctypes.POINTER(ctypes.c_ubyte)),
    )
    decrypted_blob = _DataBlob()

    crypt32 = ctypes.WinDLL("crypt32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    crypt32.CryptUnprotectData.argtypes = (
        ctypes.POINTER(_DataBlob),
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.c_void_p,
        wintypes.DWORD,
        ctypes.POINTER(_DataBlob),
    )
    crypt32.CryptUnprotectData.restype = wintypes.BOOL
    kernel32.LocalFree.argtypes = (ctypes.c_void_p,)
    kernel32.LocalFree.restype = ctypes.c_void_p

    cryptprotect_ui_forbidden = 0x1
    if not crypt32.CryptUnprotectData(
        ctypes.byref(encrypted_blob),
        None,
        None,
        None,
        None,
        cryptprotect_ui_forbidden,
        ctypes.byref(decrypted_blob),
    ):
        raise OSError(ctypes.get_last_error(), "DPAPI decryption failed")

    try:
        return ctypes.string_at(decrypted_blob.pbData, decrypted_blob.cbData)
    finally:
        if decrypted_blob.pbData:
            kernel32.LocalFree(decrypted_blob.pbData)


def load_api_key(
    path: Path | None = None,
    unprotect: Callable[[bytes], bytes] | None = None,
) -> ApiKeyLoadResult:
    try:
        config_path = path or default_config_path()
    except OSError:
        return ApiKeyLoadResult(None, ("API_KEY_CONFIG_PATH_UNAVAILABLE",))

    try:
        raw = config_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        return ApiKeyLoadResult(None, ("API_KEY_MISSING",))
    except (OSError, UnicodeError):
        return ApiKeyLoadResult(None, ("API_KEY_CONFIG_UNREADABLE",))

    try:
        document = json.loads(raw, object_pairs_hook=_object_without_duplicates)
        ciphertext = _read_ciphertext(document)
    except (ValueError, TypeError, KeyError, json.JSONDecodeError):
        return ApiKeyLoadResult(None, ("API_KEY_CONFIG_INVALID",))

    try:
        decrypted = (unprotect or dpapi_unprotect)(ciphertext)
        api_key = decrypted.decode("utf-8").strip()
    except (OSError, UnicodeError, ValueError):
        return ApiKeyLoadResult(None, ("API_KEY_DECRYPT_FAILED",))

    if not api_key:
        return ApiKeyLoadResult(None, ("API_KEY_MISSING",))
    return ApiKeyLoadResult(api_key, ())


def _object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError("duplicate configuration field")
        value[key] = item
    return value


def _read_ciphertext(document: Any) -> bytes:
    if not isinstance(document, dict) or set(document) != {"version", "api_key"}:
        raise ValueError("invalid configuration root")
    if document["version"] != CONFIG_VERSION or isinstance(document["version"], bool):
        raise ValueError("unsupported configuration version")

    api_key = document["api_key"]
    if not isinstance(api_key, dict) or set(api_key) != {"protection", "ciphertext"}:
        raise ValueError("invalid API key record")
    if api_key["protection"] != CONFIG_PROTECTION:
        raise ValueError("unsupported key protection")
    encoded = api_key["ciphertext"]
    if not isinstance(encoded, str) or not encoded:
        raise ValueError("missing encrypted key")
    try:
        decoded = base64.b64decode(encoded, validate=True)
    except (ValueError, TypeError) as exception:
        raise ValueError("invalid encrypted key") from exception
    if not decoded:
        raise ValueError("missing encrypted key")
    return decoded
