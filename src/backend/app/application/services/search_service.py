from app.infrastructure.search.vector_search import PlaceholderSearchEngine
from app.schemas.search import SearchRequest, SearchResponse, SearchResultItem


class SearchService:
    """自然语言搜索与 RAG 编排层占位实现。"""

    def __init__(self) -> None:
        self.search_engine = PlaceholderSearchEngine()

    def search(self, payload: SearchRequest) -> SearchResponse:
        items = [
            SearchResultItem(
                asset_id=result["asset_id"],
                name=result["name"],
                asset_type=result["asset_type"],
                summary=result["summary"],
                score=result["score"],
            )
            for result in self.search_engine.search(payload.query)
        ]
        return SearchResponse(
            query=payload.query,
            items=items,
            answer="当前为占位回答，后续会在这里接入 RAG 生成结果。",
            stage="skeleton",
        )
