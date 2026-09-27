# Script para publicar e gerar o instalador do GetIt automaticamente
$ErrorActionPreference = "Stop"

$repoRoot = (Get-Item $PSScriptRoot).Parent.FullName
Set-Location $repoRoot

# 1. Obter a versão definida no GetIt.App.csproj
$csprojPath = Join-Path $repoRoot "GetIt.App\GetIt.App.csproj"
[xml]$csproj = Get-Content $csprojPath
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = "1.0.0" }

Write-Host "=== Compilando e Publicando GetIt v$version (Release x64) ===" -ForegroundColor Cyan
dotnet publish GetIt.App/GetIt.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "GetIt.App/bin/Release/net10.0-windows10.0.26100.0/win-x64/publish"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha ao publicar o projeto."
    exit 1
}

# 2. Localizar o compilador do Inno Setup (ISCC.exe)
$isccCandidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
    "C:\Program Files\Inno Setup 5\ISCC.exe"
)

$isccPath = $null
foreach ($path in $isccCandidates) {
    if (Test-Path $path) {
        $isccPath = $path
        break
    }
}

if (-not $isccPath) {
    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { $isccPath = $cmd.Source }
}

$issFile = Join-Path $PSScriptRoot "GetItInstaller.iss"

if ($isccPath) {
    Write-Host "=== Gerando o Instalador via Inno Setup ($isccPath) ===" -ForegroundColor Green
    & $isccPath "/DMyAppVersion=$version" $issFile
    if ($LASTEXITCODE -eq 0) {
        $outDir = Join-Path $repoRoot "bin\Installer"
        Write-Host "`n[SUCESSO] Instalador gerado em:" -ForegroundColor Green
        Write-Host "  $outDir\GetIt-Setup-v$version.exe`n" -ForegroundColor Yellow
    } else {
        Write-Error "Erro ao compilar o instalador com o Inno Setup."
    }
} else {
    Write-Host "`n[AVISO] Inno Setup 6 não foi encontrado instalado no sistema." -ForegroundColor Yellow
    Write-Host "Para gerar o executável do instalador (.exe):" -ForegroundColor Cyan
    Write-Host "1. Baixe o Inno Setup gratuitamente em: https://jrsoftware.org/isdl.php" -ForegroundColor Gray
    Write-Host "2. Instale e execute novamente este script:" -ForegroundColor Gray
    Write-Host "   powershell installer\build_installer.ps1`n" -ForegroundColor Yellow
}
