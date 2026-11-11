using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDrawer.Helpers;

namespace DesktopDrawer;

/// <summary>
/// The pager uses one shared pill that slides between page numbers with spring
/// physics. A second, clipped copy of the tab row travels with it, so the digit
/// under the pill is the only one drawn in the contrast colour - exactly like a
/// label that changes colour only where the pill covers it.
/// </summary>
public partial class MainWindow
{
    private bool _indicatorReady;
    private bool _pagerOverflow;

    /// <summary>
    /// Extra room added to the trailing end of the pager when the page dots no
    /// longer fit, so the last dot has space to fade out instead of being cut.
    /// </summary>
    private const double PagerFadeTail = 28;

    /// <summary>
    /// How far before the resting edge the dissolve begins. Together with the
    /// tail this is the length of the gradient.
    /// </summary>
    private const double PagerFadeLead = 6;

    /// <summary>
    /// Eased opacity ramp for the dissolve, following a cosine curve so the
    /// gradient starts and ends without the hard shoulders of a linear fade.
    /// Values are offsets into the fade span with the matching opacity.
    /// </summary>
    private static readonly (double Offset, double Opacity)[] PagerFadeRamp =
    [
        (0.00, 1.000),
        (0.10, 0.976),
        (0.20, 0.905),
        (0.30, 0.794),
        (0.40, 0.655),
        (0.50, 0.500),
        (0.60, 0.345),
        (0.70, 0.206),
        (0.80, 0.095),
        (0.90, 0.024),
        (1.00, 0.000)
    ];

