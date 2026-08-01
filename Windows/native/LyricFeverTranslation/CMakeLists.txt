cmake_minimum_required(VERSION 3.21)
project(LyricFeverTranslation LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# CTranslate2 源码路径（与 LyricFeverTranslation 同级的 CTranslate2 目录）
set(CT2_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../CTranslate2")

add_library(LyricFeverTranslation SHARED translation.cpp)

# 源文件为 UTF-8（含中文注释），MSVC 无 BOM 时按 /utf-8 读取
# 静态 CRT（/MT）：与 ctranslate2.lib / sentencepiece.lib 一致，且免分发 VC 运行库
if(MSVC)
    set_target_properties(LyricFeverTranslation PROPERTIES
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(LyricFeverTranslation PRIVATE /utf-8)
endif()

target_include_directories(LyricFeverTranslation PRIVATE
    "${CT2_SOURCE_DIR}/include"
    "${CT2_SOURCE_DIR}/src"
)

# 链接 CTranslate2 静态库（由 CTranslate2 自己的 CMake 构建产出）
add_library(ctranslate2 STATIC IMPORTED)
set_target_properties(ctranslate2 PROPERTIES
    IMPORTED_LOCATION "${CT2_SOURCE_DIR}/build/Release/ctranslate2.lib"
    INTERFACE_INCLUDE_DIRECTORIES "${CT2_SOURCE_DIR}/include"
)

# cpu_features（CTranslate2 的 CPU 特性检测依赖）
add_library(cpu_features STATIC IMPORTED)
set_target_properties(cpu_features PROPERTIES
    IMPORTED_LOCATION "${CT2_SOURCE_DIR}/build/third_party/cpu_features/Release/cpu_features.lib"
)

# oneDNN（int8 推理后端，导入库；运行时需分发 dnnl.dll）
set(ONEDNN_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../oneDNN")
add_library(dnnl SHARED IMPORTED)
set_target_properties(dnnl PROPERTIES
    IMPORTED_IMPLIB "${ONEDNN_SOURCE_DIR}/build/src/Release/dnnl.lib"
    IMPORTED_LOCATION "${ONEDNN_SOURCE_DIR}/build/src/Release/dnnl.dll"
    INTERFACE_INCLUDE_DIRECTORIES "${ONEDNN_SOURCE_DIR}/include"
)

target_link_libraries(LyricFeverTranslation PRIVATE ctranslate2 cpu_features dnnl)

# sentencepiece（分词器，由 sentencepiece 自己的 CMake 构建产出）
set(SPM_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../sentencepiece")
add_library(sentencepiece STATIC IMPORTED)
set_target_properties(sentencepiece PROPERTIES
    IMPORTED_LOCATION "${SPM_SOURCE_DIR}/build/src/Release/sentencepiece.lib"
    INTERFACE_INCLUDE_DIRECTORIES "${SPM_SOURCE_DIR}/src"
)

target_link_libraries(LyricFeverTranslation PRIVATE ctranslate2 sentencepiece)
