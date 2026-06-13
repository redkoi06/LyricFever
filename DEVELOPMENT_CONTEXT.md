# Lyric Fever 开发上下文

最后更新：2026-06-12

本文档记录本机维护版 Lyric Fever 的必要背景、已完成修复、开发流程和用户偏好。后续开发应先阅读本文档，避免重复调查或破坏当前可用版本。

## 项目与安装位置

- 源码目录：`/Users/lin/Project/LyricFever`
- 当前安装应用：`/Applications/Lyric Fever.app`
- Xcode 工程：`Lyric Fever.xcodeproj`
- Scheme：`SpotifyLyricsInMenubar`
- Bundle ID：`com.aviwadhwa.SpotifyLyricsInMenubar`
- 应用版本：`3.3`
- 当前构建架构：Apple Silicon `arm64`
- 原始上游仓库：`https://github.com/aviwad/LyricFever.git`
- 用户自己的仓库：`git@github.com:redkoi06/Lyric.git`

本地 `origin` 仍指向原作者仓库。不要擅自修改 Git remote、全局或本地 `user.name` / `user.email`。向用户仓库推送时使用完整 SSH 地址：

```bash
git push git@github.com:redkoi06/Lyric.git main:main
```

提交时如本机未配置作者，可仅对单次命令使用环境变量，不写入 Git 配置：

```bash
GIT_AUTHOR_NAME='redkoi06' \
GIT_AUTHOR_EMAIL='redkoi06@users.noreply.github.com' \
GIT_COMMITTER_NAME='redkoi06' \
GIT_COMMITTER_EMAIL='redkoi06@users.noreply.github.com' \
git commit -m 'Commit message'
```

## 用户开发偏好

- 始终使用简体中文沟通。
- 优先做小而稳的修复，不重构整个项目，不新增复杂依赖。
- 修改后应完成 Release 编译、签名验证、替换 `/Applications` 中的应用并启动检查。
- 不要只给方案；在可行时直接完成实现、验证和安装。
- 不要擅自修改用户 Git 配置或 remote。
- 不要在磁盘留下多份 App、旧备份或大型 `DerivedData`。
- 当前正式 App 可正常使用时，清理构建产物前必须确保 `/Applications/Lyric Fever.app` 已复制并验证。
- CoreData 已用于离线歌词缓存，歌词问题应优先修复现有缓存和状态恢复路径，不重复实现缓存。
- Apple Music 与 Spotify 逻辑应明确隔离，修 Spotify 时不要破坏 Apple Music。

## 已完成修复

### Spotify 切歌后歌词不显示

提交：`38226b0 Fix Spotify lyric sync recovery`

主要修改位于 `LyricFever/ViewModel.swift`：

- Spotify playback notification 到达时记录并校验 track ID、名称、歌手、position 和播放状态。
- notification 信息不完整时使用 ScriptingBridge 当前值作为补充。
- track ID 变化时停止旧 updater，清空歌词、索引、loading/empty 状态并重新 fetch。
- 新增有限次数空歌词重试，先查 CoreData，再访问远程 provider。
- 新增 Spotify watchdog，纠正 Spotify 实际曲目与内部 `currentlyPlaying` 不一致。
- 同一 track ID 的 position 从结尾跳回开头时重启 updater，支持单曲循环。
- 异步歌词结果返回时重新校验 track ID，防止旧歌曲结果覆盖新歌曲。
- 到达最后一句时不再错误清空 `currentlyPlaying`。

相关日志前缀：

```text
[LyricFever][SpotifySync]
```

### 专辑封面偶发不显示

提交：`3278ebc Fix album artwork refresh`

- Apple Music MediaRemote 回调不再强依赖 `applicationName == "Music"`，因为不同 macOS 版本可能返回不同名称。
- Spotify 封面获取支持短延迟重试，处理切歌瞬间 `artworkUrl` 尚未稳定。
- 封面异步返回前校验当前 track ID，防止旧封面覆盖新歌曲。
- Spotify notification、watchdog 和 SwiftUI task 共用统一封面刷新逻辑。

相关日志前缀：

```text
[LyricFever][Artwork]
```

### 刷新歌词导致闪退

提交：`e6fbcc2 Harden lyric state updates against crashes`

2026-06-10 的两份崩溃报告具有相同调用栈：

```text
refreshLyrics
-> setNewLyricsColorTranslationRomanizationAndStartUpdater
-> startLyricUpdater
-> upcomingIndex
```

根因是刷新后歌词数组长度变化，但旧的 `currentlyPlayingLyricsIndex` 仍被用于访问新数组，触发 Swift 数组越界和 `SIGTRAP`。

