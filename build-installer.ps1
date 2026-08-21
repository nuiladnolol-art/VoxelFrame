param(
    [string]$Version = "v0.9.0"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"
$launcherPublish = Join-Path $rootDir "src/VoxelFrame.Launcher/bin/Release/net10.0-windows/win-x64"
$setupDir = Join-Path $distDir "setup"
$installerDir = Join-Path $rootDir "src/VoxelFrame.Installer"
$payloadZip = Join-Path $installerDir "payload.zip"

Write-Host "== [VoxelFrame] Сборка инсталлятора $Version ==" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $setupDir -Force | Out-Null

Write-Host "1. Публикация автономного VoxelFrame.Launcher..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Launcher/VoxelFrame.Launcher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false -o "$launcherPublish"

Write-Host "2. Упаковка payload.zip для автономного инсталлятора..." -ForegroundColor Yellow
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }
Compress-Archive -Path "$launcherPublish/*" -DestinationPath $payloadZip -Force

Write-Host "3. Публикация автономного VoxelFrame-Setup.exe..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Installer/VoxelFrame.Installer.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o "$setupDir"

# Очищаем временный payload.zip
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }

$setupExe = Join-Path $setupDir "VoxelFrame-Setup.exe"
$distSetupExe = Join-Path $distDir "VoxelFrame-Setup-$Version.exe"

if (Test-Path $setupExe) {
    Copy-Item -Force $setupExe "$distDir/VoxelFrame-Setup.exe"
    Write-Host "== Инсталлятор успешно собран: $distDir/VoxelFrame-Setup.exe ==" -ForegroundColor Green
} else {
    Write-Host "Предупреждение: Setup exe собран в $setupDir" -ForegroundColor Yellow
}
