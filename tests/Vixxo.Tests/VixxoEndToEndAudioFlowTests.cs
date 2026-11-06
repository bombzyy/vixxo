using System.Net;
using System.Net.Sockets;
using Vixxo.Core.Audio;
using Vixxo.Core.Protocol;
using Vixxo.Core.Server;

namespace Vixxo.Tests;

public class VixxoEndToEndAudioFlowTests
{
    [Fact]
    public async Task Server_AcceptsTestClient_StreamsAudioAndMeasuresPing()
    {
        var jitterBuffer = new CircularJitterBuffer(48000, 1, 16, capacityMs: 500, LatencyMode.Low);
        int testPort = 48721; // Ephemeral port

        await using var server = new VixxoServer(jitterBuffer, port: testPort, listenAddress: IPAddress.Loopback);
        server.Start();

        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, testPort);
            tcpClient.NoDelay = true;

            var stream = tcpClient.GetStream();
            var reader = new VixxoFrameReader(stream);
            var writer = new VixxoFrameWriter(stream);

            // 1. Send HELLO
            var hello = new HelloPayload("TestRunner", "1.0", 1, "test-device-id", new() { new AudioFormatDescriptor() });
            await writer.WriteJsonAsync(MessageType.Hello, hello);

            // 2. Send AUTH
            var auth = new AuthPayload("testSender", "test-token", "test-device-id");
            await writer.WriteJsonAsync(MessageType.Auth, auth);

            // 3. Receive WELCOME
            var (welcomeHeader, welcomePayload) = await reader.ReadFrameAsync();
            Assert.Equal(MessageType.Welcome, welcomeHeader.Type);
            var welcome = ProtocolJson.Deserialize<WelcomePayload>(welcomePayload);
            Assert.NotNull(welcome);
            Assert.True(welcome.Authenticated);

            // 4. Send 5 Audio frames (each 1920 bytes)
            byte[] pcmChunk = new byte[1920];
            Array.Fill(pcmChunk, (byte)0x3C);

            for (ulong i = 0; i < 5; i++)
            {
                var audioHeader = new AudioHeader(i, i * 20000, 960, AudioFrameFlags.None);
                await writer.WriteAudioFrameAsync(audioHeader, pcmChunk);
            }

            // 5. Send PING, verify PONG
            ulong pingTs = 5551234;
            await writer.WritePingAsync(pingTs);
            var (pongHeader, pongPayload) = await reader.ReadFrameAsync();
            Assert.Equal(MessageType.Pong, pongHeader.Type);
            ulong receivedPongTs = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(pongPayload);
            Assert.Equal(pingTs, receivedPongTs);

            // Allow short moment for server processing
            await Task.Delay(50);

            Assert.Equal(5ul, server.TotalAudioFramesReceived);
            Assert.Equal(5ul * 1920, server.TotalAudioBytesReceived);

            // 6. Send BYE
            await writer.WriteFrameAsync(MessageType.Bye, ReadOnlyMemory<byte>.Empty);
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
