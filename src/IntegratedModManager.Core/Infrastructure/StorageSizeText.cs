namespace IntegratedModManager.Core.Infrastructure;

/// <summary>A byte count for people: "512 B", "1.5 KB", "2 GB" (stage J finish, step 8a: moved from the window unchanged).</summary>
public static class StorageSizeText
{
    public static string Format(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
