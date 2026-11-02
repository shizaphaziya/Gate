using System.IO;
using System.Net.Sockets;
using Gate.Shared;

namespace Gate.Client.Services;

public class GateNetworkClient : IDisposable
{
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
    public string? SessionToken { get; private set; }
    public string? CurrentRole { get; private set; }
    public string? CurrentUsername { get; private set; }

    public async Task<AuthResponseDto> LoginAsync(string host, int port, string username, string password, CancellationToken ct = default)
    {
        Disconnect();

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct);
        _stream = _tcpClient.GetStream();

        var authRequest = NetworkPacket.Create(PacketType.AuthRequest, new AuthRequestDto(username, password));
        var responsePacket = await SendAndReceiveAsync(authRequest, ct);

        var authResponse = responsePacket?.DeserializePayload<AuthResponseDto>();
        if (authResponse != null && authResponse.Success && !string.IsNullOrWhiteSpace(authResponse.Token))
        {
            SessionToken = authResponse.Token;
            CurrentRole = authResponse.Role;
            CurrentUsername = username;
            return authResponse;
        }

        Disconnect();
        return authResponse ?? new AuthResponseDto(false, null, null, "Failed to parse authentication response");
    }

    public async Task<List<ServiceInfoDto>> GetServicesAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        var request = NetworkPacket.Create(PacketType.GetServicesRequest, new { }, SessionToken);
        var responsePacket = await SendAndReceiveAsync(request, ct);

        var responseDto = responsePacket?.DeserializePayload<ServicesResponseDto>();
        return responseDto?.Services ?? [];
    }

    public async Task<ServiceActionResponseDto> ExecuteActionAsync(string serviceName, ServiceAction action, CancellationToken ct = default)
    {
        EnsureConnected();

        var request = NetworkPacket.Create(
            PacketType.ServiceActionRequest,
            new ServiceActionRequestDto(serviceName, action),
            SessionToken
        );
        var responsePacket = await SendAndReceiveAsync(request, ct);

        var responseDto = responsePacket?.DeserializePayload<ServiceActionResponseDto>();
        return responseDto ?? new ServiceActionResponseDto(false, "No response from server", ServiceState.Unknown);
    }

    public async Task<string> GetLogsAsync(string serviceName, int lines = 100, CancellationToken ct = default)
    {
        EnsureConnected();

        var request = NetworkPacket.Create(
            PacketType.GetLogsRequest,
            new LogsRequestDto(serviceName, lines),
            SessionToken
        );
        var responsePacket = await SendAndReceiveAsync(request, ct);

        var responseDto = responsePacket?.DeserializePayload<LogsResponseDto>();
        return responseDto?.LogContent ?? "No logs received.";
    }

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(CancellationToken ct = default)
    {
        EnsureConnected();

        var request = NetworkPacket.Create(PacketType.GetAuditRequest, new { }, SessionToken);
        var responsePacket = await SendAndReceiveAsync(request, ct);

        var responseDto = responsePacket?.DeserializePayload<AuditResponseDto>();
        return responseDto?.Logs ?? [];
    }

    public void Disconnect()
    {
        try
        {
            _stream?.Dispose();
            _stream = null;
            _tcpClient?.Close();
            _tcpClient?.Dispose();
            _tcpClient = null;
        }
        catch
        {
            // Ignore socket cleanup errors
        }
        finally
        {
            SessionToken = null;
            CurrentRole = null;
            CurrentUsername = null;
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected || _stream == null || string.IsNullOrWhiteSpace(SessionToken))
        {
            throw new InvalidOperationException("Not connected or not authenticated to Gate server");
        }
    }

    private async Task<NetworkPacket?> SendAndReceiveAsync(NetworkPacket packet, CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("Network stream is null");

        await _sendLock.WaitAsync(ct);
        try
        {
            await PacketProtocol.SendPacketAsync(_stream, packet, ct);
            return await PacketProtocol.ReadPacketAsync(_stream, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        Disconnect();
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
