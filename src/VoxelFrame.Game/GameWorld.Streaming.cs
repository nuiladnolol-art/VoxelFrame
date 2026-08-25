using VoxelFrame.Core;

namespace VoxelFrame.Game;

public sealed partial class GameWorld {
    private float _unloadTimer;

    /// <summary>Выгружает чанки дальше renderDistance+2 от центра. Не трогает грязные чанки.</summary>
    public int UnloadFarChunks(Vec3i centerChunk, int renderDistance) {
        int keep = renderDistance + 2;
        var toRemove = new List<Vec3i>();
        foreach (var kv in _chunks) {
            var gc = kv.Value;
            if (_lightDirty.Contains(gc) || _meshDirty.Contains(gc) || gc.Chunk.Version > 0) continue;
            var c = kv.Key;
            int dx = Math.Abs(c.X - centerChunk.X);
            int dz = Math.Abs(c.Z - centerChunk.Z);
            if (dx > keep || dz > keep) toRemove.Add(c);
        }
        foreach (var cc in toRemove) {
            if (_chunks.TryGetValue(cc, out var gc)) {
                gc.UnloadMesh();
                _chunks.Remove(cc);
                _lightDirty.Remove(gc);
                _meshDirty.Remove(gc);
            }
        }
        return toRemove.Count;
    }

    public void TickStreaming(Vec3i playerChunk, float dt) {
        _unloadTimer += dt;
        if (_unloadTimer < 2f) return;
        _unloadTimer = 0f;
        UnloadFarChunks(playerChunk, RenderDistance);
    }
}
