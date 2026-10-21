using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Npgsql;

namespace AssetIQ.Api.Tests.Infrastructure;

/// <summary>
/// The PostgreSQL server the integration tests run against. It never touches the development database:
/// <list type="bullet">
/// <item>ASSETIQ_TEST_DATABASE_URL set (e.g. CI) → that server; the tests create and drop their own database on it.</item>
/// <item>otherwise → a throwaway PostgreSQL 16 cluster in a temp folder, started from the binaries that
/// <c>pnpm install</c> placed in node_modules (the embedded-postgres package behind <c>pnpm db:local</c>).</item>
/// </list>
/// </summary>
public sealed class TestPostgres : IAsyncDisposable
{
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private readonly string? _dataDirectory;
    private readonly string? _pgCtl;
    private int _stopped;

    private TestPostgres(string adminConnectionString, string? dataDirectory, string? pgCtl)
    {
        AdminConnectionString = adminConnectionString;
        _dataDirectory = dataDirectory;
        _pgCtl = pgCtl;
        // A server left running would outlive the test run; stop it even if disposal never happens.
        if (pgCtl is not null) AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    /// <summary>Connection to the server's maintenance database (used to create the test database).</summary>
    public string AdminConnectionString { get; }

    public static async Task<TestPostgres> StartAsync()
    {
        var external = Environment.GetEnvironmentVariable("ASSETIQ_TEST_DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(external))
        {
            var builder = new NpgsqlConnectionStringBuilder(Api.Configuration.DatabaseUrl.ToConnectionString(external)) { Database = "postgres", Pooling = false };
            return new TestPostgres(builder.ConnectionString, null, null);
        }

        var bin = FindEmbeddedPostgresBin()
            ?? throw new InvalidOperationException(
                "No PostgreSQL for the integration tests: run `pnpm install` (it provides PostgreSQL 16 in node_modules) " +
                "or set ASSETIQ_TEST_DATABASE_URL to a server where the tests may create a database.");
        var exe = IsWindows ? ".exe" : "";
        var initdb = Path.Combine(bin, "initdb" + exe);
        var pgCtl = Path.Combine(bin, "pg_ctl" + exe);

        var dataDirectory = Path.Combine(Path.GetTempPath(), $"assetiq-test-pg-{Guid.NewGuid():N}");
        await RunCapturedAsync(initdb, ["-D", dataDirectory, "-U", "postgres", "-A", "trust", "-E", "UTF8", "--locale=C"]);

        var port = FreePort();
        var log = Path.Combine(dataDirectory, "server.log");
        var options = $"-p {port} -c listen_addresses=127.0.0.1 -c timezone=UTC -c fsync=off -c synchronous_commit=off -c full_page_writes=off";
        var exitCode = await StartDetachedAsync(pgCtl, ["start", "-D", dataDirectory, "-l", log, "-o", options, "-w", "-t", "60"]);
        if (exitCode != 0)
        {
            var serverLog = File.Exists(log) ? await File.ReadAllTextAsync(log) : "(no server log)";
            throw new InvalidOperationException($"pg_ctl start exited with code {exitCode}:\n{serverLog}");
        }

        var admin = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = port,
            Username = "postgres",
            Database = "postgres",
            Pooling = false,
        };
        return new TestPostgres(admin.ConnectionString, dataDirectory, pgCtl);
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    /// <summary>Stop the throwaway server and delete its folder (once; safe to call from any thread).</summary>
    private void Stop()
    {
        if (_pgCtl is null || _dataDirectory is null || Interlocked.Exchange(ref _stopped, 1) == 1) return;
        try
        {
            RunCapturedAsync(_pgCtl, ["stop", "-D", _dataDirectory, "-m", "immediate", "-w", "-t", "30"]).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            // Already stopped.
        }
        for (var attempt = 0; attempt < 10 && Directory.Exists(_dataDirectory); attempt++)
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(300); // Windows may hold the log file open for a moment after the server stops
            }
        }
    }

    /// <summary>node_modules/.pnpm/@embedded-postgres+&lt;platform&gt;-&lt;arch&gt;@*/…/native/bin of the repository.</summary>
    private static string? FindEmbeddedPostgresBin()
    {
        var platform = IsWindows ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        var package = $"{platform}-{arch}";

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "pnpm-workspace.yaml"))) continue;
            var store = Path.Combine(dir.FullName, "node_modules", ".pnpm");
            if (!Directory.Exists(store)) return null;
            return Directory.EnumerateDirectories(store, $"@embedded-postgres+{package}@*")
                .Select(d => Path.Combine(d, "node_modules", "@embedded-postgres", package, "native", "bin"))
                .Where(Directory.Exists)
                .OrderDescending(StringComparer.Ordinal)
                .FirstOrDefault();
        }
        return null;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// Runs <c>pg_ctl start</c>, which leaves the server running. On Windows a child process inherits
    /// every inheritable handle of its parent — including the test host's output pipe, which
    /// <c>dotnet test</c> reads until it closes — so the server is launched through ShellExecute,
    /// which inherits nothing. Elsewhere pg_ctl's <c>-l</c> redirection already detaches the server.
    /// </summary>
    private static async Task<int> StartDetachedAsync(string fileName, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo;
        if (IsWindows)
        {
            startInfo = new ProcessStartInfo(fileName, string.Join(' ', arguments.Select(QuoteWindowsArgument)))
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
        }
        else
        {
            startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>Runs a PostgreSQL tool to completion; a non-zero exit throws with its output.</summary>
    private static async Task RunCapturedAsync(string fileName, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        // Settings for another installation must not leak into the throwaway cluster.
        startInfo.Environment.Remove("PGDATA");
        startInfo.Environment.Remove("PGPORT");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(fileName)} exited with code {process.ExitCode}:\n{await output}\n{await error}");
        }
    }

    /// <summary>The arguments contain no quotes, so wrapping values with spaces in quotes is enough.</summary>
    private static string QuoteWindowsArgument(string argument) =>
        argument.Length > 0 && !argument.Contains(' ') && !argument.Contains('\t') ? argument : $"\"{argument}\"";
}
