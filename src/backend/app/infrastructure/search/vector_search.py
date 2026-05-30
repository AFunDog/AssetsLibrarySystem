class PlaceholderSearchEngine:
    """
    未来在这里接入：
    - 素材描述向量化
    - 向量索引召回
    - reranker 精排
    - RAG 上下文拼装
    """

    def search(self, query: str) -> list[dict[str, str | float]]:
        return [
            {
                "asset_id": "asset-image-001",
                "name": "示例图片素材",
                "asset_type": "image",
                "summary": f"占位搜索结果：已接收到查询“{query}”。",
                "score": 0.0,
            }
        ]
