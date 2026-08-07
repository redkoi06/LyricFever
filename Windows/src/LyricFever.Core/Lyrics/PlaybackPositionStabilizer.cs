namespace LyricFever.Core.Lyrics;

/// <summary>
/// Filters short backward jumps reported by Windows SMTC while preserving real seeks.
/// Forward samples are immediate; a backward seek during playback must be confirmed by
/// multiple coherent samples. Paused seeks and track/loop restarts are immediate.
/// </summary>
public sealed class PlaybackPositionStabilizer
{
    private const double BackwardNoiseToleranceMs = 650;
    private const double PendingSampleBackwardToleranceMs = 300;
    private const double PendingSampleMaximumAdvanceMs = 2500;
    private const double LargeBackwardSeekThresholdMs = 5000;
    private const double LoopPreviousMinimumMs = 30_000;
    private const double LoopNewMaximumMs = 5_000;

    private double? _acceptedPositionMs;
    private double? _pendingBackwardPositionMs;
    private int _pendingBackwardSamples;

    public double? Observe(double observedPositionMs, bool isPlaying)
    {
        if (double.IsNaN(observedPositionMs) || double.IsInfinity(observedPositionMs))
            return null;

        observedPositionMs = Math.Max(0, observedPositionMs);
        if (_acceptedPositionMs is not { } accepted)
            return Accept(observedPositionMs);

        if (!isPlaying || IsRestart(accepted, observedPositionMs))
            return Accept(observedPositionMs);

        if (observedPositionMs >= accepted)
            return Accept(observedPositionMs);

        if (accepted - observedPositionMs <= BackwardNoiseToleranceMs)
        {
            _pendingBackwardPositionMs = null;
            _pendingBackwardSamples = 0;
            return null;
        }

        if (_pendingBackwardPositionMs is not { } pending ||
            observedPositionMs < pending - PendingSampleBackwardToleranceMs ||
            observedPositionMs > pending + PendingSampleMaximumAdvanceMs)
        {
            _pendingBackwardPositionMs = observedPositionMs;
            _pendingBackwardSamples = 1;
            return null;
        }

        _pendingBackwardPositionMs = observedPositionMs;
        _pendingBackwardSamples++;
        var requiredSamples = accepted - observedPositionMs >= LargeBackwardSeekThresholdMs ? 2 : 3;
        if (_pendingBackwardSamples < requiredSamples) return null;

        return Accept(observedPositionMs);
    }

    public void Reset()
    {
        _acceptedPositionMs = null;
        _pendingBackwardPositionMs = null;
        _pendingBackwardSamples = 0;
    }

    private double Accept(double positionMs)
    {
        _acceptedPositionMs = positionMs;
        _pendingBackwardPositionMs = null;
        _pendingBackwardSamples = 0;
        return positionMs;
    }

    private static bool IsRestart(double previousPositionMs, double newPositionMs) =>
        previousPositionMs > LoopPreviousMinimumMs && newPositionMs < LoopNewMaximumMs;
}
