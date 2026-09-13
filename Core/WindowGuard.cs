using System;
using System.Collections.Generic;

namespace ChillFocusWhitelist.Core;

public sealed class WindowGuard
{
    private readonly WhitelistStore _store;
    private readonly HashSet<IntPtr> _minimizedByUs = new HashSet<IntPtr>();
    private readonly object _lock = new object();
    private float _nextSkipLog;

    public WindowGuard(WhitelistStore store)
    {
        _store = store;
    }

    public Action<string, bool> OnWindowMinimized { get; set; }

    public bool FocusActive { get; private set; }

    public void SetFocusActive(bool active)
    {
        lock (_lock)
        {
            if (active == FocusActive)
                return;

            FocusActive = active;
            if (active)
            {
                Plugin.Log.LogInfo("[Chill Clock] focus minimize started");
                // 开场这一轮收窗口**不出声**：那些应用是开始专注之前就开着的，
                // 游戏自己这时候也正在说"开始工作了"，紧跟着来一句"你又开别的应用了吧"
                // 会显得莫名其妙。专注期间新冒出来的窗口才会提醒（见 Tick）。
                if (!HeroineActionBridge.IsForegroundCritical())
                    SweepMinimizeLocked(false, false);
            }
            else
            {
                EndFocusLocked();
            }
        }
    }

    public void Tick()
    {
        lock (_lock)
        {
            if (!FocusActive)
                return;

            // 只在"开场问候 / 结束通话告别"这类依赖游戏前台焦点的演出里让路。
            // 以前这里用的是宽判据 IsGameSequenceBusy()，把野生动作（伸懒腰、端杯子）、
            // 睡觉、番茄钟动作、点击反应统统算成"忙"，结果她一想事情就不收窗口了 ——
            // 那些和窗口焦点没有任何关系。
            if (HeroineActionBridge.IsForegroundCritical())
            {
                LogSweepSkipped();
                return;
            }

            SweepMinimizeLocked(false, true);
            PruneDeadHandlesLocked();
        }
    }

    public void TickTaskManagerOnly()
    {
        lock (_lock)
        {
            if (HeroineActionBridge.IsForegroundCritical())
                return;

            SweepMinimizeLocked(true, true);
            PruneDeadHandlesLocked();
        }
    }

    public void ReleaseAll()
    {
        lock (_lock)
            EndFocusLocked();
    }

    /// <summary>演出期间跳过收窗口时留一条线索（10 秒最多一条，正常情况下几乎不会出现）。</summary>
    private void LogSweepSkipped()
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        if (now < _nextSkipLog)
            return;

        _nextSkipLog = now + 10f;
        Plugin.Log.LogInfo("[Chill Clock] 游戏演出中（开场 / 结束通话 / 离席），这一轮先不收窗口");
    }

    private void SweepMinimizeLocked(bool taskManagerOnly, bool allowVoice)
    {
        var windows = Win32.EnumerateVisibleTopLevelWindows();
        var hidAny = false;
        foreach (var window in windows)
        {
            if (taskManagerOnly && !window.IsTaskManager)
                continue;
            if (window.IsShellWindow)
                continue;
            if (window.ProcessId == (uint)Win32.CurrentProcessId)
                continue;
            if (string.IsNullOrEmpty(window.ProcessPath) && !window.IsTaskManager)
                continue;
            if (Win32.IsMinimized(window.Handle))
                continue;
            if (_store.IsAllowed(window.ProcessPath, window.ProcessName))
                continue;

            if (window.IsTaskManager)
            {
                Win32.RequestClose(window.Handle);
                if (allowVoice)
                    OnWindowMinimized?.Invoke(window.ProcessName ?? "taskmgr.exe", true);
                continue;
            }

            if (Win32.HideWindow(window.Handle))
            {
                hidAny = true;
                _minimizedByUs.Add(window.Handle);
                if (allowVoice)
                    OnWindowMinimized?.Invoke(window.ProcessName, false);
                Plugin.Log.LogInfo("[Chill Clock] minimized window: " + window.ProcessName);
            }
        }

        // 这里**不**去抢焦点：SetForegroundWindow 在我们不是前台进程时会失败，
        // 而 Windows 会把"有人想让这个窗口到前台"渲染成任务栏按钮闪红光 —— 实测会一直闪。
        // 收窗口本身不该动焦点，游戏自己会处理失焦/回焦。
        _ = hidAny;
    }

    private void EndFocusLocked()
    {
        // 只解除管制：保留当前最小化状态，不自动把所有窗口弹回。
        _minimizedByUs.Clear();
        FocusActive = false;
        Plugin.Log.LogInfo("[Chill Clock] focus minimize ended (windows stay minimized)");
    }

    private void PruneDeadHandlesLocked()
    {
        _minimizedByUs.RemoveWhere(handle => !Win32.IsWindowAlive(handle));
    }
}
