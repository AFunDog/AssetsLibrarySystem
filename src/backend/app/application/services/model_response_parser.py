from __future__ import annotations

from typing import Any

from app.schemas.model import ModelGenerateResponse


class ModelResponseParser:
    """解析模型输出文本与 token 使用量。"""

    def extract_text(self, response: Any) -> str:
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
            return "\n".join(
                str(item["text"]) if isinstance(item, dict) and "text" in item else str(item)
                for item in content
            ).strip()
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
