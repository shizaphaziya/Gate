namespace Gate.Shared;

public enum PacketType
{
    AuthRequest,
    AuthResponse,
    GetServicesRequest,
    GetServicesResponse,
    ServiceActionRequest,
    ServiceActionResponse,
    GetLogsRequest,
    GetLogsResponse,
    GetAuditRequest,
    GetAuditResponse
}

public enum ServiceAction
{
    Start,
    Stop,
    Restart,
    Status
}

public enum ServiceState
{
    Active,
    Inactive,
    Failed,
    Unknown
}
