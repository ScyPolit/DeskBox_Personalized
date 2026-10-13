using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DeskBox.Models;
using DeskBox.Contracts;
using DeskBox.TodoQingdan.Protocol;
using Microsoft.Win32;

namespace DeskBox.Services;

public enum TodoDataSource
{
    DeskBoxLocal,
    TodoQingdan
}

public sealed class TodoQingdanIntegrationSettings
{
    public int Version { get; set; } = 1;
    public TodoDataSource DataSource { get; set; }
    public bool TodoDebugMode { get; set; }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(TodoQingdanIntegrationSettings))]
[JsonSerializable(typeof(TodoWidgetData))]
internal sealed partial class TodoQingdanIntegrationJsonContext : JsonSerializerContext
{
}

public sealed class TodoQingdanIntegrationStore
{
    private readonly string _path;

    public TodoQingdanIntegrationStore(string? dataDirectory = null)
    {
        IsDefaultLocation = dataDirectory is null;
        _path = Path.Combine(
            dataDirectory ?? DeskBoxDataPathService.Current.DataDirectory,
            "todo-qingdan-integration.json");
    }

    public bool Exists => File.Exists(_path);
    public bool IsDefaultLocation { get; }

    public Task<TodoQingdanIntegrationSettings> LoadAsync() => ResilientJsonStore.LoadAsync(
        _path,
        json => JsonSerializer.Deserialize(json, TodoQingdanIntegrationJsonContext.Default.TodoQingdanIntegrationSettings)
            ?? new TodoQingdanIntegrationSettings(),
        () => new TodoQingdanIntegrationSettings(),
        nameof(TodoQingdanIntegrationStore));

    public Task SaveAsync(TodoQingdanIntegrationSettings settings) => ResilientJsonStore.SaveAsync(
        _path,
        JsonSerializer.Serialize(settings, TodoQingdanIntegrationJsonContext.Default.TodoQingdanIntegrationSettings));
}

public sealed record TodoQingdanConnectionStatus(
    string State,
    string? ProductVersion,
    string? AdapterId,
    string? SchemaFingerprint,
    IReadOnlyList<string> Capabilities,
    string? ErrorCode,
    string DataSourcePath,
    bool DatabaseExists,
    DateTimeOffset? LastSyncTime,
    int TaskCount,
    bool UsedOfflineCache,
    string? ErrorMessage);

public sealed class TodoRepositoryRouter : ITodoRepositoryFactory, ITodoIntegrationSettings, IDisposable
{
    private static readonly TimeSpan ExternalRefreshInterval = TimeSpan.FromSeconds(10);
    private readonly ITodoRepositoryFactory _local;
    private readonly TodoQingdanRepositoryFactory _external;
    private readonly TodoQingdanIntegrationStore _store;
    private TodoQingdanIntegrationSettings _settings = new();
    private readonly CancellationTokenSource _monitorLifetime = new();
    private Task? _monitorTask;
    private string? _observedExternalRevision;
    private SynchronizationContext? _synchronizationContext;

    public TodoRepositoryRouter(
        ITodoRepositoryFactory? local = null,
        TodoQingdanRepositoryFactory? external = null,
        TodoQingdanIntegrationStore? store = null)
    {
        _local = local ?? DeskBoxTodoRepositoryFactory.Default;
        _external = external ?? new TodoQingdanRepositoryFactory();
        _store = store ?? new TodoQingdanIntegrationStore();
    }

    public bool UsesExternalSource => _settings.DataSource == TodoDataSource.TodoQingdan;
    public TodoDataSource DataSource => _settings.DataSource;
    string ITodoIntegrationSettings.DataSource => _settings.DataSource.ToString();
    public string ConnectionState => Status.State;
    public string DetectedVersion => Status.ProductVersion ?? "-";
    public bool TodoDebugMode => _settings.TodoDebugMode;
    public string DataSourcePath => Status.DataSourcePath;
    public bool DatabaseExists => Status.DatabaseExists;
    public DateTimeOffset? LastSyncTime => Status.LastSyncTime;
    public int TaskCount => Status.TaskCount;
    public string ErrorLog => Status.ErrorMessage ?? Status.ErrorCode ?? string.Empty;
    public TodoQingdanConnectionStatus Status => _external.Status;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        _synchronizationContext = SynchronizationContext.Current;
        bool hadPersistedSettings = _store.Exists;
        bool externalSnapshotLoaded = false;
        _settings = await _store.LoadAsync();
        _external.LogStartupDiagnostics();

