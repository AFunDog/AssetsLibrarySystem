from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import os

import numpy as np

from app.core.paths import ensure_shared_data_dir
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
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
    ) -> None:
        data_dir = ensure_shared_data_dir()
        cache_folder = os.getenv("ALS_SEARCH_CACHE_DIR")
        if cache_folder is None or not cache_folder.strip():
            cache_folder = str(data_dir / "huggingface")

        self._model_bundle = model_bundle or LocalSearchModelBundle(
            SearchModelConfig(
                embedding_model=os.getenv("ALS_SEARCH_EMBED_MODEL", DEFAULT_EMBED_MODEL_NAME),
                rerank_model=os.getenv("ALS_SEARCH_RERANK_MODEL", DEFAULT_RERANK_MODEL_NAME),
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
                    source_store_path=candidate.source_store_path,
                    generated_at=candidate.generated_at,
                    embedding_similarity=0.0,
                    rerank_score=rerank_score,
                )
            )

        ranked_items.sort(key=lambda item: item.rerank_score, reverse=True)

        return SearchQueryResponse(
            query=payload.query,
            final_top_k=min(payload.final_top_k, len(ranked_items)),
            rerank_model=self._model_bundle.rerank_model_name,
            results=ranked_items[: payload.final_top_k],
        )
