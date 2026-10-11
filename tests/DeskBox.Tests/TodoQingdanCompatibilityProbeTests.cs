using System.Diagnostics;
using System.Text.Json;

namespace DeskBox.Tests;

public sealed class TodoQingdanCompatibilityProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "deskbox-todo-qingdan-probe",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Probe_UsesReadOnlyMetadataAndKeepsEveryTaskCapabilityDisabled()
    {
        string installation = Path.Combine(_root, "app");
        string dataRoot = Path.Combine(_root, "data");
        Directory.CreateDirectory(installation);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(installation, "Todo清单.exe"), "fixture");
        CreateFixture(Path.Combine(dataRoot, "task.db"));
        CreateFixture(Path.Combine(dataRoot, "todos.db"));

        ProcessResult result = RunPowerShell(
            "-InstallationPath", installation,
            "-DataRoot", dataRoot);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("PRIVATE_SENTINEL_TITLE", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE_SENTINEL_TOKEN", result.StandardOutput, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        JsonElement root = document.RootElement;
        Assert.False(root.GetProperty("privacy").GetProperty("rowQueriesPerformed").GetBoolean());
        Assert.False(root.GetProperty("privacy").GetProperty("taskContentRead").GetBoolean());
        Assert.Equal("blocked", root.GetProperty("compatibility").GetProperty("writePolicy").GetString());
        Assert.Equal("unsupported", root.GetProperty("compatibility").GetProperty("adapter").GetString());
        Assert.StartsWith("sha256:", root.GetProperty("compatibility").GetProperty("schemaFingerprint").GetString());

        JsonElement capabilities = root.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("discoverInstallation").GetBoolean());
        Assert.True(capabilities.GetProperty("detectVersionSchema").GetBoolean());
        foreach (JsonProperty capability in capabilities.EnumerateObject())
        {
            if (capability.Name is "discoverInstallation" or "detectVersionSchema") continue;
            Assert.False(capability.Value.GetBoolean(), capability.Name);
        }
    }

    [Fact]
    public void Probe_DoesNotMutateFixtureAndProducesStableFingerprint()
    {
        string installation = Path.Combine(_root, "stable-app");
        string dataRoot = Path.Combine(_root, "stable-data");
        Directory.CreateDirectory(installation);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(installation, "Todo清单.exe"), "fixture");
        string taskDb = Path.Combine(dataRoot, "task.db");
        string todosDb = Path.Combine(dataRoot, "todos.db");
        CreateFixture(taskDb);
        CreateFixture(todosDb);
        byte[] taskBefore = File.ReadAllBytes(taskDb);
        byte[] todosBefore = File.ReadAllBytes(todosDb);

        ProcessResult first = RunPowerShell("-InstallationPath", installation, "-DataRoot", dataRoot);
        ProcessResult second = RunPowerShell("-InstallationPath", installation, "-DataRoot", dataRoot);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        using JsonDocument firstDocument = JsonDocument.Parse(first.StandardOutput);
        using JsonDocument secondDocument = JsonDocument.Parse(second.StandardOutput);
        Assert.Equal(
            firstDocument.RootElement.GetProperty("compatibility").GetProperty("schemaFingerprint").GetString(),
            secondDocument.RootElement.GetProperty("compatibility").GetProperty("schemaFingerprint").GetString());
        Assert.Equal(taskBefore, File.ReadAllBytes(taskDb));
        Assert.Equal(todosBefore, File.ReadAllBytes(todosDb));
        Assert.False(File.Exists(taskDb + "-wal"));
        Assert.False(File.Exists(todosDb + "-wal"));
    }

    [Fact]
    public void Probe_TreatsUnknownDatabaseFormatAsUnavailableAndNeverThrowsContent()
    {
        string installation = Path.Combine(_root, "unknown-app");
        string dataRoot = Path.Combine(_root, "unknown-data");
        Directory.CreateDirectory(installation);
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(installation, "Todo清单.exe"), "fixture");
        File.WriteAllText(Path.Combine(dataRoot, "task.db"), "PRIVATE_SENTINEL_TOKEN");
        File.WriteAllText(Path.Combine(dataRoot, "todos.db"), "PRIVATE_SENTINEL_TITLE");

        ProcessResult result = RunPowerShell("-InstallationPath", installation, "-DataRoot", dataRoot);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("PRIVATE_SENTINEL", result.StandardOutput, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(result.StandardOutput);
        foreach (JsonElement database in document.RootElement.GetProperty("databases").EnumerateArray())
        {
            Assert.Equal("encrypted-or-unrecognized", database.GetProperty("format").GetString());
            Assert.False(database.GetProperty("structureAccessible").GetBoolean());
            Assert.Equal("not-plain-sqlite", database.GetProperty("errorCode").GetString());
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void CreateFixture(string databasePath)
    {
        const string source = """
            const { DatabaseSync } = require('node:sqlite');
            const db = new DatabaseSync(process.argv[1]);
            db.exec(`
              PRAGMA user_version=31402;
              CREATE TABLE lists (id TEXT PRIMARY KEY, name TEXT NOT NULL);
              CREATE TABLE tasks (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                token TEXT,
                list_id TEXT,
                completed INTEGER NOT NULL DEFAULT 0
              );
              CREATE INDEX ix_tasks_list ON tasks(list_id);
              INSERT INTO tasks(id,title,token) VALUES
                ('1','PRIVATE_SENTINEL_TITLE','PRIVATE_SENTINEL_TOKEN');
            `);
            db.close();
            """;
        ProcessResult result = RunProcess("node", ["-e", source, databasePath]);
        Assert.Equal(0, result.ExitCode);
    }

    private static ProcessResult RunPowerShell(params string[] arguments)
    {
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        List<string> allArguments =
        [
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            TestPaths.FromRepository("scripts/Test-TodoQingdanCompatibility.ps1")
        ];
        allArguments.AddRange(arguments);
        return RunProcess(powershell, allArguments);
    }

    private static ProcessResult RunProcess(string fileName, IEnumerable<string> arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start {fileName}.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, standardOutput, standardError);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
