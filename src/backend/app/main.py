from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api.routes.health import router as health_router
from app.api.routes.model import router as model_router
from app.core.config import settings


app = FastAPI(
    title=settings.app_name,
    version=settings.app_version,
    description="桌面端素材管理系统的 Python 模型网关，当前只承担大模型 HTTP 服务。",
)

app.add_middleware(
    CORSMiddleware,
    allow_origin_regex=r"https?://(localhost|127\.0\.0\.1)(:\d+)?",
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(health_router)
app.include_router(model_router, prefix=settings.api_prefix)
