param(
    [string]$Version = "v1.0.4"
)

$ErrorActionPreference = "Stop"
$rootDir = $PSScriptRoot
$distDir = Join-Path $rootDir "dist"
$buildDir = Join-Path $distDir "VoxelFrame-$Version-win-x64"
# Версия сборки без префикса «v» — попадает в AssemblyInformationalVersion,
# иначе лаунчер/инсталлер сообщают о себе версию из csproj и вечно предлагают обновление.
$numericVersion = $Version.TrimStart('v', 'V')

Write-Host "== [VoxelFrame] Сборка релиза $Version ==" -ForegroundColor Cyan

if (Test-Path $distDir) {
    Remove-Item -Recurse -Force $distDir
}
New-Item -ItemType Directory -Path $buildDir -Force | Out-Null

Write-Host "1. Публикация VoxelFrame.Game..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Game/VoxelFrame.Game.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishTrimmed=false /p:Version=$numericVersion -o "$buildDir"

Write-Host "2. Публикация VoxelFrame.Launcher..." -ForegroundColor Yellow
dotnet publish "$rootDir/src/VoxelFrame.Launcher/VoxelFrame.Launcher.csproj" -c Release -r win-x64 --self-contained true /p:PublishSingleFile=false /p:PublishTrimmed=false /p:Version=$numericVersion -o "$buildDir"

Write-Host "3. Копирование ассетов..." -ForegroundColor Yellow
if (Test-Path "$rootDir/assets") {
    Copy-Item -Recurse -Force "$rootDir/assets" "$buildDir/assets"
}

Write-Host "4. Упаковка в ZIP архив..." -ForegroundColor Yellow
$zipFile = Join-Path $distDir "VoxelFrame-$Version-win-x64.zip"
Compress-Archive -Path "$buildDir/*" -DestinationPath $zipFile -Force

Write-Host "5. Контрольные суммы (SHA256SUMS.txt)..." -ForegroundColor Yellow
$hash = (Get-FileHash -Algorithm SHA256 -Path $zipFile).Hash.ToLowerInvariant()
$sumsFile = Join-Path $distDir "SHA256SUMS.txt"
"$hash  $(Split-Path -Leaf $zipFile)" | Out-File -FilePath $sumsFile -Encoding ascii

Write-Host "== Релиз успешно собран: $zipFile ==" -ForegroundColor Green
Write-Host "== Контрольные суммы: $sumsFile ==" -ForegroundColor Green
