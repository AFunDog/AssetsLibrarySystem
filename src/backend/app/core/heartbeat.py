import asyncio
import os
import time
from contextlib import asynccontextmanager

from fastapi import FastAPI


class HeartbeatMonitor:
    def __init__(self) -> None:
        self.timeout_seconds = float(os.environ.get("BACKEND_HEARTBEAT_TIMEOUT", "8"))
        self.check_interval_seconds = float(os.environ.get("BACKEND_HEARTBEAT_CHECK_INTERVAL", "1"))
        self.startup_grace_seconds = float(os.environ.get("BACKEND_HEARTBEAT_STARTUP_GRACE", "15"))
        self.started_at = time.monotonic()
        self.last_heartbeat_at = self.started_at
        self.watcher_task: asyncio.Task[None] | None = None

    def touch(self) -> None:
        self.last_heartbeat_at = time.monotonic()

    async def heartbeat(self) -> dict[str, bool]:
        self.touch()
        return {"ok": True}

    async def watch(self) -> None:
        while True:
            await asyncio.sleep(self.check_interval_seconds)

            now = time.monotonic()
            if now - self.started_at < self.startup_grace_seconds:
                continue

            if now - self.last_heartbeat_at > self.timeout_seconds:
                print("[heartbeat] desktop heartbeat timeout, backend will exit", flush=True)
                os._exit(0)

    @asynccontextmanager
    async def lifespan(self, app: FastAPI):
        self.started_at = time.monotonic()
        self.last_heartbeat_at = self.started_at
        self.watcher_task = asyncio.create_task(self.watch())

        try:
            yield
        finally:
            if self.watcher_task is not None:
                self.watcher_task.cancel()
                try:
                    await self.watcher_task
                except asyncio.CancelledError:
                    pass
