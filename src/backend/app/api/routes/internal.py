from fastapi import APIRouter

from app.core.heartbeat import HeartbeatMonitor


def build_internal_router(monitor: HeartbeatMonitor) -> APIRouter:
    router = APIRouter(prefix="/internal", tags=["internal"])

    @router.post("/heartbeat")
    async def heartbeat() -> dict[str, bool]:
        return await monitor.heartbeat()

    return router
