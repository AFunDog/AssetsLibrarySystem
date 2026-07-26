from __future__ import annotations

from unittest.mock import patch, MagicMock

import pytest

from app.application.services.search_service import SearchService
from app.schemas.search import (
    SearchIndexRequest,
    SearchQueryRequest,
    SearchQueryCandidate,
)


@pytest.fixture
def service() -> SearchService:
    return SearchService()


class TestSearchServiceDashScope:
    def test_index_routes_dashscope_model_from_request(self, service: SearchService):
        payload = SearchIndexRequest(
            provider="dashscope",
            model="text-embedding-v4",
            asset_id="test-001",
            asset_name="test.png",
            asset_format="图片",
            asset_path="C:\\test.png",
            description="测试描述",
        )

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.output = {
            "embeddings": [{"embedding": [0.1, 0.2, 0.3]}]
        }
        mock_response.usage = {"total_tokens": 42}

        with patch("dashscope.TextEmbedding.call", return_value=mock_response):
            result = service.vectorize(payload)

        assert result.asset_id == "test-001"
        assert result.embedding_model == "text-embedding-v4"
        assert result.vector_dim == 3
        assert result.token_usage == 42

    def test_index_routes_dashscope_embedding_dimensions_from_request(self, service: SearchService):
        payload = SearchIndexRequest(
            provider="dashscope",
            model="text-embedding-v4",
            embedding_dimensions=1024,
            asset_id="test-001",
            asset_name="test.png",
            asset_format="图片",
            asset_path="C:\\test.png",
            description="测试描述",
        )

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.output = {
            "embeddings": [{"embedding": [0.1] * 1024}]
        }
        mock_response.usage = {"total_tokens": 42}

        with patch("dashscope.TextEmbedding.call", return_value=mock_response) as mock_call:
            result = service.vectorize(payload)

            assert mock_call.call_count == 1
            _, kwargs = mock_call.call_args
            assert kwargs.get("dimension") == 1024

        assert result.embedding_model == "text-embedding-v4@1024d"
        assert result.vector_dim == 1024

    def test_query_routes_dashscope_model_from_request(self, service: SearchService):
        candidates = [
            SearchQueryCandidate(
                candidate_id="cand-1",
                asset_id="test-001",
                asset_name="test1.png",
                asset_format="图片",
                asset_path="C:\\test1.png",
                description="描述一",
            ),
            SearchQueryCandidate(
                candidate_id="cand-2",
                asset_id="test-002",
                asset_name="test2.png",
                asset_format="图片",
                asset_path="C:\\test2.png",
                description="描述二",
            ),
        ]
        payload = SearchQueryRequest(
            provider="dashscope",
            model="qwen3-rerank",
            query="测试查询",
            candidates=candidates,
            final_top_k=2,
        )

        mock_response = MagicMock()
        mock_response.status_code = 200
        mock_response.output = {
            "results": [
                {"index": 0, "relevance_score": 0.95},
                {"index": 1, "relevance_score": 0.50},
            ]
        }
        mock_response.usage = {"total_tokens": 88}

        with patch("dashscope.TextReRank.call", return_value=mock_response):
            result = service.rerank(payload)

        assert result.final_top_k == 2
        assert result.rerank_model == "qwen3-rerank"
        assert len(result.results) == 2
        assert result.results[0].rerank_score == 0.95
        assert result.results[1].rerank_score == 0.50
        assert result.token_usage == 88