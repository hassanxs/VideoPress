using Microsoft.Win32;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace VideoPress.Services;

public enum AppTheme { Dark, Light }

public class ThemeService
{
    private static readonly string SettingsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "VideoPress", "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private record Settings(string Theme = "Auto");

    public AppTheme Current { get; private set; } = AppTheme.Dark;

    // ── Apply ────────────────────────────────────────────────────────────────

    public void ApplyTheme(AppTheme theme)
    {
        Current = theme;

        var uri = theme == AppTheme.Light
            ? new Uri("Themes/Light.xaml", UriKind.Relative)
            : new Uri("Themes/Dark.xaml",  UriKind.Relative);

        var dicts = Application.Current.Resources.MergedDictionaries;

        // ADD new dict FIRST so there is never a moment with no theme loaded,
        // then remove the old one. Removing first causes StaticResource lookups
        // in the element tree to fail briefly, cascading error dialogs.
        var newDict = new ResourceDictionary { Source = uri };
        dicts.Insert(0, newDict);

        // Remove any previous theme dicts (skip the one we just added)
        var toRemove = dicts
            .Where(d => d != newDict && (
                d.Source?.OriginalString?.Contains("Light.xaml") == true ||
                d.Source?.OriginalString?.Contains("Dark.xaml")  == true))
            .ToList();

        foreach (var old in toRemove)
            dicts.Remove(old);
    }

    public void Toggle() =>
        ApplyTheme(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    // ── Detect Windows system theme ──────────────────────────────────────────

    public AppTheme DetectSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int i)
                return i == 1 ? AppTheme.Light : AppTheme.Dark;
        }
        catch { }
        return AppTheme.Dark; // safe default
    }

    // ── Persist ──────────────────────────────────────────────────────────────

    public void Save(string themeChoice) // "Dark", "Light", or "Auto"
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(new Settings(themeChoice), JsonOpts));
    }

    public string Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return "Auto";
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<Settings>(json, JsonOpts)?.Theme ?? "Auto";
        }
        catch { return "Auto"; }
    }
}
