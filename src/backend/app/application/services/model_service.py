from __future__ import annotations

from dataclasses import dataclass
import asyncio
from pathlib import Path
from typing import Any

from app.application.services.dashscope_model_client import DashScopeModelClient
from app.application.services.media_preprocessor import MediaPreprocessor
from app.application.services.model_response_parser import ModelResponseParser
from app.application.services.provider_resolver import ProviderResolver
from app.core.config import get_settings
from app.core.paths import ensure_shared_data_dir
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
        settings = get_settings()
        data_dir = ensure_shared_data_dir()
        configured_temp_dir = settings.media_temp_dir.strip() if settings.media_temp_dir else ""
        self._temp_dir = Path(configured_temp_dir).expanduser().resolve() if configured_temp_dir else data_dir / "temp"
        self._enable_media_preprocess = settings.enable_media_preprocess
        self._image_max_side = max(256, settings.image_max_side)
        self._image_jpeg_quality = min(max(settings.image_jpeg_quality, 40), 95)
        self._video_crf = min(max(settings.video_crf, 18), 40)
        self._video_audio_bitrate = settings.video_audio_bitrate.strip() or "128k"
        self._provider_manager: ProviderConfigManager | None = None
        self._provider_resolver: ProviderResolver | None = None
        self._dashscope_client = DashScopeModelClient()
        self._response_parser = ModelResponseParser()

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
                token_usage=None,
            )

        if provider_context.provider.lower() != "dashscope":
            raise ValueError(f"当前仅实现 dashscope live 调用，实际 provider 为: {provider_context.provider}")

        raw_output_text, usage = await self._call_dashscope(
            provider_context,
            system_prompt,
            prompt,
            payload.asset_format,
            payload.asset_path,
            call_model,
        )
        output_text = self._clean_llm_output(raw_output_text)
        return ModelGenerateResponse(
            provider_slot=DEFAULT_PROVIDER_SLOT,
            provider=provider_context.provider,
            model=call_model,
            mode="live",
            output_text=output_text,
            system_prompt=system_prompt,
            token_usage=usage,
        )

    def _resolve_provider_context(self, provider_slot: str) -> ModelRuntimeContext:
        provider_manager = self._get_provider_manager()
        resolved_slot = self._get_provider_resolver().resolve_slot(provider_slot)
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
        provider_manager = self._get_provider_manager()
        resolved_slot = self._get_provider_resolver().resolve_asset_slot(asset_format)
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
        return ProviderResolver(
            provider_manager,
            DEFAULT_PROVIDER_SLOT,
            LEGACY_PROVIDER_SLOT,
            ASSET_PROVIDER_SLOTS,
        ).resolve_slot(requested_slot)

    def _resolve_asset_provider_slot(self, provider_manager: ProviderConfigManager, asset_format: str) -> str:
        return ProviderResolver(
            provider_manager,
            DEFAULT_PROVIDER_SLOT,
            LEGACY_PROVIDER_SLOT,
            ASSET_PROVIDER_SLOTS,
        ).resolve_asset_slot(asset_format)

    def _get_provider_manager(self) -> ProviderConfigManager:
        if self._provider_manager is None:
            self._provider_manager = ProviderConfigManager(self._providers_path)
        return self._provider_manager

    def _get_provider_resolver(self) -> ProviderResolver:
        if self._provider_resolver is None:
            self._provider_resolver = ProviderResolver(
                self._get_provider_manager(),
                DEFAULT_PROVIDER_SLOT,
                LEGACY_PROVIDER_SLOT,
                ASSET_PROVIDER_SLOTS,
            )
        return self._provider_resolver

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
    ) -> tuple[str, ModelGenerateResponse.TokenUsage | None]:
        provider_manager = self._get_provider_manager()
        provider_config = provider_manager.get(context.config_slot)
        if asset_format == "文本":
            response = await asyncio.to_thread(
                self._call_generation_sync,
                provider_config,
                model_name,
                system_prompt,
                self._build_text_user_prompt(prompt, asset_path),
                self._read_text_asset(asset_path),
            )
            return self._extract_response_text(response), self._extract_token_usage(response)

        preprocessed_path = await asyncio.to_thread(self._prepare_media_asset, asset_format, asset_path)
        try:
            multimodal_content = self._build_multimodal_content(asset_format, preprocessed_path, prompt)
            response = await asyncio.to_thread(
                self._call_multimodal_sync,
                provider_config,
                model_name,
                system_prompt,
                multimodal_content,
            )
            return self._extract_response_text(response), self._extract_token_usage(response)
        finally:
            await asyncio.to_thread(self._cleanup_preprocessed_asset, asset_path, preprocessed_path)

    def _supports_live_call(self, provider: ProviderConfig) -> bool:
        return ProviderResolver.supports_live_call(provider)

    def _call_generation_sync(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        user_prompt: str,
        text_content: str,
    ) -> Any:
        return self._dashscope_client.call_generation(
            provider_config,
            model_name,
            system_prompt,
            user_prompt,
            text_content,
            self._build_response_format(provider_config),
        )

    def _call_multimodal_sync(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        multimodal_content: list[dict[str, Any]],
    ) -> Any:
        return self._dashscope_client.call_multimodal(
            provider_config,
            model_name,
            system_prompt,
            multimodal_content,
            self._build_response_format(provider_config),
        )

    def _build_response_format(self, provider_config: ProviderConfig) -> dict[str, Any]:
        configured = provider_config.extra_body or {}
        response_format = configured.get("response_format")
        if isinstance(response_format, dict) and response_format.get("type"):
            return response_format
        return {"type": "json_object"}

    def _extract_response_text(self, response: Any) -> str:
        return self._response_parser.extract_text(response)

    def _extract_token_usage(self, response: Any) -> ModelGenerateResponse.TokenUsage | None:
        return self._response_parser.extract_token_usage(response)

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

    def _prepare_media_asset(self, asset_format: str, asset_path: str) -> str:
        return self._create_media_preprocessor().prepare(asset_format, asset_path)

    def _build_temp_asset_path(self, source_path: Path, preferred_suffix: str | None = None) -> Path:
        return self._create_media_preprocessor().build_temp_asset_path(source_path, preferred_suffix)

    def _compress_image(self, source_path: Path, target_path: Path) -> Path:
        return self._create_media_preprocessor().compress_image(source_path, target_path)

    def _compress_video(self, source_path: Path, target_path: Path) -> Path:
        return self._create_media_preprocessor().compress_video(source_path, target_path)

    def _run_ffmpeg_or_fallback(self, command: list[str], source_path: Path, target_path: Path) -> Path:
        return self._create_media_preprocessor().run_ffmpeg_or_fallback(command, source_path, target_path)

    def _cleanup_preprocessed_asset(self, source_path: str, prepared_path: str) -> None:
        self._create_media_preprocessor().cleanup(source_path, prepared_path)

    @staticmethod
    def _remove_file_if_exists(path: Path) -> None:
        MediaPreprocessor.remove_file_if_exists(path)

    def _create_media_preprocessor(self) -> MediaPreprocessor:
        return MediaPreprocessor(
            temp_dir=self._temp_dir,
            enabled=self._enable_media_preprocess,
            image_max_side=self._image_max_side,
            image_jpeg_quality=self._image_jpeg_quality,
            video_crf=self._video_crf,
            video_audio_bitrate=self._video_audio_bitrate,
        )

    def _resolve_model_name(self, configured_model: str, asset_format: str) -> str:
        if asset_format == "音频" and "omni" not in configured_model.lower() and "audio" not in configured_model.lower():
            return AUDIO_FALLBACK_MODEL
        return configured_model

    @staticmethod
    def _clean_llm_output(text: str) -> str:
        """清理 LLM 输出中可能混入的 Markdown 代码块标记等杂质。

        LLM 在配置了 ``response_format=json_object`` 后仍可能在输出首尾包裹
        ```json / ``` 等 Markdown 代码块语法，此方法负责剥离这些标记，
        确保返回纯净的 JSON 文本。
        """
        cleaned = text.strip()
        # 去掉开头的 `` ```json `` 或 `` ``` ``
        fence_patterns = ("```json", "```")
        for prefix in fence_patterns:
            if cleaned.startswith(prefix):
                cleaned = cleaned[len(prefix):].lstrip()
                break
        # 去掉结尾的 `` ``` ``
        if cleaned.endswith("```"):
            cleaned = cleaned[:-3].rstrip()
        return cleaned.strip()

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
