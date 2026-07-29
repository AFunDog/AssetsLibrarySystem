"""
视频描述测试工具
支持指定时间范围裁剪，自动生成人类可读的 HTML/文本报告。

用法:
  python test_video_desc.py                                            # 描述完整视频
  python test_video_desc.py --end 180                                   # 描述前 3 分钟
  python test_video_desc.py --start 60 --end 120 --fps 1               # 1:00-2:00, 1fps
  python test_video_desc.py --path "D:/video.mp4" --start 0 --end 30   # 自定义路径
"""

import sys, logging, asyncio, json, os, subprocess, tempfile, argparse, ast
from datetime import datetime
from pathlib import Path

# ── 命令行参数 ──────────────────────────────────────────────────
parser = argparse.ArgumentParser(description="视频描述测试工具")
parser.add_argument("--path", default="D:/Data/全资源/MyGo素材/官方资源/mygo/01.mkv",
                    help="视频文件路径")
parser.add_argument("--start", type=float, default=None, help="开始时间（秒）")
parser.add_argument("--end", type=float, default=None, help="结束时间（秒）")
parser.add_argument("--fps", type=int, default=5, help="帧率（默认 5fps）")
parser.add_argument("--slice-threshold", type=float, default=30.0, help="切片阈值秒数（默认 30s）")
parser.add_argument("--no-slice", action="store_true", help="禁用视频切片")
args = parser.parse_args()

# ── 日志文件（DEBUG 级别，用于事后解析） ────────────────────────
timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
log_file = f"test_video_desc_{timestamp}.log"
report_file = f"test_video_desc_{timestamp}.md"

file_handler = logging.FileHandler(log_file, encoding='utf-8')
file_handler.setLevel(logging.DEBUG)
console_handler = logging.StreamHandler()
console_handler.setLevel(logging.INFO)

logging.basicConfig(
    level=logging.DEBUG,
    format='%(asctime)s [%(levelname)s] %(message)s',
    handlers=[file_handler, console_handler],
    force=True,
)

logger = logging.getLogger(__name__)
sys.path.insert(0, '.')

from app.application.services.model_service import ModelService
from app.schemas.model import ModelGenerateRequest


# ── DashScope 计费（qwen3.6-flash） ──────────────────────────────
# 参考: https://help.aliyun.com/zh/model-studio/charges
DASHSCOPE_PRICE_PER_1K_INPUT = 0.002   # ¥0.002/1k input tokens
DASHSCOPE_PRICE_PER_1K_OUTPUT = 0.006  # ¥0.006/1k output tokens


def trim_video(source_path: str, start: float | None, end: float | None) -> str:
    """用 ffmpeg 裁剪视频到指定时间范围，返回临时文件路径。"""
    if start is None and end is None:
        return source_path
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
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=60)
    if result.returncode != 0:
        logger.error(f"ffmpeg 裁剪失败: {result.stderr[:300]}")
        Path(target_path).unlink(missing_ok=True)
        sys.exit(1)
    target_size = os.path.getsize(target_path)
    logger.info(f"裁剪完成: {target_path} ({target_size/1024/1024:.0f} MB)")
    return target_path


def parse_log(log_path: str) -> list[dict]:
    """从 DEBUG 日志中解析所有片段的 API 响应和 token 用量。"""
    with open(log_path, 'r', encoding='utf-8') as f:
        content = f.read()
    segments = []
    lines = content.split('\n')
    for line in lines:
        if 'Response:' in line and 'output' in line:
            try:
                idx = line.index('Response: ') + 10
                data = ast.literal_eval(line[idx:])
                usage = data.get('usage', {})
                choices = data.get('output', {}).get('choices', [])
                if choices:
                    content_list = choices[0].get('message', {}).get('content', [])
                    text = ''
                    for item in content_list:
                        if isinstance(item, dict) and 'text' in item:
                            text = item['text']
                            break
                    angle_data = {}
                    if text:
                        try:
                            # Remove markdown code fences if present
                            cleaned = text.strip()
                            if cleaned.startswith('```'):
                                cleaned = cleaned[cleaned.index('\n'):cleaned.rindex('```')].strip()
                            angle_data = json.loads(cleaned)
                        except:
                            pass
                    segments.append({
                        'angles': angle_data,
                        'usage': usage,
                        'is_summary': (usage.get('video_tokens', 0) or 0) == 0,
                    })
            except:
                pass
    return segments


