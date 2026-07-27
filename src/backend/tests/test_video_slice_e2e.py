"""
视频切片描述端到端测试脚本。

用法：
  python test_video_slice_e2e.py                          # 使用默认测试视频
  python test_video_slice_e2e.py --video /path/to/video   # 使用指定视频
  python test_video_slice_e2e.py --no-slice               # 禁用切片对比测试
"""

from __future__ import annotations

import argparse
import asyncio
import json
import subprocess
import sys
import tempfile
from pathlib import Path


def create_test_video(duration: int = 70) -> str:
    """创建测试视频。超过 60s 会触发切片。"""
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
        # 回退：循环短视频
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


async def test_video(video_path: str, enable_slicing: bool):
    """运行视频描述测试"""
    sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src" / "backend"))
    from app.application.services.model_service import ModelService
    from app.schemas.model import ModelGenerateRequest, AngleDef

    svc = ModelService()
    mode = "切片" if enable_slicing else "非切片"
    print(f"\n{'='*60}")
    print(f"测试模式: {mode}")
    print(f"视频: {video_path}")
    print(f"{'='*60}")

    req = ModelGenerateRequest(
        asset_format="视频",
        asset_path=video_path,
        subtype="实拍",
        mock_response=False,
        angles=[
            AngleDef(key="场景", label="场景环境", prompt="描述视频中的场景和环境", max_length=100),
            AngleDef(key="整体", label="整体", prompt="一句话概括", max_length=120),
        ],
        enable_slicing=enable_slicing,
        slice_threshold=30.0,
    )

    resp = await svc.generate_text(req)
    parsed = json.loads(resp.output_text)

    print(f"模式: {resp.mode}")
    print(f"Token: {resp.token_usage.total_tokens if resp.token_usage else 'N/A'}")

    if "segments" in parsed:
        segments = parsed["segments"]
        print(f"\n场景数: {len(segments)}")
        for i, seg in enumerate(segments):
            overall = seg.get("整体", {})
            text = overall.get("text", "") if isinstance(overall, dict) else ""
            print(f"  [{i+1}] {seg['start_time']:.1f}s-{seg['end_time']:.1f}s: {text[:80]}")

    overall = parsed.get("整体", {})
    if isinstance(overall, dict):
        print(f"\n整体摘要: {overall.get('text', '')[:150]}")
        print(f"整体标签: {overall.get('tags', [])}")

    return parsed


async def main():
    parser = argparse.ArgumentParser(description="视频切片描述端到端测试")
    parser.add_argument("--video", help="测试视频路径（默认自动创建）")
    parser.add_argument("--duration", type=int, default=70, help="测试视频时长（秒）")
    parser.add_argument("--no-slice", action="store_true", help="跳过切片测试")
    args = parser.parse_args()

    video_path = args.video or create_test_video(args.duration)

    # 非切片测试（用于对比）
    if args.duration < 60:
        await test_video(video_path, enable_slicing=False)

    # 切片测试
    if not args.no_slice and args.duration >= 30:
        await test_video(video_path, enable_slicing=True)

    print("\n✅ 测试完成")


if __name__ == "__main__":
    asyncio.run(main())