using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DesktopDrawer.Models;

public sealed class PageDot : INotifyPropertyChanged
{
    private bool _isCurrent;
    private string _tooltip = string.Empty;

    public required int Index { get; init; }

    public required string Label { get; init; }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => Set(ref _isCurrent, value);
    }

    public string Tooltip
    {
        get => _tooltip;
        set => Set(ref _tooltip, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
