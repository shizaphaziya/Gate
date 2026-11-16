using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gate.Server.Services;

/// <summary>
/// Supplies the TLS server certificate for the Gate TCP channel.
///
/// Configuration (appsettings.json / environment variables):
///   Gate:Tls:CertificatePath      - PFX file to use. Default: gate-server.pfx next to the binary.
///   Gate:Tls:CertificatePassword  - PFX password. Default: empty.
///
/// If the PFX file does not exist, a self-signed certificate is generated and saved there,
/// so that its fingerprint stays stable across restarts. The SHA-256 fingerprint is also written
/// to "&lt;pfx&gt;.sha256" so that an installer can pin it over a trusted channel (SSH).
/// </summary>
public class TlsCertificateProvider
{
    private readonly ILogger<TlsCertificateProvider> _logger;
    private readonly string _path;
    private readonly string _password;
    private readonly Lazy<X509Certificate2> _certificate;

    public TlsCertificateProvider(IConfiguration configuration, ILogger<TlsCertificateProvider> logger)
    {
        _logger = logger;
        _path = configuration["Gate:Tls:CertificatePath"] is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "gate-server.pfx");
        _password = configuration["Gate:Tls:CertificatePassword"] ?? string.Empty;
        _certificate = new Lazy<X509Certificate2>(LoadOrCreate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public X509Certificate2 Certificate => _certificate.Value;

    public static string Fingerprint(X509Certificate certificate)
    {
        return Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    }

    private X509Certificate2 LoadOrCreate()
    {
        if (File.Exists(_path))
        {
            var existing = new X509Certificate2(_path, _password);
            _logger.LogInformation("Loaded TLS certificate from {Path} (SHA-256 {Fingerprint})", _path, Fingerprint(existing));
            WriteFingerprintFile(existing);
            return existing;
        }

        _logger.LogInformation("No TLS certificate found at {Path}. Generating a self-signed certificate...", _path);
        byte[] pfx = CreateSelfSignedPfx(_password);

        string? dir = Path.GetDirectoryName(Path.GetFullPath(_path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(_path, pfx);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var created = new X509Certificate2(pfx, _password);
        _logger.LogInformation("Generated TLS certificate {Path} (SHA-256 {Fingerprint})", _path, Fingerprint(created));
        WriteFingerprintFile(created);
        return created;
    }

    private void WriteFingerprintFile(X509Certificate2 certificate)
    {
        try
        {
            File.WriteAllText(_path + ".sha256", Fingerprint(certificate) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the certificate fingerprint file next to {Path}", _path);
        }
    }

    private static byte[] CreateSelfSignedPfx(string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Gate.Server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        try
        {
            san.AddDnsName(Dns.GetHostName());
        }
        catch
        {
            // Host name is optional
        }
        request.CertificateExtensions.Add(san.Build());

        using X509Certificate2 cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        return cert.Export(X509ContentType.Pfx, password);
    }
}
