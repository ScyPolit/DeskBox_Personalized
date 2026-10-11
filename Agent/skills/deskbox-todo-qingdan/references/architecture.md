# Architecture Reference

Use this reference when changing application boundaries or selecting integration call sites.

## Existing Todo Flow

```text
WidgetKind.Todo
-> TodoWidgetContentProvider
-> TodoWidgetContentAdapter
-> TodoWidgetViewModel
-> TodoWidgetContent.xaml
```

Keep the XAML, window host, capsule behavior, grouping, responsive layout, theme, and keyboard model intact. Change data ownership below the ViewModel through a repository/session abstraction.

## Important Existing Files

- `src/DeskBox/Services/TodoWidgetStore.cs`: local resilient JSON store and attachment root.
- `src/DeskBox/ViewModels/TodoWidgetViewModel*.cs`: current whole-document mutation and save behavior.
- `src/DeskBox/Controls/WidgetContents/TodoWidgetContent*.cs|xaml`: UI and interaction surface.
- `src/DeskBox/Services/TodoWidgetContentProvider.cs`: content composition.
- `src/DeskBox/Services/IWidgetContentProvider.cs`: current Todo store factory in provider context.
- `src/DeskBox/Services/ContentWidgetWindowFactory.cs`: detached content/window construction.
- `src/DeskBox/Services/TodoReminderService.cs`: reminder scans and notification mutations.
- `src/DeskBox/Features/Todo/TodoReminderRuntime.cs`: reminder lifetime.
- `src/DeskBox/Services/SearchEngineService.cs`: Todo search projection.
- `src/DeskBox/Services/SearchResultActionService.cs`: search-to-Todo mutation.
- `src/DeskBox/Services/WidgetManager.FeatureWidgets.cs`: Todo enable/reset paths.
- `src/DeskBox/Services/WidgetManager.cs`: widget deletion cleanup.
- `src/DeskBox/Services/DeskBoxDataBackupService.cs`: Todo backup/restore assumptions.
- `src/DeskBox/Services/TodoSettingsCoordinator.cs`: authoritative Todo preference writer.
- `src/DeskBox/Features/Todo/TodoSettingsViewModel.cs`: settings binding surface.
- `src/DeskBox/Models/TodoSettingsSlice.cs`: Todo settings slice.

Search current references with:

```powershell
rg -n "TodoWidgetStore|TodoStoreFactory|TodoReminder|WidgetKind.Todo" src tests -g "*.cs"
```

## Intended Boundary

Use a stable DeskBox-owned contract. Exact method shape should follow current behavior, cancellation needs, and testability. A likely transitional contract loads and saves a normalized `TodoWidgetData` snapshot plus exposes capabilities and an attachment policy. The local implementation wraps `TodoWidgetStore`; the external implementation translates snapshot differences to bridge operations.

Do not expose Todo清单 database rows or private protocol DTOs to the ViewModel. Keep these mappings inside the bridge adapter.

## Ownership

| Concern | Owner in Todo清单 mode |
| --- | --- |
| Widget window, capsule, layout, theme | DeskBox |
| Display filtering and transient selection | DeskBox |
| Task identity and business fields | Todo清单 |
| Lists/categories/tags and ordering | Todo清单 |
| Cloud sync and conflict rules | Todo清单 |
| Recurrence generation | Todo清单 |
| Notifications and snooze | Todo清单 |
| Attachment file lifecycle | Todo清单 |
| Pomodoro, workload, reports | Todo清单 |
| Last successful display snapshot | DeskBox integration cache |

## Settings

`TodoEnabled` remains the feature-widget switch. Add a data-source choice inside the Todo settings section. Switching sources must not migrate or delete data.

`AppSettings` is a frozen on-disk contract. Read the current architecture verdict before adding properties. If integration settings do not safely belong in that contract, use an independent versioned document under the DeskBox data root. Use a source-generated JSON context and resilient atomic persistence.

## Runtime

Start the bridge only when Todo is enabled and Todo清单 is selected or when settings performs an explicit connection check. Own watchers, refresh loops, IPC, and child-process cleanup through the established `IFeatureRuntime` and shutdown sequence patterns. Hidden/disabled integration must not retain polling or third-party modules.
