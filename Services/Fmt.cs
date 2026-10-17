using System.Globalization;

namespace El3Almey.Services;

public static class Fmt
{
    static string F(double d, string f) => d.ToString(f, CultureInfo.InvariantCulture);

    public static string Bytes(long b)
    {
        if (b <= 0) return "0 MB";
        double mb = b / 1048576.0;
        if (mb >= 1024) return F(mb / 1024, "0.0") + " GB";
        return mb >= 10 ? F(mb, "0") + " MB" : F(mb, "0.0") + " MB";
    }

    public static string Views(long v) =>
        v >= 1_000_000_000 ? F(v / 1e9, "0.#") + "B" :
        v >= 1_000_000 ? F(v / 1e6, "0.#") + "M" :
        v >= 1_000 ? F(v / 1e3, "0.#") + "K" : v.ToString(CultureInfo.InvariantCulture);

    public static string Duration(TimeSpan? t)
    {
        if (t == null) return "";
        var d = t.Value;
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
            : $"{d.Minutes}:{d.Seconds:00}";
    }

    public static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.Length > 120) name = name[..120].TrimEnd();
        return string.IsNullOrWhiteSpace(name) ? "video" : name;
    }
}
