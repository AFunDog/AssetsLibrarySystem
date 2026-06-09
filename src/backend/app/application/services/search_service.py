from __future__ import annotations

from app.core.config import get_settings
from app.infrastructure.search.search_model_bundle import (
    DEFAULT_EMBED_MODEL_NAME,
    DEFAULT_RERANK_MODEL_NAME,
    LocalSearchModelBundle,
    SearchModelConfig,
)
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchWarmupResponse,
    SearchModelCloseRequest,
    SearchModelCloseResponse,
    SearchModelStatusResponse,
    SearchQueryRequest,
    SearchQueryResponse,
    SearchQueryResultItem,
)


class SearchService:
    def __init__(
        self,
        model_bundle: LocalSearchModelBundle | None = None,
    ) -> None:
        settings = get_settings()
        cache_folder = settings.search_cache_dir or ""

        self._model_bundle = model_bundle or LocalSearchModelBundle(
            SearchModelConfig(
                embedding_model=settings.search_embed_model or DEFAULT_EMBED_MODEL_NAME,
                rerank_model=settings.search_rerank_model or DEFAULT_RERANK_MODEL_NAME,
                cache_folder=cache_folder,
            )
        )

    def vectorize(self, payload: SearchIndexRequest) -> SearchIndexResponse:
        vector = self._model_bundle.encode_documents([payload.description])[0]
        return SearchIndexResponse(
            asset_id=payload.asset_id,
            asset_name=payload.asset_name,
            asset_format=payload.asset_format,
            asset_path=payload.asset_path,
            description=payload.description,
            vector=[float(item) for item in vector.tolist()],
            vector_dim=int(vector.shape[0]),
            embedding_model=self._model_bundle.embedding_model_name,
        )

    def warmup_embedding_model(self) -> SearchWarmupResponse:
        self._model_bundle.warmup_embedding()
        return SearchWarmupResponse(
            model_kind="embedding",
            model_name=self._model_bundle.embedding_model_name,
            device=self._model_bundle.device_name,
            warmed=True,
        )

    def warmup_rerank_model(self) -> SearchWarmupResponse:
        self._model_bundle.warmup_rerank()
        return SearchWarmupResponse(
            model_kind="rerank",
            model_name=self._model_bundle.rerank_model_name,
            device=self._model_bundle.device_name,
            warmed=True,
        )

    def close_model(self, payload: SearchModelCloseRequest) -> SearchModelCloseResponse:
        model_name, closed, remaining_models, cuda_cache_cleared = self._model_bundle.close_model(payload.model_kind)
        return SearchModelCloseResponse(
            model_kind=payload.model_kind,
            model_name=model_name,
            device=self._model_bundle.device_name,
            closed=closed,
            cuda_cache_cleared=cuda_cache_cleared,
            remaining_loaded_models=remaining_models,
        )

    def close_all_models(self) -> None:
        self._model_bundle.close_all_models()

    def get_model_status(self) -> SearchModelStatusResponse:
        (
            embedding_model_name,
            rerank_model_name,
            device,
            loaded_models,
            embedding_loaded,
            rerank_loaded,
        ) = self._model_bundle.model_status()
        return SearchModelStatusResponse(
            embedding_model_name=embedding_model_name,
            rerank_model_name=rerank_model_name,
            device=device,
            loaded_model_kinds=loaded_models,
            embedding_loaded=embedding_loaded,
            rerank_loaded=rerank_loaded,
            loaded_count=len(loaded_models),
        )

    def rerank(self, payload: SearchQueryRequest) -> SearchQueryResponse:
        candidates = payload.candidates
        descriptions = [candidate.description for candidate in candidates]
        rerank_scores = self._model_bundle.rerank(payload.query, descriptions)

        ranked_items = []
        for candidate, rerank_score in zip(candidates, rerank_scores, strict=True):
            ranked_items.append(
                SearchQueryResultItem(
                    candidate_id=candidate.candidate_id,
                    asset_id=candidate.asset_id,
                    asset_name=candidate.asset_name,
                    asset_format=candidate.asset_format,
                    asset_path=candidate.asset_path,
                    description=candidate.description,
                    tags=candidate.tags,
                    generated_at=candidate.generated_at,
                    embedding_similarity=None,
                    vector_distance=None,
                    rerank_score=rerank_score,
                    combined_score=rerank_score,
                )
            )

        ranked_items.sort(key=lambda item: item.combined_score if item.combined_score is not None else item.rerank_score, reverse=True)

        return SearchQueryResponse(
            query=payload.query,
            final_top_k=min(payload.final_top_k, len(ranked_items)),
            rerank_model=self._model_bundle.rerank_model_name,
            results=ranked_items[: payload.final_top_k],
        )
