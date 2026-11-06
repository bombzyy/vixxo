using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vixxo.Core.Discovery;

public sealed class DiscoveryRequest
{
    [JsonPropertyName("magic")]
    public string Magic { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = string.Empty;
}

public sealed class DiscoveryResponse
{
    [JsonPropertyName("magic")]
    public string Magic { get; set; } = "VIXXO_DISC";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "DISCOVER_RESPONSE";

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("tcp_port")]
    public int TcpPort { get; set; }

    [JsonPropertyName("server_version")]
    public int ServerVersion { get; set; } = 1;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("addresses")]
    public List<string> Addresses { get; set; } = new();
}

public sealed class UdpDiscoveryResponder : IAsyncDisposable
{
    public const int DefaultDiscoveryPort = 47801;
    public const string DiscoveryMagic = "VIXXO_DISC";

    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private readonly object _lock = new();

    public int Port { get; }
    public string ServerName { get; set; } = Environment.MachineName;
    public int TcpPort { get; set; } = 47800;
    public string CertificateFingerprint { get; set; } = string.Empty;
    public List<string> LocalAddresses { get; set; } = new();

    public UdpDiscoveryResponder(int port = DefaultDiscoveryPort)
    {
        Port = port;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_udpClient != null) return;

            _cts = new CancellationTokenSource();
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, Port))
            {
                EnableBroadcast = true
            };

            _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        UdpClient? client;

        lock (_lock)
        {
            cts = _cts;
            client = _udpClient;
            _cts = null;
            _udpClient = null;
        }

        cts?.Cancel();
        try { client?.Close(); client?.Dispose(); } catch { }

        if (_listenTask != null)
        {
            try { await _listenTask.ConfigureAwait(false); } catch { }
            _listenTask = null;
        }

        cts?.Dispose();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct).ConfigureAwait(false);
                string text = Encoding.UTF8.GetString(result.Buffer);

                DiscoveryRequest? req = null;
                try
                {
                    req = JsonSerializer.Deserialize<DiscoveryRequest>(text);
                }
                catch { }

                if (req != null && req.Magic == DiscoveryMagic && req.Type == "DISCOVER_REQUEST")
                {
                    var resp = new DiscoveryResponse
                    {
                        ServerName = ServerName,
                        TcpPort = TcpPort,
                        Fingerprint = CertificateFingerprint,
                        Addresses = LocalAddresses.Count > 0 ? LocalAddresses : new List<string> { "127.0.0.1" }
                    };

                    byte[] respBytes = JsonSerializer.SerializeToUtf8Bytes(resp);
                    await _udpClient.SendAsync(respBytes, respBytes.Length, result.RemoteEndPoint).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    System.Diagnostics.Debug.WriteLine($"UDP Discovery error: {ex.Message}");
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
