from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import yaml

from app.core.config import get_settings


@dataclass(slots=True)
class PricingTier:
    max_input_tokens: int
    price_per_million: float


@dataclass(slots=True)
class PricingConfig:
    """模型计费配置（按官方定价页写入 providers.yaml 的 pricing 段）。

    阶梯按「单次请求输入 token 数」分档，输入/输出各自独立档位。
    """

    currency: str = "CNY"
    input_tiers: list[PricingTier] = field(default_factory=list)
    output_tiers: list[PricingTier] = field(default_factory=list)

    @classmethod
    def from_mapping(cls, raw: Any) -> "PricingConfig | None":
        if not isinstance(raw, dict):
            return None
        return cls(
            currency=str(raw.get("currency") or "CNY"),
            input_tiers=_parse_tiers(raw.get("input_tiers")),
            output_tiers=_parse_tiers(raw.get("output_tiers")),
        )

    def estimate_cost_cny(self, input_tokens: int, output_tokens: int) -> float | None:
        """按输入 token 数所在档位估算费用（元）；未配置价格时返回 None。"""
        input_price = _tier_price(self.input_tiers, input_tokens)
        output_price = _tier_price(self.output_tiers, input_tokens)
        if input_price is None or output_price is None:
            return None
        return input_tokens / 1_000_000 * input_price + output_tokens / 1_000_000 * output_price


def _parse_tiers(raw: Any) -> list[PricingTier]:
    if not isinstance(raw, list):
        return []
    tiers: list[PricingTier] = []
    for item in raw:
        if not isinstance(item, dict):
            continue
        max_tokens = item.get("max_input_tokens")
        price = item.get("price_per_million")
        if max_tokens is None or price is None:
            continue
        tiers.append(PricingTier(int(max_tokens), float(price)))
    return sorted(tiers, key=lambda t: t.max_input_tokens)


def _tier_price(tiers: list[PricingTier], input_tokens: int) -> float | None:
    if not tiers:
        return None
    for tier in tiers:
        if input_tokens <= tier.max_input_tokens:
            return tier.price_per_million
    return tiers[-1].price_per_million


@dataclass(slots=True)
class ProviderConfig:
    provider: str
    model: str
    api_key: str
    base_url: str = ""
    temperature: float = 0.2
    max_tokens: int = 1024
    reasoning_effort: str | None = None
    extra_body: dict[str, Any] | None = None
    pricing: PricingConfig | None = None


def _expand_env_var(value: str) -> str:
    """展开 ${VAR} 环境变量占位符；变量不存在时返回空串。"""
    import os
    import re

    def _replace(match: re.Match[str]) -> str:
        return os.environ.get(match.group(1) or "", "")

    return re.sub(r"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", _replace, value.strip())


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
        api_key = _expand_env_var(str(self._raw.get("api_key") or ""))
        if api_key:
            return api_key

        settings = get_settings()
        return str(getattr(settings, "dashscope_api_key", "") or "").strip()

    def get(self, slot: str) -> ProviderConfig:
        item = self._raw.get(slot)
        if not isinstance(item, dict):
            raise KeyError(f"provider 槽位不存在: {slot}")

        api_key = _expand_env_var(str(item.get("api_key") or ""))
        provider = str(item.get("provider") or "").strip()
        model = str(item.get("model") or "").strip()
        if not api_key:
            api_key = self._shared_api_key

        return ProviderConfig(
            provider=provider,
            model=model,
            api_key=api_key,
            base_url=str(item.get("base_url") or "").strip(),
            # 显式兼容 temperature=0 / max_tokens=0 的合法配置，
            # 避免 `or 默认值` 把确定性输出（0）静默改成默认采样。
            temperature=float(item.get("temperature") if item.get("temperature") is not None else 0.2),
            max_tokens=int(item.get("max_tokens") if item.get("max_tokens") is not None else 1024),
            reasoning_effort=item.get("reasoning_effort"),
            extra_body=item.get("extra_body") or {},
            pricing=PricingConfig.from_mapping(item.get("pricing")),
        )

    def has_slot(self, slot: str) -> bool:
        return isinstance(self._raw.get(slot), dict)

    def slots(self) -> tuple[str, ...]:
        return tuple(slot for slot, value in self._raw.items() if isinstance(value, dict))
