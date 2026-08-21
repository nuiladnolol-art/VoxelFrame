using System;
using System.Collections.Generic;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Аутентичная симуляция жидкостей по стандартам Minecraft (Вода и Лава):
/// 1. Ограничение дистанции течения (MaxWaterDistance = 7, MaxLavaDistance = 3).
/// 2. Каскадное высыхание (receding): если источник удалён/перекрыт, поток исчезает.
/// 3. Бесконечный источник воды: формируется ТОЛЬКО из >=2 соседних источников (level 0) на твердой опоре.
/// 4. Лава не создает бесконечных источников и течет медленнее воды (0.45с vs 0.15с).
/// 5. Реакции Вода <-> Лава:
///    - Вода касается источника лавы -> Обсидиан (BObsidian).
///    - Вода касается текущей лавы -> Булыжник (BCobblestone).
///    - Лава течет сверху на воду -> Камень (BStone).
/// 6. Жидкости смывают хрупкие блоки (трава, факелы, пшеница) с выпадением предметов.
/// </summary>
public sealed class FluidEngine {
    public const int MaxWaterDistance = 7;
    public const int MaxLavaDistance = 3;
    public const byte FallingLevel = 8; // Вертикальный падающий поток

    private readonly GameWorld _world;
    private float _waterTimer;
    private float _lavaTimer;
    private readonly HashSet<Vec3i> _activeWater = new();
    private readonly HashSet<Vec3i> _activeLava = new();
    private readonly HashSet<Vec3i> _pendingUpdates = new();

    public IReadOnlyCollection<Vec3i> ActiveLava => _activeLava;

    public FluidEngine(GameWorld world) {
        _world = world;
    }

    public void ScheduleUpdate(Vec3i pos) {
        _pendingUpdates.Add(pos);
    }

    public void Tick(float dt) {
        // Добавляем новые запланированные координаты в соответствующие пулы
        if (_pendingUpdates.Count > 0) {
            foreach (var pos in _pendingUpdates) {
                var vox = _world.GetVoxel(pos);
                if (vox.TypeId == GameData.BWater.Id) _activeWater.Add(pos);
                else if (vox.TypeId == GameData.BLava.Id) _activeLava.Add(pos);
                else {
                    // Если блок стал воздухом/другим блоком, соседи могут требовать пересчета
                    CheckEmptyCellForFluids(pos);
                }
            }
            _pendingUpdates.Clear();
        }

        _waterTimer += dt;
        if (_waterTimer >= 0.15f) {
            _waterTimer = 0f;
            TickFluidType(GameData.BWater.Id, _activeWater, MaxWaterDistance);
        }

        _lavaTimer += dt;
        if (_lavaTimer >= 0.45f) {
            _lavaTimer = 0f;
            TickFluidType(GameData.BLava.Id, _activeLava, MaxLavaDistance);
        }
    }

    private void TickFluidType(ushort fluidId, HashSet<Vec3i> activeSet, int maxDistance) {
        if (activeSet.Count == 0) return;

        var toProcess = new List<Vec3i>(activeSet);
        activeSet.Clear();

        int budget = 384;
        foreach (var pos in toProcess) {
            if (budget-- <= 0) {
                activeSet.Add(pos);
                break;
            }
            UpdateFluidAt(pos);
        }
    }

    private void CheckEmptyCellForFluids(Vec3i pos) {
        // Проверяем 6 соседей вокруг изменившейся ячейки
        var neighbors = new Vec3i[] {
            pos + new Vec3i(0, 1, 0),
            pos + new Vec3i(0, -1, 0),
            pos + new Vec3i(1, 0, 0),
            pos + new Vec3i(-1, 0, 0),
            pos + new Vec3i(0, 0, 1),
            pos + new Vec3i(0, 0, -1)
        };

        foreach (var n in neighbors) {
            var nv = _world.GetVoxel(n);
            if (nv.TypeId == GameData.BWater.Id) _activeWater.Add(n);
            else if (nv.TypeId == GameData.BLava.Id) _activeLava.Add(n);
        }

        // Проверяем возможность создания бесконечного источника воды в этой пустой клетке
        CheckInfiniteWaterSource(pos);
    }

