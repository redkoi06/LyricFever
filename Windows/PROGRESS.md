# LyricFever Windows 移植状态

> 最后更新：2026-08-07
> 技术栈：C#、.NET 8、WPF、Windows SMTC、SQLite、Kawazu/MeCab

## 当前结论

Windows 版已具备 Apple Music / Spotify 播放监听、歌词抓取与缓存、罗马音、人工译词、悬浮字幕、托盘和设置界面。当前发布策略明确禁止机器翻译：只有在曲目匹配可靠且平台人工译词覆盖率达到 60% 时才显示中文；没有合格人工译词时只显示原文和用户启用的罗马音。

## 2026-08-07 收敛项

### 人工译词

- 淘汰并移除原先基于成人语料微调的 ja→zh 模型、CTranslate2 原生运行库、转换脚本、发布模型和本地下载数据。
- 网易云歌词源改用 `music.163.com` 官方域名网页接口，不再依赖已失效的第三方 Vercel 代理。
- 搜索结果必须通过曲名以及歌手/专辑校验；支持 Apple Music 带角色列表的长歌手名。
- 平台原文和译词按时间轴对齐，允许把平台拆开的两句合并到当前歌词的一行，禁止按数组下标硬配。
- 人工译词至少覆盖 60% 的有效歌词行才会被采用；不足时整首不显示中文翻译。
- 译词缓存策略版本已升级，启动时物理删除所有旧版本缓存；同时删除 `%APPDATA%\LyricFever\models`。
- 《ふわふわ時間》在线验收通过，目标行得到“抱着心爱的小兔子 今晚也进入梦中”。

### Apple Music 稳定性

- 同一个 SMTC session 的重复通知不再反复解绑、清空歌词和重启请求。
- session 短暂消失时保留当前歌词 3 秒，watchdog 确认持续丢失后才清空。
- Apple Music 先返回空标题时按 250ms / 750ms / 1500ms 重试；2 秒 watchdog 补偿漏发的元数据事件。
- 所有异步元数据结果校验 session 引用与 revision，旧 session 不得覆盖新曲目。
- SMTC 位置结合 `LastUpdatedTime` 和播放速率连续外推，避免时间轴事件稀疏时字幕停住。
- 空歌词抓取进行有限重试；同一曲目的 watchdog 与事件不会互相取消造成饥饿。
- 若首次重试后仍为空，watchdog 会按 10 / 30 / 60 秒退避持续自愈；已有歌词时不重复请求。
- 字幕窗口仅在媒体 session 确实处于播放状态时显示；暂停、停止或 session 消失时隐藏，恢复播放后再显示。

### 悬浮字幕与图标

- 字幕卡根据原文、罗马音和人工译词的真实 WPF 排版动态计算宽高；各可见层严格保持单行，最多三行。
- 字号 12–48 调整时外框同步缩放；极端长句只受工作区安全宽度限制，不拆成两行。
- 宽度始终以拖动后位置的水平中轴为锚点，左右对称伸缩；仅在屏幕边缘为保证可见性纠偏。
- 拖动采用固定屏幕坐标与 DPI 换算，超过系统拖动阈值后才移动；拖动中冻结尺寸变化，松开后一次应用，避免抽搐。
- 鼠标悬停和普通单击没有视觉响应；右键菜单和按住拖动仍可用。
- 卡片改为单层圆角着色，移除叠层造成的黑色边缘；过亮专辑色自动压暗，背景不透明度下限为 78%。
- EXE、安装器、快捷方式和系统托盘统一使用 macOS `origin/main` 的原版绿色双人音符 AppIcon。

## 发布与验证

```powershell
dotnet test Windows\tests\LyricFever.Core.Tests\LyricFever.Core.Tests.csproj -c Release
& Windows\scripts\publish.ps1
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" Windows\scripts\installer.iss
```

- `publish.ps1` 会拒绝任何 `models`、`LyricFeverTranslation.dll` 或 `dnnl.dll` 混入发布包。
- 安装器升级时也会清理安装目录中遗留的上述文件。
- 受网络控制的《ふわふわ時間》验收测试可通过设置 `LYRICFEVER_RUN_NETWORK_TEST=1` 启用。
- 当前仍未做代码签名，首次运行可能出现 SmartScreen 提示。
