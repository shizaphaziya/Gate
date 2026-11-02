using System.Diagnostics;
using System.Text.RegularExpressions;
using Gate.Shared;
using Microsoft.Extensions.Logging;

namespace Gate.Server.Services;

public partial class SystemdManager
{
    private readonly ILogger<SystemdManager> _logger;
    private static readonly string SystemctlPath = File.Exists("/usr/bin/systemctl") ? "/usr/bin/systemctl" : "systemctl";
    private static readonly string JournalctlPath = File.Exists("/usr/bin/journalctl") ? "/usr/bin/journalctl" : "journalctl";

    public static readonly (string Name, string Description)[] DefaultServices =
    [
        ("nginx.service", "Nginx HTTP and Reverse Proxy Server"),
        ("postgresql.service", "PostgreSQL Relational Database Server"),
        ("ssh.service", "OpenSSH Remote Secure Shell Daemon"),
        ("dummy-worker.service", "Gate Demo Dummy Worker Heartbeat Service"),
        ("test-api.service", "Internal Test API Microservice")
    ];

    [GeneratedRegex(@"^[a-zA-Z0-9_\-\.]+\.service$")]
    private static partial Regex ServiceNameRegex();

    public SystemdManager(ILogger<SystemdManager> logger)
    {
        _logger = logger;
    }

    public async Task<ServiceState> GetServiceStateAsync(string serviceName, CancellationToken ct = default)
    {
        if (!ServiceNameRegex().IsMatch(serviceName))
        {
            _logger.LogWarning("Invalid service name requested for state: {Service}", serviceName);
            return ServiceState.Unknown;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SystemctlPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("is-active");
            psi.ArgumentList.Add(serviceName);

            using var process = Process.Start(psi);
            if (process == null)
            {
                return ServiceState.Unknown;
            }

            string stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            string status = stdout.Trim().ToLowerInvariant();
            return status switch
            {
                "active" => ServiceState.Active,
                "inactive" or "dead" => ServiceState.Inactive,
                "failed" => ServiceState.Failed,
                _ => ServiceState.Unknown
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get service state for {Service}", serviceName);
            return ServiceState.Unknown;
        }
    }

    public async Task<(bool Success, string Message)> ExecuteActionAsync(string serviceName, ServiceAction action, CancellationToken ct = default)
    {
        if (!ServiceNameRegex().IsMatch(serviceName))
        {
            return (false, "Validation error: invalid service name format. Expected *.service");
        }

        string actionArg = action switch
        {
            ServiceAction.Start => "start",
            ServiceAction.Stop => "stop",
            ServiceAction.Restart => "restart",
            ServiceAction.Status => "status",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = SystemctlPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(actionArg);
            psi.ArgumentList.Add(serviceName);

            using var process = Process.Start(psi);
            if (process == null)
            {
                return (false, "Failed to start systemctl process");
            }

            string stdout = await process.StandardOutput.ReadToEndAsync(ct);
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            bool success = process.ExitCode == 0;
            string output = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}".Trim();
            if (string.IsNullOrWhiteSpace(output))
            {
                output = success ? $"Action '{actionArg}' completed successfully" : $"Action '{actionArg}' failed with exit code {process.ExitCode}";
            }

            return (success, output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute {Action} on {Service}", action, serviceName);
            return (false, $"Execution exception: {ex.Message}");
        }
    }

    public async Task<string> GetServiceLogsAsync(string serviceName, int lines = 100, CancellationToken ct = default)
    {
        if (!ServiceNameRegex().IsMatch(serviceName))
        {
            return "Validation error: invalid service name format.";
        }

        if (lines <= 0 || lines > 1000)
        {
            lines = 100;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = JournalctlPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(serviceName);
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(lines.ToString());
            psi.ArgumentList.Add("--no-pager");

            using var process = Process.Start(psi);
            if (process == null)
            {
                return "Failed to start journalctl process";
            }

            string stdout = await process.StandardOutput.ReadToEndAsync(ct);
            string stderr = await process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                return stdout;
            }

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                return stderr;
            }

            return $"No logs found for unit {serviceName}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get logs for {Service}", serviceName);
            return $"Failed to retrieve logs: {ex.Message}";
        }
    }
}
