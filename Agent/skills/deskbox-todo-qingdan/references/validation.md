# Validation Reference

Use this reference for implementation gates, builds, restarts, and releases.

## Test Layers

### Contract and unit tests

- Local repository preserves all existing Todo semantics and persisted format.
- Source switching is non-destructive.
- Capability projection controls commands and settings.
- Protocol serialization, handshake, version mismatch, error mapping, timeout, and cancellation.
- Adapter fingerprint selection and unknown-schema read-only behavior.
- Snapshot diff, conflict detection, retry deduplication, and rollback.
- Privacy sentinels are absent from logs.

### Integration tests with isolated fixtures

- Read lists/tasks and map stable IDs.
- Bridge restart and DeskBox reconnect.
- Database busy/WAL behavior.
- Each write capability independently, including Todo清单 restart and sync observation.
- Upgrade from a known fixture fingerprint to an unknown one.

### Product integration tests

- Todo window, details, filters, capsule, grouping, responsive layout, keyboard navigation.
- Todo settings source selector and degraded states.
- Search reads the selected source.
- Todo清单 mode disables DeskBox recurrence generation and reminder mutations.
- Deleting/resetting a DeskBox widget never deletes Todo清单 account data.

## Standard Commands

Follow root `AGENTS.md`; it is authoritative. Typical test command:

```powershell
dotnet test .\tests\DeskBox.Tests\DeskBox.Tests.csproj `
  --no-restore --verbosity:minimal -p:Platform=x64
```

When restored assets are architecture-specific, add:

```text
-p:RuntimeIdentifier=win-x64
```

Build the affected project with the repository's canonical non-platform Debug workflow unless an architecture-specific gate is being run.

## Required Restart Workflow

After changing application code:

1. Stop every running `DeskBox.exe` whose executable path is under this repository.
2. Build the affected project.
3. Start `src/DeskBox/bin/Debug/net10.0-windows10.0.22621.0/DeskBox.exe`.
4. Verify exactly the intended repository build is running.
5. Report its absolute executable path.

Do not launch ordinary development sessions from `Output`, `artifacts`, `.artifacts`, or `src/DeskBox/AppPackages`.

## AOT And Packaging

- Keep reflection serialization out of new application/bridge protocol paths.
- Do not weaken existing AOT warning or XAML binding ratchets.
- Decide explicitly whether the bridge ships in Direct, Store, both, or neither. Unknown architecture/support must degrade cleanly.
- Direct installers remain fully offline; do not introduce runtime downloads.
- If the bridge is packaged, update installer manifests and upgrade/uninstall contract tests without deleting user-owned Todo清单 data.

## Completion Evidence

For each phase, update `Agent/IMPLEMENTATION_STATUS.md` with:

- tests/builds executed and exact result
- current capabilities and evidence level
- live data access disclosure
- known unsupported versions/platforms
- current Debug executable path after restart
- remaining risks and next stop condition
