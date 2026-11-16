using System.Security.Cryptography;
using System.Text.Json;

namespace Gate.Client.Services;

public enum CertificateTrust
{
    /// <summary>The fingerprint matches the one pinned earlier.</summary>
    Trusted,

    /// <summary>First connection to this host: the fingerprint has just been pinned (trust on first use).</summary>
    TrustedOnFirstUse,

    /// <summary>The server presented a different certificate than the pinned one.</summary>
    Mismatch
}

/// <summary>
/// Remembers the SHA-256 fingerprint of the Gate.Server certificate per host:port, like SSH known_hosts.
/// Gate.Server uses a self-signed certificate, so the client pins it instead of validating a CA chain.
///
/// The file location can be overridden with the GATE_KNOWN_HOSTS environment variable.
/// </summary>
public static class CertificateTrustStore
{
    public const string KnownHostsEnvVar = "GATE_KNOWN_HOSTS";

    private static readonly object Sync = new();

    public static string StorePath
    {
        get
        {
            string? fromEnv = Environment.GetEnvironmentVariable(KnownHostsEnvVar);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Gate",
                "known_hosts.json");
        }
    }

    public static string Fingerprint(byte[] rawCertificateData)
    {
        return Convert.ToHexString(SHA256.HashData(rawCertificateData));
    }

    public static CertificateTrust Verify(string host, int port, string fingerprint)
    {
        lock (Sync)
        {
            var entries = Load();
            string key = Key(host, port);

            if (!entries.TryGetValue(key, out string? pinned))
            {
                entries[key] = fingerprint;
                Save(entries);
                return CertificateTrust.TrustedOnFirstUse;
            }

            return string.Equals(pinned, fingerprint, StringComparison.OrdinalIgnoreCase)
                ? CertificateTrust.Trusted
                : CertificateTrust.Mismatch;
        }
    }

    /// <summary>Pins a fingerprint obtained over a trusted channel (for example SSH during installation).</summary>
    public static void Pin(string host, int port, string fingerprint)
    {
        lock (Sync)
        {
            var entries = Load();
            entries[Key(host, port)] = fingerprint.Trim();
            Save(entries);
        }
    }

    /// <summary>Forgets the pinned fingerprint so the next connection pins the new certificate.</summary>
    public static void Forget(string host, int port)
    {
        lock (Sync)
        {
            var entries = Load();
            if (entries.Remove(Key(host, port)))
            {
                Save(entries);
            }
        }
    }

    private static string Key(string host, int port) => $"{host.Trim().ToLowerInvariant()}:{port}";

    private static Dictionary<string, string> Load()
    {
        try
        {
            string path = StorePath;
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                if (loaded != null)
                {
                    return new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // A corrupt or unreadable store is treated as empty.
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save(Dictionary<string, string> entries)
    {
        try
        {
            string path = StorePath;
            string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // If the store cannot be written, the connection still works; the certificate is just not remembered.
        }
    }
}
