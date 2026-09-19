// 临时调试用：把游戏里已加载的 MasterData（NovelMaster / ScenarioGroupMaster …）
// 导成 TSV，方便照抄它每一句的 BodyMotion / FacialMotion / LookScale。
// 用完可以整文件删掉。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using UnityEngine;

namespace ChillFocusWhitelist.Core;

internal static class MasterDataDump
{
    private static bool _done;

    /// <summary>找到所有已加载的 MasterData 对象，把每个集合字段里的行按字段名导出。</summary>
    public static void DumpOnce(ManualLogSource log)
    {
        if (_done)
            return;
        _done = true;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type\tRow\t" + "Fields...");
            var found = 0;

            foreach (var obj in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                var type = obj.GetType();
                if (!type.Name.Contains("Master") && !type.Name.Contains("Novel"))
                    continue;

                found++;
                sb.AppendLine("# " + type.FullName);
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var value = field.GetValue(obj);
                    if (value is not IEnumerable list || value is string)
                        continue;

                    var rows = new List<object>();
                    foreach (var item in list)
                        rows.Add(item);
                    if (rows.Count == 0)
                        continue;

                    sb.AppendLine("#   " + field.Name + " (" + rows.Count + " rows)");
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        if (row == null)
                            continue;
                        var cells = new List<string> { field.Name, i.ToString() };
                        var rt = row.GetType();
                        foreach (var rf in rt.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        {
                            var v = rf.GetValue(row);
                            cells.Add(rf.Name + "=" + (v?.ToString() ?? ""));
                        }
                        sb.AppendLine(string.Join("\t", cells));
                    }
                }
            }

            var folder = Path.GetDirectoryName(typeof(MasterDataDump).Assembly.Location) ?? ".";
            var path = Path.Combine(folder, "MasterDataDump.tsv");
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            log.LogInfo("[Chill Clock] MasterData 导出完成：" + path + "（对象 " + found + " 个）");
        }
        catch (Exception e)
        {
            log.LogWarning("[Chill Clock] MasterData 导出失败: " + e);
        }
    }
}
