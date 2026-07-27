from __future__ import annotations

import unittest

from app.core.angle_prompt_builder import build_system_prompt_from_angles


class AnglePromptBuilderTestCase(unittest.TestCase):
    def test_build_prompt_contains_asset_type_label(self) -> None:
        angles = [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}]
        prompt = build_system_prompt_from_angles("视频", angles)
        self.assertIn("视频素材", prompt)

    def test_build_prompt_contains_angle_keys(self) -> None:
        angles = [
            {"key": "场景", "label": "场景环境", "prompt": "描述场景", "max_length": 100},
            {"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120},
        ]
        prompt = build_system_prompt_from_angles("视频", angles)
        self.assertIn('"场景"', prompt)
        self.assertIn('"整体"', prompt)
        self.assertIn("描述场景", prompt)
        self.assertIn("100 字", prompt)

    def test_build_prompt_contains_required_fields_section(self) -> None:
        angles = [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}]
        prompt = build_system_prompt_from_angles("音频", angles)
        self.assertIn("音频素材", prompt)
        self.assertIn("只能输出 JSON", prompt)
        self.assertIn('{"text": ..., "tags": [...]}', prompt)

    def test_build_prompt_contains_json_example(self) -> None:
        angles = [
            {"key": "场景", "label": "场景", "prompt": "描述场景", "max_length": 100},
            {"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120},
        ]
        prompt = build_system_prompt_from_angles("视频", angles)
        self.assertIn("输出格式示例", prompt)
        self.assertIn('"场景": { "text": "...", "tags": ["..."] }', prompt)
        self.assertIn('"整体": { "text": "...", "tags": ["..."] }', prompt)

    def test_build_prompt_single_angle_no_trailing_comma(self) -> None:
        angles = [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}]
        prompt = build_system_prompt_from_angles("文本", angles)
        # 唯一字段，末尾不应有逗号
        self.assertIn('"整体": { "text": "...", "tags": ["..."] }', prompt)

    def test_build_prompt_multiple_angles_correct_commas(self) -> None:
        angles = [
            {"key": "场景", "label": "场景", "prompt": "描述场景", "max_length": 100},
            {"key": "动作", "label": "动作", "prompt": "描述动作", "max_length": 100},
            {"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120},
        ]
        prompt = build_system_prompt_from_angles("视频", angles)
        lines = prompt.split("\n")
        # 只统计 JSON 示例块中的行（以 4 空格缩进的 "key": {...} 格式）
        example_lines = [l for l in lines if l.strip().startswith('"') and "text" in l and "tags" in l]
        self.assertEqual(3, len(example_lines))
        # 前两个有逗号，最后一个没有
        self.assertTrue(example_lines[0].rstrip().endswith(","), f"Line 1 should end with comma: {example_lines[0]}")
        self.assertTrue(example_lines[1].rstrip().endswith(","), f"Line 2 should end with comma: {example_lines[1]}")
        self.assertFalse(example_lines[2].rstrip().endswith(","), f"Line 3 should not end with comma: {example_lines[2]}")

    def test_build_prompt_handles_audio_format(self) -> None:
        angles = [
            {"key": "歌词大意", "label": "歌词大意", "prompt": "分析歌词", "max_length": 150},
            {"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120},
        ]
        prompt = build_system_prompt_from_angles("音频", angles)
        self.assertIn("音频素材", prompt)

    def test_build_prompt_handles_empty_angles(self) -> None:
        prompt = build_system_prompt_from_angles("视频", [])
        self.assertIn("视频素材", prompt)

    def test_build_prompt_handles_picture_format(self) -> None:
        angles = [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}]
        prompt = build_system_prompt_from_angles("图片", angles)
        self.assertIn("图片素材", prompt)

    def test_build_prompt_handles_text_format(self) -> None:
        angles = [{"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120}]
        prompt = build_system_prompt_from_angles("文本", angles)
        self.assertIn("文本素材", prompt)

    def test_build_prompt_contains_all_angle_keys_in_required_section(self) -> None:
        angles = [
            {"key": "场景", "label": "场景", "prompt": "描述场景", "max_length": 100},
            {"key": "动作", "label": "动作", "prompt": "描述动作", "max_length": 100},
            {"key": "整体", "label": "整体", "prompt": "概括", "max_length": 120},
        ]
        prompt = build_system_prompt_from_angles("视频", angles)
        self.assertIn('"场景", "动作", "整体"', prompt)

    def test_build_prompt_includes_field_meanings(self) -> None:
        angles = [{"key": "整体", "label": "整体", "prompt": "一句话概括内容", "max_length": 120}]
        prompt = build_system_prompt_from_angles("视频", angles)
        self.assertIn("字段含义", prompt)
        self.assertIn("一句话概括内容", prompt)


if __name__ == "__main__":
    unittest.main()