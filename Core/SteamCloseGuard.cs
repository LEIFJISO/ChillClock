using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChillFocusWhitelist.Core;

/// <summary>
/// 专注期间不让 Steam 被关掉。
///
/// 为什么要它：Steam 一退出就会把游戏进程强杀（拦不住的 —— 日志直接断在半截，
/// 没有 Unity 的关闭流程、也没有崩溃栈）。所以只能不让 Steam 退出，两条路一起堵：
///
///   1. 托盘图标 →「退出 Steam」：那个菜单是 steamwebhelper 用 CEF 画的窗口
///      （class=Chrome_RenderWidgetHostHWND），没有 Win32 菜单项可以读，
///      所以直接把落在这种小弹窗上的点击吞掉。
///   2. 任务栏右键 →「关闭窗口 / 退出 Steam」：任务栏按钮来自 Steam 的窗口，
///      所以把 Steam / steamwebhelper 的可见窗口直接收起来（隐藏），
///      任务栏上没有按钮，也就没有那个菜单了。
///
/// 为什么用隐藏而不是发关闭消息：Steam 把 WM_CLOSE 实现成了"最小化到托盘"，
/// 发过去它只是最小化，任务栏按钮还在；而且**最小化的窗口同样有按钮**，
/// 所以两种状态都要收。
///
/// 只在"禁止关闭游戏"生效期间工作，其它时候钩子都不装，零影响。
/// </summary>
internal sealed class SteamCloseGuard
{
    private const int WhMouseLl = 14;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int GwlStyle = -16;
    private const int SwHide = 0;
    private const uint GaRoot = 2;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;

    private readonly Func<bool> _shouldBlock;
    private readonly Func<string, string, bool> _isAllowed;
    private readonly LowLevelMouseProc _proc;
    private readonly EnumWindowsProc _enumProc;
    private readonly HashSet<int> _steamPids = new HashSet<int>();
    private IntPtr _hook;
    private int _nextPidLookup;
    private int _nextSweep;
    private bool _steamAllowed;

    public SteamCloseGuard(Func<bool> shouldBlock, Func<string, string, bool> isAllowed)
    {
        _shouldBlock = shouldBlock;
        _isAllowed = isAllowed;
        // 这两个委托必须长期持有：钩子和枚举回调随时可能被系统调进来
        _proc = Callback;
        _enumProc = SweepCallback;
    }

    public Action OnCloseBlocked { get; set; }

    public void EnsureInstalled()
    {
        if (_hook != IntPtr.Zero)
            return;

        try
        {
            _hook = SetWindowsHookEx(WhMouseLl, _proc, IntPtr.Zero, 0);
        }
        catch
        {
            _hook = IntPtr.Zero;
        }
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero)
            return;

        try
        {
            UnhookWindowsHookEx(_hook);
        }
        catch
        {
            // 进程要退了，解不掉就算了
        }

        _hook = IntPtr.Zero;
    }

    /// <summary>
    /// 把 Steam 的可见窗口收起来（含最小化的）：
    /// 任务栏上没按钮，就没有"右键任务栏 → 退出 Steam"这条路。
    /// </summary>
    public void SweepSteamWindows()
    {
        if (_shouldBlock == null || !_shouldBlock())
            return;

        var now = Environment.TickCount;
        if (now < _nextSweep)
            return;
        _nextSweep = now + 500;

        try
        {
            RefreshSteamProcesses();
            EnumWindows(_enumProc, IntPtr.Zero);
        }
        catch
        {
            // 收不掉就算了，别影响主流程
        }
    }

    private bool SweepCallback(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || !_steamPids.Contains((int)pid))
                return true;

            // 最小化的窗口也有任务栏按钮，一样要收（Steam 主窗口大多数时候就是最小化的）
            if (!IsWindowVisible(hwnd))
                return true;

            // 白名单里的 Steam 不动：用户明确把它加进白名单，就是想专注期间也能用
            if (_steamAllowed || IsPathAllowed(pid))
                return true;

            ShowWindow(hwnd, SwHide);
        }
        catch
        {
            // 单个窗口失败不影响其它
        }

        return true;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var message = wParam.ToInt32();
            if (nCode >= 0 &&
                (message == WmLeftButtonDown || message == WmLeftButtonUp) &&
                _shouldBlock != null &&
                _shouldBlock())
            {
                var info = Marshal.PtrToStructure<MouseHookStruct>(lParam);
                if (IsSteamPopupClick(info.Point))
                {
                    OnCloseBlocked?.Invoke();
                    return (IntPtr)1; // 吞掉这次点击
                }

            }
        }
        catch
        {
            // 钩子回调里绝不能抛，出错就放行
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// 点的是不是 Steam（含 steamwebhelper）的小弹窗 —— 托盘菜单就是这类。
    /// 大窗口（Steam 主界面）放行，不影响正常操作。
    /// </summary>
    private bool IsSteamPopupClick(Point point)
    {
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero)
            return false;

        var root = GetAncestor(hwnd, GaRoot);
        if (root == IntPtr.Zero)
            root = hwnd;

        GetWindowThreadProcessId(root, out var pid);
        if (pid == 0)
            return false;

        // 这里**不刷新**进程列表：钩子回调在系统输入路径上，枚举进程会卡住这次点击。
        // 列表由主线程的 SweepSteamWindows() 每 0.5 秒刷新一次，够用。
        if (_steamAllowed || !_steamPids.Contains((int)pid))
            return false;

        return IsPopupLike(root);
    }

    /// <summary>Steam 在白名单里就别管它（用户想专注期间也能用 Steam）。</summary>
    private bool IsPathAllowed(uint pid)
    {
        if (_isAllowed == null)
            return false;

        try
        {
            var path = Win32.GetProcessPath(pid);
            return _isAllowed(path, "steam.exe") || _isAllowed(path, "steamwebhelper.exe");
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPopupLike(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return false;

        // Steam 主界面那种大窗口放行
        if (width > 1200 || height > 900)
            return false;

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var popup = (style & WsPopup) != 0;
        var hasCaption = (style & WsCaption) == WsCaption;
        return popup || !hasCaption;
    }

    private void RefreshSteamProcesses()
    {
        var now = Environment.TickCount;
        if (now < _nextPidLookup)
            return;
        _nextPidLookup = now + 5000;

        _steamPids.Clear();
        _steamAllowed = false;
        foreach (var name in new[] { "steam", "steamwebhelper" })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        _steamPids.Add(process.Id);
                        if (!_steamAllowed && IsPathAllowed((uint)process.Id))
                            _steamAllowed = true;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch
            {
                // 查不到就当没有 Steam
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr hwnd, int index);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : GetWindowLong32(hwnd, index);
    }
}
