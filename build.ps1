# Sandbox Timeline build script
param(
    [switch]$Installer,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "SandboxTimeline.csproj"
$publishDir = Join-Path $root "bin\Release\net8.0-windows\win-x64\publish"
$distDir = Join-Path $root "dist"
$zipPath = Join-Path $distDir "SandboxTimeline-win-x64.zip"

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

if ($Zip) {
    if (-not (Test-Path $distDir)) {
        New-Item -ItemType Directory -Path $distDir -Force | Out-Null
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Write-Host "Creating release zip: $zipPath"
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "Zip ready: $zipPath"
    Write-Host "Upload: gh release create v1.0.0 `"$zipPath`" --title `"v1.0.0`""
}

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
