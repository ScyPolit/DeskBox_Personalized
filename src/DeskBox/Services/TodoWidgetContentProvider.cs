using DeskBox.Contracts;
using DeskBox.Controls.WidgetContents;
using DeskBox.Models;

namespace DeskBox.Services;

internal sealed class TodoWidgetContentProvider : IWidgetContentProvider
{
    public WidgetKind WidgetKind => WidgetKind.Todo;

    public bool CanCreateDetachedContent => true;

    public IWidgetContent CreateDetachedContent(WidgetConfig config, WidgetContentProviderContext context)
    {
        if (config.WidgetKind != WidgetKind)
        {
            throw new ArgumentException("Todo content requires a Todo widget config.", nameof(config));
        }

        ITodoRepository repository = context.TodoRepositoryFactory.ForWidget(config.Id);
        return context.SettingsService is null
            ? new TodoWidgetContentAdapter(config, repository, context.LocalizationService)
            : new TodoWidgetContentAdapter(config, repository, context.LocalizationService, context.SettingsService);
    }
}
