from fastapi import APIRouter, Header

from app.core.heartbeat import HeartbeatMonitor

def build_internal_router(monitor: HeartbeatMonitor) -> APIRouter:
    router = APIRouter(prefix="/internal", tags=["internal"])

    @router.post("/heartbeat")
    async def heartbeat(x_backend_token: str = Header(default="")) -> dict[str, bool]:
        return await monitor.verify_token(x_backend_token)

    return router
