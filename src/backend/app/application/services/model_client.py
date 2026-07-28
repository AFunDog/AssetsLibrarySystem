from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any

from app.core.provider_config import ProviderConfig


class ModelClient(ABC):
    """大模型客户端抽象基类。

    所有 provider 的客户端必须实现此接口，
    ModelService 通过 _get_client(provider) 动态路由到对应实现。
    """

    @abstractmethod
    def call_generation(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        user_prompt: str,
        text_content: str,
        response_format: dict[str, Any],
    ) -> Any:
        """调用文本生成模型。

        Args:
            provider_config: provider 配置（含 api_key, temperature 等）
            model_name: 模型名称
            system_prompt: 系统提示词
            user_prompt: 用户提示词
            text_content: 素材文本内容
            response_format: 响应格式，如 {"type": "json_object"}

        Returns:
            模型返回的原始响应对象，需由 ModelResponseParser 解析
        """
        ...

    @abstractmethod
    def call_multimodal(
        self,
        provider_config: ProviderConfig,
        model_name: str,
        system_prompt: str,
        multimodal_content: list[dict[str, Any]],
        response_format: dict[str, Any],
    ) -> Any:
        """调用多模态模型（图片/视频/音频）。

        Args:
            provider_config: provider 配置
            model_name: 模型名称
            system_prompt: 系统提示词
            multimodal_content: 多模态内容列表，格式因 provider 而异
            response_format: 响应格式

        Returns:
            模型返回的原始响应对象
        """
        ...