def generate_report(segments: list[dict], elapsed: float, model: str, video_path: str,
                    time_range: str, fps: int, args_info: dict) -> str:
    """生成人类可读的 Markdown 报告。"""
    # 分离场景描述和摘要调用
    scene_segs = [s for s in segments if not s.get('is_summary', False)]
    summary_segs = [s for s in segments if s.get('is_summary', False)]

    lines = []
    lines.append(f"# 视频描述测试报告")
    lines.append(f"")
    lines.append(f"**生成时间**: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    lines.append(f"**视频文件**: `{video_path}`")
    lines.append(f"**时间范围**: {time_range}")
    lines.append(f"**帧率**: {fps}fps")
    lines.append(f"**模型**: {model}")
    lines.append(f"**总耗时**: {elapsed:.0f} 秒 ({elapsed/60:.1f} 分钟)")
    lines.append(f"**场景片段数**: {len(scene_segs)}")
    lines.append(f"**API 调用总计**: {len(segments)} 次（场景描述 {len(scene_segs)} 次 + 摘要 {len(summary_segs)} 次）")
    lines.append(f"")

    # ── Token 统计（仅场景描述） ──
    total_input = sum(s['usage'].get('input_tokens', 0) or 0 for s in scene_segs)
    total_output = sum(s['usage'].get('output_tokens', 0) or 0 for s in scene_segs)
    total_video = sum(s['usage'].get('video_tokens', 0) or 0 for s in scene_segs)
    total_total = total_input + total_output
    
    summary_input = sum(s['usage'].get('input_tokens', 0) or 0 for s in summary_segs)
    summary_output = sum(s['usage'].get('output_tokens', 0) or 0 for s in summary_segs)
    summary_total = summary_input + summary_output
    
    cost_input = total_input / 1000 * DASHSCOPE_PRICE_PER_1K_INPUT
    cost_output = total_output / 1000 * DASHSCOPE_PRICE_PER_1K_OUTPUT
    cost_total = cost_input + cost_output

    lines.append(f"## Token 消耗")
    lines.append(f"")
    lines.append(f"| 项目 | 数值 |")
    lines.append(f"|---|---|")
    lines.append(f"| 总输入 tokens | {total_input:,} |")
    lines.append(f"| ├ 视频帧 tokens | {total_video:,} ({total_video/total_input*100:.0f}%) |" if total_input else "")
    lines.append(f"| ├ 文本 tokens | {total_input - total_video:,} |" if total_input else "")
    lines.append(f"| 总输出 tokens | {total_output:,} |")
    lines.append(f"| **总消耗 tokens** | **{total_total:,}** |")
    if summary_total > 0:
        lines.append(f"| 摘要额外消耗 | {summary_total:,}（未计入场景描述） |")
    lines.append(f"| 平均每场景 | {total_total//len(scene_segs):,} |" if scene_segs else "")
    lines.append(f"")
    lines.append(f"### 费用估算（仅场景描述）")
    lines.append(f"")
    lines.append(f"| 项目 | 单价 | 消耗量 | 费用 |")
    lines.append(f"|---|---|---|---|")
    lines.append(f"| 输入 | ¥{DASHSCOPE_PRICE_PER_1K_INPUT}/1k tokens | {total_input:,} | ¥{cost_input:.4f} |")
    lines.append(f"| 输出 | ¥{DASHSCOPE_PRICE_PER_1K_OUTPUT}/1k tokens | {total_output:,} | ¥{cost_output:.4f} |")
    lines.append(f"| **合计** | | **{total_total:,}** | **¥{cost_total:.4f}** |")
    if summary_total > 0:
        lines.append(f"| 摘要 | 含在输入中 | {summary_total:,} | (已含) |")
    lines.append(f"")

    # ── 片段详情（仅场景描述） ──
    lines.append(f"## 场景片段描述")
    lines.append(f"")
    angle_labels = {'整体': '整体概述', '场景': '场景环境', '人物': '人物主体',
                    '动作': '动作事件', '情感': '情绪氛围', '镜头': '镜头语言',
                    '关键画面': '关键画面', '时间线': '时间线'}
    for i, seg in enumerate(scene_segs):
        usage = seg['usage']
        inp = usage.get('input_tokens', 0) or 0
        out = usage.get('output_tokens', 0) or 0
        vid = usage.get('video_tokens', 0) or 0

        lines.append(f"### 片段 {i+1}")
        lines.append(f"")
        lines.append(f"**Token**: input={inp}, output={out}, video={vid}")
        lines.append(f"")

        angles = seg.get('angles', {})
        if angles:
            for key in ['整体', '场景', '人物', '动作', '情感', '镜头', '关键画面', '时间线']:
                if key in angles:
                    val = angles[key]
                    text = val.get('text', '')
                    tags = val.get('tags', [])
                    lines.append(f"- **{key}**: {text}")
                    if tags:
                        lines.append(f"  - 标签: {'、'.join(tags)}")
            lines.append(f"")
        else:
            lines.append(f"  (描述解析失败)\n")

    # ── 整体总结 ──
    lines.append(f"---")
    lines.append(f"*报告生成时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}*")
    lines.append(f"*原始日志: `{log_file}`*")

    return '\n'.join(lines)


async def test():
    start_time = datetime.now()

    # ── 裁剪视频 ──
    video_path = trim_video(args.path, args.start, args.end)

    ms = ModelService()

    video_size = os.path.getsize(video_path) / 1024 / 1024
    time_range = f"{args.start or 0}s - {args.end or '末尾'}s"
    logger.info(f"开始描述视频: {video_path} ({video_size:.0f} MB)")
    logger.info(f"时间范围: {time_range}, fps={args.fps}")

    # ── 构建请求 ──
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

    logger.info("调用 generate_text...")
    try:
        result = await ms.generate_text(req)
        elapsed = (datetime.now() - start_time).total_seconds()
        logger.info(f"描述完成! 耗时: {elapsed:.0f} 秒")
    except Exception as e:
        import traceback
        logger.error(f"描述失败: {e}")
        traceback.print_exc()
        elapsed = (datetime.now() - start_time).total_seconds()

    # ── 清理临时文件 ──
    if video_path != args.path:
        Path(video_path).unlink(missing_ok=True)

    # ── 解析日志生成报告 ──
    segments = parse_log(log_file)
    report = generate_report(
        segments=segments,
        elapsed=elapsed,
        model='qwen3.6-flash',
        video_path=args.path,
        time_range=time_range,
        fps=args.fps,
        args_info=vars(args),
    )

    with open(report_file, 'w', encoding='utf-8') as f:
        f.write(report)

    # ── 输出摘要 ──
    scene_segs = [s for s in segments if not s.get('is_summary', False)]
    summary_segs = [s for s in segments if s.get('is_summary', False)]
    total_input = sum(s['usage'].get('input_tokens', 0) or 0 for s in scene_segs)
    total_output = sum(s['usage'].get('output_tokens', 0) or 0 for s in scene_segs)
    total_total = total_input + total_output
    cost_total = (total_input / 1000 * 0.002) + (total_output / 1000 * 0.006)

    print(f"\n{'='*60}")
    print(f"  测试完成")
    print(f"  耗时: {elapsed:.0f} 秒 ({elapsed/60:.1f} 分钟)")
    print(f"  场景片段: {len(scene_segs)} 个")
    print(f"  API 调用: {len(segments)} 次（含 {len(summary_segs)} 次摘要总结）")
    print(f"  总 Tokens: {total_total:,}")
    print(f"  预估费用: ¥{cost_total:.4f}（仅场景描述）")
    print(f"  报告文件: {report_file}")
    print(f"  日志文件: {log_file}")
    print(f"{'='*60}")

asyncio.run(test())