param(
    [string]$Version = "v0.7.5"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"
$buildDir = Join-Path $distDir "VoxelFrame-$Version-win-x64"

Write-Host "== [VoxelFrame] Сборка релиза $Version ==" -ForegroundColor Cyan

if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

Write-Host "1. Публикация VoxelFrame.Game..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Game/VoxelFrame.Game.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishTrimmed=false -o "$buildDir"

Write-Host "2. Публикация VoxelFrame.Launcher..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Launcher/VoxelFrame.Launcher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishTrimmed=false -o "$buildDir"

Write-Host "3. Копирование ассетов..." -ForegroundColor Yellow
if (Test-Path "$rootDir/assets") {
    Copy-Item -Recurse -Force "$rootDir/assets" "$buildDir/assets"
}

Write-Host "4. Упаковка в ZIP архив..." -ForegroundColor Yellow
$zipFile = Join-Path $distDir "VoxelFrame-$Version-win-x64.zip"
Compress-Archive -Path "$buildDir/*" -DestinationPath $zipFile -Force

Write-Host "== Релиз успешно собран: $zipFile ==" -ForegroundColor Green
