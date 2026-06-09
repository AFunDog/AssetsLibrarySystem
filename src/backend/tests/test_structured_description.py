from __future__ import annotations

import unittest

from app.infrastructure.search.structured_description import (
    extract_description_by_angle,
    extract_primary_description,
)


class StructuredDescriptionTestCase(unittest.TestCase):
    def test_extract_primary_description_returns_plain_text_when_not_json(self) -> None:
        self.assertEqual(extract_primary_description("普通描述文本"), "普通描述文本")

    def test_extract_primary_description_reads_quanmian_text_field(self) -> None:
        raw = '{"全面":{"text":"一段舒缓的钢琴配乐","tags":["钢琴","舒缓"]},"风格":{"text":"节奏平稳","tags":[]}}'
        self.assertEqual(extract_primary_description(raw), "一段舒缓的钢琴配乐")

    def test_extract_primary_description_reads_quanmian_string_value(self) -> None:
        raw = '{"全面":"一段安静的环境音","情绪":{"text":"安静","tags":[]}}'
        self.assertEqual(extract_primary_description(raw), "一段安静的环境音")

    def test_extract_primary_description_falls_back_when_quanmian_missing(self) -> None:
        raw = '{"风格":{"text":"偏抒情","tags":[]}}'
        self.assertEqual(extract_primary_description(raw), raw)

    def test_extract_description_by_angle_reads_specific_angle_text(self) -> None:
        raw = '{"全面":{"text":"一段舒缓的钢琴配乐","tags":["钢琴","舒缓"]},"风格":{"text":"节奏平稳","tags":[]}}'
        self.assertEqual(extract_description_by_angle(raw, "风格"), "节奏平稳")


if __name__ == "__main__":
    unittest.main()
