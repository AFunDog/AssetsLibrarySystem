# 素材详情面板实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 在素材详情面板中展示子类型和按角度拆分的描述

**Architecture:** C# 端：新增 AngleDescriptionRecord 模型，LibraryCatalogService 解析 JSON 为角度列表，AXAML 重新设计详情面板

**Tech Stack:** Avalonia UI, CommunityToolkit.Mvvm, SQLite

---

## 文件结构

| 文件 | 操作 | 职责 |
|------|------|------|
| `Models/AngleDescriptionRecord.cs` | 新建 | UI 展示用的角度记录 |
| `Services/Library/LibraryCatalogService.cs` | 修改 | 新增子类型 + 角度解析 |
| `ViewModels/AssetDetailViewModel.cs` | 修改 | 透传新属性 |
| `Views/Pages/LibraryPage.axaml` | 修改 | 重新设计详情面板 |
| `Services/Infrastructure/SqliteAssetDatabase.cs` | 修改 | 新增 UpdateSubtypeAsync |
| `Tests/...` | 新建 | 角度解析测试 |

---

### Task 1: 创建 AngleDescriptionRecord 模型

**Files:**
- Create: `src/avalonia/AssetsLibrarySystem.Application/Models/AngleDescriptionRecord.cs`

- [ ] **Step 1: 创建文件**

```csharp
namespace AssetsLibrarySystem.Application.Models;

public sealed record AngleDescriptionRecord(
    string AngleKey,
    string Label,
    string Text,
    string[] Tags,
    int MaxLength)
{
    public string TagsDisplay => string.Join("、", Tags);
}
```

- [ ] **Step 2: Commit**

```bash
git add src/avalonia/AssetsLibrarySystem.Application/Models/AngleDescriptionRecord.cs
git commit -m "feat(ui): 新增 AngleDescriptionRecord 模型"
```

---

### Task 2: LibraryCatalogService 新增子类型 + 角度解析

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Services/Library/LibraryCatalogService.cs`

- [ ] **Step 1: 新增属性**

```csharp
public partial string SelectedAssetSubtype { get; set; } = "";
public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles { get; } = new();
```

- [ ] **Step 2: 在 LoadAssetDescription 方法中添加角度解析**

```csharp
private void RefreshDescriptionAngles(ManagedAssetRecord? asset)
{
    SelectedAssetDescriptionAngles.Clear();
    if (asset is null) return;

    // 获取子类型
    var subtype = asset.Subtype;
    if (string.IsNullOrWhiteSpace(subtype))
    {
        var detector = new SubtypeDetector();
        subtype = detector.DetectSubtype(asset) ?? "默认";
    }
    SelectedAssetSubtype = subtype;

    // 获取描述
    var description = AssetDescriptionStore?.TryGetForAssetAsync(asset).GetAwaiter().GetResult();
    if (description is null) return;

    // 解析角度
    var segments = StructuredDescriptionHelper.ExtractSegments(description.Description);
    var profile = new AngleProfileManager(/* 需要 YAML 路径 */).GetProfile(asset.AssetType, subtype);

    foreach (var segment in segments)
    {
        var angleDef = profile.Angles.FirstOrDefault(a => a.Key == segment.NormalizedAngleType);
        SelectedAssetDescriptionAngles.Add(new AngleDescriptionRecord(
            AngleKey: segment.NormalizedAngleType,
            Label: angleDef?.Label ?? segment.NormalizedAngleType,
            Text: segment.NormalizedText,
            Tags: [],  // 从 JSON 解析 tags
            MaxLength: angleDef?.MaxLength ?? 120));
    }
}
```

- [ ] **Step 3: 添加子类型更新方法**

```csharp
public async Task UpdateAssetSubtypeAsync(string newSubtype)
{
    if (SelectedAsset is null) return;
    await _assetDatabase.UpdateSubtypeAsync(SelectedAsset.DatabaseId, newSubtype);
    SelectedAssetSubtype = newSubtype;
    // 重新解析角度
    RefreshDescriptionAngles(SelectedAsset);
}
```

- [ ] **Step 4: 构建测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia
dotnet build -c Debug
```

- [ ] **Step 5: Commit**

---

### Task 3: AssetDetailViewModel 透传新属性

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/ViewModels/AssetDetailViewModel.cs`

- [ ] **Step 1: 添加属性**

```csharp
public string SelectedAssetSubtype => LibraryCatalogService.SelectedAssetSubtype;
public ObservableCollection<AngleDescriptionRecord> SelectedAssetDescriptionAngles
    => LibraryCatalogService.SelectedAssetDescriptionAngles;
```

- [ ] **Step 2: Commit**

---

### Task 4: 重新设计 LibraryPage.axaml 详情面板

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Avalonia/Views/Pages/LibraryPage.axaml`

