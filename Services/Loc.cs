using System.Windows;

namespace El3Almey.Services;

public static class Loc
{
    public static event Action? Changed;
    static ResourceDictionary? _current;
    public static string Language { get; private set; } = "en";

    public static void Apply(string lang)
    {
        if (lang != "ar") lang = "en";
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Strings/Strings.{lang}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Add(dict);
        _current = dict;
        Language = lang;
        var fd = lang == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        foreach (Window w in Application.Current.Windows) w.FlowDirection = fd;
        Changed?.Invoke();
    }

    public static string T(string key) => Application.Current.TryFindResource(key) as string ?? key;
}
