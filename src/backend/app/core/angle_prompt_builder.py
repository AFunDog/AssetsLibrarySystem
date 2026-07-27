"""从角度定义列表动态构建系统提示词。

提示词模板从 configs/prompt_template.yaml 加载，
与 C# 端 angle_profiles.yaml 的 prompt_template 保持一致。
"""

from __future__ import annotations

from pathlib import Path
from typing import Any

import yaml

# 素材类型到中文描述前缀的映射
_ASSET_TYPE_LABELS: dict[str, str] = {
    "文本": "文本素材",
    "图片": "图片素材",
    "视频": "视频素材",
    "音频": "音频素材",
}

# 默认模板（YAML 文件加载失败时的回退）
_DEFAULT_TEMPLATE: dict[str, Any] = {
    "role": "你是{asset_type}素材的结构化内容分析助手。你的分析结果将用于素材检索、时间定位和视频剪辑。",
    "intro": "请根据实际输入的素材内容，输出一个严格合法的 JSON 对象。只能描述素材中能够明确看到或听到的内容。",
    "rules": [
        "只能输出 JSON 对象，不要输出 Markdown、代码块、解释、前后缀或额外文本。",
        "输出字段必须与任务中要求的字段完全一致，不得增加、删除或重命名字段。",
        "每个字段的值必须是对象 {\"text\": ..., \"tags\": [...]}，不能直接使用字符串、数组或 null。",
        "只描述素材中能够明确看到或听到的内容，不得补全未出现的过程，不得根据常识臆造。",
        "不得将文件名、路径、目录名或文件元数据当作素材内容。",
        "上下文信息只能用于保持人物和事件连续性，不能用来替代当前素材中的视觉或听觉证据。",
        "人物身份无法确认时，只描述外观、服装、发色、位置或动作，不要猜测姓名。",
        "事件因果关系不明确时，只按实际发生顺序描述，不要自行推断原因。",
        "存在多个连续画面或事件时，必须按照实际发生顺序描述，不能只概括持续时间最长的画面。",
        "必须重点检查素材开头和结尾，记录其中出现的瞬时画面、插入帧、闪回、闪白、黑场、字幕、转场或快速动作。",
        "即使某个画面只出现很短时间，只要能够辨认且具有独立内容，也必须在对应字段中记录。",
        "每个 text 必须使用中文，不超过对应字段的最大字数。",
        "无法从素材中可靠判断的字段必须输出 {\"text\": \"\", \"tags\": []}，不得猜测或编造。",
        "tags 必须是中文短标签数组，每个字段最多 6 个标签，每个标签建议 2 至 8 个汉字，使用名词或短语。",
        "tags 不得使用完整句子，不得包含重复或近义标签。",
        "JSON 中所有字符串必须使用双引号，不能包含注释、尾随逗号或未转义字符。",
    ],
    "note": (
        '必须严格使用以下字段结构：\n'
        '"字段名": { "text": "字段描述", "tags": ["标签1", "标签2"] }\n\n'
        '如果无法判断，必须输出：\n'
        '"字段名": { "text": "", "tags": [] }'
    ),
}


def _load_template() -> dict[str, Any]:
    """从 YAML 文件加载提示词模板，失败时返回默认模板。"""
    template_path = Path(__file__).resolve().parents[2] / "configs" / "prompt_template.yaml"
    try:
        with open(template_path, encoding="utf-8") as f:
            data = yaml.safe_load(f)
            if isinstance(data, dict) and "prompt_template" in data:
                return data["prompt_template"]
    except Exception:
        pass
    return _DEFAULT_TEMPLATE


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
    template = _load_template()
    asset_label = _ASSET_TYPE_LABELS.get(asset_format, f"{asset_format}素材")
    angle_keys = ', '.join(f'"{a["key"]}"' for a in angles)

    role = template["role"].replace("{asset_type}", asset_label)
    lines: list[str] = [
        role,
        template["intro"],
        "",
        "输出要求：",
    ]

    # 插入字段约束（紧跟输出要求之后）
    field_constraint = f"JSON 必须包含且只包含以下字段： {angle_keys}"
    rules = list(template.get("rules", []))
    rules.insert(0, field_constraint)

    for rule in rules:
        lines.append(f"- {rule}")

    lines.append("")
    lines.append("字段含义：")

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
    lines.append(template.get("note", ""))

    return "\n".join(lines)


def build_summary_prompt(
    asset_format: str,
    angles: list[dict[str, Any]],
    segment_descriptions: list[dict[str, Any]],
) -> str:
    """从所有片段的描述文本合成整体摘要的提示词。"""
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