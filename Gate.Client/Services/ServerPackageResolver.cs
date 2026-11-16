using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Gate.Client.Services;

public static class ServerPackageResolver
{
    /// <summary>Environment variable with an explicit path to a published Gate.Server binary.</summary>
    public const string ServerBinaryEnvVar = "GATE_SERVER_BINARY";

    public static string ResolveBinaryPath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        string? fromEnv = Environment.GetEnvironmentVariable(ServerBinaryEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return Path.GetFullPath(fromEnv);
        }

        var candidates = new List<string?>
        {
            Path.Combine(AppContext.BaseDirectory, "server-bundle", "Gate.Server"),
            Path.Combine(Directory.GetCurrentDirectory(), "server-bundle", "Gate.Server"),
            Path.Combine(Directory.GetCurrentDirectory(), "Gate.Client", "server-bundle", "Gate.Server"),

            Path.Combine(Directory.GetCurrentDirectory(), "Gate.Server", "bin", "Release", "net8.0", "linux-x64", "publish", "Gate.Server"),
            RepoLocator.InRepo("Gate.Server", "bin", "Release", "net8.0", "linux-x64", "publish", "Gate.Server"),
        };

        foreach (var c in candidates)
        {
            if (!string.IsNullOrEmpty(c) && File.Exists(c))
            {
                return c;
            }
        }

        return string.Empty;
    }

    public static async Task<(bool Success, string BinaryPath, string Output)> AutoBuildServerBinaryAsync()
    {
        var projectCandidates = new List<string?>
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Gate.Server", "Gate.Server.csproj"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Gate.Server", "Gate.Server.csproj"),
            RepoLocator.InRepo("Gate.Server", "Gate.Server.csproj"),
        };

        string? projectPath = projectCandidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        if (projectPath == null)
        {
            return (false, string.Empty, "Исходный проект Gate.Server.csproj не найден для сборки.");
        }

        var outputDir = Path.Combine(AppContext.BaseDirectory, "server-bundle");
        Directory.CreateDirectory(outputDir);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"publish \"{projectPath}\" -c Release -r linux-x64 --self-contained false -p:PublishSingleFile=true -o \"{outputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return (false, string.Empty, "Не удалось запустить dotnet publish.");

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        var output = (await stdoutTask) + "\n" + (await stderrTask);
        var targetFile = Path.Combine(outputDir, "Gate.Server");

        if (proc.ExitCode == 0 && File.Exists(targetFile))
        {
            return (true, targetFile, output);
        }

        return (false, string.Empty, output);
    }
}
