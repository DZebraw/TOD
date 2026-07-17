from __future__ import annotations

import base64
import json

from dawn_tod_ai_service.configuration import load_api_key


def write_config(path, ciphertext: bytes = b"encrypted"):
    path.write_text(
        json.dumps(
            {
                "version": 1,
                "api_key": {
                    "protection": "dpapi-current-user",
                    "ciphertext": base64.b64encode(ciphertext).decode("ascii"),
                },
            }
        ),
        encoding="utf-8",
    )


def test_valid_dpapi_configuration_returns_decrypted_key(tmp_path):
    path = tmp_path / "config.json"
    write_config(path)

    result = load_api_key(path, unprotect=lambda value: b"sk-secret" if value == b"encrypted" else b"")

    assert result.ready is True
    assert result.api_key == "sk-secret"
    assert result.errors == ()


def test_missing_configuration_reports_stable_error(tmp_path):
    result = load_api_key(tmp_path / "missing.json", unprotect=lambda _: b"unused")

    assert result.ready is False
    assert result.errors == ("API_KEY_MISSING",)


def test_duplicate_or_unknown_configuration_fields_are_rejected(tmp_path):
    path = tmp_path / "config.json"
    path.write_text(
        '{"version":1,"version":1,"api_key":{"protection":"dpapi-current-user","ciphertext":"YQ=="}}',
        encoding="utf-8",
    )

    result = load_api_key(path, unprotect=lambda _: b"sk-secret")

    assert result.errors == ("API_KEY_CONFIG_INVALID",)


def test_dpapi_failure_does_not_expose_ciphertext(tmp_path):
    path = tmp_path / "config.json"
    write_config(path, b"sensitive-ciphertext")

    def fail(_: bytes) -> bytes:
        raise OSError("sensitive-ciphertext")

    result = load_api_key(path, unprotect=fail)

    assert result.api_key is None
    assert result.errors == ("API_KEY_DECRYPT_FAILED",)
