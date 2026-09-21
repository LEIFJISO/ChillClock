using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ChillFocusWhitelist.Core;

/// <summary>一条窗口标题规则。</summary>
public sealed class WindowRule
{
    /// <summary>进程列原文（exe 名或完整路径，语义同白名单）。</summary>
    public string Process;

    /// <summary>标题模式（支持 * / ?，不含通配符时按"包含"匹配）。</summary>
    public string Pattern;

    /// <summary>true = allow（放行）；false = block（收起）。</summary>
    public bool Allow;

    /// <summary>原始行，日志/展示用。</summary>
    public string Raw;

    public string ModeName
    {
        get { return Allow ? "allow" : "block"; }
    }

    public string Describe()
    {
        return Process + "|" + Pattern + "|" + ModeName;
    }
}

/// <summary>一个窗口对规则库的判定结果。</summary>
public enum WindowRuleDecision
{
    /// <summary>没有适用规则：按进程白名单处理。</summary>
    None,

    /// <summary>标题命中了 allow 规则：放行。</summary>
    Allow,

    /// <summary>标题命中了 block 规则：收起（即使进程在白名单）。</summary>
    Block,

    /// <summary>该进程有 allow 规则，但标题没命中：收起（窗口级白名单 / 限制模式）。</summary>
    Restricted
}

public enum WindowRuleAddResult
{
    Added,
    Duplicate,
    Invalid
}

/// <summary>
/// 窗口标题规则（WindowRules.txt）。
///
/// 白名单只能按进程放行/收起；浏览器这类一个进程有多个窗口的应用就没法细分。
/// 这里在进程白名单之上再加一层"窗口标题"判定：
///
///   block：标题命中 ⇒ 一定收起（进程在白名单也一样）
///   allow：标题命中 ⇒ 一定放行（进程不在白名单也放行）；
///          某进程只要写了 allow 规则，它的其它窗口（标题非空）就会被当成
///          "不在白名单"处理 —— 也就是窗口级白名单。
///
/// 标题来源是 GetWindowText（= 活动标签页标题，或 Chrome/Edge「命名窗口」的
/// 静态名字）。规则只作用于专注期间的窗口巡逻（WindowGuard）。
///
/// 文件格式：进程|标题模式|模式。标题里含 | 也没关系 ——
/// 按第一个 | 和最后一个 | 切分，中间整段都是标题模式。
/// </summary>
public sealed class WindowRuleStore
{
    public const string FileName = "WindowRules.txt";

    /// <summary>文件被重写时写回的说明头（设置页增删规则也会重写文件）。</summary>
    private static readonly string[] HeaderLines =
    {
        "# ===== Chill Clock 窗口标题规则 / Window title rules =====",
        "# 每行一条：进程|标题模式|模式",
        "#   进程     ：exe 名（chrome.exe）或完整路径，写法与 FocusWhitelist.txt 相同",
        "#   标题模式 ：* 任意长度、? 单个字符；不含通配符时按“包含”匹配；忽略大小写",
        "#   模式     ：allow（放行） / block（收起）；block 优先于 allow",
        "# 说明：",
        "#   block 命中  → 该窗口一定被收起（进程在白名单也一样）",
        "#   allow 命中  → 该窗口一定放行；某进程写了 allow 规则后，它的其它窗口",
        "#                （标题非空）会被当成“不在白名单”处理（窗口级白名单）",
        "#   空标题的窗口在有 allow 规则的进程下按放行处理（避免误伤浏览器内部窗口）",
        "#   标题里含 | 也没关系（按第一个 | 和最后一个 | 切分）",
        "# 例（默认全部注释，取消注释即生效）：",
        "# 宽版：未命名窗口一律收（Chrome/Edge 命名窗口后标题是静态名字，不带浏览器后缀）",
        "#   chrome.exe|* - Google Chrome|block",
        "#   msedge.exe|* - Microsoft Edge|block",
        "# 严版：只放行名字里带 [CC] 的命名窗口（工作窗口命名成“[CC]工作”）",
        "#   chrome.exe|*[CC]*|allow",
        "#   msedge.exe|*[CC]*|allow",
        "# 页面标题规则（best effort；页面标题会变，也可能被扩展改）",
        "#   chrome.exe|*新标签页*|block",
        "# 注意：在设置页增删规则会重写本文件（上面的说明会保留，你手写的其它注释不会）"
    };

