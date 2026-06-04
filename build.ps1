# Sandbox Timeline build script
param(
    [switch]$Installer
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "SandboxTimeline.csproj"
$publishDir = Join-Path $root "bin\Release\net8.0-windows\win-x64\publish"

Write-Host "Restoring packages..."
dotnet restore $project

Write-Host "Publishing Release win-x64..."
dotnet publish $project -c Release -r win-x64 `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --self-contained false `
    -o $publishDir

Write-Host "Published to: $publishDir"

if ($Installer) {
    $iscc = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1

    if (-not $iscc) {
        Write-Warning "Inno Setup not found. Install from https://jrsoftware.org/isinfo.php"
        exit 0
    }

    & $iscc (Join-Path $root "installer\SandboxTimeline.iss")
    Write-Host "Installer output: $(Join-Path $root 'dist')"
}
