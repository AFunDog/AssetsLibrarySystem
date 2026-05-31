from __future__ import annotations

from dataclasses import dataclass
import importlib.util
from pathlib import Path
from typing import Any

from app.core.prompt_config import extract_system_prompts, load_prompt_config
from app.core.provider_config import ProviderConfig, ProviderConfigManager
from app.schemas.model import (
    ModelCapabilitiesResponse,
    ModelGenerateRequest,
    ModelGenerateResponse,
)

DEFAULT_PROVIDER_SLOT = "llm_gateway"
LEGACY_PROVIDER_SLOT = "asset_describer"
DEFAULT_BACKEND_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_PROVIDER_PATH = DEFAULT_BACKEND_ROOT / "configs/providers.yaml"
DEFAULT_PROMPTS_PATH = DEFAULT_BACKEND_ROOT / "configs/prompts.yaml"
DEFAULT_SYSTEM_PROMPT = (
    "你是 Assets Library System 的模型网关。"
    "你只负责响应桌面端透传过来的提示词，不承担素材目录或文件管理。"
)


@dataclass(slots=True)
class ModelRuntimeContext:
    provider_slot: str
    config_slot: str
    provider: str
    model: str
    system_prompt: str
    supports_live_call: bool


class ModelService:
    """Python 端只保留大模型 HTTP 能力。"""

    def __init__(
        self,
        providers_path: str | Path = DEFAULT_PROVIDER_PATH,
        prompts_path: str | Path = DEFAULT_PROMPTS_PATH,
    ) -> None:
        self._providers_path = Path(providers_path)
        self._prompts_path = Path(prompts_path)

    def get_capabilities(self, provider_slot: str = DEFAULT_PROVIDER_SLOT) -> ModelCapabilitiesResponse:
        context = self._resolve_context(provider_slot)
        return ModelCapabilitiesResponse(
            provider_slot=context.provider_slot,
            provider=context.provider,
            model=context.model,
            supports_live_call=context.supports_live_call,
            description="桌面端通过该 HTTP 服务调用大模型；素材管理逻辑保留在 Avalonia/.NET。",
        )

    async def generate_text(self, payload: ModelGenerateRequest) -> ModelGenerateResponse:
        context = self._resolve_context(payload.provider_slot)

        if payload.mock_response or not context.supports_live_call:
            return ModelGenerateResponse(
                provider_slot=context.provider_slot,
                provider=context.provider,
                model=context.model,
                mode="mock",
                output_text=self._build_mock_output(payload.prompt),
                system_prompt=context.system_prompt,
            )

        if context.provider.lower() != "dashscope":
            raise ValueError(f"当前仅实现 dashscope live 调用，实际 provider 为: {context.provider}")

        output_text = await self._call_dashscope(context, payload)
        return ModelGenerateResponse(
            provider_slot=context.provider_slot,
            provider=context.provider,
            model=context.model,
            mode="live",
            output_text=output_text,
            system_prompt=context.system_prompt,
        )

    def _resolve_context(self, provider_slot: str) -> ModelRuntimeContext:
        provider_manager = ProviderConfigManager(self._providers_path)
        resolved_slot = self._resolve_provider_slot(provider_manager, provider_slot)
        provider = provider_manager.get(resolved_slot)
        system_prompt = self._load_system_prompt(resolved_slot)
        return ModelRuntimeContext(
            provider_slot=provider_slot,
            config_slot=resolved_slot,
            provider=provider.provider,
            model=provider.model,
            system_prompt=system_prompt,
            supports_live_call=self._supports_live_call(provider),
        )

    def _resolve_provider_slot(
        self,
        provider_manager: ProviderConfigManager,
        requested_slot: str,
    ) -> str:
        raw = getattr(provider_manager, "_raw", {})
        if requested_slot in raw:
            return requested_slot
        if requested_slot == DEFAULT_PROVIDER_SLOT and LEGACY_PROVIDER_SLOT in raw:
            return LEGACY_PROVIDER_SLOT
        if isinstance(raw, dict) and raw:
            return next(iter(raw))
        raise KeyError(f"provider 槽位不存在: {requested_slot}")

    def _load_system_prompt(self, provider_slot: str) -> str:
        prompt_config = load_prompt_config(self._prompts_path)
        prompts = extract_system_prompts(prompt_config)
        if provider_slot in prompts:
            return prompts[provider_slot].strip() or DEFAULT_SYSTEM_PROMPT
        if provider_slot == LEGACY_PROVIDER_SLOT and DEFAULT_PROVIDER_SLOT in prompts:
            return prompts[DEFAULT_PROVIDER_SLOT].strip() or DEFAULT_SYSTEM_PROMPT
        if provider_slot == DEFAULT_PROVIDER_SLOT and LEGACY_PROVIDER_SLOT in prompts:
            return prompts[LEGACY_PROVIDER_SLOT].strip() or DEFAULT_SYSTEM_PROMPT
        return DEFAULT_SYSTEM_PROMPT

    async def _call_dashscope(self, context: ModelRuntimeContext, payload: ModelGenerateRequest) -> str:
        provider_manager = ProviderConfigManager(self._providers_path)
        provider_config = provider_manager.get(context.config_slot)
        return await __import__("asyncio").to_thread(
            self._call_dashscope_sync,
            provider_config,
            context.system_prompt,
            payload,
        )

    def _supports_live_call(self, provider: ProviderConfig) -> bool:
        if not provider.api_key:
            return False
        return importlib.util.find_spec("dashscope") is not None

    def _call_dashscope_sync(
        self,
        provider_config: ProviderConfig,
        system_prompt: str,
        payload: ModelGenerateRequest,
    ) -> str:
        try:
            from dashscope import Generation
        except ImportError as exc:
            raise RuntimeError("live 模式需要安装 `dashscope` 包。") from exc

        messages: list[dict[str, str]] = [{"role": "system", "content": payload.system_prompt or system_prompt}]
        for item in payload.messages:
            messages.append({"role": item.role, "content": item.content})
        messages.append({"role": "user", "content": payload.prompt})

        response = Generation.call(
            api_key=provider_config.api_key,
            model=provider_config.model,
            messages=messages,
            temperature=provider_config.temperature,
            max_tokens=provider_config.max_tokens,
            result_format="message",
        )

        return self._extract_response_text(response)

    def _extract_response_text(self, response: Any) -> str:
        output = getattr(response, "output", None)
        if output is None and isinstance(response, dict):
            output = response.get("output")

        choices = getattr(output, "choices", None)
        if choices is None and isinstance(output, dict):
            choices = output.get("choices")

        if not choices:
            raise RuntimeError(f"无法解析模型响应: {response}")

        message = choices[0].get("message") if isinstance(choices[0], dict) else getattr(choices[0], "message", None)
        if message is None:
            raise RuntimeError(f"无法解析模型消息体: {response}")

        content = message.get("content") if isinstance(message, dict) else getattr(message, "content", None)
        if isinstance(content, str):
            return content
        if isinstance(content, list):
            parts: list[str] = []
            for item in content:
                if isinstance(item, dict) and "text" in item:
                    parts.append(str(item["text"]))
                else:
                    parts.append(str(item))
            return "\n".join(parts).strip()

        raise RuntimeError(f"无法解析模型输出文本: {response}")

    def _build_mock_output(self, prompt: str) -> str:
        clipped = prompt.strip().replace("\r", " ").replace("\n", " ")
        if len(clipped) > 96:
            clipped = f"{clipped[:96]}..."
        return (
            "当前处于桌面端联调阶段，Python 后端仅保留模型网关能力。"
            f" 这是针对提示词“{clipped}”返回的占位响应，后续可替换为真实 provider 调用。"
        )
