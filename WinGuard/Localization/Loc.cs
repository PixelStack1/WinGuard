using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using System.Windows.Markup;

namespace WinGuard.Localization;

/// <summary>Live-switchable FR/EN localization. XAML binds via {loc:Tr Key}; code uses Loc.T("Key").</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? LanguageChanged;

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGuard", "language.txt");

    private string _lang = "fr";

    private Loc()
    {
        try { if (File.Exists(SettingsFile)) { string l = File.ReadAllText(SettingsFile).Trim(); if (l is "en" or "fr") _lang = l; } } catch { }
    }

    public string Language
    {
        get => _lang;
        set
        {
            value = value == "en" ? "en" : "fr";
            if (_lang == value) return;
            _lang = value;
            try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, value); } catch { }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFrench)));
            LanguageChanged?.Invoke();
        }
    }

    public bool IsFrench => _lang == "fr";
    public void Toggle() => Language = IsFrench ? "en" : "fr";

    public string this[string key] => T(key);

    public static string T(string key)
    {
        var table = Instance._lang == "en" ? Strings.En : Strings.Fr;
        if (table.TryGetValue(key, out var v)) return v;
        return Strings.En.TryGetValue(key, out var e) ? e : key;
    }

    public static string F(string key, params object?[] args) => string.Format(T(key), args);

    /// <summary>Picks the French or English variant (falls back to English if French is empty).</summary>
    public static string Pick(string en, string? fr) => Instance._lang == "fr" && !string.IsNullOrWhiteSpace(fr) ? fr! : en;
}

/// <summary>Markup extension: Text="{loc:Tr Key}" — updates live when the language changes.</summary>
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) { Key = key; }
    [ConstructorArgument("key")] public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider sp) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(sp);
}
