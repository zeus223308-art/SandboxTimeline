#Requires -Version 5.1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$packageDir = Join-Path $root "SandboxTimeline.Package"
$imagesDir = Join-Path $packageDir "Images"
$distDir = Join-Path $root "dist\Store"
$project = Join-Path $root "SandboxTimeline.csproj"
$wapproj = Join-Path $packageDir "SandboxTimeline.Package.wapproj"

function Ensure-StoreImages {
    if (-not (Test-Path $imagesDir)) {
        New-Item -ItemType Directory -Path $imagesDir -Force | Out-Null
    }

    Add-Type -AssemblyName System.Drawing
    $sizes = @{
        "StoreLogo.png" = @(50, 50)
        "Square44x44Logo.png" = @(44, 44)
        "Square150x150Logo.png" = @(150, 150)
        "Wide310x150Logo.png" = @(310, 150)
    }

    foreach ($entry in $sizes.GetEnumerator()) {
        $path = Join-Path $imagesDir $entry.Key
        if (Test-Path $path) { continue }
        $width, $height = $entry.Value
        $bitmap = New-Object System.Drawing.Bitmap $width, $height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::FromArgb(255, 30, 30, 30))
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 122, 204))
        $margin = [Math]::Max(4, [int]($width / 8))
        $graphics.FillRectangle($brush, $margin, $margin, $width - (2 * $margin), $height - (2 * $margin))
        $graphics.Dispose()
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        Write-Host "Created placeholder image: $path"
    }
}

function Ensure-SigningCertificate {
    $pfxPath = Join-Path $packageDir "SandboxTimeline.Package_TemporaryKey.pfx"
    if (Test-Path $pfxPath) { return }

    if (-not (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue)) {
        Write-Warning "New-SelfSignedCertificate not available. Open the packaging project in Visual Studio to generate a temporary certificate."
        return
    }

    $cert = New-SelfSignedCertificate -Type Custom -Subject "CN=Sandbox Timeline Dev" `
        -KeyUsage DigitalSignature -FriendlyName "SandboxTimeline Store Dev" `
        -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password (ConvertTo-SecureString -String "sandboxtimeline" -Force -AsPlainText) | Out-Null
    Write-Host "Created dev signing certificate: $pfxPath"
}

Write-Host "Preparing Store packaging assets..."
Ensure-StoreImages
Ensure-SigningCertificate

Write-Host "Publishing SandboxTimeline (Store distribution)..."
dotnet publish $project -c Release -r win-x64 `
    -p:StoreDistribution=true `
    -p:PublishSingleFile=false `
    --self-contained false

if (-not (Test-Path $wapproj)) {
    throw "Missing packaging project: $wapproj"
}

$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" `
    -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1

if (-not $msbuild) {
    Write-Warning @"
MSBuild with Desktop Bridge was not found.
Open SandboxTimeline.sln in Visual Studio 2022 and publish SandboxTimeline.Package manually.
See docs/STORE-PUBLISH.md
"@
    exit 0
}

if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir -Force | Out-Null
}

Write-Host "Building MSIX with: $msbuild"
& $msbuild $wapproj /p:Configuration=Release /p:Platform=x64 /p:AppxPackageDir="$distDir" /p:UapAppxPackageBuildMode=StoreUpload

Write-Host "Store build output: $distDir"
Write-Host "Next: Partner Center submission — docs/STORE-PUBLISH.md"
