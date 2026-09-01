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

    public static SkinDefinition CreateSolidColorSkin(string id, string name, Color baseCol) {
        Color darker = new Color((int)(baseCol.R * 0.75f), (int)(baseCol.G * 0.75f), (int)(baseCol.B * 0.75f), 255);
        Color pants = new Color((int)(baseCol.R * 0.85f), (int)(baseCol.G * 0.85f), (int)(baseCol.B * 0.85f), 255);
        Color shoes = new Color((int)(baseCol.R * 0.65f), (int)(baseCol.G * 0.65f), (int)(baseCol.B * 0.65f), 255);
        Color detail = new Color((int)(baseCol.R * 0.55f), (int)(baseCol.G * 0.55f), (int)(baseCol.B * 0.55f), 255);
        Color eyes = new Color(245, 245, 255, 255);
        return new SkinDefinition(id, name, baseCol, darker, eyes, baseCol, pants, shoes, detail);
    }

    public static void Initialize() {
        _availableSkins.Clear();


        // Однотонные цвета модели («1 цвет на всю модельку»)
        _availableSkins.Add(CreateSolidColorSkin("cyan", "Бирюзовый", new Color(0, 185, 215, 255)));
        _availableSkins.Add(CreateSolidColorSkin("blue", "Синий", new Color(45, 90, 225, 255)));
        _availableSkins.Add(CreateSolidColorSkin("green", "Зелёный", new Color(50, 180, 70, 255)));
        _availableSkins.Add(CreateSolidColorSkin("lime", "Лайм", new Color(130, 215, 45, 255)));
        _availableSkins.Add(CreateSolidColorSkin("yellow", "Жёлтый", new Color(245, 205, 35, 255)));
        _availableSkins.Add(CreateSolidColorSkin("orange", "Оранжевый", new Color(245, 125, 30, 255)));
        _availableSkins.Add(CreateSolidColorSkin("red", "Красный", new Color(230, 50, 50, 255)));
        _availableSkins.Add(CreateSolidColorSkin("crimson", "Малиновый", new Color(195, 35, 95, 255)));
        _availableSkins.Add(CreateSolidColorSkin("purple", "Фиолетовый", new Color(145, 60, 215, 255)));
        _availableSkins.Add(CreateSolidColorSkin("pink", "Розовый", new Color(235, 120, 185, 255)));
        _availableSkins.Add(CreateSolidColorSkin("white", "Белый", new Color(235, 235, 240, 255)));
        _availableSkins.Add(CreateSolidColorSkin("gray", "Серый", new Color(130, 135, 145, 255)));
        _availableSkins.Add(CreateSolidColorSkin("dark", "Тёмный", new Color(45, 48, 55, 255)));

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