"""一次性模型转换：OPUS-MT (Marian) → CTranslate2 INT8。

用法：
    HF_ENDPOINT=https://hf-mirror.com python convert_models.py

产物：
    native/models/en-zh/  （Helsinki-NLP/opus-mt-en-zh，INT8）
    native/models/ja-zh/  （shun89/opus-mt-ja-zh，INT8；失败则回退 Helsinki-NLP/opus-mt-ja-zh）
"""
import os
import shutil
import subprocess
import sys

OUTPUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "models")
SRC_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "models-src")

CONFIGS = [
    ("en-zh", "Helsinki-NLP/opus-mt-en-zh"),
    ("ja-zh", "shun89/opus-mt-ja-zh"),
]

# ja-zh 备选模型（shun89 fork 不可用时的官方 Helsinki 版本）
FALLBACKS = {"ja-zh": "Helsinki-NLP/opus-mt-ja-zh"}


def convert(name: str, model: str) -> bool:
    out = os.path.join(OUTPUT_DIR, name)
    if os.path.isdir(os.path.join(out, "model.bin")):
        print(f"[skip] {name} already converted")
        copy_spm(name, out)
        return True
    print(f"[convert] {model} -> {out} (int8)")
    rc = subprocess.run(
        [
            sys.executable, "-m", "ctranslate2.converters.transformers",
            "--model", model,
            "--output_dir", out,
            "--quantization", "int8",
            "--force",
        ],
        env={**os.environ, "HF_HUB_DISABLE_SYMLINKS_WARNING": "1"},
    ).returncode
    if rc == 0:
        copy_spm(name, out)
    return rc == 0


def copy_spm(name: str, out: str) -> None:
    """CT2 模型目录补齐 sentencepiece 模型文件（分词需要 source.spm/target.spm）。"""
    src = os.path.join(SRC_DIR, name)
    for f in ("source.spm", "target.spm"):
        if os.path.exists(os.path.join(src, f)):
            shutil.copy(os.path.join(src, f), os.path.join(out, f))
            print(f"[spm] copied {name}/{f}")
    # 同时也从原模型仓库兜底（本地已下载则跳过）
    if not os.path.exists(os.path.join(out, "source.spm")):
        print(f"[spm] WARNING: {name}/source.spm missing")


def main() -> None:
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    for name, model in CONFIGS:
        if not convert(name, model) and name in FALLBACKS:
            print(f"[fallback] trying {FALLBACKS[name]}")
            convert(name, FALLBACKS[name])


if __name__ == "__main__":
    main()
