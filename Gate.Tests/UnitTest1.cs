using System.IO;
using Gate.Client.Services;
using Gate.Server.Database;
using Gate.Server.Services;
using Gate.Shared;
using Xunit;

namespace Gate.Tests;

public class GateTests
{
    [Fact]
    public async Task PacketProtocol_LengthPrefixedJsonRoundTrip_PreservesDataIntegrity()
    {
        using var stream = new MemoryStream();

        var originalAuth = new AuthRequestDto("testuser", "securePass123!");
        var originalPacket = NetworkPacket.Create(PacketType.AuthRequest, originalAuth, "token_12345");

        await PacketProtocol.SendPacketAsync(stream, originalPacket);

        stream.Position = 0;

        var receivedPacket = await PacketProtocol.ReadPacketAsync(stream);

        Assert.NotNull(receivedPacket);
        Assert.Equal(PacketType.AuthRequest, receivedPacket.Type);
        Assert.Equal("token_12345", receivedPacket.SessionToken);

        var deserializedAuth = receivedPacket.DeserializePayload<AuthRequestDto>();
        Assert.NotNull(deserializedAuth);
        Assert.Equal("testuser", deserializedAuth.Username);
        Assert.Equal("securePass123!", deserializedAuth.Password);
    }

    [Fact]
    public void PermissionChecker_RbacRules_EnforcesStrictAccessControls()
    {
        var checker = new PermissionChecker();

        var devPermissions = new List<Permission>
        {
            new() { ServicePattern = "nginx*", CanManage = true, CanViewLogs = true },
            new() { ServicePattern = "dummy-*", CanManage = true, CanViewLogs = true },
            new() { ServicePattern = "test-*", CanManage = true, CanViewLogs = true }
        };

        var auditorPermissions = new List<Permission>
        {
            new() { ServicePattern = "*", CanManage = false, CanViewLogs = true }
        };

        var adminPermissions = new List<Permission>
        {
            new() { ServicePattern = "*", CanManage = true, CanViewLogs = true }
        };

        Assert.True(checker.CanManage(devPermissions, "nginx.service"));
        Assert.True(checker.CanManage(devPermissions, "dummy-worker.service"));
        Assert.True(checker.CanManage(devPermissions, "test-api.service"));
        Assert.False(checker.CanManage(devPermissions, "ssh.service"));
        Assert.False(checker.CanViewLogs(devPermissions, "ssh.service"));

        Assert.False(checker.CanManage(auditorPermissions, "nginx.service"));
        Assert.False(checker.CanManage(auditorPermissions, "ssh.service"));
        Assert.True(checker.CanViewLogs(auditorPermissions, "nginx.service"));
        Assert.True(checker.CanViewLogs(auditorPermissions, "ssh.service"));

        Assert.True(checker.CanManage(adminPermissions, "ssh.service"));
        Assert.True(checker.CanViewLogs(adminPermissions, "ssh.service"));
    }

    [Fact]
    public async Task SshDeployer_And_GateClient_FullIntegrationTest()
    {
        string binaryPath = "/home/pilldev/CLionProjects/Gate/Gate.Server/bin/Release/net8.0/linux-x64/publish/Gate.Server";
        if (!File.Exists(binaryPath))
        {
            // Skip integration test if binary not built yet
            return;
        }

        var deployer = new SshDeployerService();
        var logs = new List<string>();

        await deployer.DeployAsync(
            host: "127.0.0.1",
            port: 2222,
            user: "root",
            password: "root",
            localServerBinaryPath: binaryPath,
            onProgressLog: msg => logs.Add(msg)
        );

        Assert.NotEmpty(logs);

        // Give Gate.Server 2 seconds to initialize database seeding and start listening on port 5050
        await Task.Delay(2500);

        using var client = new GateNetworkClient();
        var authRes = await client.LoginAsync("127.0.0.1", 5050, "admin", "admin123");

        Assert.True(authRes.Success);
        Assert.Equal("Admin", authRes.Role);

        var services = await client.GetServicesAsync();
        Assert.NotEmpty(services);

        var dummyService = services.FirstOrDefault(s => s.Name == "dummy-worker.service");
        Assert.NotNull(dummyService);

        var actionRes = await client.ExecuteActionAsync("dummy-worker.service", ServiceAction.Restart);
        Assert.True(actionRes.Success);

        var dummyLogs = await client.GetLogsAsync("dummy-worker.service", 50);
        Assert.False(string.IsNullOrWhiteSpace(dummyLogs));

        using var devClient = new GateNetworkClient();
        var devAuth = await devClient.LoginAsync("127.0.0.1", 5050, "dev", "dev123");
        Assert.True(devAuth.Success);
        Assert.Equal("Developer", devAuth.Role);

        var devActionRes = await devClient.ExecuteActionAsync("ssh.service", ServiceAction.Restart);
        Assert.False(devActionRes.Success);
        Assert.Contains("403 FORBIDDEN", devActionRes.Message);

        var auditLogs = await devClient.GetAuditLogsAsync();
        Assert.NotEmpty(auditLogs);
        Assert.Contains(auditLogs, a => a.Username == "dev" && a.Status == "FORBIDDEN" && a.ServiceName == "ssh.service");
    }

    [Fact]
    public async Task SshDeployer_CheckAndRepair_And_Reset_Flow()
    {
        var deployer = new SshDeployerService();
        var manager = new TestServerManager();
        var logs = new List<string>();
        void Log(string msg) => logs.Add(msg);

        var res1 = await deployer.CheckAndRepairAsync("127.0.0.1", 2222, "root", "root", Log);
        Assert.True(res1.IsInstalled);
        Assert.True(res1.IsRunning);

        using (var proc = System.Diagnostics.Process.Start("docker", "exec gate-test-linux systemctl stop gate.service"))
        {
            await proc.WaitForExitAsync();
        }
        using (var proc = System.Diagnostics.Process.Start("docker", "exec gate-test-linux chmod -x /opt/gate/Gate.Server"))
        {
            await proc.WaitForExitAsync();
        }

        var res2 = await deployer.CheckAndRepairAsync("127.0.0.1", 2222, "root", "root", Log);
        Assert.True(res2.IsInstalled);
        Assert.True(res2.IsRunning);
        Assert.True(res2.WasRepaired);

        await manager.ResetAsync(Log);

        var res3 = await deployer.CheckAndRepairAsync("127.0.0.1", 2222, "root", "root", Log);
        Assert.False(res3.IsInstalled);
        Assert.False(res3.IsRunning);

        await deployer.DeployAsync("127.0.0.1", 2222, "root", "root", "", Log);

        var res4 = await deployer.CheckAndRepairAsync("127.0.0.1", 2222, "root", "root", Log);
        Assert.True(res4.IsInstalled);
        Assert.True(res4.IsRunning);
    }
}
