using Gate.Shared;

namespace Gate.Client.ViewModels;

public class AuditItemViewModel
{
    public int Id { get; }
    public string Username { get; }
    public string Action { get; }
    public string ServiceName { get; }
    public string Status { get; }
    public DateTime ExecutedAt { get; }

    public string TimeDisplay => ExecutedAt.ToString("HH:mm:ss (UTC)");

    public string StatusColor => Status switch
    {
        "SUCCESS" => "#10B981",
        "FORBIDDEN" => "#EF4444",
        _ => "#F59E0B"
    };

    public string StatusBgColor => Status switch
    {
        "SUCCESS" => "#163828",
        "FORBIDDEN" => "#3B1818",
        _ => "#382914"
    };

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
