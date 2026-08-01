# LyricFever Windows 发布脚本
# 用法: powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
# 产物: publish\LyricFever\ （自包含目录，含模型与原生 DLL）

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\LyricFever"

Write-Host "[1/4] dotnet publish (self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\LyricFever.Windows.App\LyricFever.Windows.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None `
    -o $out | Out-Null

Write-Host "[2/4] copy LyricFeverTranslation.dll..." -ForegroundColor Cyan
$dll = Join-Path $root "native\LyricFeverTranslation\build\Release\LyricFeverTranslation.dll"
if (-not (Test-Path $dll)) { throw "DLL not found: $dll (run native build first)" }
Copy-Item $dll $out

Write-Host "[3/4] copy translation models (en-zh, ja-zh)..." -ForegroundColor Cyan
foreach ($m in @("en-zh", "ja-zh")) {
    $src = Join-Path $root "native\models\$m"
    if (-not (Test-Path $src)) { throw "Model not found: $src (run native\convert_models.py first)" }
    Copy-Item $src (Join-Path $out "models\$m") -Recurse
}

Write-Host "[4/4] verify..." -ForegroundColor Cyan
$files = @("LyricFever.exe", "LyricFeverTranslation.dll",
           "models\en-zh\model.bin", "models\en-zh\source.spm",
           "models\ja-zh\model.bin", "models\ja-zh\source.spm",
           "IpaDic\char.bin", "Kawazu.dll")
foreach ($f in $files) {
    $p = Join-Path $out $f
    if (-not (Test-Path $p)) { Write-Warning "MISSING: $f" }
}
$size = (Get-ChildItem $out -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Done. Output: $out (${size:N0} MB)" -ForegroundColor Green
