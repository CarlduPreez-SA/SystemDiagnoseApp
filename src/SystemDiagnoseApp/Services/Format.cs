namespace SystemDiagnoseApp.Services;

public static class Format
{
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    public static string Bytes(long? bytes) => bytes is null ? "unknown" : Bytes(bytes.Value);

    public static string Percent(double fraction) => $"{fraction * 100:0.#}%";
}
