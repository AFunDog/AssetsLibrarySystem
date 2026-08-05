"""用 LLM 重算剪辑素材的整体摘要：对全部片段描述做一次真正的总结，写回 DB。

背景：增量描述合并时"整体"采用"已有非空则保留"，导致整体摘要停留在最早批次；
旧版脚本是纯拼接（片段文本按时间顺序串起来 + 去重标签），内容冗长、头重脚轻。

本脚本收集全部片段文本，调用 providers.yaml 中"视频"槽位配置的模型
（默认 qwen3.7-flash）生成精炼概述 + 精选标签，再写回 DB。
LLM 调用失败时自动退化为旧的纯拼接逻辑，保证脚本永远可用。

用法（注意需使用项目 venv 的 Python，系统 Python 缺少 dashscope 等依赖）：
    src/backend/.venv/Scripts/python.exe scripts/rebuild_clip_overall_summary.py [--asset-id 1720] [--dry-run]
"""
from __future__ import annotations

import argparse
import asyncio
import datetime
import json
import re
import sqlite3
import sys
from pathlib import Path

# 允许独立运行：scripts/ 下运行时可 import app 包
BACKEND_ROOT = Path(__file__).resolve().parent.parent / "src" / "backend"
if str(BACKEND_ROOT) not in sys.path:
    sys.path.insert(0, str(BACKEND_ROOT))

from app.application.services.dashscope_model_client import DashScopeModelClient  # noqa: E402
from app.application.services.model_client import ModelClient  # noqa: E402
from app.application.services.model_response_parser import ModelResponseParser  # noqa: E402
from app.application.services.openai_model_client import OpenAIModelClient  # noqa: E402
from app.core.provider_config import ProviderConfig, ProviderConfigManager  # noqa: E402

DB = Path(__file__).resolve().parent.parent / "data" / "asset_descriptions.db"
PROVIDERS_PATH = BACKEND_ROOT / "configs" / "providers.yaml"

# 每段片段文本送入 LLM 的最大字符数（92 段 × 250 ≈ 2.3 万字，控制成本）
MAX_SEGMENT_TEXT_CHARS = 250
# 生成的概述目标长度
SUMMARY_MAX_CHARS = 500
# 生成的标签数量上限
TAGS_MAX = 20


def _get_client(provider: str) -> ModelClient:
    provider = provider.lower().strip()
    if provider == "dashscope":
        return DashScopeModelClient()
    if provider == "openai":
        return OpenAIModelClient()
    raise ValueError(f"不支持的 provider: {provider}")


def _extract_json(raw: str) -> dict | None:
    """从模型输出中提取 JSON 对象，容错处理 markdown 代码块与前后缀。"""
    text = (raw or "").strip()
    if not text:
        return None
    # 去掉 ```json ... ``` 包裹
    fence = re.search(r"```(?:json)?\s*(.*?)```", text, re.S)
    if fence:
        text = fence.group(1).strip()
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass
    # 找第一个 { 到最后一个 } 的子串
    start, end = text.find("{"), text.rfind("}")
    if start != -1 and end > start:
        try:
            return json.loads(text[start : end + 1])
        except json.JSONDecodeError:
            return None
    return None


def _collect_segments(doc: dict) -> tuple[list[str], list[str]]:
    """按时间顺序收集片段文本与标签（同旧逻辑）。"""
    segments = sorted(doc.get("segments", []), key=lambda s: s.get("start_time", 0.0))
    texts: list[str] = []
    tags: list[str] = []
    seen_tags: set[str] = set()
    for seg in segments:
        overall = seg.get("整体")
        if not isinstance(overall, dict):
            continue
        text = str(overall.get("text", "")).strip()
        if text and "已跳过描述" not in text:
            # 加时间标记帮助模型理解顺序，同时控制单段长度
            marked = f"[{seg.get('start_time', 0.0):.1f}-{seg.get('end_time', 0.0):.1f}s] {text}"
            texts.append(marked[: MAX_SEGMENT_TEXT_CHARS + 40])
        for t in overall.get("tags") or []:
            t = str(t)
            if t and t not in seen_tags:
                seen_tags.add(t)
                tags.append(t)
    return texts, tags


