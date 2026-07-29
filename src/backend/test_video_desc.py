"""
视频描述测试工具
支持指定时间范围裁剪，避免描述完整长视频耗时过长。

用法:
  python test_video_desc.py                                            # 描述完整视频
  python test_video_desc.py --start 60 --end 120                       # 描述 1:00-2:00
  python test_video_desc.py --start 300 --end 330 --fps 5              # 描述 5:00-5:30，5fps
  python test_video_desc.py --path "D:/video.mp4" --start 0 --end 30   # 自定义路径
"""

import sys, logging, asyncio, json, os, subprocess, tempfile, argparse
from datetime import datetime
from pathlib import Path

# ── 命令行参数 ──────────────────────────────────────────────────
parser = argparse.ArgumentParser(description="视频描述测试工具")
parser.add_argument("--path", default="D:/Data/全资源/MyGo素材/官方资源/mygo/01.mkv",
                    help="视频文件路径")
parser.add_argument("--start", type=float, default=None,
                    help="开始时间（秒），不指定则从视频开头")
parser.add_argument("--end", type=float, default=None,
                    help="结束时间（秒），不指定则到视频末尾")
parser.add_argument("--fps", type=int, default=5,
                    help="帧率（默认 5fps）")
parser.add_argument("--slice-threshold", type=float, default=30.0,
                    help="视频切片阈值秒数（默认 30s）")
parser.add_argument("--no-slice", action="store_true",
                    help="禁用视频切片")
args = parser.parse_args()

# ── 日志 ─────────────────────────────────────────────────────────
log_file = f"test_video_desc_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"
file_handler = logging.FileHandler(log_file, encoding='utf-8')
file_handler.setLevel(logging.DEBUG)
console_handler = logging.StreamHandler()
console_handler.setLevel(logging.INFO)

logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s [%(levelname)s] %(message)s',
    handlers=[file_handler, console_handler]
)

logger = logging.getLogger(__name__)
sys.path.insert(0, '.')

from app.application.services.model_service import ModelService
from app.schemas.model import ModelGenerateRequest


def trim_video(source_path: str, start: float | None, end: float | None) -> str:
    """用 ffmpeg 裁剪视频到指定时间范围，返回临时文件路径。"""
    if start is None and end is None:
        return source_path  # 不裁剪

    source = Path(source_path)
    if not source.exists():
        logger.error(f"视频文件不存在: {source_path}")
        sys.exit(1)

    with tempfile.NamedTemporaryFile(suffix=f"_{source.stem}_trim.mp4", delete=False) as tmp:
        target_path = tmp.name

    cmd = ["ffmpeg", "-y", "-i", str(source)]
    if start is not None:
        cmd.extend(["-ss", str(start)])
    if end is not None:
        duration = end - (start or 0)
        cmd.extend(["-t", str(duration)])
    cmd.extend(["-c", "copy", "-avoid_negative_ts", "make_zero", target_path])

    logger.info(f"裁剪视频: {source_path}")
    if start is not None:
        logger.info(f"  起始: {start}s ({start//60:.0f}m{start%60:.0f}s)")
    if end is not None:
        logger.info(f"  结束: {end}s ({end//60:.0f}m{end%60:.0f}s)")
    logger.info(f"  命令: {' '.join(cmd)}")

    result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    if result.returncode != 0:
        logger.error(f"ffmpeg 裁剪失败: {result.stderr[:300]}")
        Path(target_path).unlink(missing_ok=True)
        sys.exit(1)

    target_size = os.path.getsize(target_path)
    logger.info(f"裁剪完成: {target_path} ({target_size/1024/1024:.0f} MB)")
    return target_path


async def test():
    # ── 裁剪视频 ──────────────────────────────────────────────
    video_path = trim_video(args.path, args.start, args.end)

    ms = ModelService()

    logger.info(f"开始描述视频: {video_path}")
    logger.info(f"视频大小: {os.path.getsize(video_path) / 1024 / 1024:.0f} MB")
    if args.start or args.end:
        logger.info(f"时间范围: {args.start or 0}s - {args.end or '末尾'}s")

    # ── 构建请求 ──────────────────────────────────────────────
    req = ModelGenerateRequest(
        asset_format='视频',
        asset_path=video_path,
        angles=[
            {'key': '整体', 'label': '整体概述', 'prompt': '用一句话总结这个视频片段', 'max_length': 120},
            {'key': '场景', 'label': '场景环境', 'prompt': '描述这个片段的环境、地点、氛围', 'max_length': 140},
            {'key': '人物', 'label': '人物主体', 'prompt': '描述画面中的人物、服装、表情', 'max_length': 160},
            {'key': '动作', 'label': '动作事件', 'prompt': '描述人物的动作和互动', 'max_length': 180},
            {'key': '情感', 'label': '情绪氛围', 'prompt': '描述画面的情绪氛围', 'max_length': 100},
            {'key': '镜头', 'label': '镜头语言', 'prompt': '描述镜头类型、角度、运镜', 'max_length': 140},
            {'key': '关键画面', 'label': '关键画面', 'prompt': '描述这个片段中可用于检索的标志性画面', 'max_length': 180},
            {'key': '时间线', 'label': '时间线', 'prompt': '按时间顺序描述事件', 'max_length': 300},
        ],
        enable_slicing=not args.no_slice,
        slice_threshold=args.slice_threshold,
        fps=args.fps,
        min_scene_len=15,
        adaptive_threshold=3.0,
        mock_response=False,
    )

    # ── 执行描述 ──────────────────────────────────────────────
    logger.info("调用 generate_text...")
    start = datetime.now()
    try:
        result = await ms.generate_text(req)
        elapsed = (datetime.now() - start).total_seconds()
        logger.info(f"描述完成! 耗时: {elapsed:.0f} 秒")
        logger.info(f"模式: {result.mode}")
        logger.info(f"model: {result.model}")
        logger.info(f"provider: {result.provider}")
        if result.token_usage:
            logger.info(f"token_usage: input={result.token_usage.input_tokens}, "
                        f"output={result.token_usage.output_tokens}, "
                        f"total={result.token_usage.total_tokens}")

        # 解析 JSON 输出
        try:
            parsed = json.loads(result.output_text)
            logger.info(f"JSON 顶层 keys: {list(parsed.keys())}")
            if '整体' in parsed:
                text = parsed['整体'].get('text', '')
                logger.info(f"整体描述: {text[:300]}")
            if 'segments' in parsed:
                logger.info(f"场景片段数: {len(parsed['segments'])}")
                for i, seg in enumerate(parsed['segments'][:5]):
                    keys = [k for k in seg.keys() if k not in ('start_time', 'end_time')]
                    logger.info(f"  片段 {i+1}: start={seg.get('start_time', '?'):.1f}s, "
                                f"end={seg.get('end_time', '?'):.1f}s, angles={keys}")
        except json.JSONDecodeError:
            logger.warning("输出不是 JSON 格式")
            logger.info(f"原始输出(前500字): {result.output_text[:500]}")

    except Exception as e:
        import traceback
        logger.error(f"描述失败: {e}")
        traceback.print_exc(file=file_handler.stream)
        traceback.print_exc()

    # ── 清理临时文件 ──────────────────────────────────────────
    if video_path != args.path:
        logger.info(f"清理临时文件: {video_path}")
        Path(video_path).unlink(missing_ok=True)

    logger.info(f"\n完整日志已保存到: {log_file}")
    print(f"\n=== 完整日志文件: {log_file} ===")

asyncio.run(test())