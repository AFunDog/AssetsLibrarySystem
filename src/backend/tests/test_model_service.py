from __future__ import annotations

import asyncio
import sys
import tempfile
import types
from pathlib import Path
import unittest
from unittest.mock import MagicMock, patch

from app.application.services.media_preprocessor import MediaPreprocessor
from app.application.services.model_service import ModelRuntimeContext, ModelService
from app.core.provider_config import ProviderConfig, ProviderConfigManager
from app.schemas.model import ModelGenerateRequest


class ModelServiceTestCase(unittest.TestCase):
    def test_capabilities_falls_back_to_example_config(self) -> None:
        fake_manager = self._build_fake_provider_manager(
            raw={
                "文本": {
                    "provider": "dashscope",
                    "model": "qwen-plus",
                    "temperature": 0.2,
                    "max_tokens": 1024,
                    "reasoning_effort": None,
                    "extra_body": {},
                }
            },
            provider=ProviderConfig(
                provider="dashscope",
                model="qwen-plus",
                api_key="",
            ),
        )

        with patch("app.application.services.model_service.ProviderConfigManager", return_value=fake_manager):
            service = ModelService()

            capabilities = service.get_capabilities()

        self.assertEqual(capabilities.provider_slot, "llm_gateway")
        self.assertEqual(capabilities.provider, "dashscope")
        self.assertTrue(capabilities.model)
        self.assertFalse(capabilities.supports_live_call)

    def test_generate_text_returns_mock_output_without_api_key(self) -> None:
        service = ModelService()

        with (
            patch.object(
                ModelService,
                "_resolve_provider_context_for_asset_format",
                return_value=ModelRuntimeContext(
                    config_slot="音频",
                    provider="dashscope",
                    model="qwen3-omni-30b-a3b-captioner",
                    system_prompt="",
                    prompt="",
                    supports_live_call=False,
                ),
            ),
            patch.object(
                ModelService,
                "_resolve_prompt_context",
                return_value=ModelRuntimeContext(
                    config_slot="音频",
                    provider="dashscope",
                    model="qwen3-omni-30b-a3b-captioner",
                    system_prompt="你是 Assets Library System 的音频素材打标助手。",
                    prompt="请生成一句适合桌面端联调的说明。",
                    supports_live_call=False,
                ),
            ),
        ):
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

        self.assertEqual(
            service._to_file_uri(r"D:\Data\sample.txt"),
            "file://D:/Data/sample.txt",
        )
        self.assertEqual(
            service._build_text_user_prompt("请生成摘要", r"D:\Data\sample.txt"),
            "请生成摘要\n\n素材绝对路径：D:\\Data\\sample.txt",
        )

        with (
            patch.object(Path, "exists", return_value=True),
            patch.object(Path, "read_text", side_effect=self._fake_read_text),
        ):
            self.assertEqual(service._read_text_asset(r"D:\Data\sample.txt"), "第一行\n第二行")

    def test_multimodal_content_uses_file_uri(self) -> None:
        service = ModelService()

        content = service._build_multimodal_content("图片", r"D:\Data\全资源\music\cover.png", "请打标")

        self.assertEqual(content[0], {"image": "file://D:/Data/全资源/music/cover.png"})
        self.assertEqual(content[1], {"text": "请打标"})

    def test_prepare_media_asset_returns_original_when_disabled(self) -> None:
        service = ModelService()
        service._enable_media_preprocess = False

        with patch.object(Path, "exists", return_value=True):
            prepared = service._prepare_media_asset("图片", r"D:\Data\cover.png")

        self.assertEqual(prepared, str(Path(r"D:\Data\cover.png").resolve()))

    def test_prepare_media_asset_uses_temp_directory_for_images(self) -> None:
        service = ModelService()

        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "cover.png"
            source_path.write_bytes(b"fake")
            service._temp_dir = Path(temp_dir) / "temp"

            with patch.object(MediaPreprocessor, "compress_image", return_value=service._temp_dir / "cover-12345678.png") as compress_mock:
                prepared = service._prepare_media_asset("图片", str(source_path))

        compress_mock.assert_called_once()
        self.assertTrue(prepared.endswith(".png"))
        self.assertIn("temp", prepared)

    def test_prepare_media_asset_keeps_original_path_for_audio(self) -> None:
        service = ModelService()

        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "sample.ogg"
            source_path.write_bytes(b"fake-audio")
            prepared = service._prepare_media_asset("音频", str(source_path))

        self.assertEqual(prepared, str(source_path.resolve()))

    def test_compress_video_uses_configured_audio_bitrate(self) -> None:
        service = ModelService()

        with tempfile.TemporaryDirectory() as temp_dir:
            source_path = Path(temp_dir) / "source.mp4"
            target_path = Path(temp_dir) / "target.mp4"

            with (
                patch("app.application.services.media_preprocessor.shutil.which", return_value="ffmpeg"),
                patch.object(MediaPreprocessor, "run_ffmpeg_or_fallback", return_value=target_path) as run_mock,
            ):
                result = service._compress_video(source_path, target_path)

        command = run_mock.call_args.args[0]
        bitrate_index = command.index("-b:a")
        self.assertEqual(command[bitrate_index + 1], "128k")
        self.assertEqual(result, target_path)

    def test_call_dashscope_uses_preprocessed_media_path(self) -> None:
        service = ModelService()
        provider = ProviderConfig(provider="dashscope", model="qwen-vl-max", api_key="sk-test")
        fake_response = {"output": {"choices": [{"message": {"content": [{"text": "ok"}]}}]}}

        with (
            patch("app.application.services.model_service.ProviderConfigManager") as manager_cls,
            patch.object(ModelService, "_prepare_media_asset", return_value=r"D:\Data\temp\cover.jpg") as prepare_mock,
            patch.object(ModelService, "_call_multimodal_sync", return_value=fake_response) as call_mock,
        ):
            manager = MagicMock()
            manager.get.return_value = provider
            manager_cls.return_value = manager

            output_text, usage = asyncio.run(
                service._call_dashscope(
                    ModelRuntimeContext(
                        config_slot="图片",
                        provider="dashscope",
                        model="qwen-vl-max",
                        system_prompt="",
                        prompt="",
                        supports_live_call=True,
                    ),
                    "system",
                    "请描述",
                    "图片",
                    r"D:\Data\cover.png",
                    "qwen-vl-max",
                )
            )

        prepare_mock.assert_called_once_with("图片", r"D:\Data\cover.png")
        sent_content = call_mock.call_args.args[3]
        self.assertEqual(sent_content[0], {"image": "file://D:/Data/temp/cover.jpg"})
        self.assertEqual(sent_content[1], {"text": "请描述"})
        self.assertEqual(output_text, "ok")
        self.assertIsNone(usage)

    def test_call_dashscope_removes_preprocessed_media_after_success(self) -> None:
        service = ModelService()
        provider = ProviderConfig(provider="dashscope", model="qwen-vl-max", api_key="sk-test")
        fake_response = {"output": {"choices": [{"message": {"content": [{"text": "ok"}]}}]}}

        with tempfile.TemporaryDirectory() as temp_dir:
            service._temp_dir = Path(temp_dir)
            prepared_path = service._temp_dir / "cover-prepared.jpg"
            prepared_path.write_bytes(b"prepared")

            with (
                patch("app.application.services.model_service.ProviderConfigManager") as manager_cls,
                patch.object(ModelService, "_prepare_media_asset", return_value=str(prepared_path)),
                patch.object(ModelService, "_call_multimodal_sync", return_value=fake_response),
            ):
                manager_cls.return_value.get.return_value = provider
                asyncio.run(
                    service._call_dashscope(
                        ModelRuntimeContext("图片", "dashscope", "qwen-vl-max", "", "", True),
                        "system",
                        "请描述",
                        "图片",
                        str(Path(temp_dir) / "cover.png"),
                        "qwen-vl-max",
                    )
                )

            self.assertFalse(prepared_path.exists())

    def test_call_dashscope_removes_preprocessed_media_after_failure(self) -> None:
        service = ModelService()
        provider = ProviderConfig(provider="dashscope", model="qwen-vl-max", api_key="sk-test")

        with tempfile.TemporaryDirectory() as temp_dir:
            service._temp_dir = Path(temp_dir)
            prepared_path = service._temp_dir / "cover-prepared.jpg"
            prepared_path.write_bytes(b"prepared")

            with (
                patch("app.application.services.model_service.ProviderConfigManager") as manager_cls,
                patch.object(ModelService, "_prepare_media_asset", return_value=str(prepared_path)),
                patch.object(ModelService, "_call_multimodal_sync", side_effect=RuntimeError("模型调用失败")),
            ):
                manager_cls.return_value.get.return_value = provider
                with self.assertRaisesRegex(RuntimeError, "模型调用失败"):
                    asyncio.run(
                        service._call_dashscope(
                            ModelRuntimeContext("图片", "dashscope", "qwen-vl-max", "", "", True),
                            "system",
                            "请描述",
                            "图片",
                            str(Path(temp_dir) / "cover.png"),
                            "qwen-vl-max",
                        )
                    )

            self.assertFalse(prepared_path.exists())

    def test_call_generation_sync_requests_json_object_response(self) -> None:
        service = ModelService()
        provider = ProviderConfig(provider="dashscope", model="qwen-plus", api_key="sk-test")
        fake_generation = types.SimpleNamespace(call=MagicMock(return_value={"output": {"choices": [{"message": {"content": "{}"}}]}}))
        fake_dashscope = types.SimpleNamespace(Generation=fake_generation)

        with patch.dict(sys.modules, {"dashscope": fake_dashscope}):
            service._call_generation_sync(
                provider,
                "qwen-plus",
                "system",
                "user prompt",
                "text body",
            )

        self.assertEqual(fake_generation.call.call_args.kwargs["response_format"], {"type": "json_object"})
        self.assertEqual(fake_generation.call.call_args.kwargs["result_format"], "message")

    def test_call_multimodal_sync_requests_json_object_response(self) -> None:
        service = ModelService()
        provider = ProviderConfig(provider="dashscope", model="qwen3-vl-plus", api_key="sk-test")
        fake_multimodal = types.SimpleNamespace(
            call=MagicMock(return_value={"output": {"choices": [{"message": {"content": [{"text": "{}"}]}}]}})
        )
        fake_dashscope = types.SimpleNamespace(MultiModalConversation=fake_multimodal)

        with patch.dict(sys.modules, {"dashscope": fake_dashscope}):
            service._call_multimodal_sync(
                provider,
                "qwen3-vl-plus",
                "system",
                [{"image": "file://D:/Data/cover.png"}],
            )

        self.assertEqual(fake_multimodal.call.call_args.kwargs["response_format"], {"type": "json_object"})

    def test_build_response_format_prefers_provider_override(self) -> None:
        service = ModelService()
        provider = ProviderConfig(
            provider="dashscope",
            model="qwen-plus",
            api_key="sk-test",
            extra_body={"response_format": {"type": "json_object"}},
        )

        self.assertEqual(service._build_response_format(provider), {"type": "json_object"})

    def test_audio_format_uses_audio_compatible_model_when_needed(self) -> None:
        service = ModelService()

        self.assertEqual(service._resolve_model_name("qwen-vl-max", "音频"), "qwen3-omni-30b-a3b-captioner")
        self.assertEqual(service._resolve_model_name("qwen3-omni-flash", "音频"), "qwen3-omni-flash")

    def test_asset_format_resolves_dedicated_provider_slot(self) -> None:
        service = ModelService()

        context = service._resolve_provider_context_for_asset_format("文本")

        self.assertEqual(context.config_slot, "文本")
        self.assertEqual(context.provider, "dashscope")

    def test_provider_config_manager_is_reused_across_resolutions(self) -> None:
        fake_manager = self._build_fake_provider_manager(
            raw={"文本": {}},
            provider=ProviderConfig(provider="dashscope", model="qwen-plus", api_key=""),
        )

        with patch("app.application.services.model_service.ProviderConfigManager", return_value=fake_manager) as manager_cls:
            service = ModelService()
            service._resolve_provider_context_for_asset_format("文本")
            service._resolve_provider_context_for_asset_format("文本")

        manager_cls.assert_called_once_with(service._providers_path)

    def test_shared_api_key_is_inherited_by_all_slots(self) -> None:
        with patch.object(
            ProviderConfigManager,
            "_load",
            return_value={
                "api_key": "sk-shared",
                "文本": {
                    "provider": "dashscope",
                    "model": "qwen-plus",
                    "temperature": 0.2,
                    "max_tokens": 1024,
                    "reasoning_effort": None,
                    "extra_body": {},
                },
                "图片": {
                    "provider": "dashscope",
                    "model": "qwen-vl-max",
                    "temperature": 0.2,
                    "max_tokens": 1024,
                    "reasoning_effort": None,
                    "extra_body": {},
                },
            },
        ):
            with patch.object(ProviderConfigManager, "_load_shared_api_key", return_value="sk-shared"):
                manager = ProviderConfigManager("configs/providers.yaml")

        text_config = manager.get("文本")
        image_config = manager.get("图片")

        self.assertEqual(text_config.api_key, "sk-shared")
        self.assertEqual(text_config.api_key, image_config.api_key)

    def test_extract_token_usage_reads_dashscope_response(self) -> None:
        service = ModelService()
        response = {
            "usage": {
                "input_tokens": 11,
                "output_tokens": 7,
                "total_tokens": 18,
                "input_tokens_details": {
                    "cached_tokens": 3,
                },
                "output_tokens_details": {
                    "reasoning_tokens": 2,
                },
                "prompt_tokens_details": {
                    "cached_tokens": 3,
                },
            }
        }

        usage = service._extract_token_usage(response)

        self.assertIsNotNone(usage)
        self.assertEqual(usage.input_tokens, 11)
        self.assertEqual(usage.output_tokens, 7)
        self.assertEqual(usage.total_tokens, 18)
        self.assertEqual(usage.input_tokens_details, {"cached_tokens": 3})
        self.assertEqual(usage.output_tokens_details, {"reasoning_tokens": 2})
        self.assertEqual(usage.prompt_tokens_details, {"cached_tokens": 3})

    def test_extract_token_usage_reads_dashscope_input_output_schema(self) -> None:
        service = ModelService()
        response = {
            "usage": {
                "input_tokens": 22,
                "output_tokens": 17,
            }
        }

        usage = service._extract_token_usage(response)

        self.assertIsNotNone(usage)
        self.assertEqual(usage.input_tokens, 22)
        self.assertEqual(usage.output_tokens, 17)
        self.assertEqual(usage.total_tokens, 39)

    def test_extract_token_usage_reads_dashscope_models_schema(self) -> None:
        service = ModelService()
        response = {
            "usage": {
                "models": [
                    {
                        "model_id": "qwen-plus",
                        "input_tokens": 75,
                        "output_tokens": 36,
                    }
                ]
            }
        }

        usage = service._extract_token_usage(response)

        self.assertIsNotNone(usage)
        self.assertEqual(usage.input_tokens, 75)
        self.assertEqual(usage.output_tokens, 36)
        self.assertEqual(usage.total_tokens, 111)

    def _build_fake_provider_manager(
        self,
        *,
        raw: dict[str, object],
        provider: ProviderConfig,
    ) -> object:
        class FakeProviderManager:
            def __init__(self, raw_data: dict[str, object], provider_config: ProviderConfig) -> None:
                self._raw = raw_data
                self._provider_config = provider_config

            def get(self, slot: str) -> ProviderConfig:
                if slot not in self._raw:
                    raise KeyError(slot)
                return self._provider_config

            def has_slot(self, slot: str) -> bool:
                return slot in self._raw

            def slots(self) -> tuple[str, ...]:
                return tuple(self._raw)

        return FakeProviderManager(raw, provider)

    @staticmethod
    def _fake_read_text(encoding: str | None = None, errors: str | None = None) -> str:
        if encoding == "utf-8-sig":
            raise UnicodeDecodeError("utf-8", b"", 0, 1, "bad")
        if encoding in {"utf-8", "gb18030"}:
            return "第一行\n第二行"
        raise UnicodeDecodeError("utf-8", b"", 0, 1, "bad")


if __name__ == "__main__":
    unittest.main()
