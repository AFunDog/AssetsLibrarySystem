from __future__ import annotations

from dataclasses import dataclass
import importlib.util
from pathlib import Path
from typing import Any

from app.core.prompt_config import extract_prompt_templates, load_prompt_config
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
AUDIO_FALLBACK_MODEL = "qwen3-omni-30b-a3b-captioner"
ASSET_PROVIDER_SLOTS = {
    "文本": "文本",
    "图片": "图片",
    "视频": "视频",
    "音频": "音频",
}
DEFAULT_SYSTEM_PROMPT = (
    "你是 Assets Library System 的模型网关。"
    "你只负责响应桌面端透传过来的提示词，不承担素材目录或文件管理。"
)


@dataclass(slots=True)
class ModelRuntimeContext:
    config_slot: str
    provider: str
    model: str
    system_prompt: str
    prompt: str
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
        context = self._resolve_provider_context(provider_slot)
        return ModelCapabilitiesResponse(
            provider_slot=provider_slot,
            provider=context.provider,
            model=context.model,
            supports_live_call=context.supports_live_call,
            description="桌面端通过该 HTTP 服务调用大模型；素材管理逻辑保留在 Avalonia/.NET。",
        )

    async def generate_text(self, payload: ModelGenerateRequest) -> ModelGenerateResponse:
        context = self._resolve_prompt_context(payload.asset_format)
        provider_context = self._resolve_provider_context_for_asset_format(payload.asset_format)
        system_prompt = self._resolve_system_prompt(payload.system_prompt, context.system_prompt)
        prompt = self._resolve_prompt(payload.prompt, context.prompt)
        call_model = self._resolve_model_name(provider_context.model, payload.asset_format)

        if payload.mock_response or not context.supports_live_call:
            return ModelGenerateResponse(
                provider_slot=DEFAULT_PROVIDER_SLOT,
                provider=provider_context.provider,
                model=call_model,
                mode="mock",
                output_text=self._build_mock_output(payload.asset_format, payload.asset_path, prompt),
                system_prompt=system_prompt,
            )

        if provider_context.provider.lower() != "dashscope":
            raise ValueError(f"当前仅实现 dashscope live 调用，实际 provider 为: {provider_context.provider}")

        output_text = await self._call_dashscope(
            provider_context,
            system_prompt,
            prompt,
            payload.asset_format,
            payload.asset_path,
            call_model,
        )
        return ModelGenerateResponse(
            provider_slot=DEFAULT_PROVIDER_SLOT,
            provider=provider_context.provider,
            model=call_model,
            mode="live",
            output_text=output_text,
            system_prompt=system_prompt,
        )

    def _resolve_provider_context(self, provider_slot: str) -> ModelRuntimeContext:
        provider_manager = ProviderConfigManager(self._providers_path)
        resolved_slot = self._resolve_provider_slot(provider_manager, provider_slot)
        provider = provider_manager.get(resolved_slot)
        return ModelRuntimeContext(
            config_slot=resolved_slot,
            provider=provider.provider,
            model=provider.model,
            system_prompt="",
            prompt="",
            supports_live_call=self._supports_live_call(provider),
        )

    def _resolve_prompt_context(self, asset_format: str) -> ModelRuntimeContext:
        provider_context = self._resolve_provider_context_for_asset_format(asset_format)
        template = self._load_prompt_template(asset_format)
        return ModelRuntimeContext(
            config_slot=provider_context.config_slot,
            provider=provider_context.provider,
            model=provider_context.model,
            system_prompt=template.system_prompt.strip() or DEFAULT_SYSTEM_PROMPT,
            prompt=template.prompt.strip(),
            supports_live_call=provider_context.supports_live_call,
        )

    def _resolve_provider_context_for_asset_format(self, asset_format: str) -> ModelRuntimeContext:
        provider_manager = ProviderConfigManager(self._providers_path)
        resolved_slot = self._resolve_asset_provider_slot(provider_manager, asset_format)
        provider = provider_manager.get(resolved_slot)
        return ModelRuntimeContext(
            config_slot=resolved_slot,
            provider=provider.provider,
            model=provider.model,
            system_prompt="",
            prompt="",
            supports_live_call=self._supports_live_call(provider),
        )

    def _resolve_provider_slot(
        self,
        provider_manager: ProviderConfigManager,
        requested_slot: str,
    ) -> str:
        raw = getattr(provider_manager, "_raw", {})
        if isinstance(raw.get(requested_slot), dict):
            return requested_slot
        if requested_slot == DEFAULT_PROVIDER_SLOT and isinstance(raw.get(LEGACY_PROVIDER_SLOT), dict):
            return LEGACY_PROVIDER_SLOT
        if isinstance(raw, dict):
            for slot, value in raw.items():
                if isinstance(value, dict):
                    return slot
        raise KeyError(f"provider 槽位不存在: {requested_slot}")

    def _resolve_asset_provider_slot(self, provider_manager: ProviderConfigManager, asset_format: str) -> str:
        raw = getattr(provider_manager, "_raw", {})
        preferred_slot = ASSET_PROVIDER_SLOTS.get(asset_format, asset_format)
        if isinstance(raw.get(preferred_slot), dict):
            return preferred_slot
        if isinstance(raw.get(DEFAULT_PROVIDER_SLOT), dict):
            return DEFAULT_PROVIDER_SLOT
        if isinstance(raw.get(LEGACY_PROVIDER_SLOT), dict):
            return LEGACY_PROVIDER_SLOT
        if isinstance(raw, dict):
            for slot, value in raw.items():
                if isinstance(value, dict):
                    return slot
        raise KeyError(f"素材类型的 provider 槽位不存在: {asset_format}")

    def _load_prompt_template(self, asset_format: str):
        prompt_config = load_prompt_config(self._prompts_path)
        templates = extract_prompt_templates(prompt_config)
        if asset_format in templates:
            return templates[asset_format]
        raise KeyError(f"素材格式的 prompt 配置不存在: {asset_format}")

    def _resolve_system_prompt(self, override_prompt: str | None, configured_prompt: str) -> str:
        if isinstance(override_prompt, str) and override_prompt.strip():
            return override_prompt.strip()
        return configured_prompt.strip() or DEFAULT_SYSTEM_PROMPT

    def _resolve_prompt(self, override_prompt: str | None, configured_prompt: str) -> str:
        if isinstance(override_prompt, str) and override_prompt.strip():
            return override_prompt.strip()
        return configured_prompt.strip()

    async def _call_dashscope(
        self,
        context: ModelRuntimeContext,
        system_prompt: str,
        prompt: str,
        asset_format: str,
        asset_path: str,
        model_name: str,
    ) -> str:
        provider_manager = ProviderConfigManager(self._providers_path)
        provider_config = provider_manager.get(context.config_slot)
        if asset_format == "文本":
            return await __import__("asyncio").to_thread(
                self._call_generation_sync,
                provider_config,
                model_name,
                system_prompt,
                self._build_text_user_prompt(prompt, asset_path),
                self._read_text_asset(asset_path),
            )

        multimodal_content = self._build_multimodal_content(asset_format, asset_path, prompt)
        return await __import__("asyncio").to_thread(
            self._call_multimodal_sync,
            provider_config,
            model_name,
            system_prompt,
            multimodal_content,
        )

    def _supports_live_call(self, provider: ProviderConfig) -> bool:
        if not provider.api_key:
            return False
        return importlib.util.find_spec("dashscope") is not None

    def _call_generation_sync(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        user_prompt: str,
        text_content: str,
    ) -> str:
        try:
            from dashscope import Generation
        except ImportError as exc:
            raise RuntimeError("live 模式需要安装 `dashscope` 包。") from exc

        messages: list[dict[str, str]] = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": f"{user_prompt}\n\n素材内容：\n{text_content}".strip()},
        ]

        response = Generation.call(
            api_key=provider_config.api_key,
            model=model_name,
            messages=messages,
            temperature=provider_config.temperature,
            max_tokens=provider_config.max_tokens,
            result_format="message",
        )

        return self._extract_response_text(response)

    def _call_multimodal_sync(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        multimodal_content: list[dict[str, Any]],
    ) -> str:
        try:
            from dashscope import MultiModalConversation
        except ImportError as exc:
            raise RuntimeError("live 模式需要安装 `dashscope` 包。") from exc

        messages: list[dict[str, Any]] = []
        if system_prompt.strip():
            messages.append({"role": "system", "content": [{"text": system_prompt}]})
        messages.append({"role": "user", "content": multimodal_content})

        response = MultiModalConversation.call(
            api_key=provider_config.api_key,
            model=model_name,
            messages=messages,
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

    def _build_text_user_prompt(self, prompt: str, asset_path: str) -> str:
        parts: list[str] = [f"素材绝对路径：{asset_path}"]
        if prompt.strip():
            parts.insert(0, prompt.strip())
        return "\n\n".join(parts)

    def _build_multimodal_content(self, asset_format: str, asset_path: str, prompt: str) -> list[dict[str, Any]]:
        content: list[dict[str, Any]] = [self._build_media_item(asset_format, asset_path)]
        if prompt.strip():
            content.append({"text": prompt.strip()})
        return content

    def _build_media_item(self, asset_format: str, asset_path: str) -> dict[str, Any]:
        file_uri = self._to_file_uri(asset_path)
        if asset_format == "图片":
            return {"image": file_uri}
        if asset_format == "视频":
            return {"video": file_uri, "fps": 2}
        if asset_format == "音频":
            return {"audio": file_uri}
        raise ValueError(f"不支持的多模态素材格式: {asset_format}")

    def _to_file_uri(self, asset_path: str) -> str:
        return f"file://{Path(asset_path).resolve().as_posix()}"

    def _resolve_model_name(self, configured_model: str, asset_format: str) -> str:
        if asset_format == "音频" and "omni" not in configured_model.lower() and "audio" not in configured_model.lower():
            return AUDIO_FALLBACK_MODEL
        return configured_model

    def _read_text_asset(self, asset_path: str) -> str:
        path = Path(asset_path)
        if not path.exists():
            raise FileNotFoundError(f"文本素材不存在: {path}")

        for encoding in ("utf-8-sig", "utf-8", "gb18030"):
            try:
                return path.read_text(encoding=encoding)
            except UnicodeDecodeError:
                continue

        raise ValueError(f"无法解码文本素材: {path}")

    def _build_mock_output(self, asset_format: str, asset_path: str, prompt: str) -> str:
        clipped = prompt.strip().replace("\r", " ").replace("\n", " ")
        if len(clipped) > 96:
            clipped = f"{clipped[:96]}..."
        return (
            "当前处于桌面端联调阶段，Python 后端仅保留素材打标能力。"
            f" 已接收到素材格式“{asset_format}”和路径“{asset_path}”。"
            f" 这是针对提示词“{clipped}”返回的占位响应，后续可替换为真实 provider 调用。"
        )
