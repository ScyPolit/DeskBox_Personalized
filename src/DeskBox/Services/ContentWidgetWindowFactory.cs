using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Views;

namespace DeskBox.Services;

/// <summary>
/// Prepares lightweight content widget windows for non-file widget kinds.
/// </summary>
public sealed class ContentWidgetWindowFactory
{
    private readonly WidgetContentFactory _contentFactory;
    private readonly SettingsService _settingsService;
    private readonly Func<WidgetConfig, IWidgetContent, SettingsService, WidgetContentDescriptor, ContentWidgetWindow> _windowFactory;
    private readonly ITodoRepositoryFactory _todoRepositoryFactory;
    private readonly Func<WidgetConfig, IWidgetContent>? _quickCaptureContentFactory;
    private readonly Func<WidgetConfig, IWidgetContent>? _fileContentFactory;

    public ContentWidgetWindowFactory(
        WidgetContentFactory contentFactory,
        SettingsService settingsService,
        Func<WidgetConfig, IWidgetContent, SettingsService, WidgetContentDescriptor, ContentWidgetWindow>? windowFactory = null,
        Func<WidgetConfig, TodoWidgetStore>? todoStoreFactory = null,
        Func<WidgetConfig, IWidgetContent>? quickCaptureContentFactory = null,
        Func<WidgetConfig, IWidgetContent>? fileContentFactory = null,
        ITodoRepositoryFactory? todoRepositoryFactory = null)
    {
        _contentFactory = contentFactory;
        _settingsService = settingsService;
        _windowFactory = windowFactory ?? ((config, content, settings, descriptor) =>
            new ContentWidgetWindow(config, content, settings, descriptor));
        _todoRepositoryFactory = todoRepositoryFactory ??
            (todoStoreFactory is null
                ? DeskBoxTodoRepositoryFactory.Default
                : new DelegateTodoRepositoryFactory(widgetId =>
                    todoStoreFactory(new WidgetConfig
                    {
                        Id = widgetId,
                        WidgetKind = WidgetKind.Todo
                    })));
        _quickCaptureContentFactory = quickCaptureContentFactory;
        _fileContentFactory = fileContentFactory;
    }

    internal bool CanCreateContentWindow(WidgetKind widgetKind)
    {
        return widgetKind switch
        {
            WidgetKind.QuickCapture => _quickCaptureContentFactory is not null,
            WidgetKind.File => _fileContentFactory is not null,
            _ => _contentFactory.CanCreateDetachedContent(widgetKind)
        };
    }

    internal ContentWidgetWindow CreateContentWindow(WidgetConfig config)
    {
        var plan = CreateContentWindowPlan(config);
        return CreateContentWindow(plan);
    }

    internal ContentWidgetWindow CreateContentWindow(
        ContentWidgetWindowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _windowFactory(plan.Config, plan.Content, _settingsService, plan.Descriptor);
    }

    internal ContentWidgetWindowPlan CreateContentWindowPlan(
        WidgetConfig config,
        IWidgetContent? reusableContent = null)
    {
        if (!CanCreateContentWindow(config.WidgetKind))
        {
            throw new NotSupportedException(
                $"Widget kind '{config.WidgetKind}' does not support content windows.");
        }

        var descriptor = _contentFactory.GetDescriptor(config.WidgetKind);
        IWidgetContent content = reusableContent ?? (config.WidgetKind switch
        {
            WidgetKind.QuickCapture => _quickCaptureContentFactory!(config),
            WidgetKind.File => _fileContentFactory!(config),
            _ => _contentFactory.CreateDetachedContentFromRepository(
                config,
                _todoRepositoryFactory,
                _settingsService)
        });

        if (!string.Equals(content.WidgetId, config.Id, StringComparison.Ordinal) ||
            content.WidgetKind != config.WidgetKind)
        {
            (content as IDisposable)?.Dispose();
            throw new InvalidOperationException(
                "Reusable widget content did not match the requested member.");
        }

        return new ContentWidgetWindowPlan(config, content, descriptor);
    }
}

internal sealed record ContentWidgetWindowPlan(
    WidgetConfig Config,
    IWidgetContent Content,
    WidgetContentDescriptor Descriptor);
