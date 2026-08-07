# Removes the retired local machine-translation toolchain and downloaded model data.
$ErrorActionPreference = "Stop"
$windowsRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$target = [System.IO.Path]::GetFullPath((Join-Path $windowsRoot "native"))

if (-not $target.StartsWith($windowsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove a path outside the Windows workspace: $target"
}

if ([System.IO.Directory]::Exists($target)) {
    Get-ChildItem -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue |
        ForEach-Object {
            if (-not ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
                $_.Attributes = [System.IO.FileAttributes]::Normal
            }
        }
    [System.IO.Directory]::Delete($target, $true)
}

if ([System.IO.Directory]::Exists($target)) {
    throw "Retired local MT directory still exists: $target"
}

Write-Host "Removed retired local MT directory: $target"
