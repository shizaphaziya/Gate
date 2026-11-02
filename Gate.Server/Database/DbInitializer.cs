using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gate.Server.Database;

public static class DbInitializer
{
    public static async Task SeedAsync(GateDbContext context, ILogger logger)
    {
        try
        {
            await context.Database.EnsureCreatedAsync();

            if (!await context.Roles.AnyAsync())
            {
                logger.LogInformation("Database empty. Seeding initial roles, permissions and users...");

                var adminRole = new Role
                {
                    Name = "Admin",
                    Permissions = new List<Permission>
                    {
                        new() { ServicePattern = "*", CanManage = true, CanViewLogs = true }
                    }
                };

                var devRole = new Role
                {
                    Name = "Developer",
                    Permissions = new List<Permission>
                    {
                        new() { ServicePattern = "nginx*", CanManage = true, CanViewLogs = true },
                        new() { ServicePattern = "dummy-*", CanManage = true, CanViewLogs = true },
                        new() { ServicePattern = "test-*", CanManage = true, CanViewLogs = true }
                    }
                };

                var auditorRole = new Role
                {
                    Name = "Auditor",
                    Permissions = new List<Permission>
                    {
                        new() { ServicePattern = "*", CanManage = false, CanViewLogs = true }
                    }
                };

                await context.Roles.AddRangeAsync(adminRole, devRole, auditorRole);
                await context.SaveChangesAsync();

                var users = new List<User>
                {
                    new()
                    {
                        Username = "admin",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                        RoleId = adminRole.Id
                    },
                    new()
                    {
                        Username = "dev",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("dev123"),
                        RoleId = devRole.Id
                    },
                    new()
                    {
                        Username = "auditor",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("audit123"),
                        RoleId = auditorRole.Id
                    }
                };

                await context.Users.AddRangeAsync(users);
                await context.SaveChangesAsync();

                logger.LogInformation("Database seeding completed successfully.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
