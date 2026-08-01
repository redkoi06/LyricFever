// LyricFeverTranslation.dll
// CTranslate2 + sentencepiece 封装：C 接口，供 C# P/Invoke 调用。
// 运行配置（用户定案）：CPU、INT8、inter_threads=1、intra_threads=2、beam_size=1（贪心）。
//
// 模型目录约定（CT2 目录 + 分词文件）：
//   {model_dir}/model.bin  config.json  shared_vocabulary.json
//   {model_dir}/source.spm  {model_dir}/target.spm
#include <cstring>
#include <cstdlib>
#include <memory>
#include <string>
#include <vector>
#include <mutex>

#include <ctranslate2/translator.h>
#include <sentencepiece_processor.h>

namespace {

std::mutex g_mutex;
std::unique_ptr<ctranslate2::Translator> g_translator;
std::unique_ptr<sentencepiece::SentencePieceProcessor> g_source_spm;
std::unique_ptr<sentencepiece::SentencePieceProcessor> g_target_spm;

}  // namespace

extern "C" {

// 加载模型与分词器。model_path 为 CT2 模型目录（内含 source.spm/target.spm）。返回 0 成功。
__declspec(dllexport) int lf_load_model(const char* model_path,
                                        int inter_threads,
                                        int intra_threads) {
    std::lock_guard<std::mutex> lock(g_mutex);
    // 错误码：1=模型加载失败，2=source.spm 失败，3=target.spm 失败，4=异常
    try {
        std::string dir(model_path ? model_path : "");
        if (dir.empty()) return 1;

        ctranslate2::ReplicaPoolConfig config;
        config.num_threads_per_replica = intra_threads > 0 ? intra_threads : 2;
        g_translator = std::make_unique<ctranslate2::Translator>(
            dir, ctranslate2::Device::CPU, ctranslate2::ComputeType::INT8,
            std::vector<int>{0}, config);

        auto source = std::make_unique<sentencepiece::SentencePieceProcessor>();
        auto target = std::make_unique<sentencepiece::SentencePieceProcessor>();
        if (!source->Load(dir + "/source.spm").ok()) return 2;
        if (!target->Load(dir + "/target.spm").ok()) return 3;
        g_source_spm = std::move(source);
        g_target_spm = std::move(target);
        return 0;
    } catch (const std::exception& e) {
        fprintf(stderr, "[LyricFeverTranslation] load error: %s\n", e.what());
        g_translator.reset();
        g_source_spm.reset();
        g_target_spm.reset();
        return 4;
    }
}

// 批量翻译（自动分词）。source_lang/target_lang 保留参数（单语对模型无需语言前缀）。
// 成功返回 0，输出 out_lines（char**，UTF-8，需用 lf_free_lines 释放）。
__declspec(dllexport) int lf_translate_batch(const char** lines,
                                             int count,
                                             const char* /*source_lang*/,
                                             const char* /*target_lang*/,
                                             char*** out_lines,
                                             int* out_count) {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_translator || !g_source_spm || !g_target_spm || count <= 0 || !lines) return 1;
    try {
        std::vector<std::vector<std::string>> batches;
        batches.reserve(count);
        for (int i = 0; i < count; ++i) {
            std::vector<std::string> pieces;
            auto status = g_source_spm->Encode(lines[i] ? lines[i] : "", &pieces);
            if (!status.ok()) return 3;
            // Marian 源句需要显式 </s> 结束符（模型 add_source_eos=false，须自行追加）
            pieces.emplace_back("</s>");
            batches.emplace_back(std::move(pieces));
        }

        ctranslate2::TranslationOptions options;
        options.beam_size = 1;  // 贪心解码（用户定案）
        options.max_input_length = 256;
        options.max_decoding_length = 512;

        auto results = g_translator->translate_batch(batches, options);

        auto** output = static_cast<char**>(std::calloc(count, sizeof(char*)));
        for (int i = 0; i < count; ++i) {
            const auto& hypotheses = results[i].hypotheses;
            std::string text;
            if (!hypotheses.empty()) {
                std::string decoded;
                g_target_spm->Decode(hypotheses.front(), &decoded);
                text = std::move(decoded);
            }
            auto* buf = static_cast<char*>(std::malloc(text.size() + 1));
            std::memcpy(buf, text.c_str(), text.size());
            buf[text.size()] = '\0';
            output[i] = buf;
        }
        *out_lines = output;
        *out_count = count;
        return 0;
    } catch (const std::exception&) {
        return 2;
    }
}

// 释放 lf_translate_batch 的输出。
__declspec(dllexport) void lf_free_lines(char** lines, int count) {
    if (!lines) return;
    for (int i = 0; i < count; ++i) {
        if (lines[i]) std::free(lines[i]);
    }
    std::free(lines);
}

// 卸载模型与分词器，释放内存（空闲卸载策略）。
__declspec(dllexport) void lf_unload_model() {
    std::lock_guard<std::mutex> lock(g_mutex);
    g_translator.reset();
    g_source_spm.reset();
    g_target_spm.reset();
}

}  // extern "C"
