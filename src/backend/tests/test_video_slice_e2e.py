"""
视频切片描述端到端测试脚本（实时输出模式）。

每个片段的 LLM 输入和输出都会实时打印，无需等待所有片段完成。

用法：
  python test_video_slice_e2e.py --video /path/to/video              # 实时输出
  python test_video_slice_e2e.py --video /path/to/video --full-json  # 最终 JSON
"""

from __future__ import annotations

import argparse
import asyncio
import json
import subprocess
import sys
import tempfile
import time
from pathlib import Path


def create_test_video(duration: int = 70) -> str:
    """创建测试视频。"""
    output = Path(tempfile.gettempdir()) / "test_slice_video.mp4"
    if output.exists():
        return str(output)

    colors = ["red", "green", "blue", "white", "yellow", "cyan", "magenta"]
    n = len(colors)
    seg_duration = duration // n
    filter_complex = "".join(f"[{i}:v]" for i in range(n)) + f"concat=n={n}:v=1:a=0"
    cmd = ["ffmpeg", "-y"]
    for c in colors:
        cmd.extend(["-f", "lavfi", "-i", f"color=c={c}:s=640x360:d={seg_duration}"])
    cmd.extend(["-filter_complex", filter_complex, "-t", str(duration), str(output)])
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        short = Path(tempfile.gettempdir()) / "test_desc_video.mp4"
        if not short.exists():
            subprocess.run([
                "ffmpeg", "-y", "-f", "lavfi", "-i", "color=c=red:s=640x360:d=2",
                "-f", "lavfi", "-i", "color=c=blue:s=640x360:d=2",
                "-f", "lavfi", "-i", "color=c=green:s=640x360:d=2",
                "-filter_complex", "[0:v][1:v][2:v]concat=n=3:v=1:a=0",
                "-t", "6", str(short),
            ], capture_output=True)
        subprocess.run([
            "ffmpeg", "-y", "-stream_loop", "-1", "-i", str(short),
            "-t", str(duration), "-c", "copy", str(output),
        ], capture_output=True)
    return str(output)


async def test_video_verbose(video_path: str, full_json: bool = False):
    """实时输出模式：每个切片的 LLM 输入输出立即可见。"""
    # 延迟导入，确保路径正确
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src" / "backend"))
    from app.application.services.video_scene_detector import VideoSceneDetector
    from app.application.services.video_slice_describer import (
        VideoSliceDescriber,
        get_video_duration,
    )
    from app.application.services.model_service import ModelService
    from app.core.angle_prompt_builder import build_system_prompt_from_angles

    # 1. 检测场景
    print(f"\n视频: {video_path}")
    duration = get_video_duration(video_path)
    print(f"时长: {duration:.1f}s")

    detector = VideoSceneDetector(min_seconds=5.0)
    scenes = detector.detect(video_path)
    print(f"场景数: {len(scenes)}")
    if len(scenes) <= 1:
        print("视频只有一个场景，无需切片")

    # 2. 构建系统 prompt
    angles = [
        {"key": "场景", "label": "场景环境", "prompt": "描述视频中的场景和环境", "max_length": 100},
        {"key": "整体", "label": "整体", "prompt": "一句话概括", "max_length": 120},
    ]
    system_prompt = build_system_prompt_from_angles("视频", angles)

    print(f"\n{'='*70}")
    print(f"系统提示词 ({len(system_prompt)} 字):")
    print(f"{'='*70}")
    for line in system_prompt.split("\n"):
        print(f"  | {line}")
    print()

    # 3. 创建 ModelService 并封装带日志的回调
    svc = ModelService()

    async def logged_call_llm(system_prompt, prompt, asset_format, asset_path):
        """带日志的 LLM 回调"""
        seg_label = prompt.split("\n")[0] if prompt else ""
        print(f"\n{'─'*70}")
        print(f"▶ 发送 LLM 请求 | {seg_label}")
        print(f"{'─'*70}")
        print(f"  用户提示词:")
        for line in prompt.split("\n"):
            print(f"    | {line}")

        t0 = time.time()
        provider_context = svc._resolve_provider_context_for_asset_format(asset_format)
        call_model = svc._resolve_model_name(provider_context.model, asset_format)
        raw_text, usage = await svc._call_dashscope(
            provider_context, system_prompt, prompt, asset_format, asset_path, call_model,
        )
        elapsed = time.time() - t0
        cleaned = svc._clean_llm_output(raw_text)

        print(f"  耗时: {elapsed:.1f}s | 模型: {call_model}")
        print(f"  LLM 原始返回:")
        for line in cleaned.split("\n"):
            print(f"    | {line}")
        print(f"{'─'*70}")

        return raw_text, usage

    async def logged_summarize(text):
        """带日志的总结回调"""
        print(f"\n  📝 正在总结累积摘要...")
        t0 = time.time()
        # 直接调用 svc._summarize_text
        result = await svc._summarize_text(text)
        elapsed = time.time() - t0
        print(f"  📝 总结完成 ({elapsed:.1f}s): {result[:150]}...")
        return result

    # 4. 执行切片描述
    describer = VideoSliceDescriber(
        call_llm=logged_call_llm,
        summarize_fn=logged_summarize,
        slice_threshold=30.0,
        min_seconds=5.0,
        temp_dir=svc._temp_dir,
    )

    result = await describer.describe_sliced(
        video_path=video_path,
        asset_format="视频",
        angles=angles,
        system_prompt=system_prompt,
        prompt="",
    )

    # 5. 输出结果
    print(f"\n{'='*70}")
    print(f"✅ 所有切片描述完成")
    print(f"{'='*70}")

    segments = result.get("segments", [])
    empty = sum(1 for s in segments if not any(
        isinstance(v, dict) and v.get("text") for v in s.values() if isinstance(v, dict)
    ))
    print(f"场景: {len(segments)}, 空: {empty}")

    if full_json:
        print(f"\n完整 JSON:")
        print(json.dumps(result, ensure_ascii=False, indent=2))
    else:
        for i, s in enumerate(segments):
            for k, v in s.items():
                if isinstance(v, dict) and v.get("text"):
                    print(f"  [{i+1}] {k}: {v['text'][:100]}")
                    break
            else:
                print(f"  [{i+1}] (空)")

        overall = result.get("整体", {})
        if isinstance(overall, dict):
            print(f"\n整体摘要: {overall.get('text', '')[:150]}")
            print(f"整体标签: {overall.get('tags', [])}")

    print("\n✅ 测试完成")


async def main():
    parser = argparse.ArgumentParser(description="视频切片描述端到端测试（实时输出）")
    parser.add_argument("--video", help="测试视频路径（默认自动创建）")
    parser.add_argument("--duration", type=int, default=70, help="测试视频时长（秒）")
    parser.add_argument("--full-json", action="store_true", help="输出完整 JSON")
    args = parser.parse_args()

    video_path = args.video or create_test_video(args.duration)
    await test_video_verbose(video_path, full_json=args.full_json)


if __name__ == "__main__":
    asyncio.run(main())