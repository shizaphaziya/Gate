namespace Gate.Server.Database;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public Role Role { get; set; } = null!;
}

public class Role
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class Permission
{
    public int Id { get; set; }
    public int RoleId { get; set; }
    public string ServicePattern { get; set; } = string.Empty;
    public bool CanManage { get; set; }
    public bool CanViewLogs { get; set; }
    public Role Role { get; set; } = null!;
}

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // SUCCESS, FORBIDDEN, ERROR
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}
