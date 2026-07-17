from __future__ import annotations

from dawn_tod_ai_service.service_logging import create_service_logger


def test_logs_rotate_to_at_most_ten_files_and_redact_secrets(tmp_path):
    secret = "sk-never-write-this"
    logger = create_service_logger(
        secret,
        log_directory=tmp_path,
        max_bytes=180,
        backup_count=9,
        logger_name="dawn_tod_ai_service.test.rotation",
    )

    for index in range(80):
        logger.error(
            "request_failed index=%d key=%s Authorization: Bearer %s",
            index,
            secret,
            secret,
        )
    for handler in logger.handlers:
        handler.flush()
        handler.close()

    files = list(tmp_path.glob("service.log*"))
    contents = "\n".join(path.read_text(encoding="utf-8") for path in files)
    assert 1 < len(files) <= 10
    assert secret not in contents
    assert "[REDACTED]" in contents
