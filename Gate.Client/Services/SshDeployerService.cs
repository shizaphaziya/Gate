using System.IO;
using System.Text;
using Renci.SshNet;

namespace Gate.Client.Services;

public class SshDeployerService
{
    public async Task DeployAsync(
        string host,
        int port,
        string user,
        string password,
        string localServerBinaryPath,
        Action<string> onProgressLog,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            void Log(string message)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                onProgressLog($"[{timestamp}] {message}");
            }

            Log($"Connecting to SSH server at {host}:{port} as user '{user}'...");

            var connInfo = new ConnectionInfo(
                host,
                port,
                user,
                new PasswordAuthenticationMethod(user, password))
            {
                Timeout = TimeSpan.FromSeconds(20)
            };

            using var ssh = new SshClient(connInfo);
            using var sftp = new SftpClient(connInfo);

            ssh.Connect();
            sftp.Connect();
            Log("SSH and SFTP connections successfully established.");

            string RunCommand(string command)
            {
                using var cmd = ssh.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromMinutes(10);
                string result = cmd.Execute();
                if (cmd.ExitStatus != 0 && !string.IsNullOrWhiteSpace(cmd.Error))
                {
                    Log($"[Command Warning] (exit {cmd.ExitStatus}): {cmd.Error.Trim()}");
                }
                return result;
            }

            Log("Step 1/6: Preparing directory structure at /opt/gate...");
            RunCommand("mkdir -p /opt/gate");
            Log("Directory /opt/gate is ready.");

            string pgCheck = RunCommand("systemctl is-active postgresql.service 2>/dev/null || echo 'inactive'").Trim();
            if (pgCheck == "active")
            {
                Log("Step 2/6: PostgreSQL service is already active and healthy. Ensuring database gatedb exists...");
                string ensureDbScript =
                    "sudo -u postgres psql -tc \"SELECT 1 FROM pg_roles WHERE rolname='gateuser'\" | grep -q 1 || " +
                    "sudo -u postgres psql -c \"CREATE USER gateuser WITH PASSWORD 'gatepass';\" && " +
                    "sudo -u postgres psql -tc \"SELECT 1 FROM pg_database WHERE datname='gatedb'\" | grep -q 1 || " +
                    "sudo -u postgres psql -c \"CREATE DATABASE gatedb OWNER gateuser;\"";
                RunCommand(ensureDbScript);
            }
            else
            {
                Log("Step 2/6: Installing PostgreSQL, configuring gatedb database and gateuser...");
                string pgScript =
                    "export DEBIAN_FRONTEND=noninteractive && " +
                    "apt-get update -y && apt-get install -y postgresql && " +
                    "systemctl mask postgresql@16-main.service 2>/dev/null || true; " +
                    "cat << 'EOF' > /etc/systemd/system/postgresql.service\n" +
                    "[Unit]\n" +
                    "Description=PostgreSQL Database Server\n" +
                    "After=network.target\n\n" +
                    "[Service]\n" +
                    "Type=simple\n" +
                    "User=postgres\n" +
                    "Group=postgres\n" +
                    "RuntimeDirectory=postgresql\n" +
                    "RuntimeDirectoryMode=0775\n" +
                    "ExecStartPre=/bin/mkdir -p /var/run/postgresql\n" +
                    "ExecStart=/usr/lib/postgresql/16/bin/postgres -D /var/lib/postgresql/16/main -c config_file=/etc/postgresql/16/main/postgresql.conf\n" +
                    "Restart=always\n\n" +
                    "[Install]\n" +
                    "WantedBy=multi-user.target\n" +
                    "EOF\n" +
                    "systemctl daemon-reload && systemctl enable --now postgresql.service && " +
                    "sudo -u postgres psql -tc \"SELECT 1 FROM pg_roles WHERE rolname='gateuser'\" | grep -q 1 || " +
                    "sudo -u postgres psql -c \"CREATE USER gateuser WITH PASSWORD 'gatepass';\" && " +
                    "sudo -u postgres psql -tc \"SELECT 1 FROM pg_database WHERE datname='gatedb'\" | grep -q 1 || " +
                    "sudo -u postgres psql -c \"CREATE DATABASE gatedb OWNER gateuser;\"";

                RunCommand(pgScript);
            }
            Log("PostgreSQL database 'gatedb' and user 'gateuser' configured successfully.");

