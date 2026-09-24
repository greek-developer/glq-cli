# BRIEF.md — glq-cli

## Overview

`glq` is a read-only command-line tool for querying Graylog through its REST API, for
backend, SRE and support engineers. It replaces ad-hoc `curl` calls and the unmaintained
community CLIs, which target legacy Graylog 2.x–4.x endpoints and have neither profiles nor
saved queries. Two capabilities define it: **named profiles**, so switching between
clusters (`dev`, `staging`, `prod`) is one flag, and **stored queries** — named,
parameterized searches, shared across profiles or kept to one, and passed
between people with `query export` / `query import`.

It searches and retrieves log messages and stream metadata, and nothing else. Graylog holds
customer identifiers and other personal data, so the security and privacy behaviour in
[specs/privacy.md](specs/privacy.md) is a requirement of v1, not an extra. Functional
behaviour is specified per slice under [specs/](specs/); the design is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); acceptance criteria and open questions are in
[tasks/tasks.md](tasks/tasks.md).

**Out of scope for v1:** aggregations, charts and dashboards (possibly v2); message
ingestion; a GUI or TUI; username/password login.

## Build & run

Nothing is built yet. Once the solution exists:

```powershell
dotnet build                                   # build
dotnet run --project src/Glq.Cli -- <args>     # run locally
dotnet test tests/Glq.Cli.UnitTests            # all tests that need no services
./scripts/itest.ps1                            # integration tests — starts Graylog in docker first
```

Integration tests need Docker. `scripts/itest.ps1` starts Graylog, OpenSearch and MongoDB
from the compose file in `tests/Glq.Cli.IntegrationTests`, seeds synthetic messages, and
runs the suite. They are opt-in and never part of `dotnet test` on the solution's default
run.

| Environment variable | Effect |
|---|---|
| `GLQ_PROFILE` | Selects the profile when `--profile` is absent |
| `GLQ_TOKEN` | Supplies the token directly — for CI |

## Layout

| Path | Status |
|---|---|
| `./src/Glq.Cli` | The tool — the only application project |
| `./skills/glq/SKILL.md` | The agent guide, embedded in the assembly and printed by `glq skill` |
| `./tests/Glq.Cli.IntegrationTests` | Also holds the `docker-compose.yml` for the local Graylog |
| Others | Standard; created when there is something to put in them |

## Stack

.NET global tool, `net10.0`.

| Thing | Value |
|---|---|
| `PackageId` | `grdev.glq-cli` |
| `ToolCommandName` | `glq` |
| Profile folder | `~/.grdev.glq-cli/` — `config.json` (profiles, tokens, queries), `cache/` |

- **CLI parsing:** `System.CommandLine`. **JSON:** `System.Text.Json` with
  `JsonNamingPolicy.SnakeCaseLower`, so file keys read `verify_tls`, `default_range`.
- **HTTP:** `HttpClient` via `IHttpClientFactory`; one typed client, never a generic one —
  see [ARCHITECTURE.md](docs/ARCHITECTURE.md).
- **Dependencies:** `RestorePackagesWithLockFile` is on and `packages.lock.json` is
  committed; CI restores with `--locked-mode` and runs
  `dotnet list package --vulnerable --include-transitive`, failing on any hit.
- **CI:** Linux, macOS and Windows — format check, build (warnings as errors), unit tests,
  vulnerability scan.

## Tests

| Layer | Covers |
|---|---|
| `tests/Glq.Cli.UnitTests` | Config and profile resolution, token resolution, query templating and Lucene escaping (including injection attempts), masking (Luhn, deterministic pseudonyms), formatters, exit-code mapping, and both search backends against a mocked `HttpMessageHandler` — 401, 403, 404 legacy fallback, timeouts, TLS errors |
| `tests/Glq.Cli.IntegrationTests` | Both backends against a real local Graylog in docker — opt-in via `scripts/itest.ps1` |

A token-leak test captures every stdout, stderr and log line across the command surface and
asserts the test token appears in none of them. The `skill` output is asserted to open with
skill frontmatter. Validate against staging before pointing the tool at production.

## Never

| Never | Because |
|---|---|
| Write a token anywhere but `config.json` | It is the one owner-only file that holds secrets |
| Create or accept `config.json` readable by anyone but its owner | It holds plaintext tokens |
| Keep config or secrets outside `~/.grdev.glq-cli/` — no keyring, no second file, no path override | One folder holds all of it |
| Let a token reach any output — logs, errors, `--raw`, debug HTTP traces, crash traces | Redact `Authorization` everywhere; the token-leak test enforces it |
| Output an unmasked card-like number (Luhn-valid 13–19 digits) | Not even with `--no-mask` |
| Cache message content locally | Only stream metadata and the server version may be cached |
| Add any Graylog call beyond search, streams and system info, or a generic "call any endpoint" command | The tool is read-only by construction |
| Add upload, webhook, share or any network output | Results go only to stdout or files the user redirects to |
| Commit real log data | Test fixtures are synthetic only |
| Disable TLS verification silently | `--insecure` / `verify_tls = false` warn on every run |
| Put a token in `query export` output | Exports are shared; `query import` rejects files with token-like keys |

## Decisions

### 2026-09-24

- Adopted the grdev standard: `AGENTS.md` is synced verbatim from
  `greek-developer/agentic` (`src/AGENTS.md` on `develop`) and never edited locally.
- `CLAUDE.md` holds only `@AGENTS.md`, so Claude Code loads the standard.
- `.editorconfig` and `.gitattributes` are taken from `agentic` until a .NET project exists
  and `dotnet new editorconfig` replaces the former.
- `glq` is a .NET global tool — `grdev.glq-cli`, command `glq` — not Python or Go.
- Configuration is JSON, not TOML.
- All user state lives in `~/.grdev.glq-cli/`, not `$XDG_CONFIG_HOME/glq/`.
- One file, `~/.grdev.glq-cli/config.json`, holds all config and secrets: profiles, their
  tokens, stored queries, masking settings. No OS keyring, no separate queries file, no
  `--queries-file` / `GLQ_QUERIES_FILE`.
- Tokens are stored in plaintext; `config.json` is owner-only (`0600`, user-only ACL on
  Windows) and `glq` refuses to run when it is not.
- Stored queries are shared (top-level `queries`) or profile-only
  (`profiles.<name>.queries`); profile-only wins on a name clash. People share queries via
  `query export` / `query import`.
- `glq version` prints the standard CLI version block only; the server version is reported
  by `glq profile test`.
- `glq` carries the standard `config path | edit` and `skill` commands.
- `query edit` uses the same editor fallback as `config edit`: `$VISUAL`, `$EDITOR`, then
  `notepad` / `vi`.
- User documentation (install, token setup, config and query examples, security notes)
  lives in `docs/`; `README.md` stays a plain-language front door that links to it.
- Helper scripts, including the integration-test runner, are PowerShell; no `Makefile`.
- CI runs on Linux, macOS and Windows.
