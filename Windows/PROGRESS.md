# LyricFever Windows 版 — 进度汇报（事实状态）

> 更新时间：2026-08-01（分支 `windows-build`）
> 技术栈：C# .NET 8 + WPF + CTranslate2/sentencepiece（原生 C++ DLL）

---

## 一、总体进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| P0 | 环境 + 解决方案骨架 + 托盘 + 设置窗口 | ✅ 完成（Release 0 错误 0 警告） |
| P1 | 歌词引擎（LRC 解析、匹配、同步、Provider、SQLite 缓存、语言检测） | ✅ 完成（46/46 单元测试通过） |
| P2 | Apple Music / Spotify 集成（SMTC、track 映射、DPAPI、WebView2 登录、主流程 ViewModel） | ⚠️ Apple Music 真实 E2E 已通过；Spotify 真实 E2E 仍未验收 |
| P3 | K 歌悬浮窗（置顶透明 NOACTIVATE、拖动吸附、当前歌词/音符占位、封面色） | ⚠️ 单显示器视觉验收通过；多显示器验收未做 |
| P4 | 翻译/罗马音管线 | ⚠️ Apple Music 真实日文歌词、罗马音和翻译 E2E 已通过；Spotify E2E 未验收 |
| P5 | 打包发布 | ✅ 431 MB portable 与 204,303,030 字节安装包已重新生成；安装/启动/单实例验证通过 |

---

## 2026-08-01 可用性与视觉复核

- 修复重复托盘/重复进程：命名互斥量保证单实例，第二次启动只唤醒已有歌词窗；安装版实测连续启动两次仍为 1 个进程。
- 播放来源改为 `AppleMusic` / `Spotify` / `Any` 可配置，默认 Apple Music；本机实际 AUMID `AppleInc.AppleMusicWin_nzyj5cx40ttqa!App` 已识别。
- Apple Music SMTC 会把 Artist/Album 合并为 `歌手 — 专辑`；现已仅在 Apple Music 路径拆分，`glory days (Movie Version) / Luna Haruna` 从 0 行恢复为 48 行，自动切歌到 `コイセカイ / ClariS` 获取 87 行。
- K 歌窗已对齐 Swift `KaraokeView`：`800×130`、当前一句、0.7 倍罗马音、0.82 倍翻译、无可显示歌词时单音符、16 px 圆角、50% 专辑色叠加暗色 HUD、右上角默认位置与拖动位置保存。
- 新增非敏感运行日志 `%APPDATA%\LyricFever\logs\app.log`，记录启动、SMTC 会话、规范化元数据、歌词行数与异常，不记录 cookie/token。
- `dotnet test` 46/46，Release 0 错误 0 警告，`git diff --check` 通过。

---

## 二、P0-A/P0-B/P0-C/P0-D 状态（执行指挥书）

### P0-A：ja→zh 模型 —— ✅ 已解决（2026-08-01）

- 根因：CTranslate2 `MarianMTLoader` 删除 `<pad>`（id 65000）的真实权重与词表项，旧脚本只把名称补回词表，造成 decoder 起始 embedding 错位，翻译退化为单 token/循环。
- 修复：`Windows/native/convert_ja_zh.py` 覆盖 loader（Bart 逻辑 + 保留 pad 权重/词表 + `<pad>` decoder start），转换后自动三句语义校验，失败非零退出。
- 验证：int8 与 float32 均输出正常中文（`君の声が聞こえる → 我听见你的声音` 等）；最终 int8 模型经 native DLL C API 探针 `load rc=0 / translate rc=0 / count=3`。
- 维护规则与详细记录：`Windows/JA_ZH_MODEL_ISSUE.md`。

### P0-B：翻译/罗马音管线 —— ✅ 实现完成（E2E 未验收）

