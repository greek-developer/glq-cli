# BRIEF.md — glq-cli

## Overview

`glq` — a command-line tool, distributed as a .NET global tool per the grdev standard.
What it does and who it's for is not written down yet; fill this in with the first spec.

## Build & run

Nothing to build yet — the repository holds only the agent standard and this brief.

## Layout

Nothing deviates from the standard layout. None of the standard folders exist yet; create
each one when there is something to put in it.

## Stack

Names, derived from the repository name per the standard's Naming rules:

| Thing | Value |
|---|---|
| `PackageId` | `grdev.glq-cli` |
| `ToolCommandName` | `glq` |
| Profile folder | `~/.grdev.glq-cli/` |

## Tests

None yet.

## Never

Nothing project-specific yet.

## Decisions

### 2026-09-24

- Adopted the grdev standard: `AGENTS.md` is synced verbatim from
  `greek-developer/agentic` (`src/AGENTS.md` on `develop`) and never edited locally.
- `CLAUDE.md` holds only `@AGENTS.md`, so Claude Code loads the standard.
- `.editorconfig` and `.gitattributes` are taken from `agentic` — CRLF on checkout, final
  newline — until a .NET project exists and `dotnet new editorconfig` replaces the former.
