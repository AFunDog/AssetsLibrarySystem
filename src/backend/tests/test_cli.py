from __future__ import annotations

import io
from contextlib import redirect_stdout
from unittest.mock import AsyncMock, patch
import unittest

from app.cli import main
from app.schemas.model import ModelGenerateResponse


class CliTestCase(unittest.TestCase):
    def test_main_prints_response_for_single_asset(self) -> None:
        mock_response = ModelGenerateResponse(
            provider_slot="文本",
            provider="dashscope",
            model="qwen-plus",
            mode="mock",
            output_text="标签：日常对话；主题：说明文本",
            system_prompt="你是 Assets Library System 的文本素材打标助手。",
        )

        with patch("app.cli.ModelService") as mock_service_cls:
            mock_service = mock_service_cls.return_value
            mock_service.generate_text = AsyncMock(return_value=mock_response)

            buffer = io.StringIO()
            with redirect_stdout(buffer):
                exit_code = main(
                    [
                        "--asset-format",
                        "文本",
                        "--asset-path",
                        r"D:\Data\sample.txt",
                        "--mock-response",
                    ]
                )

        self.assertEqual(exit_code, 0)
        output = buffer.getvalue()
        self.assertIn("模式: mock", output)
        self.assertIn("输出:", output)
        self.assertIn("标签：日常对话", output)


if __name__ == "__main__":
    unittest.main()
