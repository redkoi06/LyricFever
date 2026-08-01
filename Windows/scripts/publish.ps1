# LyricFever Windows 发布脚本（执行指挥书 P0-C：严格 manifest + 失败即退出）
# 用法: powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
# 产物: publish\LyricFever\ （自包含目录：exe + 托管依赖 + DLL + 模型 + IPAdic）

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\LyricFever"

# ---- 0. 清理旧输出（幂等发布，避免 Copy-Item 嵌套复制） ----
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

# ---- 1. dotnet publish ----
Write-Host "[1/5] dotnet publish (self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\LyricFever.Windows.App\LyricFever.Windows.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None `
    -o $out | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# ---- 2. 复制原生 DLL ----
Write-Host "[2/5] copy native DLLs..." -ForegroundColor Cyan
$nativeDir = Join-Path $root "native"
$dlls = @{
    "LyricFeverTranslation.dll" = (Join-Path $nativeDir "LyricFeverTranslation\build\Release\LyricFeverTranslation.dll")
    "dnnl.dll"                  = (Join-Path $nativeDir "oneDNN\build\src\Release\dnnl.dll")
}
foreach ($name in $dlls.Keys) {
    $src = $dlls[$name]
    if (-not (Test-Path $src)) { throw "Missing native DLL: $src (run native builds first)" }
    Copy-Item $src (Join-Path $out $name)
}

# ---- 3. 复制模型（必须含全部 5 个文件） ----
Write-Host "[3/5] copy translation models..." -ForegroundColor Cyan
$modelFiles = @("model.bin", "config.json", "shared_vocabulary.json", "source.spm", "target.spm")
foreach ($m in @("en-zh", "ja-zh")) {
    $src = Join-Path $nativeDir "models\$m"
    if (-not (Test-Path $src)) { throw "Model dir not found: $src (run convert scripts first)" }
    foreach ($f in $modelFiles) {
        $p = Join-Path $src $f
        if (-not (Test-Path $p) -or (Get-Item $p).Length -eq 0) { throw "Model file missing/empty: $p" }
    }
    Copy-Item $src (Join-Path $out "models\$m") -Recurse
}

# ---- 4. 生成模型清单（版本/来源/hash 供运行时与安装校验） ----
Write-Host "[4/5] generate model manifest..." -ForegroundColor Cyan
$manifest = @{}
foreach ($m in @("en-zh", "ja-zh")) {
    $dir = Join-Path $out "models\$m"
    $files = @{}
    foreach ($f in $modelFiles) {
        $p = Join-Path $dir $f
        $files[$f] = @{
            size = (Get-Item $p).Length
            sha256 = (Get-FileHash $p -Algorithm SHA256).Hash
        }
    }
    $manifest[$m] = @{ files = $files }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out "models\model_manifest.json") -Encoding UTF8

# ---- 5. 严格验证全部必需文件 ----
Write-Host "[5/5] verify manifest..." -ForegroundColor Cyan
$required = @(
    "LyricFever.exe",
    "LyricFeverTranslation.dll",
    "dnnl.dll",
    "Kawazu.dll",
    "models\model_manifest.json",
    "models\en-zh\model.bin", "models\en-zh\config.json", "models\en-zh\shared_vocabulary.json",
    "models\en-zh\source.spm", "models\en-zh\target.spm",
    "models\ja-zh\model.bin", "models\ja-zh\config.json", "models\ja-zh\shared_vocabulary.json",
    "models\ja-zh\source.spm", "models\ja-zh\target.spm",
    "IpaDic\char.bin", "IpaDic\matrix.bin", "IpaDic\sys.dic", "IpaDic\unk.dic"
)
$missing = @()
foreach ($f in $required) {
    $p = Join-Path $out $f
    if (-not (Test-Path $p)) { $missing += $f }
}
if ($missing.Count -gt 0) { throw "Missing required files: $($missing -join ', ')" }

$size = (Get-ChildItem $out -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Done. Output: {0} ({1:N0} MB)" -f $out, $size) -ForegroundColor Green
Write-Host "Manifest: $out\models\model_manifest.json" -ForegroundColor Green
