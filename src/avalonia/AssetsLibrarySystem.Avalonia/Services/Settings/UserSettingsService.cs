using System;
using System.IO;
using System.Text.Json;
using AssetsLibrarySystem.Application.Infrastructure;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Settings;

public sealed class UserSettingsService : IUserSettingsService
{
    private const string DashScopeProvider = "dashscope";
    private const string LocalProvider = "local";
    private const string DefaultDashScopeEmbeddingModel = "text-embedding-v4";
    private const string DefaultLocalEmbeddingModel = "Qwen/Qwen3-Embedding-0.6B";
    private const string DefaultDashScopeRerankModel = "qwen3-rerank";
    private const string DefaultLocalRerankModel = "Qwen/Qwen3-Reranker-0.6B";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private bool _autoWarmupEmbeddingModel;
    private bool _autoWarmupRerankModel;
    private string _embeddingProvider = DashScopeProvider;
    private string _rerankProvider = DashScopeProvider;
    private ProviderEmbeddingSettings _dashScopeEmbedding = new(DefaultDashScopeEmbeddingModel, 1024);
    private ProviderEmbeddingSettings _localEmbedding = new(DefaultLocalEmbeddingModel, 1024);
    private ProviderRerankSettings _dashScopeRerank = new(DefaultDashScopeRerankModel);
    private ProviderRerankSettings _localRerank = new(DefaultLocalRerankModel);

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
        get => _autoWarmupEmbeddingModel;
        set
        {
            if (_autoWarmupEmbeddingModel == value)
            {
                return;
            }

            _autoWarmupEmbeddingModel = value;
            SaveIfReady();
        }
    }

    public bool AutoWarmupRerankModel
    {
        get => _autoWarmupRerankModel;
        set
        {
            if (_autoWarmupRerankModel == value)
            {
                return;
            }

            _autoWarmupRerankModel = value;
            SaveIfReady();
        }
    }

    public string EmbeddingProvider
    {
        get => _embeddingProvider;
        set
        {
            value = NormalizeProvider(value);
            if (_embeddingProvider == value)
            {
                return;
            }

            _embeddingProvider = value;
            SaveIfReady();
        }
    }

    public string EmbeddingModel
    {
        get => CurrentEmbeddingSettings.Model;
        set
        {
            value = NormalizeModel(value, GetDefaultEmbeddingModel(EmbeddingProvider));
            var settings = CurrentEmbeddingSettings;
            if (settings.Model == value)
            {
                return;
            }

            settings.Model = value;
            SaveIfReady();
        }
    }

    public int EmbeddingDimensions
    {
        get => CurrentEmbeddingSettings.Dimensions;
        set
        {
            value = SearchModelOptions.NormalizeEmbeddingDimensions(value);
            var settings = CurrentEmbeddingSettings;
            if (settings.Dimensions == value)
            {
                return;
            }

            settings.Dimensions = value;
            SaveIfReady();
        }
    }

    public string RerankProvider
    {
        get => _rerankProvider;
        set
        {
            value = NormalizeProvider(value);
            if (_rerankProvider == value)
            {
                return;
            }

            _rerankProvider = value;
            SaveIfReady();
        }
    }

    public string RerankModel
    {
        get => CurrentRerankSettings.Model;
        set
        {
            value = NormalizeModel(value, GetDefaultRerankModel(RerankProvider));
            var settings = CurrentRerankSettings;
            if (settings.Model == value)
            {
                return;
            }

            settings.Model = value;
            SaveIfReady();
        }
    }

    public SearchModelOptions Current => new(EmbeddingProvider, EmbeddingModel, EmbeddingDimensions, RerankProvider, RerankModel);

    private ProviderEmbeddingSettings CurrentEmbeddingSettings =>
        EmbeddingProvider == LocalProvider ? _localEmbedding : _dashScopeEmbedding;

    private ProviderRerankSettings CurrentRerankSettings =>
        RerankProvider == LocalProvider ? _localRerank : _dashScopeRerank;

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

            _dashScopeEmbedding = NormalizeEmbeddingSettings(
                snapshot.DashScopeEmbedding,
                DefaultDashScopeEmbeddingModel,
                snapshot.EmbeddingProvider == DashScopeProvider ? snapshot.EmbeddingModel : null,
                snapshot.EmbeddingProvider == DashScopeProvider ? snapshot.EmbeddingDimensions : null);
            _localEmbedding = NormalizeEmbeddingSettings(
                snapshot.LocalEmbedding,
                DefaultLocalEmbeddingModel,
                snapshot.EmbeddingProvider == LocalProvider ? snapshot.EmbeddingModel : null,
                snapshot.EmbeddingProvider == LocalProvider ? snapshot.EmbeddingDimensions : null);

            _dashScopeRerank = NormalizeRerankSettings(
                snapshot.DashScopeRerank,
                DefaultDashScopeRerankModel,
                snapshot.RerankProvider == DashScopeProvider ? snapshot.RerankModel : null);
            _localRerank = NormalizeRerankSettings(
                snapshot.LocalRerank,
                DefaultLocalRerankModel,
                snapshot.RerankProvider == LocalProvider ? snapshot.RerankModel : null);

            EmbeddingProvider = snapshot.EmbeddingProvider;
            RerankProvider = snapshot.RerankProvider;

            Log.Debug(
                "用户设置已加载: settingsPath={SettingsPath}, autoWarmupEmbeddingModel={AutoWarmupEmbeddingModel}, autoWarmupRerankModel={AutoWarmupRerankModel}, embeddingProvider={EmbeddingProvider}, embeddingModel={EmbeddingModel}, embeddingDimensions={EmbeddingDimensions}, rerankProvider={RerankProvider}, rerankModel={RerankModel}",
                SettingsPath,
                AutoWarmupEmbeddingModel,
                AutoWarmupRerankModel,
                EmbeddingProvider,
                EmbeddingModel,
                EmbeddingDimensions,
                RerankProvider,
                RerankModel);
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
                EmbeddingDimensions = EmbeddingDimensions,
                RerankProvider = RerankProvider,
                RerankModel = RerankModel,
                DashScopeEmbedding = _dashScopeEmbedding.Clone(),
                LocalEmbedding = _localEmbedding.Clone(),
                DashScopeRerank = _dashScopeRerank.Clone(),
                LocalRerank = _localRerank.Clone(),
            };

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(SettingsPath, json);
            Log.Information(
                "用户设置已保存: settingsPath={SettingsPath}, autoWarmupEmbeddingModel={AutoWarmupEmbeddingModel}, autoWarmupRerankModel={AutoWarmupRerankModel}, embeddingProvider={EmbeddingProvider}, embeddingModel={EmbeddingModel}, embeddingDimensions={EmbeddingDimensions}, rerankProvider={RerankProvider}, rerankModel={RerankModel}",
                SettingsPath,
                AutoWarmupEmbeddingModel,
                AutoWarmupRerankModel,
                EmbeddingProvider,
                EmbeddingModel,
                EmbeddingDimensions,
                RerankProvider,
                RerankModel);
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

        public string EmbeddingProvider { get; set; } = DashScopeProvider;

        public string EmbeddingModel { get; set; } = DefaultDashScopeEmbeddingModel;

        public int EmbeddingDimensions { get; set; } = 1024;

        public string RerankProvider { get; set; } = DashScopeProvider;

        public string RerankModel { get; set; } = DefaultDashScopeRerankModel;

        public ProviderEmbeddingSettings? DashScopeEmbedding { get; set; }

        public ProviderEmbeddingSettings? LocalEmbedding { get; set; }

        public ProviderRerankSettings? DashScopeRerank { get; set; }

        public ProviderRerankSettings? LocalRerank { get; set; }
    }

    private sealed class ProviderEmbeddingSettings
    {
        public ProviderEmbeddingSettings()
        {
        }

        public ProviderEmbeddingSettings(string model, int dimensions)
        {
            Model = model;
            Dimensions = SearchModelOptions.NormalizeEmbeddingDimensions(dimensions);
        }

        public string Model { get; set; } = "";

        public int Dimensions { get; set; } = 1024;

        public ProviderEmbeddingSettings Clone() => new(Model, Dimensions);
    }

    private sealed class ProviderRerankSettings
    {
        public ProviderRerankSettings()
        {
        }

        public ProviderRerankSettings(string model)
        {
            Model = model;
        }

        public string Model { get; set; } = "";

        public ProviderRerankSettings Clone() => new(Model);
    }

    private static ProviderEmbeddingSettings NormalizeEmbeddingSettings(
        ProviderEmbeddingSettings? settings,
        string defaultModel,
        string? legacyModel,
        int? legacyDimensions)
    {
        var model = NormalizeModel(settings?.Model, NormalizeModel(legacyModel, defaultModel));
        var dimensions = SearchModelOptions.NormalizeEmbeddingDimensions(settings?.Dimensions ?? legacyDimensions);
        return new ProviderEmbeddingSettings(model, dimensions);
    }

    private static ProviderRerankSettings NormalizeRerankSettings(
        ProviderRerankSettings? settings,
        string defaultModel,
        string? legacyModel)
    {
        return new ProviderRerankSettings(NormalizeModel(settings?.Model, NormalizeModel(legacyModel, defaultModel)));
    }

    private static string NormalizeProvider(string? value) =>
        string.Equals(value?.Trim(), LocalProvider, StringComparison.OrdinalIgnoreCase) ? LocalProvider : DashScopeProvider;

    private static string NormalizeModel(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string GetDefaultEmbeddingModel(string provider) =>
        NormalizeProvider(provider) == LocalProvider ? DefaultLocalEmbeddingModel : DefaultDashScopeEmbeddingModel;

    private static string GetDefaultRerankModel(string provider) =>
        NormalizeProvider(provider) == LocalProvider ? DefaultLocalRerankModel : DefaultDashScopeRerankModel;
}
