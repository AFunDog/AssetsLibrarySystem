from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml


def load_prompt_config(prompts_path: str | Path) -> dict[str, Any]:
    path = Path(prompts_path)
    if not path.exists():
        raise FileNotFoundError(f"prompt 配置不存在: {path}")
    with path.open("r", encoding="utf-8") as f:
        data = yaml.safe_load(f) or {}
    if not isinstance(data, dict):
        raise ValueError(f"prompt 配置格式错误: {path}")
    return data


def extract_system_prompts(prompt_config: dict[str, Any]) -> dict[str, str]:
    prompts: dict[str, str] = {}
    for slot, payload in prompt_config.items():
        if not isinstance(payload, dict):
            continue
        prompt = payload.get("system_prompt")
        if isinstance(prompt, str):
            prompts[slot] = prompt
    return prompts
