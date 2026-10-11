# Safety And Compatibility

Read this before inspecting Todo清单 internals or enabling any mutation.

## Live Data Rules

- Treat `%APPDATA%\todo-list` as production user data.
- Structural reads may inspect version metadata, SQLite PRAGMA output, table/column/index/trigger names, file headers, locking behavior, and hashes.
- Do not emit task rows, titles, notes, attachment paths, account data, tokens, cookies, or keys.
- Do not copy a live database into the repository or test results.
- Mutation experiments require an isolated fixture, test account, or explicit user authorization naming the exact live mutation and recovery method.

## Bridge Isolation

DeskBox must not load Todo清单's `better-sqlite3-multiple-ciphers` binary. Use a separate process with bounded startup/operation timeouts and a versioned named-pipe protocol. A bridge failure must return a structured unavailable/degraded state.

The protocol should include:

- protocol version and request ID
- operation and payload schema
- cancellation or deadline
- capability set
- adapter ID and schema fingerprint
- stable error codes such as `unsupported-version`, `schema-mismatch`, `read-only`, `conflict`, `busy`, `not-running`, and `operation-failed`

Use source-generated JSON metadata. Avoid reflection-based serialization in Native AOT application code.

## Compatibility Fingerprint

Do not gate writes on executable version alone. A useful non-sensitive fingerprint combines:

- product/file version
- executable or app resource build identity when appropriate
- database `user_version` and `application_id`
- required table/view names
- required column names and declared types
- relevant indexes and triggers
- private IPC/protocol version when discovered

Store known fingerprints in a reviewable manifest. An unknown fingerprint is read-only by default.

Adapters should be narrow, for example:

```text
Adapters/
  TodoQingdan_3_14_2
  UnsupportedTodoQingdanAdapter
```

Reuse an adapter across patch versions only after the compatibility script proves the required fingerprint and behavior remain equivalent.

## Write Evidence

An operation is not safe merely because a SQL statement succeeds. Before setting a write capability true, prove on isolated data that:

1. The Todo清单 UI observes the change.
2. Its in-memory state does not revert the change.
3. Its sync queue/cloud round trip preserves the change.
4. Another client receives the intended result when applicable.
5. Restarting Todo清单 preserves the result.
6. Statistics, recurrence, reminders, or attachments are not corrupted.
7. A repeated request is idempotent or safely deduplicated.

Prefer a discovered Todo清单-owned IPC/private mutation path over direct database writes. Direct database writes should remain disabled unless the full write set, transaction, cache invalidation, and sync semantics are understood.

## Capability And UI Policy

Return explicit read/write capabilities from the adapter. DeskBox hides unsupported commands. Read-only mode must not present editable controls that later discard user input.

Degrade in this order:

```text
verified write/read -> live read-only -> last valid snapshot -> unavailable
```

Never silently switch to DeskBox local Todo when Todo清单 was selected.

## Privacy Tests

Use fixture values such as `PRIVATE_SENTINEL_TITLE` and `PRIVATE_SENTINEL_TOKEN`; assert they never appear in logs, diagnostics, IPC metadata logs, compatibility reports, or exception summaries. Keep actual fixture payloads outside release output unless tests require them.