    private readonly string _filePath;
    private readonly object _lock = new object();
    private readonly List<WindowRule> _rules = new List<WindowRule>();

    public WindowRuleStore(string filePath)
    {
        _filePath = filePath;
        Reload();
    }

    public string FilePath
    {
        get { return _filePath; }
    }

    /// <summary>读写失败时叫一声（由调用方接日志）。</summary>
    public Action<string> OnWarning { get; set; }

    public IReadOnlyList<WindowRule> Rules
    {
        get
        {
            lock (_lock)
                return _rules.ToList();
        }
    }

    /// <summary>重新读文件（设置页每次打开时调一次，手改文件免重启）。</summary>
    public void Reload()
    {
        lock (_lock)
        {
            _rules.Clear();
            try
            {
                if (!File.Exists(_filePath))
                {
                    // 首次运行：写一份带注释示例的模板（全部注释掉，不改变现有行为）
                    SaveLocked();
                    return;
                }

                foreach (var raw in File.ReadAllLines(_filePath, Encoding.UTF8))
                {
                    if (TryParse(raw, out var rule))
                        _rules.Add(rule);
                }
            }
            catch (Exception e)
            {
                OnWarning?.Invoke("窗口规则读取失败：" + e.Message);
            }
        }
    }

    /// <summary>
    /// 规则判定。调用方按结果决定：
    ///   Block / Restricted → 收起；Allow → 放行；None → 按进程白名单。
    /// </summary>
    public WindowRuleDecision Evaluate(string processPath, string processName, string title)
    {
        lock (_lock)
        {
            var block = false;
            var allow = false;
            var restricted = false;

            foreach (var rule in _rules)
            {
                if (!ProcessMatches(rule.Process, processPath, processName))
                    continue;

                if (rule.Allow)
                {
                    restricted = true;
                    if (TitleMatches(rule.Pattern, title))
                        allow = true;
                }
                else if (TitleMatches(rule.Pattern, title))
                {
                    block = true;
                }
            }

            if (block)
                return WindowRuleDecision.Block;
            if (allow)
                return WindowRuleDecision.Allow;
            if (restricted && !string.IsNullOrEmpty(title))
                return WindowRuleDecision.Restricted;
            return WindowRuleDecision.None;
        }
    }

    /// <summary>
    /// 找命中的规则（block 优先）并给出可读描述；没有命中返回 null。
    /// 匹配测试和日志都用它。
    /// </summary>
    public WindowRule FindMatch(string processPath, string processName, string title, out bool isAllow)
    {
        isAllow = false;

        lock (_lock)
        {
            WindowRule allowRule = null;
            foreach (var rule in _rules)
            {
                if (!ProcessMatches(rule.Process, processPath, processName))
                    continue;
                if (!TitleMatches(rule.Pattern, title))
                    continue;

                if (!rule.Allow)
                    return rule;

                if (allowRule == null)
                    allowRule = rule;
            }

            if (allowRule != null)
            {
                isAllow = true;
                return allowRule;
            }

            return null;
        }
    }

    /// <summary>命中的规则描述（如 "block chrome.exe|*新标签页*"）；没命中返回 null。</summary>
    public string MatchedRuleDescription(string processPath, string processName, string title)
    {
        var rule = FindMatch(processPath, processName, title, out var isAllow);
        return rule == null ? null : rule.ModeName + " " + rule.Process + "|" + rule.Pattern;
    }

    /// <summary>该进程是不是写了 allow 规则（设置页提示"限制模式"用）。</summary>
    public bool HasAllowRules(string processPath, string processName)
    {
        lock (_lock)
        {
            return _rules.Any(rule =>
                rule.Allow && ProcessMatches(rule.Process, processPath, processName));
        }
    }

