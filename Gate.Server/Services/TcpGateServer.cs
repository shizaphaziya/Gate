using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Gate.Server.Database;
using Gate.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Gate.Server.Services;

public record UserSession(int UserId, string Username, string RoleName, List<Permission> Permissions);

public class TcpGateServer : BackgroundService
{
    private readonly ILogger<TcpGateServer> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SystemdManager _systemd;
    private readonly PermissionChecker _permissionChecker;
    private readonly ConcurrentDictionary<string, UserSession> _sessions = new();
    private readonly int _port = 5050;

    public TcpGateServer(
        ILogger<TcpGateServer> logger,
        IServiceScopeFactory scopeFactory,
        SystemdManager systemd,
        PermissionChecker permissionChecker)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _systemd = systemd;
        _permissionChecker = permissionChecker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        try
        {
            listener.Start();
            _logger.LogInformation("Gate TCP Server started listening on port {Port}", _port);

            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Gate TCP Server stopping...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gate TCP Server encountered a fatal error");
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
            _logger.LogInformation("Client connected: {Endpoint}", clientEndpoint);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    NetworkPacket? packet = await PacketProtocol.ReadPacketAsync(stream, ct);
                    if (packet == null)
                    {
                        _logger.LogInformation("Client disconnected: {Endpoint}", clientEndpoint);
                        break;
                    }

                    NetworkPacket response = await ProcessPacketAsync(packet, ct);
                    await PacketProtocol.SendPacketAsync(stream, response, ct);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
                _logger.LogInformation("Connection closed for client {Endpoint}", clientEndpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling client {Endpoint}", clientEndpoint);
            }
        }
    }

    private async Task<NetworkPacket> ProcessPacketAsync(NetworkPacket packet, CancellationToken ct)
    {
        if (packet.Type == PacketType.AuthRequest)
        {
            return await HandleAuthAsync(packet);
        }

        if (string.IsNullOrWhiteSpace(packet.SessionToken) || !_sessions.TryGetValue(packet.SessionToken, out UserSession? session))
        {
            return NetworkPacket.Create(
                PacketType.AuthResponse,
                new AuthResponseDto(false, null, null, "401 UNAUTHORIZED: Invalid or expired session token")
            );
        }

        return packet.Type switch
        {
            PacketType.GetServicesRequest => await HandleGetServicesAsync(session, ct),
            PacketType.ServiceActionRequest => await HandleServiceActionAsync(session, packet, ct),
            PacketType.GetLogsRequest => await HandleGetLogsAsync(session, packet, ct),
            PacketType.GetAuditRequest => await HandleGetAuditAsync(session, ct),
            _ => NetworkPacket.Create(PacketType.ServiceActionResponse, new ServiceActionResponseDto(false, "Unknown packet type", ServiceState.Unknown))
        };
    }

    private async Task<NetworkPacket> HandleAuthAsync(NetworkPacket packet)
    {
        var dto = packet.DeserializePayload<AuthRequestDto>();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
        {
            return NetworkPacket.Create(PacketType.AuthResponse, new AuthResponseDto(false, null, null, "Username and password are required"));
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GateDbContext>();

        var user = await db.Users
            .Include(u => u.Role)
            .ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.Username == dto.Username);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for user '{Username}'", dto.Username);
            return NetworkPacket.Create(PacketType.AuthResponse, new AuthResponseDto(false, null, null, "Invalid username or password"));
        }

        string token = Guid.NewGuid().ToString("N");
        var userSession = new UserSession(user.Id, user.Username, user.Role.Name, user.Role.Permissions.ToList());
        _sessions[token] = userSession;

        _logger.LogInformation("User '{Username}' ({Role}) authenticated successfully", user.Username, user.Role.Name);
        return NetworkPacket.Create(PacketType.AuthResponse, new AuthResponseDto(true, token, user.Role.Name, null), token);
    }

    private async Task<NetworkPacket> HandleGetServicesAsync(UserSession session, CancellationToken ct)
    {
        var services = new List<ServiceInfoDto>();

        foreach (var (name, description) in SystemdManager.DefaultServices)
        {
            ServiceState state = await _systemd.GetServiceStateAsync(name, ct);
            bool canManage = _permissionChecker.CanManage(session.Permissions, name);
            bool canViewLogs = _permissionChecker.CanViewLogs(session.Permissions, name);

            services.Add(new ServiceInfoDto(name, description, state, canManage, canViewLogs));
        }

        return NetworkPacket.Create(PacketType.GetServicesResponse, new ServicesResponseDto(services));
    }

    private async Task<NetworkPacket> HandleServiceActionAsync(UserSession session, NetworkPacket packet, CancellationToken ct)
    {
        var dto = packet.DeserializePayload<ServiceActionRequestDto>();
        if (dto == null || string.IsNullOrWhiteSpace(dto.ServiceName))
        {
            return NetworkPacket.Create(PacketType.ServiceActionResponse, new ServiceActionResponseDto(false, "Invalid service action request", ServiceState.Unknown));
        }

        bool hasPermission = _permissionChecker.CanManage(session.Permissions, dto.ServiceName);
        if (!hasPermission)
        {
            _logger.LogWarning("Access DENIED: User '{User}' tried to execute '{Action}' on '{Service}'", session.Username, dto.Action, dto.ServiceName);
            await LogAuditAsync(session, dto.Action.ToString(), dto.ServiceName, "FORBIDDEN");

            ServiceState current = await _systemd.GetServiceStateAsync(dto.ServiceName, ct);
            return NetworkPacket.Create(
                PacketType.ServiceActionResponse,
                new ServiceActionResponseDto(false, $"403 FORBIDDEN: Role '{session.RoleName}' is not allowed to manage {dto.ServiceName}", current)
            );
        }

        var (success, message) = await _systemd.ExecuteActionAsync(dto.ServiceName, dto.Action, ct);
        ServiceState newState = await _systemd.GetServiceStateAsync(dto.ServiceName, ct);

        await LogAuditAsync(session, dto.Action.ToString(), dto.ServiceName, success ? "SUCCESS" : "ERROR");

        return NetworkPacket.Create(
            PacketType.ServiceActionResponse,
            new ServiceActionResponseDto(success, message, newState)
        );
    }

    private async Task<NetworkPacket> HandleGetLogsAsync(UserSession session, NetworkPacket packet, CancellationToken ct)
    {
        var dto = packet.DeserializePayload<LogsRequestDto>();
        if (dto == null || string.IsNullOrWhiteSpace(dto.ServiceName))
        {
            return NetworkPacket.Create(PacketType.GetLogsResponse, new LogsResponseDto(string.Empty, "Invalid logs request"));
        }

        bool hasPermission = _permissionChecker.CanViewLogs(session.Permissions, dto.ServiceName);
        if (!hasPermission)
        {
            _logger.LogWarning("Access DENIED: User '{User}' tried to read logs of '{Service}'", session.Username, dto.ServiceName);
            await LogAuditAsync(session, "ViewLogs", dto.ServiceName, "FORBIDDEN");

            return NetworkPacket.Create(
                PacketType.GetLogsResponse,
                new LogsResponseDto(dto.ServiceName, $"403 FORBIDDEN: Role '{session.RoleName}' cannot view logs for {dto.ServiceName}")
            );
        }

        string logs = await _systemd.GetServiceLogsAsync(dto.ServiceName, dto.Lines, ct);
        await LogAuditAsync(session, "ViewLogs", dto.ServiceName, "SUCCESS");

        return NetworkPacket.Create(
            PacketType.GetLogsResponse,
            new LogsResponseDto(dto.ServiceName, logs)
        );
    }

    private async Task<NetworkPacket> HandleGetAuditAsync(UserSession session, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GateDbContext>();

        var logs = await db.AuditLogs
            .OrderByDescending(a => a.ExecutedAt)
            .Take(50)
            .Select(a => new AuditLogDto(a.Id, a.Username, a.Action, a.ServiceName, a.Status, a.ExecutedAt))
            .ToListAsync(ct);

        return NetworkPacket.Create(PacketType.GetAuditResponse, new AuditResponseDto(logs));
    }

    private async Task LogAuditAsync(UserSession session, string action, string serviceName, string status)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GateDbContext>();

            var entry = new AuditLog
            {
                UserId = session.UserId,
                Username = session.Username,
                Action = action,
                ServiceName = serviceName,
                Status = status,
                ExecutedAt = DateTime.UtcNow
            };

            await db.AuditLogs.AddAsync(entry);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log entry for user {Username}", session.Username);
        }
    }
}
