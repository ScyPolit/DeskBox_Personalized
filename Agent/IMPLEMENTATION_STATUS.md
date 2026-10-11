# Todo清单 Integration Status

This file is the handoff ledger for development agents. Update it after every independently verified phase. Do not record task content, credentials, cookies, keys, or other private data.

## Current State

- Phase: `Read-only delivery complete; mutation phase intentionally gated`
- Last updated: `2026-10-12`
- Todo清单 baseline: `3.14.2`
- Integration mode: `Selectable DeskBox local / Todo清单 3.14.2 read-only backend`
- Live data writes performed: `No`
- Live task content read or logged: `No`
- Running DeskBox: `F:\project\20261011\DeskBox_Personalized\src\DeskBox\bin\Debug\net10.0-windows10.0.22621.0\DeskBox.exe` (PID 40568 at final verification time; one repository instance; zero bridge processes in local mode)

## Capability Matrix

| Capability | State | Evidence |
| --- | --- | --- |
| Discover installation | Verified for current installation | Process, uninstall registry, explicit directory, and conventional per-user discovery are implemented without a machine-specific path |
| Detect version/schema | Verified structural only | Product version `3.14.2.0`; combined fingerprint `sha256:bb3953657542389c0ad4b3fe142a297f6851306b4d973c07a55fc77adf19a32f` |
| Read lists | Disabled | 3.14.2 schema exposes no verified list table/semantics |
| Read tasks | Verified read-only | Isolated bridge + exact 3.14.2 version/required-shape gate; fixture protocol/UI/privacy tests |
| Create task | Disabled | No verified Todo清单-owned mutation channel or sync evidence |
| Update title | Disabled | No isolated UI/cache/sync/restart evidence |
| Complete task | Disabled | No isolated UI/cache/sync/restart evidence |
| Delete task | Disabled | No isolated UI/cache/sync/restart evidence |
| Due date / importance | Disabled | Field and business-semantic mapping unverified |
| Move / reorder | Disabled | List identity, ordering, and conflict semantics unverified |
| Steps / notes | Disabled | Field and business-semantic mapping unverified |
| Attachments | Disabled | Todo清单-owned by product decision |
| Open native task | Disabled | No public URI or verified callable internal boundary |
| Recurrence / reminders | Disabled in future Todo清单 mode | Todo清单-owned by product decision |

## Phase Evidence

### Phase 0: safety probe and decision

- Added `scripts/Test-TodoQingdanCompatibility.ps1` and the dependency-free Node structural probe. The PowerShell entry discovers the install and data root; the Node process uses `node:sqlite` in `readOnly` mode plus `PRAGMA query_only=ON`.
- The probe reads only the SQLite file header, `user_version`, `application_id`, journal mode, object names, column names/types, index metadata, and trigger names. It never queries application rows or emits SQL definitions/database paths.
- While Todo清单 was running, `todos.db` was confirmed as readable SQLite in WAL mode; structure-only concurrent read succeeded. The observed structure has 2 tables, 6 indexes, 0 views, 0 triggers, and 24 columns. Names/types are present only in the local privacy-filtered report, not this ledger.
- `task.db` does not have a plain SQLite header and is reported only as `encrypted-or-unrecognized`; no decryption was attempted.
- `app.asar` metadata-only parsing enumerated 59,065 archive file names and found 33 candidate IPC/service entry names. Only a hash of that candidate-name set was reported (`sha256:074be75cf7428483acea5c29a1c667773e9f821caaec4162fa69d0bb1bc5c1fc`); no source body was extracted or logged. No callable internal boundary is verified.
- Added `docs/architecture/todo-qingdan-phase-0-safety-decision.md` with privacy boundaries and mandatory write stop conditions. Direct database writes are not approved.

### Phase 1: zero-behavior-change repository abstraction

