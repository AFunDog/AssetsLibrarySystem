from __future__ import annotations

from dataclasses import dataclass
import gc
import threading

import numpy as np


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
        self._embed_model_lock = threading.Lock()
        self._rerank_model_lock = threading.Lock()

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

    def close_all_models(self) -> None:
        self._embed_model = None
        self._rerank_model = None
        self._release_model_cache()

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
            with self._embed_model_lock:
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
            with self._rerank_model_lock:
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
