from __future__ import annotations

import json
from pathlib import Path

import numpy as np

from app.infrastructure.search.sqlite_vector_repository import IndexState, IndexedAssetVectorRecord


class HnswIndexManager:
    def __init__(self, index_path: str | Path, metadata_path: str | Path) -> None:
        self.index_path = Path(index_path)
        self.metadata_path = Path(metadata_path)
        self.index_path.parent.mkdir(parents=True, exist_ok=True)
        self.metadata_path.parent.mkdir(parents=True, exist_ok=True)

        self._index = None
        self._dim: int | None = None

    def ensure_current(self, records: list[IndexedAssetVectorRecord], state: IndexState) -> None:
        if not records:
            raise ValueError("当前没有可检索的素材描述。")

        if self._is_current(state):
            self._load()
            return

        self._build(records)
        self._save(state)

    def search(self, query_vector: np.ndarray, top_k: int) -> list[tuple[int, float]]:
        if self._index is None:
            raise RuntimeError("向量索引尚未加载。")

        labels, distances = self._index.knn_query(np.asarray(query_vector, dtype=np.float32), k=top_k)
        return [
            (int(doc_id), 1.0 - float(distance))
            for doc_id, distance in zip(labels[0], distances[0], strict=True)
        ]

    def _build(self, records: list[IndexedAssetVectorRecord]) -> None:
        hnswlib = self._import_hnswlib()

        vectors = np.vstack([record.vector for record in records]).astype(np.float32)
        labels = np.asarray([record.doc_id for record in records], dtype=np.int64)

        self._dim = int(vectors.shape[1])
        index = hnswlib.Index(space="cosine", dim=self._dim)
        index.init_index(max_elements=len(records), ef_construction=200, M=16)
        index.add_items(vectors, labels)
        index.set_ef(max(50, min(len(records), 100)))
        self._index = index

    def _save(self, state: IndexState) -> None:
        if self._index is None or self._dim is None:
            raise RuntimeError("向量索引尚未构建。")

        self._index.save_index(str(self.index_path))
        self.metadata_path.write_text(
            json.dumps(
                {
                    "dim": self._dim,
                    "document_count": state.document_count,
                    "latest_updated_at": state.latest_updated_at,
                },
                ensure_ascii=False,
                indent=2,
            ),
            encoding="utf-8",
        )

    def _load(self) -> None:
        if not self.index_path.exists() or not self.metadata_path.exists():
            raise FileNotFoundError("向量索引文件不存在。")

        metadata = json.loads(self.metadata_path.read_text(encoding="utf-8"))
        dim = int(metadata["dim"])

        hnswlib = self._import_hnswlib()
        index = hnswlib.Index(space="cosine", dim=dim)
        index.load_index(str(self.index_path))
        index.set_ef(max(50, min(int(metadata["document_count"]), 100)))

        self._dim = dim
        self._index = index

    def _is_current(self, state: IndexState) -> bool:
        if not self.index_path.exists() or not self.metadata_path.exists():
            return False

        metadata = json.loads(self.metadata_path.read_text(encoding="utf-8"))
        return (
            int(metadata.get("document_count", -1)) == state.document_count
            and str(metadata.get("latest_updated_at", "")) == state.latest_updated_at
        )

    @staticmethod
    def _import_hnswlib():
        try:
            import hnswlib
        except ImportError as exc:  # pragma: no cover - import failure path
            raise RuntimeError("检索功能需要安装 `hnswlib`。") from exc
        return hnswlib
