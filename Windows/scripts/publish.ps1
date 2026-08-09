# LyricFever Windows release script.
# The release uses matched human translations only; do not package local MT artifacts.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\LyricFever"

# Clean the exact publish directory so retired files cannot survive an incremental publish.
if (Test-Path -LiteralPath $out) {
    Remove-Item -LiteralPath $out -Recurse -Force
}

Write-Host "[1/2] dotnet publish (self-contained)..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\LyricFever.Windows.App\LyricFever.Windows.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None `
    -o $out | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "[2/2] verify release files..." -ForegroundColor Cyan
$required = @(
    "LyricFever.exe",
    "Kawazu.dll",
    "Assets\Fonts\OFL.txt",
    "IpaDic\char.bin", "IpaDic\matrix.bin", "IpaDic\sys.dic", "IpaDic\unk.dic"
)
$missing = @()
foreach ($file in $required) {
    $path = Join-Path $out $file
    if (-not (Test-Path -LiteralPath $path)) { $missing += $file }
}
if ($missing.Count -gt 0) { throw "Missing required files: $($missing -join ', ')" }

$forbidden = @(
    "LyricFeverTranslation.dll", "dnnl.dll", "models",
    "Microsoft.Web.WebView2.Core.dll",
    "Microsoft.Web.WebView2.Wpf.dll",
    "Microsoft.Web.WebView2.WinForms.dll",
    "WebView2Loader.dll",
    "runtimes\win-x64\native\WebView2Loader.dll"
)
foreach ($item in $forbidden) {
    $path = Join-Path $out $item
    if (Test-Path -LiteralPath $path) {
        throw "Retired dependency leaked into release: $path"
    }
}

$size = (Get-ChildItem -LiteralPath $out -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("Done. Output: {0} ({1:N0} MB)" -f $out, $size) -ForegroundColor Green
