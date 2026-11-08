using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gate.Client.Services;
using Gate.Shared;

namespace Gate.Client.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GateNetworkClient _networkClient = new();
    private readonly SshDeployerService _sshDeployer = new();
    private System.Threading.Timer? _autoRefreshTimer;

    public Func<string, Task>? CopyToClipboardHandler { get; set; }
    public Func<Task<string?>>? BrowseFileHandler { get; set; }

    public ObservableCollection<ThemeItemViewModel> ThemeList { get; }

    [ObservableProperty]
    private ThemeItemViewModel _selectedTheme;

    [ObservableProperty]
    private bool _isPinkTheme = false;

    private Views.TestServerWindow? _testServerWindowInstance;

    [RelayCommand]
    public void OpenTestServerWindow()
    {
        if (_testServerWindowInstance == null || !_testServerWindowInstance.IsVisible)
        {
            _testServerWindowInstance = new Views.TestServerWindow
            {
                DataContext = new TestServerViewModel()
            };
            _testServerWindowInstance.Closed += (_, _) => _testServerWindowInstance = null;
            _testServerWindowInstance.Show();
        }
        else
        {
            _testServerWindowInstance.Activate();
        }
    }

    partial void OnSelectedThemeChanged(ThemeItemViewModel value)
    {
        if (value == null) return;
        ThemeService.ApplyTheme(value.Theme);
        IsPinkTheme = (value.Theme == AppTheme.Pink);

        if (_testServerWindowInstance?.DataContext is TestServerViewModel testVm)
        {
            testVm.RefreshTheme();
        }

        foreach (var svc in Services)
        {
            svc.RefreshTheme();
        }

        OnPropertyChanged(nameof(LoginTabButtonBg));
        OnPropertyChanged(nameof(LoginTabButtonFg));
        OnPropertyChanged(nameof(DeployTabButtonBg));
        OnPropertyChanged(nameof(DeployTabButtonFg));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoginMode))]
    [NotifyPropertyChangedFor(nameof(IsDeployMode))]
    [NotifyPropertyChangedFor(nameof(LoginTabButtonBg))]
    [NotifyPropertyChangedFor(nameof(LoginTabButtonFg))]
    [NotifyPropertyChangedFor(nameof(DeployTabButtonBg))]
    [NotifyPropertyChangedFor(nameof(DeployTabButtonFg))]
    private int _connectionMode = 0;

    public bool IsLoginMode => ConnectionMode == 0;
    public bool IsDeployMode => ConnectionMode == 1;

    public string LoginTabButtonBg => IsLoginMode
        ? (ThemeService.CurrentTheme == AppTheme.Pink ? "#C2185B" : "#2563EB")
        : "Transparent";

    public string LoginTabButtonFg => IsLoginMode
        ? "#FFFFFF"
        : (ThemeService.CurrentTheme == AppTheme.Pink ? "#88304E" : StatusPalette.Muted);

    public string DeployTabButtonBg => IsDeployMode
        ? (ThemeService.CurrentTheme == AppTheme.Pink ? "#C2185B" : "#2563EB")
        : "Transparent";

    public string DeployTabButtonFg => IsDeployMode
        ? "#FFFFFF"
        : (ThemeService.CurrentTheme == AppTheme.Pink ? "#88304E" : StatusPalette.Muted);

    [RelayCommand]
    public void SetConnectionMode(object? parameter)
    {
        if (parameter is string str && int.TryParse(str, out int val))
        {
            ConnectionMode = val;
        }
        else if (parameter is int intVal)
        {
            ConnectionMode = intVal;
        }
    }

    [RelayCommand]
    public void SwitchToLoginMode() => ConnectionMode = 0;

    [RelayCommand]
    public void SwitchToDeployMode() => ConnectionMode = 1;

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    [ObservableProperty]
    private string _serverHost = "127.0.0.1";

    [ObservableProperty]
    private int _serverPort = 5050;

    [ObservableProperty]
    private string _username = "admin";

    [ObservableProperty]
    private string _password = "admin123";

    [ObservableProperty]
    private bool _isLoggedIn = false;

    [ObservableProperty]
    private bool _isLoggingIn = false;

    [ObservableProperty]
    private string _loginErrorMessage = string.Empty;

    [ObservableProperty]
    private string _sshHost = "127.0.0.1";

    [ObservableProperty]
    private int _sshPort = 2222;

    [ObservableProperty]
    private string _sshUser = "root";

    [ObservableProperty]
    private string _sshPassword = "root";

    [ObservableProperty]
    private string _localServerBinaryPath = string.Empty;

    [ObservableProperty]
    private string _serverBinaryStatusText = "Проверка дистрибутива...";

    [ObservableProperty]
    private string _serverBinaryStatusColor = StatusPalette.Ok;

    [ObservableProperty]
    private Material.Icons.MaterialIconKind _serverBinaryStatusIcon = Material.Icons.MaterialIconKind.CheckCircle;

    [ObservableProperty]
    private bool _isCustomBinarySpecified = false;

    public void RefreshServerBinaryStatus()
    {
        var path = ServerPackageResolver.ResolveBinaryPath(LocalServerBinaryPath);
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (string.IsNullOrWhiteSpace(LocalServerBinaryPath))
            {
                ServerBinaryStatusText = $"Встроенный пакет готов: {info.Name} ({info.Length / (1024 * 1024)} МБ)";
                ServerBinaryStatusColor = StatusPalette.Ok;
                ServerBinaryStatusIcon = Material.Icons.MaterialIconKind.CheckCircle;
                IsCustomBinarySpecified = false;
            }
            else
            {
                ServerBinaryStatusText = $"Пользовательский файл: {info.Name} ({info.Length / (1024 * 1024)} МБ)";
                ServerBinaryStatusColor = StatusPalette.Info;
                ServerBinaryStatusIcon = Material.Icons.MaterialIconKind.FolderCheck;
                IsCustomBinarySpecified = true;
            }
        }
        else
        {
            ServerBinaryStatusText = "Пакет не найден (будет собран автоматически перед деплоем)";
            ServerBinaryStatusColor = StatusPalette.Warn;
            ServerBinaryStatusIcon = Material.Icons.MaterialIconKind.Autorenew;
            IsCustomBinarySpecified = false;
        }
    }

    [RelayCommand]
    public async Task RebuildServerBinaryAsync()
    {
        ServerBinaryStatusText = "Сборка пакета через dotnet publish...";
        ServerBinaryStatusColor = StatusPalette.Info;
        ServerBinaryStatusIcon = Material.Icons.MaterialIconKind.Autorenew;

        var (success, path, log) = await ServerPackageResolver.AutoBuildServerBinaryAsync();
        if (success)
        {
            LocalServerBinaryPath = string.Empty;
            RefreshServerBinaryStatus();
            DeployProgressLog += $"[{DateTime.Now:HH:mm:ss}] Успешно собран пакет: {path}\n";
        }
        else
        {
            ServerBinaryStatusText = "Ошибка сборки пакета";
            ServerBinaryStatusColor = StatusPalette.Error;
            ServerBinaryStatusIcon = Material.Icons.MaterialIconKind.CloseCircle;
            DeployProgressLog += $"[{DateTime.Now:HH:mm:ss}] Ошибка сборки: {log}\n";
        }
    }

    [RelayCommand]
    public void ResetToDefaultBinary()
    {
        LocalServerBinaryPath = string.Empty;
        RefreshServerBinaryStatus();
    }

    [ObservableProperty]
    private bool _isDeploying = false;

    [ObservableProperty]
    private bool _isCheckingServer = false;

    [ObservableProperty]
    private string _checkRepairStatusMessage = string.Empty;

    [ObservableProperty]
    private string _checkRepairStatusColor = StatusPalette.Ok;

    [ObservableProperty]
    private bool _canSwitchToLoginAfterCheck = false;

    [ObservableProperty]
    private string _deployProgressLog = "Ready for SSH deployment.\n";

    [ObservableProperty]
    private ObservableCollection<ServiceItemViewModel> _services = new();

    [ObservableProperty]
    private ServiceItemViewModel? _selectedService;

    [ObservableProperty]
    private string _selectedServiceHeader = "Журнал логов journalctl (служба не выбрана)";

    [ObservableProperty]
    private string _terminalLogs = "Выберите службу и нажмите «Логи» для просмотра journalctl...\n";

    [ObservableProperty]
    private ObservableCollection<AuditItemViewModel> _auditLogs = new();

    [ObservableProperty]
    private string _currentUserDisplay = "Не авторизован";

    [ObservableProperty]
    private string _statusNotification = "Готов к работе.";

    [ObservableProperty]
    private string _statusNotificationColor = StatusPalette.Muted;

    [ObservableProperty]
    private bool _isAutoRefreshEnabled = false;

    public MainWindowViewModel()
    {
        var dark = new ThemeItemViewModel { Name = "Тёмная", Icon = Material.Icons.MaterialIconKind.WeatherNight, Theme = AppTheme.Dark };
        var light = new ThemeItemViewModel { Name = "Светлая", Icon = Material.Icons.MaterialIconKind.WeatherSunny, Theme = AppTheme.Light };
        var pink = new ThemeItemViewModel { Name = "Розовая", Icon = Material.Icons.MaterialIconKind.Paw, Theme = AppTheme.Pink };

        ThemeList = new ObservableCollection<ThemeItemViewModel> { dark, light, pink };
        _selectedTheme = dark;

        RefreshServerBinaryStatus();

        // Auto refresh periodic timer (runs every 3 seconds if enabled)
        _autoRefreshTimer = new System.Threading.Timer(async _ =>
        {
            if (IsLoggedIn && IsAutoRefreshEnabled && !IsDeploying)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await RefreshServicesInternalAsync(silent: true);
                    if (SelectedService != null)
                    {
                        await LoadLogsInternalAsync(SelectedService.Name, silent: true);
                    }
                    await RefreshAuditInternalAsync(silent: true);
                });
            }
        }, null, 3000, 3000);
    }

    partial void OnSelectedServiceChanged(ServiceItemViewModel? value)
    {
        foreach (var s in Services)
        {
            s.IsSelected = (s == value);
        }

        if (value != null)
        {
            SelectedServiceHeader = $"Журнал логов journalctl ({value.Name})";
            _ = LoadLogsAsync(value.Name);
        }
        else
        {
            SelectedServiceHeader = "Журнал логов journalctl (служба не выбрана)";
        }
    }

    [RelayCommand]
    private void FillAdmin()
    {
        Username = "admin";
        Password = "admin123";
    }

    [RelayCommand]
    private void FillDev()
    {
        Username = "dev";
        Password = "dev123";
    }

    [RelayCommand]
    private void FillAuditor()
    {
        Username = "auditor";
        Password = "audit123";
    }

    [RelayCommand]
    private async Task QuickLoginAsAdminAsync()
    {
        FillAdmin();
        await LoginAsync();
    }

    [RelayCommand]
    private async Task QuickLoginAsDevAsync()
    {
        FillDev();
        await LoginAsync();
    }

    [RelayCommand]
    private async Task QuickLoginAsAuditorAsync()
    {
        FillAuditor();
        await LoginAsync();
    }

    [RelayCommand]
    private void FillLocalContainerPreset()
    {
        SshHost = "127.0.0.1";
        SshPort = 2222;
        SshUser = "root";
        SshPassword = "root";
        SetStatus("Параметры локального тестового Linux (127.0.0.1:2222, root/root) установлены", StatusPalette.Info);
    }

    [RelayCommand]
    private async Task BrowseBinaryPathAsync()
    {
        if (BrowseFileHandler != null)
        {
            string? selected = await BrowseFileHandler.Invoke();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                LocalServerBinaryPath = selected;
                RefreshServerBinaryStatus();
                SetStatus($"Выбран бинарник: {selected}", StatusPalette.Ok);
            }
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsLoggingIn = true;
        LoginErrorMessage = string.Empty;

        try
        {
            var response = await _networkClient.LoginAsync(ServerHost, ServerPort, Username, Password);
            if (response.Success)
            {
                IsLoggedIn = true;
                CurrentUserDisplay = $"{Username} ({response.Role})";
                SetStatus($"Успешный вход в систему с ролью {response.Role}", StatusPalette.Ok);

                await RefreshServicesAsync();
                await RefreshAuditAsync();

                // Automatically select dummy-worker.service if available, else first service
                if (Services.Count > 0)
                {
                    SelectedService = Services.FirstOrDefault(s => s.Name == "dummy-worker.service") ?? Services[0];
                }

                SelectedTabIndex = 2;
            }
            else
            {
                LoginErrorMessage = response.ErrorMessage ?? "Ошибка аутентификации.";
                SetStatus(LoginErrorMessage, StatusPalette.Error);
            }
        }
        catch (Exception ex)
        {
            LoginErrorMessage = $"Ошибка подключения: {ex.Message}";
            SetStatus(LoginErrorMessage, StatusPalette.Error);
        }
        finally
        {
            IsLoggingIn = false;
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _networkClient.Disconnect();
        IsLoggedIn = false;
        CurrentUserDisplay = "Не авторизован";
        Services.Clear();
        AuditLogs.Clear();
        SelectedService = null;
        TerminalLogs = "Сессия завершена.\n";
        SetStatus("Соединение разорвано.", StatusPalette.Muted);
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (IsDeploying) return;

        IsDeploying = true;
        DeployProgressLog = $"[{DateTime.Now:HH:mm:ss}] Запуск автоматической SSH-установки...\n";

        try
        {
            await _sshDeployer.DeployAsync(
                SshHost,
                SshPort,
                SshUser,
                SshPassword,
                LocalServerBinaryPath,
                logLine =>
                {
                    DeployProgressLog += logLine + "\n";
                }
            );

            DeployProgressLog += $"[{DateTime.Now:HH:mm:ss}] Установка успешно завершена!\n";
            ServerHost = SshHost;
            ServerPort = 5050;
            CanSwitchToLoginAfterCheck = true;
            CheckRepairStatusMessage = "Gate успешно установлен и запущен на порту 5050!";
            CheckRepairStatusColor = StatusPalette.Ok;
            SetStatus("Развертывание сервера по SSH успешно завершено", StatusPalette.Ok);
        }
        catch (Exception ex)
        {
            DeployProgressLog += $"[{DateTime.Now:HH:mm:ss}] ОШИБКА РАЗВЕРТЫВАНИЯ: {ex.Message}\n";
            SetStatus($"Ошибка SSH-установки: {ex.Message}", StatusPalette.Error);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    [RelayCommand]
    public async Task CheckAndRepairServerAsync()
    {
        if (IsCheckingServer || IsDeploying) return;

        IsCheckingServer = true;
        CheckRepairStatusMessage = "Подключение и проверка состояния Gate...";
        CheckRepairStatusColor = StatusPalette.Info;
        CanSwitchToLoginAfterCheck = false;
        DeployProgressLog += $"--- [{DateTime.Now:HH:mm:ss}] Проверка и диагностика Gate на {SshHost}:{SshPort} ---\n";

        try
        {
            var res = await _sshDeployer.CheckAndRepairAsync(
                SshHost,
                SshPort,
                SshUser,
                SshPassword,
                logLine =>
                {
                    DeployProgressLog += logLine + "\n";
                }
            );

            CheckRepairStatusMessage = res.Message;
            if (res.IsRunning)
            {
                CheckRepairStatusColor = StatusPalette.Ok;
                CanSwitchToLoginAfterCheck = true;
                ServerHost = SshHost;
                ServerPort = 5050;
                SetStatus($"Gate готов к работе на порту 5050", StatusPalette.Ok);
            }
            else if (!res.IsInstalled)
            {
                CheckRepairStatusColor = StatusPalette.Warn;
                SetStatus("Gate не установлен на сервере", StatusPalette.Warn);
            }
            else
            {
                CheckRepairStatusColor = StatusPalette.Error;
                SetStatus("Ошибка запуска Gate после починки", StatusPalette.Error);
            }
        }
        catch (Exception ex)
        {
            CheckRepairStatusMessage = $"Ошибка подключения: {ex.Message}";
            CheckRepairStatusColor = StatusPalette.Error;
            DeployProgressLog += $"[{DateTime.Now:HH:mm:ss}] ОШИБКА ДИАГНОСТИКИ: {ex.Message}\n";
            SetStatus($"Ошибка SSH: {ex.Message}", StatusPalette.Error);
        }
        finally
        {
            IsCheckingServer = false;
        }
    }

    [RelayCommand]
    public async Task ResetRemoteGateAsync()
    {
        if (IsCheckingServer || IsDeploying) return;

        IsDeploying = true;
        DeployProgressLog += $"--- [{DateTime.Now:HH:mm:ss}] Сброс Gate на удаленном сервере {SshHost}:{SshPort} ---\n";

        try
        {
            await _sshDeployer.ResetRemoteServerAsync(
                SshHost,
                SshPort,
                SshUser,
                SshPassword,
                logLine =>
                {
                    DeployProgressLog += logLine + "\n";
                }
            );

            CheckRepairStatusMessage = "Сервер сброшен до исходного состояния без Gate.";
            CheckRepairStatusColor = StatusPalette.Muted;
            CanSwitchToLoginAfterCheck = false;
            SetStatus("Удаленный сервер успешно сброшен", StatusPalette.Ok);
        }
        catch (Exception ex)
        {
            DeployProgressLog += $"[{DateTime.Now:HH:mm:ss}] ОШИБКА СБРОСА: {ex.Message}\n";
            SetStatus($"Ошибка сброса: {ex.Message}", StatusPalette.Error);
        }
        finally
        {
            IsDeploying = false;
        }
    }

    [RelayCommand]
    private async Task RefreshServicesAsync() => await RefreshServicesInternalAsync(false);

    private async Task RefreshServicesInternalAsync(bool silent)
    {
        if (!IsLoggedIn) return;

        try
        {
            var serviceDtos = await _networkClient.GetServicesAsync();
            string? currentSelectedName = SelectedService?.Name;

            Services.Clear();
            foreach (var dto in serviceDtos)
            {
                var vm = new ServiceItemViewModel(dto)
                {
                    IsSelected = (dto.Name == currentSelectedName)
                };
                Services.Add(vm);
            }

            if (!string.IsNullOrEmpty(currentSelectedName))
            {
                SelectedService = Services.FirstOrDefault(s => s.Name == currentSelectedName);
            }
            else if (Services.Count > 0 && SelectedService == null)
            {
                SelectedService = Services.FirstOrDefault(s => s.Name == "dummy-worker.service") ?? Services[0];
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                SetStatus($"Ошибка обновления служб: {ex.Message}", StatusPalette.Error);
            }
        }
    }

    [RelayCommand]
    private async Task RefreshAuditAsync() => await RefreshAuditInternalAsync(false);

    private async Task RefreshAuditInternalAsync(bool silent)
    {
        if (!IsLoggedIn) return;

        try
        {
            var logs = await _networkClient.GetAuditLogsAsync();
            AuditLogs.Clear();
            foreach (var item in logs)
            {
                AuditLogs.Add(new AuditItemViewModel(item));
            }
        }
        catch (Exception ex)
        {
            if (!silent)
            {
                SetStatus($"Ошибка загрузки аудита: {ex.Message}", StatusPalette.Error);
            }
        }
    }

    [RelayCommand]
    private async Task ExecuteServiceActionAsync(object? parameter)
    {
        if (!IsLoggedIn) return;

        string? serviceName = null;
        ServiceAction action = ServiceAction.Status;

        if (parameter is ServiceItemViewModel item)
        {
            serviceName = item.Name;
            action = ServiceAction.Restart;
            SelectedService = item;
        }
        else if (parameter is string paramStr)
        {
            var parts = paramStr.Split(':');
            if (parts.Length == 2)
            {
                serviceName = parts[0];
                Enum.TryParse(parts[1], true, out action);
            }
            else if (SelectedService != null && Enum.TryParse(paramStr, true, out action))
            {
                serviceName = SelectedService.Name;
            }
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            SetStatus("Служба не выбрана для выполнения действия", StatusPalette.Error);
            return;
        }

        try
        {
            var result = await _networkClient.ExecuteActionAsync(serviceName, action);
            if (result.Success)
            {
                SetStatus($"[200 OK] {result.Message}", StatusPalette.Ok);
            }
            else
            {
                SetStatus($"[{result.Message}]", StatusPalette.Error);
            }

            await RefreshServicesInternalAsync(silent: true);
            await LoadLogsInternalAsync(serviceName, silent: true);
            await RefreshAuditInternalAsync(silent: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Исключение при выполнении действия: {ex.Message}", StatusPalette.Error);
        }
    }

    [RelayCommand]
    private async Task LoadLogsAsync(object? parameter)
    {
        string? name = parameter is ServiceItemViewModel item ? item.Name : parameter as string;
        await LoadLogsInternalAsync(name, false);
    }

    private async Task LoadLogsInternalAsync(string? serviceName, bool silent)
    {
        if (!IsLoggedIn) return;

        if (string.IsNullOrWhiteSpace(serviceName) && SelectedService != null)
        {
            serviceName = SelectedService.Name;
        }

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            if (!silent) SetStatus("Выберите службу для загрузки логов", StatusPalette.Error);
            return;
        }

        var matched = Services.FirstOrDefault(s => s.Name == serviceName);
        if (matched != null && SelectedService != matched)
        {
            SelectedService = matched;
        }

        try
        {
            if (!silent)
            {
                TerminalLogs = $"--- Запрос логов {serviceName} через journalctl... ---\n";
            }

            string logs = await _networkClient.GetLogsAsync(serviceName, 100);
            TerminalLogs = logs;

            if (logs.StartsWith("403 FORBIDDEN"))
            {
                SetStatus(logs, StatusPalette.Error);
            }
            else if (!silent)
            {
                SetStatus($"Логи службы {serviceName} успешно обновлены", StatusPalette.Ok);
            }

            await RefreshAuditInternalAsync(silent: true);
        }
        catch (Exception ex)
        {
            TerminalLogs = $"Ошибка при загрузке логов: {ex.Message}";
            if (!silent) SetStatus($"Ошибка чтения логов: {ex.Message}", StatusPalette.Error);
        }
    }

    [RelayCommand]
    private async Task CopyLogsToClipboardAsync()
    {
        if (string.IsNullOrWhiteSpace(TerminalLogs))
        {
            SetStatus("Логи пусты для копирования", StatusPalette.Muted);
            return;
        }

        if (CopyToClipboardHandler != null)
        {
            await CopyToClipboardHandler.Invoke(TerminalLogs);
            SetStatus("Логи успешно скопированы в буфер обмена", StatusPalette.Ok);
        }
    }

    [RelayCommand]
    private void ClearTerminal()
    {
        TerminalLogs = string.Empty;
        SetStatus("Окно логов очищено", StatusPalette.Muted);
    }

    private void SetStatus(string message, string hexColor)
    {
        StatusNotification = $"[{DateTime.Now:HH:mm:ss}] {message}";
        StatusNotificationColor = hexColor;
    }
}
