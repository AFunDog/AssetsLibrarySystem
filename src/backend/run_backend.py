"""uvicorn 启动封装脚本，启动失败时将错误写入日志文件。

用法: python run_backend.py <uvicorn_args>...

示例: python run_backend.py app.main:app --host 127.0.0.1 --port 8000
"""
import sys
import traceback
from pathlib import Path


def _main() -> None:
    error_log = _resolve_error_log()
    _write_startup_marker(sys.argv)

    try:
        from uvicorn import Config, Server

        # 手动解析参数: app.main:app --host 127.0.0.1 --port 8000
        argv = sys.argv[1:]
        app = argv[0] if argv else "app.main:app"
        host = _parse_arg(argv, "--host", "127.0.0.1")
        port = int(_parse_arg(argv, "--port", "8000"))

        config = Config(app, host=host, port=port, log_level="info")
        server = Server(config)
        server.run()
    except Exception:
        error_log.parent.mkdir(parents=True, exist_ok=True)
        error_log.write_text(
            f"启动失败时间: 2026-07-26 22:00:00\n"
            f"Python: {sys.executable}\n"
            f"工作目录: {Path.cwd()}\n"
            f"sys.argv: {sys.argv}\n"
            f"sys.path: {sys.path}\n\n"
            f"异常:\n{traceback.format_exc()}",
            encoding="utf-8",
        )
        print(traceback.format_exc(), file=sys.stderr, flush=True)
        sys.exit(1)


def _parse_arg(argv: list[str], name: str, default: str) -> str:
    try:
        idx = argv.index(name)
        if idx + 1 < len(argv):
            return argv[idx + 1]
    except ValueError:
        pass
    return default


def _resolve_error_log() -> Path:
    cwd = Path.cwd()
    repo_root = cwd.parent.parent
    return repo_root / "data" / "logs" / "startup-error.log"


def _write_startup_marker(argv: list[str]) -> None:
    try:
        marker = _resolve_error_log().parent / "startup-marker.log"
        marker.parent.mkdir(parents=True, exist_ok=True)
        marker.write_text(
            f"startup_marker: 脚本已执行\n"
            f"argv: {argv}\n"
            f"cwd: {Path.cwd()}\n",
            encoding="utf-8",
        )
    except Exception:
        pass


if __name__ == "__main__":
    _main()