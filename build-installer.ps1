param(
    [string]$Version = "v1.0.4"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"
$launcherPublish = Join-Path $rootDir "src/VoxelFrame.Launcher/bin/Release/net10.0-windows/win-x64"
$setupDir = Join-Path $distDir "setup"
$installerDir = Join-Path $rootDir "src/VoxelFrame.Installer"
$payloadZip = Join-Path $installerDir "payload.zip"
$numericVersion = $Version.TrimStart('v', 'V')

Write-Host "== [VoxelFrame] Сборка инсталлятора $Version ==" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

Write-Host "1. Публикация автономного VoxelFrame.Launcher..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Launcher/VoxelFrame.Launcher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:Version=$numericVersion -o "$launcherPublish"

Write-Host "2. Упаковка payload.zip для автономного инсталлятора..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }
Compress-Archive -Path "$launcherPublish/*" -DestinationPath $payloadZip -Force

Write-Host "3. Публикация автономного VoxelFrame-Setup.exe..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Installer/VoxelFrame.Installer.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:Version=$numericVersion -o "$setupDir"

# Очищаем временный payload.zip
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }

$setupExe = Join-Path $setupDir "VoxelFrame-Setup.exe"
$distSetupExe = Join-Path $distDir "VoxelFrame-Setup-$Version.exe"

if (Test-Path $setupExe) {
    Copy-Item -Force $setupExe "$distDir/VoxelFrame-Setup.exe"
    Copy-Item -Force $setupExe "$distSetupExe"
    Write-Host "== Инсталлятор успешно собран: $distDir/VoxelFrame-Setup.exe и $distSetupExe ==" -ForegroundColor Green

    # Дописываем хеши инсталлятора в общий файл контрольных сумм релиза.
    $sumsFile = Join-Path $distDir "SHA256SUMS.txt"
    $lines = @()
    if (Test-Path $sumsFile) { $lines = @(Get-Content -Path $sumsFile) }
    $lines += "$((Get-FileHash -Algorithm SHA256 -Path "$distDir/VoxelFrame-Setup.exe").Hash.ToLowerInvariant())  VoxelFrame-Setup.exe"
    $lines += "$((Get-FileHash -Algorithm SHA256 -Path $distSetupExe).Hash.ToLowerInvariant())  $(Split-Path -Leaf $distSetupExe)"
    $lines | Out-File -FilePath $sumsFile -Encoding ascii
    Write-Host "== Контрольные суммы обновлены: $sumsFile ==" -ForegroundColor Green
} else {
    Write-Host "Предупреждение: Setup exe собран в $setupDir" -ForegroundColor Yellow
}
