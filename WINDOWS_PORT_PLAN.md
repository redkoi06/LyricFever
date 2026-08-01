# LyricFever Windows 移植评估方案（修订版）

> 基于代码库现状（macOS 15+ 原生 SwiftUI 应用）对 Windows 移植的可行性评估、技术选型与实施路线。
> 分支：`windows-build`（2026-08-01）
> **技术栈决策（已确认）：C# .NET 8 + WPF**
> **功能范围决策（2026-08-01 修订）：Windows 版仅保留 K 歌模式 + 本地翻译（英/日→中）+ 日语罗马音，其余功能删除**（详见 §3）

---

## 1. 项目现状概览

| 维度 | 现状 |
|---|---|
| 技术栈 | 原生 SwiftUI + AppKit，Swift 6，deployment target macOS 15.0+ |
| 规模 | 87 个 Swift 文件，约 1.2 万行（Views 4,420 行 / ViewModel 2,195 行 / 服务+模型+Provider ~3,500 行） |
| 第三方依赖 | 14 个 SPM 包 |
| 功能 | 菜单栏歌词、Karaoke 悬浮窗、全屏模式、歌词翻译（Apple 本地框架）、罗马音、简繁转换、Spotify/Apple Music 集成、歌词源（Spotify 内部 API / LRCLIB / NetEase）、CoreData 离线缓存、全局快捷键 |

**核心数据流**（移植后保持不变）：

```text
播放器状态监听（macOS: ScriptingBridge + DistributedNotificationCenter）
  → 曲目元数据 → track ID → 歌词获取（CoreData 缓存 → Spotify 内部 API → LRCLIB → NetEase）
  → 歌词数组 + 播放进度 → 歌词 updater → K 歌悬浮窗
```

---

## 2. 平台依赖分类

### 2.1 纯 Swift 逻辑 —— 仅需换语言重写，可对照移植（~3,500 行）

| 模块 | 文件 | 说明 |
|---|---|---|
| LRC 解析 | `Models/LyricsParser/*`（226 行） | 无任何平台 API |
| 歌词匹配 | `Models/MetadataMatcher.swift`（115 行） | 纯逻辑 |
| Spotify 歌词获取链 | `LyricProvider/Spotify/*`（约 450 行） | spclient 歌词 API、token API、HOTP、GraphQL 搜索——**全部是 HTTPS 网络调用** |
| LRCLIB / NetEase 歌词源 | `LyricProvider/LRCLIB/*`、`NetEase/*`（约 400 行） | 纯 URLSession |
| 歌词模型、枚举、工具 | `LyricLine.swift`、`SongResult.swift`、`PlayerType.swift` 等 | 纯数据 |
| 专辑封面 URL 解析 | `Support Files/SpotifyProcessedURL.swift` | 纯逻辑 |

### 2.2 Apple 平台通用但可替代

| 现状 | Windows 替代 |
|---|---|
| CoreData 4 实体（歌词/颜色/AM 映射/翻译语言缓存） | SQLite（Microsoft.Data.Sqlite）或 JSON 缓存 |
| Keychain 存 sp_dc cookie | DPAPI 加密落盘 |
| UserDefaults | settings.json / 注册表 |
| URLSession | HttpClient |
| 颜色提取（ColorKit + NSImage 扩展） | 纯算法重写（已有手写 `findAverageColor()` 可参考） |
| SwiftOTP / StringMetric | 同算法 C# 重写（各几十行） |
| Amplitude | Amplitude .NET SDK（或删除） |
| LaunchAtLogin | 注册表 `HKCU\...\Run` 键 |
| WKWebView 登录页 | WebView2 |

### 2.3 深度绑定 macOS —— 必须重写/替换

| 模块 | macOS API | Windows 对策 |
|---|---|---|
| **K 歌悬浮窗**（`FloatingPanel.swift` 188 行 + `KaraokeView.swift` 164 行） | NSPanel、NSVisualEffectView | WPF 无边框透明置顶窗口（§5.2） |
| **Spotify 播放器桥**（`SpotifyPlayer.swift` 124 行 + `SpotifyScripting.swift` 76 行） | ScriptingBridge | SMTC 监听 + 控制（§5.1） |
| **Apple Music 播放器桥**（171 + 590 行） | ScriptingBridge、MediaRemote | 删除或 SMTC 尽力而为（§3） |
| **封面获取** | mediaremote-adapter（私有框架） | SMTC 自带缩略图 / artworkUrl |
| 磨砂/毛玻璃 | NSVisualEffectView | DWM 亚克力或自绘半透明 |
| 翻译框架 | Translation framework（macOS 15+） | **CTranslate2 + OPUS-MT en-zh / ja-zh（INT8）**（§5.4） |
| 日语罗马音 | Mecab-Swift + IPAdic | **Kawazu + LibNMeCab + IPADic**（同词库，§5.5） |
| 简繁转换 | SwiftyOpenCC | OpenCC .NET 绑定（或删除） |

