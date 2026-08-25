namespace VoxelFrame.Core.Materials;

public enum MaterialCategory : byte { Wood, Stone, Metal, Soil, Fluid, Organic }

/// <summary>
/// Категория вещества. Изначально слой «научной» физики (плотность, прочность,
/// закон сохранения массы) был удалён — остался только игровой тег категории,
/// который используется в логике добычи блоков (твёрдость, требуемый инструмент).
/// </summary>
public sealed class Material {
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public MaterialCategory Category { get; init; }
}