        // Personalized builds historically exposed TodoQingdan in Settings but
        // defaulted silently to DeskBoxLocal. Adopt a verified local database
        // only on the first run; an explicit existing preference always wins.
        if (!hadPersistedSettings && _store.IsDefaultLocation)
        {
            try
            {
                BridgeResponse response = await _external.ReadTasksAsync(_monitorLifetime.Token);
                if (response.Succeeded && response.Snapshot is not null)
                {
                    externalSnapshotLoaded = true;
                    _settings.DataSource = TodoDataSource.TodoQingdan;
                    await _store.SaveAsync(_settings);
                    LogDebug($"First-run compatible source adopted; tasks={response.Snapshot.Tasks.Count}.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _external.RecordFailure(ex);
            }
        }

        if (UsesExternalSource && !externalSnapshotLoaded)
        {
            try
            {
                await _external.ReadTasksAsync(_monitorLifetime.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _external.RecordFailure(ex);
            }
        }

        _observedExternalRevision = _external.GetStorageRevision();
        _monitorTask = MonitorExternalChangesAsync(_monitorLifetime.Token);
        RaiseChanged();
    }

    public async Task SetDataSourceAsync(TodoDataSource source)
    {
        if (_settings.DataSource == source) return;
        _settings.DataSource = source;
        await _store.SaveAsync(_settings);
        if (source == TodoDataSource.TodoQingdan)
        {
            try
            {
                await _external.ReadTasksAsync(_monitorLifetime.Token);
                _observedExternalRevision = _external.GetStorageRevision();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _external.RecordFailure(ex);
            }
        }
        RaiseChanged();
    }

    public Task SetDataSourceAsync(string source) => SetDataSourceAsync(
        Enum.TryParse(source, out TodoDataSource parsed)
            ? parsed
            : TodoDataSource.DeskBoxLocal);

    public async Task SetTodoDebugModeAsync(bool enabled)
    {
        if (_settings.TodoDebugMode == enabled) return;
        _settings.TodoDebugMode = enabled;
        await _store.SaveAsync(_settings);
        RaiseChanged();
    }

    public async Task RefreshCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _external.ReadTasksAsync(cancellationToken);
            _observedExternalRevision = _external.GetStorageRevision();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _external.RecordFailure(ex);
        }
        RaiseChanged();
    }

    public ITodoRepository ForWidget(string widgetId) => new RoutedTodoRepository(this, widgetId);

    public Task DeleteForWidgetAsync(string widgetId) => UsesExternalSource
        ? Task.CompletedTask
        : _local.DeleteForWidgetAsync(widgetId);

    public void Dispose()
    {
        _monitorLifetime.Cancel();
        _monitorLifetime.Dispose();
    }

    private async Task MonitorExternalChangesAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(ExternalRefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!UsesExternalSource) continue;
                string revision = _external.GetStorageRevision();
                if (string.Equals(revision, _observedExternalRevision, StringComparison.Ordinal)) continue;
                _observedExternalRevision = revision;
                LogDebug($"External storage changed; revision={revision}.");
                RaiseChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void LogDebug(string message) => App.Log($"[TODOLIST DEBUG] {message}");

    private void RaiseChanged()
    {
        SynchronizationContext? context = _synchronizationContext;
        if (context is not null && !ReferenceEquals(context, SynchronizationContext.Current))
        {
            context.Post(_ => Changed?.Invoke(), null);
            return;
        }

        Changed?.Invoke();
    }

