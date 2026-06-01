from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path

from app.application.services.model_service import ModelService
from app.schemas.model import ModelGenerateRequest


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="assets-library-system-backend",
        description="单素材打标测试入口，直接调用 Python 后端的模型服务。",
    )
    parser.add_argument(
        "--asset-format",
        required=True,
        choices=["文本", "图片", "视频", "音频"],
        help="素材格式",
    )
    parser.add_argument(
        "--asset-path",
        required=True,
        help="素材文件绝对路径",
    )
    parser.add_argument(
        "--prompt",
        default=None,
        help="覆盖默认提示词",
    )
    parser.add_argument(
        "--system-prompt",
        default=None,
        help="覆盖默认系统提示词",
    )
    parser.add_argument(
        "--mock-response",
        action="store_true",
        help="强制返回占位结果，不调用真实模型",
    )
    parser.add_argument(
        "--providers-path",
        default=str(Path("configs") / "providers.yaml"),
        help="providers.yaml 路径，默认指向当前工作目录下的 configs/providers.yaml",
    )
    parser.add_argument(
        "--prompts-path",
        default=str(Path("configs") / "prompts.yaml"),
        help="prompts.yaml 路径，默认指向当前工作目录下的 configs/prompts.yaml",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="以 JSON 格式输出完整响应",
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    service = ModelService(
        providers_path=args.providers_path,
        prompts_path=args.prompts_path,
    )
    request = ModelGenerateRequest(
        asset_format=args.asset_format,
        asset_path=args.asset_path,
        prompt=args.prompt,
        system_prompt=args.system_prompt,
        mock_response=args.mock_response,
    )

    try:
        response = asyncio.run(service.generate_text(request))
    except Exception as exc:  # noqa: BLE001
        print(f"打标失败: {exc}", file=sys.stderr)
        return 1

    if args.json:
        print(response.model_dump_json(indent=2, ensure_ascii=False))
        return 0

    print(f"模式: {response.mode}")
    print(f"提供商: {response.provider}")
    print(f"模型: {response.model}")
    if response.token_usage is not None:
        print(
            "Token: "
            f"input={response.token_usage.input_tokens}, "
            f"output={response.token_usage.output_tokens}, "
            f"total={response.token_usage.total_tokens}"
        )
    print(f"输出:\n{response.output_text}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