**删除后可忽略的 macOS 代码**（约 2,300 行）：`LyricFever.swift` 菜单栏宿主（226 行）、`MenubarLabelView.swift`（432 行）、`LyricsNSScrollView.swift`（615 行）、`MulticolorGradient` + Metal shader（全屏背景）、`SearchResultsNSTableView`/`LyricPreviewNSTableView`（188 行）、FullscreenView（326 行）、MenubarWindowView 大部分（775 行）。

---

## 3. 功能范围（Windows 版）

### 3.1 保留（核心）

| 功能 | 说明 |
|---|---|
| **K 歌模式悬浮窗** | 置顶透明窗口，逐行高亮歌词；含翻译、罗马音层（对齐 macOS `KaraokeView`：原文 + 罗马音 + 译文三层结构） |
| **本地歌词翻译**（英/日 → 中） | 本地模型推理，无需联网，无需 API key（§5.4） |
| **日语罗马音标注** | 分词 + 假名读音转罗马音（§5.5） |
| **Spotify 集成** | SMTC 读取播放状态/控制；歌词获取链原样移植（§5.1） |
| **歌词源** | Spotify 内部 API → LRCLIB → NetEase 兜底链 |
| **歌词离线缓存** | SQLite 缓存（替代 CoreData），切歌秒开 |
| **歌词偏移调整** | 歌词与播放进度不同步时的手动补偿（K 歌体验关键） |
| **专辑封面 → 背景色提取** | K 歌窗口背景色（延续 macOS 行为） |
| **托盘** | 托盘图标 + 右键菜单（显示/隐藏 K 歌窗口、播放控制、设置、退出） |
| **设置窗口** | 精简设置：播放器选择、翻译语言对、罗马音开关、歌词偏移、模型管理 |

### 3.2 删除（已确认）

| 功能 | 原因 |
|---|---|
| 状态栏歌词 | Windows 无状态栏（已定） |
| 全屏模式 | 不需要（已定） |
| 全局快捷键 | 不需要（已定） |
| 菜单栏点击小浮窗（popover） | 已定删除；播放控制收进托盘菜单 |
| 菜单栏长度截断设置（truncationLength） | 依附菜单栏 |
| 歌曲详情显示设置 | 依附菜单栏 |

### 3.3 待确认（默认删除，需要保留请告知）

| 功能 | macOS 实现 | 默认 |
|---|---|---|
| Apple Music 集成 | ScriptingBridge + persistentID 映射 | **删除**（Windows 上无公开 API，SMTC 精度差，集成成本高收益低） |
| 简繁中文转换 | SwiftyOpenCC | 删除 |
| 手动搜索歌词/替换歌词 | SearchWindow + NSTableView | 删除（歌词源兜底链已覆盖绝大多数情况） |
| 本地歌词文件上传 | NSOpenPanel | 删除 |
| 音量滑块 | ScriptingBridge setSoundVolume | 删除（音量控制权限在播放器内） |
| 开机自启 | LaunchAtLogin | 删除 |
| 自动更新 | Sparkle | 删除（V1 手动发布安装包） |
| 统计分析 | Amplitude | 删除（隐私友好） |
| 多语言 UI | Localizable.xcstrings（79KB） | 删除（V1 仅简体中文 UI） |
| 翻译目标语言选择 | 设置里可选 | 固定中文（需求即英/日→中） |
| K 歌窗口内播放控制（暂停/切歌） | 无（macOS 版控制在小浮窗） | 托盘菜单提供 |

---

## 4. 技术栈

**C# .NET 8 + WPF**（已确认）。理由：SMTC/托盘/透明置顶窗口/WebView2 全部是成熟路径；Windows 歌词领域同类项目（Lyricify 等）同栈可参考；需重写的纯逻辑约 3,500 行，可控。

**架构约定**：

```text
LyricFever.Windows（WPF App，单项目或 2 项目分层）
  ├─ Core/       ← 纯逻辑：LRC 解析、MetadataMatcher、歌词 Provider、缓存、翻译抽象
  ├─ Players/    ← SMTC 监听 + 播放控制（Spotify）
  ├─ ViewModels/ ← 歌词状态机（对应 macOS ViewModel.swift，状态组语义 1:1）
  ├─ Views/      ← K 歌悬浮窗、设置窗口
  └─ Services/   ← 翻译（CTranslate2 + OPUS-MT）、罗马音（Kawazu）、颜色、模型下载
```