def _fallback_join(texts: list[str], tags: list[str]) -> tuple[str, list[str]]:
    """LLM 不可用时的兜底：纯拼接（旧行为）。"""
    summary = f"视频包含{len(texts)}个场景：{'；'.join(texts)}"
    return summary[:SUMMARY_MAX_CHARS], tags[:TAGS_MAX]


def _summarize_with_llm(provider_config: ProviderConfig, texts: list[str], tags: list[str]) -> tuple[str, list[str]]:
    """调用 LLM 生成精炼概述 + 精选标签。失败时回退拼接。"""
    client = _get_client(provider_config.provider)

    joined = "\n".join(texts)
    system_prompt = (
        "你是一个视频内容总结助手。用户会提供一段视频按时间顺序排列的片段描述，"
        "请生成这段视频的总体概述。"
    )
    user_prompt = (
        f"以下是视频的 {len(texts)} 个片段描述（方括号内为该片段的时间范围）：\n\n"
        f"{joined}\n\n"
        "请输出 JSON 对象，包含两个字段：\n"
        f'- "text"：整段视频的概述，通顺自然的中文段落，不超过 {SUMMARY_MAX_CHARS} 字，'
        "按时间顺序组织，保持人物与事件逻辑连贯，删除重复冗余，不要提及'片段''场景序号'。\n"
        f'- "tags"：精选不超过 {TAGS_MAX} 个中文标签数组，概括视频内容主题（如人物、场景、动作、氛围），'
        "去重且避免过于宽泛。\n"
        "只输出 JSON，不要输出其他内容。"
    )
    try:
        response = client.call_multimodal(
            provider_config,
            provider_config.model,
            system_prompt,
            [{"text": user_prompt}],
            {"type": "text"},
            max_tokens=2048,
        )
        raw = ModelResponseParser().extract_text(response)
        parsed = _extract_json(raw)
        if parsed is None:
            print("[warn] LLM 输出无法解析为 JSON，回退到拼接模式", file=sys.stderr)
            return _fallback_join(texts, tags)
        summary_text = str(parsed.get("text", "")).strip() or _fallback_join(texts, tags)[0]
        new_tags = [str(t).strip() for t in (parsed.get("tags") or []) if str(t).strip()]
        if not new_tags:
            new_tags = tags[:TAGS_MAX]
        return summary_text[: SUMMARY_MAX_CHARS + 100], new_tags[:TAGS_MAX]
    except Exception as exc:  # noqa: BLE001 - 网络/解析失败都应回退
        print(f"[warn] LLM 总结失败（{exc}），回退到拼接模式", file=sys.stderr)
        return _fallback_join(texts, tags)


def main() -> None:
    parser = argparse.ArgumentParser(description="用 LLM 重算剪辑素材的整体摘要")
    parser.add_argument("--asset-id", type=int, default=1720, help="素材数据库 ID（默认 1720）")
    parser.add_argument("--dry-run", action="store_true", help="只打印结果，不写回 DB")
    args = parser.parse_args()

    conn = sqlite3.connect(DB)
    cur = conn.cursor()
    row = cur.execute(
        "SELECT description FROM asset_descriptions WHERE asset_id=?", (args.asset_id,)
    ).fetchone()
    if row is None:
        raise SystemExit(f"描述记录不存在: asset_id={args.asset_id}")
    doc = json.loads(row[0])

    texts, tags = _collect_segments(doc)
    if not texts:
        raise SystemExit("没有可用的片段描述，无法生成整体摘要")

    provider_config = ProviderConfigManager(PROVIDERS_PATH).get("视频")
    summary_text, new_tags = _summarize_with_llm(provider_config, texts, tags)

    doc["整体"] = {"text": summary_text, "tags": new_tags}
    if not args.dry_run:
        cur.execute(
            "UPDATE asset_descriptions SET description=?, generated_at=? WHERE asset_id=?",
            (json.dumps(doc, ensure_ascii=False), datetime.datetime.now(datetime.timezone.utc).isoformat(), args.asset_id),
        )
        conn.commit()
    conn.close()

    print(f"整体摘要已重算(LLM): 场景数={len(texts)}, 模型={provider_config.model}")
    print(f"标签数: {len(new_tags)}")
    print("--- 新整体摘要 ---")
    print(summary_text)
    if args.dry_run:
        print("[dry-run] 未写回 DB")


if __name__ == "__main__":
    main()
