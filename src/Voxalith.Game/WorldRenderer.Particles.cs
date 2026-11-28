using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.World;

namespace Voxalith.Game;
public sealed partial class WorldRenderer {
    public void SpawnBlockParticles(Vec3i pos, ushort blockId) {
        int count = SaveSystem.ParticlesMode == 0 ? 4 : (SaveSystem.ParticlesMode == 1 ? 8 : 16);
        Color baseCol = BlockTint(blockId);
        var center = new Vector3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        for (int i = 0; i < count; i++) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 0.7f;
            float oy = (ParticleRng.NextSingle() - 0.5f) * 0.7f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 0.7f;

            float vx = (ParticleRng.NextSingle() - 0.5f) * 3.8f;
            float vy = 1.2f + ParticleRng.NextSingle() * 3.2f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 3.8f;

            int d = ParticleRng.Next(-22, 23);
            var col = new Color(
                (byte)Math.Clamp(baseCol.R + d, 0, 255),
                (byte)Math.Clamp(baseCol.G + d, 0, 255),
                (byte)Math.Clamp(baseCol.B + d, 0, 255),
                (byte)255);

            float size = 0.08f + ParticleRng.NextSingle() * 0.08f;
            float life = 0.50f + ParticleRng.NextSingle() * 0.45f;
            _particles.Add(new VoxelParticle {
                Position = center + new Vector3(ox, oy, oz),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void SpawnCritParticles(Vector3 pos, int count = 14) {
        for (int i = 0; i < count; i++) {
            float angle = ParticleRng.NextSingle() * MathF.Tau;
            float speed = 2.0f + ParticleRng.NextSingle() * 3.0f;
            float vx = MathF.Cos(angle) * speed;
            float vy = 1.5f + ParticleRng.NextSingle() * 2.5f;
            float vz = MathF.Sin(angle) * speed;

            var col = ParticleRng.NextDouble() < 0.5 ? new Color(255, 220, 60, 255) : new Color(255, 140, 30, 255);
            float size = 0.12f + ParticleRng.NextSingle() * 0.08f;
            float life = 0.40f + ParticleRng.NextSingle() * 0.30f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.3f, (ParticleRng.NextSingle() - 0.5f) * 0.3f, (ParticleRng.NextSingle() - 0.5f) * 0.3f),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = true
            });
        }
    }

