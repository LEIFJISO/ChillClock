# Phase B 设计记录：标签页级筛选（浏览器扩展）

> 状态：**未实施**（Phase A = 窗口标题规则，已在本仓库分支 `feature/window-title-filter` 完成）
> 记录日期：2026-09-21

## 核心决定

- 浏览器内部标签页切换时，Win32 只能看到**活动标签页的窗口标题**，而且标题可被改名绕过 —— 要拿真实 URL、要精确处置，必须配套浏览器扩展。
- **标签页级的处置方式不是最小化窗口，而是把当前摸鱼标签切回白名单里的标签页。**
  - 优先切回该窗口内最近一个"白名单标签页"（按窗口记录 lastAllowedTabId）。
  - 只有该窗口内没有白名单标签页、切无可切时，才退化为最小化整窗。
- 每一次拦截都上报 mod，由 mod 播聪音的走神提醒（复用现有语音池，不新增）。

## 架构（待实施）

### mod 侧（BepInEx 插件内）
- 起一个 loopback HTTP 服务（建议 `TcpListener` 手写极简 HTTP，绑定 `127.0.0.1` + 随机 token），避免 http.sys 前缀/管理员权限问题：
  - `GET /state`：专注状态（是否专注/休息）、URL 规则或白名单、token 校验。
  - `POST /event`：扩展上报拦截事件（url / windowId / tabId / 动作类型）→ mod 触发语音。
- 只监听回环、只提供状态与事件两类接口，不做任意指令通道。

### 扩展侧（Chrome / Edge，MV3）
- 轮询 `GET /state`（专注状态变化不频繁，1–2 秒一次即可）。
- 监听 `chrome.tabs.onUpdated` / `chrome.tabs.onActivated`；专注中若活动标签 URL 不在白名单：
  1. `chrome.tabs.update(lastAllowedTabId, { active: true })` —— **切回白名单标签**；
  2. 没有可切回的标签时，`chrome.windows.update(windowId, { state: 'minimized' })`；
  3. `POST /event` 通知 mod 播语音。
- 窗口大小/命名策略与 Phase A 的窗口标题规则互不冲突：扩展管标签、mod 管窗口与其他应用。

### 明确不采用的方案
- **CDP / `--remote-debugging-port`**：Chrome 136+ 对默认用户数据目录直接忽略该开关，不可行。
- **UI Automation 读地址栏**：语言相关、性能开销、窗口最小化时读不到，太脆弱。
- **全局键鼠钩子**：本项目已踩过键盘延迟的坑（0.8.0 起全部移除），不再使用。

## 待定（实施前需确认）

- 扩展分发方式：unpacked 手动装 vs 上架商店。
- URL 白名单/规则格式，以及编辑入口（扩展 options 页 vs mod 设置页同步下发）。
- Firefox 适配：无内置命名窗口（需 Window Titler），WebExtensions API 基本兼容但需单独验证。
- 安装心智成本与失败提示（扩展没装时，mod 侧应能检测并提示）。

## 与 Phase A 的关系

Phase A（`WindowRules.txt` / 设置页"从窗口列表添加规则" + "窗口匹配测试"）解决的是**窗口级**筛选：
未命名/未标记的浏览器窗口会被收起，带标记（如 `[CC]`）的命名窗口放行。
标签页级（同一窗口内切换标签页）只能由 Phase B 的扩展解决，且按本记录采用**切回**而不是最小化。