    public void UpdateFluidAt(Vec3i pos) {
        var vox = _world.GetVoxel(pos);
        ushort fluidId = vox.TypeId;
        if (fluidId != GameData.BWater.Id && fluidId != GameData.BLava.Id) {
            // Если в этой клетке уже не жидкость, проверяем не нужно ли создать источник воды
            CheckInfiniteWaterSource(pos);
            return;
        }

        byte currentLevel = vox.SubGridLayerMask;
        int maxDistance = fluidId == GameData.BWater.Id ? MaxWaterDistance : MaxLavaDistance;

        // ── 1. Взаимодействие Вода <-> Лава ─────────────────────────────────────
        if (fluidId == GameData.BLava.Id) {
            // Если лава касается любой воды со всех 6 сторон
            if (HasAdjacentFluid(pos, GameData.BWater.Id, out _)) {
                if (currentLevel == 0) {
                    // Источник лавы превращается в обсидиан
                    _world.PlacePlacedBlock(pos, GameData.BObsidian, 1f);
                } else {
                    // Текущая лава превращается в булыжник
                    _world.PlacePlacedBlock(pos, GameData.BCobblestone, 1f);
                }
                SoundSystem.PlaySplash();
                NotifyNeighbors(pos);
                return;
            }

            // Лава поджигает соседние деревянные / горючие блоки
            var lavaDirs = new Vec3i[] {
                new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)
            };
            foreach (var d in lavaDirs) {
                var np = pos + d;
                var nb = _world.GetBlockType(np);
                if (nb != null && nb.IsFlammable) {
                    _world.Fire.Ignite(np);
                }
            }
        } else if (fluidId == GameData.BWater.Id) {
            // Если сверху на воду течет лава
            var up = pos + new Vec3i(0, 1, 0);
            if (_world.GetVoxel(up).TypeId == GameData.BLava.Id) {
                _world.PlacePlacedBlock(pos, GameData.BStone, 1f);
                SoundSystem.PlaySplash();
                NotifyNeighbors(pos);
                return;
            }
        }

        // ── 2. Проверка валидности текущей жидкости и каскадное высыхание ─────────
        if (currentLevel > 0) {
            // Текущий блок жидкости (не источник): вычисляем, питается ли он от родителя
            int newLevel = CalculateFlowLevel(pos, fluidId, maxDistance);
            if (newLevel < 0 || newLevel > maxDistance) {
                // Питающий источник исчез -> высыхаем обратно в воздух!
                _world.RemoveBlock(pos);
                NotifyNeighbors(pos);
                return;
            }

            if (newLevel != currentLevel) {
                currentLevel = (byte)newLevel;
                _world.PlacePlacedBlock(pos, GameData.GetBlock(fluidId), 1f, currentLevel);
                NotifyNeighbors(pos);
            }
        }

        // ── 3. Стекание вниз (вертикальный водопад / лавапад) ───────────────────
        Vec3i down = pos + new Vec3i(0, -1, 0);
        var downVox = _world.GetVoxel(down);
        bool canFlowDown = CanFlowInto(downVox.TypeId);

        if (canFlowDown) {
            WashBlock(down, downVox.TypeId, fluidId);
            _world.PlacePlacedBlock(down, GameData.GetBlock(fluidId), 1f, FallingLevel);
            ScheduleFluid(down, fluidId);
            NotifyNeighbors(down);
            // В Minecraft, если жидкость может свободно течь вниз, она течет вниз и не растекается в стороны
            return;
        }

        // ── 4. Горизонтальное растекание ────────────────────────────────────────
        // Эффективный уровень, от которого отталкиваемся при растекании
        int spreadBaseLevel = (currentLevel == 0 || currentLevel == FallingLevel) ? 0 : currentLevel;

        if (spreadBaseLevel < maxDistance) {
            byte nextLevel = (byte)(spreadBaseLevel + 1);
            var hDirs = new Vec3i[] {
                new(1, 0, 0), new(-1, 0, 0),
                new(0, 0, 1), new(0, 0, -1)
            };

            foreach (var dir in hDirs) {
                var targetPos = pos + dir;
                var targetVox = _world.GetVoxel(targetPos);

                if (CanFlowInto(targetVox.TypeId)) {
                    WashBlock(targetPos, targetVox.TypeId, fluidId);
                    _world.PlacePlacedBlock(targetPos, GameData.GetBlock(fluidId), 1f, nextLevel);
                    ScheduleFluid(targetPos, fluidId);
                    NotifyNeighbors(targetPos);
                } else if (targetVox.TypeId == fluidId) {
                    // Если сосед уже жидкость, но с более слабым уровнем
                    byte targetLevel = targetVox.SubGridLayerMask;
                    if (targetLevel > nextLevel && targetLevel != FallingLevel && targetLevel != 0) {
                        _world.PlacePlacedBlock(targetPos, GameData.GetBlock(fluidId), 1f, nextLevel);
                        ScheduleFluid(targetPos, fluidId);
                        NotifyNeighbors(targetPos);
                    }
                }
            }
        }

