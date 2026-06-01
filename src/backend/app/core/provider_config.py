from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any

import yaml

from app.core.config import get_settings


@dataclass(slots=True)
class ProviderConfig:
    provider: str
    model: str
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
        self._shared_api_key = self._load_shared_api_key()

    def _load(self) -> dict[str, Any]:
        path = self.config_path
        if not path.exists():
            example_path = path.with_name(f"{path.stem}.example{path.suffix}")
            if example_path.exists():
                path = example_path
            else:
                raise FileNotFoundError(f"provider 配置不存在: {self.config_path}")

        with path.open("r", encoding="utf-8") as f:
            data = yaml.safe_load(f) or {}
        if not isinstance(data, dict):
            raise ValueError(f"provider 配置格式错误: {path}")
        return data

    def _load_shared_api_key(self) -> str:
        api_key = str(self._raw.get("api_key") or "").strip()
        if api_key:
            return api_key

        settings = get_settings()
        return str(getattr(settings, "dashscope_api_key", "") or "").strip()

    def get(self, slot: str) -> ProviderConfig:
        item = self._raw.get(slot)
        if not isinstance(item, dict):
            raise KeyError(f"provider 槽位不存在: {slot}")

        api_key = str(item.get("api_key") or "").strip()
        provider = str(item.get("provider") or "").strip()
        model = str(item.get("model") or "").strip()
        if not api_key:
            api_key = self._shared_api_key

        return ProviderConfig(
            provider=provider,
            model=model,
            api_key=api_key,
            temperature=float(item.get("temperature") or 0.2),
            max_tokens=int(item.get("max_tokens") or 1024),
            reasoning_effort=item.get("reasoning_effort"),
            extra_body=item.get("extra_body") or {},
        )
