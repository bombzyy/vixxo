using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Vixxo.Core.Audio;

public sealed class LocalMicrophoneCapture : IDisposable
{
    private readonly object _lock = new();
    private WasapiCapture? _capture;
    private CircularJitterBuffer? _mainJitterBuffer;
    private CircularJitterBuffer? _monitorJitterBuffer;
    private bool _isCapturing;
    private string? _currentDeviceId;
    private string? _currentDeviceName;

    private byte[] _pcmConversionBuffer = new byte[65536];
    private float[] _rawMonoFloatBuffer = new float[32768];
    private float[] _resampledFloatBuffer = new float[32768];

    public const int TargetSampleRate = 48000;
    public const int TargetChannels = 1;
    public const int TargetBitsPerSample = 16;

    public bool IsCapturing => _isCapturing;
    public string? CurrentDeviceId => _currentDeviceId;
    public string? CurrentDeviceName => _currentDeviceName;

    public event Action<string>? CaptureError;
    public event Action<bool>? CaptureStateChanged;

    public void SetMonitorJitterBuffer(CircularJitterBuffer? monitorBuffer)
    {
        lock (_lock)
        {
            _monitorJitterBuffer = monitorBuffer;
        }
    }

    public bool Start(MMDevice device, CircularJitterBuffer mainBuffer, CircularJitterBuffer? monitorBuffer = null)
    {
        if (device == null) throw new ArgumentNullException(nameof(device));
        if (mainBuffer == null) throw new ArgumentNullException(nameof(mainBuffer));

        lock (_lock)
        {
            StopInternal();

            try
            {
                _mainJitterBuffer = mainBuffer;
                _monitorJitterBuffer = monitorBuffer;
                _currentDeviceId = device.ID;
                _currentDeviceName = device.FriendlyName;

                _capture = new WasapiCapture(device)
                {
                    ShareMode = AudioClientShareMode.Shared
                };

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;

                _capture.StartRecording();
                _isCapturing = true;
                CaptureStateChanged?.Invoke(true);
                return true;
            }
            catch (Exception ex)
            {
                StopInternal();
                CaptureError?.Invoke($"Failed to start PC microphone capture: {ex.Message}");
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        if (_capture != null)
        {
            try
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch { }
            _capture = null;
        }

        _isCapturing = false;
        _currentDeviceId = null;
        _currentDeviceName = null;
        CaptureStateChanged?.Invoke(false);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _capture == null) return;

        lock (_lock)
        {
            if (!_isCapturing || _mainJitterBuffer == null) return;

            var waveFormat = _capture.WaveFormat;
            int convertedBytes = ConvertRawBufferTo48kMonoPcm(
                e.Buffer,
                e.BytesRecorded,
                waveFormat,
                ref _rawMonoFloatBuffer,
                ref _resampledFloatBuffer,
                ref _pcmConversionBuffer);

            if (convertedBytes > 0)
            {
                var span = _pcmConversionBuffer.AsSpan(0, convertedBytes);
                _mainJitterBuffer.Enqueue(span);
                _monitorJitterBuffer?.Enqueue(span);
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            _isCapturing = false;
            CaptureStateChanged?.Invoke(false);

            if (e.Exception != null)
            {
                CaptureError?.Invoke($"Microphone capture error: {e.Exception.Message}");
            }
        }
    }

    public static int ConvertRawBufferTo48kMonoPcm(
        byte[] inputBuffer,
        int bytesRecorded,
        WaveFormat format,
        ref float[] rawMonoFloats,
        ref float[] resampledFloats,
        ref byte[] pcmOutputBytes)
    {
        if (bytesRecorded <= 0) return 0;

        int channels = Math.Max(1, format.Channels);
        int sampleRate = format.SampleRate;
        int bitsPerSample = format.BitsPerSample;

        bool isIeeeFloat = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                           bitsPerSample == 32 ||
                           (format is WaveFormatExtensible ext && ext.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));

        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0) bytesPerSample = isIeeeFloat ? 4 : 2;
        int bytesPerFrame = channels * bytesPerSample;
        int inputFrames = bytesRecorded / bytesPerFrame;

        if (inputFrames <= 0) return 0;

        if (rawMonoFloats.Length < inputFrames)
        {
            rawMonoFloats = new float[Math.Max(rawMonoFloats.Length * 2, inputFrames)];
        }

        // 1. Convert any input format to mono float [-1.0, 1.0]
        if (isIeeeFloat && bytesPerSample == 4)
        {
            var floatSpan = MemoryMarshal.Cast<byte, float>(inputBuffer.AsSpan(0, bytesRecorded));
            for (int i = 0; i < inputFrames; i++)
            {
                float sum = 0f;
                int baseIdx = i * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    sum += floatSpan[baseIdx + ch];
                }
                rawMonoFloats[i] = sum / channels;
            }
        }
        else if (bitsPerSample == 16)
        {
            var shortSpan = MemoryMarshal.Cast<byte, short>(inputBuffer.AsSpan(0, bytesRecorded));
            for (int i = 0; i < inputFrames; i++)
            {
                int sum = 0;
                int baseIdx = i * channels;
                for (int ch = 0; ch < channels; ch++)
                {
                    sum += shortSpan[baseIdx + ch];
                }
                rawMonoFloats[i] = (sum / (float)channels) / 32768.0f;
            }
        }
        else if (bitsPerSample == 24)
        {
            for (int i = 0; i < inputFrames; i++)
            {
                double sum = 0;
                int frameOffset = i * bytesPerFrame;
                for (int ch = 0; ch < channels; ch++)
                {
                    int sampleOffset = frameOffset + ch * 3;
                    int s24 = inputBuffer[sampleOffset] |
                              (inputBuffer[sampleOffset + 1] << 8) |
                              ((sbyte)inputBuffer[sampleOffset + 2] << 16);
                    sum += s24 / 8388608.0;
                }
                rawMonoFloats[i] = (float)(sum / channels);
            }
        }
        else
        {
            // Fallback: 16-bit
            int shortCount = bytesRecorded / 2;
            var shortSpan = MemoryMarshal.Cast<byte, short>(inputBuffer.AsSpan(0, shortCount * 2));
            for (int i = 0; i < inputFrames; i++)
            {
                rawMonoFloats[i] = shortSpan[Math.Min(i * channels, shortSpan.Length - 1)] / 32768.0f;
            }
        }

