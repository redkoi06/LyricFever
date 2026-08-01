"""ja-zh 模型专用转换：修复 Marian decoder_start_token（pad token）转换错误。

背景：Marian 的 decoder_start_token_id = pad_token_id（65000）。ct2 转换器
（4.8.1）对 Marian 把 decoder_start_token 写成 '</s>'（eos）且丢弃 '<pad>'，
导致解码起点错误、短句翻译退化（输出 '。'）。

修复：转换后手动把 '<pad>' 追加进 shared_vocabulary.json（id 65000，与模型
embedding 行一致）并把 config.decoder_start_token 改为 '<pad>'。
"""
import io
import json
import shutil
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

import ctranslate2.converters as converters

MODEL_SRC = r"D:/Tools/LyricFever/Windows/native/models-src/ja-zh-lemmony"
MODEL_OUT = r"D:/Tools/LyricFever/Windows/native/models/ja-zh"


def main() -> None:
    print(f"[1/3] convert {MODEL_SRC} -> {MODEL_OUT} (int8)")
    converter = converters.TransformersConverter(MODEL_SRC)
    converter.convert(MODEL_OUT, quantization="int8", force=True)

    # ---- 修复 decoder_start_token ----
    vocab_path = f"{MODEL_OUT}/shared_vocabulary.json"
    with open(vocab_path, encoding="utf-8") as f:
        vocab = json.load(f)
    assert isinstance(vocab, list), "unexpected vocab format"
    if "<pad>" not in vocab:
        vocab.append("<pad>")
        with open(vocab_path, "w", encoding="utf-8") as f:
            json.dump(vocab, f, ensure_ascii=False)
        print(f"[2/3] appended '<pad>' at id {vocab.index('<pad>')} (vocab now {len(vocab)})")

    cfg_path = f"{MODEL_OUT}/config.json"
    with open(cfg_path, encoding="utf-8") as f:
        cfg = json.load(f)
    cfg["decoder_start_token"] = "<pad>"
    with open(cfg_path, "w", encoding="utf-8") as f:
        json.dump(cfg, f, indent=2, ensure_ascii=False)
    print(f"[2/3] decoder_start_token -> '<pad>'")

    # ---- 复制分词文件 ----
    for name in ("source.spm", "target.spm"):
        shutil.copy(f"{MODEL_SRC}/{name}", f"{MODEL_OUT}/{name}")
    print("[3/3] spm files copied")

    # 校验
    vocab2 = json.load(open(vocab_path, encoding="utf-8"))
    assert "<pad>" in vocab2, "pad missing"
    cfg2 = json.load(open(cfg_path, encoding="utf-8"))
    assert cfg2.get("decoder_start_token") == "<pad>", "decoder_start fix failed"
    print("OK")


if __name__ == "__main__":
    main()
