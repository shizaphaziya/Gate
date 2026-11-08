using CommunityToolkit.Mvvm.ComponentModel;
using Gate.Client.Services;
using Gate.Shared;

namespace Gate.Client.ViewModels;

public class AuditItemViewModel : ViewModelBase
{
    public int Id { get; }
    public string Username { get; }
    public string Action { get; }
    public string ServiceName { get; }
    public string Status { get; }
    public DateTime ExecutedAt { get; }

    public string TimeDisplay => ExecutedAt.ToString("HH:mm:ss (UTC)");

    public string ActionDisplay => Action switch
    {
        "Start" => "Запуск",
        "Stop" => "Остановка",
        "Restart" => "Перезапуск",
        "Status" => "Статус",
        "ViewLogs" => "Логи",
        _ => Action
    };

    public string StatusDisplay => Status switch
    {
        "SUCCESS" => "Успешно",
        "FORBIDDEN" => "Запрещено",
        "ERROR" => "Ошибка",
        _ => Status
    };

    public string StatusColor => Status switch
    {
        "SUCCESS" => StatusPalette.Ok,
        "FORBIDDEN" => StatusPalette.Error,
        _ => StatusPalette.Warn
    };

    public string StatusBgColor => (ThemeService.CurrentTheme == AppTheme.Dark)
        ? Status switch
        {
            "SUCCESS" => "#163828",
            "FORBIDDEN" => "#3B1818",
            _ => "#382914"
        }
        : Status switch
        {
            "SUCCESS" => "#D1FAE5",
            "FORBIDDEN" => "#FEE2E2",
            _ => "#FEF3C7"
        };

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusBgColor));
    }

    public AuditItemViewModel(AuditLogDto dto)
    {
        Id = dto.Id;
        Username = dto.Username;
        Action = dto.Action;
        ServiceName = dto.ServiceName;
        Status = dto.Status;
        ExecutedAt = dto.ExecutedAt;
    }
}
