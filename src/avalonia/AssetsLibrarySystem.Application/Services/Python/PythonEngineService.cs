using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AssetsLibrarySystem.Application.Services.BackendLauncher;
using Python.Runtime;
using Serilog;

namespace AssetsLibrarySystem.Application.Services.Python;

public sealed class PythonEngineService : IBackendLauncher, IDisposable
{
    private IntPtr _threadState;
    private bool _isInitialized;
    private readonly object _lock = new();

    public string PythonHome { get; }
    public string PythonDll { get; }
    public string BackendSourcePath { get; }

    public bool IsRunning => _isInitialized;
    public string BaseUrl => "in-process";

    public PythonEngineService(string backendSourcePath, string? pythonHome = null, string? pythonDll = null)
    {
        BackendSourcePath = backendSourcePath;
        PythonHome = pythonHome ?? ResolveDefaultPythonHome();
        PythonDll = pythonDll ?? ResolveDefaultPythonDll(PythonHome);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        Initialize();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    public void Initialize()
    {
        lock (_lock)
        {
            if (_isInitialized)
                return;

            if (!File.Exists(PythonDll))
                throw new InvalidOperationException($"Python DLL 不存在: {PythonDll}");

            if (!Directory.Exists(BackendSourcePath))
                throw new InvalidOperationException($"Python 后端源码目录不存在: {BackendSourcePath}");

            Log.Information(
                "PythonEngine 初始化: pythonDll={PythonDll}, pythonHome={PythonHome}, backendSource={BackendSource}",
                PythonDll, PythonHome, BackendSourcePath);

            // 设置环境变量
            SetupEnvironment();

            // 设置 Python.NET 运行时
            Runtime.PythonDLL = PythonDll;
            PythonEngine.PythonHome = ResolveBasePython(PythonHome);
            PythonEngine.PythonPath = BuildPythonPath();

            PythonEngine.Initialize();
            _threadState = PythonEngine.BeginAllowThreads();
            _isInitialized = true;

            // 导入 Python 后端模块
            ImportBackendModules();

            Log.Information("PythonEngine 初始化完成");
        }
    }

    public void Execute(Action action)
    {
        EnsureInitialized();
        using (Py.GIL())
        {
            action();
        }
    }

    public T Execute<T>(Func<T> func)
    {
        EnsureInitialized();
        using (Py.GIL())
        {
            return func();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_isInitialized)
                return;

            Log.Information("PythonEngine 开始关闭");
            try
            {
                PythonEngine.EndAllowThreads(_threadState);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PythonEngine EndAllowThreads 异常");
            }

            try
            {
                // PythonEngine.Shutdown() 在某些环境下可能挂起或崩溃。
                // 使用超时任务避免阻塞进程退出。
                var shutdownTask = Task.Run(() => PythonEngine.Shutdown());
                if (!shutdownTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    Log.Warning("PythonEngine.Shutdown 超时（5s），跳过关闭");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PythonEngine.Shutdown 异常（已忽略）");
            }

            _isInitialized = false;
            Log.Information("PythonEngine 关闭处理完成");
        }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("PythonEngine 尚未初始化，请先调用 Initialize()。");
    }

    private void SetupEnvironment()
    {
        var basePython = ResolveBasePython(PythonHome);

        // 将 .venv\Scripts 加入 PATH 确保 Python DLL 可加载
        var scriptsPath = Path.Combine(PythonHome, "Scripts");
        // 将 base Python 目录和 DLLs 目录加入 PATH 确保 C 扩展模块可加载
        var basePythonDir = basePython;
        var baseDllsDir = Path.Combine(basePython, "DLLs");

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathEntries = new[] { scriptsPath, basePythonDir, baseDllsDir };
        foreach (var entry in pathEntries)
        {
            if (!currentPath.Contains(entry, StringComparison.OrdinalIgnoreCase))
            {
                currentPath = $"{entry};{currentPath}";
            }
        }
        Environment.SetEnvironmentVariable("PATH", currentPath, EnvironmentVariableTarget.Process);

        // PYTHONHOME 必须指向 base Python 安装目录（含标准库），而非 .venv
        Environment.SetEnvironmentVariable("PYTHONHOME", basePython, EnvironmentVariableTarget.Process);

        // 设置 PYTHONPATH 确保能加载 .venv 的 site-packages 和后端源码
        Environment.SetEnvironmentVariable("PYTHONPATH", BuildPythonPath(), EnvironmentVariableTarget.Process);
    }

    private string BuildPythonPath()
    {
        var basePython = ResolveBasePython(PythonHome);
        var venvSitePackages = Path.Combine(PythonHome, "Lib", "site-packages");
        var stdLib = Path.Combine(basePython, "Lib");
        var venvLib = Path.Combine(PythonHome, "Lib");
        var dllsDir = Path.Combine(basePython, "DLLs");
        // 后端源码目录必须在 path 中，以便 import app
        // DLLs 目录必须包含在内，否则 C 扩展模块（如 _socket.pyd）无法加载
        return $"{BackendSourcePath};{venvSitePackages};{venvLib};{stdLib};{dllsDir}";
    }

    private static string ResolveBasePython(string pythonHome)
    {
        // 如果 pythonHome 是 .venv，base Python 是 C:\Users\...\Python312
        // 如果 pythonHome 已经是 base Python，直接用
        var basePrefixFile = Path.Combine(pythonHome, "pyvenv.cfg");
        if (File.Exists(basePrefixFile))
        {
            // 这是虚拟环境，从 pyvenv.cfg 中读取 home
            var lines = File.ReadAllLines(basePrefixFile);
            foreach (var line in lines)
            {
                if (line.TrimStart().StartsWith("home = ", StringComparison.OrdinalIgnoreCase))
                {
                    var home = line.Substring(line.IndexOf('=') + 1).Trim();
                    if (Directory.Exists(home))
                        return home;
                }
            }
        }
        // 可能是 base Python 目录，或者直接使用 pythonHome
        if (File.Exists(Path.Combine(pythonHome, "Lib", "encodings", "__init__.py")))
            return pythonHome;

        // 回退到已知路径
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallback = Path.Combine(localAppData, "Programs", "Python", "Python312");
        if (Directory.Exists(fallback))
            return fallback;

        throw new InvalidOperationException(
            $"无法确定 base Python 安装路径。pythonHome={pythonHome}，请检查 pyvenv.cfg 或直接指定 PythonDll 路径。");
    }

    private void ImportBackendModules()
    {
        using (Py.GIL())
        {
            // 将后端源码目录加入 sys.path
            dynamic sys = Py.Import("sys");
            sys.path.insert(0, BackendSourcePath);

            // 验证核心模块可导入
            try
            {
                Py.Import("app.core.config");
                Py.Import("app.application.services.search_service");
                Py.Import("app.application.services.model_service");
                Log.Information("Python 后端模块导入成功");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Python 后端模块导入失败");
                throw;
            }
        }
    }

    private static string ResolveDefaultPythonHome()
    {
        var baseDir = AppContext.BaseDirectory;
        // 从 bin/Debug/net10.0 向上找到仓库根
        var current = new DirectoryInfo(baseDir);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "backend")))
            {
                var venvPath = Path.Combine(current.FullName, "src", "backend", ".venv");
                if (Directory.Exists(venvPath))
                    return venvPath;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("无法找到 Python 虚拟环境路径 (.venv)");
    }

    private static string ResolveDefaultPythonDll(string pythonHome)
    {
        // 先检查 .venv 的 Scripts 目录
        var venvDll = Path.Combine(pythonHome, "Scripts", "python312.dll");
        if (File.Exists(venvDll))
            return venvDll;

        // 尝试从 base Python 安装目录查找
        var basePython = ResolveBasePython(pythonHome);
        var baseDll = Path.Combine(basePython, "python312.dll");
        if (File.Exists(baseDll))
            return baseDll;

        // 尝试从已知路径查找
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var candidates = new[]
            {
                Path.Combine(localAppData, "Programs", "Python", "Python312", "python312.dll"),
                Path.Combine(programFiles, "Python312", "python312.dll"),
                Path.Combine(programFiles, "Python", "Python312", "python312.dll"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        throw new InvalidOperationException(
            $"无法找到 python312.dll。pythonHome={pythonHome}，basePython={basePython}");
    }
}