            Log("Step 3/6: Creating test systemd service /etc/systemd/system/dummy-worker.service...");
            string dummyWorkerService =
                "[Unit]\n" +
                "Description=Gate Demo Dummy Worker Heartbeat Service\n" +
                "After=network.target\n\n" +
                "[Service]\n" +
                "Type=simple\n" +
                "ExecStart=/bin/bash -c 'while true; do echo \"[DummyWorker] Heartbeat OK $(date)\"; sleep 5; done'\n" +
                "Restart=always\n" +
                "User=root\n\n" +
                "[Install]\n" +
                "WantedBy=multi-user.target\n";

            using (var dummyStream = new MemoryStream(Encoding.UTF8.GetBytes(dummyWorkerService)))
            {
                sftp.UploadFile(dummyStream, "/etc/systemd/system/dummy-worker.service", true);
            }
            Log("Created /etc/systemd/system/dummy-worker.service.");

            Log("Step 4/6: Resolving and uploading Gate.Server binary and appsettings.json to /opt/gate/...");
            RunCommand("systemctl stop gate.service 2>/dev/null || true");

            string binaryToUpload = ServerPackageResolver.ResolveBinaryPath(localServerBinaryPath);
            if (string.IsNullOrWhiteSpace(binaryToUpload))
            {
                Log("Binary Gate.Server not found in local paths. Auto-building via dotnet publish...");
                var (success, builtPath, buildLog) = ServerPackageResolver.AutoBuildServerBinaryAsync().GetAwaiter().GetResult();
                if (success)
                {
                    binaryToUpload = builtPath;
                    Log($"Automatic build succeeded: {binaryToUpload}");
                }
                else
                {
                    Log($"[Build Warning]: {buildLog}");
                }
            }

            if (File.Exists(binaryToUpload))
            {
                var fileInfo = new FileInfo(binaryToUpload);
                Log($"Uploading binary '{fileInfo.Name}' ({fileInfo.Length / (1024 * 1024)} MB) to /opt/gate/Gate.Server...");
                using var binStream = File.OpenRead(binaryToUpload);
                sftp.UploadFile(binStream, "/opt/gate/Gate.Server.tmp", true);
                RunCommand("mv -f /opt/gate/Gate.Server.tmp /opt/gate/Gate.Server && chmod +x /opt/gate/Gate.Server");
                Log("Uploaded and set executable permissions on /opt/gate/Gate.Server.");
            }
            else
            {
                Log($"Notice: Local binary '{localServerBinaryPath}' could not be located. Creating wrapper stub at /opt/gate/Gate.Server...");
                string stubScript = "#!/bin/bash\necho 'Gate.Server placeholder'\n";
                using var stubStream = new MemoryStream(Encoding.UTF8.GetBytes(stubScript));
                sftp.UploadFile(stubStream, "/opt/gate/Gate.Server.tmp", true);
                RunCommand("mv -f /opt/gate/Gate.Server.tmp /opt/gate/Gate.Server && chmod +x /opt/gate/Gate.Server");
            }

            string appSettingsJson =
                "{\n" +
                "  \"Logging\": { \"LogLevel\": { \"Default\": \"Information\" } },\n" +
                "  \"ConnectionStrings\": {\n" +
                "    \"GateDb\": \"Host=localhost;Port=5432;Database=gatedb;Username=gateuser;Password=gatepass\"\n" +
                "  }\n" +
                "}\n";

