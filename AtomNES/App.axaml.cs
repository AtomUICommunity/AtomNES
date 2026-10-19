using System.Linq;
using AtomUI.Theme.Algorithms;
using AtomUI.Theme.Configuration;
using AtomNES.ViewModels;
using AtomNES.Views;
using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Localization;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AtomNES;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        this.UseAtomUI(builder =>
        {
            builder.WithFollowSystemThemes(
                new ThemeRequest(IThemeManager.DEFAULT_THEME_ID, null, ThemeTransitionReason.Startup),
                new ThemeRequest(
                    IThemeManager.DEFAULT_THEME_ID,
                    new ThemeConfigBuilder().WithAlgorithms(ThemeAlgorithm.Default, ThemeAlgorithm.Dark).Build(),
                    ThemeTransitionReason.Startup));
            builder.UseAlibabaSansFont();
            builder.UseDesktopControls();
            builder.UseDesktopColorPicker();
            builder.UseDesktopDataGrid();
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            if (desktop.Args is [var romPath, ..] && System.IO.File.Exists(romPath))
            {
                desktop.MainWindow.Opened += (_, _) =>
                {
                    vm.LoadRom(romPath);
                    if (desktop.Args.Contains("--debug")) vm.ShowDebuggerCommand.Execute(null);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

