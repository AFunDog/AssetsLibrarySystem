import os
import subprocess
import threading
import time
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Final

from fastapi import FastAPI


class HeartbeatMonitor:
    _taskkill_exe: Final[str] = "taskkill"

    def __init__(self) -> None:
        self.timeout_seconds = float(os.environ.get("BACKEND_HEARTBEAT_TIMEOUT", "8"))
        self.check_interval_seconds = float(os.environ.get("BACKEND_HEARTBEAT_CHECK_INTERVAL", "1"))
        self.startup_grace_seconds = float(os.environ.get("BACKEND_HEARTBEAT_STARTUP_GRACE", "15"))
        self.launcher_pid = self._read_launcher_pid()
        self._state_lock = threading.Lock()
        self._watcher_stop_event = threading.Event()
        self._watcher_thread: threading.Thread | None = None
        self.started_at = time.monotonic()
        self.started_wall_at = time.time()
        self.last_heartbeat_at = self.started_at
        self.last_heartbeat_wall_at = self.started_wall_at

    def touch(self) -> None:
        with self._state_lock:
            self.last_heartbeat_at = time.monotonic()
            self.last_heartbeat_wall_at = time.time()

    async def heartbeat(self) -> dict[str, bool]:
        self.touch()
        return {"ok": True}

    @staticmethod
    def _format_wall_time(timestamp: float) -> str:
        return datetime.fromtimestamp(timestamp).astimezone().strftime("%Y-%m-%d %H:%M:%S")

    @staticmethod
    def _read_launcher_pid() -> int:
        raw_value = os.environ.get("BACKEND_LAUNCHER_PID", "").strip()
        if not raw_value:
            return 0

        try:
            return int(raw_value)
        except ValueError:
            return 0

    def _terminate_supervisor_tree(self) -> None:
        parent_pid = os.getppid()
        if self.launcher_pid > 0 and parent_pid == self.launcher_pid:
            return

        if parent_pid <= 1:
            return

        try:
            subprocess.run(
                [self._taskkill_exe, "/PID", str(parent_pid), "/T", "/F"],
                check=False,
                capture_output=True,
                text=True,
            )
        except Exception as exc:  # pragma: no cover - defensive guard
            print(f"[heartbeat] failed to terminate supervisor pid={parent_pid}: {exc}", flush=True)

    def _force_exit(self) -> None:
        current_pid = os.getpid()
        try:
            subprocess.run(
                [self._taskkill_exe, "/PID", str(current_pid), "/T", "/F"],
                check=False,
                capture_output=True,
                text=True,
            )
        except Exception as exc:  # pragma: no cover - defensive guard
            print(f"[heartbeat] failed to terminate backend pid={current_pid}: {exc}", flush=True)

        self._terminate_supervisor_tree()
        os._exit(1)

    def _watch_loop(self) -> None:
        while not self._watcher_stop_event.wait(self.check_interval_seconds):
            try:
                with self._state_lock:
                    started_at = self.started_at
                    last_heartbeat_at = self.last_heartbeat_at
                    started_wall_at = self.started_wall_at
                    last_heartbeat_wall_at = self.last_heartbeat_wall_at

                now = time.monotonic()
                if now - started_at < self.startup_grace_seconds:
                    continue

                if now - last_heartbeat_at > self.timeout_seconds:
                    now_wall = self._format_wall_time(time.time())
                    started_wall = self._format_wall_time(started_wall_at)
                    last_heartbeat_wall = self._format_wall_time(last_heartbeat_wall_at)
                    age_seconds = now - last_heartbeat_at
                    print(
                        f"[heartbeat] {now_wall} desktop heartbeat timeout after {age_seconds:.1f}s; "
                        f"started_at={started_wall}; last_heartbeat_at={last_heartbeat_wall}; backend will exit",
                        flush=True,
                    )
                    self._force_exit()
            except Exception as exc:  # pragma: no cover - defensive guard
                print(f"[heartbeat] watcher error, keep monitoring: {exc}", flush=True)

    def _start_watcher(self) -> None:
        self._watcher_stop_event.clear()
        self._watcher_thread = threading.Thread(
            target=self._watch_loop,
            name="heartbeat-monitor",
            daemon=True,
        )
        self._watcher_thread.start()

    def _stop_watcher(self) -> None:
        self._watcher_stop_event.set()
        watcher_thread = self._watcher_thread
        if watcher_thread is not None and watcher_thread.is_alive():
            watcher_thread.join(timeout=5)
        self._watcher_thread = None

    @asynccontextmanager
    async def lifespan(self, app: FastAPI):
        now = time.monotonic()
        now_wall = time.time()
        with self._state_lock:
            self.started_at = now
            self.last_heartbeat_at = now
            self.started_wall_at = now_wall
            self.last_heartbeat_wall_at = now_wall
        self._start_watcher()
        print(
            "[heartbeat] monitor started at "
            f"{self._format_wall_time(now_wall)}, timeout={self.timeout_seconds:.1f}s, "
            f"startup_grace={self.startup_grace_seconds:.1f}s",
            flush=True,
        )

        try:
            yield
        finally:
            self._stop_watcher()
