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
            return;
        }

        PageItems.UpdateLayout();
        PageIndicator.Visibility = Visibility.Visible;

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
