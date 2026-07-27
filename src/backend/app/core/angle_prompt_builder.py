"""从角度定义列表动态构建系统提示词。

C# 端负责管理子类型和角度配置，Python 端只负责接收角度列表并构建 prompt。
"""

from __future__ import annotations

from typing import Any

# 素材类型到中文描述前缀的映射
_ASSET_TYPE_LABELS: dict[str, str] = {
    "文本": "文本素材",
    "图片": "图片素材",
    "视频": "视频素材",
    "音频": "音频素材",
}


def build_system_prompt_from_angles(
    asset_format: str,
    angles: list[dict[str, Any]],
) -> str:
    """根据角度定义列表动态构建系统提示词。

    Args:
        asset_format: 素材格式（"文本"/"图片"/"视频"/"音频"）
        angles: 角度定义列表，每个元素包含 key / label / prompt / max_length

    Returns:
        动态生成的系统提示词字符串
    """
    asset_label = _ASSET_TYPE_LABELS.get(asset_format, f"{asset_format}素材")
    angle_keys = ', '.join(f'"{a["key"]}"' for a in angles)

    lines: list[str] = [
        f"你是{asset_label}结构化描述助手。",
        "请根据输入的素材内容、格式信息和绝对路径，输出严格合法的 JSON 对象。",
        "",
        "输出要求：",
        "- 只描述当前素材内容本身，不做文件管理、使用建议、版权判断或目录推断。",
        "- 只写素材中能明确看到或听到的内容，不得臆造。",
        "- 不要把文件名、路径、目录名当作素材内容，除非素材本身支持。",
        "- 只能输出 JSON，不要输出 Markdown、代码块、解释或额外文本。",
        f"- JSON 必须包含且只包含以下字段： {angle_keys}",
        '- 每个字段的值必须是对象 {"text": ..., "tags": [...]}，不能是普通字符串。',
        '- 如果某个字段没有合适的标签，tags 请使用空数组 []，不要省略该字段。',
        "- 每个 text 用中文，不超过对应字段的最大字数。",
        '- tags 是简短中文标签数组，适合筛选和展示，避免重复和长句。',
        "- JSON 字符串必须使用双引号，不能有注释或尾随逗号。",
        "",
        "字段含义：",
    ]

    for a in angles:
        max_len = a.get("max_length", 120)
        lines.append(f'- "{a["key"]}"：{a.get("prompt", "")}（不超过 {max_len} 字）')

    lines.extend([
        "",
        "输出格式示例：",
        "{",
    ])

    for i, a in enumerate(angles):
        comma = "," if i < len(angles) - 1 else ""
        lines.append(f'  "{a["key"]}": {{ "text": "...", "tags": ["..."] }}{comma}')

    lines.append("}")
    lines.append("")
    lines.append("注意：必须严格按照以上示例的嵌套格式输出，每个字段的值必须是 {\"text\": ..., \"tags\": [...]} 对象。")
    lines.append("如果输出扁平字符串（如 \"场景\": \"描述\"）会导致程序解析失败。")

    return "\n".join(lines)


def build_summary_prompt(
    asset_format: str,
    angles: list[dict[str, Any]],
    segment_descriptions: list[dict[str, Any]],
) -> str:
    """从所有片段的描述文本合成整体摘要的提示词。

    Args:
        asset_format: 素材格式
        angles: 角度定义列表
        segment_descriptions: 每个片段的描述结果

    Returns:
        用于合成摘要的提示词字符串
    """
    asset_label = _ASSET_TYPE_LABELS.get(asset_format, f"{asset_format}素材")
    angle_keys = ', '.join(f'"{a["key"]}"' for a in angles)

    lines = [
        f"你是{asset_label}综合摘要助手。",
        "以下是一段视频的多个场景描述，请根据这些描述生成整体摘要。",
        "",
        "输出要求：",
        "- 综合所有场景，概括视频的整体内容和风格。",
        "- 不要编造场景描述中没有的信息。",
        "- 只能输出 JSON，不要输出 Markdown、代码块或解释。",
        f"- JSON 必须包含且只包含以下字段： {angle_keys}",
        '- 每个字段是对象，包含 "text" 和 "tags"。',
        "- 每个 text 用中文，不超过 200 个中文字符。",
        "- tags 是简短中文标签数组，适合筛选和展示。",
        "",
        "场景描述如下：",
    ]

    for i, seg in enumerate(segment_descriptions):
        seg_texts = []
        for angle in angles:
            key = angle["key"]
            if key in seg:
                value = seg[key]
                if isinstance(value, dict):
                    text = value.get("text", "")
                    if text:
                        seg_texts.append(f"{key}: {text}")
        if seg_texts:
            start = seg.get("start_time", 0)
            end = seg.get("end_time", 0)
            lines.append(
                f"场景{i+1} ({start:.1f}s-{end:.1f}s)：{'；'.join(seg_texts)}"
            )

    lines.extend([
        "",
        "请输出 JSON：",
        "{",
    ])
    for i, a in enumerate(angles):
        comma = "," if i < len(angles) - 1 else ""
        lines.append(f'  "{a["key"]}": {{ "text": "...", "tags": ["..."] }}{comma}')
    lines.append("}")

    return "\n".join(lines)