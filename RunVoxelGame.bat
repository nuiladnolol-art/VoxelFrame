@echo off
rem ── Запуск VoxelFrame (игра напрямую) ──────────────────────────────────────
cd /d "%~dp0"

echo Проверка и сборка VoxelFrame (Release)...
dotnet build src\VoxelFrame.Game\VoxelFrame.Game.csproj -c Release -v q
set "EXE=src\VoxelFrame.Game\bin\Release\net10.0\VoxelFrame.Game.exe"

if not exist "%EXE%" (
    echo Не удалось собрать игру. Убедитесь, что установлен .NET 10 SDK.
    pause
    exit /b 1
)
start "" "%EXE%"
exit /b 0
