# LyricFever Windows 版后续执行指挥书

> 评估基线：2026-08-01，仓库 D:\Tools\LyricFever，分支 windows-build。
>
> 受令对象：接手本仓库的下一个 Agent。
>
> 本文的目标不是重复 WINDOWS_PORT_PLAN.md，而是把当前真实完成度、已经证实的问题、执行顺序和验收门槛固定下来。除非本文明确写“已验证”，否则不要把 PROGRESS.md 中的“代码完成”理解为“功能完成”或“可发布”。

---

## 0. 先执行的总命令

本项目当前的正确总判断是：

> Windows 端已经有可编译的 .NET 8 + WPF 原型，P0/P1 的主要骨架和一部分 P2/P3/P4 代码已经存在。2026-08-01 已修复并验证 ja→zh 的模型转换与 native C API；但 P2/P3 没有真实端到端验收，P4 的异步管线、缓存、真实歌词和性能验收未完成，P5 尚未完成。因此现在不能声称 Windows 版已完成，也不能制作“可用发布包”交付。

接手后必须遵守以下总原则：

1. 保持已确认的产品范围：K 歌悬浮窗、Spotify、歌词源兜底、SQLite 歌词缓存、本地英/日→中翻译、日语罗马音、托盘和精简设置。
2. 不重新加入 Apple Music、全屏、状态栏歌词、全局快捷键、手动歌词搜索、自动更新、多语言 UI 等已经删除或默认删除的功能。
3. 先修复“发布资产链路”和“异步状态正确性”，再做视觉打磨。native 日→中模型已可用，但切歌竞态、缓存和发布链路未闭环前不要做安装包。
4. 所有涉及歌词、翻译、罗马音的异步结果都必须带当前曲目/任务版本校验，并且旧任务要真正取消，而不是只在结果返回后丢弃。
5. 不使用 git reset --hard、git checkout --、git clean -fd 等破坏性命令。当前工作树有用户/前序 Agent 的未提交内容，先读取并保留；只提交本次明确修复的文件。
6. 不把模型、DLL、oneDNN/CTranslate2/sentencepiece 第三方源码、bin/obj、临时探针和用户凭据提交到 Git。发布物通过脚本生成，不通过把大文件硬塞进仓库解决问题。
7. 不为了让进度表变绿而静默换模型或降低验收标准。若最终不能在资源约束下得到可用 ja→zh，必须明确记录阻塞原因和备选方案，不能把退化输出标成“可用”。

---

## 1. 现状基线与证据

### 1.1 Git 和仓库状态

当前基线信息：

- 分支：windows-build。
- 当前 HEAD：2894d54 Add SMTC playback, Spotify login, track mapping, karaoke window (P2-P3)。
- 工作树不是干净的。当前修改包括 `Windows/native/convert_ja_zh.py`、`Windows/scripts/publish.ps1`、`CTranslate2TranslationProvider.cs`、`ModelInstallService.cs`；未跟踪文档包括本指挥书和 `Windows/JA_ZH_MODEL_ISSUE.md`。这些内容尚未提交，接手时先阅读差异并保留。
- Windows/native 目录中混有第三方源码、构建目录、DLL 和模型目录；不能直接 git add Windows/native。
- Windows/.tdebug 是临时原生探针目录，不是产品源码。

接手第一步要重新运行：

~~~powershell
git status --short --branch
git diff --stat
git diff --check
git ls-files Windows
~~~

不要假设前序 Agent 的未跟踪文件都应该提交；逐个判断是否是产品源码、可复现脚本、验证工具还是构建产物。

### 1.2 已实际运行的验证

本次评估已运行以下命令，结果应作为接手时的参考基线：

~~~powershell
dotnet test Windows\LyricFever.Windows.sln -c Release --no-restore --verbosity minimal
~~~

结果：27/27 个 Core 测试通过，0 失败。

~~~powershell
dotnet build Windows\src\LyricFever.Windows.App\LyricFever.Windows.App.csproj -c Release --no-restore --verbosity minimal
~~~

