from __future__ import annotations

import importlib.util

from app.core.provider_config import ProviderConfig, ProviderConfigManager


class ProviderResolver:
    """通过 ProviderConfigManager 的公开接口解析模型槽位。"""

    def __init__(
        self,
        manager: ProviderConfigManager,
        default_slot: str,
        legacy_slot: str,
        asset_slots: dict[str, str],
    ) -> None:
        self.manager = manager
        self.default_slot = default_slot
        self.legacy_slot = legacy_slot
        self.asset_slots = asset_slots

    def resolve_slot(self, requested_slot: str) -> str:
        if self.manager.has_slot(requested_slot):
            return requested_slot
        if requested_slot == self.default_slot and self.manager.has_slot(self.legacy_slot):
            return self.legacy_slot
        return self._first_slot_or_raise(f"provider 槽位不存在: {requested_slot}")

    def resolve_asset_slot(self, asset_format: str) -> str:
        preferred_slot = self.asset_slots.get(asset_format, asset_format)
        for slot in (preferred_slot, self.default_slot, self.legacy_slot):
            if self.manager.has_slot(slot):
                return slot
        return self._first_slot_or_raise(f"素材类型的 provider 槽位不存在: {asset_format}")

    @staticmethod
    def supports_live_call(provider: ProviderConfig) -> bool:
        if not provider.api_key:
            return False
        provider_name = provider.provider.lower()
        if provider_name == "dashscope":
            import importlib
            return importlib.util.find_spec("dashscope") is not None
        if provider_name == "openai":
            # OpenAI 兼容 API 只需要 httpx（FastAPI 已依赖）
            import importlib
            return importlib.util.find_spec("httpx") is not None
        # 未知 provider，保守返回 False
        return False

    def _first_slot_or_raise(self, message: str) -> str:
        for slot in self.manager.slots():
            return slot
        raise KeyError(message)
