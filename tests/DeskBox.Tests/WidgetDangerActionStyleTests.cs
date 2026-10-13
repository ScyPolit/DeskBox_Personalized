using System.Text.Json;

namespace DeskBox.Tests;

public sealed class WidgetDangerActionStyleTests
{
    [Fact]
    public void WidgetCloseActions_UseTheSharedFluentCriticalStyle()
    {
        string root = FindRepositoryRoot();
        string sharedStyle = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Services/WidgetDangerActionStyle.cs"));
        string contentMenus = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/ContentWidgetWindow.Commands.cs"));
        string confirmationBuilder = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Services/WidgetCompactConfirmationMenuBuilder.cs"));
        string shell = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Controls/WidgetShell.xaml"));

        Assert.Contains(
            "SystemFillColorCriticalBrush",
            sharedStyle,
            StringComparison.Ordinal);
        Assert.Contains(
            "WidgetDangerActionStyle.Apply(disableWidget)",
            contentMenus,
            StringComparison.Ordinal);
        Assert.Contains(
            "WidgetDangerActionStyle.Apply(confirmItem)",
            confirmationBuilder,
            StringComparison.Ordinal);
        Assert.Contains(
            "Foreground=\"{ThemeResource SystemFillColorCriticalBrush}\"",
            shell,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Colors.Red", contentMenus, StringComparison.Ordinal);
        Assert.DoesNotContain("Colors.Red", confirmationBuilder, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedFolderRecycle_RequiresASecondConfirmationWithCancelFirst()
    {
        string root = FindRepositoryRoot();
        string contentMenus = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/ContentWidgetWindow.Commands.cs"));
        string confirmationBuilder = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Services/WidgetCompactConfirmationMenuBuilder.cs"));

        Assert.Contains(
            "ShowDeleteManagedFolderConfirmationAsync",
            contentMenus,
            StringComparison.Ordinal);
        Assert.Contains(
            "CancelFirst = true",
            contentMenus,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (options.CancelFirst && cancelItem is not null)",
            confirmationBuilder,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedFolderCloseMenu_UsesRequestedCopyAndQuestionIcon()
    {
        string root = FindRepositoryRoot();
        string contentMenus = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/ContentWidgetWindow.Commands.cs"));
        using JsonDocument strings = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root, "src/DeskBox/Strings/zh-CN.json")));

        Assert.Equal(
            "格子内的文件怎么处理?",
            strings.RootElement.GetProperty("Widget.DeleteManagedInfo").GetString());
        Assert.Equal(
            "同时移入回收站",
            strings.RootElement.GetProperty("Widget.DeleteFolderToRecycleBin").GetString());
        Assert.Contains(
            "Icon = new FontIcon { Glyph = \"\\uE897\" }",
            contentMenus,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GroupPickerDissolve_RequiresCompactConfirmationAndCriticalStyle()
    {
        string root = FindRepositoryRoot();
        string switcher = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Controls/WidgetGroupTitleSwitcher.xaml.cs"));

        // Both trailing group commands carry the shared critical style; the
        // single-member removal stays direct because it is reversible.
        Assert.Contains(
            "WidgetDangerActionStyle.Apply(remove);",
            switcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "WidgetDangerActionStyle.Apply(dissolve);",
            switcher,
            StringComparison.Ordinal);
        // Dissolving routes through the compact confirmation builder with the
        // localized settings dialog copy instead of the menu click.
        Assert.Contains(
            "CreateDeleteConfirmation(",
            switcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "Settings.WidgetGroups.DissolveDialog.Title",
            switcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "Settings.WidgetGroups.DissolveDialog.Confirm",
            switcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "Settings.WidgetGroups.DissolveDialog.Description",
            switcher,
            StringComparison.Ordinal);
        string dissolveSection = ExtractSection(
            switcher,
            "var dissolve = new MenuFlyoutItem",
            "flyout.Items.Add(dissolve);");
        // The click only arms the pending flag; DissolveRequested now fires
        // from the confirmation callback, never straight from the menu item.
        Assert.Contains(
            "dissolve.Click += (_, _) => dissolvePendingWhenClosed = true;",
            dissolveSection,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DissolveRequested",
            dissolveSection,
            StringComparison.Ordinal);
        Assert.Contains(
            "DissolveRequested?.Invoke(this, EventArgs.Empty);",
            switcher,
            StringComparison.Ordinal);
        Assert.Contains(
            "DispatcherQueue.TryEnqueue(ShowDissolveConfirmation)",
            switcher,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ManagedFolderConfirmation_FormatsParentQualifiedDisplayPath()
    {
        string root = FindRepositoryRoot();
        string contentMenus = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/ContentWidgetWindow.Commands.cs"));
        string confirmation = ExtractSection(
            contentMenus,
            "private async Task ShowDeleteManagedFolderConfirmationAsync()",
            "private static Task<int?> CountTopLevelFolderEntriesAsync(");

        Assert.Contains(
            "string parentName = Path.GetFileName(",
            confirmation,
            StringComparison.Ordinal);
        Assert.Contains(
            "string displayPath = string.IsNullOrEmpty(parentName)",
            confirmation,
            StringComparison.Ordinal);
        // 48 characters is the widest single-line CJK budget; beyond it the
        // bare folder name reads better than a tail-truncated path.
        Assert.Contains(
            "displayPath.Length > 48",
            confirmation,
            StringComparison.Ordinal);
        // Both message formats take the display path; the full folderPath
        // only feeds the entry count.
        Assert.Equal(
            2,
            CountOccurrences(confirmation, "displayPath)"));
        Assert.Contains(
            "CountTopLevelFolderEntriesAsync(folderPath)",
            confirmation,
            StringComparison.Ordinal);
    }

    private static string ExtractSection(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
    }

    private static int CountOccurrences(string source, string marker)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "src",
                    "DeskBox",
                    "DeskBox.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "DeskBox repository root was not found.");
    }
}
