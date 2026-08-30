using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VoxelFrame.Game;

public record DiscoveredServer(string Host, int Port, string WorldName, string HostPlayer, int PlayerCount, DateTime LastSeen);

public sealed class LanDiscovery : IDisposable {
    private UdpClient? _broadcaster;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private readonly ConcurrentDictionary<string, DiscoveredServer> _servers = new();

    public IReadOnlyCollection<DiscoveredServer> FoundServers => _servers.Values.ToArray();

    public void StartBroadcaster(int tcpPort, string worldName, string hostPlayer, Func<int> getPlayerCount) {
        StopBroadcaster();
        _broadcaster = new UdpClient { EnableBroadcast = true };
        _cts ??= new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () => {
            var target = new IPEndPoint(IPAddress.Broadcast, NetworkProtocol.DiscoveryPort);
            while (!token.IsCancellationRequested) {
                try {
                    int count = getPlayerCount();
                    string msg = $"{NetworkProtocol.DiscoveryMagic}|{tcpPort}|{worldName}|{hostPlayer}|{count}";
                    byte[] data = Encoding.UTF8.GetBytes(msg);
                    await _broadcaster.SendAsync(data, data.Length, target);
                } catch {
                    // Ignore transient network errors
                }
                await Task.Delay(1500, token);
            }
        }, token);
    }

    public void StartListener() {
        StopListener();
        try {
            _listener = new UdpClient();
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, NetworkProtocol.DiscoveryPort));
        } catch {
            return;
        }

        _cts ??= new CancellationTokenSource();
        var token = _cts.Token;

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
                } catch {
                    break;
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
        try { _broadcaster?.Close(); } catch { }
        _broadcaster = null;
    }

    public void StopListener() {
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    public void Dispose() {
        _cts?.Cancel();
        StopBroadcaster();
        StopListener();
        _cts?.Dispose();
        _cts = null;
    }
}