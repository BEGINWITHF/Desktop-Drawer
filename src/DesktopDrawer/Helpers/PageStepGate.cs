namespace DesktopDrawer.Helpers;

/// <summary>
/// Turns a stream of wheel or touchpad deltas into page steps that are paced at
/// a fixed interval.
///
/// One page step is emitted for every <see cref="NotchUnits"/> of accumulated
/// input, but never more often than <see cref="StepIntervalMilliseconds"/>.
/// Input that arrives during the interval is kept, so a continuous gesture keeps
/// turning pages instead of losing notches or releasing several pages at once.
/// A direction reversal drops the stale remainder so the new intent is never
/// swallowed by input from the other direction.
///
/// The pacing model follows the pager policy used by FluidBar
/// (https://github.com/Doulor/FluidBar): accumulate, then commit at most one
/// step per interval and subtract a single notch, keeping the rest.
/// </summary>
internal sealed class PageStepGate
{
    /// <summary>Input units that make up one mouse wheel notch.</summary>
    public const double NotchUnits = 120.0;

    /// <summary>
    /// Minimum time between two page steps. This is the single knob that sets
    /// the page-change cadence for every input source.
    /// </summary>
    public const double StepIntervalMilliseconds = 140.0;

    private readonly double _notch;
    private double _accumulator;
    private long _lastStepAt;
    private bool _stepped;

    public PageStepGate(double notch = NotchUnits)
    {
        _notch = Math.Max(1, notch);
    }

    /// <summary>Whether enough input is buffered for another step.</summary>
    public bool HasQueuedStep => Math.Abs(_accumulator) >= _notch;

    /// <summary>
    /// Adds input and returns the direction of a step that may run right now
    /// (1 or -1), or 0 when the step is still accumulating or the interval has
    /// not elapsed yet.
    /// </summary>
    public int Add(double delta, long nowMilliseconds)
    {
        if (delta == 0)
        {
            return 0;
        }

        if (_accumulator != 0 && Math.Sign(delta) != Math.Sign(_accumulator))
        {
            // A reversal is a new intent; keep the remainder only when it moves
            // in the same direction.
            _accumulator = 0;
        }

        _accumulator += delta;

        // Keep at most one notch queued. Without this, a long fast spin would
        // bank pages faster than the interval can release them and the strip
        // would keep flipping after the user stopped.
        if (Math.Abs(_accumulator) > _notch)
        {
            _accumulator = Math.Sign(_accumulator) * _notch;
        }

        return Take(nowMilliseconds);
    }

    /// <summary>
    /// Returns the direction of a step that may run right now, or 0. Call this
    /// from a tail timer to release input that was buffered when the gesture
    /// stopped between two intervals.
    /// </summary>
    public int Take(long nowMilliseconds)
    {
        if (_stepped && nowMilliseconds - _lastStepAt < StepIntervalMilliseconds)
        {
            return 0;
        }

        if (Math.Abs(_accumulator) < _notch)
        {
            return 0;
        }

        int direction = _accumulator > 0 ? 1 : -1;
        _accumulator -= direction * _notch;
        _lastStepAt = nowMilliseconds;
        _stepped = true;
        return direction;
    }

    /// <summary>
    /// Milliseconds until buffered input may turn into a step, or -1 when
    /// nothing is buffered.
    /// </summary>
    public long NextStepDelay(long nowMilliseconds)
    {
        if (!HasQueuedStep)
        {
            return -1;
        }

        if (!_stepped)
        {
            return 0;
        }

        long elapsed = nowMilliseconds - _lastStepAt;
        return Math.Max(0, (long)Math.Ceiling(StepIntervalMilliseconds - elapsed));
    }

    /// <summary>Drops buffered input (filters, grouping or density changed).</summary>
    public void Reset()
    {
        _accumulator = 0;
        _stepped = false;
    }
}