结果：0 错误、0 警告。

应用 Release 可执行文件也做过启动冒烟：进程能启动并在测试后退出。但这只证明 WPF 启动路径没有立即崩溃，不证明托盘交互、SMTC、Spotify 登录、歌词、翻译或窗口视觉行为正确。

### 1.3 当前模型资产的事实（2026-08-01 更新）

`Windows/native/models/ja-zh` 现已包含且通过非零检查：

- model.bin
- config.json
- shared_vocabulary.json
- source.spm
- target.spm

模型转换层的根因已修复：旧转换器删除了 `<pad>` 的三组真实权重，却只把 `<pad>` 补回词表；Marian decoder 因此从错位的起始 embedding 解码并退化。当前 `convert_ja_zh.py` 保留 pad 权重和词表、设置 `<pad>` decoder start、避免 zero embedding，并在每次转换后自动做三句语义检查。

已实际验证：默认 int8 与 `--float32` 都输出正常中文；最终 int8 产物经 `LyricFeverTranslation.dll` 的 C API 探针得到 `lf_load_model == 0`、`lf_translate_batch == 0`、`count == 3`。详情见 `Windows/JA_ZH_MODEL_ISSUE.md`。模型与源模型均为 Git 忽略资产，后续变更工具链或模型时必须重新生成，不得仅修改 JSON。

### 1.4 当前发布链路的事实

当前 Release build 输出目录只包含托盘/WPF/托管依赖和 IPAdic，不包含：

- LyricFeverTranslation.dll
- dnnl.dll
- models/en-zh
- models/ja-zh

这些内容依赖 Windows/scripts/publish.ps1 另外复制。工作树中有一组**尚未执行 publish 验收**的 P0-C 修改：脚本已尝试复制 `dnnl.dll`、校验两个模型的五个必需文件、生成 SHA-256 manifest，并在缺项时 `throw`；`ModelInstallService` 也改为检查必需文件和临时目录部署。

这些变更尚不能当作 P0-C 已完成，原因是：

- 尚未在完整资产条件下实际运行 publish，也没有故意缺文件的负向测试；
- 尚未在脱离仓库的 portable 目录启动并加载两种模型；
- `ModelInstallService` 当前只做文件存在/非零检查，虽可读取 manifest hash，但未在部署时逐项比对 hash；
- WebView2 Runtime 的清洁机器前置条件仍未验证。

---

## 2. 完成度评估

评价时必须把“源码存在”“本机编译通过”“真实功能通过”“干净机器可发布”分开。

| 阶段 | PROGRESS.md 声称 | 现场可证实情况 | 当前结论 |
|---|---|---|---|
| P0 | 完成 | Solution、Core/App/Tests 项目、托盘和设置框架存在；Release 可编译；应用可启动冒烟 | 代码骨架完成，仍需清理编译警告和设置行为 |
| P1 | 完成，27/27 测试通过 | LRC、匹配、同步、SQLite、语言检测和缓存有 27 个单测并通过 | Core 逻辑基本完成；Provider 网络、并发和真实数据仍未验收 |
| P2 | 代码完成 | SMTC、登录窗口、track 映射和 ViewModel 主链路存在，但无真实 Spotify 端到端证据；登录后 cookie 不会立即注入运行中的 Provider | 实现存在，功能未接受 |
| P3 | 代码完成 | K 歌窗口、拖动、置顶、三层行模型和背景色算法存在；无多显示器/焦点/视觉/切歌人工验收，设置修改也不保证立即应用 | 原型完成，人工验收未开始 |
| P4 | 进行中 | 英文模型和 C# 管线代码存在；ja→zh 模型转换、int8/float32 语义检查及 native C API 已验证；管线仍是同步阻塞式实现，取消、缓存、真实歌词和性能测试不足 | 模型阻塞已解除；P4 集成与验收仍未完成 |
| P5 | 未开始 | 有 publish.ps1 草稿；无可验证 portable 发布目录、安装包、清洁环境安装/运行记录 | 未开始 |

