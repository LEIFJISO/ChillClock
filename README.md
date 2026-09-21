# Chill Clock（专注时钟）

[简体中文](README.md) | [English](README_EN.md) | [日本語](README_JA.md)

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET Framework 4.7.2](https://img.shields.io/badge/.NET%20Framework-4.7.2-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework/net472)
[![BepInEx](https://img.shields.io/badge/BepInEx-Plugin-green.svg)](https://github.com/BepInEx/BepInEx)

一个用于游戏《放松时光：与你共享Lo-Fi故事》的 BepInEx 插件：**在聪音专注时禁止打开白名单以外的应用**

---

[![Chill with You](imgs/header_schinese.jpg)](https://store.steampowered.com/app/3548580/)

> 「放松时光：与你共享Lo-Fi故事」是一个与喜欢写故事的女孩聪音一起工作的有声小说游戏。您可以自定义艺术家的原创乐曲、环境音和风景，以营造一个专注于工作的环境。在与聪音的关系加深的过程中，您可能会发现与她之间的特别联系。
---
## 效果演示：

![alt text](imgs/overview.png)
## 它解决什么问题
<img src="imgs/satone.png" alt="satone" width="300">

### 专注软件首先得能专注！专注，唯有专注！

- Chill Clock 会在专注期间自动把**不在白名单里的应用最小化到任务栏**；
- 即使你再次打开它们，也会被继续最小化；
- 专注结束 / 休息 / 结束通话后，Chill Clock 停止干预。
- 同时支持**番茄钟**与**正计时**两种计时模式；
- 设置页提供「专注时禁止结束/跳过」「专注时隐藏 UI」「专注时禁止关闭游戏」用于更强制性的专注模式；
- 走神、打开任务管理器或尝试关闭游戏时，聪音会播放语音提醒；
- 支持**窗口标题规则**：在白名单（进程级）之上按窗口标题再筛一层 —— Chrome/Edge 多窗口、命名窗口可按名字放行/收起。设置页提供「从窗口列表添加规则」和「窗口匹配测试」（模拟判定，不会真的收窗口）；

**新增的语音（共 1722 条，全部内嵌在 DLL 里）**

| 场景 | 合计 | 多句语音 | 单句语音 |
| --- | ---: | ---: | ---: |
| 专注中自言自语 | 466 | 115 | 351 |
| 专注中点她 | 125 | 44 | 81 |
| 休息中点她 | 133 | 44 | 89 |
| 平时点她 | 51 | 4 | 47 |
| 走神提醒 | 328 | 163 | 165 |
| 休息提醒 | 110 | 30 | 80 |
| 任务管理器拦截 | 110 | 30 | 80 |
| 关闭游戏拦截 | 110 | 30 | 80 |
| 小课堂故事（平时 / 休息闲聊、点她） | 289 | 289 | 0 |
| **合计** | **1722** | **749** | **973** |

> 「多句语音」是连着说几句的连播段落，共 260 组，其中 53 组是「小课堂」故事（每段 4～20 句）。
> 「小课堂」的 289 条会进平时点她 / 休息中点她 / 非专注闲聊 / 休息闲聊这几个池子；另有 36 条节日台词，只在节日当天说一次，并按早 / 午 / 晚 / 夜区分时段。

> 试了让应用隐藏和应用置顶的方案，结果都不太好用，要是强退游戏还会出现些后遗症，最终还是让应用最小化这个方案稳妥点


## 窗口标题规则（窗口级筛选）

白名单只能按**进程**放行，浏览器这类一个进程多个窗口的应用会整体放行。
窗口标题规则在它之上再按**窗口标题**判断（标题 = 当前活动标签页，或 Chrome/Edge「命名窗口」的静态名字）：

| 模式 | 行为 |
| --- | --- |
| `block` | 标题匹配 ⇒ 一定收起（进程在白名单也一样） |
| `allow` | 标题匹配 ⇒ 一定放行；某进程写了 allow 规则后，它的其它窗口（标题非空）一律收起，也就是"窗口级白名单" |

- 文件：`BepInEx/plugins/WindowRules.txt`（首次运行自动生成带注释示例，默认全部注释、不生效）
- 格式：`进程|标题模式|模式`；标题模式支持 `*`（任意长度）`?`（单个字符），不含通配符时按"包含"匹配，忽略大小写
- 设置页：「从窗口列表添加规则」会按窗口生成标题模式初稿（可在文件里手改细调）；「窗口匹配测试」按当前规则 + 白名单模拟判定

**示例：Chrome/Edge「命名窗口」当通行证**

1. 右键浏览器标签栏 →「命名窗口」，把工作窗口命名成 `[CC]工作`（命名后窗口标题就是这个名字，不随页面变化）；
2. 规则写严版 `chrome.exe|*[CC]*|allow`、`msedge.exe|*[CC]*|allow`；或宽版 `chrome.exe|* - Google Chrome|block`、`msedge.exe|* - Microsoft Edge|block`（未命名窗口全收，命名过的不限名字）；
3. 专注期间：未命名/没带 `[CC]` 的浏览器窗口会被收起，带 `[CC]` 的命名窗口放行。

> 限制：只能看到**当前活动标签页**的标题（或命名窗口的名字）。同一个窗口里切换到摸鱼标签页、以及后台标签页都无法单独识别 —— 要做到标签页级，需要配套浏览器扩展，计划在后续版本提供；届时的处置方式是**把摸鱼标签切回白名单里的标签页**，而不是最小化窗口。Firefox 没有内置命名窗口，可用 Window Titler 扩展。


## 安装步骤

### 前置环境要求

- 游戏《放松时光：与你共享Lo-Fi故事》
- [BepInEx 5.x](https://github.com/BepInEx/BepInEx/releases)（请勿使用 6.0）

### 步骤
1. **安装 BepInEx**
* 从上方链接下载 BepInEx （`BepInEx_win_x64_5.4.23.5.zip`）。
* 解压至游戏根目录。
* 运行一次游戏以生成 BepInEx 相关文件夹（能看到 `[游戏根目录]/BepInEx/plugins/`）。

2. **安装 Mod**
* 从 Release 下载最新版本的 `ChillClock.dll`。
* 将 `ChillClock.dll` 放入`BepInEx/plugins/` 目录下。
* 之前装过旧版的话，**用新文件覆盖**同一个 `ChillClock.dll` 就行；不要把两个名字不同的 dll 同时放在 `plugins/` 里（BepInEx 会认为它们是两个插件，把同一套功能加载两遍）。
* 确保你的文件夹结构如下所示：


```
[游戏根目录]/
└── BepInEx/
    └── plugins/
            └── ChillClock.dll
```

## 关于其他Mod
对演示视频中的界面UI和播放音乐感兴趣的话，可以看 [ChillPatcherLite](https://github.com/anyukari/ChillPatcherLite)

如果您对此游戏其他Mod感兴趣，可参见：[awesome-chillwithyou](https://github.com/clsty/awesome-chillwithyou)

## 开源协议

本项目采用 [MIT License](LICENSE)。

> 简单说：你可以自由使用、修改、分发、用于个人或商业项目；
> 只需保留版权声明和许可文本，并为自己的使用行为负责。

## 致谢

- 感谢 [BepInEx](https://github.com/BepInEx/BepInEx) 社区
- 设置页注入思路参考 [iGPU Savior (Potato Mode)](https://github.com/Small-tailqwq/iGPUSaviorMod)
- 番茄钟挂钩思路参考 [LofiNotify](https://github.com/kanghengliu/lofinotify)

> 本插件仅供学习交流，请支持正版游戏。
