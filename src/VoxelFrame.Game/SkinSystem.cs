using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Raylib_cs;

namespace VoxelFrame.Game;

public record SkinDefinition(
    string Id,
    string DisplayName,
    Color SkinColor,
    Color HairColor,
    Color EyeColor,
    Color ShirtColor,
    Color PantsColor,
    Color ShoeColor,
    Color DetailColor,
    string? FilePath = null
);

public static class SkinSystem {
    private static readonly List<SkinDefinition> _availableSkins = new();
    private static int _selectedSkinIndex = 0;

    public static IReadOnlyList<SkinDefinition> AvailableSkins => _availableSkins;

    public static SkinDefinition CurrentSkin {
        get {
            if (_availableSkins.Count == 0) Initialize();
            int idx = Math.Clamp(_selectedSkinIndex, 0, _availableSkins.Count - 1);
            return _availableSkins[idx];
        }
    }

    public static void Initialize() {
        _availableSkins.Clear();

        // 1. Встроенные классические и тематические скины
        _availableSkins.Add(new SkinDefinition(
            "steve",
            "Стив (Steve)",
            new Color(225, 175, 135, 255), // Кожа
            new Color(80, 50, 25, 255),     // Волосы
            new Color(50, 70, 200, 255),    // Глаза
            new Color(0, 160, 185, 255),    // Синяя футболка
            new Color(40, 50, 120, 255),    // Темно-синие джинсы
            new Color(55, 55, 60, 255),     // Серые кеды
            new Color(195, 140, 105, 255)   // Нос
        ));

        _availableSkins.Add(new SkinDefinition(
            "alex",
            "Алекс (Alex)",
            new Color(235, 185, 150, 255), // Кожа
            new Color(200, 100, 40, 255),   // Рыжие волосы
            new Color(30, 150, 70, 255),    // Зеленые глаза
            new Color(90, 145, 60, 255),    // Зеленая туника
            new Color(105, 65, 40, 255),    // Коричневые брюки
            new Color(65, 45, 30, 255),     // Ботинки
            new Color(75, 75, 75, 255)      // Кожаный ремень
        ));

        _availableSkins.Add(new SkinDefinition(
            "miner",
            "Шахтёр (Miner)",
            new Color(215, 165, 130, 255), // Кожа
            new Color(45, 35, 30, 255),     // Волосы
            new Color(60, 110, 140, 255),   // Глаза
            new Color(70, 75, 85, 255),     // Рабочая куртка
            new Color(35, 45, 65, 255),     // Рабочие штаны
            new Color(40, 40, 40, 255),     // Сапоги
            new Color(245, 200, 50, 255)    // Каска / фонарик
        ));

        _availableSkins.Add(new SkinDefinition(
            "knight",
            "Рыцарь (Knight)",
            new Color(220, 170, 130, 255), // Кожа
            new Color(30, 30, 30, 255),     // Волосы
            new Color(180, 210, 240, 255),  // Глаза
            new Color(160, 165, 175, 255),  // Стальная кираса
            new Color(110, 115, 125, 255),  // Латные поножи
            new Color(75, 80, 90, 255),     // Латные ботинки
            new Color(180, 40, 40, 255)     // Красный плащ / герб
        ));

        _availableSkins.Add(new SkinDefinition(
            "ranger",
            "Лесник (Ranger)",
            new Color(210, 160, 120, 255), // Кожа
            new Color(110, 70, 40, 255),    // Волосы
            new Color(70, 130, 90, 255),    // Глаза
            new Color(60, 90, 55, 255),     // Камуфляжная куртка
            new Color(85, 60, 40, 255),     // Кожаные брюки
            new Color(45, 30, 20, 255),     // Охотничьи сапоги
            new Color(130, 95, 60, 255)     // Патронташ / пояс
        ));

        _availableSkins.Add(new SkinDefinition(
            "cyber",
            "Кибер (Cyber)",
            new Color(200, 185, 190, 255), // Кожа
            new Color(20, 20, 25, 255),     // Черные волосы
            new Color(0, 230, 255, 255),    // Неоновые голубые глаза
            new Color(25, 28, 38, 255),     // Нано-костюм
            new Color(18, 20, 28, 255),     // Темные брюки
            new Color(15, 15, 20, 255),     // Сапоги
            new Color(0, 200, 255, 255)     // Неоновые линии
        ));

        // 2. Поиск пользовательских скинов в папках assets/skins/ и skin.png
        DiscoverCustomSkins();

        // Восстановление выбранного скина из настроек
        SetSkin(SaveSystem.SelectedSkin);
    }

    private static void DiscoverCustomSkins() {
        string[] probeDirs = {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "skins"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "skins"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "skins"),
            Path.Combine(Directory.GetCurrentDirectory(), "skins")
        };

        foreach (var dir in probeDirs) {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.png")) {
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (_availableSkins.Exists(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                _availableSkins.Add(new SkinDefinition(
                    name.ToLowerInvariant(),
                    $"{name} (PNG)",
                    new Color(225, 175, 135, 255),
                    new Color(70, 50, 30, 255),
                    new Color(80, 150, 230, 255),
                    new Color(120, 140, 180, 255),
                    new Color(60, 70, 90, 255),
                    new Color(40, 40, 45, 255),
                    new Color(200, 180, 60, 255),
                    file
                ));
            }
        }
    }

    public static void NextSkin() {
        if (_availableSkins.Count == 0) Initialize();
        _selectedSkinIndex = (_selectedSkinIndex + 1) % _availableSkins.Count;
        SaveSystem.SelectedSkin = CurrentSkin.Id;
        SaveSystem.SaveSettings();
    }

    public static void PreviousSkin() {
        if (_availableSkins.Count == 0) Initialize();
        _selectedSkinIndex = (_selectedSkinIndex - 1 + _availableSkins.Count) % _availableSkins.Count;
        SaveSystem.SelectedSkin = CurrentSkin.Id;
        SaveSystem.SaveSettings();
    }

    public static void SetSkin(string skinIdOrName) {
        if (string.IsNullOrWhiteSpace(skinIdOrName)) return;
        if (_availableSkins.Count == 0) Initialize();
        int found = _availableSkins.FindIndex(s => 
            s.Id.Equals(skinIdOrName, StringComparison.OrdinalIgnoreCase) || 
            s.DisplayName.Contains(skinIdOrName, StringComparison.OrdinalIgnoreCase));
        if (found >= 0) {
            _selectedSkinIndex = found;
            SaveSystem.SelectedSkin = _availableSkins[found].Id;
        }
    }

    public static SkinDefinition GetSkin(string? skinIdOrName) {
        if (_availableSkins.Count == 0) Initialize();
        if (string.IsNullOrWhiteSpace(skinIdOrName)) return CurrentSkin;
        var found = _availableSkins.Find(s => 
            s.Id.Equals(skinIdOrName, StringComparison.OrdinalIgnoreCase) || 
            s.DisplayName.Contains(skinIdOrName, StringComparison.OrdinalIgnoreCase));
        return found ?? CurrentSkin;
    }
}