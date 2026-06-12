# CLAUDE.md — AssetsLibrarySystem (Avalonia)

## 项目简述

Avalonia 桌面的素材管理系统前端，管理文本/图片/视频/音乐四类素材，支持 AI 描述生成和语义搜索。

## 构建与运行

```bash
# 构建全部
cd src/avalonia && dotnet build

# 运行桌面端
dotnet run --project AssetsLibrarySystem.Avalonia

# 运行命令行
dotnet run --project AssetsLibrarySystem.Console -- --help

# 运行测试
dotnet test AssetsLibrarySystem.Application.Tests
```

## 关键文件索引

| 用途 | 路径 |
|---|---|
| 入口点 | `AssetsLibrarySystem.Avalonia/Program.cs` |
| DI 容器 | `AssetsLibrarySystem.Avalonia/App.axaml.cs` |
| 主窗口 VM | `AssetsLibrarySystem.Avalonia/ViewModels/MainWindowViewModel.cs` |
| 核心素材服务 | `AssetsLibrarySystem.Application/Services/AssetLibrary/AssetLibraryService.cs` |
| 搜索服务 | `AssetsLibrarySystem.Application/Services/AssetSearch/AssetSearchService.cs` |
| 描述生成服务 | `AssetsLibrarySystem.Application/Services/AssetDescription/AssetDescriptionService.cs` |
| 后端启动器 | `AssetsLibrarySystem.Application/Services/BackendLauncher/BackendLauncherService.cs` |
| SQLite 数据库 | `AssetsLibrarySystem.Application/Services/Infrastructure/SqliteAssetDatabase.cs` |
| 搜索索引管理 | `AssetsLibrarySystem.Application/Services/AssetSearch/LocalHnswSearchIndexManager.cs` |
| 用例编排 | `AssetsLibrarySystem.Application/UseCases/AssetOperations/` |
| 导航/目录服务 | `AssetsLibrarySystem.Avalonia/Services/Library/` |
| 窗口管理 | `AssetsLibrarySystem.Avalonia/Services/Shell/ShellWindowService.cs` |

## C# 编码风格

- 属性优先于字段（永不暴露 `public string name;`）
- 自定义 get/set 用 `field` 关键字（C# 13），不用显式后备字段
- ViewModel 使用 `[ObservableProperty]` partial property，不手写 `SetProperty`
- 记录/DTO 不用 `[JsonPropertyName]`，靠全局 `JsonNamingPolicy.SnakeCaseLower`
- 简单成员用表达式体 `=>`
- 不可变属性用 `init`
- 必要初始化属性用 `required`
- 中文注释和中文文档

## 常见操作指引

### 添加新页面
1. 在 `Application/` 中添加/修改 Service 或 UseCase
2. 在 `Avalonia/ViewModels/` 中新建 ViewModel（双重构造 + `[ObservableProperty]`）
3. 在 `Avalonia/Views/Pages/` 中新建 View（AXAML + 编译绑定 `x:DataType`）
4. 在 `App.BuildContainer()` 中注册新类型
5. 在 `MainWindowViewModel` 中暴露属性并绑定 TabItem

### 添加 HTTP 端点调用
1. 在相应 Service 中添加 HTTP 请求方法（使用 `HttpClient`）
2. 端点定义参考 `AssetSearchService` 或 `AssetDescriptionService` 中的模式
3. JSON 序列化走全局 `JsonNamingPolicy.SnakeCaseLower`，无需特性标注

### 测试
- xUnit 框架
- Service/UseCase 通过手动 fake 实现解耦
- 参考 `Application.Tests/` 下的现有测试
