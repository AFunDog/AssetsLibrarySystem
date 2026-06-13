using System;
using System.IO;
using System.Text.Json;
using AssetsLibrarySystem.Application.Infrastructure;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Settings;

public sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private bool IsLoading { get; set; } = true;

    public UserSettingsService()
        : this(CreateFallbackSettingsPath())
    {
    }

    public UserSettingsService(IConfiguration configuration)
        : this(CreateRuntimeSettingsPath(configuration))
    {
    }

    private UserSettingsService(string settingsPath)
    {
        SettingsPath = settingsPath;
        Load();
        IsLoading = false;
    }

    public string SettingsPath { get; }

    public bool AutoWarmupEmbeddingModel
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            SaveIfReady();
        }
    }

    public bool AutoWarmupRerankModel
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            SaveIfReady();
        }
    }

    public string EmbeddingProvider
    {
        get => field;
        set
        {
            value = NormalizeProvider(value);
            if (field == value) return;
            field = value;
            SaveIfReady();
        }
    } = "dashscope";

    public string EmbeddingModel
    {
        get => field;
        set
        {
            value = NormalizeModel(value, EmbeddingProvider == "local" ? "Qwen/Qwen3-Embedding-0.6B" : "text-embedding-v4");
            if (field == value) return;
            field = value;
            SaveIfReady();
        }
    } = "text-embedding-v4";

    public string RerankProvider
    {
        get => field;
        set
        {
            value = NormalizeProvider(value);
            if (field == value) return;
            field = value;
            SaveIfReady();
        }
    } = "dashscope";

    public string RerankModel
    {
        get => field;
        set
        {
            value = NormalizeModel(value, RerankProvider == "local" ? "Qwen/Qwen3-Reranker-0.6B" : "qwen3-rerank");
            if (field == value) return;
            field = value;
            SaveIfReady();
        }
    } = "qwen3-rerank";

    public SearchModelOptions Current => new(EmbeddingProvider, EmbeddingModel, RerankProvider, RerankModel);

    private static string CreateFallbackSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "data", "user-settings.json");
    }

    private static string CreateRuntimeSettingsPath(IConfiguration configuration)
    {
        var dataRoot = configuration["Runtime:DataRoot"];
        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            return Path.Combine(Path.GetFullPath(dataRoot), "user-settings.json");
        }

        return CreateFallbackSettingsPath();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                Log.Debug("用户设置文件不存在，使用默认值: settingsPath={SettingsPath}", SettingsPath);
                return;
            }

            var json = File.ReadAllText(SettingsPath);
            var snapshot = JsonSerializer.Deserialize<UserSettingsSnapshot>(json, JsonOptions);
            if (snapshot is null)
            {
                Log.Warning("用户设置文件为空或无效，使用默认值: settingsPath={SettingsPath}", SettingsPath);
                return;
            }

            AutoWarmupEmbeddingModel = snapshot.AutoWarmupEmbeddingModel;
            AutoWarmupRerankModel = snapshot.AutoWarmupRerankModel;
            EmbeddingProvider = snapshot.EmbeddingProvider;
            EmbeddingModel = snapshot.EmbeddingModel;
            RerankProvider = snapshot.RerankProvider;
            RerankModel = snapshot.RerankModel;
            Log.Debug(
                "用户设置已加载: settingsPath={SettingsPath}, autoWarmupEmbeddingModel={AutoWarmupEmbeddingModel}, autoWarmupRerankModel={AutoWarmupRerankModel}",
                SettingsPath,
                AutoWarmupEmbeddingModel,
                AutoWarmupRerankModel);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载用户设置失败，使用默认值: settingsPath={SettingsPath}", SettingsPath);
        }
    }

    private void SaveIfReady()
    {
        if (IsLoading)
        {
            return;
        }

        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);

            var snapshot = new UserSettingsSnapshot
            {
                AutoWarmupEmbeddingModel = AutoWarmupEmbeddingModel,
                AutoWarmupRerankModel = AutoWarmupRerankModel,
                EmbeddingProvider = EmbeddingProvider,
                EmbeddingModel = EmbeddingModel,
                RerankProvider = RerankProvider,
                RerankModel = RerankModel,
            };

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            Log.Information(
                "用户设置已保存: settingsPath={SettingsPath}, autoWarmupEmbeddingModel={AutoWarmupEmbeddingModel}, autoWarmupRerankModel={AutoWarmupRerankModel}",
                SettingsPath,
                AutoWarmupEmbeddingModel,
                AutoWarmupRerankModel);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存用户设置失败: settingsPath={SettingsPath}", SettingsPath);
        }
    }

    private sealed class UserSettingsSnapshot
    {
        public bool AutoWarmupEmbeddingModel { get; set; }

        public bool AutoWarmupRerankModel { get; set; }

        public string EmbeddingProvider { get; set; } = "dashscope";

        public string EmbeddingModel { get; set; } = "text-embedding-v4";

        public string RerankProvider { get; set; } = "dashscope";

        public string RerankModel { get; set; } = "qwen3-rerank";
    }

    private static string NormalizeProvider(string? value) =>
        string.Equals(value?.Trim(), "local", StringComparison.OrdinalIgnoreCase) ? "local" : "dashscope";

    private static string NormalizeModel(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
