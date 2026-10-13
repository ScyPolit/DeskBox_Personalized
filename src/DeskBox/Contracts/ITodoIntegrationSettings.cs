namespace DeskBox.Contracts;

public interface ITodoIntegrationSettings
{
    string DataSource { get; }
    bool UsesExternalSource { get; }
    string ConnectionState { get; }
    string DetectedVersion { get; }
    bool TodoDebugMode { get; }
    string DataSourcePath { get; }
    bool DatabaseExists { get; }
    DateTimeOffset? LastSyncTime { get; }
    int TaskCount { get; }
    string ErrorLog { get; }
    event Action? Changed;
    Task SetDataSourceAsync(string source);
    Task SetTodoDebugModeAsync(bool enabled);
    Task RefreshCompatibilityAsync(CancellationToken cancellationToken = default);
}
