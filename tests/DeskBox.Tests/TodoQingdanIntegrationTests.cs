using DeskBox.Models;
using DeskBox.Services;

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

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
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
