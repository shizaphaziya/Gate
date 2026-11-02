using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gate.Client.Services;

namespace Gate.Client.ViewModels;

public partial class TestServerViewModel : ViewModelBase
{
    private readonly TestServerManager _serverManager = new();
    private Timer? _pollTimer;

    [ObservableProperty]
    private string _statusText = "Опрос...";

    [ObservableProperty]
    private string _statusColor = "#6B7280";

    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private bool _isBusy = false;

    [ObservableProperty]
    private string _statusDetails = "Инициализация...";

    [ObservableProperty]
    private string _consoleLog = "Готов к управлению тестовым Linux-сервером.\n";

    [ObservableProperty]
    private bool _isPinkTheme = false;

    public TestServerViewModel()
    {
        _isPinkTheme = (ThemeService.CurrentTheme == AppTheme.Pink);

        _pollTimer = new Timer(async _ =>
        {
            if (!IsBusy)
            {
                await CheckStatusAsync();
            }
        }, null, 300, 2500);
    }

    public void RefreshTheme()
    {
        IsPinkTheme = (ThemeService.CurrentTheme == AppTheme.Pink);
    }

    public async Task CheckStatusAsync()
    {
        var info = await _serverManager.GetStatusAsync();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            StatusText = info.StatusText;
            StatusColor = info.StatusColor;
            IsRunning = info.IsRunning;
            StatusDetails = info.Details;
        });
    }

    private void AppendLog(string message)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ConsoleLog += message + "\n";
        });
    }

    [RelayCommand]
    public async Task StartServerAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AppendLog($"--- [{DateTime.Now:HH:mm:ss}] Запуск тестового Linux-сервера ---");
            await _serverManager.StartAsync(AppendLog);
            await CheckStatusAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ОШИБКА]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task StopServerAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AppendLog($"--- [{DateTime.Now:HH:mm:ss}] Остановка тестового Linux-сервера ---");
            await _serverManager.StopAsync(AppendLog);
            await CheckStatusAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ОШИБКА]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RestartServerAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AppendLog($"--- [{DateTime.Now:HH:mm:ss}] Перезапуск тестового Linux-сервера ---");
            await _serverManager.RestartAsync(AppendLog);
            await CheckStatusAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ОШИБКА]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ResetServerAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AppendLog($"--- [{DateTime.Now:HH:mm:ss}] Сброс тестового сервера ---");
            await _serverManager.ResetAsync(AppendLog);
            await CheckStatusAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ОШИБКА]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task RebuildServerAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            AppendLog($"--- [{DateTime.Now:HH:mm:ss}] Полная пересборка тестового Linux-сервера ---");
            await _serverManager.RebuildAsync(AppendLog);
            await CheckStatusAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ОШИБКА]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task FetchJournalLogsAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _serverManager.FetchJournalLogsAsync(AppendLog, 25);
        }
        catch (Exception ex)
        {
            AppendLog($"[ОШИБКА]: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public void ClearLog()
    {
        ConsoleLog = string.Empty;
    }
}
