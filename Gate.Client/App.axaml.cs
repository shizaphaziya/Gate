using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Gate.Client.ViewModels;
using Gate.Client.Views;

namespace Gate.Client;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainWindowViewModel();

            if (desktop.Args != null)
            {
                if (desktop.Args.Contains("--test-deploy"))
                {
                    vm.ConnectionMode = 1;
                    vm.SelectedTabIndex = 1;
                }
                else if (desktop.Args.Contains("--test-check-repair"))
                {
                    vm.ConnectionMode = 1;
                    _ = vm.CheckAndRepairServerCommand.ExecuteAsync(null);
                }
                else if (desktop.Args.Contains("--test-login"))
                {
                    _ = vm.QuickLoginAsAdminCommand.ExecuteAsync(null);
                }
                else if (desktop.Args.Contains("--test-pink"))
                {
                    vm.SelectedTheme = vm.ThemeList.FirstOrDefault(t => t.Theme == Services.AppTheme.Pink) ?? vm.SelectedTheme;
                }
                else if (desktop.Args.Contains("--test-light"))
                {
                    vm.SelectedTheme = vm.ThemeList.FirstOrDefault(t => t.Theme == Services.AppTheme.Light) ?? vm.SelectedTheme;
                }
            }

            var mainWindow = new MainWindow
            {
                DataContext = vm
            };
            desktop.MainWindow = mainWindow;

            if (desktop.Args != null && (desktop.Args.Contains("--demo") || desktop.Args.Contains("--two-windows")))
            {
                var serverWin = new TestServerWindow
                {
                    DataContext = new TestServerViewModel()
                };
                serverWin.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
