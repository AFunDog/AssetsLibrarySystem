from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml


@dataclass(slots=True)
class PromptTemplate:
    system_prompt: str
    prompt: str


def load_prompt_config(prompts_path: str | Path) -> dict[str, Any]:
    path = Path(prompts_path)
    if not path.exists():
        raise FileNotFoundError(f"prompt 配置不存在: {path}")
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    if not isinstance(data, dict):
        raise ValueError(f"prompt 配置格式错误: {path}")
    return data


def extract_prompt_templates(prompt_config: dict[str, Any]) -> dict[str, PromptTemplate]:
    templates: dict[str, PromptTemplate] = {}
    for asset_format, payload in prompt_config.items():
        if not isinstance(payload, dict):
            continue

        system_prompt = payload.get("system_prompt")
        prompt = payload.get("prompt", "")
        if isinstance(system_prompt, str):
            templates[asset_format] = PromptTemplate(
                system_prompt=system_prompt,
                prompt=prompt if isinstance(prompt, str) else "",
            )

    return templates
