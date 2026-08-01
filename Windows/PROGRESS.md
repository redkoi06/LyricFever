# LyricFever Windows 版 — 进度汇报

> 更新时间：2026-08-01（分支 `windows-build`）
> 技术栈：C# .NET 8 + WPF + CTranslate2/sentencepiece（原生 C++ DLL）

---

## 一、总体进度

| 阶段 | 内容 | 状态 |
|---|---|---|
| P0 | 环境（.NET 8.0.423 / VS Build Tools+MSVC / CMake 3.30）+ 解决方案骨架 + 托盘 + 设置窗口 | ✅ 完成 |
| P1 | 歌词引擎：LRC 解析、MetadataMatcher、Spotify/LRCLIB/NetEase Provider、SQLite 缓存、翻译产物缓存、歌词状态机、语言检测 | ✅ 完成（24/24 单元测试通过） |
| P2 | Spotify 集成：SMTC 监听与控制、GraphQL track ID 映射（含 DB 缓存）、DPAPI 凭据、WebView2 登录、主流程 ViewModel（防竞态） | ✅ 代码完成 |
| P3 | K 歌悬浮窗：置顶透明 + NOACTIVATE、手动拖动 + 边缘吸附、三层歌词排版逐行高亮、封面背景色提取 | ✅ 代码完成 |
| P4 | 翻译/罗马音管线 | ⚠️ 进行中（见问题一） |
| P5 | 打磨 + 打包发布 | ⏸ 未开始 |

**P4 细分状态**：
- ✅ 翻译管线 C# 侧（P/Invoke Provider、Kawazu 罗马音、PipelineService：产物缓存优先/整首批量/翻译罗马音并行/空闲 5 分钟自动卸载/任务版本校验）
- ✅ **en-zh 模型**：Helsinki-NLP/opus-mt-en-zh → CT2 INT8（78MB），翻译质量验证通过
- ✅ **引擎 DLL**：LyricFeverTranslation.dll（2.2MB）= CTranslate2 v3.24 + sentencepiece v0.2.0 + oneDNN（int8 后端），C 接口（load/translate_batch/unload）
- ✅ **罗马音**：Kawazu + LibNMeCab + IPADic 验证通过（`君の声が聞こえる → kimi no koe ga kikoeru`，与方案示例一致）
- ❌ **ja-zh 模型翻译退化**（当前唯一阻塞项，详见问题一）

---

## 二、当前问题（1 个阻塞项 + 已解决项清单）

### 问题一（阻塞）：ja-zh 模型经 CTranslate2 转换后翻译全部退化

**现象**：ja→zh 翻译输出单个退化 token（`。`/`我`/`同`），无法使用。

**已尝试的模型**（全部退化）：
| 模型 | 说明 |
|---|---|
| shun89/opus-mt-ja-zh | 用户指定模型，退化 |
| suirenka/opus-mt-ja-zh | 与 shun89 **md5 完全相同**（同源），退化 |
| lemmonyjiang/opus-mt-ja-zh-jav | 第三个候选，退化 |

**关键事实**：同一个 lemmonyjiang 模型用 **transformers PyTorch 管线翻译完全正常**（`君の声が聞こえる → 我听见你的声音`），**但 CTranslate2 转换产物全部退化** → 问题在 CT2 转换/推理链路，不在模型本身。

**已排查并排除的原因**：
1. ❌ 词表映射错位（shared_vocabulary 与 spm piece 完全匹配，逐条验证）
2. ❌ decoder_start_token 错误（Marian 的 decoder_start = pad_token_id(65000)，ct2 转换器写成 `</s>`(0)；已手动 patch 词表补 `<pad>` + 改 config，仍退化）
3. ❌ int8 量化问题（float32 同样退化）
4. ❌ 解码起点（target_prefix 实验无效）
5. ❌ 源/目标 spm 词表差异（ja-zh 的 source/target spm 逐 id 完全一致；en-zh 虽不同但翻译正常，证明解码词表路径正确）
6. ✅ en-zh 模型同一转换链路完全正常 → 差异特定于 ja-zh 模型本身

