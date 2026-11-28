using Voxalith.Core;

namespace Voxalith.Game;

/// <summary>
/// Явный контракт сетевой синхронизации для мира (серверный хост или удалённый клиент),
/// заменяющий глобальные статические делегаты GameWorld.
/// </summary>
public interface IWorldNetworkSink {
    bool IsRemoteClient { get; }
    void OnBlockChanged(Vec3i pos, ushort typeId, byte mask, bool isBreak, Dimension dimension);
}
