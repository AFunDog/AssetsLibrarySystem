from __future__ import annotations

from dataclasses import dataclass
import gc

import numpy as np

from app.core.config import get_settings
from app.core.paths import ensure_shared_data_dir
from app.infrastructure.search.hnsw_index_manager import HnswIndexManager
from app.infrastructure.search.sqlite_vector_repository import (
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


DEFAULT_EMBED_MODEL_NAME = "Qwen/Qwen3-Embedding-0.6B"
DEFAULT_RERANK_MODEL_NAME = "Qwen/Qwen3-Reranker-0.6B"


@dataclass(slots=True)
class SearchModelConfig:
    embedding_model: str = DEFAULT_EMBED_MODEL_NAME
    rerank_model: str = DEFAULT_RERANK_MODEL_NAME
    cache_folder: str | None = None


class LocalSearchModelBundle:
    def __init__(self, config: SearchModelConfig) -> None:
        self._config = config
        self._device: str | None = None
        self._embed_model = None
        self._rerank_model = None

    @property
    def embedding_model_name(self) -> str:
        return self._config.embedding_model

    @property
    def rerank_model_name(self) -> str:
        return self._config.rerank_model

    @property
    def device_name(self) -> str:
        return self._device or "cpu"

    def encode_documents(self, descriptions: list[str]) -> np.ndarray:
        model = self._get_embed_model()
        return model.encode_document(
            descriptions,
            normalize_embeddings=True,
            convert_to_numpy=True,
            show_progress_bar=False,
        ).astype(np.float32)

    def encode_query(self, query: str) -> np.ndarray:
        model = self._get_embed_model()
        return model.encode_query(
            query,
            normalize_embeddings=True,
            convert_to_numpy=True,
        ).astype(np.float32)

    def rerank(self, query: str, descriptions: list[str]) -> list[float]:
        model = self._get_rerank_model()
        pairs = [(query, description) for description in descriptions]
        scores = model.predict(pairs)
        return [float(score) for score in scores]

    def warmup_embedding(self) -> None:
        self._get_embed_model()

    def warmup_rerank(self) -> None:
        self._get_rerank_model()

    def close_model(self, model_kind: str) -> tuple[str, bool, list[str], bool]:
        if model_kind == "embedding":
            model_name = self.embedding_model_name
            closed = self._embed_model is not None
            self._embed_model = None
        elif model_kind == "rerank":
            model_name = self.rerank_model_name
            closed = self._rerank_model is not None
            self._rerank_model = None
        else:
            raise ValueError(f"不支持的模型类型: {model_kind}")

        cuda_cache_cleared = self._release_model_cache()
        return model_name, closed, self.loaded_model_kinds(), cuda_cache_cleared

    def loaded_model_kinds(self) -> list[str]:
        loaded_models: list[str] = []
        if self._embed_model is not None:
            loaded_models.append("embedding")
        if self._rerank_model is not None:
            loaded_models.append("rerank")
        return loaded_models

    def model_status(self) -> tuple[str, str, str, list[str], bool, bool]:
        loaded_models = self.loaded_model_kinds()
        return (
            self.embedding_model_name,
            self.rerank_model_name,
            self.device_name,
            loaded_models,
            "embedding" in loaded_models,
            "rerank" in loaded_models,
        )

    def _get_embed_model(self):
        if self._embed_model is None:
            sentence_transformers = self._import_sentence_transformers()
            self._embed_model = sentence_transformers.SentenceTransformer(
                self._config.embedding_model,
                device=self._get_device(),
                cache_folder=self._config.cache_folder,
            )
        return self._embed_model

    def _get_rerank_model(self):
        if self._rerank_model is None:
            sentence_transformers = self._import_sentence_transformers()
            self._rerank_model = sentence_transformers.CrossEncoder(
                self._config.rerank_model,
                device=self._get_device(),
                cache_folder=self._config.cache_folder,
            )
        return self._rerank_model

    def _get_device(self) -> str:
        if self._device is None:
            try:
                import torch
            except ImportError as exc:  # pragma: no cover - import failure path
                raise RuntimeError("检索功能需要安装 `torch`。") from exc
            self._device = "cuda" if torch.cuda.is_available() else "cpu"
        return self._device

    def _release_model_cache(self) -> bool:
        gc.collect()
        cuda_cache_cleared = False
        if self._device == "cuda":
            try:
                import torch
            except ImportError:
                return False

            torch.cuda.empty_cache()
            if hasattr(torch.cuda, "ipc_collect"):
                torch.cuda.ipc_collect()
            cuda_cache_cleared = True
        return cuda_cache_cleared

    @staticmethod
    def _import_sentence_transformers():
        try:
            import sentence_transformers
        except ImportError as exc:  # pragma: no cover - import failure path
            raise RuntimeError("检索功能需要安装 `sentence-transformers`。") from exc
        return sentence_transformers


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
            max(payload.candidate_top_k * 5, payload.candidate_top_k, 50),
        )
        search_results = vector_index_manager.search(query_vector, search_top_k)

        record_by_doc_id = {record.doc_id: record for record in records}
        candidates: list[tuple[float, float, object]] = []
        for doc_id, embedding_similarity in search_results:
            record = record_by_doc_id.get(doc_id)
            if record is None:
                continue
            if payload.asset_format is not None and record.asset_format != payload.asset_format:
                continue
            vector_distance = max(0.0, 1.0 - embedding_similarity)
            candidates.append((embedding_similarity, vector_distance, record))
            if len(candidates) >= payload.candidate_top_k:
                break

        if not candidates:
            raise ValueError("未找到符合条件的素材。")

        rerank_scores = self._model_bundle.rerank(
            payload.query,
            [record.description for _, _, record in candidates],
        )
        normalized_rerank_scores = self._normalize_scores(rerank_scores)

        ranked_items = []
        for (embedding_similarity, vector_distance, record), rerank_score, normalized_rerank_score in zip(
            candidates,
            rerank_scores,
            normalized_rerank_scores,
            strict=True,
        ):
            combined_score = self._combine_scores(embedding_similarity, normalized_rerank_score)
            ranked_items.append(
                SearchQueryResultItem(
                    asset_id=record.asset_id,
                    asset_name=record.asset_name,
                    asset_format=record.asset_format,
                    asset_path=record.asset_path,
                    description=record.description,
                    tags=record.tags,
                    source_store_path=record.source_store_path,
                    generated_at=record.generated_at,
                    embedding_similarity=embedding_similarity,
                    vector_distance=vector_distance,
                    rerank_score=rerank_score,
                    combined_score=combined_score,
                )
            )

        ranked_items.sort(key=lambda item: item.combined_score if item.combined_score is not None else item.rerank_score, reverse=True)

        return SearchExploreResponse(
            query=payload.query,
            candidate_top_k=len(candidates),
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
                    asset_id=candidate.asset_id,
                    asset_name=candidate.asset_name,
                    asset_format=candidate.asset_format,
                    asset_path=candidate.asset_path,
                    description=candidate.description,
                    tags=candidate.tags,
                    source_store_path=candidate.source_store_path,
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
