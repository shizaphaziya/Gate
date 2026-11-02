using Gate.Server.Database;

namespace Gate.Server.Services;

public class PermissionChecker
{
    public bool MatchesPattern(string pattern, string serviceName)
    {
        if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(serviceName))
            return false;

        pattern = pattern.Trim();
        serviceName = serviceName.Trim();

        if (pattern == "*")
            return true;

        if (pattern.EndsWith('*'))
        {
            string prefix = pattern[..^1];
            return serviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(pattern, serviceName, StringComparison.OrdinalIgnoreCase);
    }

    public bool CanManage(IEnumerable<Permission> permissions, string serviceName)
    {
        return permissions.Any(p => p.CanManage && MatchesPattern(p.ServicePattern, serviceName));
    }

    public bool CanViewLogs(IEnumerable<Permission> permissions, string serviceName)
    {
        return permissions.Any(p => p.CanViewLogs && MatchesPattern(p.ServicePattern, serviceName));
    }
}
