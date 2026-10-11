# Tooling, Skills, And MCP

This integration primarily needs local filesystem, shell, Git, .NET, PowerShell, process inspection, and Windows UI verification. Do not add an MCP dependency merely because it exists.

## Required Agent Capabilities

- Read/write files with patch-aware editing.
- Run PowerShell commands and long-running builds/tests.
- Search with `rg` and inspect Git history/diffs.
- Inspect Windows processes, executable versions, registry, and network listeners.
- Run .NET 10 SDK and xUnit tests.
- Start and verify the canonical DeskBox Debug executable.

The agent must support periodic progress updates and continue through implementation, verification, and restart rather than stopping at a plan.

## Recommended Skills

- `deskbox-todo-qingdan` (this skill): mandatory for this feature.
- A general code-review skill or review mode: useful after each mutation phase to audit data loss, concurrency, lifecycle, privacy, and missing tests.
- An OpenAI/Codex documentation skill only when configuring the target coding agent itself; it is not needed for product implementation.

No Figma, image generation, documents, presentations, spreadsheets, or web-site skill is required. The existing WinUI design must be preserved and refined in code.

## MCP Recommendations

### Local Windows/.NET MCP or shell: required capability

Use the native shell tools already available to the agent. A dedicated MCP is optional if it provides equivalent process, filesystem, PowerShell, and test control. It must not upload live Todo data.

### GitHub MCP: optional

Use only if the development workflow needs remote issues, branches, or PRs. Local Git is sufficient for implementation. Do not push, create PRs, or mutate remote state unless the user explicitly requests it.

### Web search MCP: optional and read-only

Use to verify public Todo清单 release notes or documentation. Public search cannot prove private schema/write compatibility. Prefer official Todo清单 sources when available.

### Browser/UI automation: recommended if available

Windows-native UI automation (WinAppDriver, Appium Windows driver, FlaUI, or an equivalent local tool) can improve repeatability for DeskBox and Todo清单 UI checks. Playwright alone is not sufficient for native WinUI/Electron desktop-window lifecycle unless the application exposes an appropriate automation endpoint.

Use UI automation only against isolated test data for mutations.

### SQLite inspection

Use a trusted local SQLite tool or the application-shipped module only inside an isolated inspection process. Open the live database read-only, avoid task-row queries, and account for WAL/SHM. Never install or execute an unreviewed remote MCP with access to the Todo data directory.

## Suggested Subagent Roles

When multi-agent support exists, useful bounded roles are:

- architecture reviewer: read-only review of repository abstraction and ownership
- compatibility reviewer: inspect fingerprint and downgrade behavior without task data
- test reviewer: identify missing failure/concurrency/privacy cases

The primary agent remains responsible for reading this skill, integrating findings, editing code, and running final verification. Do not delegate secrets or live mutation work.

## Minimal Agent Invocation

Point the development agent at:

```text
Agent/DEVELOPMENT_PROMPT.md
Agent/skills/deskbox-todo-qingdan/SKILL.md
```

For Codex-compatible environments, install or link the skill folder into the environment's skill search path if repository-local skills are not auto-discovered. Keep this repository copy canonical so future agents receive the same instructions.
