using System.Windows;
using System.Windows.Media.Animation;

namespace DesktopDrawer.Helpers;

/// <summary>
/// A closed-form damped spring (mass = 1) exposed as a WPF easing function.
/// Use <see cref="EasingFunctionBase.EasingMode"/> = EaseIn so the curve is
/// evaluated directly; the other modes re-map the normalised time first.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty StiffnessProperty =
        DependencyProperty.Register(
            nameof(Stiffness),
            typeof(double),
            typeof(SpringEase),
            new PropertyMetadata(240.0));

    public static readonly DependencyProperty DampingProperty =
        DependencyProperty.Register(
            nameof(Damping),
            typeof(double),
            typeof(SpringEase),
            new PropertyMetadata(24.0));

    public static readonly DependencyProperty SecondsProperty =
        DependencyProperty.Register(
            nameof(Seconds),
            typeof(double),
            typeof(SpringEase),
            new PropertyMetadata(0.4));

    /// <summary>Spring constant k. Higher values settle faster and feel snappier.</summary>
    public double Stiffness
    {
        get => (double)GetValue(StiffnessProperty);
        set => SetValue(StiffnessProperty, value);
    }

    /// <summary>Damping coefficient c. Lower values overshoot more.</summary>
    public double Damping
    {
        get => (double)GetValue(DampingProperty);
        set => SetValue(DampingProperty, value);
    }

    /// <summary>
    /// The physical settle time in seconds. The animation's normalised time is
    /// scaled by this value, so the spring feels the same no matter how long the
    /// WPF animation itself lasts.
    /// </summary>
    public double Seconds
    {
        get => (double)GetValue(SecondsProperty);
        set => SetValue(SecondsProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();

    protected override double EaseInCore(double normalizedTime)
    {        if (normalizedTime <= 0)
        {
            return 0;
        }

        if (normalizedTime >= 1)
        {
            return 1;
        }

        double stiffness = Math.Max(1, Stiffness);
        double damping = Math.Max(0, Damping);
        double w0 = Math.Sqrt(stiffness);
        double zeta = damping / (2 * Math.Sqrt(stiffness));
        double t = normalizedTime * Math.Max(0.01, Seconds);

        if (zeta < 1)
        {
            double wd = w0 * Math.Sqrt(1 - (zeta * zeta));
            return 1 - Math.Exp(-zeta * w0 * t) *
                (Math.Cos(wd * t) + ((zeta * w0 / wd) * Math.Sin(wd * t)));
        }

        if (Math.Abs(zeta - 1) < 1e-6)
        {
            return 1 - Math.Exp(-w0 * t) * (1 + (w0 * t));
        }

        double root = Math.Sqrt((zeta * zeta) - 1);
        double r1 = -w0 * (zeta - root);
        double r2 = -w0 * (zeta + root);
        double a = r2 / (r2 - r1);
        double b = -r1 / (r2 - r1);
        return 1 - ((a * Math.Exp(r1 * t)) + (b * Math.Exp(r2 * t)));
    }
}
