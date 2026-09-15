using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ChillFocusWhitelist.Core;

/// <summary>
/// 给游戏自己的顶层窗口挂上关闭消息拦截，在专注/休息期间阻止
/// 右上角 X、任务栏关闭、Alt+F4 这些正常退出。
///
/// 为什么必须是它：任务栏那条「关闭窗口」是**直接发送**给窗口过程的（不进消息队列），
/// Unity 自己的 Application.wantsToQuit 也不管这条 —— 实测两条路都拦不到，
/// 只有接管窗口过程能拦。
///
/// 注意：它和"小窗模式"（画中画 mod 会改同一个窗口）冲突，专注中按 F3 会崩，
/// 所以 <see cref="Plugin"/> 会在小窗模式下把它卸掉，退出小窗再装回来。
/// </summary>
internal sealed class CloseGuard
{
    private const int GwlWndProc = -4;
    private const uint WmClose = 0x0010;
    private const uint WmSyscommand = 0x0112;
    private const uint WmNcdestroy = 0x0082;
    private const int ScClose = 0xF060;

    private readonly Func<bool> _shouldBlock;
    private readonly Dictionary<IntPtr, IntPtr> _previousProcs = new Dictionary<IntPtr, IntPtr>();

    /// <summary>
    /// 窗口过程回调。**一旦创建就永不释放**。
    ///
    /// SetWindowLongPtr 记的是这个委托的函数指针，只要还有窗口没还原成功，
    /// Windows 之后仍可能回调进来。以前 Uninstall() 里把它置成 null ——
    /// 那些没还原成功的窗口就成了"指向已回收内存的过程"，退出时收到
    /// WM_DESTROY 之类的消息就是 0xc0000005。
    /// </summary>
    private readonly WindowProcDelegate _procDelegate;

    private int _nextEnumerateTime;
    private float _lastCallAt;

    public CloseGuard(Func<bool> shouldBlock)
    {
        _shouldBlock = shouldBlock;
        _procDelegate = WndProc;
    }

    public Action OnCloseBlocked { get; set; }

    /// <summary>
    /// 我们这个过程最近被调用过吗。
    ///
    /// 用来判断"我们是不是还在链子里"：画中画 mod 会把自己的过程装在我们上面，
    /// 这时窗口的当前过程不是我们，但它的过程会往下转给我们 —— 我们照样收得到消息。
    /// 所以只看"当前过程是不是我们"会误判，再装一次就会在链子里出现两个我们的过程。
    /// </summary>
    public bool WasCalledRecently(float withinSeconds)
    {
        return UnityEngine.Time.realtimeSinceStartup - _lastCallAt < withinSeconds;
    }

    public void EnsureInstalled()
    {
        var now = Environment.TickCount;
        if (now < _nextEnumerateTime)
            return;
        _nextEnumerateTime = now + 300;

        try
        {
            var handles = Win32.EnumerateCurrentProcessVisibleWindowHandles();
            if (handles.Count == 0)
            {
                var fallback = Win32.GetCurrentProcessMainWindow();
                if (fallback != IntPtr.Zero)
                    handles.Add(fallback);
            }

            var alive = new HashSet<IntPtr>();
            foreach (var hwnd in handles)
            {
                if (!Win32.IsWindowAlive(hwnd))
                    continue;
                alive.Add(hwnd);
                EnsureWindowInstalled(hwnd);
            }

            RemoveDeadAndMissing(alive);
        }
        catch
        {
            // 同步失败不阻塞主流程
        }
    }

    public void Uninstall()
    {
        foreach (var hwnd in new List<IntPtr>(_previousProcs.Keys))
            RestoreWindow(hwnd, remove: true);
    }

    private void EnsureWindowInstalled(IntPtr hwnd)
    {
        if (_previousProcs.ContainsKey(hwnd))
        {
            var currentProc = GetWindowLongPtr(hwnd, GwlWndProc);
            var myProc = Marshal.GetFunctionPointerForDelegate(_procDelegate);
            if (currentProc == myProc)
                return;

            // 别人盖在我们上面（画中画 mod 也接管了同一个窗口）：**什么都不要做** ——
            // 既不要把我们记的旧过程写回去（那会打断它的链，崩在 ntdll），
            // 也不要把记录删掉（删了以后我们的过程就不知道该往哪转了，会把它的链弄断）。
            // 保留记录、保留我们那个过程，让它转下来的时候我们继续正常转发。
            return;
        }

        var result = SetWindowLongPtr(
            hwnd,
            GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_procDelegate));
        if (result == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != 0)
                return;
        }

        _previousProcs[hwnd] = result;
    }

    private void RemoveDeadAndMissing(HashSet<IntPtr> alive)
    {
        foreach (var hwnd in new List<IntPtr>(_previousProcs.Keys))
        {
            if (!alive.Contains(hwnd))
                RestoreWindow(hwnd, remove: true);
        }
    }

    private void RestoreWindow(IntPtr hwnd, bool remove)
    {
        if (!_previousProcs.TryGetValue(hwnd, out var previous))
            return;

        try
        {
            var currentProc = GetWindowLongPtr(hwnd, GwlWndProc);
            var myProc = Marshal.GetFunctionPointerForDelegate(_procDelegate);
            if (currentProc == myProc)
                SetWindowLongPtr(hwnd, GwlWndProc, previous);
        }
        catch
        {
            // 窗口可能已销毁
        }

        if (remove)
            _previousProcs.Remove(hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        _lastCallAt = UnityEngine.Time.realtimeSinceStartup;

        try
        {
            // 窗口正在销毁：把记录清掉再照原样转交。
            // 不清的话，HWND 被系统复用时就会拿着上一个窗口的过程指针去调。
            if (msg == WmNcdestroy)
                return PassMessageToOriginal(hwnd, msg, wParam, lParam);

            if (msg == WmClose || (msg == WmSyscommand && (wParam.ToInt32() & 0xFFF0) == ScClose))
            {
                var block = _shouldBlock != null && _shouldBlock();
                if (block)
                {
                    OnCloseBlocked?.Invoke();
                    return IntPtr.Zero;
                }

                if (msg == WmClose)
                    return PassCloseToOriginal(hwnd, wParam, lParam);
            }
        }
        catch
        {
            // 回调异常时放行
        }

        return _previousProcs.TryGetValue(hwnd, out var previous)
            ? CallWindowProc(previous, hwnd, msg, wParam, lParam)
            : DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr PassMessageToOriginal(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!_previousProcs.TryGetValue(hwnd, out var previous))
            return DefWindowProc(hwnd, msg, wParam, lParam);

        _previousProcs.Remove(hwnd);
        return CallWindowProc(previous, hwnd, msg, wParam, lParam);
    }

    private IntPtr PassCloseToOriginal(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
    {
        if (!_previousProcs.TryGetValue(hwnd, out var previous))
            return IntPtr.Zero;

        RestoreWindow(hwnd, remove: true);
        return CallWindowProc(previous, hwnd, WmClose, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(
        IntPtr previousProc,
        IntPtr hwnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(
        IntPtr hwnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern IntPtr SetWindowLong32(IntPtr hwnd, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern IntPtr GetWindowLong32(IntPtr hwnd, int index);

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newValue)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, newValue)
            : SetWindowLong32(hwnd, index, newValue);
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : GetWindowLong32(hwnd, index);
    }
}