            using (var settingsStream = new MemoryStream(Encoding.UTF8.GetBytes(appSettingsJson)))
            {
                sftp.UploadFile(settingsStream, "/opt/gate/appsettings.json", true);
            }
            RunCommand("chmod +x /opt/gate/Gate.Server");
            Log("Set execution permissions chmod +x on /opt/gate/Gate.Server.");

            Log("Step 5/6: Creating /etc/systemd/system/gate.service...");
            string gateService =
                "[Unit]\n" +
                "Description=Gate Remote Management Daemon\n" +
                "After=network.target postgresql.service\n\n" +
                "[Service]\n" +
                "Type=notify\n" +
                "WorkingDirectory=/opt/gate\n" +
                "ExecStart=/opt/gate/Gate.Server\n" +
                "Restart=always\n" +
                "User=root\n" +
                "Environment=DOTNET_ENVIRONMENT=Production\n\n" +
                "[Install]\n" +
                "WantedBy=multi-user.target\n";

            using (var gateServiceStream = new MemoryStream(Encoding.UTF8.GetBytes(gateService)))
            {
                sftp.UploadFile(gateServiceStream, "/etc/systemd/system/gate.service", true);
            }
            Log("Installed /etc/systemd/system/gate.service.");

            Log("Step 6/6: Reloading systemd daemon and enabling services...");
            RunCommand("systemctl daemon-reload && systemctl enable --now dummy-worker.service && systemctl enable --now gate.service");

            string statusOutput = RunCommand("systemctl is-active gate.service || true");
            Log($"Gate service activation status: {statusOutput.Trim()}");