修复内容：

- 安装新歌词前停止旧 updater，并清空 index、翻译、罗马音和中文转换数组。
- `upcomingIndex()` 检测非法 index 后自动清空并重新定位。
- updater 防御负数、NaN 或无穷时间差，避免转换为 `UInt64` 时崩溃。
- 菜单栏歌词和 Karaoke 歌词改用安全下标。
- 非法歌词时间戳返回解码错误，不再强制解包终止 App。
- CoreData store 加载失败改为记录错误，不再无条件 `fatalError`。

历史崩溃报告曾位于：

```text
~/Library/Logs/DiagnosticReports/Retired/Lyric Fever-2026-06-10-193746.ips
~/Library/Logs/DiagnosticReports/Retired/Lyric Fever-2026-06-10-233436.ips
```

### 罗马音注音与全屏切歌定位

主要修改位于：

```text
LyricFever/Services/RomanizerService/RomanizerService.swift
LyricFever/ViewModel.swift
LyricFever/Views/FullscreenView/LyricsNSScrollView.swift
LyricFever/Views/KaraokeView/KaraokeView.swift
```

- 日语歌词使用 Mecab/IPADic 分词后生成带词间空格的罗马音，标点附着到前词。
- 整首歌词复用一个 tokenizer，并在后台任务中生成；切歌时取消旧任务并校验 track ID。
- `romanizedLyrics` 必须始终与 `currentlyPlayingLyrics` 等长，缺少注音的行使用空字符串占位，禁止使用 `compactMap`。
- 全屏模式固定显示“原文、罗马音、翻译”三层结构，三层作为同一歌词单元参与高亮、模糊与滚动。
- Karaoke 可通过 `karaokeShowRomanization` 独立控制是否在原文下显示罗马音。
- 全屏 AppKit 列表的延迟滚动回调必须校验 `updateRevision`，旧歌曲回调不得消费新歌曲的首次定位状态。
- `currentlyPlayingLyricsIndex` 超出新歌词数组范围时应按 `nil` 处理，避免全部歌词被误判为过去行并变为透明。
- Apple Music 单曲循环不会改变 persistent ID，必须由 watchdog 检测播放位置回绕，清空旧的末句索引并重启歌词 updater。
- 同一首歌回绕导致歌词索引从末句重置为 `nil` 时，全屏 AppKit 列表也必须重新预定位到首句，不能保留末尾滚动位置。
- Karaoke 悬浮窗在播放期间不能因当前歌词索引为空而关闭；索引无效、空白歌词或纯音乐符占位行统一显示 `music.note` 图标，避免空框或窗口消失。

## 已知行为与暂未修改项

- 菜单窗口中的“大小”滑块并不是 Karaoke/歌词弹幕字体大小。
- 它当前绑定 `UserDefaultStorage.truncationLength`，控制菜单栏歌词或歌名的截断长度，取值为 30、40、50、60。
- 用户已明确表示暂时不修改这一行为。
- Karaoke 字体大小实际来自 `ViewModel.karaokeFont.pointSize`，可在 Karaoke 设置中的字体选择器调整。

## 数据流概览

Spotify：

```text
Spotify notification / watchdog
-> currentlyPlaying / metadata
-> onCurrentlyPlayingIDChange
-> fetch(for:)
-> fetchLyrics
-> CoreData
-> remote lyric providers
-> currentlyPlayingLyrics
-> startLyricUpdater
-> menu bar / Karaoke / fullscreen UI
```

Apple Music：

```text
Apple Music notification / MediaRemoteAdapter
-> persistent ID or alternative ID
-> Spotify ID mapping/search
-> shared lyric fetch path
-> MediaRemote artwork callback
-> UI
```

歌词数组、当前索引和派生文本数组应视为一组状态：

```text
currentlyPlayingLyrics
currentlyPlayingLyricsIndex
translatedLyric
romanizedLyrics
chineseConversionLyrics
```

替换歌词时必须先停止 updater 并重置整组状态。任何 UI 下标访问都应校验数组边界。

## Release 构建

Xcode 首次构建可能需要下载或重编 Swift Package，耗时数分钟。Metal Toolchain 缺失时可执行：

```bash
xcodebuild -downloadComponent MetalToolchain
```

构建命令：

```bash
cd /Users/lin/Project/LyricFever

xcodebuild -quiet \
  -project "Lyric Fever.xcodeproj" \
  -scheme SpotifyLyricsInMenubar \
  -configuration Release \
  -derivedDataPath "$PWD/../DerivedData" \
  -skipPackagePluginValidation \
  -skipMacroValidation \
  CODE_SIGNING_ALLOWED=NO \
  ONLY_ACTIVE_ARCH=YES \
  ARCHS=arm64 \
  build
```