### 总体评级

当前版本应标为：

> Windows 内部原型 / 集成开发阶段

不能标为：

> 可交付 Beta、可发布 Release、P4 完成或“英日翻译均可用”。

---

## 3. 必须优先处理的问题

下面的 P0 是交付阻塞，不是普通优化。除非有新的实测证据证明问题已经消失，否则按此顺序执行。

### P0-A：已解决 — ja→zh 模型资产（仅维护门槛）

涉及：

- Windows/native/convert_models.py
- Windows/native/convert_ja_zh.py
- Windows/native/models/ja-zh/
- Windows/native/LyricFeverTranslation/translation.cpp
- Windows/.tdebug/（仅作临时探针，不提交）

已完成事实：`Windows/native/convert_ja_zh.py` 现保留 Marian 模型非零 `<pad>` 权重和 `<pad>` 词表，而不是在删权重后伪造词表。脚本已用 `lemmonyjiang/opus-mt-ja-zh-jav` revision `bf31b1b656b537127fb82b0c0e865c364db676f4` 重建最终 int8 模型，并自动验证 int8/float32 的三句语义输出；最终 int8 模型还经 `LyricFeverTranslation.dll` 探针验证 `load rc=0`、`translate rc=0`、`count=3`。

维护要求：

1. 变更源模型、CTranslate2、Transformers、SentencePiece 或 native DLL 时，先重新生成 int8 和 float32，再运行同一 C API 探针；禁止只改 config JSON。
2. 模型目录必须同时有 `model.bin`、`config.json`、`shared_vocabulary.json`、`source.spm`、`target.spm`，缺任一项即作为发布失败处理。
3. 生成脚本必须保持命令行输入/输出参数和失败即非零的语义检查。不得恢复 `MarianMTLoader._remove_pad_weights()` 或 pop-pad 逻辑。
4. 详细根因、命令和实际输出见 `Windows/JA_ZH_MODEL_ISSUE.md`；模型资产与源模型仍不可提交到 Git。

剩余边界：这只解除“模型转换/原生加载/退化输出”阻塞，尚未证明 WPF 管线在真实歌词中不会阻塞 UI、缓存空结果或在切歌后写回旧数据。

### P0-B：修复翻译/罗马音管线的 UI 阻塞和取消语义

涉及：

- Windows/src/LyricFever.Windows.App/Services/Translation/CTranslate2TranslationProvider.cs
- Windows/src/LyricFever.Windows.App/Services/Translation/KawazuRomanizationProvider.cs
- Windows/src/LyricFever.Windows.App/Services/Translation/TranslationPipelineService.cs
- Windows/src/LyricFever.Windows.App/ViewModels/MainViewModel.cs

当前问题：

- P/Invoke 翻译方法虽然返回 Task，但实际同步执行，没有 await；CPU 推理可能发生在 WPF UI 线程。
- KawazuRomanizationProvider.RomanizeAsync 逐行同步执行，并且每行新建一个 KawazuConverter。
- 切歌时只增加 _taskVersion，没有统一取消旧的网络/翻译/罗马音任务；旧任务仍可能持续占用 CPU/网络。
- 空闲卸载和翻译调用需要统一串行化，不能出现模型已被卸载但托管状态仍认为已加载的情况。

执行要求：

1. 在 ViewModel 为每个当前曲目维护 CancellationTokenSource；新曲目、刷新歌词、退出应用时取消并释放旧 CTS。
2. 将 token 传入 track mapping、歌词 provider、翻译和罗马音管线；网络请求设置明确超时。
3. 让模型加载、批量推理、罗马音生成在后台执行，不阻塞 Dispatcher。可以使用 Task.Run 包裹纯同步 native/Kawazu 调用，但要明确 token 检查点；不要把 UI 更新放进后台线程。
4. 对 MediaSessionWatcher、ViewModel 和窗口事件统一使用 UI Dispatcher 更新 WPF 状态。异步返回后同时校验任务版本和 track ID。
5. 原生 DLL 调用必须在一个可控的串行队列或锁内执行；卸载定时器不能在翻译中间破坏状态。
6. 关闭翻译时不得加载模型；只开启罗马音时不得加载翻译模型；只开启翻译时不得要求日语罗马音模型。
7. 删除或修复 CS1998/CS4014 警告。保留未等待调用时，必须说明其生命周期和异常处理方式，不能用警告掩盖竞态。

