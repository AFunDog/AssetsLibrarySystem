from __future__ import annotations

import logging
from typing import Any

from app.schemas.model import ModelGenerateResponse

logger = logging.getLogger(__name__)


class ModelResponseParser:
    """解析模型输出文本与 token 使用量。"""

    def ensure_success(self, response: Any, *, operation: str = "模型调用") -> None:
        """检查 DashScope / 兼容响应的 status_code，非成功则抛出 RuntimeError。"""
        status_code = getattr(response, "status_code", None)
        if status_code is None and isinstance(response, dict):
            status_code = response.get("status_code")
        if status_code is None:
            return
        try:
            code = int(status_code)
        except (TypeError, ValueError):
            return
        if code == 200:
            return

        message = (
            getattr(response, "message", None)
            or (response.get("message") if isinstance(response, dict) else None)
            or getattr(response, "code", None)
            or status_code
        )
        logger.error("%s 返回错误: status=%s, message=%s", operation, status_code, message)
        raise RuntimeError(f"{operation}失败（status={status_code}）：{message}")

    def extract_text(self, response: Any) -> str:
        self.ensure_success(response, operation="模型生成")
        # DashScope 格式: response.output.choices[0].message.content
        # OpenAI 格式:   response.choices[0].message.content  (无 output 层)
        output = getattr(response, "output", None)
        if output is None and isinstance(response, dict):
            output = response.get("output")

        if output is not None:
            choices = getattr(output, "choices", None)
            if choices is None and isinstance(output, dict):
                choices = output.get("choices")
        else:
            # 直接尝试从 response 获取 choices（OpenAI 兼容格式）
            choices = getattr(response, "choices", None)
            if choices is None and isinstance(response, dict):
                choices = response.get("choices")

        if not choices:
            logger.warning("无法解析模型响应: choices 为空")
            raise RuntimeError(f"无法解析模型响应: {response}")

        message = choices[0].get("message") if isinstance(choices[0], dict) else getattr(choices[0], "message", None)
        if message is None:
            logger.warning("无法解析模型消息体: message 为空")
            raise RuntimeError(f"无法解析模型消息体: {response}")

        content = message.get("content") if isinstance(message, dict) else getattr(message, "content", None)
        if isinstance(content, str):
            return content
        if isinstance(content, list):
            text = "\n".join(
                str(item["text"]) if isinstance(item, dict) and "text" in item else str(item)
                for item in content
            ).strip()
            return text
        logger.warning("无法解析模型输出文本: content 类型=%s", type(content).__name__)
        raise RuntimeError(f"无法解析模型输出文本: {response}")

    def extract_token_usage(self, response: Any) -> ModelGenerateResponse.TokenUsage | None:
        usage = getattr(response, "usage", None)
        if usage is None and isinstance(response, dict):
            usage = response.get("usage")
        if usage is None:
            return None

        input_tokens = self._read_usage_value(usage, ("input_tokens", "prompt_tokens"))
        output_tokens = self._read_usage_value(usage, ("output_tokens", "completion_tokens"))
        total_tokens = self._read_usage_value(usage, ("total_tokens",))
        image_tokens = self._read_usage_value(usage, ("image_tokens",))
        video_tokens = self._read_usage_value(usage, ("video_tokens",))
        audio_tokens = self._read_usage_value(usage, ("audio_tokens",))
        if all(value is None for value in (input_tokens, output_tokens, total_tokens, image_tokens, video_tokens, audio_tokens)):
            return None

        logger.debug(
            "解析 token 用量: input=%s, output=%s, total=%s",
            input_tokens,
            output_tokens,
            total_tokens,
        )
        return ModelGenerateResponse.TokenUsage(
            input_tokens=input_tokens or 0,
            output_tokens=output_tokens or 0,
            total_tokens=total_tokens or (input_tokens or 0) + (output_tokens or 0),
            image_tokens=image_tokens,
            video_tokens=video_tokens,
            audio_tokens=audio_tokens,
            input_tokens_details=self._read_usage_details(usage, ("input_tokens_details",)),
            output_tokens_details=self._read_usage_details(usage, ("output_tokens_details",)),
            prompt_tokens_details=self._read_usage_details(usage, ("prompt_tokens_details",)),
        )

    def _read_usage_value(self, usage: Any, keys: tuple[str, ...]) -> int | None:
        for key in keys:
            value = self._read_usage_field(usage, key)
            if value is not None:
                return value

        models = self._read_usage_field(usage, "models")
        if isinstance(models, list):
            values = []
            for item in models:
                if not isinstance(item, dict):
                    continue
                for key in keys:
                    if item.get(key) is not None:
                        try:
                            values.append(int(item[key]))
                        except (TypeError, ValueError):
                            pass
                        break
            if values:
                return sum(values)
        return None

    def _read_usage_details(self, usage: Any, keys: tuple[str, ...]) -> dict[str, Any] | None:
        for key in keys:
            value = self._read_usage_field(usage, key)
            if isinstance(value, dict):
                return value
            if value is not None:
                return {"value": value}
        return None

    @staticmethod
    def _read_usage_field(usage: Any, key: str) -> Any:
        try:
            value = getattr(usage, key, None)
        except KeyError:
            value = None
        if value is None and isinstance(usage, dict):
            value = usage.get(key)
        return value
