from __future__ import annotations

import json
import unittest
from pathlib import Path

import numpy as np

from app.application.services.search_service import SearchService
from app.infrastructure.search.sqlite_vector_repository import AssetVectorInput, IndexState
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

    def test_reindex_rebuilds_index_from_binary_vectors(self) -> None:
        fake_repository = FakeVectorRepository()
        fake_index_manager = FakeIndexManager()
        service = SearchService(
            model_bundle=FakeModelBundle(),
            vector_repository=fake_repository,
            vector_index_manager=fake_index_manager,
        )

        vector_1 = np.asarray([1.0, 0.0, 0.0], dtype=np.float32)
        vector_2 = np.asarray([0.0, 1.0, 0.0], dtype=np.float32)

        response = service.rebuild_index(
            [
                AssetVectorInput(
                    asset_id="asset-1",
                    asset_name="shock.png",
                    asset_format="图片",
                    asset_path=r"D:\Data\shock.png",
                    description="角色受到惊吓后退半步并停顿。",
                    source_store_path=None,
                    generated_at=None,
                    embedding_model="fake-embed",
                    vector=vector_1,
                ),
                AssetVectorInput(
                    asset_id="asset-2",
                    asset_name="happy.png",
                    asset_format="图片",
                    asset_path=r"D:\Data\happy.png",
                    description="角色开心挥手。",
                    source_store_path=None,
                    generated_at=None,
                    embedding_model="fake-embed",
                    vector=vector_2,
                ),
            ]
        )

        self.assertEqual(response.document_count, 2)
        self.assertEqual(response.vector_dim, 3)
        self.assertEqual(fake_repository.cleared, 1)
        self.assertEqual(len(fake_repository.saved_documents), 2)
        self.assertEqual(len(fake_index_manager.rebuild_calls), 1)
        self.assertEqual(fake_index_manager.rebuild_calls[0][0], 2)


class FakeVectorRepository:
    def __init__(self) -> None:
        self.database_path = Path("fake.db")
        self.cleared = 0
        self.saved_documents: list[object] = []

    def replace_documents(self, documents: list[AssetVectorInput]) -> list[object]:
        self.cleared += 1
        self.saved_documents = documents
        return [
            type(
                "IndexedAssetVectorRecord",
                (),
                {"doc_id": index + 1},
            )()
            for index, _ in enumerate(documents)
        ]

    def get_state(self) -> IndexState:
        return IndexState(document_count=len(self.saved_documents), latest_updated_at="2026-06-01T00:00:00+00:00")


class FakeIndexManager:
    def __init__(self) -> None:
        self.index_path = Path("fake.index")
        self.metadata_path = Path("fake.meta.json")
        self.rebuild_calls: list[tuple[int, int]] = []

    def rebuild(self, records: list[object], state: IndexState) -> None:
        self.rebuild_calls.append((len(records), state.document_count))


if __name__ == "__main__":
    unittest.main()
