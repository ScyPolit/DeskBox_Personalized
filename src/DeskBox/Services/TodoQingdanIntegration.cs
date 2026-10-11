using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        _path = Path.Combine(
            dataDirectory ?? DeskBoxDataPathService.Current.DataDirectory,
            "todo-qingdan-integration.json");
    }

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
    string? ErrorCode);

public sealed class TodoRepositoryRouter : ITodoRepositoryFactory, ITodoIntegrationSettings
{
    private readonly ITodoRepositoryFactory _local;
    private readonly TodoQingdanRepositoryFactory _external;
    private readonly TodoQingdanIntegrationStore _store;
    private TodoQingdanIntegrationSettings _settings = new();

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
    public TodoQingdanConnectionStatus Status => _external.Status;
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        _settings = await _store.LoadAsync();
        Changed?.Invoke();
    }

    public async Task SetDataSourceAsync(TodoDataSource source)
    {
        if (_settings.DataSource == source) return;
        _settings.DataSource = source;
        await _store.SaveAsync(_settings);
        Changed?.Invoke();
    }

    public Task SetDataSourceAsync(string source) => SetDataSourceAsync(
        Enum.TryParse(source, out TodoDataSource parsed)
            ? parsed
            : TodoDataSource.DeskBoxLocal);

    public async Task RefreshCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        await _external.RefreshCompatibilityAsync(cancellationToken);
        Changed?.Invoke();
    }

    public ITodoRepository ForWidget(string widgetId) => new RoutedTodoRepository(this, widgetId);

    public Task DeleteForWidgetAsync(string widgetId) => UsesExternalSource
        ? Task.CompletedTask
        : _local.DeleteForWidgetAsync(widgetId);

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
    private TodoQingdanConnectionStatus _status = new("not-checked", null, null, null, [], null);
    public bool UsesExternalSource => true;
    public TodoQingdanConnectionStatus Status { get { lock (_gate) return _status; } }

    public ITodoRepository ForWidget(string widgetId) => new TodoQingdanRepository(this, widgetId);
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
                response.ErrorCode);
        }
    }

    private static async Task<BridgeResponse> SendAsync(string operation, CancellationToken cancellationToken)
    {
        (string executable, string dataRoot, string productVersion) = Discover();
        string pipeName = $"DeskBox.TodoQingdan.{Environment.ProcessId}.{Guid.NewGuid():N}";
        string bridge = Path.Combine(
            AppContext.BaseDirectory,
            "TodoQingdanBridge",
            "DeskBox.TodoQingdanBridge.exe");
        if (!File.Exists(bridge)) throw new InvalidOperationException("bridge-not-found");
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = bridge,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = $"--pipe \"{pipeName}\" --data-root \"{dataRoot}\" --product-version \"{productVersion}\""
        }) ?? throw new InvalidOperationException("bridge-start-failed");
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

    private static (string Executable, string DataRoot, string ProductVersion) Discover()
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
        if (executable is null || !Directory.Exists(dataRoot))
            throw new InvalidOperationException("todo-qingdan-not-found");
        return (executable, dataRoot, FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? string.Empty);
    }

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

internal sealed class TodoQingdanRepository(TodoQingdanRepositoryFactory factory, string widgetId) : ITodoRepository
{
    private readonly string _snapshotPath = Path.Combine(
        DeskBoxDataPathService.Current.DataDirectory,
        "todo-qingdan-cache",
        Sanitize(widgetId) + ".json");
    private string? _versionToken;

    public bool CanWrite => false;
    public string AttachmentDirectory => string.Empty;
    public string? VersionToken => _versionToken;

    public async Task<TodoWidgetData> LoadAsync()
    {
        try
        {
            BridgeResponse response = await factory.ReadTasksAsync();
            if (!response.Succeeded || response.Snapshot is null) throw new InvalidOperationException(response.ErrorCode);
            TodoWidgetData data = Map(response.Snapshot);
            _versionToken = response.Snapshot.Revision;
            await ResilientJsonStore.SaveAsync(
                _snapshotPath,
                JsonSerializer.Serialize(data, TodoQingdanIntegrationJsonContext.Default.TodoWidgetData));
            return data;
        }
        catch
        {
            return await ResilientJsonStore.LoadAsync(
                _snapshotPath,
                json => JsonSerializer.Deserialize(json, TodoQingdanIntegrationJsonContext.Default.TodoWidgetData) ?? new TodoWidgetData(),
                () => new TodoWidgetData(),
                nameof(TodoQingdanRepository));
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
