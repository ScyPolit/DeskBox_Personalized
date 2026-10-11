using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Creates the editable, localized guide content shown in newly created
/// feature widgets. The generated data is ordinary user content: it is never
/// rewritten after creation and can be edited or removed at any time.
/// </summary>
internal static class WidgetFirstRunGuideFactory
{
    public static async Task<bool> EnsureQuickCaptureGuideAsync(
        QuickCaptureService quickCaptureService,
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(quickCaptureService);
        ArgumentNullException.ThrowIfNull(localizationService);

        QuickCaptureStoreData data = await quickCaptureService.GetDataAsync();
        if (!ShouldSeedQuickCapture(data))
        {
            return false;
        }

        (string title, string body) = CreateQuickCaptureGuide(localizationService);
        await quickCaptureService.AddDetailedItemWithResultAsync(
            title,
            body,
            QuickCaptureAppearancePreset.Default,
            TextContentFormat.Markdown);
        return true;
    }

    public static bool ShouldSeedQuickCapture(QuickCaptureStoreData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        // A data reset leaves tombstone stubs behind; only live entries
        // decide whether the store counts as empty for guide seeding.
        return data.Items.Count(item => item is not null && !item.IsDeleted) == 0;
    }

    public static (string Title, string Body) CreateQuickCaptureGuide(
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);
        return (
            localizationService.T("QuickCapture.Guide.Title"),
            localizationService.T("QuickCapture.Guide.Body"));
    }

    public static bool ShouldSeedTodo(TodoWidgetData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        // Same tombstone rule as the quick-capture store: deleted stubs
        // must not keep the first-run guide from seeding after a reset.
        return data.Items.Count(item => item is not null && !item.IsDeleted) == 0;
    }

    public static async Task<bool> EnsureTodoGuideAsync(
        TodoWidgetStore store,
        LocalizationService localizationService)
    {
        return await EnsureTodoGuideAsync(
            new DeskBoxTodoRepository(store),
            localizationService);
    }

    public static async Task<bool> EnsureTodoGuideAsync(
        ITodoRepository store,
        LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(localizationService);

        TodoWidgetData data = await store.LoadAsync();
        if (!ShouldSeedTodo(data))
        {
            return false;
        }

        data.Items.Add(CreateTodoGuide(localizationService));
        await store.SaveAsync(data);
        return true;
    }

    public static TodoItem CreateTodoGuide(LocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(localizationService);

        var now = DateTimeOffset.UtcNow;
        return new TodoItem
        {
            Text = localizationService.T("Todo.Guide.Title"),
            Notes = localizationService.T("Todo.Guide.Notes"),
            CreatedAt = now,
            UpdatedAt = now,
            SortOrder = 0,
            Steps =
            [
                new TodoStep
                {
                    Text = localizationService.T("Todo.Guide.Step.Complete"),
                    SortOrder = 0
                },
                new TodoStep
                {
                    Text = localizationService.T("Todo.Guide.Step.Schedule"),
                    SortOrder = 1
                },
                new TodoStep
                {
                    Text = localizationService.T("Todo.Guide.Step.BreakDown"),
                    SortOrder = 2
                }
            ]
        };
    }
}