完成门槛：

- 连续切换歌曲时 UI 仍可拖动、打开设置和响应托盘。
- 切歌后旧歌曲的译文/罗马音不会覆盖新歌。
- 取消旧任务后，native 模型和网络请求不会无限继续。
- 关闭翻译时进程不加载翻译模型，且无持续 CPU 工作。

### P0-C：补齐模型、DLL 和发布依赖链

涉及：

- Windows/scripts/publish.ps1
- Windows/src/LyricFever.Windows.App/Services/Translation/ModelInstallService.cs
- Windows/src/LyricFever.Windows.App/LyricFever.Windows.App.csproj
- Windows/native/LyricFeverTranslation/CMakeLists.txt

执行要求：

1. 发布脚本必须复制并验证：
   - LyricFever.exe 及托管依赖
   - LyricFeverTranslation.dll
   - dnnl.dll
   - models/en-zh/{model.bin,config.json,shared_vocabulary.json,source.spm,target.spm}
   - models/ja-zh/{model.bin,config.json,shared_vocabulary.json,source.spm,target.spm}
   - IpaDic 全部必要文件
2. 将所有必需文件列入严格 manifest。任一文件缺失、大小为零、hash 不匹配或 DLL 依赖缺失都必须 throw/退出非零，不得只 Write-Warning。
3. 复制模型到 %APPDATA% 时，先复制到临时目录，校验完整后再原子替换；目标目录存在但不完整时不能直接返回成功。
4. IsModelDownloaded 和 EnsureDeployed 必须检查 manifest/必需文件，而不是只检查目录存在。
5. 给模型缓存定义明确的版本和来源。模型升级后旧产物缓存必须失效。
6. 发布脚本创建 $out\models 等父目录，并在脚本结束后输出每个关键文件的路径、大小和 hash。
7. 验证 LyricFeverTranslation.dll 的运行时依赖。当前 CMake 明确链接 oneDNN，发布脚本漏复制 dnnl.dll，必须修复并在 clean 目录中实测。
8. 记录 WebView2 Runtime 是安装包前置条件还是随安装包部署。没有 WebView2 Runtime 的机器上，登录窗口必须给出可理解提示，而不是静默失败。
9. 模型和 native 第三方源码继续保持仓库外构建/发布策略；补充下载、版本和构建说明，不要提交当前工作站的大型目录。

完成门槛：

- 从干净的 publish\LyricFever 目录启动，不依赖 Windows/native 或 .tdebug 中的 DLL/模型。
- 关闭/改名原始工作目录后，英文模型可加载，验证脚本能明确指出日文模型是否可用。
- 发布脚本在缺文件时失败，在完整资产时成功。

### P0-D：修复 Spotify 登录和播放器状态主链路

涉及：

- Windows/src/LyricFever.Windows.App/Views/SpotifyLoginWindow.xaml.cs
- Windows/src/LyricFever.Windows.App/Services/MediaSessionWatcher.cs
- Windows/src/LyricFever.Windows.App/ViewModels/MainViewModel.cs
- Windows/src/LyricFever.Windows.App/Services/TrayIconService.cs
- Windows/src/LyricFever.Core/Providers/Spotify/SpotifyTrackMapper.cs

当前风险：

