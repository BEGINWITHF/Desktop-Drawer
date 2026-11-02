namespace DesktopDrawer.Helpers;

public static class FileSizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            return string.Empty;
        }

        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        if (unit == 0)
        {
            return $"{value:0} {Units[unit]}";
        }

        return value >= 100 ? $"{value:0} {Units[unit]}" : $"{value:0.#} {Units[unit]}";
    }
}
