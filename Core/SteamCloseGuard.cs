using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ChillFocusWhitelist.Core;

/// <summary>
/// 专注期间不让 Steam 被关掉 —— Steam 一退出会把游戏进程强杀，那一步拦不住，
/// 所以只能不让它退，两条路一起堵：
///
///   1. 托盘图标 →「退出 Steam」：那个菜单是 steamwebhelper 用 CEF（Chromium）画的弹窗，
///      没有 Win32 菜单项可以读，所以整块处理 —— 一发现就把这个弹窗隐藏掉。
///   2. 任务栏右键 →「关闭窗口 / 退出 Steam」：任务栏按钮来自 Steam 的窗口，
///      把 Steam / steamwebhelper 的可见窗口收起来（含最小化的），按钮就没了。
///
/// <b>为什么不用鼠标钩子</b>：低级鼠标钩子（WH_MOUSE_LL）是同步链 —— 每个鼠标事件
/// （包括移动）都要先交给我们的进程、等回调返回才发给目标程序；主线程一卡，鼠标就跟着卡。
/// 改成轮询，而且分两级，保证开销可以忽略：
///
///   - <b>快巡</b>（每 50ms）：先花一两微秒看"前台窗口 / 光标下的窗口是不是 Steam 的"，
///     像才继续查类名和尺寸。托盘菜单刚弹出来就会被收掉，用户来不及点。
///   - <b>慢巡</b>（每 500ms）：全量扫一遍，收 Steam 主窗口。
///
/// 只在"禁止关闭游戏"生效期间工作，其它时候一次都不跑。
/// </summary>
internal sealed class SteamCloseGuard
{
    private const int GwlStyle = -16;
    private const int SwHide = 0;
    private const uint GaRoot = 2;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;
    private const int PopupSweepMs = 50;
    private const int FullSweepMs = 500;

    private readonly Func<string, string, bool> _isAllowed;
    private readonly EnumWindowsProc _enumProc;
    private readonly HashSet<int> _steamPids = new HashSet<int>();

    private int _nextPidLookup;
    private int _nextFullSweep;
    private int _nextPopupSweep;
    private bool _steamAllowed;

    public SteamCloseGuard(Func<string, string, bool> isAllowed)
    {
        _isAllowed = isAllowed;
        // 枚举回调必须长期持有：系统随时会调进来
        _enumProc = SweepCallback;
    }

    /// <summary>拦下"关闭 Steam"这个动作时叫一声，让聪音播提醒。</summary>
    public Action OnCloseBlocked { get; set; }

    /// <summary>
    /// 慢巡：把 Steam 的可见窗口收起来（含最小化的）。
    /// 任务栏上没按钮，就没有"右键任务栏 → 退出 Steam"这条路。
    /// </summary>
    public void SweepSteamWindows()
    {
        var now = Environment.TickCount;
        if (now < _nextFullSweep)
            return;
        _nextFullSweep = now + FullSweepMs;

        try
        {
            RefreshSteamProcesses();
            if (_steamAllowed)
                return;

            EnumWindows(_enumProc, IntPtr.Zero);
        }
        catch
        {
            // 收不掉就算了，别影响主流程
        }
    }

    /// <summary>
    /// 快巡：只看"前台窗口 / 光标下的窗口"，是 Steam 的小弹窗就隐藏掉（托盘菜单就是它）。
    ///
    /// 两级：先做两个几乎免费的判断（是不是 Steam 进程的窗口），像了才查类名和尺寸 ——
    /// 常见情况每次就一两微秒，而且完全不碰输入路径。
    /// </summary>
    public void SweepSteamPopups()
    {
        var now = Environment.TickCount;
        if (now < _nextPopupSweep)
            return;
        _nextPopupSweep = now + PopupSweepMs;

        try
        {
            RefreshSteamProcesses();
            if (_steamAllowed || _steamPids.Count == 0)
                return;

            TryHideSteamPopup(GetForegroundWindow());

            if (GetCursorPos(out var cursor))
                TryHideSteamPopup(WindowFromPoint(cursor));
        }
        catch
        {
            // 同上
        }
    }

    private void TryHideSteamPopup(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        var root = GetAncestor(hwnd, GaRoot);
        if (root == IntPtr.Zero)
            root = hwnd;

        GetWindowThreadProcessId(root, out var pid);
        if (pid == 0 || !_steamPids.Contains((int)pid))
            return;

        if (!IsWindowVisible(root) || !IsPopupLike(root))
            return;

        if (ShowWindow(root, SwHide))
            OnCloseBlocked?.Invoke();
    }

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

        // Steam 主界面那种大窗口交给慢巡处理
        if (width > 1200 || height > 900)
            return false;

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        var popup = (style & WsPopup) != 0;
        var hasCaption = (style & WsCaption) == WsCaption;
        return popup || !hasCaption;
    }

    private bool SweepCallback(IntPtr hwnd, IntPtr lParam)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || !_steamPids.Contains((int)pid))
                return true;

            // 最小化的窗口也有任务栏按钮，一样要收（Steam 主窗口大多数时候就是最小化的）
            if (IsWindowVisible(hwnd))
                ShowWindow(hwnd, SwHide);
        }
        catch
        {
            // 单个窗口失败不影响其它
        }

        return true;
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
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

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
