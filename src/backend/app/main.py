from fastapi import FastAPI

from app.api.routes.assets import router as assets_router
from app.api.routes.health import router as health_router
from app.api.routes.search import router as search_router
from app.api.routes.tagging import router as tagging_router
from app.core.config import settings


app = FastAPI(
    title=settings.app_name,
    version=settings.app_version,
    description="素材管理系统后端骨架，当前只提供占位接口。",
)

app.include_router(health_router)
app.include_router(assets_router, prefix=settings.api_prefix)
app.include_router(search_router, prefix=settings.api_prefix)
app.include_router(tagging_router, prefix=settings.api_prefix)
