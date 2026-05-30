from app.domain.models.asset import Asset


class InMemoryAssetRepository:
    """当前阶段只保留内存假数据，后续替换为文件系统和数据库实现。"""

    def list_assets(self) -> list[Asset]:
        return [
            Asset(
                id="asset-text-001",
                name="示例文本素材",
                asset_type="text",
                path="library/text/demo.txt",
                description="用于演示文本素材在列表页中的展示位置。",
                tags=["示例", "文本"],
                status="placeholder",
            ),
            Asset(
                id="asset-image-001",
                name="示例图片素材",
                asset_type="image",
                path="library/images/demo.png",
                description="用于演示图片素材在统一素材模型中的表示。",
                tags=["示例", "图片"],
                status="placeholder",
            ),
        ]