- 登录窗口把 sp_dc 写入 DPAPI，但没有把新 cookie 注入已经运行的 SpotifyLyricProvider，登录后通常要重启程序才能真正使用。
- 登录异常提示“可手动粘贴 Cookie”，但界面没有手动粘贴入口。
- PlaybackStateChanged 事件没有接到 MainViewModel.IsPlaying；UseSpotify 设置没有真正控制监听器。
- watcher 读取当前系统 SMTC session，但没有证明它只选择 Spotify；其他播放器可能被当成当前曲目。
- current session 变成 null、session 切换或异步读取跨 session 返回时，没有完整清理旧歌词/任务的证据。
- SpotifyTrackMapper 按搜索结果第一首直接缓存，没有用歌名、歌手、专辑的规范化匹配校验，可能把错误曲目永久缓存。

执行要求：

1. 登录成功后通过显式事件/服务回调更新运行中的 Provider、ViewModel 登录状态并清除旧 token；登出/401 时清除状态。
2. 要么实现真正的手动 sp_dc 输入并安全保存，要么删除误导性提示。不得保留不存在的功能文案。
3. 明确 Spotify session 选择策略：优先 Spotify AppId；无 Spotify session 时显示“未检测到 Spotify”，不把任意浏览器/其他播放器默认为 Spotify。
4. 对每个 session 使用 generation/token；异步 TryGetMediaPropertiesAsync 返回后确认仍是当前 session。
5. 将 SMTC 回调安全转发到 UI Dispatcher；处理 session 消失时清空当前歌曲、歌词、译文、罗马音和索引。
6. 使用现有 MetadataMatcher 或新建规范化匹配逻辑，至少校验 title 和主 artist；结果不匹配时不要写入 SpotifyTrackMap，改走 LRCLIB/NetEase。
7. 为 Spotify track JSON 解析、匹配拒绝、缓存命中和 401/429 增加单测；真实网络测试不要把 Cookie 写进日志或测试输出。

完成门槛：

- 真实 Spotify 播放/暂停/切歌能触发正确曲目。
- 登录后无需重启即可获取 Spotify 歌词。
- 播放器退出或切换到其他播放器不会继续显示旧歌词。
- 错误搜索结果不会进入映射缓存。

---

## 4. 已发现但可在 P0 后处理的代码问题

这些问题不应被忽略；如果修复 P0 时触及相同文件，应一并处理。

### 4.1 ViewModel 切歌、刷新和派生数组

当前 MainViewModel 已在切歌时重置歌词/译文/罗马音，这是正确方向，但仍需补齐：

- RefreshLyrics() 重新获取歌词后没有完整更新 _currentLyrics、翻译/罗马音和任务取消状态；刷新后的旧派生结果可能与新歌词不一致。
- 刷新应与切歌使用同一套“停止旧任务 → 清空状态 → 获取 → 校验版本 → 设置整组状态 → 重新处理产物”的逻辑。
- TranslatedLyrics、RomanizedLyrics 必须始终与当前歌词等长，空行用空字符串占位。
- 任何窗口访问歌词数组都必须安全下标；UI 不能因索引暂时为 null 或超界而崩溃。

### 4.2 TranslationCache 的缓存污染

当前缓存键含 track、歌词 hash、语言和版本，但没有记录“本次是否实际生成了翻译/罗马音”。这会导致：

- 先只开启翻译，再开启罗马音时，旧缓存可能命中但罗马音仍全为空。
- 翻译失败后 TranslationPipelineService 仍可能把空译文写入缓存；模型修复后同版本缓存会继续命中空结果。
- 罗马音逐行失败时也可能把空结果当成成功产物缓存。

建议方案：

1. 为缓存条目加入 translationReady 和 romanizationReady，或把两类产物拆成独立缓存。
2. 读取缓存时按当前开关要求检查对应产物是否 ready；缺哪一类就只重建哪一类。
3. 失败结果不得覆盖已有有效产物，也不得把未完成任务写成 ready。
4. 为上述场景补充测试：首次只翻译、首次只罗马音、翻译失败后重试、模型版本变化、歌词 hash 变化。

### 4.3 设置项和界面文案必须与真实行为一致

当前 SettingsWindow.xaml 和 AppSettings 中存在未接线或范围不一致的设置：