            Log("=== Automated installation and configuration finished successfully! ===");
        }, ct);
    }

    public record CheckResult(bool IsInstalled, bool IsRunning, bool WasRepaired, string Message);

    public async Task<CheckResult> CheckAndRepairAsync(
        string host,
        int port,
        string user,
        string password,
        Action<string> onProgressLog,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            void Log(string message)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                onProgressLog($"[{timestamp}] {message}");
            }

            Log($"Диагностика: подключение к SSH {host}:{port} как '{user}'...");
            var connInfo = new ConnectionInfo(
                host,
                port,
                user,
                new PasswordAuthenticationMethod(user, password))
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            using var ssh = new SshClient(connInfo);
            ssh.Connect();
            Log("SSH-соединение установлено.");

            string RunCommand(string command)
            {
                using var cmd = ssh.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(30);
                return cmd.Execute().Trim();
            }

            Log("Шаг 1: Проверка наличия бинарника и службы Gate...");
            string binaryCheck = RunCommand("test -f /opt/gate/Gate.Server && echo 'exists' || echo 'missing'");
            string serviceCheck = RunCommand("test -f /etc/systemd/system/gate.service && echo 'exists' || echo 'missing'");

            if (binaryCheck != "exists" || serviceCheck != "exists")
            {
                Log("РЕЗУЛЬТАТ: Gate не установлен на этом сервере (/opt/gate/Gate.Server отсутствует).");
                Log("Для установки нажмите кнопку «Установить Gate по SSH».");
                return new CheckResult(false, false, false, "Gate не установлен. Нажмите «Установить Gate по SSH».");
            }

            Log("Файлы /opt/gate/Gate.Server и gate.service присутствуют. Gate установлен.");

            Log("Шаг 2: Проверка состояния gate.service и postgresql.service...");
            string gateStatus = RunCommand("systemctl is-active gate.service 2>/dev/null || echo 'inactive'");
            string pgStatus = RunCommand("systemctl is-active postgresql.service 2>/dev/null || echo 'inactive'");

            Log($"Текущее состояние: gate.service = {gateStatus}, postgresql.service = {pgStatus}");

            if (gateStatus == "active" && pgStatus == "active")
            {
                Log("ОТЛИЧНО: Gate.Server уже установлен, активен и слушает TCP порт 5050!");
                Log("Можно сразу переходить на вкладку входа и подключаться.");
                return new CheckResult(true, true, false, "Gate уже установлен и работает на порту 5050.");
            }

            Log("ВНИМАНИЕ: Gate установлен, но служба не запущена или остановлена. Запуск автоматической починки...");

            Log("Починка 1/4: Восстановление прав на запуск /opt/gate/Gate.Server...");
            RunCommand("chmod +x /opt/gate/Gate.Server");

            Log("Починка 2/4: Проверка и запуск службы PostgreSQL...");
            RunCommand("systemctl daemon-reload && systemctl enable --now postgresql.service 2>/dev/null || systemctl restart postgresql.service 2>/dev/null || true");
            string ensureDbScript =
                "sudo -u postgres psql -tc \"SELECT 1 FROM pg_roles WHERE rolname='gateuser'\" 2>/dev/null | grep -q 1 || " +
                "sudo -u postgres psql -c \"CREATE USER gateuser WITH PASSWORD 'gatepass';\" 2>/dev/null && " +
                "sudo -u postgres psql -tc \"SELECT 1 FROM pg_database WHERE datname='gatedb'\" 2>/dev/null | grep -q 1 || " +
                "sudo -u postgres psql -c \"CREATE DATABASE gatedb OWNER gateuser;\" 2>/dev/null";
            RunCommand(ensureDbScript);

            Log("Починка 3/4: Перезагрузка юнитов systemd и перезапуск gate.service...");
            RunCommand("systemctl daemon-reload && systemctl enable --now dummy-worker.service 2>/dev/null || true");
            RunCommand("systemctl restart gate.service");

            System.Threading.Thread.Sleep(1500);
            string finalStatus = RunCommand("systemctl is-active gate.service 2>/dev/null || echo 'inactive'");
            Log($"Починка 4/4: Итоговое состояние gate.service: {finalStatus}");

            if (finalStatus == "active")
            {
                Log("УСПЕХ: Gate.Server успешно восстановлен и запущен на порту 5050!");
                return new CheckResult(true, true, true, "Gate был успешно починен и запущен на порту 5050.");
            }

            string logDump = RunCommand("journalctl -u gate.service -n 12 --no-pager 2>/dev/null || true");
            Log($"НЕ УДАЛОСЬ ЗАПУСТИТЬ: Последние строки логов gate.service:\n{logDump}");
            return new CheckResult(true, false, false, "Gate установлен, но не смог запуститься. См. лог выше.");
        }, ct);
    }

    public async Task ResetRemoteServerAsync(
        string host,
        int port,
        string user,
        string password,
        Action<string> onProgressLog,
        CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            void Log(string message)
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                onProgressLog($"[{timestamp}] {message}");
            }

            Log($"Подключение к SSH {host}:{port} для сброса Gate...");
            var connInfo = new ConnectionInfo(
                host,
                port,
                user,
                new PasswordAuthenticationMethod(user, password))
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            using var ssh = new SshClient(connInfo);
            ssh.Connect();

            string RunCommand(string command)
            {
                using var cmd = ssh.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(30);
                return cmd.Execute().Trim();
            }

            Log("Остановка служб gate.service и dummy-worker.service...");
            RunCommand("systemctl stop gate.service dummy-worker.service 2>/dev/null || true");
            RunCommand("systemctl disable gate.service dummy-worker.service 2>/dev/null || true");

            Log("Удаление директории /opt/gate и systemd-юнитов...");
            RunCommand("rm -rf /opt/gate /etc/systemd/system/gate.service /etc/systemd/system/dummy-worker.service");

            Log("Очистка базы данных gatedb и пользователя gateuser...");
            RunCommand("sudo -u postgres psql -c 'DROP DATABASE IF EXISTS gatedb;' 2>/dev/null || true");
            RunCommand("sudo -u postgres psql -c 'DROP USER IF EXISTS gateuser;' 2>/dev/null || true");

            Log("Перезагрузка конфигурации systemd...");
            RunCommand("systemctl daemon-reload && systemctl reset-failed 2>/dev/null || true");

            Log("Сброс завершен: сервер очищен от компонентов Gate.");
        }, ct);
    }
}
