using System;
using System.Runtime.InteropServices;
using System.Threading;
using Serilog;

namespace AssetsLibrarySystem.Avalonia.Services.Hotkey;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x5A11;
    private const uint ModifierControl = 0x0002;
    private const uint ModifierShift = 0x0004;
    private const uint VirtualKeySpace = 0x20;
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;

    private ManualResetEventSlim ThreadReady { get; } = new(false);
    private Thread? WorkerThread { get; set; }
    private uint WorkerThreadId { get; set; }
    private bool IsDisposed { get; set; }

    public event EventHandler? HotkeyPressed;

    public void Start()
    {
        if (WorkerThread is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Log.Information("当前平台不支持全局热键，已跳过注册");
            ThreadReady.Set();
            return;
        }

        WorkerThread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "AssetsLibrarySystem Hotkey",
        };

#pragma warning disable CA1416
        WorkerThread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        WorkerThread.Start();
        ThreadReady.Wait(TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;

        if (WorkerThreadId != 0)
        {
            PostThreadMessage(WorkerThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        WorkerThread?.Join(TimeSpan.FromSeconds(1));
        ThreadReady.Dispose();
    }

    private void RunMessageLoop()
    {
        WorkerThreadId = GetCurrentThreadId();
        ThreadReady.Set();

        if (!RegisterHotKey(IntPtr.Zero, HotkeyId, ModifierControl | ModifierShift, VirtualKeySpace))
        {
            Log.Warning("注册全局快捷键失败，快速检索窗口只保留托盘菜单入口");
            return;
        }

        try
        {
            while (true)
            {
                var result = GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    Log.Warning("全局快捷键消息循环异常退出");
                    break;
                }

                if (message.message == WmHotkey && message.wParam == (IntPtr)HotkeyId)
                {
                    HotkeyPressed?.Invoke(this, EventArgs.Empty);
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TranslateMessage([In] ref NativeMessage lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DispatchMessage([In] ref NativeMessage lpMsg);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr hWnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public NativePoint pt;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