- UseSpotify 被显示但 watcher 没有读取。
- SourceLanguage 可选 en/ja，但 ViewModel 始终自动检测，没有使用这个值。
- LaunchAtStartup 可编辑但没有注册表/启动任务实现；计划中该功能默认删除，应删除控件或实现后再显示。
- KaraokeBackgroundColor 存在但没有实际使用。
- Spotify 状态文案仍写着“未登录（P2 实现）”，即使已有登录代码，属于过期文案。
- 计划要求的模型管理/下载状态没有真正的 UI；当前只在运行时尝试从安装目录复制。
- 托盘菜单计划中提到的翻译/罗马音开关没有实现；要么补上，要么修订计划和文案，不能两边都声称已完成。

处理原则：每个可见控件必须满足“读到当前值、修改能影响运行时、保存后下次启动恢复”；否则删除该控件并更新计划。

### 4.4 背景色和存储

- LyricFetchService 会保存 ColorData，但 MainViewModel 没有消费缓存颜色；当前背景色主要依赖 SMTC 缩略图重新提取。
- LyricsRepository.GetColor() 使用 ExecuteScalar() as int?，需要确认 Microsoft.Data.Sqlite 返回的整数类型，避免因 boxed long 导致缓存读取永远为空。
- 如果决定保留 Spotify 返回色缓存，应在下一次播放时实际应用，并测试无封面/封面读取失败的降级颜色。
- 如果决定只使用 SMTC artwork，应删掉无效的颜色表/字段或明确它的用途，避免两条逻辑半完成。

### 4.5 解析和网络稳健性

- 时间戳解析应使用 CultureInfo.InvariantCulture，避免 Windows 用户区域设置影响小数解析。
- HttpClient 应设置合理超时，并保留取消 token。
- Provider 兜底必须记录可诊断的非敏感错误；不能把所有失败都静默成空窗口。
- Spotify 私有接口、远程 secret、GraphQL persisted query 都是外部漂移风险；必须保留 LRCLIB/NetEase 兜底并测试无 Cookie 场景。
- 任何日志都不得输出 sp_dc、Authorization header 或完整 Cookie。

---

## 5. 分阶段执行顺序

不要跳阶段。每阶段结束先达到门槛，再进入下一阶段。

### 阶段 A：冻结基线和修正进度表

1. 重新读取 WINDOWS_PORT_PLAN.md、Windows/PROGRESS.md、本指挥书。
2. 保存 git status、当前 commit、dotnet SDK、CMake/MSVC、native DLL/model 文件清单。
3. 将 PROGRESS.md 改成事实状态：P2/P3 标记为“代码存在，E2E 未验收”，P4 标记为“ja→zh native 模型已验证，管线/E2E 未验收”，P5 标记为“未开始”。
4. 不删除 .tdebug、native 构建目录或模型，除非先确认它们不是唯一验证证据；但不要把它们提交。
5. 后续每次“完成”都必须附命令、输出摘要、日期和实际资产路径。

### 阶段 B：先修 Core 和状态机

1. 为 ViewModel 加入按曲目取消的 CTS 和统一的任务版本/generation。
2. 修复 RefreshLyrics() 与切歌共用状态重置和派生产物处理。
3. 修复 TranslationCache 的 ready 状态和失败污染。
4. 增加 Provider JSON、metadata match、缓存和取消测试。
5. 修复时间戳 invariant culture、网络超时、SQLite 颜色类型等低层确定性问题。
6. 重新运行 Core 全量测试，目标不低于现有 27/27，且新增测试全部通过。

### 阶段 C：修 P2 SMTC/Spotify

1. 先不依赖模型，使用已知歌词/缓存验证曲目事件、位置事件、循环回绕和切歌取消。
2. 接通登录成功回调、Provider cookie、401 状态和 UI 状态。
3. 确认 Spotify session 过滤和 session 消失清理。
4. 确认 track mapping 的规范化匹配和缓存失效策略。
5. 用真实 Spotify 做最小手动验证；敏感凭据只留在本机 DPAPI，不写日志、不提交。

### 阶段 D：修 P3 K 歌窗口

