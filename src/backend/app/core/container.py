from __future__ import annotations

from dataclasses import dataclass

from app.application.services.model_service import ModelService
from app.application.services.search_service import SearchService


@dataclass(slots=True)
class AppContainer:
    model_service: ModelService
    search_service: SearchService

    def close(self) -> None:
        self.search_service.close_all_models()


def build_app_container() -> AppContainer:
    return AppContainer(
        model_service=ModelService(),
        search_service=SearchService(),
    )
