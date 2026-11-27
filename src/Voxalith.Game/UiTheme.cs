using System;
using Raylib_cs;

namespace Voxalith.Game;

public enum ThemeType {
    VoxalithDark = 0,
    MonolithStone = 1,
    AstralVoid = 2
}

public sealed class UiTheme {
    public string Name { get; init; } = "";
    public ThemeType Type { get; init; }

    // Colors
    public Color Background { get; init; }
    public Color BackgroundDark { get; init; }
    public Color PanelBg { get; init; }
    public Color PanelBorder { get; init; }
    public Color PanelBorderLight { get; init; }

    public Color AccentPrimary { get; init; }
    public Color AccentSecondary { get; init; }

    public Color ButtonNormal { get; init; }
    public Color ButtonHover { get; init; }
    public Color ButtonActive { get; init; }
    public Color ButtonDisabled { get; init; }
    public Color ButtonBorder { get; init; }

    public Color HotbarSlotBg { get; init; }
    public Color HotbarSlotSelectedBg { get; init; }
    public Color HotbarSelectedBorder { get; init; }
    public Color HotbarBorder { get; init; }

    public Color TextPrimary { get; init; }
    public Color TextSecondary { get; init; }
    public Color TextAccent { get; init; }

    public Color HealthBarFill { get; init; }
    public Color HealthBarBg { get; init; }
    public Color HungerBarFill { get; init; }
    public Color HungerBarBg { get; init; }
    public Color OxygenBarFill { get; init; }

    public static readonly UiTheme VoxalithDark = new() {
        Name = "Voxalith Dark",
        Type = ThemeType.VoxalithDark,
        Background = new Color(20, 22, 30, 255),          // #14161E
        BackgroundDark = new Color(14, 16, 22, 255),      // #0E1016
        PanelBg = new Color(26, 30, 42, 235),             // Slate panel
        PanelBorder = new Color(48, 56, 76, 255),
        PanelBorderLight = new Color(0, 210, 255, 180),   // Neon cyan accent border
        AccentPrimary = new Color(0, 210, 255, 255),      // Neon azure #00D2FF
        AccentSecondary = new Color(255, 215, 80, 255),   // Gold #FFD700
        ButtonNormal = new Color(32, 38, 52, 230),
        ButtonHover = new Color(45, 54, 76, 255),
        ButtonActive = new Color(0, 180, 230, 255),
        ButtonDisabled = new Color(22, 25, 34, 180),
        ButtonBorder = new Color(60, 72, 98, 255),
        HotbarSlotBg = new Color(24, 28, 38, 210),
        HotbarSlotSelectedBg = new Color(38, 48, 68, 240),
        HotbarSelectedBorder = new Color(0, 210, 255, 255),
        HotbarBorder = new Color(50, 60, 80, 220),
        TextPrimary = new Color(240, 245, 255, 255),
        TextSecondary = new Color(150, 165, 190, 255),
        TextAccent = new Color(0, 210, 255, 255),
        HealthBarFill = new Color(235, 50, 75, 255),
        HealthBarBg = new Color(45, 20, 25, 200),
        HungerBarFill = new Color(230, 140, 40, 255),
        HungerBarBg = new Color(45, 30, 15, 200),
        OxygenBarFill = new Color(60, 190, 255, 255)
    };

    public static readonly UiTheme MonolithStone = new() {
        Name = "Monolith Stone",
        Type = ThemeType.MonolithStone,
        Background = new Color(32, 35, 42, 255),          // #20232A
        BackgroundDark = new Color(24, 26, 31, 255),
        PanelBg = new Color(40, 44, 54, 240),
        PanelBorder = new Color(80, 86, 100, 255),
        PanelBorderLight = new Color(140, 148, 165, 255),
        AccentPrimary = new Color(220, 225, 235, 255),    // Pure monolithic stone white/silver
        AccentSecondary = new Color(160, 170, 185, 255),
        ButtonNormal = new Color(46, 51, 62, 230),
        ButtonHover = new Color(62, 68, 82, 255),
        ButtonActive = new Color(90, 98, 115, 255),
        ButtonDisabled = new Color(28, 30, 36, 180),
        ButtonBorder = new Color(75, 82, 98, 255),
        HotbarSlotBg = new Color(34, 38, 46, 210),
        HotbarSlotSelectedBg = new Color(52, 58, 70, 240),
        HotbarSelectedBorder = new Color(230, 235, 245, 255),
        HotbarBorder = new Color(70, 76, 90, 220),
        TextPrimary = new Color(245, 245, 250, 255),
        TextSecondary = new Color(160, 165, 175, 255),
        TextAccent = new Color(255, 255, 255, 255),
        HealthBarFill = new Color(225, 60, 70, 255),
        HealthBarBg = new Color(45, 25, 28, 200),
        HungerBarFill = new Color(210, 150, 50, 255),
        HungerBarBg = new Color(45, 35, 20, 200),
        OxygenBarFill = new Color(100, 180, 230, 255)
    };

    public static readonly UiTheme AstralVoid = new() {
        Name = "Astral Void",
        Type = ThemeType.AstralVoid,
        Background = new Color(18, 14, 28, 255),          // #120E1C
        BackgroundDark = new Color(12, 9, 18, 255),
        PanelBg = new Color(28, 22, 44, 235),
        PanelBorder = new Color(68, 48, 102, 255),
        PanelBorderLight = new Color(192, 64, 255, 200),  // Neon violet
        AccentPrimary = new Color(192, 64, 255, 255),     // Neon violet #C040FF
        AccentSecondary = new Color(112, 160, 255, 255),  // Cosmic azure
        ButtonNormal = new Color(36, 28, 56, 230),
        ButtonHover = new Color(52, 40, 82, 255),
        ButtonActive = new Color(160, 50, 220, 255),
        ButtonDisabled = new Color(22, 18, 32, 180),
        ButtonBorder = new Color(85, 60, 128, 255),
        HotbarSlotBg = new Color(26, 20, 40, 210),
        HotbarSlotSelectedBg = new Color(46, 34, 70, 240),
        HotbarSelectedBorder = new Color(192, 64, 255, 255),
        HotbarBorder = new Color(75, 55, 110, 220),
        TextPrimary = new Color(245, 235, 255, 255),
        TextSecondary = new Color(180, 160, 210, 255),
        TextAccent = new Color(210, 120, 255, 255),
        HealthBarFill = new Color(240, 45, 110, 255),
        HealthBarBg = new Color(48, 18, 32, 200),
        HungerBarFill = new Color(180, 90, 230, 255),
        HungerBarBg = new Color(40, 20, 50, 200),
        OxygenBarFill = new Color(112, 160, 255, 255)
    };

    public static readonly UiTheme[] AllThemes = { VoxalithDark, MonolithStone, AstralVoid };

    public static UiTheme Current => AllThemes[Math.Clamp(SaveSystem.SelectedThemeIndex, 0, AllThemes.Length - 1)];
}