- 按曲目 CancellationTokenSource + 任务版本校验（切歌/刷新丢弃旧结果）。
- 模型加载/批量推理/罗马音全部后台执行（Task.Run），UI 不阻塞。
- 原生调用串行化（SemaphoreSlim + 锁），空闲 5 分钟卸载与推理互斥。
- 产物缓存 ready 语义：只翻译/只罗马音互不遮蔽、失败不污染、模型版本变化失效（27+ 测试覆盖）。
- 关闭翻译不加载模型；只开罗马音不加载翻译模型。
- 网络请求 15s 超时 + 取消 token。

### P0-C：发布链路 —— ✅ portable 目录 + 安装包已验证

- `publish.ps1` 严格 manifest：任一必需文件缺失/为空即失败退出（实测：IpaDic 缺失时正确报错）；幂等（每次全量清理）。
- 输出 `Windows/publish/LyricFever/`（433MB）：exe + 托管依赖 + LyricFeverTranslation.dll + dnnl.dll + 双模型（en-zh/ja-zh 各 5 文件）+ IPAdic + model_manifest.json（SHA256）。
- 从 publish 目录启动冒烟通过；native 探针用 publish 产物 DLL + 模型验证：
  - en-zh：20 行批量 213ms，unload/reload 正常
  - ja-zh：20 行批量 96ms，unload/reload 正常
- **新发现并修复（发布阻断级）**：C# 侧 `lf_translate_batch` P/Invoke 使用 `ArraySubType=LPUTF8Str`（.NET 8 运行时非法，直接抛 MarshalDirectiveException）→ 翻译调用从未真正成功过；改为手动 `StringToCoTaskMemUTF8` 指针数组。
- **新发现并修复**：CTranslate2 3.24 + oneDNN 3.14 组合下，`intra_threads=2` 推理后析构（空闲卸载）死锁；已改为 `intra_threads=1`（探针实测稳定，负载更低，翻译速度仍远优于需求）。
- IpaDic 复制修复：csproj CopyIpaDic target 同时覆盖 Build 与 Publish。

### P0-D：Spotify 主链路 —— ✅ 代码修复完成（真实 E2E 未验收）

- SMTC session 过滤：`SpotifyOnly`（由 UseSpotify 驱动）只接受 Spotify AppId，无 Spotify session 时清空曲目状态并显示"未检测到 Spotify"。
- session generation 校验：`TryGetMediaPropertiesAsync` 异步返回后校验仍是当前 session。
- track 映射匹配校验：`SpotifyTrackMapper.MatchesRequest`（规范化歌名/歌手匹配），错误搜索结果不写入缓存，改走 LRCLIB/NetEase 兜底。
- 登录成功即时注入运行中的 Provider（无需重启）；手动粘贴 sp_dc 入口已实现（替代误导性文案）。
- 设置接线：UseSpotify → watcher 过滤；SourceLanguage → 语言检测覆盖；LaunchAtStartup → HKCU Run 注册表；删除无效 KaraokeBackgroundColor 与过期文案。
- 新增 16 个测试：匹配校验、缓存命中/拒绝、GraphQL JSON 解析、401/429 状态分类。

---

## 三、已解决的技术问题（记录备查）

| 问题 | 解决 |
|---|---|
| CMake 4.3 与旧项目（cpu_features 等）不兼容 | pip 安装 CMake 3.30.5 |
| CTranslate2 v4 移除句子级 API/分词器 | 使用 v3.24（token 级 API）+ 集成 sentencepiece v0.2.0 |
| int8 计算需要 oneDNN 后端 | 源码构建 oneDNN（产出 dnnl.dll 随包分发） |
| sentencepiece master 版依赖 abseil | 改用 v0.2.0（无需 abseil） |
| 静态库 /MT 与 DLL /MD 运行时库不匹配 | 统一 /MT（免分发 VC 运行库） |
| 中文注释导致 MSVC 编码错误 | `/utf-8` 编译选项 |
| Marian 源句缺 `</s>` 导致 en 翻译无限重复 | DLL 编码后追加 `</s>` |
| ja-zh 模型转换后翻译退化 | 见 P0-A（pad 权重/词表错位，convert_ja_zh.py 已修复） |
| **intra_threads=2 推理后析构死锁** | 改为 intra_threads=1（CT2 3.24 + oneDNN 3.14 组合问题，探针实测稳定） |
| IpaDic 未进入 publish 目录 | csproj CopyIpaDic target 同时覆盖 Build 与 Publish |
| hf-mirror 镜像连接不稳定 | 改用官方 huggingface.co |
| git 误提交第三方源码/大模型 | .gitignore 补全（CTranslate2/sentencepiece/models 等） |
| 旧版 LibNMeCab.IpaDicBin targets 不复制词典 | csproj 自定义 CopyIpaDic target |
| Microsoft.Data.Sqlite / SMTC / WebView2 等 API 细节 | 已逐一验证（SMTC 用同步 API） |