---

## 5. 各模块移植方案

### 5.1 Spotify 集成

| 能力 | macOS 实现 | Windows 实现 |
|---|---|---|
| 播放状态/曲目/进度 | ScriptingBridge | **SMTC**（`Windows.Media.Control`，NuGet 封装 `Dubya.WindowsMediaController`；Spotify 的 SMTC 质量 Perfect） |
| 播放控制 | ScriptingBridge | SMTC `TryPlayAsync` / `TrySkipNextAsync` 等 |
| 曲目 ID | `spotify:track:` URI | SMTC 不给 URI → 按歌手+歌名搜索兜底（macOS 已有 `alternativeID` 思路） |
| 歌词获取 | spclient 内部 API + sp_dc + HOTP | **原样重写为 C#（纯网络）**，协议不变 |
| 登录 | WKWebView 抓 sp_dc | WebView2 抓 cookie，或手动粘贴 sp_dc（macOS 已有该入口） |

### 5.2 K 歌悬浮窗

| NSPanel 行为 | WPF 等价 |
|---|---|
| `.nonactivatingPanel`（点击不抢焦点） | `WS_EX_NOACTIVATE` 扩展样式 |
| `level = .mainMenu` 置顶 | `Topmost = true` |
| 透明背景 | `WindowStyle=None` + `AllowsTransparency=true` |
| 点击拖动 + 边缘吸附 | 手写鼠标事件（逻辑同 macOS `FloatingPanel.sendEvent`） |
| 毛玻璃 | `DwmSetWindowAttribute` 亚克力或自绘半透明模糊 |
| alpha 淡入淡出 | WPF 动画 |

内容层（歌词 + 罗马音 + 译文三层、逐行高亮）对照 `KaraokeView.swift` 的排版逻辑重写；字体、背景色、透明度设置保留。

### 5.3 歌词引擎与缓存

- LRC 解析、MetadataMatcher、LRCLIB/NetEase/Spotify provider：C# 重写，对照现有 3 个测试文件写单元测试。
- 歌词状态机（ViewModel）：**歌词数组 + 索引 + 派生数组（译文/罗马音）作为一组状态、切歌整组重置、异步结果回验 track ID、安全下标**——AGENTS.md 沉淀的修复经验逐条对照移植。
- 缓存：SQLite 单库（对应 CoreData 实体），歌词数组 JSON 列存储。
- **翻译/罗马音产物缓存（用户要求：下次优先用本地产物，不重新调用模型）**：
  - 缓存表：`trackID + lyricHash + sourceLanguage + targetLanguage + translationModelVersion + romanizationVersion` 为唯一键
  - 内容：原歌词、中文翻译、日语罗马音、模型版本、生成时间
  - 命中即直接显示，跳过模型加载与翻译；版本号变化（模型升级）自动失效重建

### 5.4 本地翻译（英/日 → 中）—— 最终定案（2026-08-01 用户确认）

**结论：Windows 无系统级本地翻译 API**（微软从未提供免费的本地文本翻译 WinRT API）。macOS 版使用的 Apple Translation framework 无法在 Windows 上使用，必须集成第三方本地模型。

**要求（用户确认）**：完全本地、低 CPU/内存/磁盘占用、24GB 内存目标机、翻译质量要求不高（生硬可接受）、速度和低负载优先。

**最终方案：CTranslate2 + OPUS-MT（Marian）INT8**

| 组件 | 选型 |
|---|---|
| 推理引擎 | **CTranslate2**（INT8，CPU） |
| 英→中模型 | Helsinki-NLP/opus-mt-en-zh（转换 CT2 INT8） |
| 日→中模型 | shun89/opus-mt-ja-zh（转换 CT2 INT8） |
| 日语罗马音 | Kawazu + LibNMeCab + IPADic（独立管线） |

**为何不用 NLLB / Qwen**（用户决策）：
1. NLLB-600M 支持 ~200 种语言，本项目只需英/日两对，能力浪费；INT8 运行时内存 ~1GB+，专用 Marian 模型仅需几百 MB。
2. Qwen 是通用 LLM，翻译速度慢、CPU/内存占用更高。
3. OPUS-MT 是专用翻译模型，速度快，适合整首歌词批量翻译；用户接受较低质量，无需 LLM。

