# Script para publicar e gerar o instalador do GetIt automaticamente
$ErrorActionPreference = "Stop"

$repoRoot = (Get-Item (Join-Path $PSScriptRoot "..\..")).FullName
Set-Location $repoRoot

# 1. Obter a versão definida no GetIt.App.csproj
$csprojPath = Join-Path $repoRoot "src\GetIt.App\GetIt.App.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) {
    throw "Não foi possível encontrar <Version> em $csprojPath."
}
$version = $version.Trim()

$engineBin = Join-Path $repoRoot "tools\Engine\Bin"
$requiredEngineFiles = @("yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe")
$missingEngineFiles = @(
    foreach ($fileName in $requiredEngineFiles) {
        $filePath = Join-Path $engineBin $fileName
        if (!(Test-Path -LiteralPath $filePath -PathType Leaf)) {
            $fileName
        } elseif ((Get-Item -LiteralPath $filePath).Length -eq 0) {
            $fileName
        }
    }
)
if ($missingEngineFiles.Count -gt 0) {
    throw "Engine incompleta em '$engineBin'. Arquivos ausentes ou vazios: $($missingEngineFiles -join ', ')."
}

$licenseRoot = Join-Path $repoRoot "licenses"
$requiredLicenseFiles = @(
    "THIRD_PARTY_NOTICES.md",
    "THIRD_PARTY_SOURCES.md",
    "texts\FFmpeg-COPYING.GPLv2.txt",
    "texts\FFmpeg-COPYING.GPLv3.txt",
    "texts\FFmpeg-COPYING.LGPLv2.1.txt",
    "texts\FFmpeg-COPYING.LGPLv3.txt",
    "texts\yt-dlp-UNLICENSE.txt",
    "texts\Deno-MIT-LICENSE.md",
    "texts\Node-LICENSE",
    "texts\dotnet-runtime-LICENSE.TXT",
    "texts\dotnet-runtime-THIRD-PARTY-NOTICES.TXT",
    "texts\CommunityToolkit-Mvvm-8.4.2-LICENSE.md",
    "texts\CommunityToolkit-Mvvm-8.4.2-ThirdPartyNotices.txt",
    "texts\System-Numerics-Tensors-9.0.0-LICENSE.TXT",
    "texts\System-Numerics-Tensors-9.0.0-THIRD-PARTY-NOTICES.TXT",
    "texts\Microsoft-WebView2-1.0.3719.77-LICENSE.txt",
    "texts\Microsoft-WebView2-1.0.3719.77-NOTICE.txt",
    "texts\Microsoft-WindowsAppSDK-2.1.3-license.txt",
    "texts\Microsoft-WindowsAppSDK-2.1.3-NOTICE.txt",
    "texts\Microsoft-WindowsAppSDK-AI-2.1.10-license.txt",
    "texts\Microsoft-WindowsAppSDK-Base-2.0.4-license.txt",
    "texts\Microsoft-WindowsAppSDK-Base-2.0.4-NOTICE.txt",
    "texts\Microsoft-WindowsAppSDK-DWrite-2.1.0-license.txt",
    "texts\Microsoft-WindowsAppSDK-Foundation-2.0.21-license.txt",
    "texts\Microsoft-WindowsAppSDK-InteractiveExperiences-2.0.13-license.txt",
    "texts\Microsoft-WindowsAppSDK-ML-2.1.1-license.txt",
    "texts\Microsoft-WindowsAppSDK-ML-2.1.1-ThirdPartyNotices.txt",
    "texts\Microsoft-WindowsAppSDK-Runtime-2.1.3-license.txt",
    "texts\Microsoft-WindowsAppSDK-Runtime-2.1.3-NOTICE.txt",
    "texts\Microsoft-WindowsAppSDK-Widgets-2.0.5-license.txt",
    "texts\Microsoft-WindowsAppSDK-WinUI-2.1.0-license.txt",
    "texts\Microsoft-WindowsAppSDK-WinUI-2.1.0-NOTICE.txt"
)
$missingLicenseFiles = @(
    foreach ($relativePath in $requiredLicenseFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $licenseRoot $relativePath) -PathType Leaf)) {
            $relativePath
        }
    }
)
if ($missingLicenseFiles.Count -gt 0) {
    throw "Materiais de licenciamento ausentes em '$licenseRoot': $($missingLicenseFiles -join ', ')."
}

