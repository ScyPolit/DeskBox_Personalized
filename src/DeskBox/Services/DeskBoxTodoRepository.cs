using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Local Todo repository preserving the existing TodoWidgetStore behavior and
/// on-disk format.
/// </summary>
public sealed class DeskBoxTodoRepository : ITodoRepository
{
    private readonly TodoWidgetStore _store;

    public DeskBoxTodoRepository(string widgetId)
        : this(new TodoWidgetStore(widgetId))
    {
    }

    internal DeskBoxTodoRepository(TodoWidgetStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public string AttachmentDirectory => _store.AttachmentDirectory;
    public bool CanWrite => true;

    public string? VersionToken
    {
        get
        {
            DateTime writtenAtUtc = File.GetLastWriteTimeUtc(_store.StorePath);
            return writtenAtUtc == DateTime.MinValue
                ? null
                : writtenAtUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public Task<TodoWidgetData> LoadAsync() => _store.LoadAsync();

    public Task SaveAsync(TodoWidgetData data) => _store.SaveAsync(data);

    public Task ClearAsync() => _store.ClearAsync();
}

public sealed class DeskBoxTodoRepositoryFactory : ITodoRepositoryFactory
{
    public static DeskBoxTodoRepositoryFactory Default { get; } = new();

    private readonly string? _widgetsDataRoot;
    public bool UsesExternalSource => false;

    public DeskBoxTodoRepositoryFactory()
    {
    }

    internal DeskBoxTodoRepositoryFactory(string widgetsDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetsDataRoot);
        _widgetsDataRoot = widgetsDataRoot;
    }

    public ITodoRepository ForWidget(string widgetId)
    {
        TodoWidgetStore store = _widgetsDataRoot is null
            ? new TodoWidgetStore(widgetId)
            : new TodoWidgetStore(_widgetsDataRoot, widgetId);
        return new DeskBoxTodoRepository(store);
    }

    public Task DeleteForWidgetAsync(string widgetId) => _widgetsDataRoot is null
        ? TodoWidgetStore.DeleteForWidgetAsync(widgetId)
        : TodoWidgetStore.DeleteForWidgetAsync(_widgetsDataRoot, widgetId);
}

internal sealed class DelegateTodoRepositoryFactory(
    Func<string, TodoWidgetStore> storeFactory) : ITodoRepositoryFactory
{
    private readonly Func<string, TodoWidgetStore> _storeFactory =
        storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
    public bool UsesExternalSource => false;

    public ITodoRepository ForWidget(string widgetId) =>
        new DeskBoxTodoRepository(_storeFactory(widgetId));

    public Task DeleteForWidgetAsync(string widgetId) =>
        throw new NotSupportedException(
            "A delegate repository factory does not own widget deletion.");
}