**Windows 调用方式**：将 CTranslate2 封装为**原生 C++ DLL**，对外暴露简单 C 接口（`load_model` / `translate_batch` / `unload_model`），C# 客户端通过 P/Invoke 调用。模型转换（HuggingFace Marian → CT2 INT8）为一次性工具链操作，产出 `.ct2` 模型目录随应用分发或首次下载。

**运行配置（低负载定案）**：
- 设备 CPU；计算格式 INT8
- `inter_threads = 1`、`intra_threads = 2`
- `beam_size = 1`（贪心解码，不做 Beam Search）

**模型加载策略**（默认 0 占用）：
1. 默认不加载任何模型；用户开启翻译后才加载。
2. 英文歌只加载 en-zh，日文歌只加载 ja-zh。
3. 连续播放同语言歌曲复用已加载模型；语言切换时卸载旧模型再加载新模型。
4. 关闭翻译或空闲数分钟后自动卸载。

**歌词批处理**：不逐行调用——整首 20~60 行一次性批量提交，模型批量翻译，按歌词行 ID 恢复顺序。减少调用次数、降低 CPU 频繁唤醒、缩短整首翻译时间、便于任务取消与切歌校验。

**翻译产物缓存（用户明确要求：下次优先用缓存，不重新调用模型）**：
- 缓存键：`trackID + lyricHash + sourceLanguage + targetLanguage + translationModelVersion`（罗马音另加 `romanizationVersion`）
- 缓存内容：原歌词、中文翻译、罗马音、模型版本、生成时间
- 同一首歌再次播放直接读缓存，不加载翻译模型（§5.3）

**架构**：

```text
ITranslationProvider
  ├── CTranslate2TranslationProvider（Windows，en-zh / ja-zh 双模型）
  └── AppleTranslationProvider（macOS，包装 TranslationSession，供未来抽象复用）

IRomanizationProvider
  └── KawazuRomanizationProvider（Windows）/ 现有 RomanizerService（macOS）
```

ViewModel 与歌词显示逻辑不直接依赖 `TranslationSession` 类型——Windows 移植时移除 ViewModel 对 Apple 类型的直接依赖。

**翻译处理流程**：获取整首歌词 → 识别主语言 → 按语言选择/加载模型 → 批量翻译（日文歌并行 Kawazu 生成罗马音）→ 按行 ID 对齐 → 写入缓存 → 显示。翻译失败仍可显示罗马音（两管线相互独立）。

**资源占用预估**：单模型 INT8 磁盘 ~100–300MB，运行时只加载一个模型，内存几百 MB，翻译完成后 CPU 接近零；需模型转换后基准实测。

### 5.5 日语罗马音（定案：Kawazu + LibNMeCab + IPADic）

- **Kawazu**（NuGet，基于 LibNMeCab + LibNMeCab.IpaDicBin 的日语形态分析封装）直接输出 Romaji，与 macOS 版 Mecab-Swift + IPAdic **同词库、同分词质量**
- 示例：`君の声が聞こえる` → `kimi no koe ga kikoeru`
- **与翻译相互独立**：罗马音由专用形态分析工具生成，不交给翻译模型——速度更快、资源占用极低、结果稳定，且翻译失败时罗马音仍可显示
- 运行时机：整首歌词一次处理，与翻译任务并行执行（后台任务、切歌取消、校验 track ID）

### 5.6 颜色提取

- K 歌窗口背景色：从专辑封面取"白字可读的最饱和主色"（`findWhiteTextLegibleMostSaturatedDominantColor`）——纯算法重写为 C#（ColorKit 的 kMeans 部分用简化版或参考已有手写 `findAverageColor`）
- 缓存到 SQLite（IDToColor 表）

### 5.7 托盘与设置

- `Hardcodet.NotifyIcon.Wpf`（托盘）：
  - 左键单击：显示/隐藏 K 歌窗口
  - 右键菜单：显示/隐藏、播放控制（播放/暂停、上一首/下一首）、翻译开关、罗马音开关、设置、退出
- 设置窗口：单窗口，Tab 结构：播放器（Spotify 登录）、翻译（源语言、模型管理/下载状态）、K 歌（字体、大小、背景色、透明度、偏移）、通用
- 登录：WebView2（`Microsoft.Web.WebView2` NuGet）复刻 macOS `WebLoginView` 流程（登录 → 抓 sp_dc → DPAPI 加密存储）

### 5.8 存储

- SQLite（`Microsoft.Data.Sqlite`）4 张表：`SongObject`（歌词缓存）、`IDToColor`、`PersistentIDToSpotify`（仅 Spotify 场景保留与否视 Apple Music 决策）、`SongToLocale`（可删，翻译目标固定中文）
- 策略：ViewModel 串行访问 + 后台写入（同 macOS）