        // ── 5. Бесконечный источник воды ────────────────────────────────────────
        if (fluidId == GameData.BWater.Id) {
            CheckInfiniteWaterSource(pos);
        }
    }

    private int CalculateFlowLevel(Vec3i pos, ushort fluidId, int maxDistance) {
        // Проверяем блок сверху: если сверху течет та же жидкость -> уровень FallingLevel (8)
        var up = pos + new Vec3i(0, 1, 0);
        var upVox = _world.GetVoxel(up);
        if (upVox.TypeId == fluidId) {
            return FallingLevel;
        }

        byte currentLevel = _world.GetVoxel(pos).SubGridLayerMask;

        // Проверяем 4 горизонтальных соседа: ищем минимальный уровень родителя
        var hDirs = new Vec3i[] {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1)
        };

        int minParentLevel = int.MaxValue;
        foreach (var dir in hDirs) {
            var nPos = pos + dir;
            var nv = _world.GetVoxel(nPos);
            if (nv.TypeId == fluidId) {
                byte nLevel = nv.SubGridLayerMask;
                if (nLevel == 0 || nLevel == FallingLevel) {
                    // Источник или падающий столб дает уровень 1
                    minParentLevel = Math.Min(minParentLevel, 0);
                } else if (nLevel < currentLevel || currentLevel == FallingLevel) {
                    minParentLevel = Math.Min(minParentLevel, (int)nLevel);
                }
            }
        }

        if (minParentLevel == int.MaxValue) return -1; // Нет источника
        int resultLevel = minParentLevel + 1;
        return resultLevel <= maxDistance ? resultLevel : -1;
    }

    private void CheckInfiniteWaterSource(Vec3i pos) {
        var vox = _world.GetVoxel(pos);
        // Бесконечный источник может сформироваться только в пустой клетке или в текущей воде (level > 0)
        if (vox.TypeId != 0 && (vox.TypeId != GameData.BWater.Id || vox.SubGridLayerMask == 0)) {
            return;
        }

        // Обязательное условие: опора снизу (твёрдый блок или блок воды)
        var down = pos + new Vec3i(0, -1, 0);
        var downVox = _world.GetVoxel(down);
        if (downVox.TypeId == 0) return;

        // Считаем соседние полноценные источники воды (level == 0)
        var hDirs = new Vec3i[] {
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 0, 1), new(0, 0, -1)
        };

        int sourceCount = 0;
        foreach (var dir in hDirs) {
            var nv = _world.GetVoxel(pos + dir);
            if (nv.TypeId == GameData.BWater.Id && nv.SubGridLayerMask == 0) {
                sourceCount++;
            }
        }

        // Если с двух или более сторон настоящие источники -> превращаемся в полноценный источник!
        if (sourceCount >= 2) {
            _world.PlacePlacedBlock(pos, GameData.BWater, 1f, 0);
            _activeWater.Add(pos);
            NotifyNeighbors(pos);
        }
    }

    private static bool CanFlowInto(ushort typeId) {
        if (typeId == 0) return true;
        // Хрупкие блоки, которые смываются жидкостью
        if (typeId == GameData.BTallGrass.Id ||
            typeId == GameData.BTorch.Id ||
            typeId == GameData.BWheatCrop.Id) {
            return true;
        }
        return false;
    }

    private void WashBlock(Vec3i pos, ushort typeId, ushort fluidId) {
        if (typeId == 0) return;
        if (fluidId == GameData.BWater.Id) {
            if (typeId == GameData.BTallGrass.Id) {
                if (Random.Shared.NextDouble() < 0.20) {
                    _world.SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                }
            } else if (typeId == GameData.BTorch.Id) {
                _world.SpawnPickup(GameData.TorchItem.Id, 1, pos);
            } else if (typeId == GameData.BWheatCrop.Id) {
                var vox = _world.GetVoxel(pos);
                if (vox.SubGridLayerMask >= 3) {
                    _world.SpawnPickup(GameData.WheatItem.Id, 1, pos);
                    _world.SpawnPickup(GameData.WheatSeedsItem.Id, 1 + Random.Shared.Next(2), pos);
                } else {
                    _world.SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                }
            }
        }
    }

    private bool HasAdjacentFluid(Vec3i pos, ushort targetFluidId, out byte neighborLevel) {
        neighborLevel = 0;
        var neighbors = new Vec3i[] {
            pos + new Vec3i(0, 1, 0),
            pos + new Vec3i(0, -1, 0),
            pos + new Vec3i(1, 0, 0),
            pos + new Vec3i(-1, 0, 0),
            pos + new Vec3i(0, 0, 1),
            pos + new Vec3i(0, 0, -1)
        };

        foreach (var n in neighbors) {
            var nv = _world.GetVoxel(n);
            if (nv.TypeId == targetFluidId) {
                neighborLevel = nv.SubGridLayerMask;
                return true;
            }
        }
        return false;
    }

    private void NotifyNeighbors(Vec3i pos) {
        var neighbors = new Vec3i[] {
            pos + new Vec3i(0, 1, 0),
            pos + new Vec3i(0, -1, 0),
            pos + new Vec3i(1, 0, 0),
            pos + new Vec3i(-1, 0, 0),
            pos + new Vec3i(0, 0, 1),
            pos + new Vec3i(0, 0, -1)
        };

        foreach (var n in neighbors) {
            var nv = _world.GetVoxel(n);
            if (nv.TypeId == GameData.BWater.Id) _activeWater.Add(n);
            else if (nv.TypeId == GameData.BLava.Id) _activeLava.Add(n);
            else if (nv.TypeId == 0) {
                CheckInfiniteWaterSource(n);
            }
        }
    }

    private void ScheduleFluid(Vec3i pos, ushort fluidId) {
        if (fluidId == GameData.BWater.Id) _activeWater.Add(pos);
        else if (fluidId == GameData.BLava.Id) _activeLava.Add(pos);
    }
}
