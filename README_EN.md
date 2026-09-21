# Chill Clock

[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET Framework 4.7.2](https://img.shields.io/badge/.NET%20Framework-4.7.2-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework/net472)
[![BepInEx](https://img.shields.io/badge/BepInEx-Plugin-green.svg)](https://github.com/BepInEx/BepInEx)

A BepInEx plugin for *Chill with You : Lo-Fi Story*: **when Satone is focusing, it prevents you from opening apps that are not on the whitelist.**

---

[![Chill with You](imgs/header_schinese.jpg)](https://store.steampowered.com/app/3548580/)

> *Chill with You : Lo-Fi Story* is an audiovisual novel about spending your work time with Satone, a girl who loves writing stories. You can customize original music, ambient sounds, and scenery to create an environment that helps you focus—and as your relationship deepens, you might discover something special between you two.

---

## Demo

![Demo](imgs/overview_en.png)

## What problem does it solve?

<img src="imgs/satone.png" alt="satone" width="300">

### Honestly, it solves my own problem: I get distracted too easily—clicking this, checking that, and the whole day is gone without getting anything done.

- During a focus session, Chill Clock automatically minimizes apps that are **not on your whitelist** to the taskbar;
- Non-whitelisted windows that are already open get minimized;
- Even if you open them again from the Start menu or system tray, they get minimized again;
- Whitelisted apps keep working normally;
- After focus ends / a break starts / you hang up the call, Chill Clock stops interfering.
- Works with both **Pomodoro** and **Count-up** timer modes;
- Settings include "Disable End/Skip in Focus", "Hide Side UI in Focus" and "Block Game Exit in Focus" (this one also keeps Steam from being closed, since quitting Steam force-kills the game — add Steam to the whitelist to disable that part);
- Satone can play voice reminders when you get distracted, open Task Manager, or try to exit;
- **Window title rules** add per-window filtering on top of the process-level whitelist — multi-window apps like Chrome/Edge can be allowed or minimized by window title (including Chromium "named windows"). The settings page has "Add Rule from Windows" and "Test Window Matching" (a simulation; nothing is actually minimized);
- The settings UI is localized in Simplified Chinese / English / Japanese.

**Satone's voice lines (1,722 in total, all embedded in the DLL)**

| Scene | Total | Chained | Single |
| --- | ---: | ---: | ---: |
| Idle chat during focus | 466 | 115 | 351 |
| Click during focus | 125 | 44 | 81 |
| Click during a break | 133 | 44 | 89 |
| Click outside focus | 51 | 4 | 47 |
| Distraction reminders | 328 | 163 | 165 |
| Break reminders | 110 | 30 | 80 |
| Task Manager interception | 110 | 30 | 80 |
| Game exit interception | 110 | 30 | 80 |
| Mini-lecture stories (idle / break chat, clicks) | 289 | 289 | 0 |
| **Total** | **1,722** | **749** | **973** |

> "Chained" means a run of lines spoken back to back — 260 chains in total, 53 of which are the "mini-lecture" stories (4–20 lines each). The 289 mini-lecture lines go into the click-outside-focus / click-during-break / idle chat / break chat pools. There are also 36 festival lines, spoken only once on the day itself and split by morning / noon / evening / night. The voice pack is embedded in `ChillClock.dll`, so no extra voice folder is needed.

## Window title rules (per-window filtering)

The whitelist works at **process** level, so an app with multiple windows (browsers!) is allowed as a whole. Window title rules add a per-window check on top (title = the active tab, or the static name of a Chromium "named window"):

| Mode | Behavior |
| --- | --- |
| `block` | Matching windows are always minimized (even if the process is whitelisted) |
| `allow` | Matching windows are always kept; once a process has any `allow` rule, its other windows (with a non-empty title) are minimized — a per-window whitelist |

- File: `BepInEx/plugins/WindowRules.txt` (created automatically with commented examples; all examples are disabled by default)
- Format: `process|title-pattern|mode`; `*` and `?` wildcards, or plain substring match when no wildcard is used (case-insensitive)
- In the settings page: "Add Rule from Windows" generates a title-pattern draft from a window; "Test Window Matching" simulates the verdict of the current rules + whitelist without minimizing anything

**Example: use Chrome/Edge "named windows" as a pass**

1. Right-click the tab strip → "Name window" and name your work window `[CC]work` (the OS window title then stays that name and no longer follows the page title);
2. Add strict rules `chrome.exe|*[CC]*|allow`, `msedge.exe|*[CC]*|allow` — or loose rules `chrome.exe|* - Google Chrome|block`, `msedge.exe|* - Microsoft Edge|block` (every unnamed window gets minimized);
3. During focus, unnamed/unmarked browser windows are minimized while the marked work window stays.

> Limitation: Windows can only see the **active tab's** title (or the name of a named window). Switching to a distracting tab inside an allowed window, and background tabs, cannot be detected individually. Tab-level control requires a companion browser extension, planned for a later release; its action will be **switching back to a whitelisted tab** rather than minimizing the window. Firefox has no built-in window naming; use the Window Titler extension instead.

## Installation

### Requirements

- *Chill with You : Lo-Fi Story*
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases) (do not use 6.0)

### Steps

1. **Install BepInEx**
   - Download BepInEx from the link above.
   - Extract it into the game root folder.
   - Run the game once so BepInEx creates its folders (you should see `BepInEx/plugins/`).

2. **Install the Mod**
   - Download the latest `ChillClock.dll` from Releases.
   - Put `ChillClock.dll` into `BepInEx/plugins/`.
   - Upgrading from an older build? Just **overwrite** that same `ChillClock.dll`. Do not keep two differently-named copies of this plugin in `plugins/` — BepInEx would load the whole plugin twice.
   - Your folder structure should look like this:

```
[Game root]/
└── BepInEx/
    └── plugins/
            └── ChillClock.dll
```

## License

This project is released under the [MIT License](LICENSE).

> In short: you are free to use, modify, and distribute it for personal or commercial projects; just keep the copyright notice and license text, and take responsibility for your own use.

## Credits

- Thanks to the [BepInEx](https://github.com/BepInEx/BepInEx) community
- Settings page injection based on [iGPU Savior (Potato Mode)](https://github.com/Small-tailqwq/iGPUSaviorMod)
- Pomodoro hooking based on [LofiNotify](https://github.com/kanghengliu/lofinotify)

> For personal and learning purposes only. Do not sell it directly. Use at your own risk.
