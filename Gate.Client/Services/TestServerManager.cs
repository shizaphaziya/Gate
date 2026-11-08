using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Gate.Client.Services;

public record ContainerStatusInfo(
    bool IsRunning,
    string StatusText,
    string StatusColor,
    string Details
);

public class TestServerManager
{
    private static string FindTestEnvDir()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "test-env"),
            "/home/pilldev/CLionProjects/Gate/test-env",
            "/home/pilldev/RiderProjects/Gate/test-env",
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../test-env")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../test-env")),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "docker-compose.yml")))
            {
                return c;
            }
        }

        return Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), "test-env"))
            ? Path.Combine(Directory.GetCurrentDirectory(), "test-env")
            : "/home/pilldev/CLionProjects/Gate/test-env";
    }

    public async Task<ContainerStatusInfo> GetStatusAsync()
    {
        try
        {
            var (code, stdout, _) = await RunProcessAsync("docker", "inspect --format \"{{.State.Status}}|{{.State.Running}}|{{.Created}}|{{.State.StartedAt}}\" gate-test-linux");
            if (code != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return new ContainerStatusInfo(false, "Не создан", "#4B5563", "Контейнер gate-test-linux не найден в системе.");
            }

            var parts = stdout.Trim().Trim('"').Split('|');
            var status = parts.Length > 0 ? parts[0].ToLowerInvariant() : "unknown";
            var running = parts.Length > 1 && bool.TryParse(parts[1], out var r) && r;

            if (running)
            {
                var startedAt = parts.Length > 3 ? parts[3] : "";
                return new ContainerStatusInfo(true, "Работает (Online)", "#047857", $"Запущен в {startedAt}. Порты: 2222->22, 5050->5050");
            }

            return new ContainerStatusInfo(false, $"Остановлен ({status})", "#B91C1C", "Контейнер существует, но не запущен.");
        }
        catch (Exception ex)
        {
            return new ContainerStatusInfo(false, "Ошибка опроса", "#B91C1C", ex.Message);
        }
    }

    public async Task StartAsync(Action<string> onLog)
    {
        var status = await GetStatusAsync();
        if (status.StatusText == "Не создан")
        {
            var dir = FindTestEnvDir();
            onLog($"[{DateTime.Now:HH:mm:ss}] Сборка и первый запуск контейнера из {dir}...");
            await RunStreamAsync("docker", $"compose -f {Path.Combine(dir, "docker-compose.yml")} up -d --build", onLog);
        }
        else
        {
            onLog($"[{DateTime.Now:HH:mm:ss}] Запуск остановленного контейнера gate-test-linux...");
            await RunStreamAsync("docker", "start gate-test-linux", onLog);
        }

        onLog($"[{DateTime.Now:HH:mm:ss}] Проверка состояния systemd внутри контейнера...");
        await RunStreamAsync("docker", "exec gate-test-linux systemctl is-system-running", onLog);
        onLog($"[{DateTime.Now:HH:mm:ss}] Сервер готов к работе по SSH (порт 2222) и Gate (порт 5050)!");
    }

    public async Task StopAsync(Action<string> onLog)
    {
        onLog($"[{DateTime.Now:HH:mm:ss}] Остановка контейнера gate-test-linux...");
        await RunStreamAsync("docker", "stop gate-test-linux", onLog);
        onLog($"[{DateTime.Now:HH:mm:ss}] Контейнер успешно остановлен.");
    }

    public async Task RestartAsync(Action<string> onLog)
    {
        onLog($"[{DateTime.Now:HH:mm:ss}] Перезапуск контейнера gate-test-linux...");
        await RunStreamAsync("docker", "restart gate-test-linux", onLog);
        onLog($"[{DateTime.Now:HH:mm:ss}] Контейнер перезапущен.");
    }

    public async Task RebuildAsync(Action<string> onLog)
    {
        var dir = FindTestEnvDir();
        onLog($"[{DateTime.Now:HH:mm:ss}] Полная пересборка контейнера через docker compose (директория: {dir})...");
        await RunStreamAsync("docker", $"compose -f {Path.Combine(dir, "docker-compose.yml")} up -d --build --force-recreate", onLog);
        onLog($"[{DateTime.Now:HH:mm:ss}] Пересборка завершена.");
    }

    public async Task ResetAsync(Action<string> onLog)
    {
        var status = await GetStatusAsync();
        if (!status.IsRunning)
        {
            onLog($"[{DateTime.Now:HH:mm:ss}] Контейнер остановлен. Выполняется запуск перед сбросом...");
            await StartAsync(onLog);
        }

        onLog($"[{DateTime.Now:HH:mm:ss}] Сброс тестового сервера: удаление служб Gate и очистка базы данных...");
        string resetCmd =
            "systemctl stop gate.service dummy-worker.service 2>/dev/null || true; " +
            "systemctl disable gate.service dummy-worker.service 2>/dev/null || true; " +
            "rm -rf /opt/gate /etc/systemd/system/gate.service /etc/systemd/system/dummy-worker.service; " +
            "sudo -u postgres psql -c 'DROP DATABASE IF EXISTS gatedb;' 2>/dev/null || true; " +
            "sudo -u postgres psql -c 'DROP USER IF EXISTS gateuser;' 2>/dev/null || true; " +
            "systemctl daemon-reload; " +
            "systemctl reset-failed 2>/dev/null || true";

        await RunStreamAsync("docker", $"exec gate-test-linux bash -c \"{resetCmd}\"", onLog);
        onLog($"[{DateTime.Now:HH:mm:ss}] Сброс завершен успешно! Сервер возвращен в исходное чистое состояние.");
        onLog($"[{DateTime.Now:HH:mm:ss}] Теперь можно выполнить чистую установку Gate через вкладку «Установка по SSH».");
    }

    public async Task FetchJournalLogsAsync(Action<string> onLog, int count = 25)
    {
        onLog($"[{DateTime.Now:HH:mm:ss}] Чтение последних записей journalctl из тестового сервера:");
        await RunStreamAsync("docker", $"exec gate-test-linux journalctl -n {count} --no-pager", onLog);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunProcessAsync(string file, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Не удалось запустить {file}");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task RunStreamAsync(string file, string args, Action<string> onLog)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            onLog($"[ОШИБКА] Не удалось запустить {file}");
            return;
        }

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) onLog(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onLog(e.Data);
        };

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync();
    }
}
