from __future__ import annotations

import asyncio
import base64
import json
import mimetypes
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Protocol

from app.core.prompt_config import extract_system_prompts, load_prompt_config
from app.core.provider_config import ProviderConfig, ProviderConfigManager
from app.domain.models.tagging import TaggingAsset

DEFAULT_PROVIDER_SLOT = "asset_describer"
DEFAULT_BACKEND_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_PROVIDER_PATH = DEFAULT_BACKEND_ROOT / "configs/providers.yaml"
DEFAULT_PROMPTS_PATH = DEFAULT_BACKEND_ROOT / "configs/prompts.yaml"
DEFAULT_SYSTEM_PROMPT = (
    "你是一个素材打标助手。请根据素材内容输出适合检索和管理的中文描述。"
)


@dataclass(slots=True)
class TaggedAsset:
    asset_id: str
    asset_type: str
    provider: str
    model: str
    description: str
    tags: list[str]
    raw_text: str


class AssetDescriber(Protocol):
    async def describe(self, asset: TaggingAsset) -> TaggedAsset:
        """为单个素材生成打标结果。"""


def _strip_markdown_fence(text: str) -> str:
    text = text.strip()
    if text.startswith("```") and text.endswith("```"):
        inner = text[3:-3].strip()
        if inner.startswith("json"):
            inner = inner[4:].strip()
        return inner
    return text


def _parse_tagging_payload(raw_text: str) -> tuple[str, list[str]]:
    cleaned = _strip_markdown_fence(raw_text)
    try:
        payload = json.loads(cleaned)
    except json.JSONDecodeError:
        payload = None

    if isinstance(payload, dict):
        description = str(payload.get("description") or payload.get("text") or "").strip()
        tags_value = payload.get("tags") or []
        tags: list[str] = []
        if isinstance(tags_value, list):
            for item in tags_value:
                tag = str(item).strip()
                if tag and tag not in tags:
                    tags.append(tag)
        if description or tags:
            return description or cleaned, tags

    return cleaned, []


def _guess_mime_type(path: Path) -> str:
    mime_type, _ = mimetypes.guess_type(path.name)
    return mime_type or "application/octet-stream"


def _to_data_url(path: Path) -> str:
    encoded = base64.b64encode(path.read_bytes()).decode("ascii")
    return f"data:{_guess_mime_type(path)};base64,{encoded}"


def _to_dashscope_file_uri(path: Path) -> str:
    resolved = path.resolve()
    if resolved.drive:
        return f"file:///{resolved.as_posix().lstrip('/')}"
    return resolved.as_uri()


def _load_system_prompt(prompts_path: str | Path, slot: str) -> str:
    prompt_config = load_prompt_config(prompts_path)
    prompts = extract_system_prompts(prompt_config)
    prompt = prompts.get(slot, "").strip()
    return prompt or DEFAULT_SYSTEM_PROMPT


class DashScopeAssetDescriber:
    """使用 DashScope 多模态能力做素材打标。"""

    def __init__(self, provider_config: ProviderConfig, system_prompt: str) -> None:
        self._config = provider_config
        self._system_prompt = system_prompt.strip() or DEFAULT_SYSTEM_PROMPT

    async def describe(self, asset: TaggingAsset) -> TaggedAsset:
        return await asyncio.to_thread(self._describe_sync, asset)

    def _describe_sync(self, asset: TaggingAsset) -> TaggedAsset:
        try:
            from dashscope import MultiModalConversation
        except ImportError as exc:
            raise RuntimeError("DashScope provider 需要安装 `dashscope` 包。") from exc

        prompt = (
            f"{self._system_prompt} "
            f"资产ID={asset.asset_id}，素材类型={asset.asset_type}。"
            " 请只输出 JSON 对象，字段必须包含 description 和 tags。"
        )

        content: list[dict[str, Any]] = [{"text": prompt}]
        if asset.text:
            content.insert(0, {"text": asset.text})
        elif asset.source_path:
            path = Path(asset.source_path)
            if asset.asset_type == "text":
                content.insert(0, {"text": path.read_text(encoding="utf-8")})
            elif asset.asset_type == "image":
                content.insert(0, {"image": _to_dashscope_file_uri(path)})
            elif asset.asset_type == "video":
                content.insert(0, {"video": _to_dashscope_file_uri(path)})
            else:
                content.insert(0, {"text": _to_data_url(path)})

        response = MultiModalConversation.call(
            api_key=self._config.api_key,
            model=self._config.model,
            messages=[{"role": "user", "content": content}],
        )

        try:
            raw_text = response.output.choices[0].message.content[0]["text"]
        except (AttributeError, IndexError, KeyError, TypeError) as exc:
            raise RuntimeError(f"无法解析 DashScope 响应: {response}") from exc

        description, tags = _parse_tagging_payload(raw_text)
        if not tags:
            tags = self._extract_tags(description)
        return TaggedAsset(
            asset_id=asset.asset_id,
            asset_type=asset.asset_type,
            provider=self._config.provider,
            model=self._config.model,
            description=description,
            tags=tags,
            raw_text=raw_text,
        )

    def _extract_tags(self, description: str) -> list[str]:
        separators = ["、", ",", "，", ";", "；", " "]
        normalized = description
        for sep in separators:
            normalized = normalized.replace(sep, " ")
        tokens = [token.strip() for token in normalized.split() if token.strip()]
        seen: set[str] = set()
        tags: list[str] = []
        for token in tokens:
            if len(token) < 2:
                continue
            if token not in seen:
                seen.add(token)
                tags.append(token)
            if len(tags) >= 8:
                break
        return tags


def build_asset_describer(
    providers_path: str | Path = DEFAULT_PROVIDER_PATH,
    prompts_path: str | Path = DEFAULT_PROMPTS_PATH,
    provider_slot: str = DEFAULT_PROVIDER_SLOT,
) -> DashScopeAssetDescriber:
    provider_manager = ProviderConfigManager(providers_path)
    provider_config = provider_manager.get(provider_slot)
    system_prompt = _load_system_prompt(prompts_path, provider_slot)
    if provider_config.provider.lower() != "dashscope":
        raise ValueError(
            f"当前打标模块只实现了 dashscope provider，实际配置为: {provider_config.provider}"
        )
    return DashScopeAssetDescriber(provider_config, system_prompt)
