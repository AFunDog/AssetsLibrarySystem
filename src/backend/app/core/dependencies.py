from __future__ import annotations

from fastapi import Request

from app.core.container import AppContainer, build_app_container
from app.application.services.model_service import ModelService
from app.application.services.search_service import SearchService


def get_app_container(request: Request) -> AppContainer:
    container = getattr(request.app.state, "container", None)
    if container is None:
        container = build_app_container()
        request.app.state.container = container
    return container


def get_model_service(request: Request) -> ModelService:
    return get_app_container(request).model_service


def get_search_service(request: Request) -> SearchService:
    return get_app_container(request).search_service
