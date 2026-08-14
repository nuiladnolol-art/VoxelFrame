using System;
using System.Collections.Generic;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Симуляция жидкостей (Вода и Лава):
/// - Стекание вниз и горизонтальное растекание.
/// - Бесконечные источники воды (2 смежных источника).
/// - Взаимодействие воды и лавы (Обсидиан, Булыжник, Камень).
/// </summary>
public sealed class FluidEngine {
    private readonly GameWorld _world;
    private float _timer;
    private readonly HashSet<Vec3i> _activeFluids = new();
    private readonly Queue<Vec3i> _updateQueue = new();

    public FluidEngine(GameWorld world) {
        _world = world;
    }

    public void ScheduleUpdate(Vec3i pos) {
        _activeFluids.Add(pos);
    }

    public void Tick(float dt) {
        _timer += dt;
        if (_timer < 0.15f) return;
        _timer = 0f;

        if (_activeFluids.Count == 0) return;

        var toProcess = new List<Vec3i>(_activeFluids);
        _activeFluids.Clear();

        int budget = 256;
        foreach (var pos in toProcess) {
            if (budget-- <= 0) {
                _activeFluids.Add(pos);
                break;
            }
            UpdateFluidAt(pos);
        }
    }

    public void UpdateFluidAt(Vec3i pos) {
        var vox = _world.GetVoxel(pos);
        ushort fluidId = vox.TypeId;
        if (fluidId != GameData.BWater.Id && fluidId != GameData.BLava.Id) return;

        Vec3i down = pos + new Vec3i(0, -1, 0);
        var downVox = _world.GetVoxel(down);

        // 1. Проверка контакта с противоположной жидкостью
        ushort otherId = fluidId == GameData.BWater.Id ? GameData.BLava.Id : GameData.BWater.Id;
        var neighbors = new Vec3i[] {
            pos + new Vec3i(1, 0, 0), pos + new Vec3i(-1, 0, 0),
            pos + new Vec3i(0, 0, 1), pos + new Vec3i(0, 0, -1),
            pos + new Vec3i(0, 1, 0),
            down
        };

        foreach (var n in neighbors) {
            var nv = _world.GetVoxel(n);
            if (nv.TypeId == otherId) {
                if (fluidId == GameData.BLava.Id) {
                    _world.PlacePlacedBlock(pos, GameData.BObsidian, 1f);
                    return;
                } else if (nv.TypeId == GameData.BLava.Id) {
                    _world.PlacePlacedBlock(n, GameData.BCobblestone, 1f);
                    return;
                }
            }
        }

        // 2. Течение вниз
        if (downVox.TypeId == 0) {
            _world.PlacePlacedBlock(down, GameData.GetBlock(fluidId), 1f);
            _activeFluids.Add(down);
            return;
        }

        // 3. Горизонтальное растекание (если внизу твердый блок или та же жидкость)
        if (downVox.TypeId != 0) {
            var hNeighbors = new Vec3i[] {
                pos + new Vec3i(1, 0, 0), pos + new Vec3i(-1, 0, 0),
                pos + new Vec3i(0, 0, 1), pos + new Vec3i(0, 0, -1)
            };

            foreach (var hn in hNeighbors) {
                var hv = _world.GetVoxel(hn);
                if (hv.TypeId == 0) {
                    _world.PlacePlacedBlock(hn, GameData.GetBlock(fluidId), 1f);
                    _activeFluids.Add(hn);

                    // Проверяем бесконечный источник воды для пустых соседей
                    CheckInfiniteWaterSource(hn);
                }
            }
        }
    }

    private void CheckInfiniteWaterSource(Vec3i pos) {
        var hNeighbors = new Vec3i[] {
            pos + new Vec3i(1, 0, 0), pos + new Vec3i(-1, 0, 0),
            pos + new Vec3i(0, 0, 1), pos + new Vec3i(0, 0, -1)
        };

        int waterCount = 0;
        foreach (var n in hNeighbors) {
            if (_world.GetVoxel(n).TypeId == GameData.BWater.Id) {
                waterCount++;
            }
        }

        // Если с 2 сторон вода и снизу опора — создаем источник воды
        if (waterCount >= 2 && _world.IsSolidAt(pos + new Vec3i(0, -1, 0))) {
            _world.PlacePlacedBlock(pos, GameData.BWater, 1f);
        }
    }
}
