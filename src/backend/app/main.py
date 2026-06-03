from __future__ import annotations

from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api.routes.health import router as health_router
from app.api.routes.internal import build_internal_router
from app.api.routes.model import router as model_router
from app.api.routes.search import router as search_router
from app.core.config import settings
from app.core.container import build_app_container
from app.core.heartbeat import HeartbeatMonitor


heartbeat_monitor = HeartbeatMonitor()


@asynccontextmanager
async def lifespan(app: FastAPI):
    app.state.container = build_app_container()
    async with heartbeat_monitor.lifespan(app):
        yield
    container = getattr(app.state, "container", None)
    if container is not None:
        container.close()
        app.state.container = None


app = FastAPI(
    title=settings.app_name,
    version=settings.app_version,
    description="桌面端素材管理系统的 Python 模型网关，当前只承担大模型 HTTP 服务。",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origin_regex=r"https?://(localhost|127\.0\.0\.1)(:\d+)?",
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(health_router)
app.include_router(build_internal_router(heartbeat_monitor))
app.include_router(model_router, prefix=settings.api_prefix)
app.include_router(search_router, prefix=settings.api_prefix)
