from __future__ import annotations

from typing import Any

from app.core.provider_config import ProviderConfig


class DashScopeModelClient:
    """封装 DashScope SDK 的同步模型调用。"""

    def call_generation(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        user_prompt: str,
        text_content: str,
        response_format: dict[str, Any],
    ) -> Any:
        try:
            from dashscope import Generation
        except ImportError as exc:
            raise RuntimeError("live 模式需要安装 `dashscope` 包。") from exc

        messages: list[dict[str, str]] = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": f"{user_prompt}\n\n素材内容：\n{text_content}".strip()},
        ]
        return Generation.call(
            api_key=provider_config.api_key,
            model=model_name,
            messages=messages,
            temperature=provider_config.temperature,
            max_tokens=provider_config.max_tokens,
            result_format="message",
            response_format=response_format,
        )

    def call_multimodal(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        multimodal_content: list[dict[str, Any]],
        response_format: dict[str, Any],
    ) -> Any:
        try:
            from dashscope import MultiModalConversation
        except ImportError as exc:
            raise RuntimeError("live 模式需要安装 `dashscope` 包。") from exc

        messages: list[dict[str, Any]] = []
        if system_prompt.strip():
            messages.append({"role": "system", "content": [{"text": system_prompt}]})
        messages.append({"role": "user", "content": multimodal_content})
        return MultiModalConversation.call(
            api_key=provider_config.api_key,
            model=model_name,
            messages=messages,
            response_format=response_format,
        )