    public void SpawnDustParticles(Vector3 pos, int count = 4) {
        for (int i = 0; i < count; i++) {
            float vx = (ParticleRng.NextSingle() - 0.5f) * 1.5f;
            float vy = 0.5f + ParticleRng.NextSingle() * 1.2f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 1.5f;
            float size = 0.08f + ParticleRng.NextSingle() * 0.06f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.25f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.4f, 0.05f, (ParticleRng.NextSingle() - 0.5f) * 0.4f),
                Velocity = new Vector3(vx, vy, vz),
                Color = new Color(180, 160, 140, 180),
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    /// <summary>Луч лечения: искры летят от кристалла к боссу по прямой (без гравитации).</summary>
    public void SpawnHealBeamParticles(Vector3 from, Vector3 to, int count = 3) {
        for (int i = 0; i < count; i++) {
            var jitter = new Vector3(
                (ParticleRng.NextSingle() - 0.5f) * 0.5f,
                (ParticleRng.NextSingle() - 0.5f) * 0.5f,
                (ParticleRng.NextSingle() - 0.5f) * 0.5f);
            float life = 0.55f + ParticleRng.NextSingle() * 0.3f;
            _particles.Add(new VoxelParticle {
                Position = from + jitter,
                Velocity = Vector3.Zero,
                Color = new Color(90, 255, 160, 220),
                Size = 0.10f + ParticleRng.NextSingle() * 0.06f,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = true,
                IsHealBeam = true,
                Target = to + new Vector3(0f, EndSlime.HalfSizeY, 0f)
            });
        }
    }

    private float _ambientTimer;

    private void UpdateAmbientParticles(float dt) {
        if (SaveSystem.ParticlesMode == 0) return;
        _ambientTimer -= dt;
        if (_ambientTimer > 0f) return;
        _ambientTimer = SaveSystem.ParticlesMode == 1 ? 0.45f : 0.22f;

        var pPos = _session.Player.Position;
        int px = (int)MathF.Floor(pPos.X);
        int py = (int)MathF.Floor(pPos.Y);
        int pz = (int)MathF.Floor(pPos.Z);

        var eyeVox = _world.GetVoxel(new Vec3i((int)MathF.Floor(_session.Player.Eye.X), (int)MathF.Floor(_session.Player.Eye.Y), (int)MathF.Floor(_session.Player.Eye.Z)));
        bool inWater = eyeVox.TypeId == GameData.BWater.Id;

        // 1. Подводные пузырьки воздуха
        if (inWater) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 3.5f;
            float oy = -0.5f + ParticleRng.NextSingle() * 1.5f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 3.5f;
            _particles.Add(new VoxelParticle {
                Position = _session.Player.Eye + new Vector3(ox, oy, oz),
                Velocity = new Vector3(0f, 1.4f, 0f),
                Color = new Color(200, 235, 255, 170),
                Size = 0.05f + ParticleRng.NextSingle() * 0.04f,
                Lifetime = 1.2f + ParticleRng.NextSingle() * 0.8f,
                MaxLifetime = 2.0f,
                AmbientType = 4
            });
            return;
        }

        // 2. Нижний мир — восходящие адские искры
        if (_world.Dimension == Dimension.Nether) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 16.0f;
            float oy = -2.0f + ParticleRng.NextSingle() * 8.0f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 16.0f;
            _particles.Add(new VoxelParticle {
                Position = pPos + new Vector3(ox, oy, oz),
                Velocity = new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.5f, 1.0f + ParticleRng.NextSingle() * 1.5f, (ParticleRng.NextSingle() - 0.5f) * 0.5f),
                Color = ParticleRng.NextDouble() < 0.6 ? new Color(255, 150, 30, 240) : new Color(255, 60, 20, 220),
                Size = 0.06f + ParticleRng.NextSingle() * 0.05f,
                Lifetime = 1.4f + ParticleRng.NextSingle() * 1.0f,
                MaxLifetime = 2.4f,
                AmbientType = 3
            });
            return;
        }

        // 3. Обычный мир: Ночные светлячки
        if (_world.Dimension == Dimension.Overworld && _session.DayNight.SkyFactor < 0.22f) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 20.0f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 20.0f;
            int gX = px + (int)ox, gZ = pz + (int)oz;
            int surfY = _world.Generator.SurfaceHeight(gX, gZ);
            float spawnY = surfY + 0.8f + ParticleRng.NextSingle() * 2.2f;
            _particles.Add(new VoxelParticle {
                Position = new Vector3(gX + ParticleRng.NextSingle(), spawnY, gZ + ParticleRng.NextSingle()),
                Velocity = new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.4f, (ParticleRng.NextSingle() - 0.5f) * 0.3f, (ParticleRng.NextSingle() - 0.5f) * 0.4f),
                Color = new Color(210, 255, 90, 220),
                Size = 0.07f + ParticleRng.NextSingle() * 0.04f,
                Lifetime = 2.5f + ParticleRng.NextSingle() * 2.0f,
                MaxLifetime = 4.5f,
                AmbientType = 1
            });
        }

        // 4. Обычный мир: падающие листья с крон деревьев
        if (_world.Dimension == Dimension.Overworld) {
            int rx = px + ParticleRng.Next(-8, 9);
            int rz = pz + ParticleRng.Next(-8, 9);
            int ry = py + ParticleRng.Next(-1, 8);
            var checkVox = _world.GetVoxel(new Vec3i(rx, ry, rz));
            if (checkVox.TypeId == GameData.BLeaves.Id) {
                _particles.Add(new VoxelParticle {
                    Position = new Vector3(rx + ParticleRng.NextSingle(), ry - 0.1f, rz + ParticleRng.NextSingle()),
                    Velocity = new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.6f, -0.35f, (ParticleRng.NextSingle() - 0.5f) * 0.6f),
                    Color = ParticleRng.NextDouble() < 0.7 ? new Color(55, 140, 42, 230) : new Color(190, 135, 30, 230),
                    Size = 0.08f + ParticleRng.NextSingle() * 0.04f,
                    Lifetime = 3.0f + ParticleRng.NextSingle() * 2.0f,
                    MaxLifetime = 5.0f,
                    AmbientType = 2
                });
            }
        }
    }

    public void DrawParticles(float dt) {
        for (int i = _particles.Count - 1; i >= 0; i--) {
            var p = _particles[i];
            p.Lifetime -= dt;
            if (p.Lifetime <= 0f) {
                // Swap-remove: RemoveAt сдвигал весь хвост списка на каждую
                // просроченную частицу (O(n²) при взрыве с сотнями частиц).
                int last = _particles.Count - 1;
                _particles[i] = _particles[last];
                _particles.RemoveAt(last);
                continue;
            }

            if (p.IsHealBeam) {
                // Искра летит к цели по прямой с лёгкой волной
                var jitter = new Vector3(
                    MathF.Sin(p.MaxLifetime * 23f + i) * 0.15f, 0f,
                    MathF.Cos(p.MaxLifetime * 19f + i) * 0.15f);
                p.Position = Vector3.Lerp(p.Position, p.Target, 4.5f * dt) + jitter * dt;
            } else if (p.AmbientType == 1) {
                // Firefly (светлячок): плавное хаотическое парение без гравитации
                p.Position += p.Velocity * dt;
                p.Velocity.X += (ParticleRng.NextSingle() - 0.5f) * 1.5f * dt;
                p.Velocity.Y += (ParticleRng.NextSingle() - 0.5f) * 1.5f * dt;
                p.Velocity.Z += (ParticleRng.NextSingle() - 0.5f) * 1.5f * dt;
                p.Velocity *= MathF.Exp(-1.2f * dt);
            } else if (p.AmbientType == 2) {
                // Leaf (падающий лист): медленный планирующий спуск с покачиванием
                p.Position += p.Velocity * dt;
                p.Position.X += MathF.Sin(p.Lifetime * 5.0f + i) * 0.4f * dt;
                p.Position.Z += MathF.Cos(p.Lifetime * 4.0f + i) * 0.4f * dt;
                p.Velocity.Y = Math.Clamp(p.Velocity.Y - 1.2f * dt, -0.65f, 0.5f);
                p.Velocity.X *= MathF.Exp(-1.0f * dt);
                p.Velocity.Z *= MathF.Exp(-1.0f * dt);
            } else if (p.AmbientType == 3) {
                // Nether ember (искра): подъем вверх с турбулентностью
                p.Position += p.Velocity * dt;
                p.Position.X += MathF.Sin(p.Lifetime * 6.0f + i) * 0.25f * dt;
                p.Position.Z += MathF.Cos(p.Lifetime * 5.0f + i) * 0.25f * dt;
                p.Velocity.Y += 0.8f * dt;
                p.Velocity.X *= MathF.Exp(-1.0f * dt);
                p.Velocity.Z *= MathF.Exp(-1.0f * dt);
            } else if (p.AmbientType == 4) {
                // Underwater bubble (пузырек воздуха): всплытие
                p.Position += p.Velocity * dt;
                p.Velocity.Y = 1.6f;
                p.Position.X += MathF.Sin(p.Lifetime * 8.0f + i) * 0.15f * dt;
                p.Position.Z += MathF.Cos(p.Lifetime * 8.0f + i) * 0.15f * dt;
            } else {
                p.Position += p.Velocity * dt;
                p.Velocity.Y -= 16f * dt;
                p.Velocity.X *= MathF.Exp(-2.0f * dt);
                p.Velocity.Z *= MathF.Exp(-2.0f * dt);
            }
            _particles[i] = p;

            float alphaRatio = Math.Clamp(p.Lifetime / p.MaxLifetime, 0f, 1f);
            byte alpha = (byte)(p.Color.A * alphaRatio);
            if (p.AmbientType == 1) {
                float pulse = 0.6f + 0.4f * MathF.Sin(p.Lifetime * 8f);
                alpha = (byte)(alpha * pulse);
            }
            var col = new Color(p.Color.R, p.Color.G, p.Color.B, alpha);

            if (p.IsCrit || p.AmbientType == 1 || p.AmbientType == 3) {
                _additiveParticlesBuf.Add((p, col));
            } else {
                var light = GetLightFactor(p.Position);
                Raylib.DrawCube(p.Position, p.Size, p.Size, p.Size, ShadeColor(col, light));
            }
        }
        if (_additiveParticlesBuf.Count > 0) {
            Raylib.BeginBlendMode(BlendMode.Additive);
            for (int i = 0; i < _additiveParticlesBuf.Count; i++) {
                var (ap, acol) = _additiveParticlesBuf[i];
                Raylib.DrawCube(ap.Position, ap.Size, ap.Size, ap.Size, acol);
            }
            Raylib.EndBlendMode();
            _additiveParticlesBuf.Clear();
        }
    }

    private static Color BlockTint(ushort blockId) => blockId switch {
        var id when id == GameData.BDirt.Id => new Color(134, 96, 58, 255),
        var id when id == GameData.BGrass.Id => new Color(122, 92, 58, 255),
        var id when id == GameData.BStone.Id || id == GameData.BCobblestone.Id => new Color(128, 128, 132, 255),
        var id when id == GameData.BLog.Id => new Color(98, 70, 42, 255),
        var id when id == GameData.BPlanks.Id => new Color(168, 132, 82, 255),
        var id when id == GameData.BLeaves.Id => new Color(52, 118, 40, 255),
        var id when id == GameData.BCoalOre.Id => new Color(80, 80, 85, 255),
        var id when id == GameData.BIronOre.Id => new Color(185, 145, 115, 255),
        var id when id == GameData.BGoldOre.Id => new Color(245, 215, 60, 255),
        var id when id == GameData.BDiamondOre.Id => new Color(90, 230, 240, 255),
        var id when id == GameData.BSapling.Id => new Color(60, 160, 40, 255),
        var id when id == GameData.BRedFlower.Id => new Color(220, 40, 40, 255),
        var id when id == GameData.BYellowFlower.Id => new Color(240, 210, 30, 255),
        var id when id == GameData.BSand.Id => new Color(220, 210, 155, 255),
        var id when id == GameData.BGravel.Id => new Color(130, 125, 125, 255),
        var id when id == GameData.BWater.Id => new Color(50, 100, 220, 200),
        var id when id == GameData.BLava.Id => new Color(245, 90, 20, 255),
        _ => new Color(150, 150, 155, 255),
    };

    public void SpawnSplashParticles(Vector3 pos, int count = 8) {
        int actualCount = SaveSystem.ParticlesMode == 0 ? count / 2 : count;
        for (int i = 0; i < actualCount; i++) {
            float angle = ParticleRng.NextSingle() * MathF.Tau;
            float r = 0.8f + ParticleRng.NextSingle() * 2.2f;
            float vx = MathF.Cos(angle) * r;
            float vy = 1.6f + ParticleRng.NextSingle() * 2.2f;
            float vz = MathF.Sin(angle) * r;
            float size = 0.06f + ParticleRng.NextSingle() * 0.05f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.25f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.3f, 0.05f, (ParticleRng.NextSingle() - 0.5f) * 0.3f),
                Velocity = new Vector3(vx, vy, vz),
                Color = new Color(185, 225, 255, 220),
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void SpawnBlockDustParticles(Vector3 pos, ushort blockId, int count = 6) {
        int actualCount = SaveSystem.ParticlesMode == 0 ? count / 2 : count;
        var baseColor = BlockTint(blockId);
        for (int i = 0; i < actualCount; i++) {
            float vx = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            float vy = 0.6f + ParticleRng.NextSingle() * 1.4f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            int d = ParticleRng.Next(-16, 17);
            var col = new Color(
                (byte)Math.Clamp(baseColor.R + d, 0, 255),
                (byte)Math.Clamp(baseColor.G + d, 0, 255),
                (byte)Math.Clamp(baseColor.B + d, 0, 255),
                (byte)255);
            float size = 0.07f + ParticleRng.NextSingle() * 0.06f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.25f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.4f, 0.05f, (ParticleRng.NextSingle() - 0.5f) * 0.4f),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void SpawnEatParticles(Vector3 pos, Color color, int count = 6) {
        for (int i = 0; i < count; i++) {
            float vx = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            float vy = -0.2f + ParticleRng.NextSingle() * 1.2f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            int d = ParticleRng.Next(-18, 19);
            var col = new Color(
                (byte)Math.Clamp(color.R + d, 0, 255),
                (byte)Math.Clamp(color.G + d, 0, 255),
                (byte)Math.Clamp(color.B + d, 0, 255),
                (byte)255);
            float size = 0.05f + ParticleRng.NextSingle() * 0.05f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.30f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.2f, (ParticleRng.NextSingle() - 0.5f) * 0.15f, (ParticleRng.NextSingle() - 0.5f) * 0.2f),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }
}