$sourceBundleDir = Join-Path $repoRoot "bin\ThirdPartySources"
$sourceBundlePath = Join-Path $sourceBundleDir "GetIt-Third-Party-Sources-v$version.zip"
$sourceHashPath = "$sourceBundlePath.sha256"
$reviewPath = Join-Path $sourceBundleDir "REDISTRIBUTION-REVIEW.txt"
foreach ($requiredPath in @($sourceBundlePath, $sourceHashPath, $reviewPath)) {
    if (!(Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Instalador bloqueado: falta '$requiredPath'. Prepare o pacote de fontes correspondente, seu SHA-256 e a revisão documentada antes de distribuir."
    }
}

$sourceHash = (Get-Content -LiteralPath $sourceHashPath -Raw).Trim()
if ($sourceHash -notmatch '^[A-Fa-f0-9]{64}$') {
    throw "O arquivo de hash '$sourceHashPath' deve conter somente um SHA-256 hexadecimal de 64 caracteres."
}
if ((Get-FileHash -LiteralPath $sourceBundlePath -Algorithm SHA256).Hash -ne $sourceHash.ToUpperInvariant()) {
    throw "O SHA-256 do pacote de fontes não corresponde ao arquivo '$sourceHashPath'."
}

$reviewContents = Get-Content -LiteralPath $reviewPath -Raw
$reviewIsInvalid = ($reviewContents -notmatch "(?m)^version=$([regex]::Escape($version))\s*$") `
    -or ($reviewContents -notmatch "(?m)^source-bundle-sha256=$([regex]::Escape($sourceHash))\s*$") `
    -or ($reviewContents -notmatch '(?m)^reviewed-by=(?!\s*$).+')
if ($reviewIsInvalid) {
    throw "O arquivo '$reviewPath' deve registrar a versão, o hash do pacote de fontes e um revisor identificável."
}

$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    "C:\Program Files\Inno Setup 5\ISCC.exe"
)

$isccPath = $null
foreach ($path in $isccCandidates) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { $isccPath = $cmd.Source }
}

if (-not $isccPath) {
    throw "Inno Setup não foi encontrado. Instale o Inno Setup 6 para gerar o instalador."
}

Write-Host "=== Compilando e Publicando GetIt v$version (Release x64) ===" -ForegroundColor Cyan
dotnet publish "src\GetIt.App\GetIt.App.csproj" -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -o "src\GetIt.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"

if ($LASTEXITCODE -ne 0) {
    throw "Falha ao publicar o projeto."
}

$publishDir = Join-Path $repoRoot "src\GetIt.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"
$missingPublishedLicenseFiles = @(
    foreach ($relativePath in $requiredLicenseFiles) {
        $publishedPath = Join-Path $publishDir (Join-Path "Licenses" $relativePath)
        if (!(Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            $publishedPath
        }
    }
)
if ($missingPublishedLicenseFiles.Count -gt 0) {
    throw "A publicação não contém todos os documentos de terceiros: $($missingPublishedLicenseFiles -join ', ')."
}

$issFile = Join-Path $PSScriptRoot "GetItInstaller.iss"
$outDir = Join-Path $repoRoot "bin\Installer"
$setupPath = Join-Path $outDir "GetIt-Setup-v$version.exe"

Write-Host "=== Gerando o Instalador via Inno Setup ($isccPath) ===" -ForegroundColor Green
& $isccPath "/DMyAppVersion=$version" $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Erro ao compilar o instalador com o Inno Setup."
}
if (!(Test-Path -LiteralPath $setupPath -PathType Leaf) -or (Get-Item -LiteralPath $setupPath).Length -eq 0) {
    throw "O Inno Setup terminou sem gerar o instalador esperado em '$setupPath'."
}

Write-Host "`n[SUCESSO] Instalador gerado em:" -ForegroundColor Green
Write-Host "  $setupPath`n" -ForegroundColor Yellow
