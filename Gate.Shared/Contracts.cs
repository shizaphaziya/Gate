using System.Text.Json;

namespace Gate.Shared;

public record NetworkPacket(PacketType Type, string? SessionToken, string PayloadJson)
{
    public static NetworkPacket Create<T>(PacketType type, T payload, string? sessionToken = null)
    {
        string json = JsonSerializer.Serialize(payload);
        return new NetworkPacket(type, sessionToken, json);
    }

    public T? DeserializePayload<T>()
    {
        if (string.IsNullOrWhiteSpace(PayloadJson))
            return default;
        return JsonSerializer.Deserialize<T>(PayloadJson);
    }
}

public record AuthRequestDto(string Username, string Password);

public record AuthResponseDto(bool Success, string? Token, string? Role, string? ErrorMessage);

public record ServiceInfoDto(string Name, string Description, ServiceState State, bool CanManage, bool CanViewLogs);

public record ServicesResponseDto(List<ServiceInfoDto> Services);

public record ServiceActionRequestDto(string ServiceName, ServiceAction Action);

public record ServiceActionResponseDto(bool Success, string Message, ServiceState NewState);

public record LogsRequestDto(string ServiceName, int Lines = 100);

public record LogsResponseDto(string ServiceName, string LogContent);

public record AuditLogDto(int Id, string Username, string Action, string ServiceName, string Status, DateTime ExecutedAt);

public record AuditResponseDto(List<AuditLogDto> Logs);