**下一步排查方向**：
1. 对比 en-zh / ja-zh 转换产物的 config 与权重结构差异（层数/词表大小/特殊 token 处理）
2. 尝试旧版 ct2 转换器（如 3.x）重转 ja-zh
3. 检查 lemmonyjiang 权重与 config 是否一致（'jav' 后缀模型可能为特殊工具链导出）
4. **备选方案**：若 OPUS-MT ja-zh 不可行，仅 ja→zh 改用 NLLB-200-600M（通用模型不退化，生硬但可用），en→zh 保持 OPUS-MT；架构（ITranslationProvider 抽象）已支持按语言切换引擎

### 已解决的技术问题（记录备查）

| 问题 | 解决 |
|---|---|
| CMake 4.3 与旧项目（cpu_features 等）不兼容 | pip 安装 CMake 3.30.5 |
| CTranslate2 v4 移除句子级 API/分词器 | 使用 v3.24（token 级 API）+ 集成 sentencepiece v0.2.0 |
| int8 计算需要 oneDNN 后端 | 源码构建 oneDNN（产出 dnnl.dll 随包分发） |
| sentencepiece master 版依赖 abseil（Windows 无权限建 symlink） | 改用 v0.2.0（无需 abseil） |
| 静态库 /MT 与 DLL /MD 运行时库不匹配 | 统一 /MT（顺带免分发 VC 运行库） |
| 中文注释导致 MSVC 编码错误 | `/utf-8` 编译选项 |
| Marian 源句缺 `</s>` 导致 en 翻译无限重复 | DLL 编码后追加 `</s>`（en-zh 已验证） |
| hf-mirror 镜像连接不稳定 | 改用官方 huggingface.co |
| git 误提交第三方源码/大模型 | .gitignore 补全（CTranslate2/sentencepiece/models 等） |
| 旧版 LibNMeCab.IpaDicBin targets 不复制词典 | csproj 自定义 CopyIpaDic target |
| Microsoft.Data.Sqlite / SMTC / WebView2 等 API 细节 | 已逐一验证（SMTC 用 TryGetMediaProperties 等同步 API） |

---

## 三、已完成模块明细

**代码规模**（Windows/ 目录，不含第三方）：
- `src/LyricFever.Core`：歌词引擎/Provider/缓存/同步（纯逻辑，可测试）
- `src/LyricFever.Windows.App`：WPF 应用（SMTC/托盘/K 歌窗口/登录/翻译管线）
- `tests/LyricFever.Core.Tests`：24 个单元测试（对照 macOS 测试移植 + 新增）
- `native/`：C++ DLL 源码 + 模型转换脚本 + 发布脚本

**模型产物**（native/models/，不提交 git）：
- en-zh：CT2 INT8，78MB（含 source/target.spm）✅
- ja-zh：CT2 INT8，80MB（当前不可用，见问题一）

**验证结果**：
- 英文歌词翻译（DLL 实测）：`I can't help falling in love with you → 我无法忍心爱上你` ✅
- 罗马音（DLL 实测）：`君の声が聞こえる → kimi no koe ga kikoeru` ✅
- 单元测试 24/24 ✅
- 应用启动冒烟（托盘常驻）✅

---

## 四、下一步计划

1. **解决 ja-zh 退化**（见问题一，优先排查转换差异；备选 NLLB 兜底）
2. P4e 端到端验证：登录 → 播放 → 歌词获取 → 翻译/罗马音 → 缓存命中 → 切歌校验
3. P5 打包：publish.ps1（已写好）→ 单目录发布（含 DLL/dnnl.dll/模型/IpaDic）→ 安装包
4. 全功能人工验证 + 提交

---

## 五、备注

- 方案文档：[WINDOWS_PORT_PLAN.md](../WINDOWS_PORT_PLAN.md)（含用户三项决策与 CTranslate2 定案）
- 与 macOS 版共享的协议/逻辑（Spotify 歌词 API、HOTP、LRC 解析）已 1:1 移植
- 待用户确认的功能默认已删除（Apple Music、全屏、快捷键等）