    public WindowRuleAddResult TryAdd(string process, string pattern, bool allow)
    {
        process = (process ?? string.Empty).Trim();
        pattern = (pattern ?? string.Empty).Trim();
        if (process.Length == 0 || pattern.Length == 0 || process.IndexOf('|') >= 0)
            return WindowRuleAddResult.Invalid;

        lock (_lock)
        {
            foreach (var rule in _rules)
            {
                if (rule.Allow == allow &&
                    string.Equals(rule.Process, process, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return WindowRuleAddResult.Duplicate;
                }
            }

            var added = new WindowRule
            {
                Process = process,
                Pattern = pattern,
                Allow = allow,
                Raw = process + "|" + pattern + "|" + (allow ? "allow" : "block")
            };
            _rules.Add(added);
            SaveLocked();
            return WindowRuleAddResult.Added;
        }
    }

    public void Remove(WindowRule rule)
    {
        if (rule == null)
            return;

        lock (_lock)
        {
            _rules.RemoveAll(existing =>
                ReferenceEquals(existing, rule) ||
                (existing.Allow == rule.Allow &&
                 string.Equals(existing.Process, rule.Process, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(existing.Pattern, rule.Pattern, StringComparison.OrdinalIgnoreCase)));
            SaveLocked();
        }
    }

    // ---------- 解析与匹配 ----------

    /// <summary>
    /// 解析一行：进程|标题模式|模式。
    /// 用第一个 | 和最后一个 | 切分，所以标题模式里含 | 不影响。
    /// </summary>
    private static bool TryParse(string raw, out WindowRule rule)
    {
        rule = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var line = raw.Trim();
        if (line.StartsWith("#") || line.StartsWith(";"))
            return false;

        var first = line.IndexOf('|');
        if (first <= 0)
            return false;

        var last = line.LastIndexOf('|');
        if (last <= first)
            return false;

        var process = line.Substring(0, first).Trim();
        var pattern = line.Substring(first + 1, last - first - 1).Trim();
        var mode = line.Substring(last + 1).Trim();
        if (process.Length == 0 || pattern.Length == 0)
            return false;

        bool allow;
        if (mode.Equals("allow", StringComparison.OrdinalIgnoreCase) ||
            mode.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            allow = true;
        }
        else if (mode.Equals("block", StringComparison.OrdinalIgnoreCase) ||
                 mode.Equals("b", StringComparison.OrdinalIgnoreCase) ||
                 mode.Equals("deny", StringComparison.OrdinalIgnoreCase))
        {
            allow = false;
        }
        else
        {
            return false;
        }

        rule = new WindowRule { Process = process, Pattern = pattern, Allow = allow, Raw = line };
        return true;
    }

    /// <summary>进程列匹配：完整路径 / exe 名 / 去扩展名，忽略大小写（与白名单同语义）。</summary>
    private static bool ProcessMatches(string entry, string processPath, string processName)
    {
        var value = Normalize(entry);
        if (string.IsNullOrEmpty(value))
            return false;

        var path = Normalize(processPath);
        if (!string.IsNullOrEmpty(path) && string.Equals(value, path, StringComparison.OrdinalIgnoreCase))
            return true;

        var name = Normalize(processName);
        if (!string.IsNullOrEmpty(name) && string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = !string.IsNullOrEmpty(name)
            ? name
            : (!string.IsNullOrEmpty(path) ? Path.GetFileName(path) : null);
        if (!string.IsNullOrEmpty(fileName))
        {
            var noExtension = Normalize(Path.GetFileNameWithoutExtension(fileName));
            if (!string.IsNullOrEmpty(noExtension) &&
                string.Equals(value, noExtension, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 标题匹配：
    ///   不含 * / ? → 子串"包含"匹配（更符合直觉：写“工作台”就能匹配“…工作台 - Google Chrome”）
    ///   含通配符   → 整串通配匹配（* 任意长度、? 单个字符），忽略大小写
    /// 空标题一律不匹配（空标题的容错在 Evaluate 里处理）。
    /// </summary>
    public static bool TitleMatches(string pattern, string title)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(title))
            return false;

        var hasWildcard = pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;
        if (!hasWildcard)
            return title.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

        var p = 0;
        var t = 0;
        var star = -1;
        var mark = 0;
        while (t < title.Length)
        {
            if (p < pattern.Length &&
                (pattern[p] == '?' ||
                 char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(title[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p;
                p++;
                mark = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                mark++;
                t = mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }

    private static string Normalize(string value)
    {
        if (value == null)
            return null;

        value = value.Trim().Trim('"');
        return value.Length == 0 ? null : value;
    }

    private void SaveLocked()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var builder = new StringBuilder();
            foreach (var line in HeaderLines)
                builder.AppendLine(line);
            foreach (var rule in _rules)
                builder.AppendLine(rule.Process + "|" + rule.Pattern + "|" + rule.ModeName);

            File.WriteAllText(_filePath, builder.ToString(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            OnWarning?.Invoke("窗口规则保存失败：" + e.Message);
        }
    }
}