    private sealed class RoutedTodoRepository(TodoRepositoryRouter router, string widgetId) : ITodoRepository
    {
        private ITodoRepository Current => router.UsesExternalSource
            ? router._external.ForWidget(widgetId)
            : router._local.ForWidget(widgetId);

        public bool CanWrite => Current.CanWrite;
        public string AttachmentDirectory => Current.AttachmentDirectory;
        public string? VersionToken => Current.VersionToken;
        public Task<TodoWidgetData> LoadAsync() => Current.LoadAsync();
        public Task SaveAsync(TodoWidgetData data) => Current.SaveAsync(data);
        public Task ClearAsync() => Current.ClearAsync();
    }
}

public sealed class TodoQingdanRepositoryFactory : ITodoRepositoryFactory
{
    private readonly object _gate = new();
    private readonly Func<(string? Executable, string DataRoot, string ProductVersion)> _discover;
    private readonly string _cacheDirectory;
    private readonly string _bridgeExecutable;
    private TodoQingdanConnectionStatus _status = CreateInitialStatus();
    public bool UsesExternalSource => true;
    public TodoQingdanConnectionStatus Status { get { lock (_gate) return _status; } }

    public TodoQingdanRepositoryFactory()
        : this(Discover, null)
    {
    }

    internal TodoQingdanRepositoryFactory(
        Func<(string? Executable, string DataRoot, string ProductVersion)> discover,
        string? cacheDirectory = null,
        string? bridgeExecutable = null)
    {
        _discover = discover ?? throw new ArgumentNullException(nameof(discover));
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory,
            "todo-qingdan-cache");
        _bridgeExecutable = bridgeExecutable ?? Path.Combine(
            AppContext.BaseDirectory,
            "TodoQingdanBridge",
            "DeskBox.TodoQingdanBridge.exe");
    }

    public ITodoRepository ForWidget(string widgetId) =>
        new TodoQingdanRepository(this, widgetId, _cacheDirectory);
    public Task DeleteForWidgetAsync(string widgetId) => Task.CompletedTask;

    public async Task<TodoQingdanConnectionStatus> RefreshCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        BridgeResponse response = await SendAsync(BridgeProtocol.Handshake, cancellationToken);
        UpdateStatus(response);
        return Status;
    }

    internal async Task<BridgeResponse> ReadTasksAsync(CancellationToken cancellationToken = default)
    {
        BridgeResponse response = await SendAsync(BridgeProtocol.ReadTasks, cancellationToken);
        UpdateStatus(response);
        return response;
    }

    internal string GetStorageRevision()
    {
        string databasePath = GetDatabasePath();
        string walPath = databasePath + "-wal";
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{GetFileRevision(databasePath)}:{GetFileRevision(walPath)}");
    }

    internal void LogStartupDiagnostics()
    {
        string path = GetDatabasePath();
        LogDebug($"检测路径: {path}");
        LogDebug($"数据库是否存在: {File.Exists(path).ToString().ToLowerInvariant()}");
    }

    internal void RecordOfflineCache(int taskCount, Exception exception)
    {
        TodoQingdanConnectionStatus current = Status;
        UpdateStatus(current with
        {
            State = "offline-cache",
            TaskCount = taskCount,
            UsedOfflineCache = true,
            ErrorCode = GetErrorCode(exception),
            ErrorMessage = exception.Message
        });
        LogDebug($"读取失败，使用离线缓存; tasks={taskCount}; error={GetErrorCode(exception)}.");
    }

    internal void RecordFailure(Exception exception)
    {
        TodoQingdanConnectionStatus current = Status;
        UpdateStatus(current with
        {
            State = "unavailable",
            ErrorCode = GetErrorCode(exception),
            ErrorMessage = exception.Message
        });
        LogDebug($"连接失败: {GetErrorCode(exception)}; {exception.Message}");
    }

    private void UpdateStatus(BridgeResponse response)
    {
        BridgeHandshake? handshake = response.Handshake;
        lock (_gate)
        {
            _status = new TodoQingdanConnectionStatus(
                handshake?.ConnectionState ?? "unavailable",
                handshake?.ProductVersion,
                handshake?.AdapterId,
                handshake?.SchemaFingerprint,
                handshake?.Capabilities ?? [],
                response.ErrorCode,
                GetDatabasePath(),
                File.Exists(GetDatabasePath()),
                response.Snapshot is null ? _status.LastSyncTime : DateTimeOffset.Now,
                response.Snapshot?.Tasks.Count ?? _status.TaskCount,
                false,
                response.ErrorMessage);
        }
        if (response.Snapshot is not null)
            LogDebug($"读取任务数量: {response.Snapshot.Tasks.Count}");
    }

    private void UpdateStatus(TodoQingdanConnectionStatus status)
    {
        lock (_gate) _status = status;
    }

    private async Task<BridgeResponse> SendAsync(string operation, CancellationToken cancellationToken)
    {
        (string? executable, string dataRoot, string productVersion) = _discover();
        string pipeName = $"DeskBox.TodoQingdan.{Environment.ProcessId}.{Guid.NewGuid():N}";
        string bridge = _bridgeExecutable;
        if (!File.Exists(bridge)) throw new InvalidOperationException("bridge-not-found");
        var startInfo = new ProcessStartInfo
        {
            FileName = bridge,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--data-root");
        startInfo.ArgumentList.Add(dataRoot);
        startInfo.ArgumentList.Add("--product-version");
        startInfo.ArgumentList.Add(productVersion);
        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("bridge-start-failed");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };
            string requestId = Guid.NewGuid().ToString("N");
            var request = new BridgeRequest(
                BridgeProtocol.Version,
                requestId,
                operation,
                DateTimeOffset.UtcNow.AddSeconds(3).ToUnixTimeMilliseconds());
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeRequest));
            string? line = await reader.ReadLineAsync(timeout.Token);
            BridgeResponse response = JsonSerializer.Deserialize(line ?? string.Empty, BridgeJsonContext.Default.BridgeResponse)
                ?? throw new InvalidDataException("empty-response");
            if (!string.Equals(response.RequestId, requestId, StringComparison.Ordinal))
                throw new InvalidDataException("request-id-mismatch");
            return response;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static (string? Executable, string DataRoot, string ProductVersion) Discover()
    {
        string dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "todo-list");
        string? executable = Process.GetProcesses()
            .Select(process => { try { return process.MainModule?.FileName; } catch { return null; } })
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) &&
                Path.GetFileName(path).StartsWith("Todo", StringComparison.OrdinalIgnoreCase));
        executable ??= DiscoverFromRegistry();
        if (executable is null)
        {
            string programs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs");
            executable = Directory.Exists(programs)
                ? Directory.EnumerateFiles(programs, "Todo*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(path => !Path.GetFileName(path).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
                : null;
        }
        if (!Directory.Exists(dataRoot) || !File.Exists(Path.Combine(dataRoot, "todos.db")))
            throw new InvalidOperationException("todo-qingdan-not-found");
        string productVersion = executable is null
            ? DiscoverVersionFromLog(dataRoot)
            : FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? string.Empty;
        return (executable, dataRoot, productVersion);
    }

    private static string DiscoverVersionFromLog(string dataRoot)
    {
        string logPath = Path.Combine(dataRoot, "logs", "main.log");
        if (!File.Exists(logPath)) return string.Empty;
        try
        {
            string tail = ReadFileTail(logPath, 256 * 1024);
            MatchCollection matches = Regex.Matches(
                tail,
                @"(?:version|版本)\s+(?<version>\d+\.\d+\.\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return matches.Count == 0 ? string.Empty : matches[^1].Groups["version"].Value;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadFileTail(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int length = (int)Math.Min(stream.Length, maximumBytes);
        stream.Seek(-length, SeekOrigin.End);
        byte[] buffer = new byte[length];
        int read = stream.Read(buffer, 0, length);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private string GetDatabasePath()
    {
        try
        {
            return Path.Combine(_discover().DataRoot, "todos.db");
        }
        catch
        {
            return GetDefaultDatabasePath();
        }
    }

    private static string GetDefaultDatabasePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "todo-list", "todos.db");

    private static string GetFileRevision(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? $"{file.Length}-{file.LastWriteTimeUtc.Ticks}" : "missing";
    }

    private static string GetErrorCode(Exception exception) =>
        exception is InvalidOperationException { Message: var message } && !string.IsNullOrWhiteSpace(message)
            ? message
            : exception.GetType().Name;

    private static TodoQingdanConnectionStatus CreateInitialStatus()
    {
        string path = GetDefaultDatabasePath();
        return new("not-checked", null, null, null, [], null, path, File.Exists(path), null, 0, false, null);
    }

    private static void LogDebug(string message) => App.Log($"[TODOLIST DEBUG] {message}");

    private static string? DiscoverFromRegistry()
    {
        foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using RegistryKey? uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using RegistryKey? entry = uninstall.OpenSubKey(name);
                string displayName = entry?.GetValue("DisplayName") as string ?? string.Empty;
                if (!displayName.Contains("Todo", StringComparison.OrdinalIgnoreCase)) continue;
                string location = entry?.GetValue("InstallLocation") as string ?? string.Empty;
                if (!Directory.Exists(location)) continue;
                string? candidate = Directory.EnumerateFiles(location, "Todo*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => !Path.GetFileName(path).StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase));
                if (candidate is not null) return candidate;
            }
        }
        return null;
    }
}

