import os
import threading
import time
from contextlib import asynccontextmanager

from fastapi import FastAPI


class HeartbeatMonitor:
    def __init__(self) -> None:
        self.timeout_seconds = float(os.environ.get("BACKEND_HEARTBEAT_TIMEOUT", "8"))
        self.check_interval_seconds = float(os.environ.get("BACKEND_HEARTBEAT_CHECK_INTERVAL", "1"))
        self.startup_grace_seconds = float(os.environ.get("BACKEND_HEARTBEAT_STARTUP_GRACE", "15"))
        self._state_lock = threading.Lock()
        self._watcher_stop_event = threading.Event()
        self._watcher_thread: threading.Thread | None = None
        self.started_at = time.monotonic()
        self.last_heartbeat_at = self.started_at

    def touch(self) -> None:
        with self._state_lock:
            self.last_heartbeat_at = time.monotonic()

    async def heartbeat(self) -> dict[str, bool]:
        self.touch()
        return {"ok": True}

    def _watch_loop(self) -> None:
        while not self._watcher_stop_event.wait(self.check_interval_seconds):
            try:
                with self._state_lock:
                    started_at = self.started_at
                    last_heartbeat_at = self.last_heartbeat_at

                now = time.monotonic()
                if now - started_at < self.startup_grace_seconds:
                    continue

                if now - last_heartbeat_at > self.timeout_seconds:
                    print("[heartbeat] desktop heartbeat timeout, backend will exit", flush=True)
                    os._exit(0)
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
        with self._state_lock:
            self.started_at = now
            self.last_heartbeat_at = now
        self._start_watcher()

        try:
            yield
        finally:
            self._stop_watcher()
