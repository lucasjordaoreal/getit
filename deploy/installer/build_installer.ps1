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
