using System.Text;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskBox.Controls;

/// <summary>
/// The confirmation shown before abandoning an interrupted restore. It names
/// the stuck items, why each cannot be restored, and where the copies live,
/// because "some items were not restored" alone gives the user nothing to act on.
/// </summary>
internal static class DesktopOrganizationAbandonDialog
{
    public static async Task<bool> ConfirmAsync(
        XamlRoot? xamlRoot,
        LocalizationService localization,
        string? itemDetails)
    {
        if (xamlRoot is null)
        {
            return true;
        }

        var content = new StackPanel { Spacing = 12, MaxWidth = 480 };
        content.Children.Add(new TextBlock
        {
            Text = localization.T("DesktopOrganization.Public.AbandonConfirmBody"),
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(itemDetails))
        {
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 260,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = itemDetails,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    // Application-scope themed lookups follow the system
                    // theme; resolve against the host window's root instead.
                    Foreground = NeutralInteractionBrush.ResolveThemedResource(
                        "TextFillColorSecondaryBrush", xamlRoot.Content) ??
                        new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E))
                }
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localization.T("DesktopOrganization.Public.AbandonConfirmTitle"),
            PrimaryButtonText = localization.T("DesktopOrganization.Public.AbandonRestore"),
            CloseButtonText = localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close,
            Content = content
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static string BuildItemDetails(
        LocalizationService localization,
        OrganizationHistoryEntry history)
    {
        List<OrganizationHistoryItem> stuck = history.Items.Where(item => !item.IsRestored).ToList();
        if (stuck.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine(localization.Format("DesktopOrganization.Public.AbandonListHeader", stuck.Count));
        const int shownLimit = 8;
        for (int index = 0; index < Math.Min(stuck.Count, shownLimit); index++)
        {
            OrganizationHistoryItem item = stuck[index];
            builder.Append("• ").AppendLine(item.Name);
            builder.AppendLine("  " + localization.T(DesktopOrganizationTransaction.GetUndoBlockReasonKey(item)));
            builder.AppendLine("  " + localization.Format("DesktopOrganization.Public.AbandonItemSource", item.SourcePath));
            builder.AppendLine("  " + localization.Format("DesktopOrganization.Public.AbandonItemDestination", item.DestinationPath));
        }

        if (stuck.Count > shownLimit)
        {
            builder.AppendLine(localization.Format("DesktopOrganization.Public.AbandonMoreItems", stuck.Count - shownLimit));
        }

        return builder.ToString().TrimEnd();
    }
}
