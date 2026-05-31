import asyncio
import unittest

from app.application.services.model_service import ModelService
from app.schemas.model import ModelGenerateRequest


class ModelServiceTestCase(unittest.TestCase):
    def test_capabilities_falls_back_to_example_config(self) -> None:
        service = ModelService()

        capabilities = service.get_capabilities()

        self.assertEqual(capabilities.provider_slot, "llm_gateway")
        self.assertEqual(capabilities.provider, "dashscope")
        self.assertTrue(capabilities.model)
        self.assertFalse(capabilities.supports_live_call)

    def test_generate_text_returns_mock_output_without_api_key(self) -> None:
        service = ModelService()

        response = asyncio.run(
            service.generate_text(
                ModelGenerateRequest(
                    prompt="请生成一句适合桌面端联调的说明。",
                )
            )
        )

        self.assertEqual(response.mode, "mock")
        self.assertIn("桌面端联调阶段", response.output_text)


if __name__ == "__main__":
    unittest.main()
