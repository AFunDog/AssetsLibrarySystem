import asyncio
from pathlib import Path
import tempfile
import unittest

from app.application.services.model_service import ModelService
from app.core.provider_config import ProviderConfigManager
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
                    asset_format="音频",
                    asset_path=r"D:\Data\全资源\music\DDLC\DDLC_PLUS\1.wav",
                    prompt="请生成一句适合桌面端联调的说明。",
                )
            )
        )

        self.assertEqual(response.mode, "mock")
        self.assertIn("桌面端联调阶段", response.output_text)
        self.assertIn("音频", response.output_text)
        self.assertIn(r"D:\Data\全资源\music\DDLC\DDLC_PLUS\1.wav", response.output_text)

    def test_text_asset_helpers_build_real_inputs(self) -> None:
        service = ModelService()

        with tempfile.TemporaryDirectory() as tmp_dir:
            text_path = Path(tmp_dir) / "sample.txt"
            text_path.write_text("第一行\n第二行", encoding="utf-8")

            self.assertEqual(
                service._to_file_uri(str(text_path)),
                f"file://{text_path.resolve().as_posix()}",
            )
            self.assertEqual(service._read_text_asset(str(text_path)), "第一行\n第二行")
            self.assertEqual(
                service._build_text_user_prompt("请生成摘要", str(text_path)),
                f"请生成摘要\n\n素材绝对路径：{text_path}",
            )

    def test_multimodal_content_uses_file_uri(self) -> None:
        service = ModelService()

        content = service._build_multimodal_content("图片", r"D:\Data\全资源\music\cover.png", "请打标")

        self.assertEqual(content[0], {"image": "file://D:/Data/全资源/music/cover.png"})
        self.assertEqual(content[1], {"text": "请打标"})

    def test_audio_format_uses_audio_compatible_model_when_needed(self) -> None:
        service = ModelService()

        self.assertEqual(service._resolve_model_name("qwen-vl-max", "音频"), "qwen3-omni-30b-a3b-captioner")
        self.assertEqual(service._resolve_model_name("qwen3-omni-flash", "音频"), "qwen3-omni-flash")

    def test_asset_format_resolves_dedicated_provider_slot(self) -> None:
        service = ModelService()

        context = service._resolve_provider_context_for_asset_format("文本")

        self.assertEqual(context.config_slot, "文本")
        self.assertEqual(context.provider, "dashscope")

    def test_shared_api_key_is_inherited_by_all_slots(self) -> None:
        manager = ProviderConfigManager("configs/providers.yaml")

        text_config = manager.get("文本")
        image_config = manager.get("图片")

        self.assertTrue(text_config.api_key)
        self.assertEqual(text_config.api_key, image_config.api_key)


if __name__ == "__main__":
    unittest.main()
