# Assets Library System Backend

这是 `Assets Library System` 的 Python 模型网关。

当前后端只承担一类职责：

- 暴露大模型 HTTP 接口
- 提供健康检查与能力清单
- 作为 Avalonia/.NET 桌面端的模型调用出口

当前后端不再承担：

- 素材库登记
- 文件扫描
- 素材元数据管理
- 本地目录浏览

## 当前接口

- `GET /health`
- `GET /api/v1/model/capabilities`
- `POST /api/v1/model/generate`

## 本地启动

```powershell
cd src/backend
copy configs\providers.example.yaml configs\providers.yaml
pip install -e .
uvicorn app.main:app --reload
```

如果未配置真实 API Key，`/api/v1/model/generate` 会返回占位响应，便于先和桌面端联调。
