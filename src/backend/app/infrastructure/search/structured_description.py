from __future__ import annotations

import json


_PRIMARY_ANGLE_KEYS = ("整体", "全面")


def extract_primary_description(raw_description: str | None) -> str:
    return extract_description_by_angle(raw_description, "整体")


def extract_description_by_angle(raw_description: str | None, angle_type: str | None) -> str:
    if raw_description is None:
        return ""

    trimmed = raw_description.strip()
    if not trimmed:
        return ""

    if not trimmed.startswith("{"):
        return trimmed

    try:
        payload = json.loads(trimmed)
    except json.JSONDecodeError:
        return trimmed

    if not isinstance(payload, dict):
        return trimmed

    normalized_angle_type = (angle_type or "整体").strip() or "整体"
    if normalized_angle_type in payload:
        angle_value = payload.get(normalized_angle_type)
        text = _extract_text(angle_value)
        if text:
            return text

    # 兼容历史主角度「全面」与当前「整体」
    for primary_key in _PRIMARY_ANGLE_KEYS:
        text = _extract_text(payload.get(primary_key))
        if text:
            return text

    return trimmed


def _extract_text(value: object) -> str:
    if isinstance(value, str):
        return value.strip()

    if isinstance(value, dict):
        text = value.get("text")
        if isinstance(text, str):
            return text.strip()

    return ""
