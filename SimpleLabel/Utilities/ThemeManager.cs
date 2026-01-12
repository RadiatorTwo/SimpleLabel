using System.Windows;
using Microsoft.Win32;

namespace SimpleLabel.Utilities;

public enum AppTheme { System, Light, Dark }

public static class ThemeManager
{
    private static AppTheme _currentMode = AppTheme.System;

    public static AppTheme CurrentMode => _currentMode;

    public static void SetTheme(AppTheme mode)
    {
        _currentMode = mode;
        ApplyTheme();
    }

    public static void ApplyTheme()
    {
        var isDark = _currentMode switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            AppTheme.System => IsSystemDarkMode(),
            _ => false
        };

        var themePath = isDark
            ? "Resources/DarkTheme.xaml"  // Will be created in Phase 17
            : "Resources/LightTheme.xaml";

        try
        {
            var dict = new ResourceDictionary { Source = new Uri(themePath, UriKind.Relative) };
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }
        catch
        {
            // Fallback to Light theme if Dark theme doesn't exist yet
            if (isDark)
            {
                var fallback = new ResourceDictionary { Source = new Uri("Resources/LightTheme.xaml", UriKind.Relative) };
                Application.Current.Resources.MergedDictionaries.Clear();
                Application.Current.Resources.MergedDictionaries.Add(fallback);
            }
        }
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void StartSystemThemeWatcher()
    {
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && _currentMode == AppTheme.System)
            {
                Application.Current.Dispatcher.Invoke(ApplyTheme);
            }
        };
    }
}
