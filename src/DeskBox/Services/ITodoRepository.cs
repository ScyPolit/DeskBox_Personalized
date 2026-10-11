using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// DeskBox-owned Todo persistence contract. Implementations expose normalized
/// widget snapshots without leaking their underlying storage format.
/// </summary>
public interface ITodoRepository
{
    bool CanWrite { get; }

    string AttachmentDirectory { get; }

    /// <summary>
    /// Optional opaque revision used to avoid reloading an unchanged snapshot.
    /// </summary>
    string? VersionToken { get; }

    Task<TodoWidgetData> LoadAsync();

    Task SaveAsync(TodoWidgetData data);

    Task ClearAsync();
}

public interface ITodoRepositoryFactory
{
    bool UsesExternalSource { get; }

    ITodoRepository ForWidget(string widgetId);

    Task DeleteForWidgetAsync(string widgetId);
}
