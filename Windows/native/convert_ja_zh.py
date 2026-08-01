"""ja-zh 模型专用转换脚本（OPUS-MT Marian → CTranslate2 INT8）。

根因修复（2026-08-01 实证）：
ja-zh 的 decoder_start_token_id 是 <pad>（65000），且 pad embedding 非零。默认
MarianMTLoader 会删除最后一行 pad 权重和 <pad> 词表项；旧脚本随后只把 <pad> 名称
补回词表，令起始 token 与权重矩阵错位，翻译退化为单 token/循环。默认的
start_from_zero_embedding=True 也不适用于该非零 pad embedding。

修复：覆盖 MarianMTLoader 的 config、decoder、model spec、vocabulary 行为：
- decoder_start_token = <pad>（Marian 的 decoder_start_token_id）
- 不设置 start_from_zero_embedding（使用真实 pad embedding 初始化解码）
- 不删除 pad 权重或 <pad> 词表项

用法：python convert_ja_zh.py [源模型目录] [输出目录] [--float32]
默认源：本脚本同级 models-src/ja-zh-lemmony；默认输出：本脚本同级 models/ja-zh。
默认转换为 INT8；--float32 仅用于排查和验证转换正确性。任何步骤失败均退出非零。
"""
import argparse
import io
import json
import os
import shutil
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

HERE = os.path.dirname(os.path.abspath(__file__))


def _fail(msg: str) -> None:
    print(f"[FAIL] {msg}")
    sys.exit(1)


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Convert and verify an OPUS-MT ja-zh Marian model for CTranslate2")
    parser.add_argument("source", nargs="?", default=os.path.join(HERE, "models-src", "ja-zh-lemmony"))
    parser.add_argument("output", nargs="?", default=os.path.join(HERE, "models", "ja-zh"))
    parser.add_argument("--float32", action="store_true", help="write an unquantized float32 model for diagnosis")
    return parser.parse_args()


def _verify_translation(model_dir: str, compute_type: str) -> None:
    """Fail conversion if CT2 immediately regresses to the historical ja-zh failure mode."""
    import ctranslate2
    import sentencepiece as spm

    source = spm.SentencePieceProcessor(model_file=os.path.join(model_dir, "source.spm"))
    target = spm.SentencePieceProcessor(model_file=os.path.join(model_dir, "target.spm"))
    translator = ctranslate2.Translator(
        model_dir, device="cpu", compute_type=compute_type, inter_threads=1, intra_threads=2
    )
    # Keep assertions semantic rather than byte-for-byte: minor decoder/library updates may
    # change wording, but must not return the prior single-token/repeated-token degeneration.
    cases = [
        ("君の声が聞こえる", ("我", "声音")),
        ("涙があふれる夜もあるけど", ("泪",)),
        ("明日の光を信じて歩こう", ("相信", "光")),
    ]
    for line, expected_fragments in cases:
        result = translator.translate_batch(
            [source.encode(line, out_type=str) + ["</s>"]], beam_size=1, max_decoding_length=128
        )[0]
        output = target.decode(result.hypotheses[0])
        print(json.dumps({"input": line, "output": output}, ensure_ascii=True))
        if not output or len(result.hypotheses[0]) <= 1:
            _fail(f"degenerate translation for {line!r}: {output!r}")
        if not all(fragment in output for fragment in expected_fragments):
            _fail(f"unexpected translation for {line!r}: {output!r}")


def main() -> None:
    args = _parse_args()
    model_src = os.path.abspath(args.source)
    model_out = os.path.abspath(args.output)
    quantization = None if args.float32 else "int8"
    compute_type = "float32" if args.float32 else "int8"
    import ctranslate2.converters as converters
    import ctranslate2.converters.transformers as tf

    # ---- 覆盖 Marian loader 的 zero-embedding / pad-removal 行为 ----
    def _patched_set_config(self, config, model, tokenizer):
        config.eos_token = tokenizer.eos_token
        config.unk_token = tokenizer.unk_token
        # Marian: decoder_start_token_id = pad_token_id（如 65000 → '<pad>'）
        config.decoder_start_token = tokenizer.convert_ids_to_tokens(model.config.decoder_start_token_id)

    def _patched_set_decoder(self, spec, decoder):
        # 跳过 MarianMTLoader.set_decoder（不设置 start_from_zero_embedding）。
        # 保留真实 pad embedding，确保 decoder_start_token_id=65000 的语义与 HF 一致。
        tf.BartLoader.set_decoder(self, spec, decoder)

    def _patched_get_model_spec(self, model):
        # MarianMTLoader.get_model_spec 在 Bart 逻辑之后无条件调用
        # _remove_pad_weights，把 id=65000 的真实 pad 权重裁掉。之前的脚本只把
        # "<pad>" 名称补回 shared_vocabulary，造成 vocab 有 65001 项、权重仅
        # 65000 行，decoder 从 <pad> 起始时读取到了错误的 embedding。
        # 这里完整复用 Bart 的模型规格构建，但不删除 pad 行。
        model.config.normalize_before = False
        model.config.normalize_embedding = False
        return tf.BartLoader.get_model_spec(self, model)

    def _patched_get_vocabulary(self, model, tokenizer):
        # 同理，保留 tokenizer 最后一项 <pad>，不要在转换后手工追加一个没有
        # 对应权重的词表项。
        return tf.BartLoader.get_vocabulary(self, model, tokenizer)

    tf.MarianMTLoader.set_config = _patched_set_config
    tf.MarianMTLoader.set_decoder = _patched_set_decoder
    tf.MarianMTLoader.get_model_spec = _patched_get_model_spec
    tf.MarianMTLoader.get_vocabulary = _patched_get_vocabulary

    # ---- 转换 ----
    print(f"[1/5] convert {model_src} -> {model_out} ({compute_type})")
    converter = converters.TransformersConverter(model_src)
    converter.convert(model_out, quantization=quantization, force=True)

    # ---- 校验产物 ----
    print("[2/5] verify conversion")
    cfg_path = os.path.join(model_out, "config.json")
    with open(cfg_path, encoding="utf-8") as f:
        cfg = json.load(f)
    if cfg.get("decoder_start_token") != "<pad>":
        _fail(f"decoder_start_token = {cfg.get('decoder_start_token')!r}, expected '<pad>'")
    vocab_path = os.path.join(model_out, "shared_vocabulary.json")
    with open(vocab_path, encoding="utf-8") as f:
        vocab = json.load(f)
    pad_id = vocab.index("<pad>") if "<pad>" in vocab else -1
    if pad_id != 65000:
        _fail(f"<pad> id = {pad_id}, expected 65000; refusing mismatched vocabulary/weights")

    # ---- 补齐分词文件 ----
    print("[3/5] copy spm files")
    for name in ("source.spm", "target.spm"):
        src = os.path.join(model_src, name)
        if not os.path.isfile(src):
            _fail(f"missing {src}")
        shutil.copy(src, os.path.join(model_out, name))

    # ---- 产物完整性 ----
    print("[4/5] verify model files")
    for name in ("model.bin", "config.json", "shared_vocabulary.json", "source.spm", "target.spm"):
        path = os.path.join(model_out, name)
        if not os.path.isfile(path) or os.path.getsize(path) == 0:
            _fail(f"missing or empty {path}")
    print("[5/5] smoke test CTranslate2 output")
    _verify_translation(model_out, compute_type)
    print("OK")


if __name__ == "__main__":
    main()
