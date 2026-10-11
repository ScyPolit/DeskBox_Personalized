---
name: deskbox-todo-qingdan
description: Develop, review, test, or maintain the optional Todo清单 backend integration for DeskBox's existing Todo widget, including the isolated bridge, capability gating, schema compatibility, and upgrade workflow. Do not use for unrelated DeskBox features.
---

# DeskBox Todo清单 Integration

Build this integration as an optional backend for the existing `WidgetKind.Todo`; do not add a second widget kind or replace the existing XAML experience.

## Start Here

1. Read the repository root `AGENTS.md`.
2. Read `Agent/DEVELOPMENT_PROMPT.md` for product scope, safety boundaries, phases, and acceptance criteria.
3. Inspect the current source before relying on file lists in these references; the repository evolves quickly.
4. Preserve unrelated worktree changes and all release artifacts.

## Core Decisions

- Keep DeskBox local Todo as the default and fully compatible backend.
- Isolate Todo清单 private formats and native dependencies in a bridge process. The DeskBox process communicates through a versioned IPC contract.
- Treat capabilities as runtime evidence, not assumptions inferred from a version string.
- Unknown version/schema means no writes. Degrade to read-only live data, then the last valid snapshot.
- Todo清单 owns cloud sync, recurrence generation, reminders, attachments, focus sessions, and statistics unless a verified integration contract proves a safe operation.
- Never use the user's live database for mutation tests. Do not log or commit task content, credentials, cookies, keys, or private third-party source.

## Routing

- For architecture, feature ownership, and likely integration call sites, read [references/architecture.md](references/architecture.md).
- Before database/schema/private-protocol investigation or bridge writes, read [references/safety-and-compatibility.md](references/safety-and-compatibility.md).
- When planning tests, builds, restart verification, or release impact, read [references/validation.md](references/validation.md).
- For tools, skills, MCP choices, and agent handoff, read [references/tooling.md](references/tooling.md).

## Working Method

Work in independently verifiable phases. First extract a repository boundary with zero behavior change. Then add bridge handshake and read-only projection. Add the settings surface and capability-driven UI next. Enable each mutation only after isolated evidence proves that Todo清单 UI and sync semantics remain correct.

Maintain `Agent/IMPLEMENTATION_STATUS.md` while developing. Record current phase, evidence, capability matrix, risks, and whether any live user data was accessed. Never mark a capability complete based only on successful SQLite execution.

After application-code changes, follow the stop/build/restart/path-verification workflow in root `AGENTS.md`.