        // 2. Resample to 48,000 Hz if necessary
        int outputFrames;
        float[] monoAt48k;

        if (sampleRate == TargetSampleRate)
        {
            outputFrames = inputFrames;
            monoAt48k = rawMonoFloats;
        }
        else
        {
            outputFrames = (int)Math.Round(inputFrames * ((double)TargetSampleRate / sampleRate));
            if (outputFrames <= 0) return 0;

            if (resampledFloats.Length < outputFrames)
            {
                resampledFloats = new float[Math.Max(resampledFloats.Length * 2, outputFrames)];
            }

            double stepRatio = (double)(inputFrames - 1) / Math.Max(1, outputFrames - 1);
            for (int j = 0; j < outputFrames; j++)
            {
                double srcPos = j * stepRatio;
                int idx0 = (int)srcPos;
                int idx1 = Math.Min(idx0 + 1, inputFrames - 1);
                float frac = (float)(srcPos - idx0);

                resampledFloats[j] = rawMonoFloats[idx0] * (1.0f - frac) + rawMonoFloats[idx1] * frac;
            }

            monoAt48k = resampledFloats;
        }

        // 3. Convert float [-1.0, 1.0] to 16-bit PCM little-endian bytes
        int bytesNeeded = outputFrames * 2;
        if (pcmOutputBytes.Length < bytesNeeded)
        {
            pcmOutputBytes = new byte[Math.Max(pcmOutputBytes.Length * 2, bytesNeeded)];
        }

        for (int i = 0; i < outputFrames; i++)
        {
            float s = Math.Clamp(monoAt48k[i], -1.0f, 1.0f);
            short pcm = (short)(s * 32767.0f);
            pcmOutputBytes[i * 2] = (byte)(pcm & 0xFF);
            pcmOutputBytes[i * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
        }

        return bytesNeeded;
    }

    public void Dispose()
    {
        Stop();
    }
}
