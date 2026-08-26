@echo off
rem ── Запуск VoxelFrame (игра напрямую) ──────────────────────────────────────
rem Release-сборка быстрее Debug в несколько раз; если её нет — берём Debug,
rem если нет и её — собираем Release автоматически.
cd /d "%~dp0"

set "EXE=src\VoxelFrame.Game\bin\Release\net10.0\VoxelFrame.Game.exe"
if exist "%EXE%" goto run

set "EXE=src\VoxelFrame.Game\bin\Debug\net10.0\VoxelFrame.Game.exe"
if exist "%EXE%" goto run

echo Сборка игры не найдена. Собираю Release...
dotnet build src\VoxelFrame.Game\VoxelFrame.Game.csproj -c Release -v q
set "EXE=src\VoxelFrame.Game\bin\Release\net10.0\VoxelFrame.Game.exe"

:run
if not exist "%EXE%" (
    echo Не удалось собрать игру. Убедитесь, что установлен .NET 10 SDK.
    pause
    exit /b 1
)
start "" "%EXE%"
exit /b 0
