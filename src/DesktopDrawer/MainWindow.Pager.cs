using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DesktopDrawer;

/// <summary>
/// The pager uses one shared highlight that slides between page numbers, so the
/// indicator reads as a single object travelling with the page change instead of
/// a background that pops from button to button.
/// </summary>
public partial class MainWindow
{
    private static readonly Color ActiveDotColor = Color.FromRgb(0x0B, 0x10, 0x20);

    private readonly HashSet<SolidColorBrush> _pagerBrushes = new();

    private bool _indicatorReady;
    private int _pagerLastIndex = -1;

    /// <summary>Queues a pager refresh for after the new items have been laid out.</summary>
    private void SchedulePagerUpdate(int previousIndex)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (_cleanedUp)
            {
                return;
            }

            UpdatePagerVisuals(previousIndex);
        }));
    }

    private void UpdatePagerVisuals(int previousIndex)
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
            IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
            IndicatorTransform.X = targetX;
            _indicatorReady = true;
        }
        else
        {
            AnimateIndicatorTo(targetX, previousIndex);
        }

        AnimateDots();
    }

    private void AnimateIndicatorTo(double targetX, int previousIndex)
    {
        double currentX = IndicatorTransform.X;

        if (Math.Abs(targetX - currentX) < 0.5)
        {
            return;
        }

        // Continue from where the highlight is actually drawn, so a flip during
        // an ongoing animation does not jump back to the previous target.
        IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
        IndicatorTransform.X = currentX;

        int from = previousIndex < 0 ? _pageIndex : previousIndex;
        int distance = Math.Abs(_pageIndex - from);
        double milliseconds = Math.Clamp(180 + (distance * 20), 140, 300);

        var animation = new DoubleAnimation(currentX, targetX, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        IndicatorTransform.BeginAnimation(
            TranslateTransform.XProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateDots()
    {
        Color muted = ((SolidColorBrush)FindResource("TextMutedBrush")).Color;

        for (int index = 0; index < PageDots.Count; index++)
        {
            Button? button = GetPageButton(index);
            if (button is null)
            {
                continue;
            }

            bool current = index == _pageIndex;
            AnimateDotColor(button, current ? ActiveDotColor : muted);
            AnimateDotScale(button, current ? 1.04 : 1.0);
        }
    }

    private void AnimateDotColor(Button button, Color target)
    {
        if (button.Foreground is not SolidColorBrush brush || !_pagerBrushes.Contains(brush))
        {
            Color start = button.Foreground is SolidColorBrush existing
                ? existing.Color
                : ((SolidColorBrush)FindResource("TextMutedBrush")).Color;

            brush = new SolidColorBrush(start);
            _pagerBrushes.Add(brush);
            button.Foreground = brush;
        }

        Color current = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = current;

        var animation = new ColorAnimation(current, target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        brush.BeginAnimation(
            SolidColorBrush.ColorProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateDotScale(Button button, double target)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, 1);
            button.RenderTransform = scale;
        }

        double current = scale.ScaleX;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = current;
        scale.ScaleY = current;

        if (Math.Abs(target - current) < 0.001)
        {
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        TimeSpan duration = TimeSpan.FromMilliseconds(180);

        scale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(current, target, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);

        scale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(current, target, duration) { EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
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

        x = SnapToPixel(x);

        if (Math.Abs(x - IndicatorTransform.X) < 0.5)
        {
            return;
        }

        IndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
        IndicatorTransform.X = x;
    }
}
