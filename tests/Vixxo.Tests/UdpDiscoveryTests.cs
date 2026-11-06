using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vixxo.Core.Discovery;

namespace Vixxo.Tests;

public class UdpDiscoveryTests
{
    [Fact]
    public async Task UdpDiscoveryResponder_RepliesToDiscoveryRequest()
    {
        int discoveryPort = 49102; // Ephemeral test port
        await using var responder = new UdpDiscoveryResponder(discoveryPort)
        {
            ServerName = "TestServer-PC",
            TcpPort = 47800,
            CertificateFingerprint = "AA:BB:CC",
            LocalAddresses = new() { "192.168.1.50" }
        };

        responder.Start();

        try
        {
            using var udpClient = new UdpClient();
            var req = new DiscoveryRequest
            {
                Magic = UdpDiscoveryResponder.DiscoveryMagic,
                Type = "DISCOVER_REQUEST",
                ClientName = "TestClientPhone"
            };

            byte[] reqBytes = JsonSerializer.SerializeToUtf8Bytes(req);
            var endpoint = new IPEndPoint(IPAddress.Loopback, discoveryPort);
            await udpClient.SendAsync(reqBytes, reqBytes.Length, endpoint);

            using var cts = new CancellationTokenSource(2000);
            var result = await udpClient.ReceiveAsync(cts.Token);

            string respJson = Encoding.UTF8.GetString(result.Buffer);
            var resp = JsonSerializer.Deserialize<DiscoveryResponse>(respJson);

            Assert.NotNull(resp);
            Assert.Equal("VIXXO_DISC", resp.Magic);
            Assert.Equal("DISCOVER_RESPONSE", resp.Type);
            Assert.Equal("TestServer-PC", resp.ServerName);
            Assert.Equal(47800, resp.TcpPort);
            Assert.Equal("AA:BB:CC", resp.Fingerprint);
            Assert.Contains("192.168.1.50", resp.Addresses);
        }
        finally
        {
            await responder.StopAsync();
        }
    }
}
