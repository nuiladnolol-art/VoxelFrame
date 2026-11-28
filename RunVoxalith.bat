@echo off
rem ── Запуск Voxalith (игра напрямую) ──────────────────────────────────────
cd /d "%~dp0"

set "INSTALLED_EXE="
for /d %%D in ("versions\*") do (
    if exist "%%~fD\Voxalith.exe" set "INSTALLED_EXE=%%~fD\Voxalith.exe"
)
if defined INSTALLED_EXE (
    echo Запуск установленной версии: "%INSTALLED_EXE%"...
    for %%I in ("%INSTALLED_EXE%") do set "INSTALLED_DIR=%%~dpI"
    start "" /d "%INSTALLED_DIR%" "%INSTALLED_EXE%"
    exit /b 0
)

echo Проверка и сборка Voxalith (Release)...
dotnet build src\Voxalith.Game\Voxalith.Game.csproj -c Release -v q
set "DIR=%~dp0src\Voxalith.Game\bin\Release\net10.0"
set "EXE=%DIR%\Voxalith.exe"

if not exist "%EXE%" (
    echo Не удалось найти Voxalith.exe. Убедитесь, что установлен .NET 10 SDK.
    pause
    exit /b 1
)

start "" /d "%DIR%" "%EXE%"
exit /b 0