- Added the DeskBox-owned `ITodoRepository` and `ITodoRepositoryFactory` contracts. The snapshot contract exposes load/save/clear, the attachment root, and an optional opaque version token; it does not expose local file paths or Todo清单 rows/protocol DTOs.
- Added `DeskBoxTodoRepository` and `DeskBoxTodoRepositoryFactory` as the local adapter over the unchanged `TodoWidgetStore`. The adapter delegates serialization, normalization, resilient backup, attachment location, clear, and delete behavior to the existing store.
- `App` now owns one repository factory instance and injects it into WidgetManager, Todo content/window creation, Todo reminders/notification mutations, search indexing/recommendations, and search-result Todo actions. These runtime callers no longer construct local stores independently.
- Widget reset, duplicate cleanup, and widget deletion use the injected factory. Local orphan-store adoption and backup/restore intentionally continue to use `TodoWidgetStore` directly because they operate on DeskBox's explicit local disk protocol/staging roots, not the selected runtime backend.
- Reminder snapshot caching now uses an opaque repository `VersionToken`; the local adapter preserves the previous last-write optimization while future bridge repositories can provide a remote revision or disable the optimization with `null`.
- Existing `TodoWidgetStore` constructors and test/AOT fixture overloads remain available, preserving established tests and isolated evidence paths.

## Verification

- Real structure-only probe: passed against Todo清单 3.14.2 while it was running. Report privacy flags confirm no row query, task content read, SQL definition output, or database path output.
- Phase 0 targeted x64 tests: `3/3` passed for read-only byte preservation, stable fingerprints, encrypted/unknown format degradation, all task capabilities disabled, and private sentinel exclusion.
- Phase 1 targeted Todo/repository tests: `147/147` passed across repository bytes/deletion/wiring, Todo store/ViewModel/reminders, search, content factories, and orphan cleanup. Final repository/deletion subset passed `7/7` after the last guard change.
- Full x64 suite: `dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj --no-restore --verbosity:minimal -p:Platform=x64` passed `5315/5315`.
- Local format evidence: the repository adapter and direct `TodoWidgetStore` produced byte-identical `todo.json` for the same normalized fixture; attachment roots, clear behavior, sibling isolation, and delete behavior passed.
- Canonical Debug build: `dotnet build .\src\DeskBox\DeskBox.csproj --no-restore --verbosity:minimal` completed with `0` errors and `18` existing warnings.
- Restart verification: the previous repository DeskBox instance was stopped before the final build, then the canonical Debug executable was started with the isolated development data root. Exactly one repository instance was running at verification time, PID 47028.
- Test host note: this machine initially had no system .NET 10 runtime. SDK 10.0.401 was installed under ignored `.codex-temp\dotnet10`; tests used process-scoped `DOTNET_ROOT` and `DOTNET_MULTILEVEL_LOOKUP=0`. No system configuration was changed.

### Read-only delivery (Phases 2, 3, 5, and safe portion of 6)

- Added `DeskBox.TodoQingdan.Protocol` and the isolated `DeskBox.TodoQingdanBridge` named-pipe process. Requests carry protocol version, request ID, operation and deadline; responses carry structured error codes, adapter identity, schema fingerprint, capabilities and risks.
- Added the `todo-qingdan-3.14.2-readonly` adapter. It opens `todos.db` with SQLite read-only mode, `query_only=ON`, private cache, no pooling, and a bounded busy timeout. The adapter maps only stable ID, title, notes, completion, due/create/update times, and sort order. Lists/categories, recurrence, reminders, steps, attachments, importance and every mutation remain unsupported.
- Added independent source-generated `todo-qingdan-integration.json` settings so the frozen `AppSettings` wire format is unchanged. Switching sources never migrates, merges, clears or deletes either side; switching back immediately restores the original local `todo.json` data.
- Added dynamic repository routing shared by window, search, reminder/notification, reset and deletion paths. Existing windows refresh on source changes. External deletion/reset is a no-op for Todo清单 account data.
- Added source-generated last-successful snapshot storage. Live read failures degrade to the snapshot, then an explicit empty/unavailable state; they never show DeskBox local tasks while Todo清单 is selected.
- Todo清单 mode is capability-driven read-only: write controls are disabled, repository writes fail closed with UI rollback, search attachment actions reject, DeskBox reminders/recurrence generation do not run, and deleting a DeskBox widget cannot delete Todo清单 data.
- Settings exposes the data-source selector, connection state, detected version, compatibility refresh, and one-click return to local Todo. Twelve localization files keep identical keys/placeholders.
- Compatibility tooling now reports the reviewed 3.14.2 read-only adapter and `ReadTasks` only when product version plus required structural shape match. Unknown versions/schemas remain unsupported/read-only.
- Bridge distribution is offline and isolated. Debug copies the complete bridge payload to `TodoQingdanBridge/`; Release publish produces a self-contained Native AOT bridge with private `e_sqlite3.dll`, no CoreCLR/hostfxr payload.

