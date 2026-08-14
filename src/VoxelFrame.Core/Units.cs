namespace VoxelFrame.Core;

/// <summary>
/// The engine speaks one language: SI (m³, kg, kN, kPa). Every conversion
/// from display units (litres, grams, tons) passes through here so that
/// conservation math is never polluted by unit mistakes.
/// </summary>
public static class Units {
    public const double LitersPerM3 = 1000.0;
    public const double Cm3PerM3 = 1_000_000.0;
    public const double GramsPerKg = 1000.0;

    public static double LitersToM3(double liters) => liters / LitersPerM3;
    public static double M3ToLiters(double m3) => m3 * LitersPerM3;
    public static double Cm3ToM3(double cm3) => cm3 / Cm3PerM3;
    public static double M3ToCm3(double m3) => m3 * Cm3PerM3;
}

/// <summary>Physical state of matter — tracked for every material and item.</summary>
public enum PhysicalState : byte { Solid = 0, Bulk = 1, Fluid = 2, Gas = 3 }
