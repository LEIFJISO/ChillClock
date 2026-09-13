using System;
using System.Collections.Generic;

namespace ChillFocusWhitelist.Core;

/// <summary>
/// 今天是哪个节日（没有节日返回 null）。
///
/// 语音目录第 11 列写着节日的台词**只**在这个 id 匹配今天时才会被选中，
/// 其它日子一句都不会出现（见 VoiceManager.Pick）。
///
///   newyear       元旦（1/1）
///   valentine     情人节（2/14）
///   aprilfool     愚人节（4/1）
///   tanabata      七夕（7/7）
///   halloween     万圣节（10/31）
///   christmas     圣诞（12/25）
///   springfestival  春节（农历正月初一，按下表）
///   midautumn     中秋（农历八月十五，按下表）
///
/// 农历节日没法用固定月日表示，所以按年列出来；跨过 2030 年时在
/// YearDates 里补上新的年份即可（农历表容易查，一年两个日期）。
/// </summary>
internal static class FestivalCalendar
{
    /// <summary>固定公历节日：MM-dd → id。</summary>
    private static readonly Dictionary<string, string> SolarDates = new Dictionary<string, string>
    {
        ["01-01"] = "newyear",
        ["02-14"] = "valentine",
        ["04-01"] = "aprilfool",
        ["07-07"] = "tanabata",
        ["10-31"] = "halloween",
        ["12-25"] = "christmas",
    };

    /// <summary>农历节日：yyyy-MM-dd → id（春节 / 中秋）。</summary>
    private static readonly Dictionary<string, string> YearDates = new Dictionary<string, string>
    {
        ["2026-02-17"] = "springfestival",
        ["2026-09-25"] = "midautumn",
        ["2027-02-06"] = "springfestival",
        ["2027-09-15"] = "midautumn",
        ["2028-01-26"] = "springfestival",
        ["2028-10-03"] = "midautumn",
        ["2029-02-13"] = "springfestival",
        ["2029-09-22"] = "midautumn",
        ["2030-02-03"] = "springfestival",
        ["2030-09-12"] = "midautumn",
    };

    /// <summary>今天（本机时间）的节日 id，没有节日返回 null。</summary>
    public static string TodayId()
    {
        try
        {
            var now = DateTime.Now;
            if (YearDates.TryGetValue(now.ToString("yyyy-MM-dd"), out var byYear))
                return byYear;
            if (SolarDates.TryGetValue(now.ToString("MM-dd"), out var bySolar))
                return bySolar;
        }
        catch
        {
            // 取日期失败就当没有节日
        }

        return null;
    }
}
