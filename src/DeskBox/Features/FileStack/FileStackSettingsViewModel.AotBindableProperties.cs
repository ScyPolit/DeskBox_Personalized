#if DESKBOX_NATIVE_AOT
namespace DeskBox.Features.FileStack;

// The file-stack section keeps its runtime {Binding} surface and binds
// through a section-level DataContext switch. Expose only the properties
// used by that XAML surface in NativeAOT builds, mirroring the
// GlanceWidgetViewModel bridge pattern. The three compiled x:Bind paths
// (OpenMode, AvailableOpenModeOptions, CustomRules) and the overview row's
// three x:Bind paths (OpenMode, AvailableOpenModeOptions,
// SettingsSummaryText) resolve through generated compiled bindings and
// need no bridge entries.
[WinRT.GeneratedBindableCustomProperty([
    nameof(AvailableGroupByOptions),
    nameof(AvailableOrderByOptions),
    nameof(AvailablePopoverLayoutOptions),
    nameof(AvailablePopoverStyleOptions),
    nameof(AvailableThresholdOptions),
    nameof(AvailableUnmatchedBehaviorOptions),
    nameof(AutoStacking),
    nameof(CanAddRule),
    nameof(HasNoRules),
    nameof(GroupBy),
    nameof(IsPopoverOpenMode),
    nameof(OrderBy),
    nameof(PopoverLayout),
    nameof(PopoverStyle),
    nameof(PreviewSummaryText),
    nameof(ShowCustomRules),
    nameof(Threshold),
    nameof(UnmatchedBehavior)
], [])]
public sealed partial class FileStackSettingsViewModel
{
}
#endif
