from __future__ import annotations

import unittest

import numpy as np

from app.application.services.search_service import SearchService
from app.schemas.search import SearchIndexRequest, SearchQueryCandidate, SearchQueryRequest


class FakeModelBundle:
    embedding_model_name = "fake-embed"
    rerank_model_name = "fake-rerank"

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

    @staticmethod
    def _vector_for_text(text: str) -> list[float]:
        if "惊吓" in text or "惊慌" in text or "后退" in text:
            return [1.0, 0.0, 0.0]
        if "开心" in text:
            return [0.0, 1.0, 0.0]
        return [0.0, 0.0, 1.0]


class SearchServiceTestCase(unittest.TestCase):
    def test_index_returns_vector_only(self) -> None:
        service = SearchService(model_bundle=FakeModelBundle())

        response = service.vectorize(
            SearchIndexRequest(
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
                query="惊吓 后退",
                candidates=[
                    SearchQueryCandidate(
                        asset_id="asset-1",
                        asset_name="shock.png",
                        asset_format="图片",
                        asset_path=r"D:\Data\shock.png",
                        description="惊吓 后退 停顿",
                    ),
                    SearchQueryCandidate(
                        asset_id="asset-2",
                        asset_name="happy.png",
                        asset_format="图片",
                        asset_path=r"D:\Data\happy.png",
                        description="开心 挥手 后退",
                    ),
                ],
                final_top_k=2,
            )
        )

        self.assertEqual(len(response.results), 2)
        self.assertEqual(response.results[0].asset_id, "asset-1")
        self.assertGreater(response.results[0].rerank_score, response.results[1].rerank_score)


if __name__ == "__main__":
    unittest.main()
