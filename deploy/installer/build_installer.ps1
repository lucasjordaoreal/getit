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
$requiredEngineFiles = @("yt-dlp.exe", "deno.exe")
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

$excludedEngineFiles = @("ffmpeg.exe", "ffprobe.exe", "ffplay.exe", "node.exe")
$bundledExcludedEngineFiles = @(
    foreach ($fileName in $excludedEngineFiles) {
        if (Test-Path -LiteralPath (Join-Path $engineBin $fileName) -PathType Leaf) {
            $fileName
        }
    }
)
if ($bundledExcludedEngineFiles.Count -gt 0) {
    Write-Host "Os seguintes arquivos locais serão deliberadamente excluídos do pacote: $($bundledExcludedEngineFiles -join ', ')." -ForegroundColor Yellow
}

$licenseRoot = Join-Path $repoRoot "licenses"
$requiredLicenseFiles = @(
    "THIRD_PARTY_NOTICES.md",
    "THIRD_PARTY_SOURCES.md",
    "texts\yt-dlp-UNLICENSE.txt",
    "texts\yt-dlp-2026.08.19-THIRD-PARTY-LICENSES.txt",
    "texts\Deno-MIT-LICENSE.md",
    "texts\Deno-THIRD-PARTY-NOTICES.txt",
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
        $publishedPath = Join-Path $publishDir (Join-Path "Licenses" ([System.IO.Path]::GetFileName($relativePath)))
        if (!(Test-Path -LiteralPath $publishedPath -PathType Leaf)) {
            $publishedPath
        }
    }
)
if ($missingPublishedLicenseFiles.Count -gt 0) {
    throw "A publicação não contém todos os documentos de terceiros: $($missingPublishedLicenseFiles -join ', ')."
}

$publishedEngineDir = Join-Path $publishDir "Engine\Bin"
$missingPublishedEngineFiles = @(
    foreach ($fileName in $requiredEngineFiles) {
        $filePath = Join-Path $publishedEngineDir $fileName
        if (!(Test-Path -LiteralPath $filePath -PathType Leaf) -or (Get-Item -LiteralPath $filePath).Length -eq 0) {
            $fileName
        }
    }
)
if ($missingPublishedEngineFiles.Count -gt 0) {
    throw "A publicação não contém os executáveis de runtime esperados: $($missingPublishedEngineFiles -join ', ')."
}

$unexpectedPublishedEngineFiles = @(
    foreach ($fileName in $excludedEngineFiles) {
        if (Test-Path -LiteralPath (Join-Path $publishedEngineDir $fileName) -PathType Leaf) {
            $fileName
        }
    }
)
if ($unexpectedPublishedEngineFiles.Count -gt 0) {
    throw "Distribuição bloqueada: a publicação contém executáveis que não devem ser redistribuídos: $($unexpectedPublishedEngineFiles -join ', ')."
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
