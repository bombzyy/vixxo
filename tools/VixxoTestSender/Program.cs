using System.Net.Sockets;
using Vixxo.Core.Protocol;

namespace VixxoTestSender;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("    Vixxo Test Sender Tool (Synthetic Audio)      ");
        Console.WriteLine("==================================================");
        Console.WriteLine("NOTE: This tool generates test audio strictly for verification");
        Console.WriteLine("and is NOT included in the production release.\n");

        string host = "127.0.0.1";
        int port = 47800;
        int durationSec = 10;
        float frequency = 440.0f; // A4 standard test tone

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--host" && i + 1 < args.Length) host = args[++i];
            if (args[i] == "--port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            if (args[i] == "--duration" && i + 1 < args.Length) int.TryParse(args[++i], out durationSec);
            if (args[i] == "--freq" && i + 1 < args.Length) float.TryParse(args[++i], out frequency);
        }

        Console.WriteLine($"Target: {host}:{port}");
        Console.WriteLine($"Tone: {frequency} Hz Sine Wave, 48 kHz Mono 16-bit PCM");
        Console.WriteLine($"Duration: {durationSec} seconds\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            Console.WriteLine("\n[TestSender] Stopping...");
        };

        try
        {
            Console.Write($"[TestSender] Connecting to {host}:{port}... ");
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(host, port, cts.Token);
            tcpClient.NoDelay = true;
            Console.WriteLine("Connected!");

            var stream = tcpClient.GetStream();
            var reader = new VixxoFrameReader(stream);
            var writer = new VixxoFrameWriter(stream);

            // Step 1: Send HELLO
            Console.Write("[TestSender] Sending HELLO... ");
            var hello = new HelloPayload(
                ClientName: "VixxoTestSender-CLI",
                ClientVersion: "1.0.0",
                ProtocolVersion: 1,
                DeviceId: Guid.NewGuid().ToString("D"),
                SupportedFormats: new() { new AudioFormatDescriptor(48000, 1, 16, 20) });

            await writer.WriteJsonAsync(MessageType.Hello, hello, cts.Token);
            Console.WriteLine("OK");

            // Step 2: Send AUTH
            Console.Write("[TestSender] Sending AUTH (testSender)... ");
            var auth = new AuthPayload(
                AuthMethod: "testSender",
                Token: "synthetic-test-token",
                DeviceId: hello.DeviceId);

            await writer.WriteJsonAsync(MessageType.Auth, auth, cts.Token);
            Console.WriteLine("OK");

            // Step 3: Receive WELCOME
            Console.Write("[TestSender] Waiting for WELCOME... ");
            var (welcomeHeader, welcomeBytes) = await reader.ReadFrameAsync(cts.Token);
            if (welcomeHeader.Type != MessageType.Welcome)
            {
                throw new Exception($"Expected WELCOME, received {welcomeHeader.Type}");
            }
            var welcome = ProtocolJson.Deserialize<WelcomePayload>(welcomeBytes);
            Console.WriteLine($"OK! Session: {welcome?.SessionId}, Server: {welcome?.ServerName}");

            // Step 4: Send START
            Console.Write("[TestSender] Sending START... ");
            await writer.WriteJsonAsync(MessageType.Start, new StartPayload("test_stream", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), cts.Token);
            Console.WriteLine("Streaming started!\n");

            // Step 5: Streaming loop (48 kHz mono 16-bit = 960 samples per 20 ms frame = 1920 bytes)
            const int sampleRate = 48000;
            const int frameMs = 20;
            const int samplesPerFrame = (sampleRate * frameMs) / 1000; // 960
            byte[] pcmBuffer = new byte[samplesPerFrame * 2]; // 1920 bytes

            double phase = 0.0;
            double phaseIncrement = 2.0 * Math.PI * frequency / sampleRate;
            float amplitude = 0.35f; // Safe audible level (-9 dBFS)

            ulong seq = 0;
            int totalFramesToSend = durationSec > 0 ? (durationSec * 1000) / frameMs : int.MaxValue;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            Console.WriteLine("Press Ctrl+C to stop streaming early.\n");

            while (seq < (ulong)totalFramesToSend && !cts.IsCancellationRequested)
            {
                // Generate sine wave samples
                for (int s = 0; s < samplesPerFrame; s++)
                {
                    short sample = (short)(Math.Sin(phase) * amplitude * 32767.0);
                    phase += phaseIncrement;
                    if (phase >= 2.0 * Math.PI) phase -= 2.0 * Math.PI;

                    pcmBuffer[s * 2] = (byte)(sample & 0xFF);
                    pcmBuffer[s * 2 + 1] = (byte)((sample >> 8) & 0xFF);
                }

                ulong timestampUs = (ulong)(DateTime.UtcNow.Ticks / 10);
                var audioHeader = new AudioHeader(seq, timestampUs, (ushort)samplesPerFrame, AudioFrameFlags.SyntheticTestTone);

                await writer.WriteAudioFrameAsync(audioHeader, pcmBuffer, cts.Token);
                seq++;

                if (seq % 50 == 0) // Every 1 second
                {
                    Console.Write($"\r[Streaming] Sent {seq} frames ({seq * 20 / 1000}s) | Elapsed: {stopwatch.Elapsed:mm\\:ss\\.ff}   ");
                }

                // Precise 20 ms pacing
                long targetElapsedMs = (long)seq * frameMs;
                long actualElapsedMs = stopwatch.ElapsedMilliseconds;
                long sleepMs = targetElapsedMs - actualElapsedMs;
                if (sleepMs > 0)
                {
                    await Task.Delay((int)sleepMs, cts.Token);
                }
            }

            Console.WriteLine($"\n\n[TestSender] Finished streaming {seq} frames ({seq * 1920 / 1024} KB).");

            // Step 6: Send STOP & BYE
            try
            {
                await writer.WriteJsonAsync(MessageType.Stop, new StopPayload("test_complete", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), CancellationToken.None);
                await writer.WriteFrameAsync(MessageType.Bye, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
            }
            catch { }

            Console.WriteLine("[TestSender] Completed cleanly.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[TestSender] Session cancelled by user.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[TestSender Error] {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }
}
