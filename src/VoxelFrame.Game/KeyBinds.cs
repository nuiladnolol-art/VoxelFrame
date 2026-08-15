using Raylib_cs;

namespace VoxelFrame.Game;

public static class KeyBinds {
    public static KeyboardKey Forward = KeyboardKey.W;
    public static KeyboardKey Backward = KeyboardKey.S;
    public static KeyboardKey Left = KeyboardKey.A;
    public static KeyboardKey Right = KeyboardKey.D;
    public static KeyboardKey Jump = KeyboardKey.Space;
    public static KeyboardKey Crouch = KeyboardKey.LeftShift;
    public static KeyboardKey Sprint = KeyboardKey.LeftControl;
    public static KeyboardKey Drop = KeyboardKey.Q;
    public static KeyboardKey Inventory = KeyboardKey.E;
    public static KeyboardKey Crafting = KeyboardKey.C;
    public static KeyboardKey Pause = KeyboardKey.Escape;

    public static string GetName(KeyboardKey key) => key switch {
        KeyboardKey.LeftShift => "L.Shift",
        KeyboardKey.RightShift => "R.Shift",
        KeyboardKey.LeftControl => "L.Ctrl",
        KeyboardKey.RightControl => "R.Ctrl",
        KeyboardKey.LeftAlt => "L.Alt",
        KeyboardKey.RightAlt => "R.Alt",
        KeyboardKey.Space => "Space",
        KeyboardKey.Escape => "ESC",
        _ => key.ToString()
    };

    public static void ResetToDefaults() {
        Forward = KeyboardKey.W;
        Backward = KeyboardKey.S;
        Left = KeyboardKey.A;
        Right = KeyboardKey.D;
        Jump = KeyboardKey.Space;
        Crouch = KeyboardKey.LeftShift;
        Sprint = KeyboardKey.LeftControl;
        Drop = KeyboardKey.Q;
        Inventory = KeyboardKey.E;
        Crafting = KeyboardKey.C;
        Pause = KeyboardKey.Escape;
    }
}
