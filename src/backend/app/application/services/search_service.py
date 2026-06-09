from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from app.core.config import get_settings
from app.core.paths import ensure_shared_data_dir
from app.infrastructure.search.hnsw_index_manager import HnswIndexManager
from app.infrastructure.search.search_model_bundle import (
    DEFAULT_EMBED_MODEL_NAME,
    DEFAULT_RERANK_MODEL_NAME,
    LocalSearchModelBundle,
    SearchModelConfig,
)
from app.infrastructure.search.sqlite_vector_repository import (
    IndexedAssetVectorRecord,
    SqliteVectorRepository,
)
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchExploreRequest,
    SearchExploreResponse,
    SearchReindexResponse,
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
        vector_repository: SqliteVectorRepository | None = None,
        vector_index_manager: HnswIndexManager | None = None,
    ) -> None:
        settings = get_settings()
        data_dir = ensure_shared_data_dir()
        cache_folder = settings.search_cache_dir or str(data_dir / "huggingface")
        vector_database_path = settings.description_vector_database_path or str(data_dir / "asset_descriptions.db")
        vector_index_path = settings.search_vector_index_path or str(data_dir / "asset_search_vectors.hnsw")
        vector_metadata_path = settings.search_vector_metadata_path or str(data_dir / "asset_search_vectors.meta.json")

        self._model_bundle = model_bundle or LocalSearchModelBundle(
            SearchModelConfig(
                embedding_model=settings.search_embed_model or DEFAULT_EMBED_MODEL_NAME,
                rerank_model=settings.search_rerank_model or DEFAULT_RERANK_MODEL_NAME,
                cache_folder=cache_folder,
            )
        )
        self._vector_repository = vector_repository
        self._vector_index_manager = vector_index_manager
        self._vector_database_path = vector_database_path
        self._vector_index_path = vector_index_path
        self._vector_metadata_path = vector_metadata_path

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

    def explore(self, payload: SearchExploreRequest) -> SearchExploreResponse:
        vector_repository = self._get_vector_repository()
        records = vector_repository.list_documents()
        if not records:
            raise ValueError("当前没有可检索的素材描述。")

        state = vector_repository.get_state()
        vector_index_manager = self._get_vector_index_manager()
        vector_index_manager.ensure_current(records, state)

        query_vector = self._model_bundle.encode_query(payload.query)
        search_top_k = min(
            len(records),
            max(payload.candidate_top_k * 8, payload.candidate_top_k, 50),
        )
        search_results = vector_index_manager.search(query_vector, search_top_k)

        record_by_doc_id = {record.doc_id: record for record in records}
        candidates: list[VectorCandidate] = []
        for doc_id, embedding_similarity in search_results:
            record = record_by_doc_id.get(doc_id)
            if record is None:
                continue
            if payload.asset_format is not None and record.asset_format != payload.asset_format:
                continue
            vector_distance = max(0.0, 1.0 - embedding_similarity)
            candidates.append(VectorCandidate(
                record=record,
                embedding_similarity=embedding_similarity,
                vector_distance=vector_distance,
            ))

        if not candidates:
            raise ValueError("未找到符合条件的素材。")

        rerank_scores = self._model_bundle.rerank(
            payload.query,
            [candidate.record.segment_text for candidate in candidates],
        )
        normalized_rerank_scores = self._normalize_scores(rerank_scores)

        scored_candidates: list[ScoredVectorCandidate] = []
        for candidate, rerank_score, normalized_rerank_score in zip(
            candidates,
            rerank_scores,
            normalized_rerank_scores,
            strict=True,
        ):
            combined_score = self._combine_scores(candidate.embedding_similarity, normalized_rerank_score)
            scored_candidates.append(
                ScoredVectorCandidate(
                    record=candidate.record,
                    embedding_similarity=candidate.embedding_similarity,
                    vector_distance=candidate.vector_distance,
                    rerank_score=rerank_score,
                    normalized_rerank_score=normalized_rerank_score,
                    combined_score=combined_score,
                )
            )

        ranked_items = self._aggregate_candidates(scored_candidates, payload.candidate_top_k)
        ranked_items.sort(key=lambda item: item.combined_score if item.combined_score is not None else item.rerank_score, reverse=True)

        return SearchExploreResponse(
            query=payload.query,
            candidate_top_k=min(payload.candidate_top_k, len(ranked_items)),
            final_top_k=min(payload.final_top_k, len(ranked_items)),
            asset_format=payload.asset_format,
            embedding_model=self._model_bundle.embedding_model_name,
            rerank_model=self._model_bundle.rerank_model_name,
            results=ranked_items[: payload.final_top_k],
        )

    def rebuild_index(self) -> SearchReindexResponse:
        vector_repository = self._get_vector_repository()
        records = vector_repository.list_documents()
        if not records:
            raise ValueError("当前没有可重建索引的素材描述。")

        vector_dim = int(records[0].vector.shape[0])
        vector_index_manager = self._get_vector_index_manager()
        state = vector_repository.get_state()
        vector_index_manager.rebuild(records, state)

        return SearchReindexResponse(
            document_count=state.document_count,
            vector_dim=vector_dim,
            database_path=str(vector_repository.database_path),
            index_path=str(vector_index_manager.index_path),
            metadata_path=str(vector_index_manager.metadata_path),
            embedding_models=sorted({record.embedding_model for record in records}),
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

    def _get_vector_repository(self) -> SqliteVectorRepository:
        if self._vector_repository is None:
            self._vector_repository = SqliteVectorRepository(self._vector_database_path)
        return self._vector_repository

    def _get_vector_index_manager(self) -> HnswIndexManager:
        if self._vector_index_manager is None:
            self._vector_index_manager = HnswIndexManager(
                self._vector_index_path,
                self._vector_metadata_path,
            )
        return self._vector_index_manager

    @staticmethod
    def _normalize_scores(scores: list[float]) -> list[float]:
        if not scores:
            return []

        min_score = min(scores)
        max_score = max(scores)
        if max_score == min_score:
            return [1.0 for _ in scores]

        scale = max_score - min_score
        return [(score - min_score) / scale for score in scores]

    @staticmethod
    def _combine_scores(embedding_similarity: float, normalized_rerank_score: float) -> float:
        vector_weight = 0.35
        rerank_weight = 0.65
        return (embedding_similarity * vector_weight) + (normalized_rerank_score * rerank_weight)

    @staticmethod
    def _angle_weight(angle_type: str) -> float:
        return {
            "全面": 0.4,
            "风格": 0.25,
            "乐器": 0.2,
            "情感": 0.15,
        }.get(angle_type, 0.1)

    def _aggregate_candidates(
        self,
        candidates: list["ScoredVectorCandidate"],
        candidate_top_k: int,
    ) -> list[SearchQueryResultItem]:
        grouped: dict[str, dict[str, ScoredVectorCandidate]] = {}
        for candidate in candidates:
            asset_candidates = grouped.setdefault(candidate.record.asset_id, {})
            existing = asset_candidates.get(candidate.record.angle_type)
            if existing is None or candidate.combined_score > existing.combined_score:
                asset_candidates[candidate.record.angle_type] = candidate

        ranked_items: list[SearchQueryResultItem] = []
        for asset_candidates in grouped.values():
            selected_candidates = list(asset_candidates.values())
            total_weight = sum(self._angle_weight(candidate.record.angle_type) for candidate in selected_candidates)
            if total_weight <= 0:
                continue

            display_candidate = next(
                (candidate for candidate in selected_candidates if candidate.record.angle_type == "全面"),
                max(selected_candidates, key=lambda item: item.combined_score),
            )

            embedding_similarity = sum(
                candidate.embedding_similarity * self._angle_weight(candidate.record.angle_type)
                for candidate in selected_candidates
            ) / total_weight
            vector_distance = sum(
                candidate.vector_distance * self._angle_weight(candidate.record.angle_type)
                for candidate in selected_candidates
            ) / total_weight
            rerank_score = sum(
                candidate.rerank_score * self._angle_weight(candidate.record.angle_type)
                for candidate in selected_candidates
            ) / total_weight
            combined_score = sum(
                candidate.combined_score * self._angle_weight(candidate.record.angle_type)
                for candidate in selected_candidates
            ) / total_weight

            ranked_items.append(
                SearchQueryResultItem(
                    asset_id=display_candidate.record.asset_id,
                    asset_name=display_candidate.record.asset_name,
                    asset_format=display_candidate.record.asset_format,
                    asset_path=display_candidate.record.asset_path,
                    description=display_candidate.record.description,
                    tags=display_candidate.record.tags,
                    generated_at=display_candidate.record.generated_at,
                    embedding_similarity=embedding_similarity,
                    vector_distance=vector_distance,
                    rerank_score=rerank_score,
                    combined_score=combined_score,
                )
            )

        ranked_items.sort(key=lambda item: item.combined_score if item.combined_score is not None else item.rerank_score, reverse=True)
        return ranked_items[:candidate_top_k]


@dataclass(slots=True)
class VectorCandidate:
    record: IndexedAssetVectorRecord
    embedding_similarity: float
    vector_distance: float


@dataclass(slots=True)
class ScoredVectorCandidate:
    record: IndexedAssetVectorRecord
    embedding_similarity: float
    vector_distance: float
    rerank_score: float
    normalized_rerank_score: float
    combined_score: float
