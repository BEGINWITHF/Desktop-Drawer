using System.Runtime.InteropServices;
using DesktopDrawer.Interop;

namespace DesktopDrawer.Helpers;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        try
        {
            return NativeMethods.StrCmpLogicalW(x, y);
        }
        catch
        {
            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}
