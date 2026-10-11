using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DeskBox.TodoQingdan.Protocol;
using Microsoft.Data.Sqlite;

return await BridgeProgram.RunAsync(args);

internal static class BridgeProgram
{
    private const string AdapterId = "todo-qingdan-3.14.2-readonly";
    private static readonly string[] RequiredColumns =
    [
        "complete", "create_time", "deleted", "sync_time", "task_content",
        "task_describe", "task_id", "task_sort", "todo_time", "update_time", "version"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        string? pipeName = GetArgument(args, "--pipe");
        string? dataRoot = GetArgument(args, "--data-root");
        string version = GetArgument(args, "--product-version") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(pipeName) || string.IsNullOrWhiteSpace(dataRoot)) return 2;

        using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await pipe.WaitForConnectionAsync(startup.Token);
        using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

        string? line;
        while ((line = await reader.ReadLineAsync()) is not null)
        {
            BridgeResponse response;
            try
            {
                BridgeRequest request = JsonSerializer.Deserialize(line, BridgeJsonContext.Default.BridgeRequest)
                    ?? throw new InvalidDataException();
                response = Handle(request, dataRoot, version);
            }
            catch
            {
                response = Error(string.Empty, "invalid-request", "The bridge request was invalid.");
            }
            await writer.WriteLineAsync(JsonSerializer.Serialize(response, BridgeJsonContext.Default.BridgeResponse));
        }
        return 0;
    }

    private static BridgeResponse Handle(BridgeRequest request, string dataRoot, string version)
    {
        if (request.ProtocolVersion != BridgeProtocol.Version)
            return Error(request.RequestId, "protocol-mismatch", "Unsupported protocol version.");
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > request.DeadlineUnixMilliseconds)
            return Error(request.RequestId, "deadline-exceeded", "The request deadline elapsed.");

        Compatibility compatibility = Inspect(Path.Combine(dataRoot, "todos.db"));
        bool supported = version.StartsWith("3.14.2", StringComparison.Ordinal) && compatibility.RequiredShape;
        BridgeHandshake handshake = new(
            version,
            supported ? AdapterId : "unsupported",
            compatibility.Fingerprint,
            compatibility.Readable ? "read-only" : "unavailable",
            supported ? ["ReadTasks"] : [],
            supported
                ? ["writes-disabled", "lists-unverified", "attachments-unverified"]
                : ["unknown-version-or-schema", "writes-disabled"]);

        if (request.Operation == BridgeProtocol.Handshake)
            return Success(request.RequestId, handshake, null);
        if (request.Operation != BridgeProtocol.ReadTasks)
            return Error(request.RequestId, "read-only", "This bridge does not allow mutations.", handshake);
        if (!supported)
            return Error(request.RequestId, "schema-mismatch", "The TodoQingdan schema is not supported.", handshake);

        try
        {
            return Success(request.RequestId, handshake, ReadTasks(Path.Combine(dataRoot, "todos.db")));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return Error(request.RequestId, "busy", "TodoQingdan data is busy.", handshake);
        }
        catch
        {
            return Error(request.RequestId, "operation-failed", "TodoQingdan tasks could not be read.", handshake);
        }
    }

    private static Compatibility Inspect(string path)
    {
        if (!File.Exists(path)) return new(false, false, null);
        try
        {
            using SqliteConnection connection = Open(path);
            string[] objects = QueryStrings(connection,
                "SELECT type || ':' || name FROM sqlite_schema WHERE type IN ('table','view','index','trigger') AND name NOT LIKE 'sqlite_%' ORDER BY type,name");
            string[] columns = QueryStrings(connection,
                "SELECT name || ':' || type FROM pragma_table_xinfo('todos') ORDER BY name");
            string[] columnNames = columns.Select(value => value.Split(':', 2)[0]).ToArray();
            string[] indexes = QueryStrings(connection,
                "SELECT name FROM pragma_index_list('todos') ORDER BY name");
            long userVersion = Convert.ToInt64(Scalar(connection, "PRAGMA user_version"));
            long applicationId = Convert.ToInt64(Scalar(connection, "PRAGMA application_id"));
            string canonical = string.Join('\n',
                [userVersion.ToString(), applicationId.ToString(), .. objects, .. columns, .. indexes]);
            string fingerprint = "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
            bool shape = objects.Contains("table:todos", StringComparer.Ordinal) &&
                         RequiredColumns.All(required => columnNames.Contains(required, StringComparer.Ordinal));
            return new(true, shape, fingerprint);
        }
        catch
        {
            return new(false, false, null);
        }
    }

    private static BridgeTaskSnapshot ReadTasks(string path)
    {
        using SqliteConnection connection = Open(path);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT task_id, task_content, task_describe, complete, todo_time,
                   create_time, update_time, task_sort, sync_time
            FROM todos WHERE deleted = 0 ORDER BY task_sort, update_time DESC
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        var tasks = new List<BridgeTask>();
        long revision = 0;
        while (reader.Read())
        {
            string id = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(id)) continue;
            long updated = reader.GetInt64(6);
            revision = Math.Max(revision, Math.Max(updated, reader.GetInt64(8)));
            tasks.Add(new BridgeTask(
                id,
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt64(3) != 0,
                NormalizeUnix(reader.GetInt64(4)),
                NormalizeUnix(reader.GetInt64(5)) ?? 0,
                NormalizeUnix(updated) ?? 0,
                reader.GetDouble(7)));
        }
        return new BridgeTaskSnapshot(
            revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), tasks);
    }

    private static long? NormalizeUnix(long value) => value <= 0
        ? null
        : value > 10_000_000_000 ? value : checked(value * 1000);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA query_only=ON; PRAGMA busy_timeout=1500;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() ?? 0;
    }

    private static string[] QueryStrings(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values.ToArray();
    }

    private static BridgeResponse Success(string id, BridgeHandshake handshake, BridgeTaskSnapshot? snapshot) =>
        new(BridgeProtocol.Version, id, true, null, null, handshake, snapshot);

    private static BridgeResponse Error(string id, string code, string message, BridgeHandshake? handshake = null) =>
        new(BridgeProtocol.Version, id, false, code, message, handshake, null);

    private static string? GetArgument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private sealed record Compatibility(bool Readable, bool RequiredShape, string? Fingerprint);
}
