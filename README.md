# Voxalith (Воксалит)

Независимая авторская воксельная игра с гладкой воксельной эстетикой (Flat / Smooth Voxel), собственным лором и процедурной генерацией.

## Требования
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows x64)

## Сборка и запуск

```powershell
# Сборка всего решения
dotnet build Voxalith.sln -c Release

# Запуск игры напрямую
dotnet run --project src/Voxalith.Game/Voxalith.Game.csproj -c Release

# Запуск лаунчера
dotnet run --project src/Voxalith.Launcher/Voxalith.Launcher.csproj -c Release
```

## Тесты

```powershell
# Юнит-проверки ядра (Voxalith.Core)
dotnet run --project tools/SelfCheck/SelfCheck.csproj -c Release

# Headless smoke-тест игровой симуляции, физики, сейвов и мультиплеера
dotnet run --project src/Voxalith.Game/Voxalith.Game.csproj -c Release -- --smoke
```

## Сборка релиза и инсталлятора

```powershell
./build-release.ps1 -Version v1.1.0-alpha
./build-installer.ps1 -Version v1.1.0-alpha
```
https://github.com/nuiladnolol-art/Voxalith/releases
