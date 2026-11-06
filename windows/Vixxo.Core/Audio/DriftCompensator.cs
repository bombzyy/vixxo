namespace Vixxo.Core.Audio;

public sealed class DriftCompensator
{
    private readonly CircularJitterBuffer _jitterBuffer;
    private readonly int _targetDepthMs;

    private float _smoothedDepthMs = 80.0f;
    private const float SmoothingAlpha = 0.05f; // Exponential moving average

    public float CurrentSpeedRatio { get; private set; } = 1.0f;
    public float SmoothedDepthMs => _smoothedDepthMs;

    public DriftCompensator(CircularJitterBuffer jitterBuffer, int targetDepthMs = 80)
    {
        _jitterBuffer = jitterBuffer ?? throw new ArgumentNullException(nameof(jitterBuffer));
        _targetDepthMs = targetDepthMs;
        _smoothedDepthMs = targetDepthMs;
    }

    public void Update()
    {
        float instantDepth = _jitterBuffer.CurrentDepthMs;
        _smoothedDepthMs = (SmoothingAlpha * instantDepth) + ((1.0f - SmoothingAlpha) * _smoothedDepthMs);

        float upperThreshold = _targetDepthMs * 1.30f;
        float lowerThreshold = _targetDepthMs * 0.70f;

        if (_smoothedDepthMs > upperThreshold)
        {
            // Phone is slightly fast; gently consume faster (+0.2%)
            CurrentSpeedRatio = 1.002f;
        }
        else if (_smoothedDepthMs < lowerThreshold && !_jitterBuffer.IsBuffering)
        {
            // Phone is slightly slow; gently slow down (-0.2%)
            CurrentSpeedRatio = 0.998f;
        }
        else
        {
            CurrentSpeedRatio = 1.000f;
        }
    }

    public void Reset()
    {
        _smoothedDepthMs = _targetDepthMs;
        CurrentSpeedRatio = 1.0f;
    }
}
