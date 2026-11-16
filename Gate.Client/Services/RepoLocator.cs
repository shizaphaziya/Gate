using System;
using System.IO;

namespace Gate.Client.Services;

/// <summary>
/// Finds the root of the Gate source tree (the folder that contains Gate.sln) without
/// relying on machine-specific absolute paths.
/// The GATE_REPO_ROOT environment variable overrides the search.
/// </summary>
public static class RepoLocator
{
    public const string RepoRootEnvVar = "GATE_REPO_ROOT";

    public static string? FindRoot()
    {
        string? fromEnv = Environment.GetEnvironmentVariable(RepoRootEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(Path.Combine(fromEnv, "Gate.sln")))
        {
            return Path.GetFullPath(fromEnv);
        }

        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Gate.sln")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }

        return null;
    }

    /// <summary>Combines the repo root with the given segments, or returns null if the root is unknown.</summary>
    public static string? InRepo(params string[] segments)
    {
        string? root = FindRoot();
        return root == null ? null : Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
