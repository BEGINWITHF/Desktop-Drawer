namespace DesktopDrawer.Helpers;

/// <summary>
/// Turns a precision touchpad horizontal swipe into exactly one page change.
///
/// Windows delivers a swipe as a continuous stream of small horizontal deltas
/// followed by an inertia tail. There is no "fingers lifted" message that can be
/// used here (the digitizer raw-input monitor caused driver feedback beeps), so
/// the gesture ends when the stream has been quiet for
/// <see cref="IdleMilliseconds"/>. Every delta of one gesture is consumed by the
/// same gesture: the first time the accumulated distance passes
/// <see cref="NotchUnits"/> the gesture commits one page and every later delta
/// of that gesture - including the inertia tail - is ignored. A direction
/// reversal counts as a new deliberate gesture.
/// </summary>
internal sealed class SwipePageGesture
{
    /// <summary>Touchpad travel that commits the gesture to one page.</summary>
    public const double NotchUnits = 60.0;

    /// <summary>Quiet time that separates two swipes.</summary>
    public const double IdleMilliseconds = 110.0;

    private long _lastInputAt;
    private bool _active;
    private double _accumulator;
    private bool _committed;

    /// <summary>
    /// Feeds one horizontal delta and returns the direction of the page change
    /// the gesture just committed (1 or -1), or 0 when no page should move.
    /// </summary>
    public int Observe(double delta, long nowMilliseconds)
    {
        if (delta == 0)
        {
            return 0;
        }

        bool continuous = _active &&
                          nowMilliseconds - _lastInputAt < IdleMilliseconds;

        if (!continuous)
        {
            _accumulator = 0;
            _committed = false;
        }
        else if (_accumulator != 0 && Math.Sign(delta) != Math.Sign(_accumulator))
        {
            // Reversing mid-gesture is a new intent, not more of the old swipe.
            _accumulator = 0;
            _committed = false;
        }

        _active = true;
        _lastInputAt = nowMilliseconds;

        if (_committed)
        {
            return 0;
        }

        _accumulator += delta;
        if (Math.Abs(_accumulator) < NotchUnits)
        {
            return 0;
        }

        _committed = true;
        _accumulator = 0;
        return delta > 0 ? 1 : -1;
    }

    /// <summary>Forgets the current gesture (page set changed, drawer closed).</summary>
    public void Reset()
    {
        _active = false;
        _accumulator = 0;
        _committed = false;
    }
}