    /// <summary>Queues a pager refresh for after the new items have been laid out.</summary>
    private void SchedulePagerUpdate()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_cleanedUp)
            {
                return;
            }

            UpdatePagerVisuals();
        }));
    }

    private void UpdatePagerVisuals()
    {
        if (!ShowPageDots || PageDots.Count == 0)
        {
            _indicatorReady = false;
            PageIndicator.Visibility = Visibility.Collapsed;
            SetPagerOverflow(false);
            return;
        }

        PageItems.UpdateLayout();
        PageIndicator.Visibility = Visibility.Visible;

        SetPagerOverflow(IsPagerContentOverflowing());
        UpdatePagerFade();

        double targetX = GetPageIndicatorX(_pageIndex);
        if (double.IsNaN(targetX))
        {
            _indicatorReady = false;
            return;
        }

        targetX = SnapToPixel(targetX);

        if (!_indicatorReady)
        {
            SetIndicatorPosition(targetX);
            _indicatorReady = true;
        }
        else
        {
            AnimateIndicatorTo(targetX);
        }
    }

    /// <summary>
    /// True when the tab row is wider than the pager's configured width. The
    /// test uses the base width without the fade tail, so extending the bar
    /// cannot make it flip back and forth.
    /// </summary>
    private bool IsPagerContentOverflowing()
    {
        if (PageDots.Count == 0)
        {
            return false;
        }

        double dotsWidth = PageItems.DesiredSize.Width;
        if (dotsWidth <= 0)
        {
            dotsWidth = PageDots.Count * 42.0;
        }

        double contentWidth =
            WidthOf(PrevButton, 28) +
            WidthOf(PagerPageLabel, 90) +
            PagerPageLabel.Margin.Left + PagerPageLabel.Margin.Right +
            dotsWidth +
            WidthOf(NextButton, 28) +
            NextButton.Margin.Left + NextButton.Margin.Right;

        double width = PagerBar.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = PagerBar.ActualWidth;
        }

        if (_pagerOverflow)
        {
            width -= PagerFadeTail;
        }

        double innerWidth = width -
            PagerBar.Padding.Left -
            PagerBar.Padding.Right -
            PagerBar.BorderThickness.Left -
            PagerBar.BorderThickness.Right;

        return innerWidth > 0 && contentWidth > innerWidth + 0.5;
    }

    private static double WidthOf(FrameworkElement element, double fallback) =>
        element.ActualWidth > 0 ? element.ActualWidth : fallback;

    private void SetPagerOverflow(bool overflow)
    {
        if (_pagerOverflow == overflow)
        {
            return;
        }

        _pagerOverflow = overflow;

        if (!overflow)
        {
            PagerContent.OpacityMask = null;
        }

        ApplyChromeLayout();
        PagerBar.UpdateLayout();
    }

    /// <summary>
    /// Fades the page dots and the sliding pill near the trailing edge so an
    /// overflowing tab row dissolves instead of being clipped. The bar surface
    /// and its outer border stay fully drawn, which keeps the pager silhouette.
    /// </summary>
    private void UpdatePagerFade()
    {
        if (!_pagerOverflow)
        {
            PagerContent.OpacityMask = null;
            return;
        }

        double width = PagerBar.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = PagerBar.ActualWidth;
        }

        double contentWidth = PagerContent.ActualWidth;
        if (width <= 1 || contentWidth <= 1)
        {
            return;
        }

        Point origin;
        try
        {
            origin = PagerContent.TransformToAncestor(PagerBar).Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // The fade starts a little before the resting edge and reaches full
        // transparency at the end of the tail. That makes the extra length read
        // as the end of the bar dissolving, not as a clipped dot.
        double baseWidth = width - PagerFadeTail;
        double baseInnerRight = baseWidth -
            PagerBar.BorderThickness.Right -
            PagerBar.Padding.Right;
        double innerRight = width -
            PagerBar.BorderThickness.Right -
            PagerBar.Padding.Right;

        double fadeStart = Math.Max(0, baseInnerRight - origin.X - PagerFadeLead);
        double fadeEnd = innerRight - origin.X;

        if (fadeEnd <= 0)
        {
            PagerContent.OpacityMask = null;
            return;
        }

        fadeEnd = Math.Min(fadeEnd, contentWidth);
        fadeStart = Math.Min(fadeStart, fadeEnd);
        double fadeSpan = fadeEnd - fadeStart;

        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(contentWidth, 0)
        };

        double solid = Math.Clamp(fadeStart / contentWidth, 0, 1);
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));

        if (fadeSpan <= 0.5)
        {
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0, 255, 255, 255),
                solid));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Colors.White, solid));

            foreach ((double offset, double opacity) in PagerFadeRamp)
            {
                if (offset <= 0)
                {
                    continue;
                }

                byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
                double position = Math.Clamp(
                    (fadeStart + fadeSpan * offset) / contentWidth,
                    solid,
                    1);
                brush.GradientStops.Add(new GradientStop(
                    Color.FromArgb(alpha, 255, 255, 255),
                    position));
            }
        }

        PagerContent.OpacityMask = brush;
    }

    private void AnimateIndicatorTo(double targetX)
    {
        // Read the effective value (animations included), not the base value.
        double currentX = (double)IndicatorTransform.GetValue(TranslateTransform.XProperty);

        if (Math.Abs(targetX - currentX) < 0.5)
        {
            SetIndicatorPosition(currentX);
            return;
        }

        // Continue from where the pill is actually drawn, so a flip during an
        // ongoing animation does not jump back to the previous target.
        SetIndicatorPosition(currentX);

        TimeSpan duration = TimeSpan.FromMilliseconds(430);

        var indicatorAnimation = new DoubleAnimation(currentX, targetX, duration)
        {
            EasingFunction = CreateSpring()
        };

        var clipAnimation = new DoubleAnimation(currentX, targetX, duration)
        {
            EasingFunction = CreateSpring()
        };

        IndicatorTransform.BeginAnimation(
            TranslateTransform.XProperty,
            indicatorAnimation,
            HandoffBehavior.SnapshotAndReplace);

        ActiveClipGeometryTransform.BeginAnimation(
            TranslateTransform.XProperty,
            clipAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// The reference motion is a damped spring: a quick glide with a small
    /// overshoot that settles in roughly a third of a second.
    /// </summary>
    private static SpringEase CreateSpring() => new()
    {
        Stiffness = 240,
        Damping = 24,
        Seconds = 0.43,
        EasingMode = EasingMode.EaseIn
    };

    /// <summary>
    /// Pins the pill and its clip window at the same position, so the contrast
    /// digit is only ever visible directly under the pill.
    /// </summary>
    private void SetIndicatorPosition(double x)
    {
        IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
        IndicatorTransform.X = x;

        ActiveClipGeometryTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ActiveClipGeometryTransform.X = x;
    }

    private double GetPageIndicatorX(int index)
    {
        Button? button = GetPageButton(index);
        if (button is null)
        {
            return double.NaN;
        }

        // The canvas is a sibling of the items control, so measure against their
        // shared parent and then remove the canvas offset.
        Point origin = IndicatorCanvas.TransformToAncestor(DotsHost).Transform(new Point(0, 0));
        Point target = button.TransformToAncestor(DotsHost).Transform(new Point(0, 0));

        return target.X - origin.X;
    }

    private Button? GetPageButton(int index)
    {
        if (PageItems.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container)
        {
            return null;
        }

        return FindVisualChild<Button>(container);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is T match)
            {
                return match;
            }

            T? nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private double SnapToPixel(double value)
    {
        double scale = VisualTreeHelper.GetDpi(IndicatorCanvas).DpiScaleX;
        return scale <= 0 ? value : Math.Round(value * scale) / scale;
    }

    private void DotsHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_cleanedUp || !_indicatorReady || !ShowPageDots)
        {
            return;
        }

        double x = GetPageIndicatorX(_pageIndex);
        if (double.IsNaN(x))
        {
            return;
        }

        SetIndicatorPosition(SnapToPixel(x));
    }
}
