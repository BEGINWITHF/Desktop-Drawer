namespace DesktopDrawer.Helpers;

/// <summary>
/// Turns a precision touchpad horizontal swipe into exactly one page change.
///
/// On Windows 11 the window registers as touchpad capable and receives real
/// contact down/up messages (see <see cref="Services.TouchpadContactTracker"/>),
/// so a gesture is exactly "fingers down, move, fingers up". Every delta of one
/// contact sequence belongs to the same gesture: the first time the accumulated
/// distance passes <see cref="NotchUnits"/> the gesture commits one page, and
/// everything after that - including the inertia tail emitted after the fingers
/// leave the pad - is ignored until the fingers touch down again.
///
/// If the touchpad-capable registration is not available the gesture boundary
/// falls back to a quiet period in the delta stream, which is the behaviour of
/// touchpads that only report wheel messages.
/// </summary>
internal sealed class SwipePageGesture
{
    /// <summary>Touchpad travel that commits the gesture to one page.</summary>
    public const double NotchUnits = 60.0;

    /// <summary>Quiet time that separates two swipes when contacts are unknown.</summary>
    public const double IdleMilliseconds = 110.0;

    /// <summary>
    /// Safety net for a lost contact-up message: after this much input without a
    /// lift the next delta is allowed to start a new gesture again.
    /// </summary>
    public const double ContactTimeoutMilliseconds = 400.0;

    private long _lastInputAt;
    private bool _active;
    private double _accumulator;
    private bool _committed;

    private bool _contactTracking;
    private bool _fingersDown;
    private bool _waitingForContact;
    private long _contactEndedAt;

    /// <summary>True once the tracker has reported contact state at least once.</summary>
    public bool ContactTracking => _contactTracking;

    /// <summary>
    /// Reports whether fingers are on the pad. While this is available it is the
    /// authoritative gesture boundary and replaces the quiet-period guess.
    /// </summary>
    public void SetContact(bool down, long nowMilliseconds)
    {
        _contactTracking = true;

        if (down)
        {
            if (!_fingersDown)
            {
                _accumulator = 0;
                _committed = false;
                _waitingForContact = false;
            }

            _fingersDown = true;
            return;
        }

        if (!_fingersDown)
        {
            return;
        }

        _fingersDown = false;
        _waitingForContact = true;
        _contactEndedAt = nowMilliseconds;
    }

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

        if (_contactTracking)
        {
            if (_waitingForContact)
            {
                // Fingers are off the pad: the inertia tail must not page, and a
                // lift that never reached the threshold simply does nothing.
                if (nowMilliseconds - _contactEndedAt < ContactTimeoutMilliseconds)
                {
                    return 0;
                }

                // The up message was lost, so treat this as a fresh gesture.
                _waitingForContact = false;
                _accumulator = 0;
                _committed = false;
            }
        }
        else if (!_active || nowMilliseconds - _lastInputAt >= IdleMilliseconds)
        {
            _accumulator = 0;
            _committed = false;
        }

        if (_accumulator != 0 && Math.Sign(delta) != Math.Sign(_accumulator))
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
        _waitingForContact = false;
    }
}
