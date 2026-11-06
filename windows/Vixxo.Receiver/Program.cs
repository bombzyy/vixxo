using System.Net;
using Vixxo.Core.Audio;
using Vixxo.Core.Server;

namespace Vixxo.Receiver;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("==========================================================");
        Console.WriteLine("   VIXXO - Windows Audio Receiver (Phase 1 Slice)        ");
        Console.WriteLine("==========================================================");

        int port = 47800;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
        }

        // 1. Detect Audio Devices
        Console.WriteLine("\n[1/3] Detecting Audio Endpoints...");
        var playbackDevices = AudioDeviceManager.GetPlaybackDevices();
        Console.WriteLine($"Found {playbackDevices.Count} active playback endpoints:");
        foreach (var dev in playbackDevices)
        {
            string marker = dev.IsVbCable ? " [VIRTUAL CABLE]" : (dev.IsDefault ? " [DEFAULT]" : "");
            Console.WriteLine($"  - {dev.Name}{marker}");
        }

        var vbCableDevice = AudioDeviceManager.FindVbCablePlaybackDevice();
        if (vbCableDevice == null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[WARNING] 'CABLE Input (VB-Audio Virtual Cable)' was not found!");
            Console.WriteLine("Falling back to system default playback device for testing.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[OK] Virtual Microphone Target Found: '{vbCableDevice.Name}'");
            Console.ResetColor();
        }

        string targetDeviceId = vbCableDevice?.Id ?? playbackDevices.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("No playback audio devices available on system.");

        var mmDevice = AudioDeviceManager.GetDeviceById(targetDeviceId)
            ?? throw new InvalidOperationException($"Failed to open audio endpoint: {targetDeviceId}");

        // 2. Initialize Audio Pipeline
        Console.WriteLine("\n[2/3] Initializing WASAPI Audio Renderer & Circular Jitter Buffer...");
        var jitterBuffer = new CircularJitterBuffer(
            sampleRate: 48000,
            channels: 1,
            bitDepth: 16,
            capacityMs: 500,
            latencyMode: LatencyMode.Balanced);

        using var renderer = new WasapiAudioRenderer(jitterBuffer);
        renderer.Start(mmDevice, latencyMs: 50);
        Console.WriteLine($"[OK] WASAPI rendering active on: {renderer.CurrentDeviceName}");
        Console.WriteLine(">>> Audio received will be routed directly to CABLE Input.");
        Console.WriteLine(">>> Windows apps (Discord, Zoom, Sound Recorder) will receive it via CABLE Output.\n");

        // 3. Initialize TCP Server
        Console.WriteLine($"[3/3] Starting Vixxo TCP Server on port {port}...");
        await using var server = new VixxoServer(jitterBuffer, port: port, listenAddress: IPAddress.Any);

        server.LogMessage += msg =>
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {msg}");
        };

        server.ClientConnected += clientName =>
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n>>> [CLIENT STREAMING] Connected: '{clientName}' <<<");
            Console.ResetColor();
        };

        server.ClientDisconnected += reason =>
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n>>> [CLIENT DISCONNECTED] Reason: {reason} <<<");
            Console.ResetColor();
        };

        server.Start();

        Console.WriteLine("\n==========================================================");
        Console.WriteLine("                   SERVER READY & LISTENING               ");
        Console.WriteLine("==========================================================");
        Console.WriteLine("Commands:");
        Console.WriteLine("  [t] Play 2-second 440 Hz test tone into CABLE Input");
        Console.WriteLine("  [m] Toggle Mute");
        Console.WriteLine("  [+] Increase Gain (+3 dB)");
        Console.WriteLine("  [-] Decrease Gain (-3 dB)");
        Console.WriteLine("  [s] Display Live Audio & Buffer Diagnostics");
        Console.WriteLine("  [q] Quit Server");
        Console.WriteLine("==========================================================\n");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        // Real-time meter task
        var meterTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (server.State == ServerState.Streaming || renderer.PeakLevel > 0.01f)
                {
                    int barLength = (int)(renderer.PeakLevel * 30);
                    barLength = Math.Clamp(barLength, 0, 30);
                    string meterBar = new string('#', barLength).PadRight(30, '-');

                    Console.Write($"\r[Live Meter] [{meterBar}] Peak: {renderer.PeakLevel:F2} | Buffer: {renderer.CurrentBufferDepthMs:F1}ms | Drops: {renderer.OverrunDropCount} | Underruns: {renderer.UnderrunCount} | RTT: {server.LastRttMs:F1}ms   ");
                }
                await Task.Delay(100, cts.Token);
            }
        }, cts.Token);

        // Command processing loop
        while (!cts.Token.IsCancellationRequested)
        {
            if (!Console.IsInputRedirected)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    switch (key.KeyChar)
                    {
                        case 't':
                        case 'T':
                            Console.WriteLine("\n[Self-Test] Playing 2-second 440 Hz tone into CABLE Input...");
                            await renderer.PlaySelfTestToneAsync(durationMs: 2000, frequency: 440.0f, amplitude: 0.35f, ct: cts.Token);
                            Console.WriteLine("\n[Self-Test] Completed. Check recording in Windows Sound Recorder / CABLE Output.\n");
                            break;

                        case 'm':
                        case 'M':
                            renderer.IsMuted = !renderer.IsMuted;
                            Console.WriteLine($"\n[Audio] Mute toggled: {(renderer.IsMuted ? "MUTED" : "UNMUTED")}\n");
                            break;

                        case '+':
                        case '=':
                            renderer.GainDb += 3.0f;
                            Console.WriteLine($"\n[Audio] Gain increased to {renderer.GainDb:F1} dB\n");
                            break;

                        case '-':
                        case '_':
                            renderer.GainDb -= 3.0f;
                            Console.WriteLine($"\n[Audio] Gain decreased to {renderer.GainDb:F1} dB\n");
                            break;

                        case 's':
                        case 'S':
                            Console.WriteLine("\n--- Status Summary ---");
                            Console.WriteLine($"Server State:    {server.State}");
                            Console.WriteLine($"Render Endpoint: {renderer.CurrentDeviceName}");
                            Console.WriteLine($"Jitter Buffer:   {renderer.CurrentBufferDepthMs:F1} ms");
                            Console.WriteLine($"Audio Frames In: {server.TotalAudioFramesReceived}");
                            Console.WriteLine($"Audio Bytes In:  {server.TotalAudioBytesReceived / 1024} KB");
                            Console.WriteLine($"Underruns:       {renderer.UnderrunCount}");
                            Console.WriteLine($"Overrun Drops:   {renderer.OverrunDropCount}");
                            Console.WriteLine($"Last RTT:        {server.LastRttMs:F1} ms");
                            Console.WriteLine($"Current Gain:    {renderer.GainDb:F1} dB (Muted: {renderer.IsMuted})");
                            Console.WriteLine("----------------------\n");
                            break;

                        case 'q':
                        case 'Q':
                            Console.WriteLine("\n[Server] Quitting...");
                            cts.Cancel();
                            break;
                    }
                }
            }

            await Task.Delay(50);
        }

        try { await meterTask; } catch { }
        renderer.Stop();
        await server.StopAsync();
        Console.WriteLine("[Server] Shutdown complete.");
        return 0;
    }
}
