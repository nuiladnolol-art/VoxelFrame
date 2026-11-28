param(
    [string]$Version = "v1.1.0-alpha"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"
$launcherPublish = Join-Path $rootDir "src/Voxalith.Launcher/bin/Release/net10.0-windows/win-x64"
$setupDir = Join-Path $distDir "setup"
$installerDir = Join-Path $rootDir "src/Voxalith.Installer"
$payloadZip = Join-Path $installerDir "payload.zip"
$numericVersion = $Version.TrimStart('v', 'V')

Write-Host "== [Voxalith] Сборка инсталлятора $Version ==" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

Write-Host "1. Публикация автономного Voxalith.Launcher..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/Voxalith.Launcher/Voxalith.Launcher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:Version=$numericVersion -o "$launcherPublish"

Write-Host "2. Упаковка payload.zip для автономного инсталлятора..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }
Compress-Archive -Path "$launcherPublish/*" -DestinationPath $payloadZip -Force

Write-Host "3. Публикация автономного Voxalith-Setup.exe..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/Voxalith.Installer/Voxalith.Installer.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:Version=$numericVersion -o "$setupDir"

# Очищаем временный payload.zip
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }

$setupExe = Join-Path $setupDir "Voxalith-Setup.exe"
$distSetupExe = Join-Path $distDir "Voxalith-Setup-$Version.exe"

if (Test-Path $setupExe) {
    Copy-Item -Force $setupExe "$distDir/Voxalith-Setup.exe"
    Copy-Item -Force $setupExe "$distSetupExe"
    Write-Host "== Инсталлятор успешно собран: $distDir/Voxalith-Setup.exe и $distSetupExe ==" -ForegroundColor Green

    # Обновляем хеши инсталлятора в общем файле контрольных сумм без дублей.
    $sumsFile = Join-Path $distDir "SHA256SUMS.txt"
    $setupHash = (Get-FileHash -Algorithm SHA256 -Path "$distDir/Voxalith-Setup.exe").Hash.ToLowerInvariant()
    $distSetupName = Split-Path -Leaf $distSetupExe
    $distSetupHash = (Get-FileHash -Algorithm SHA256 -Path $distSetupExe).Hash.ToLowerInvariant()
    $lines = @()
    if (Test-Path $sumsFile) {
        $lines = @(Get-Content -Path $sumsFile | Where-Object {
            $_ -notmatch '\s+Voxalith-Setup\.exe$' -and $_ -notmatch ("\s+" + [regex]::Escape($distSetupName) + '$')
        })
    }
    $lines += "$setupHash  Voxalith-Setup.exe"
    $lines += "$distSetupHash  $distSetupName"
    $lines | Out-File -FilePath $sumsFile -Encoding ascii
    Write-Host "== Контрольные суммы обновлены: $sumsFile ==" -ForegroundColor Green
} else {
    Write-Host "Предупреждение: Setup exe собран в $setupDir" -ForegroundColor Yellow
}
