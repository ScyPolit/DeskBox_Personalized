namespace DeskBox.Tests;

public sealed class TodoQingdanDeliveryContractTests
{
    [Fact]
    public void ProtocolPersistenceAndBridgeUseSourceGeneratedJsonAndReadOnlySqlite()
    {
        string protocol = Read("src/DeskBox.TodoQingdan.Protocol/ProtocolModels.cs");
        string integration = Read("src/DeskBox/Services/TodoQingdanIntegration.cs");
        string bridge = Read("src/DeskBox.TodoQingdanBridge/Program.cs");

        Assert.Contains("JsonSerializerContext", protocol, StringComparison.Ordinal);
        Assert.Contains("BridgeJsonContext.Default", integration, StringComparison.Ordinal);
        Assert.Contains("TodoQingdanIntegrationJsonContext.Default", integration, StringComparison.Ordinal);
        Assert.Contains("Mode = SqliteOpenMode.ReadOnly", bridge, StringComparison.Ordinal);
        Assert.Contains("PRAGMA query_only=ON", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE todos", bridge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM todos", bridge, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO todos", bridge, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeUsesOneRouterAndExternalDeletionReminderAndMutationAreBlocked()
    {
        string app = Read("src/DeskBox/App.xaml.cs");
        string integration = Read("src/DeskBox/Services/TodoQingdanIntegration.cs");
        string reminder = Read("src/DeskBox/Services/TodoReminderService.cs");
        string action = Read("src/DeskBox/Services/SearchResultActionService.cs");

        Assert.Contains("private readonly TodoRepositoryRouter _todoRepositoryFactory", app, StringComparison.Ordinal);
        Assert.Contains("? Task.CompletedTask", integration, StringComparison.Ordinal);
        Assert.Contains("Task.FromException", integration, StringComparison.Ordinal);
        Assert.Contains("!_repositoryFactory.UsesExternalSource", reminder, StringComparison.Ordinal);
        Assert.Contains("if (!repository.CanWrite) return false", action, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoSurfaceAndSettingsExposeCapabilityDrivenReadOnlyState()
    {
        string todo = Read("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml");
        string settings = Read("src/DeskBox/Views/SettingsWindow.xaml");
        string viewModel = Read("src/DeskBox/ViewModels/TodoWidgetViewModel.cs");

        Assert.Contains("Path=DataContext.CanWrite", todo, StringComparison.Ordinal);
        Assert.Contains("Path=DataContext.IsReadOnly", todo, StringComparison.Ordinal);
        Assert.Contains("Settings.Todo.DataSource.Title", settings, StringComparison.Ordinal);
        Assert.Contains("CanUseDeskBoxReminders", settings, StringComparison.Ordinal);
        Assert.Contains("public bool CanWrite => _store.CanWrite", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCopiesBridgeAndOfflineRuntimePayload()
    {
        string project = Read("src/DeskBox/DeskBox.csproj");
        string bridgeProject = Read("src/DeskBox.TodoQingdanBridge/DeskBox.TodoQingdanBridge.csproj");
        Assert.Contains("BuildTodoQingdanBridge", project, StringComparison.Ordinal);
        Assert.Contains("CopyTodoQingdanBridge", project, StringComparison.Ordinal);
        Assert.Contains("PublishTodoQingdanBridge", project, StringComparison.Ordinal);
        Assert.Contains("SelfContained=true", project, StringComparison.Ordinal);
        Assert.Contains("PublishAot=true", project, StringComparison.Ordinal);
        Assert.Contains("TodoQingdanBridge\\%(RecursiveDir)", project, StringComparison.Ordinal);
        Assert.Contains("TodoQingdanBridgeBuildDir", project, StringComparison.Ordinal);
        Assert.Contains("$(TodoQingdanBridgeBuildDir)**\\*", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Data.Sqlite", bridgeProject, StringComparison.Ordinal);
    }

    private static string Read(string relative) =>
        File.ReadAllText(TestPaths.FromRepository(relative));
}
