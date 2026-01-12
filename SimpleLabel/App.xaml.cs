using System.Windows;
using SimpleLabel.Utilities;

namespace SimpleLabel;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ThemeManager.StartSystemThemeWatcher();
        ThemeManager.ApplyTheme();
    }
}