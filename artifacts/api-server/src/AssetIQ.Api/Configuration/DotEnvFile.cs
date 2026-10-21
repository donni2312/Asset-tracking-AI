using Microsoft.Extensions.Configuration.Memory;

namespace AssetIQ.Api.Configuration;

/// <summary>
/// Local development reads the repository's .env file (git-ignored, copied from .env.example).
/// Real environment variables win over the file, as with Node's <c>--env-file</c>; deployments
/// (Replit secrets, Azure App Service settings) simply have no file.
/// </summary>
public static class DotEnvFile
{
    public static void AddTo(ConfigurationManager configuration, string startDirectory)
    {
        var path = Find(startDirectory);
        if (path is null) return;
        var values = Parse(File.ReadAllLines(path));
        // Index 0 = lowest precedence: environment variables and command-line arguments override the file.
        ((IConfigurationBuilder)configuration).Sources.Insert(0, new MemoryConfigurationSource { InitialData = values! });
    }

    /// <summary>The .env next to pnpm-workspace.yaml, searching upwards from <paramref name="startDirectory"/>.</summary>
    public static string? Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "pnpm-workspace.yaml")))
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                return File.Exists(candidate) ? candidate : null;
            }
        }
        return null;
    }

    /// <summary>KEY=value lines; blank lines and # comments are skipped; surrounding quotes are removed.</summary>
    public static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value[1..^1];
            }
            else
            {
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0) value = value[..comment].TrimEnd();
            }
            values[key] = value;
        }
        return values;
    }
}