- [ ] **Step 1: 替换详情面板布局**

找到当前详情面板的 Border/Grid，替换为：

```xml
<!-- 详情面板 -->
<Border Classes="page-card" Grid.Row="2" IsVisible="{Binding IsAssetSelected}">
  <Grid ColumnDefinitions="200,*" ColumnSpacing="12">
    
    <!-- 左侧：基本信息 -->
    <Border Classes="sub-card">
      <StackPanel Spacing="8">
        <StackPanel>
          <TextBlock Classes="eyebrow" Text="类型" />
          <TextBlock Text="{Binding SelectedAssetType}" FontSize="24" FontWeight="SemiLight" />
        </StackPanel>
        
        <StackPanel>
          <TextBlock Classes="eyebrow" Text="子类型" />
          <Grid ColumnDefinitions="*,Auto">
            <TextBlock Text="{Binding SelectedAssetSubtype}" VerticalAlignment="Center" />
            <Button Content="✏️" Classes="secondary-action" Padding="4"
                    Command="{Binding EditSubtypeCommand}" Grid.Column="1" />
          </Grid>
        </StackPanel>
        
        <StackPanel>
          <TextBlock Classes="eyebrow" Text="时长" />
          <TextBlock Text="{Binding SelectedAssetDuration}" />
        </StackPanel>
        
        <StackPanel>
          <TextBlock Classes="eyebrow" Text="状态" />
          <TextBlock Text="{Binding SelectedAssetAiState}" />
        </StackPanel>
        
        <Button Content="描述" Classes="primary-action" />
        <Button Content="向量化" Classes="secondary-action" />
      </StackPanel>
    </Border>
    
    <!-- 右侧：角度描述列表 -->
    <ScrollViewer Grid.Column="1">
      <ItemsControl ItemsSource="{Binding SelectedAssetDescriptionAngles}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Classes="sub-card" Margin="0,0,0,8">
              <StackPanel Spacing="6">
                <Grid ColumnDefinitions="*,Auto">
                  <TextBlock Text="{Binding Label}" FontWeight="SemiBold" FontSize="14" />
                  <TextBlock Text="{Binding MaxLength, StringFormat='{0} 字'}"
                             Classes="eyebrow" Grid.Column="1" />
                </Grid>
                <TextBlock Text="{Binding Text}" TextWrapping="Wrap" FontSize="13"
                           Foreground="{DynamicResource AppTextBrush}" />
                <WrapPanel Spacing="4">
                  <ItemsControl ItemsSource="{Binding Tags}">
                    <ItemsControl.ItemsPanel>
                      <ItemsPanelTemplate>
                        <WrapPanel Spacing="4" />
                      </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>
                    <ItemsControl.ItemTemplate>
                      <DataTemplate>
                        <Border Background="{DynamicResource AppSoftSurfaceBrush}"
                                CornerRadius="4" Padding="8,4">
                          <TextBlock Text="{Binding}" FontSize="12" />
                        </Border>
                      </DataTemplate>
                    </ItemsControl.ItemTemplate>
                  </ItemsControl>
                </WrapPanel>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </ScrollViewer>
  </Grid>
</Border>
```

- [ ] **Step 2: 构建验证**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia
dotnet build -c Debug
```

- [ ] **Step 3: Commit**

---

### Task 5: 数据库新增 UpdateSubtypeAsync

**Files:**
- Modify: `src/avalonia/AssetsLibrarySystem.Application/Services/Infrastructure/SqliteAssetDatabase.cs`

- [ ] **Step 1: 添加方法**

```csharp
public async Task UpdateSubtypeAsync(long assetId, string subtype, CancellationToken ct = default)
{
    await using var connection = await CreateConnectionAsync(ct);
    await using var command = connection.CreateCommand();
    command.CommandText = "UPDATE asset_metadata SET subtype = $subtype WHERE asset_id = $assetId";
    command.Parameters.AddWithValue("$subtype", subtype);
    command.Parameters.AddWithValue("$assetId", assetId);
    await command.ExecuteNonQueryAsync(ct);
}
```

- [ ] **Step 2: 在 IAssetDatabase 接口中添加声明**

```csharp
Task UpdateSubtypeAsync(long assetId, string subtype, CancellationToken ct = default);
```

- [ ] **Step 3: 构建测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia
dotnet build -c Debug
```

- [ ] **Step 4: Commit**

---

### Task 6: 运行全部测试

- [ ] **Step 1: 运行所有测试**

```bash
cd /d/GitRepository/AssetsLibrarySystem/src/avalonia
dotnet test -c Debug
```

- [ ] **Step 2: 最终提交**

```bash
git add -A
git commit -m "feat(ui): 素材详情面板支持子类型和角度描述查看"
```