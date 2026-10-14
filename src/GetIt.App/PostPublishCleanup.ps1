param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$pubDir = $PublishDir.Trim('"', '''', ' ', "`t").TrimEnd('\', '/')
if (!(Test-Path -LiteralPath $pubDir)) {
    Write-Host "Publish directory not found: $pubDir"
    exit 0
}

$libsDir = Join-Path $pubDir 'Libs'
if (!(Test-Path $libsDir)) {
    New-Item -ItemType Directory -Path $libsDir -Force | Out-Null
}

$keepFiles = @(
    'GetIt.exe',
    'GetIt.dll',
    'GetIt.deps.json',
    'GetIt.runtimeconfig.json',
    'icon.ico',
    'coreclr.dll',
    'clrjit.dll',
    'hostfxr.dll',
    'hostpolicy.dll',
    'clrgc.dll',
    'CoreMessagingXP.dll',
    'dcompi.dll',
    'dwmcorei.dll',
    'DwmSceneI.dll',
    'DWriteCore.dll',
    'Microsoft.UI.Xaml.dll',
    'Microsoft.UI.Xaml.Controls.dll',
    'Microsoft.UI.Xaml.Phone.dll',
    'Microsoft.UI.Xaml.Internal.dll',
    'Microsoft.ui.xaml.resources.19h1.dll',
    'Microsoft.ui.xaml.resources.common.dll',
    'Microsoft.UI.dll',
    'Microsoft.UI.Input.dll',
    'Microsoft.UI.Windowing.dll',
    'Microsoft.UI.Windowing.Core.dll',
    'Microsoft.DirectManipulation.dll',
    'Microsoft.WindowsAppRuntime.dll',
    'WinUIEdit.dll',
    'wuceffectsi.dll',
    'MRM.dll',
    'marshal.dll',
    'Microsoft.Windows.ApplicationModel.Resources.dll'
)

$frameworkFiles = @()
$depsPath = Join-Path $pubDir 'GetIt.deps.json'
if (Test-Path $depsPath) {
    $deps = Get-Content $depsPath -Raw | ConvertFrom-Json
    $runtimeTargetName = $deps.runtimeTarget.name
    $runtimeTarget = $deps.targets.PSObject.Properties |
        Where-Object { $_.Name -eq $runtimeTargetName } |
        Select-Object -First 1 -ExpandProperty Value
    if ($runtimeTarget) {
        $runtimePack = $runtimeTarget.PSObject.Properties |
            Where-Object { $_.Name -like 'runtimepack.Microsoft.NETCore.App.Runtime.*' } |
            Select-Object -First 1
        if ($runtimePack -and $runtimePack.Value.runtime) {
            $frameworkFiles = @(
                $runtimePack.Value.runtime.PSObject.Properties.Name |
                    Where-Object { $_ -match '\.dll$' } |
                    ForEach-Object { [System.IO.Path]::GetFileName($_) }
            )
        }
    }
}

foreach ($name in $frameworkFiles) {
    $rootPath = Join-Path $pubDir $name
    $libsPath = Join-Path $libsDir $name
    if (!(Test-Path $rootPath) -and (Test-Path $libsPath)) {
        Move-Item $libsPath -Destination $rootPath -Force
    } elseif (Test-Path $libsPath) {
        Remove-Item $libsPath -Force
    }
}

# Remove foreign language folders (keep pt-BR and en-us)
Get-ChildItem -Path $pubDir -Directory | Where-Object {
    $_.Name -match '^[a-z]{2,3}(-[A-Za-z0-9]+)*$' -and $_.Name -ne 'pt-BR' -and $_.Name -ne 'en-us'
} | ForEach-Object {
    Remove-Item $_.FullName -Recurse -Force
}

$movedCount = 0
Get-ChildItem -Path $pubDir -File | ForEach-Object {
    $name = $_.Name
    if (($frameworkFiles -contains $name) -or $name.EndsWith('.pri', [System.StringComparison]::OrdinalIgnoreCase) -or ($keepFiles -contains $name)) {
        # Keep in root
    } else {
        Move-Item $_.FullName -Destination $libsDir -Force
        $movedCount++
    }
}

Write-Host "Moved $movedCount dependencies into Libs subfolder."

# Update GetIt.deps.json so coreclr/hostpolicy loads assemblies directly from Libs/
$depsPath = Join-Path $pubDir 'GetIt.deps.json'
if (Test-Path $depsPath) {
    $content = Get-Content $depsPath -Raw
    foreach ($name in $frameworkFiles) {
        $content = $content.Replace("`"Libs/$name`":", "`"$name`":")
    }
    $libFiles = Get-ChildItem $libsDir -File |
        Where-Object { $frameworkFiles -notcontains $_.Name } |
        Select-Object -ExpandProperty Name
    foreach ($lib in $libFiles) {
        $content = $content.Replace("`"$lib`":", "`"Libs/$lib`":")
    }
    Set-Content -Path $depsPath -Value $content -NoNewline
    Write-Host "Updated GetIt.deps.json with Libs/ probing paths."
}
