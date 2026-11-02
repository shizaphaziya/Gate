using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Gate.Client.Services;

public static class ServerPackageResolver
{
    public static string ResolveBinaryPath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        var candidates = new[]
        {
            // Bundled with app
            Path.Combine(AppContext.BaseDirectory, "server-bundle", "Gate.Server"),
            Path.Combine(Directory.GetCurrentDirectory(), "server-bundle", "Gate.Server"),
            Path.Combine(Directory.GetCurrentDirectory(), "Gate.Client", "server-bundle", "Gate.Server"),

            // Published binaries
            Path.Combine(Directory.GetCurrentDirectory(), "Gate.Server", "bin", "Release", "net8.0", "linux-x64", "publish", "Gate.Server"),
            "/home/pilldev/CLionProjects/Gate/Gate.Server/bin/Release/net8.0/linux-x64/publish/Gate.Server",
            "/home/pilldev/RiderProjects/Gate/Gate.Server/bin/Release/net8.0/linux-x64/publish/Gate.Server",

            // Sibling paths
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Gate.Server/bin/Release/net8.0/linux-x64/publish/Gate.Server")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Gate.Server/bin/Release/net8.0/linux-x64/publish/Gate.Server")),
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return c;
            }
        }

        return string.Empty;
    }

    public static async Task<(bool Success, string BinaryPath, string Output)> AutoBuildServerBinaryAsync()
    {
        var projectCandidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Gate.Server", "Gate.Server.csproj"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "Gate.Server", "Gate.Server.csproj"),
            "/home/pilldev/CLionProjects/Gate/Gate.Server/Gate.Server.csproj",
            "/home/pilldev/RiderProjects/Gate/Gate.Server/Gate.Server.csproj",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Gate.Server/Gate.Server.csproj")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Gate.Server/Gate.Server.csproj")),
        };

        string? projectPath = projectCandidates.FirstOrDefault(File.Exists);
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
