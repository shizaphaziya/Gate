using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Gate.Shared;

namespace Gate.Client.Services;

public class GateNetworkClient : IDisposable
{
    private TcpClient? _tcpClient;
    private Stream? _stream;
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

        // The server uses a self-signed certificate, so it is pinned per host:port (see CertificateTrustStore).
        bool certificateMismatch = false;
        var sslStream = new SslStream(
            _tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) =>
            {
                if (certificate == null)
                {
                    return false;
                }

                string fingerprint = CertificateTrustStore.Fingerprint(certificate.GetRawCertData());
                var trust = CertificateTrustStore.Verify(host, port, fingerprint);
                certificateMismatch = trust == CertificateTrust.Mismatch;
                return !certificateMismatch;
            });

        try
        {
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, ct);
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException)
        {
            sslStream.Dispose();
            Disconnect();

            string message = certificateMismatch
                ? $"Сертификат сервера {host}:{port} не совпадает с сохранённым. Возможна подмена сервера. " +
                  "Если Gate был переустановлен, переустановите его через приложение или удалите запись о хосте в " +
                  CertificateTrustStore.StorePath + "."
                : "Не удалось установить защищённое соединение (TLS): " + ex.Message;
            return new AuthResponseDto(false, null, null, message);
        }

        _stream = sslStream;

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
