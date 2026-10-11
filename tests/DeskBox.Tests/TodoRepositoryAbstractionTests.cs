using DeskBox.Models;
using DeskBox.Services;
using DeskBox.Controls.WidgetContents;

namespace DeskBox.Tests;

public sealed class TodoRepositoryAbstractionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "deskbox-todo-repository",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LocalRepository_PreservesTodoWidgetStoreBytesAndAttachmentRoot()
    {
        string directRoot = Directory.CreateDirectory(Path.Combine(_root, "direct")).FullName;
        string repositoryRoot = Directory.CreateDirectory(Path.Combine(_root, "repository")).FullName;
        TodoWidgetData data = CreateData();
        var directStore = new TodoWidgetStore(directRoot, "todo-widget");
        await directStore.SaveAsync(data);

        var factory = new DeskBoxTodoRepositoryFactory(repositoryRoot);
        ITodoRepository repository = factory.ForWidget("todo-widget");
        await repository.SaveAsync(CreateData());

        string repositoryPath = Path.Combine(repositoryRoot, "todo-widget", "todo.json");
        Assert.Equal(
            await File.ReadAllBytesAsync(directStore.StorePath),
            await File.ReadAllBytesAsync(repositoryPath));
        Assert.Equal(
            Path.Combine(repositoryRoot, "todo-widget", "attachments"),
            repository.AttachmentDirectory);
        Assert.NotNull(repository.VersionToken);
    }

    [Fact]
    public async Task LocalFactory_ClearAndDeleteKeepSiblingWidgetData()
    {
        string widgetsRoot = Directory.CreateDirectory(Path.Combine(_root, "delete")).FullName;
        var factory = new DeskBoxTodoRepositoryFactory(widgetsRoot);
        ITodoRepository first = factory.ForWidget("first");
        ITodoRepository sibling = factory.ForWidget("sibling");
        await first.SaveAsync(CreateData());
        await sibling.SaveAsync(CreateData());

        await first.ClearAsync();
        Assert.Empty((await first.LoadAsync()).Items);
        Assert.Single((await sibling.LoadAsync()).Items);

        await factory.DeleteForWidgetAsync("first");
        Assert.False(Directory.Exists(Path.Combine(widgetsRoot, "first")));
        Assert.True(File.Exists(Path.Combine(widgetsRoot, "sibling", "todo.json")));
    }

    [Fact]
    public async Task SearchAndReminderUseInjectedRepositoryFactory()
    {
        string settingsRoot = Directory.CreateDirectory(Path.Combine(_root, "settings")).FullName;
        var settings = new SettingsService(settingsRoot);
        settings.Settings.SearchIncludeDeskBoxContent = true;
        settings.Settings.TodoReminderEnabled = true;
        settings.Settings.Widgets.Add(new WidgetConfig
        {
            Id = "todo-widget",
            Name = "Todo",
            WidgetKind = WidgetKind.Todo
        });
        FeatureWidgetSettings.SetEnabled(settings.Settings, WidgetKind.Todo, true);
        DateTimeOffset now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        var repository = new RecordingRepository(CreateData(now.AddMinutes(4)));
        var factory = new RecordingRepositoryFactory(repository);
        var localization = new LocalizationService(settings);
        var quickCapture = new QuickCaptureService(new QuickCaptureStore(
            Path.Combine(_root, "quick-capture")));
        using var search = new SearchEngineService(
            settings,
            localization,
            new EverythingSearchService(settings),
            quickCapture,
            factory);

        SearchResponse response = await search.SearchAsync("repository task");
        var notifications = new List<TodoReminderNotification>();
        var reminder = new TodoReminderService(
            settings,
            localization,
            dispatcherQueue: null,
            notifications.Add,
            factory,
            () => now);
        int notified = await reminder.CheckNowAsync(now);

        Assert.Contains(response.RankedItems, item =>
            item.Kind == SearchResultKind.Todo && item.Title == "Repository task");
        Assert.Equal(1, notified);
        Assert.Single(notifications);
        Assert.Contains("todo-widget", factory.RequestedWidgetIds);
        Assert.True(repository.LoadCount >= 2);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task WindowAndSearchActionUseInjectedRepositoryFactory()
    {
        string settingsRoot = Directory.CreateDirectory(Path.Combine(_root, "window-settings")).FullName;
        var settings = new SettingsService(settingsRoot);
        var config = new WidgetConfig
        {
            Id = "todo-widget",
            Name = "Todo",
            WidgetKind = WidgetKind.Todo
        };
        settings.Settings.Widgets.Add(config);
        var repository = new RecordingRepository(CreateData());
        var factory = new RecordingRepositoryFactory(repository);
        var contentFactory = new WidgetContentFactory(new LocalizationService(settings));
        var windowFactory = new ContentWidgetWindowFactory(
            contentFactory,
            settings,
            todoRepositoryFactory: factory);

        ContentWidgetWindowPlan plan = windowFactory.CreateContentWindowPlan(config);
        var adapter = Assert.IsType<TodoWidgetContentAdapter>(plan.Content);
        await adapter.InitializeAsync();

        string attachment = Path.Combine(_root, "attachment.txt");
        await File.WriteAllTextAsync(attachment, "fixture");
        var action = new SearchResultActionService(settings, factory);
        Assert.True(await action.AttachFileToTodoAsync(attachment));

        Assert.Contains(adapter.ViewModel.Items, item => item.Text == "Repository task");
        Assert.Contains(repository.Current.Items, item => item.Text == "attachment.txt");
        Assert.True(repository.LoadCount >= 2);
        Assert.Equal(1, repository.SaveCount);
        Assert.All(factory.RequestedWidgetIds, id => Assert.Equal("todo-widget", id));
    }

    [Fact]
    public void AppWiresOneRepositoryFactoryThroughRuntimeTodoCallers()
    {
        string app = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/App.xaml.cs"));
        string manager = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Services/WidgetManager.cs"));
        string search = File.ReadAllText(TestPaths.FromRepository("src/DeskBox/Services/SearchEngineService.cs"));

        Assert.Contains("private readonly TodoRepositoryRouter _todoRepositoryFactory", app, StringComparison.Ordinal);
        Assert.True(Count(app, "_todoRepositoryFactory") >= 6);
        Assert.Contains("_todoRepositoryFactory.DeleteForWidgetAsync(config.Id)", manager, StringComparison.Ordinal);
        Assert.DoesNotContain("new TodoWidgetStore(widget.Id)", search, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static TodoWidgetData CreateData(DateTimeOffset? dueDate = null) => new()
    {
        Items =
        [
            new TodoItem
            {
                Id = "task-1",
                Text = "Repository task",
                Notes = "Repository note",
                DueDate = dueDate,
                CreatedAt = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 10, 12, 1, 0, 0, TimeSpan.Zero),
                SortOrder = 0
            }
        ]
    };

    private sealed class RecordingRepositoryFactory(ITodoRepository repository)
        : ITodoRepositoryFactory
    {
        public bool UsesExternalSource => false;
        public List<string> RequestedWidgetIds { get; } = [];

        public ITodoRepository ForWidget(string widgetId)
        {
            RequestedWidgetIds.Add(widgetId);
            return repository;
        }

        public Task DeleteForWidgetAsync(string widgetId) => Task.CompletedTask;
    }

    private sealed class RecordingRepository(TodoWidgetData data) : ITodoRepository
    {
        private TodoWidgetData _data = data;

        public string AttachmentDirectory => string.Empty;
        public bool CanWrite => true;
        public string? VersionToken => null;
        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }
        public TodoWidgetData Current => _data;

        public Task<TodoWidgetData> LoadAsync()
        {
            LoadCount++;
            return Task.FromResult(_data);
        }

        public Task SaveAsync(TodoWidgetData data)
        {
            SaveCount++;
            _data = data;
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            _data = new TodoWidgetData();
            return Task.CompletedTask;
        }
    }

    private static int Count(string value, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