产物：

```text
/Users/lin/Project/DerivedData/Build/Products/Release/Lyric Fever.app
```

项目当前存在一些原有 Swift 6 concurrency 和 deprecated API warning。只要 `xcodebuild` 退出码为 0，可视为构建成功；新增修改不应引入新的 error。

## 本机签名

本机没有原作者 Developer ID，因此维护版使用 ad-hoc 签名。不要声称它已 notarized。

```bash
cd /Users/lin/Project/LyricFever

app="/Users/lin/Project/DerivedData/Build/Products/Release/Lyric Fever.app"
entitlements="$(mktemp /tmp/lyricfever-entitlements.XXXXXX)"

sed 's/$(PRODUCT_BUNDLE_IDENTIFIER)/com.aviwadhwa.SpotifyLyricsInMenubar/g' \
  "LyricFever/Support Files/LyricFever.entitlements" > "$entitlements"

codesign --force --deep --sign - \
  --entitlements "$entitlements" \
  "$app"

codesign --verify --deep --strict --verbose=2 "$app"
rm -f "$entitlements"
```

由于签名身份与官方版不同，macOS 可能要求重新授予 Spotify/Apple Music 自动化权限。

## 安全替换应用

不要保留长期 App 备份副本，否则 macOS 存储管理会将其识别为重复应用。使用临时 staging 路径验证后原位替换：

```bash
osascript -e \
  'tell application id "com.aviwadhwa.SpotifyLyricsInMenubar" to quit' || true
sleep 2

source_app="/Users/lin/Project/DerivedData/Build/Products/Release/Lyric Fever.app"
staged_app="/Applications/Lyric Fever.new.app"

rm -rf "$staged_app"
ditto "$source_app" "$staged_app"
codesign --verify --deep --strict --verbose=2 "$staged_app"

rm -rf "/Applications/Lyric Fever.app"
mv "$staged_app" "/Applications/Lyric Fever.app"
xattr -dr com.apple.quarantine "/Applications/Lyric Fever.app" 2>/dev/null || true

codesign --verify --deep --strict --verbose=2 "/Applications/Lyric Fever.app"
open "/Applications/Lyric Fever.app"
```

启动检查：

```bash
pgrep -fl '/Applications/Lyric Fever.app/Contents/MacOS/Lyric Fever|SpotifyLyricsInMenubar|Lyric Fever'
```

## 清理构建缓存

确认正式 App 已替换、签名通过并成功启动后，删除构建缓存，避免约 2 GB 空间占用和系统显示重复 App：

```bash
rm -rf /Users/lin/Project/DerivedData
```

确认系统只索引到一个 App：

```bash
mdfind 'kMDItemContentType == "com.apple.application-bundle" && (kMDItemFSName == "Lyric Fever.app" || kMDItemFSName == "Lyric Fever*.app*")'
```

预期只返回：

```text
/Applications/Lyric Fever.app
```

## 手动验证清单

每次涉及播放、歌词或异步状态的修改，至少验证：

1. Spotify 连续手动切歌 20 次。
2. Spotify playlist 自动播放下一首。
3. Spotify 单曲循环 5 次。
4. 网络较慢时连续切歌。
5. 已缓存歌词歌曲之间切换。
6. 未缓存歌词歌曲之间切换。
7. 播放中连续点击“刷新歌词”。
8. 刷新歌词期间切歌、暂停、恢复和拖动进度。
9. Apple Music 切歌、封面和歌词显示。
10. Karaoke、菜单栏歌词和全屏歌词切换。
11. 应用持续运行一段时间后检查崩溃报告：

```bash
find "$HOME/Library/Logs/DiagnosticReports" \
  "$HOME/Library/Logs/DiagnosticReports/Retired" \
  -maxdepth 1 -type f \
  \( -iname '*Lyric*' -o -iname '*SpotifyLyrics*' \) \
  -print
```

## 开发完成标准

一次修改只有满足以下条件才算完成：

- `git diff --check` 通过。
- Release 构建退出码为 0。
- 新 App 完成 ad-hoc 签名并通过 `codesign --verify --deep --strict`。
- `/Applications/Lyric Fever.app` 已替换并成功启动。
- 没有留下 `Lyric Fever.new.app`、备份 App 或 `DerivedData`。
- 只提交本次相关源码，不提交构建产物、签名临时文件或用户数据。
- 推送到 `git@github.com:redkoi06/Lyric.git`。
