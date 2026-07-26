from __future__ import annotations

from http import HTTPStatus
from typing import Any

from app.core.config import get_settings
from app.schemas.search import (
    SearchIndexRequest,
    SearchIndexResponse,
    SearchQueryRequest,
    SearchQueryResponse,
    SearchQueryResultItem,
)


class SearchService:
    def __init__(self) -> None:
        settings = get_settings()
        self._dashscope_api_key = settings.dashscope_api_key

    def vectorize(self, payload: SearchIndexRequest) -> SearchIndexResponse:
        import dashscope
        import numpy as np

        request_args = {
            "api_key": self._dashscope_api_key or None,
            "model": payload.model,
            "input": payload.description,
        }
        if payload.embedding_dimensions is not None:
            request_args["dimension"] = payload.embedding_dimensions

        response = dashscope.TextEmbedding.call(**request_args)
        if response.status_code != HTTPStatus.OK:
            raise RuntimeError(f"DashScope 向量化失败：{response}")
        embeddings = response.output["embeddings"]
        if not embeddings:
            raise RuntimeError("DashScope 返回空向量。")
        vector = np.asarray(embeddings[0]["embedding"], dtype=np.float32)
        token_usage = _extract_token_usage(response)

        embedding_model = _format_embedding_model(payload.model, payload.embedding_dimensions)
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

    def rerank(self, payload: SearchQueryRequest) -> SearchQueryResponse:
        import dashscope

        candidates = payload.candidates
        descriptions = [candidate.description for candidate in candidates]

        response = dashscope.TextReRank.call(
            api_key=self._dashscope_api_key or None,
            model=payload.model,
            query=payload.query,
            documents=descriptions,
            top_n=len(descriptions),
            return_documents=False,
        )
        if response.status_code != HTTPStatus.OK:
            raise RuntimeError(f"DashScope 重排序失败：{response}")
        results = response.output["results"]
        scores = [0.0] * len(descriptions)
        for result in results:
            scores[int(result["index"])] = float(result["relevance_score"])
        token_usage = _extract_token_usage(response)

        ranked_items = []
        for candidate, rerank_score in zip(candidates, scores, strict=True):
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


def _format_embedding_model(model: str, embedding_dimensions: int | None) -> str:
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