1. 验证窗口初次打开、隐藏、重新打开时能显示当前歌词。
2. 验证原文、罗马音、译文三层顺序和数组等长。
3. 验证当前行高亮、首句定位、循环回绕、无歌词占位和空白行。
4. 验证 NOACTIVATE 不抢播放器焦点，拖动/边缘吸附和右键菜单可用。
5. 使用真实多显示器环境验证吸附区域；不能只依赖 SystemParameters.WorkArea 的主屏行为。
6. 设置修改后立即刷新窗口字体、透明度、背景色和偏移，或者明确要求重新打开窗口并让文案一致。

### 阶段 E：完成 P4 管线接入与验收

1. 保留 P0-A 的转换自检；仅在模型或工具链变化时重跑转换和 C API 探针。
2. 优先完成 P0-B 的后台/取消/缓存语义。
3. 英文和日文各用真实歌词批量测试 20–60 行，验证输出顺序、空行和任务取消。
4. 验证翻译失败时罗马音仍能显示；罗马音失败时翻译仍能显示。
5. 同一曲目第二次播放必须命中缓存，不加载模型；模型版本变化必须重建。
6. 记录模型加载耗时、单曲处理耗时、峰值内存和 CPU；确认符合低负载目标，不用“代码存在”代替基准测试。

### 阶段 F：发布和清洁环境

1. 修复并运行 Windows/scripts/publish.ps1，使其对缺失资产严格失败。
2. 生成单独 portable 目录；验证应用不依赖仓库 native 路径、.tdebug 或开发机 NuGet 缓存。
3. 在没有用户模型缓存的环境中首次启用英译、日译和罗马音，分别验证模型部署和错误提示。
4. 验证 WebView2 Runtime 缺失/存在两种情况，给出明确安装前置条件。
5. 再决定是否增加安装包。没有清洁机器安装证据时，只交付“已验证 portable 目录”，不要声称安装包完成。
6. 最后才处理图标、版本信息、README、第三方许可证和发布压缩包。

---

## 6. 必须覆盖的端到端测试矩阵

至少记录下表每一项的结果。失败时记录复现步骤、日志前缀、当前曲目和是否可恢复。

### 播放器和歌词

1. Spotify 未登录、无播放会话：应用能启动，窗口显示清楚的未检测到状态。
2. Spotify 登录后播放英文歌：SMTC 读到标题/歌手/专辑/进度，歌词从缓存或 Provider 链显示。
3. Spotify 登录后播放日文歌：歌词显示，罗马音独立生成；翻译成功或明确降级。
4. 连续切歌至少 20 次，慢网络下连续切歌，旧歌词/旧译文不能覆盖新歌。
5. 播放列表自动下一首、暂停/恢复、拖动进度。
6. 单曲循环至少 5 次，位置从结尾回到开头后当前索引和滚动位置回到首句。
7. 先缓存歌词再切换到未缓存歌词，再切回缓存歌词。
8. 播放中连续刷新歌词；刷新期间切歌、暂停、恢复、拖动进度。
9. Spotify 401/429、无 sp_dc、网络断开时，LRCLIB/NetEase 兜底和错误提示可诊断。
10. 关闭 Spotify 或切换到其他 SMTC 播放器后，旧歌词被清除或明确显示未检测到 Spotify。

### 翻译、罗马音和缓存

1. 翻译关闭：启动和播放不加载 CT2 模型。
2. 只开日语罗马音：不加载翻译模型，整首歌词后台生成，结果等长。
3. 只开英译：加载 en-zh，批量输出不退化。
4. 只开日译：加载 ja-zh，三句探针和真实歌词均不退化。
5. 翻译中切歌：旧任务被取消/丢弃，不阻塞 UI。
6. 翻译失败后修复模型：下一次应重试，不应被空结果缓存永久遮蔽。
7. 同曲目第二次播放：缓存命中，不重新加载模型。
8. 修改模型版本：缓存失效并重建。
9. 切换翻译/罗马音开关：只补齐缺失产物，不错误复用空数组。
10. 空行、纯音乐占位行、非法/缺少时间戳的歌词不会导致数组越界。

