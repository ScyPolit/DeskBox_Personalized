namespace DeskBox.Contracts;

public interface ITodoIntegrationSettings
{
    string DataSource { get; }
    bool UsesExternalSource { get; }
    string ConnectionState { get; }
    string DetectedVersion { get; }
    event Action? Changed;
    Task SetDataSourceAsync(string source);
    Task RefreshCompatibilityAsync(CancellationToken cancellationToken = default);
}
