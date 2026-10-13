using DeskBox.Models;
using DeskBox.Services;
using Microsoft.Data.Sqlite;

namespace DeskBox.Tests;

public sealed class TodoQingdanIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "deskbox-todo-qingdan-integration", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SourceSwitchPersistsIndependentlyAndPreservesLocalTodoBytes()
    {
        string data = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        string widgets = Directory.CreateDirectory(Path.Combine(data, "widgets")).FullName;
        var local = new DeskBoxTodoRepositoryFactory(widgets);
        ITodoRepository repository = local.ForWidget("todo-widget");
        await repository.SaveAsync(new TodoWidgetData
        {
            Items = [new TodoItem { Id = "local", Text = "Local task" }]
        });
        string todoPath = Path.Combine(widgets, "todo-widget", "todo.json");
        byte[] before = await File.ReadAllBytesAsync(todoPath);
        var integrationStore = new TodoQingdanIntegrationStore(data);
        var router = new TodoRepositoryRouter(local, new TodoQingdanRepositoryFactory(), integrationStore);
        await router.InitializeAsync();

        await router.SetDataSourceAsync(TodoDataSource.TodoQingdan);
        Assert.True(router.UsesExternalSource);
        await router.SetDataSourceAsync(TodoDataSource.DeskBoxLocal);

        Assert.False(router.UsesExternalSource);
        Assert.Equal(before, await File.ReadAllBytesAsync(todoPath));
        TodoWidgetData restored = await router.ForWidget("todo-widget").LoadAsync();
        Assert.Equal("Local task", Assert.Single(restored.Items).Text);
        var reloaded = new TodoRepositoryRouter(local, new TodoQingdanRepositoryFactory(), integrationStore);
        await reloaded.InitializeAsync();
        Assert.Equal(TodoDataSource.DeskBoxLocal, reloaded.DataSource);
    }

    [Fact]
    public async Task ExternalSourceDeleteDoesNotReachLocalOrExternalAccountDeletion()
    {
        var local = new RecordingFactory();
        var router = new TodoRepositoryRouter(
            local,
            new TodoQingdanRepositoryFactory(),
            new TodoQingdanIntegrationStore(Directory.CreateDirectory(_root).FullName));
        await router.InitializeAsync();
        await router.SetDataSourceAsync(TodoDataSource.TodoQingdan);

        await router.DeleteForWidgetAsync("todo-widget");

        Assert.Equal(0, local.DeleteCount);
        Assert.False(router.ForWidget("todo-widget").CanWrite);
    }

    [Fact]
    public async Task CorruptIntegrationSettingsRecoverToLocalDefault()
    {
        string data = Directory.CreateDirectory(_root).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(data, "todo-qingdan-integration.json"),
            "not-json");
        var router = new TodoRepositoryRouter(
            new RecordingFactory(),
            new TodoQingdanRepositoryFactory(),
            new TodoQingdanIntegrationStore(data));

        await router.InitializeAsync();

        Assert.Equal(TodoDataSource.DeskBoxLocal, router.DataSource);
        Assert.True(router.ForWidget("todo-widget").CanWrite);
    }

    [Fact]
    public async Task DeveloperModePersistsWithAnExplicitDataSourcePreference()
    {
        string data = Directory.CreateDirectory(_root).FullName;
        var store = new TodoQingdanIntegrationStore(data);
        using var router = new TodoRepositoryRouter(
            new RecordingFactory(),
            new TodoQingdanRepositoryFactory(),
            store);
        await router.InitializeAsync();

        await router.SetDataSourceAsync(TodoDataSource.TodoQingdan);
        await router.SetTodoDebugModeAsync(true);

        using var reloaded = new TodoRepositoryRouter(
            new RecordingFactory(),
            new TodoQingdanRepositoryFactory(),
            store);
        await reloaded.InitializeAsync();
        Assert.Equal(TodoDataSource.TodoQingdan, reloaded.DataSource);
        Assert.True(reloaded.TodoDebugMode);
    }

    [Fact]
    public async Task ExternalRepositoryReturnsCachedTasksWhenDatabaseBecomesUnavailable()
    {
        string sourceRoot = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        string cacheRoot = Directory.CreateDirectory(Path.Combine(_root, "cache")).FullName;
        string databasePath = Path.Combine(sourceRoot, "todos.db");
        CreateTodoDatabase(databasePath);
        var factory = new TodoQingdanRepositoryFactory(
            () => (null, sourceRoot, "3.14.2.0"),
            cacheRoot,
            TestPaths.FromRepository(
                "src/DeskBox.TodoQingdanBridge/bin/x64/Debug/net10.0/DeskBox.TodoQingdanBridge.exe"));
        ITodoRepository repository = factory.ForWidget("todo-widget");

        TodoWidgetData online = await repository.LoadAsync();
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        TodoWidgetData offline = await repository.LoadAsync();

        Assert.True(
            online.Items.Count == 1,
            $"state={factory.Status.State}; error={factory.Status.ErrorCode}; detail={factory.Status.ErrorMessage}");
        TodoItem onlineItem = Assert.Single(online.Items);
        Assert.Equal("Cached task", onlineItem.Text);
        Assert.Equal("Cached task", Assert.Single(offline.Items).Text);
        Assert.Equal("offline-cache", factory.Status.State);
        Assert.True(factory.Status.UsedOfflineCache);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void CreateTodoDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE todos (
              task_id TEXT PRIMARY KEY, task_content TEXT, task_describe TEXT,
              complete INTEGER NOT NULL, create_time INTEGER NOT NULL,
              deleted INTEGER NOT NULL, sync_time INTEGER NOT NULL,
              task_sort REAL NOT NULL, todo_time INTEGER NOT NULL,
              update_time INTEGER NOT NULL, version INTEGER NOT NULL);
            INSERT INTO todos(task_id,task_content,task_describe,complete,create_time,deleted,sync_time,task_sort,todo_time,update_time,version)
            VALUES('cached-task','Cached task',NULL,0,1760230800,0,1760230800,1,0,1760230800,1);
            """;
        command.ExecuteNonQuery();
    }

    private sealed class RecordingFactory : ITodoRepositoryFactory
    {
        public int DeleteCount { get; private set; }
        public bool UsesExternalSource => false;
        public ITodoRepository ForWidget(string widgetId) => new MemoryRepository();
        public Task DeleteForWidgetAsync(string widgetId)
        {
            DeleteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryRepository : ITodoRepository
    {
        public bool CanWrite => true;
        public string AttachmentDirectory => string.Empty;
        public string? VersionToken => null;
        public Task<TodoWidgetData> LoadAsync() => Task.FromResult(new TodoWidgetData());
        public Task SaveAsync(TodoWidgetData data) => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;
    }
}
