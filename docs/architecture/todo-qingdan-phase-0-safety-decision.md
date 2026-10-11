# Todo清单 Phase 0 Safety Decision

Status: completed for the 3.14.2 structural investigation on 2026-10-12.

## Boundary

The compatibility probe is an operator-invoked tool, not DeskBox application
code. It discovers the installed executable without a machine-specific path,
opens only the two known database files in SQLite read-only/query-only mode,
and reads schema metadata. It never selects application rows. The ASAR check
parses only archive file-name metadata and records a hash of possible
IPC/service entry names; it does not extract or report source text.

The report intentionally omits database paths, SQL definitions, row values,
account data, tokens, cookies, keys, and attachment paths. A schema fingerprint
is derived from product version-independent structural metadata: SQLite
`user_version`, `application_id`, journal mode, object names, column names and
declared types, index names, and trigger names.

## Capability Decision

Phase 0 proves installation discovery and structural compatibility detection.
It does not prove a task projection or a Todo清单-owned mutation channel.
Therefore `ReadLists`, `ReadTasks`, and every mutation capability remain false.
The future bridge may expose live read-only tasks only after a reviewed 3.14.2
adapter maps the structural fingerprint using a sanitized fixture.

## Mandatory Write Stops

All writes remain blocked if any of the following is true:

- the product version or structural fingerprint is unknown;
- a database is encrypted, unavailable, busy, or no longer matches the known
  adapter manifest;
- no Todo清单-owned mutation channel has been verified;
- UI, restart, cache, recurrence, reminder, statistics, and cloud-sync effects
  have not passed isolated tests;
- idempotency, rollback, and concurrent-change behavior are unproven;
- Todo清单 is running with an incompatible in-memory cache;
- the data root is live production data and the user has not separately
  authorized the exact mutation and recovery procedure.

A successful SQL statement is never write evidence. Direct database writes are
not an approved integration path at the end of Phase 0.

## Running The Probe

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\scripts\Test-TodoQingdanCompatibility.ps1
```

Use `-InstallationPath` and `-DataRoot` only for an isolated fixture or a
non-standard installation. `-OutputPath` writes the same privacy-filtered JSON
shown by the command. After a Todo清单 update, rerun this command and treat any
new fingerprint as unsupported until a fixture-backed adapter review is
complete.