---

## 四、验证结果（已实际执行）

- `dotnet build LyricFever.Windows.sln -c Release`：0 错误 0 警告
- `dotnet test LyricFever.Windows.sln -c Release`：43/43 通过
- `publish.ps1`：完整资产时成功（433MB），缺 IpaDic 时失败退出，幂等可重跑
- publish 目录启动冒烟：进程存活（托盘常驻）
- native 探针（publish 产物 DLL + 模型）：en-zh/ja-zh load=0、translate=0、unload/reload 正常
- **管线集成探针**（真实 CT2 + Kawazu + SQLite，22 行英文 + 20 行日文真实歌词）：
  - 英译 423ms、日译+罗马音 442ms、语言切换重载 175ms
  - 空行占位正确、等长、日文罗马音语义正确
  - **缓存命中 4ms**（用户核心要求：命中不重新调用模型）
  - 任务取消抛 OperationCanceledException；过期任务结果丢弃
- 英文翻译（DLL 实测）：`I can't help falling in love with you → 我无法忍心爱上你`
- 日文翻译（CT2 验证）：`君の声が聞こえる → 我听见你的声音`
- 罗马音（DLL 实测）：`君の声が聞こえる → kimi no koe ga kikoeru`
- **安装包**（Inno Setup 6.7.3）：`publish/LyricFeverSetup-1.0.0.exe`（195MB，lzma2/ultra）
  - 静默安装 → 安装副本独立启动 OK → 静默卸载 → 目录清空 ✅
  - WebView2 Runtime 已装（150.0.4078.105）；缺失时登录窗口降级为手动粘贴 sp_dc
  - 清洁环境模型部署（bundled → %APPDATA% → 翻译成功）已验证
- git diff --check 通过

---

## 五、未完成 / 待验收（不得标注为完成）

1. **阶段 C/D 人工验收**：真实 Spotify E2E（登录→播放→歌词→翻译/罗马音→切歌 20 次→单曲循环 5 次）、K 歌窗口人工验收（多显示器/焦点/视觉/右键菜单）、托盘交互、设置项持久化。这些需要用户在真实环境中操作（本机无 Spotify 会话/凭据，无法自动化）。
2. **已知边界**：ja-zh 换源模型/工具链变更时必须重跑 convert_ja_zh.py 校验；安装包未代码签名（首次运行可能有 SmartScreen 提示，需用户确认）；intra=1 的翻译性能基准（20 行 <250ms，已满足低负载目标）。

---

## 六、备注

- 方案文档：[WINDOWS_PORT_PLAN.md](../WINDOWS_PORT_PLAN.md)
- 执行指挥书：[WINDOWS_NEXT_AGENT_EXECUTION_GUIDE.md](../WINDOWS_NEXT_AGENT_EXECUTION_GUIDE.md)
- ja-zh 修复记录：[JA_ZH_MODEL_ISSUE.md](JA_ZH_MODEL_ISSUE.md)
- 与 macOS 版共享的协议/逻辑（Spotify 歌词 API、HOTP、LRC 解析）已 1:1 移植
- 模型、DLL、oneDNN/CTranslate2/sentencepiece 源码、.tdebug 探针均不提交 Git（.gitignore 覆盖）