## Final Verification Evidence

- Bridge/protocol/integration fixture tests cover handshake, request IDs, version mismatch, deadlines, process exit, known/unknown schema, read projection, read-only mutation rejection, source switching, local byte preservation, corrupt settings recovery, deletion isolation, and privacy sentinels.
- TodoQingdan/JSON/localization final targeted set: `34/34` passed.
- x64 full suite: `5325/5325` passed.
- Canonical Debug build: `0` errors and `18` existing warnings; complete bridge payload present under the canonical output.
- Standalone x64 bridge Native AOT publish passed and produced only `DeskBox.TodoQingdanBridge.exe`, PDBs, and `e_sqlite3.dll`; no managed runtime files.
- Full x64 AOT publish generated both DeskBox and bridge Native AOT outputs with source-generated JSON and no unexpected warning category. The repository-wide audit script still reports 37 pre-existing/global XAML ratchet drifts unrelated to this feature (including WMC1510 expected 875 vs actual 787 and historical SearchPopup/WidgetInlineEditor counts), so the audit command is recorded as not green rather than misreported as passed.

## Live Data Access Disclosure

- Writes, copies, migrations, decryptions, and credential/session reads: `None`.
- Reads from live Todo清单 data: file existence and 16-byte database headers for `task.db`/`todos.db`; SQLite structural PRAGMA/catalog metadata from `todos.db`; no rows, titles, notes, attachment paths, account data, tokens, cookies, or keys.
- Reads from the installation: executable version metadata and ASAR file-name metadata only; no third-party source body was retained or committed.
- Phase 1 performed no additional reads from live Todo清单 installation or data. All repository behavior tests used isolated DeskBox fixtures under temporary directories.
- Read-only delivery tests used only sanitized SQLite fixtures. No live task rows were read during implementation or automated verification; the earlier Phase 0 live access remains structure metadata only. No live writes were performed.

## Known Risks And Stops

- `task.db` remains structurally inaccessible and may be encrypted or proprietary.
- The two visible tables in `todos.db` have not been mapped to product semantics; row reads stay disabled until a sanitized fixture and reviewed adapter prove the mapping.
- Candidate ASAR entry names do not prove a callable or stable IPC boundary.
- Unknown version or fingerprint, unavailable/busy structure, absent known-adapter manifest, missing Todo清单-owned mutation channel, or missing isolated sync evidence blocks every write.
- ARM64 compatibility is unverified; future bridge capability must degrade explicitly rather than loading an unsupported binary.

## Next Action

The safe deliverable is complete. The next possible work is Phase 4 mutation enablement, but it is deliberately stopped: no Todo清单-owned mutation channel or isolated cloud-sync/cache/restart evidence exists. Enabling any write requires a separate test account or explicitly authorized test data root plus UI, cache, restart, idempotency and cloud-sync evidence for each individual capability. Until then every mutation stays false.

## Upgrade Procedure

1. After a Todo清单 update, run `scripts/Test-TodoQingdanCompatibility.ps1`.
2. If version or required shape changes, the bridge selects `unsupported`, keeps writes disabled, and uses the last successful snapshot when available.
3. Add a new adapter only after a sanitized fixture proves field semantics; register it by product version plus required structural shape and add protocol/privacy tests.
4. To recover immediately, choose `DeskBox local Todo` in Todo settings. The original `todo.json` was never changed by source switching and returns without migration or merge.
