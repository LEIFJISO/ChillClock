// 我们念台词的时候，把游戏"挂在动作上的小声音"拦掉（喝咖啡的呼呼、看书时的嗯声…）。
//
// 这些声音在游戏里是语音资产，前缀是 Motion_（正常台词是 Voice_、自言自语是 SelfTalk_）。
// 它们和我们的台词会叠在一起响 —— 用户听到的就是"她一边喝咖啡一边讲话"。
// 这里只拦 Motion_*，别的一概不动。
using System;
using HarmonyLib;
using UnityEngine;

// 注意：放在 .UI 命名空间下，才能用 Plugin.PatchMethod("类名","方法名") 注册
//（这个 mod 没有用 PatchAll，是逐个手动挂的）。
namespace ChillFocusWhitelist.UI;

[HarmonyPatch(typeof(HeroineVoiceController), "PlayVoice",
    new[] { typeof(string), typeof(bool), typeof(bool) })]
internal static class MotionVoiceSuppressPatch
{
    /// <summary>我们的台词要响到这个时刻（由 VoiceManager 每句设置）。</summary>
    public static float SpeakingUntil;

    [HarmonyPrefix]
    private static bool Prefix(string __0)
    {
        if (string.IsNullOrEmpty(__0))
            return true;
        if (Time.realtimeSinceStartup >= SpeakingUntil)
            return true;
        if (!__0.StartsWith("Motion_", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;   // 我们正在说话：这条动作附带音不播
    }

    private static readonly System.Collections.Generic.HashSet<string> _logged = new();

    /// <summary>
    /// 给 MotionSoundController 那些"动作自带的声音"用的通用 Prefix：
    /// 我们正在念台词就不播（返回 false = 跳过原方法）。
    /// 目标是 PlayDrinkToCoolVoice（喝东西吹气"呼呼"）、PlayDrinkHotVoice、
    /// NovelFlipPage / TextbookFlipPage（看书翻页）。
    /// </summary>
    public static bool SimplePrefix()
    {
        return Time.realtimeSinceStartup >= SpeakingUntil;
    }
}
