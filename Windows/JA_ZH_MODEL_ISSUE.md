# ja-zh 模型翻译退化问题 — 已解决记录

> 解决日期：2026-08-01  
> 范围：`Windows/native/convert_ja_zh.py` 产生的 CTranslate2 ja→zh 模型  
> 状态：**已解决（模型转换与 native 推理层）**。WPF 翻译管线、缓存、发布包和真实歌词端到端验收仍由后续工作处理。

---

## 1. 原始现象

`lemmonyjiang/opus-mt-ja-zh-jav` 的 HuggingFace/Transformers 推理正常，但旧 CTranslate2 产物会输出单个 token 或重复循环，不能用于日→中翻译。英文 `en-zh` 不受影响。

## 2. 根因

Marian 模型的 decoder 起始 token 是 `<pad>`（词表 id 65000），且该模型的 pad embedding **不是零向量**。

旧脚本虽然将 `decoder_start_token` 改为 `<pad>`，也避免了 `start_from_zero_embedding=True`，但仍保留了 CTranslate2 `MarianMTLoader` 的两处默认行为：

1. `get_model_spec()` 调用 `_remove_pad_weights()`，删掉 encoder embedding、decoder embedding 和 decoder projection 的最后一行权重；
2. `get_vocabulary()` 移除末尾 `<pad>`。

随后旧脚本又只在 `shared_vocabulary.json` 末尾补回 `<pad>`。结果是词表中的 `<pad>` id 指向不存在的权重行；decoder 从错误的起始 embedding 开始，造成退化输出。仅改 JSON 或只补词表不能修复该问题。

## 3. 修复内容

`Windows/native/convert_ja_zh.py` 现在对该 Marian 模型专门覆盖 loader 行为：

- 使用 `BartLoader.get_model_spec()`，保留真实的 pad 权重；
- 使用 `BartLoader.get_vocabulary()`，保留 `<pad>` 词表项；
- 使用 `BartLoader.set_decoder()`，避免把非零 pad embedding 强制置零；
- 将 `decoder_start_token` 设为 `<pad>`；
- 断言 `<pad>` 位于词表最后一项且 id 为 65000，不再事后伪造词表；
- 显式复制 `source.spm`、`target.spm`，并检查五个模型文件均非空；
- 支持 `python convert_ja_zh.py [source] [output] [--float32]`；默认生成 int8 模型；
- 每次转换完成后自动用三句日文运行 CTranslate2，并校验非空且包含语义锚点；失败即非零退出。

## 4. 已验证证据

源模型：`lemmonyjiang/opus-mt-ja-zh-jav`，revision `bf31b1b656b537127fb82b0c0e865c364db676f4`。

执行命令：

```powershell
python Windows\native\convert_ja_zh.py `
  Windows\native\models-src\ja-zh-lemmony `
  Windows\native\models\ja-zh

python Windows\native\convert_ja_zh.py `
  Windows\native\models-src\ja-zh-lemmony `
  Windows\native\models\ja-zh-float32 --float32
```

int8 与 float32 的自动检查都通过，关键输出一致：

```text
君の声が聞こえる -> 我听见你的声音
涙があふれる夜もあるけど -> 即使夜晚充满泪水
明日の光を信じて歩こう -> 相信明天的光,我们走下去
```

随后用现有 `Windows/.tdebug/` C# P/Invoke 探针调用实际 `LyricFeverTranslation.dll` 和最终 `models/ja-zh`：

```text
load rc=0
rc=0 count=3
```

该探针在 Windows 控制台代码页下会把中文显示为 `?`；语义断言由 Python/CTranslate2 自动检查完成，native 探针证明实际 C API 可加载并返回三条结果。探针在采样后已停止，float32 对照模型和探针日志均已删除；最终保留的是 `Windows/native/models/ja-zh` 的 int8 产物。

## 5. 后续 Agent 的维护规则

1. 不要恢复 `MarianMTLoader._remove_pad_weights()` 或 `get_vocabulary()` 的 pop-pad 行为；这会重新引入该 bug。
2. 任何更换源模型、CTranslate2/Transformers 版本或重建 native DLL 的变更，都必须重新运行上述 int8 和 float32 转换检查，再运行 C API 探针。
3. 模型目录必须同时包含 `model.bin`、`config.json`、`shared_vocabulary.json`、`source.spm`、`target.spm`；模型和源模型均受 Git 忽略，不能提交大型资产来替代可复现脚本。
4. 本问题不代表 P4 整体完成。下一优先级是让 `CTranslate2TranslationProvider` 在后台执行、可取消、不会污染 `TranslationCache`，再做真实日文歌词和 portable 发布目录验证。
