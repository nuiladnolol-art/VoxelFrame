using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoxelFrame.Game;

public record DiscoveredServer(string Host, int Port, string WorldName, string HostPlayer, int PlayerCount, DateTime LastSeen);

public sealed class LanDiscovery : IDisposable {
    private UdpClient? _broadcaster;
    private UdpClient? _listener;
    private CancellationTokenSource? _broadcasterCts;
    private CancellationTokenSource? _listenerCts;
    private readonly ConcurrentDictionary<string, DiscoveredServer> _servers = new();

    public IReadOnlyCollection<DiscoveredServer> FoundServers => _servers.Values.ToArray();

    private static List<IPAddress> GetBroadcastAddresses() {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        try {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var ipProps = nic.GetIPProperties();
                foreach (var unicast in ipProps.UnicastAddresses) {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && unicast.IPv4Mask != null) {
                        byte[] ipBytes = unicast.Address.GetAddressBytes();
                        byte[] maskBytes = unicast.IPv4Mask.GetAddressBytes();
                        byte[] broadcastBytes = new byte[ipBytes.Length];
                        for (int i = 0; i < ipBytes.Length; i++) {
                            broadcastBytes[i] = (byte)(ipBytes[i] | (maskBytes[i] ^ 255));
                        }
                        var bcast = new IPAddress(broadcastBytes);
                        if (!list.Contains(bcast)) list.Add(bcast);
                    }
                }
            }
        } catch { }
        return list;
    }

    public void StartBroadcaster(int tcpPort, string worldName, string hostPlayer, Func<int> getPlayerCount) {
        StopBroadcaster();
        _broadcaster = new UdpClient { EnableBroadcast = true };
        _broadcasterCts = new CancellationTokenSource();
        var token = _broadcasterCts.Token;

        Task.Run(async () => {
            while (!token.IsCancellationRequested && _broadcaster != null) {
                try {
                    int count = getPlayerCount();
                    string safeWorld = (worldName ?? "World").Replace('|', '/');
                    string safeHost = (hostPlayer ?? "Player").Replace('|', '/');
                    string msg = $"{NetworkProtocol.DiscoveryMagic}|{tcpPort}|{safeWorld}|{safeHost}|{count}";
                    byte[] data = Encoding.UTF8.GetBytes(msg);

                    var targets = GetBroadcastAddresses();
                    foreach (var targetIp in targets) {
                        try {
                            var ep = new IPEndPoint(targetIp, NetworkProtocol.DiscoveryPort);
                            await _broadcaster.SendAsync(data, data.Length, ep);
                        } catch { }
                    }
                } catch {
                    // Ignore transient broadcast errors
                }
                try {
                    await Task.Delay(1500, token);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }, token);
    }

    public void StartListener() {
        StopListener();
        try {
            _listener = new UdpClient();
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, NetworkProtocol.DiscoveryPort));
        } catch (Exception ex) {
            Console.WriteLine($"[LAN Discovery] Ошибка биндинга сокета: {ex.Message}");
            return;
        }

        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;

        Task.Run(async () => {
            while (!token.IsCancellationRequested && _listener != null) {
                try {
                    var result = await _listener.ReceiveAsync(token);
                    string raw = Encoding.UTF8.GetString(result.Buffer);
                    var parts = raw.Split('|');
                    if (parts.Length >= 5 && parts[0] == NetworkProtocol.DiscoveryMagic) {
                        if (int.TryParse(parts[1], out int port) && int.TryParse(parts[4], out int count)) {
                            string host = result.RemoteEndPoint.Address.ToString();
                            string key = $"{host}:{port}";
                            _servers[key] = new DiscoveredServer(host, port, parts[2], parts[3], count, DateTime.UtcNow);
                        }
                    }
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception ex) {
                    if (token.IsCancellationRequested) break;
                    Console.WriteLine($"[LAN Discovery] Ошибка чтения пакета: {ex.Message}");
                }
            }
        }, token);
    }

    public void CleanupOldServers() {
        var now = DateTime.UtcNow;
        foreach (var (k, v) in _servers) {
            if ((now - v.LastSeen).TotalSeconds > 5.0) {
                _servers.TryRemove(k, out _);
            }
        }
    }

    public void StopBroadcaster() {
        try {
            _broadcasterCts?.Cancel();
            _broadcaster?.Close();
        } catch { }
        _broadcaster = null;
        _broadcasterCts?.Dispose();
        _broadcasterCts = null;
    }

    public void StopListener() {
        try {
            _listenerCts?.Cancel();
            _listener?.Close();
        } catch { }
        _listener = null;
        _listenerCts?.Dispose();
        _listenerCts = null;
    }

    public void Dispose() {
        StopBroadcaster();
        StopListener();
    }
}