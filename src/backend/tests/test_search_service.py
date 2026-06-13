from __future__ import annotations

import unittest
from unittest.mock import Mock

import numpy as np

from app.application.services.search_service import SearchService, _resolve_search_cache_dir
from app.schemas.search import (
    SearchIndexRequest,
    SearchModelCloseRequest,
    SearchModelStatusResponse,
    SearchQueryCandidate,
    SearchQueryRequest,
)


class FakeModelBundle:
    embedding_model_name = "fake-embed"
    rerank_model_name = "fake-rerank"

    def __init__(self) -> None:
        self._loaded_models = {"embedding", "rerank"}
        self._device_name = "cuda"

    def encode_documents(self, descriptions: list[str]) -> np.ndarray:
        return np.asarray([self._vector_for_text(text) for text in descriptions], dtype=np.float32)

    def encode_query(self, query: str) -> np.ndarray:
        return np.asarray(self._vector_for_text(query), dtype=np.float32)

    def rerank(self, query: str, descriptions: list[str]) -> list[float]:
        query_terms = query.split()
        scores: list[float] = []
        for description in descriptions:
            score = 0.0
            for term in query_terms:
                if term in description:
                    score += 1.0
            scores.append(score)
        return scores

    @property
    def device_name(self) -> str:
        return self._device_name

    def loaded_model_kinds(self) -> list[str]:
        return sorted(self._loaded_models)

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

    def close_model(self, model_kind: str) -> tuple[str, bool, list[str], bool]:
        model_name = self.embedding_model_name if model_kind == "embedding" else self.rerank_model_name
        if model_kind not in self._loaded_models:
            return model_name, False, self.loaded_model_kinds(), True

        self._loaded_models.remove(model_kind)
        return model_name, True, self.loaded_model_kinds(), True

    @staticmethod
    def _vector_for_text(text: str) -> list[float]:
        if "惊吓" in text or "惊慌" in text or "后退" in text:
            return [1.0, 0.0, 0.0]
        if "开心" in text:
            return [0.0, 1.0, 0.0]
        return [0.0, 0.0, 1.0]


class SearchServiceTestCase(unittest.TestCase):
    def test_resolve_search_cache_dir_prefers_explicit_setting(self) -> None:
        cache_dir = _resolve_search_cache_dir(
            search_cache_dir=r"D:\Models\HF",
            data_root=r"D:\Repo\data",
        )

        self.assertEqual(cache_dir, r"D:\Models\HF")

    def test_resolve_search_cache_dir_falls_back_to_data_root_huggingface(self) -> None:
        cache_dir = _resolve_search_cache_dir(
            search_cache_dir="",
            data_root=r"D:\Repo\data",
        )

        self.assertEqual(cache_dir, r"D:\Repo\data\huggingface")

    def test_resolve_search_cache_dir_returns_none_when_no_inputs(self) -> None:
        cache_dir = _resolve_search_cache_dir(
            search_cache_dir="",
            data_root="",
        )

        self.assertIsNone(cache_dir)

    def test_index_returns_vector_only(self) -> None:
        service = SearchService(model_bundle=FakeModelBundle())

        response = service.vectorize(
            SearchIndexRequest(
                provider="local",
                model="fake-embed",
                asset_id="asset-1",
                asset_name="shock.png",
                asset_format="图片",
                asset_path=r"D:\Data\shock.png",
                description="角色受到惊吓后退半步并停顿。",
            )
        )

        self.assertEqual(response.asset_id, "asset-1")
        self.assertEqual(response.asset_name, "shock.png")
        self.assertEqual(response.vector_dim, len(response.vector))
        self.assertEqual(response.embedding_model, "fake-embed")
        self.assertGreater(len(response.vector), 0)

    def test_query_returns_reranked_results_for_candidates(self) -> None:
        service = SearchService(model_bundle=FakeModelBundle())

        response = service.rerank(
            SearchQueryRequest(
                provider="local",
                model="fake-rerank",
                query="惊吓 后退",
                candidates=[
                    SearchQueryCandidate(
                        asset_id="asset-1",
                        asset_name="shock.png",
                        asset_format="图片",
                        asset_path=r"D:\Data\shock.png",
                        description="惊吓 后退 停顿",
                        tags=[],
                    ),
                    SearchQueryCandidate(
                        asset_id="asset-2",
                        asset_name="happy.png",
                        asset_format="图片",
                        asset_path=r"D:\Data\happy.png",
                        description="开心 挥手 后退",
                        tags=[],
                    ),
                ],
                final_top_k=2,
            )
        )

        self.assertEqual(len(response.results), 2)
        self.assertEqual(response.results[0].asset_id, "asset-1")
        self.assertGreater(response.results[0].rerank_score, response.results[1].rerank_score)
        self.assertIsNone(response.results[0].vector_distance)
        self.assertEqual(response.results[0].combined_score, response.results[0].rerank_score)

    def test_index_routes_dashscope_model_from_request(self) -> None:
        service = SearchService(model_bundle=FakeModelBundle())
        service._dashscope_vectorize = Mock(return_value=np.asarray([0.1, 0.2], dtype=np.float32))

        response = service.vectorize(
            SearchIndexRequest(
                provider="dashscope",
                model="text-embedding-v4",
                asset_id="asset-1",
                asset_name="sample.txt",
                asset_format="文本",
                asset_path=r"D:\Data\sample.txt",
                description="测试描述",
            )
        )

        service._dashscope_vectorize.assert_called_once_with("text-embedding-v4", "测试描述")
        self.assertEqual(response.embedding_model, "text-embedding-v4")

    def test_query_routes_dashscope_model_from_request(self) -> None:
        service = SearchService(model_bundle=FakeModelBundle())
        service._dashscope_rerank = Mock(return_value=[0.8])

        response = service.rerank(
            SearchQueryRequest(
                provider="dashscope",
                model="qwen3-rerank",
                query="测试",
                candidates=[
                    SearchQueryCandidate(
                        asset_id="asset-1",
                        asset_name="sample.txt",
                        asset_format="文本",
                        asset_path=r"D:\Data\sample.txt",
                        description="测试描述",
                    )
                ],
            )
        )

        service._dashscope_rerank.assert_called_once_with("qwen3-rerank", "测试", ["测试描述"])
        self.assertEqual(response.rerank_model, "qwen3-rerank")

    def test_close_model_releases_single_cached_model(self) -> None:
        bundle = FakeModelBundle()
        service = SearchService(model_bundle=bundle)

        response = service.close_model(SearchModelCloseRequest(model_kind="embedding"))

        self.assertEqual(response.model_kind, "embedding")
        self.assertEqual(response.model_name, "fake-embed")
        self.assertTrue(response.closed)
        self.assertTrue(response.cuda_cache_cleared)
        self.assertEqual(response.remaining_loaded_models, ["rerank"])

    def test_get_model_status_reports_loaded_models(self) -> None:
        service = SearchService(model_bundle=FakeModelBundle())

        response = service.get_model_status()

        self.assertIsInstance(response, SearchModelStatusResponse)
        self.assertEqual(response.embedding_model_name, "fake-embed")
        self.assertEqual(response.rerank_model_name, "fake-rerank")
        self.assertEqual(response.device, "cuda")
        self.assertEqual(response.loaded_model_kinds, ["embedding", "rerank"])
        self.assertTrue(response.embedding_loaded)
        self.assertTrue(response.rerank_loaded)
        self.assertEqual(response.loaded_count, 2)


if __name__ == "__main__":
    unittest.main()
