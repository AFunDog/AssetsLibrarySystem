from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import os
from typing import Any

import yaml


@dataclass(slots=True)
class ProviderConfig:
    provider: str
    model: str
    base_url: str
    api_key: str
    temperature: float = 0.2
    max_tokens: int = 1024
    reasoning_effort: str | None = None
    extra_body: dict[str, Any] | None = None


class ProviderConfigManager:
    """加载后端 LLM 提供商配置。"""

    def __init__(self, config_path: str | Path) -> None:
        self.config_path = Path(config_path)
        self._raw = self._load()

    def _load(self) -> dict[str, Any]:
        if not self.config_path.exists():
            raise FileNotFoundError(f"provider 配置不存在: {self.config_path}")
        with self.config_path.open("r", encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
        if not isinstance(data, dict):
            raise ValueError(f"provider 配置格式错误: {self.config_path}")
        return data

    def get(self, slot: str) -> ProviderConfig:
        item = self._raw.get(slot)
        if not isinstance(item, dict):
            raise KeyError(f"provider 槽位不存在: {slot}")

        api_key = str(item.get("api_key") or "").strip()
        provider = str(item.get("provider") or "").strip()
        model = str(item.get("model") or "").strip()
        base_url = str(item.get("base_url") or "").strip()
        if not api_key:
            api_key = os.getenv("DASHSCOPE_API_KEY", "") if provider == "dashscope" else ""

        return ProviderConfig(
            provider=provider,
            model=model,
            base_url=base_url,
            api_key=api_key,
            temperature=float(item.get("temperature") or 0.2),
            max_tokens=int(item.get("max_tokens") or 1024),
            reasoning_effort=item.get("reasoning_effort"),
            extra_body=item.get("extra_body") or {},
        )
