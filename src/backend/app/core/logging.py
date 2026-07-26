from __future__ import annotations

import logging
import os
from logging.handlers import TimedRotatingFileHandler
from pathlib import Path

from app.core.paths import resolve_data_root


_LOG_FORMAT = "%(asctime)s.%(msecs)03d +08:00 [%(levelname)s] %(name)s: %(message)s"
_LOG_DATE_FORMAT = "%Y-%m-%d %H:%M:%S"
_LOG_ENCODING = "utf-8"
_LOG_RETENTION_DAYS = 14


def setup_logging(log_level: str | None = None) -> logging.Logger:
    """Configure root logger with file (daily rotation) and console handlers.

    Log file is written to ``{data_root}/logs/backend-{YYYY-MM-DD}.log``.
    Safe to call multiple times — only the first call configures handlers.
    """
    root_logger = logging.getLogger()
    if root_logger.handlers:
        return root_logger

    resolved_level = _resolve_log_level(log_level)
    root_logger.setLevel(resolved_level)

    log_dir = _resolve_log_dir()
    log_dir.mkdir(parents=True, exist_ok=True)

    log_file = log_dir / f"backend-{_today()}.log"

    file_handler = TimedRotatingFileHandler(
        filename=str(log_file),
        when="midnight",
        interval=1,
        backupCount=_LOG_RETENTION_DAYS,
        encoding=_LOG_ENCODING,
    )
    file_handler.setLevel(resolved_level)
    file_handler.setFormatter(_create_formatter())

    console_handler = logging.StreamHandler()
    console_handler.setLevel(resolved_level)
    console_handler.setFormatter(_create_formatter())

    root_logger.addHandler(file_handler)
    root_logger.addHandler(console_handler)

    root_logger.info(
        "日志系统已初始化，日志文件: %s (level=%s, retention=%d days)",
        log_file,
        logging.getLevelName(resolved_level),
        _LOG_RETENTION_DAYS,
    )
    return root_logger


def _resolve_log_level(log_level: str | None) -> int:
    level_name = (log_level or os.environ.get("LOG_LEVEL", "") or "INFO").strip().upper()
    return getattr(logging, level_name, logging.INFO)


def _resolve_log_dir() -> Path:
    try:
        data_root = resolve_data_root()
        return data_root / "logs"
    except Exception:
        return Path.cwd() / "logs"


def _create_formatter() -> logging.Formatter:
    return logging.Formatter(fmt=_LOG_FORMAT, datefmt=_LOG_DATE_FORMAT)


def _today() -> str:
    from datetime import date

    return date.today().isoformat()