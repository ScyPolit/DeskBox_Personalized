using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using DeskBox.TodoQingdan.Protocol;
using Microsoft.Data.Sqlite;

namespace DeskBox.Tests;

public sealed class TodoQingdanBridgeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "deskbox-todo-qingdan-bridge", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Known3142Fixture_HandshakesReadsAndRejectsMutationWithoutLoggingContent()
    {
        Directory.CreateDirectory(_root);
        CreateFixture(Path.Combine(_root, "todos.db"), knownShape: true);
        await using BridgeSession session = await StartAsync("3.14.2.0");

        BridgeResponse handshake = await session.SendAsync(BridgeProtocol.Handshake);
        BridgeResponse read = await session.SendAsync(BridgeProtocol.ReadTasks);
        BridgeResponse mutation = await session.SendAsync("setCompleted");

        Assert.True(handshake.Succeeded);
        Assert.Equal("todo-qingdan-3.14.2-readonly", handshake.Handshake?.AdapterId);
        Assert.Equal(["ReadTasks"], handshake.Handshake?.Capabilities);
        Assert.True(read.Succeeded);
        BridgeTask task = Assert.Single(read.Snapshot!.Tasks);
        Assert.Equal("fixture-task", task.Id);
        Assert.Equal("PRIVATE_SENTINEL_TITLE", task.Title);
        Assert.False(mutation.Succeeded);
        Assert.Equal("read-only", mutation.ErrorCode);
        Assert.Equal(string.Empty, await session.ReadStandardOutputAsync());
        Assert.DoesNotContain("PRIVATE_SENTINEL", await session.ReadStandardErrorAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSchemaAndProtocolMismatchRemainUnavailableAndReadOnly()
    {
        Directory.CreateDirectory(_root);
        CreateFixture(Path.Combine(_root, "todos.db"), knownShape: false);
        await using BridgeSession session = await StartAsync("3.14.3.0");

        BridgeResponse handshake = await session.SendAsync(BridgeProtocol.Handshake);
        BridgeResponse read = await session.SendAsync(BridgeProtocol.ReadTasks);
        BridgeResponse mismatch = await session.SendAsync(BridgeProtocol.Handshake, protocolVersion: 999);

        Assert.True(handshake.Succeeded);
        Assert.Equal("unsupported", handshake.Handshake?.AdapterId);
        Assert.Empty(handshake.Handshake?.Capabilities ?? []);
        Assert.False(read.Succeeded);
        Assert.Equal("schema-mismatch", read.ErrorCode);
        Assert.False(mismatch.Succeeded);
        Assert.Equal("protocol-mismatch", mismatch.ErrorCode);
    }

    [Fact]
    public async Task ExpiredRequestIsRejectedAndBridgeExitsAfterClientDisconnect()
    {
        Directory.CreateDirectory(_root);
        CreateFixture(Path.Combine(_root, "todos.db"), knownShape: true);
        BridgeSession session = await StartAsync("3.14.2.0");
        BridgeResponse expired = await session.SendAsync(
            BridgeProtocol.Handshake,
            deadline: DateTimeOffset.UtcNow.AddSeconds(-1));
        int processId = session.ProcessId;
        await session.DisposeAsync();

        Assert.False(expired.Succeeded);
        Assert.Equal("deadline-exceeded", expired.ErrorCode);
        await Task.Delay(250);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(processId));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (int attempt = 0; attempt < 20 && Directory.Exists(_root); attempt++)
        {
            try { Directory.Delete(_root, recursive: true); }
            catch (IOException) when (attempt < 19) { Thread.Sleep(50); }
            catch (IOException) { }
        }
    }

    private async Task<BridgeSession> StartAsync(string productVersion)
    {
        string bridge = ResolveBridgePath(AppContext.BaseDirectory);
        string pipeName = "DeskBox.Tests.TodoQingdan." + Guid.NewGuid().ToString("N");
        var startInfo = new ProcessStartInfo
        {
            FileName = bridge,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--data-root");
        startInfo.ArgumentList.Add(_root);
        startInfo.ArgumentList.Add("--product-version");
        startInfo.ArgumentList.Add(productVersion);
        Process process = Process.Start(startInfo)!;
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pipe.ConnectAsync(timeout.Token);
        return new BridgeSession(process, pipe);
    }

    [Theory]
    [InlineData(@"C:\repo\tests\DeskBox.Tests\bin\x64\Release\net10.0-windows10.0.22621.0\win-x64", @"src\DeskBox.TodoQingdanBridge\bin\x64\Release\net10.0\win-x64\DeskBox.TodoQingdanBridge.exe")]
    [InlineData(@"C:\repo\tests\DeskBox.Tests\bin\ARM64\Debug\net10.0-windows10.0.22621.0\win-arm64", @"src\DeskBox.TodoQingdanBridge\bin\ARM64\Debug\net10.0\win-arm64\DeskBox.TodoQingdanBridge.exe")]
    public void ResolveBridgeRelativePathMatchesCurrentTestBuild(
        string testBaseDirectory,
        string expected)
    {
        Assert.Equal(expected, ResolveBridgeRelativePath(testBaseDirectory));
    }

    private static string ResolveBridgePath(string testBaseDirectory)
    {
        string relativePath = ResolveBridgeRelativePath(testBaseDirectory);
        string bridge = TestPaths.FromRepository(relativePath);
        Assert.True(File.Exists(bridge), $"Todo Qingdan bridge was not built at '{bridge}'.");
        return bridge;
    }

    private static string ResolveBridgeRelativePath(string testBaseDirectory)
    {
        string[] segments = Path.GetFullPath(testBaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        int binIndex = Array.FindLastIndex(
            segments,
            segment => string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase));
        if (binIndex < 0 || segments.Length <= binIndex + 3)
        {
            throw new InvalidOperationException(
                $"Cannot derive the Todo Qingdan bridge output from test directory '{testBaseDirectory}'.");
        }

        string platform = segments[binIndex + 1];
        string configuration = segments[binIndex + 2];
        string? runtimeIdentifier = segments
            .Skip(binIndex + 4)
            .FirstOrDefault(segment => segment.StartsWith("win-", StringComparison.OrdinalIgnoreCase));

        return Path.Combine(
            "src",
            "DeskBox.TodoQingdanBridge",
            "bin",
            platform,
            configuration,
            "net10.0",
            runtimeIdentifier ?? string.Empty,
            "DeskBox.TodoQingdanBridge.exe");
    }

    private static void CreateFixture(string path, bool knownShape)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = knownShape ? """
            CREATE TABLE todos (
              task_id TEXT PRIMARY KEY, task_content TEXT, task_describe TEXT,
              complete INTEGER NOT NULL, create_time INTEGER NOT NULL,
              day_start INTEGER NOT NULL DEFAULT 0, deleted INTEGER NOT NULL,
              reminder_time INTEGER, snow_add INTEGER NOT NULL DEFAULT 0,
              snow_assess INTEGER, standby_int1 INTEGER NOT NULL DEFAULT 0,
              standby_str1 TEXT, standby_str2 TEXT, standby_str3 TEXT, standby_str4 TEXT,
              status TEXT NOT NULL DEFAULT '', sync_time INTEGER NOT NULL,
              task_sort REAL NOT NULL, todo_time INTEGER NOT NULL,
              update_time INTEGER NOT NULL, user_id INTEGER, version INTEGER NOT NULL);
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
            CREATE INDEX idx_todos_category ON todos(standby_str1);
            CREATE INDEX idx_todos_complete ON todos(complete);
            CREATE INDEX idx_todos_day ON todos(day_start);
            CREATE INDEX idx_todos_reminder ON todos(reminder_time);
            CREATE INDEX idx_todos_repeat ON todos(standby_str2);
            CREATE INDEX idx_todos_status ON todos(status);
            INSERT INTO todos(task_id,task_content,task_describe,complete,create_time,deleted,sync_time,task_sort,todo_time,update_time,version)
            VALUES('fixture-task','PRIVATE_SENTINEL_TITLE','PRIVATE_SENTINEL_NOTE',0,1760230800,0,1760230800,1,1760234400,1760230800,1);
            """ : "CREATE TABLE unknown_data (id TEXT PRIMARY KEY);";
        command.ExecuteNonQuery();
    }

    private sealed class BridgeSession(Process process, NamedPipeClientStream pipe) : IAsyncDisposable
    {
        private readonly StreamReader _reader = new(pipe, new UTF8Encoding(false), false, 4096, true);
        private readonly StreamWriter _writer = new(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        public int ProcessId => process.Id;

        public async Task<BridgeResponse> SendAsync(
            string operation,
            int protocolVersion = BridgeProtocol.Version,
            DateTimeOffset? deadline = null)
        {
            string id = Guid.NewGuid().ToString("N");
            var request = new BridgeRequest(
                protocolVersion, id, operation,
                (deadline ?? DateTimeOffset.UtcNow.AddSeconds(3)).ToUnixTimeMilliseconds());
            await _writer.WriteLineAsync(JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeRequest));
            string line = (await _reader.ReadLineAsync())!;
            return JsonSerializer.Deserialize(line, BridgeJsonContext.Default.BridgeResponse)!;
        }

        public async Task<string> ReadStandardOutputAsync()
        {
            if (!process.HasExited) return string.Empty;
            return await process.StandardOutput.ReadToEndAsync();
        }

        public async Task<string> ReadStandardErrorAsync()
        {
            if (!process.HasExited) return string.Empty;
            return await process.StandardError.ReadToEndAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _writer.Dispose();
            _reader.Dispose();
            pipe.Dispose();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            if (!process.HasExited) await process.WaitForExitAsync();
            process.Dispose();
        }
    }
}
