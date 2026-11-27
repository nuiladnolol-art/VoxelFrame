@echo off
rem ── Запуск Voxalith (игра напрямую) ──────────────────────────────────────
cd /d "%~dp0"

if exist "versions\v1.1.0-alpha\Voxalith.exe" (
    echo Запуск установленной версии Voxalith v1.1.0-alpha...
    start "" /d "%~dp0versions\v1.1.0-alpha" "%~dp0versions\v1.1.0-alpha\Voxalith.exe"
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