---

## 6. 实施计划

| 阶段 | 内容 | 验证标准 |
|---|---|---|
| **P0 骨架** | .NET 解决方案、托盘 + 设置窗口框架、项目分层 | 托盘显示、设置窗口开合 |
| **P1 歌词引擎** | LRC 解析、Provider（Spotify/LRCLIB/NetEase）、SQLite 缓存、歌词状态机 | 单元测试通过（对照现有测试） |
| **P2 Spotify 集成（MVP）** | SMTC 监听、WebView2/手动登录、歌词获取、主流程跑通 | 真实播放时拿到歌词与进度 |
| **P3 K 歌窗口** | 悬浮窗（置顶/透明/拖动）、歌词三层排版 + 逐行高亮、背景色 | 与 macOS K 歌模式视觉行为对齐 |
| **P4 翻译与罗马音**（按用户四阶段顺序） | ① 抽象接口（ITranslationProvider / IRomanizationProvider；macOS 侧封装 AppleTranslationProvider、移除 ViewModel 对 TranslationSession 直接依赖，该重构在 Mac 上验证）→ ② Windows 翻译原型（下载/转换 CT2 INT8 模型、C++ DLL + P/Invoke、批量接口、英日实测）→ ③ Kawazu 罗马音（并行管线 + 缓存）→ ④ 负载优化（按需加载/空闲卸载、2 线程、beam=1、产物缓存、任务取消、基准测试） | 英/日歌词实时显示译文与罗马音；关闭翻译零占用；翻译产物缓存命中不加载模型 |
| **P5 打磨与发布** | 歌词偏移、设置完备、打包（单文件/安装包）、文档 | 干净 Windows 机器安装即用 |

（原评估中的全屏、快捷键、搜索等阶段已按新范围移除。）

---

## 7. 风险与应对

| 风险 | 等级 | 应对 |
|---|---|---|
| Spotify 私有歌词 API 变更/封禁 | 高 | 与 macOS 版共享同一协议实现；保留 LRCLIB/NetEase 兜底链；现有 UA 伪装等对抗手段原样保留 |
| OPUS-MT ja-zh 模型（shun89）可用性/质量未实测 | 中 | P4 阶段二先下载转换并实测英/日歌词翻译；不达标再评估同类 ja-zh Marian 模型替换 |
| 模型转换工具链（Python + ct2 命令） | 低 | 一次性离线操作，转换产物（CT2 INT8）随应用分发，运行时无需 Python |
| C++ DLL 构建与 P/Invoke 边界 | 低 | DLL 只暴露 3 个 C 接口；字符串传递约定（UTF-8 + 长度）在阶段二原型中先验证 |
| 日→中质量生硬到不可达意（用户接受生硬但需达意） | 中 | 歌词领域短句翻译；必要时微调 beam 或改用 float16 对比；备用切换至 NLLB/Qwen 档（架构已抽象） |
| 低配电脑资源占用 | 中 | 按需加载 + 空闲卸载（平时 0 占用）；默认关闭翻译功能；INT8 + 2 线程已是最低档 |
| 模型下载体积/网络（单模型 ~100–300MB INT8） | 中 | 首次启用翻译时懒下载 + 断点续传 + 国内镜像（HF-Mirror）可选 |
| 罗马音分词粒度与 macOS 版不一致 | 低 | Kawazu 与 macOS 版同词库（IPAdic）；粒度差异通过后处理对齐（标点附着等规则） |
| SMTC 在部分环境不可用（旧 Windows） | 低 | 要求 Windows 10 1903+（SMTC 依赖）；提供"不可用"提示 |

---

## 8. 结论

1. 新范围下工作量显著收敛：删除菜单栏/全屏/搜索等约 2,300 行 macOS 独占 UI 后，移植重心为 **K 歌窗口（WPF）+ 歌词引擎（C# 重写）+ Spotify（SMTC）+ 本地翻译/罗马音**。
2. 翻译定案：**CTranslate2 + OPUS-MT（en-zh / ja-zh INT8）**——专用 NMT 相比 NLLB/Qwen 更轻、更快、内存更低，符合低负载与"接受生硬"的要求；罗马音用 **Kawazu + LibNMeCab + IPADic**（与 macOS 同词库）。
3. 罗马音用 **NMeCab + IPAdic**（与 macOS 同词库），质量对齐。
4. 待确认功能默认删除，见 §3.3 清单。

**下一步**：确认 §3.3 待确认清单 → 开始 P0 骨架（安装 .NET 8 SDK、建解决方案、托盘 + 设置窗口）。