### K 歌和设置

1. 托盘左键显示/隐藏窗口，右键菜单每一项与实际行为一致。
2. 窗口置顶、透明、NOACTIVATE、拖动、边缘吸附。
3. 多显示器和 DPI 缩放下窗口不跑出工作区。
4. 原文/罗马音/译文三层排版、当前行高亮、滚动定位。
5. 无歌词、纯音乐符、当前索引为空时窗口不崩溃、不出现空框。
6. 设置保存后重启仍恢复；设置项不会出现“能改但不起作用”。
7. 退出应用后托盘图标、watcher、translation timer 和窗口都释放。

---

## 7. 自动化验收命令

完成代码后至少运行：

~~~powershell
dotnet restore Windows\LyricFever.Windows.sln
dotnet test Windows\LyricFever.Windows.sln -c Release
dotnet build Windows\src\LyricFever.Windows.App\LyricFever.Windows.App.csproj -c Release
git diff --check
~~~

native 侧必须另行运行：

~~~powershell
cmake --build Windows\native\LyricFeverTranslation\build --config Release
~~~

然后用临时探针测试：

~~~text
lf_load_model(en-zh) == 0
lf_load_model(ja-zh) == 0
lf_translate_batch(en examples) 输出非空且不重复退化
lf_translate_batch(ja examples) 输出非空且不重复退化
lf_free_lines 和 lf_unload_model 完成后进程无崩溃
~~~

发布前再运行：

~~~powershell
powershell -ExecutionPolicy Bypass -File Windows\scripts\publish.ps1
~~~

发布脚本必须在完整资产时返回 0，在删除任意一个关键文件时返回非 0。不能用“脚本打印了 warning”作为通过标准。

---

## 8. 交付前最终门槛

只有全部满足以下条件，才可以把状态改为“完成”或制作正式发布包：

- P0-A：已完成模型资产、自动语义检查和原生 C API 探针；真实歌词验收归入 P4，工具链变更后必须重跑该门槛。
- P0-B：翻译/罗马音不阻塞 UI；切歌、刷新、退出可以取消旧任务。
- P0-C：portable 包自包含 DLL、oneDNN、模型、IPAdic 和托管依赖，缺项严格失败。
- P0-D：Spotify 登录、SMTC 选择、曲目映射、歌词获取和兜底链真实走通。
- P1：Core 单测和新增并发/缓存/匹配测试全部通过。
- P2/P3：完成真实 Spotify、托盘、窗口、多显示器和设置人工验收，并留下结果。
- P4：英/日翻译、罗马音、产物缓存、模型卸载和低负载行为都有证据。
- P5：从干净发布目录启动，不依赖开发工作区；安装/运行前置条件已写清楚。
- Release build 0 error；现有 4 个异步警告已修复或有明确、可接受、不会吞异常的理由。
- git diff --check 通过。
- Git 中只包含本次相关源码、测试、脚本和文档；不包含模型、DLL、bin/obj、native 第三方源码、Cookie、token、数据库或临时探针。
- PROGRESS.md 与真实证据一致，不再把“代码存在”写成“功能完成”。

如果只完成了其中一部分，交付时必须使用“完成到哪一门、哪一门阻塞、下一步是什么”的状态，不得给出笼统的“Windows 移植完成”。

---

## 9. 建议的最终汇报格式

下一个 Agent 完成一轮工作后，按下面结构汇报，方便继续接手：

~~~text
当前结论：原型 / 可内部测试 / 可发布（只能选一个，并说明依据）

已完成：
- 文件和行为
- 对应命令与结果

仍阻塞：
- 问题
- 最小复现
- 是否需要产品决策

验证：
- dotnet test：
- Release build：
- native probe：
- Spotify E2E：
- K 歌人工验证：
- publish/clean environment：

未提交/未纳入发布的资产：
- 模型、DLL、第三方源码、临时文件

下一步：
- 明确到文件、命令和验收标准
~~~
