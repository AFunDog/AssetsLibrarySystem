from __future__ import annotations

from pathlib import Path
from http import HTTPStatus
from typing import Any

from app.core.config import get_settings
from app.core.provider_config import ProviderConfigManager
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
        cache_folder = _resolve_search_cache_dir(settings.search_cache_dir, settings.data_root)
        self._dashscope_api_key = settings.dashscope_api_key
        if not self._dashscope_api_key:
            try:
                providers_path = Path(__file__).resolve().parents[3] / "configs/providers.yaml"
                self._dashscope_api_key = ProviderConfigManager(providers_path).get("文本").api_key
            except (FileNotFoundError, KeyError, ValueError):
                self._dashscope_api_key = ""

        self._model_bundle = model_bundle or LocalSearchModelBundle(
            SearchModelConfig(
                embedding_model=settings.search_embed_model or DEFAULT_EMBED_MODEL_NAME,
                rerank_model=settings.search_rerank_model or DEFAULT_RERANK_MODEL_NAME,
                cache_folder=cache_folder,
            )
        )

    def vectorize(self, payload: SearchIndexRequest) -> SearchIndexResponse:
        token_usage: int | None = None
        if payload.provider == "dashscope":
            vector, token_usage = self._dashscope_vectorize(payload.model, payload.description, payload.embedding_dimensions)
            embedding_model = _format_dashscope_embedding_model(payload.model, payload.embedding_dimensions)
        else:
            self._require_local_model(payload.model, self._model_bundle.embedding_model_name)
            vector = self._model_bundle.encode_documents([payload.description])[0]
            embedding_model = payload.model
        return SearchIndexResponse(
            asset_id=payload.asset_id,
            asset_name=payload.asset_name,
            asset_format=payload.asset_format,
            asset_path=payload.asset_path,
            description=payload.description,
            vector=[float(item) for item in vector.tolist()],
            vector_dim=int(vector.shape[0]),
            embedding_model=embedding_model,
            token_usage=token_usage,
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
        token_usage: int | None = None
        if payload.provider == "dashscope":
            rerank_scores, token_usage = self._dashscope_rerank(payload.model, payload.query, descriptions)
        else:
            self._require_local_model(payload.model, self._model_bundle.rerank_model_name)
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
            rerank_model=payload.model,
            results=ranked_items[: payload.final_top_k],
            token_usage=token_usage,
        )

    @staticmethod
    def _require_local_model(requested_model: str, configured_model: str) -> None:
        if requested_model != configured_model:
            raise ValueError(f"本地模型未加载：{requested_model}，当前配置为：{configured_model}")

    def _dashscope_vectorize(self, model: str, text: str, embedding_dimensions: int | None = None):
        import dashscope
        import numpy as np

        request_args = {
            "api_key": self._dashscope_api_key or None,
            "model": model,
            "input": text,
        }
        if embedding_dimensions is not None:
            request_args["dimension"] = embedding_dimensions

        response = dashscope.TextEmbedding.call(**request_args)
        if response.status_code != HTTPStatus.OK:
            raise RuntimeError(f"DashScope 向量化失败：{response}")
        embeddings = response.output["embeddings"]
        if not embeddings:
            raise RuntimeError("DashScope 返回空向量。")
        token_usage = _extract_token_usage(response)
        return np.asarray(embeddings[0]["embedding"], dtype=np.float32), token_usage

    def _dashscope_rerank(self, model: str, query: str, documents: list[str]) -> tuple[list[float], int | None]:
        import dashscope

        response = dashscope.TextReRank.call(
            api_key=self._dashscope_api_key or None,
            model=model,
            query=query,
            documents=documents,
            top_n=len(documents),
            return_documents=False,
        )
        if response.status_code != HTTPStatus.OK:
            raise RuntimeError(f"DashScope 重排序失败：{response}")
        results = response.output["results"]
        scores = [0.0] * len(documents)
        for result in results:
            scores[int(result["index"])] = float(result["relevance_score"])
        return scores, _extract_token_usage(response)


def _format_dashscope_embedding_model(model: str, embedding_dimensions: int | None) -> str:
    if embedding_dimensions is None:
        return model
    return f"{model}@{embedding_dimensions}d"


def _extract_token_usage(response: Any) -> int | None:
    usage = getattr(response, "usage", None)
    if usage is None and isinstance(response, dict):
        usage = response.get("usage")
    if usage is None:
        try:
            usage = response["usage"]
        except (KeyError, TypeError):
            return None

    if isinstance(usage, dict):
        for key in ("total_tokens", "input_tokens", "tokens"):
            value = usage.get(key)
            if isinstance(value, int):
                return value
            if isinstance(value, str) and value.isdigit():
                return int(value)
        return None

    total_tokens = getattr(usage, "total_tokens", None)
    if isinstance(total_tokens, int):
        return total_tokens
    input_tokens = getattr(usage, "input_tokens", None)
    if isinstance(input_tokens, int):
        return input_tokens
    return None


def _resolve_search_cache_dir(search_cache_dir: str | None, data_root: str | None) -> str | None:
    normalized_cache_dir = (search_cache_dir or "").strip()
    if normalized_cache_dir:
        return str(Path(normalized_cache_dir).expanduser().resolve())

    normalized_data_root = (data_root or "").strip()
    if normalized_data_root:
        return str((Path(normalized_data_root).expanduser().resolve() / "huggingface"))

    return None