internal sealed class TodoQingdanRepository : ITodoRepository
{
    private readonly TodoQingdanRepositoryFactory _factory;
    private readonly string _snapshotPath;
    private string? _versionToken;

    public TodoQingdanRepository(
        TodoQingdanRepositoryFactory factory,
        string widgetId,
        string cacheDirectory)
    {
        _factory = factory;
        _snapshotPath = Path.Combine(cacheDirectory, Sanitize(widgetId) + ".json");
    }

    public bool CanWrite => false;
    public string AttachmentDirectory => string.Empty;
    public string? VersionToken => _versionToken;

    public async Task<TodoWidgetData> LoadAsync()
    {
        try
        {
            BridgeResponse response = await _factory.ReadTasksAsync();
            if (!response.Succeeded || response.Snapshot is null) throw new InvalidOperationException(response.ErrorCode);
            TodoWidgetData data = Map(response.Snapshot);
            _versionToken = response.Snapshot.Revision;
            await ResilientJsonStore.SaveAsync(
                _snapshotPath,
                JsonSerializer.Serialize(data, TodoQingdanIntegrationJsonContext.Default.TodoWidgetData));
            return data;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            TodoWidgetData cached = await ResilientJsonStore.LoadAsync(
                _snapshotPath,
                json => JsonSerializer.Deserialize(json, TodoQingdanIntegrationJsonContext.Default.TodoWidgetData) ?? new TodoWidgetData(),
                () => new TodoWidgetData(),
                nameof(TodoQingdanRepository));
            _factory.RecordOfflineCache(cached.Items.Count, ex);
            return cached;
        }
    }

    public Task SaveAsync(TodoWidgetData data) =>
        Task.FromException(new InvalidOperationException("TodoQingdan is read-only in DeskBox."));
    public Task ClearAsync() =>
        Task.FromException(new InvalidOperationException("TodoQingdan account data cannot be cleared by DeskBox."));

    private static TodoWidgetData Map(BridgeTaskSnapshot snapshot) => new()
    {
        Items = snapshot.Tasks.Select((task, index) => new TodoItem
        {
            Id = task.Id,
            Text = task.Title,
            Notes = task.Notes,
            IsCompleted = task.IsCompleted,
            DueDate = task.DueUnixMilliseconds is { } due ? DateTimeOffset.FromUnixTimeMilliseconds(due) : null,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(task.CreatedUnixMilliseconds),
            UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(task.UpdatedUnixMilliseconds),
            SortOrder = index
        }).ToList()
    };

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
}
