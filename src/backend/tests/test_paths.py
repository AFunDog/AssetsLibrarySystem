"""路径解析单元测试"""

from __future__ import annotations

import os
from pathlib import Path
import unittest
from unittest.mock import patch

from app.core import config, paths

# 固定仓库根：tests -> src/backend/tests，parents[3] 为仓库根，
# 避免依赖运行时的 cwd（此前用 Path.cwd().parents[1] 在任意目录运行会算错）
REPO_ROOT = Path(__file__).resolve().parents[3]


class PathsTestCase(unittest.TestCase):
    def setUp(self) -> None:
        config.get_settings.cache_clear()
        paths.resolve_data_root.cache_clear()

    def tearDown(self) -> None:
        config.get_settings.cache_clear()
        paths.resolve_data_root.cache_clear()

    def test_resolve_data_root_prefers_data_root_environment_variable(self) -> None:
        configured = Path(r"D:\GitRepository\AssetsLibrarySystem\data\custom-data")

        with patch.dict(os.environ, {"DATA_ROOT": str(configured), "APP_ENV": "dev"}, clear=True):
            resolved = paths.resolve_data_root()

        self.assertEqual(resolved, configured.resolve())

    def test_resolve_data_root_uses_repo_data_in_dev_mode(self) -> None:
        with patch.dict(os.environ, {"APP_ENV": "dev"}, clear=True):
            resolved = paths.resolve_data_root()

        self.assertEqual(resolved, (REPO_ROOT / "data").resolve())

    def test_ensure_shared_data_dir_creates_directory(self) -> None:
        configured = Path(r"D:\GitRepository\AssetsLibrarySystem\data\shared-data")

        with patch.dict(os.environ, {"DATA_ROOT": str(configured), "APP_ENV": "dev"}, clear=True):
            with patch.object(Path, "mkdir") as mock_mkdir:
                data_dir = paths.ensure_shared_data_dir()

        mock_mkdir.assert_called_once_with(parents=True, exist_ok=True)
        self.assertEqual(data_dir, configured.resolve())


if __name__ == "__main__":
    unittest.main()
