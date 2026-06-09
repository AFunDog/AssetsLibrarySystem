from __future__ import annotations

import json


def extract_primary_description(raw_description: str | None) -> str:
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

    comprehensive = payload.get("全面")
    if isinstance(comprehensive, str):
        return comprehensive.strip()

    if isinstance(comprehensive, dict):
        text = comprehensive.get("text")
        if isinstance(text, str):
            return text.strip()

    return trimmed
