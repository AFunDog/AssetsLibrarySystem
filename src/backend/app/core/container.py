from __future__ import annotations

from dataclasses import dataclass

from app.application.services.model_service import ModelService
from app.application.services.search_service import SearchService


@dataclass(slots=True)
class AppContainer:
    model_service: ModelService
    search_service: SearchService

    def close(self) -> None:
        """生命周期结束时释放资源。当前无持有需显式关闭的客户端，保留钩子供后续扩展。"""
        return None


def build_app_container() -> AppContainer:
    return AppContainer(
        model_service=ModelService(),
        search_service=SearchService(),
    )