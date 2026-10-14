# AGENTS.md — grdev standard

The conventions every grdev repository follows.

**This file is identical in every repository and is never edited locally.** It is synced
from the [`agentic`](https://github.com/greek-developer/agentic) repository and overwritten
on every sync — local changes are lost.

To get a new version, pull the latest from `agentic` and write it over the local copy:

```bash
gh api repos/greek-developer/agentic/contents/src/AGENTS.md?ref=develop --jq '.content' | base64 -d > AGENTS.md
```

Anything specific to one project — its build commands, layout deviations, stack rules,
prohibitions, decisions — goes in that project's `BRIEF.md`. If a rule belongs in every
project, change it upstream in `agentic` and re-sync.

---

## Purpose

This file is for AI agents working on this repository. Read the documents listed below
before making any changes.

## BRIEF / README / ARCHITECTURE

A project's context lives in three documents, split by who reads them. Agentic development
is where this is heading — agents now do the reading that keeps a project moving — so the
technical documents are written for agents, and the one document still meant for people is
kept genuinely for people.

| Document | Written for | Holds | When to read |
|---|---|---|---|
| [README.md](README.md) | **People** | What the project does, in plain language — and links out to anything promotional: demo videos, blog posts, screenshots. Not a build manual; nothing an agent needs to do the work belongs here | — |
| [BRIEF.md](BRIEF.md) | Agents — ground truth | High-level **project** documentation: what the project is, its goals, constraints, and past decisions | Start of every session |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | Agents | High-level **technical** documentation: project layout, data model, lifecycle, key design decisions | Before touching source |

Keep each to its lane: project intent that shapes decisions goes in `BRIEF.md`, how the
system is built goes in `ARCHITECTURE.md`, and `README.md` stays the human front door.

For nested context on `AGENTS.md` & `BRIEF.md`, **the closer one wins** — a subfolder's file complements the root, and
overrides it only where it deliberately means to:

### BRIEF.md protocol

`BRIEF.md` is the counterpart to this file: **everything project-specific lives there**,
because this file is overwritten on every sync. It is the only place a project records
what makes it different.

| Rule | Detail |
|---|---|
| Read it first | At the start of every session, read [BRIEF.md](BRIEF.md) — it holds project context, goals, and past decisions. Treat it as ground truth. |
| It wins on specifics | Where `BRIEF.md` describes this project and `AGENTS.md` describes the general rule, follow `BRIEF.md`. It cannot override a **Never** in this file — raise the conflict instead. |
| Never edit AGENTS.md | A project-specific rule goes in `BRIEF.md`. A rule for every project changes upstream in `agentic`. |

### What belongs in BRIEF.md

| Section | Holds |
|---|---|
| `## Overview` | What the project is and who it's for — one or two paragraphs |
| `## Build & run` | Clearly state **how to build**, **how to run locally**, and **how to run all tests** — plus any environment variables and local services to start first |
| `## Layout` | Deviations from the standard layout, and what any project-specific folder is for |
| `## Stack` | Rules that apply to this project's stack only — namespaces, async, DI, serialization — plus any dependency chosen over the standard one, with the reason |
| `## Contributing` *(optional)* | Conventions and a map of where each concern lives — **only the points that differ from `AGENTS.md`**. Omit the section entirely when nothing does; never restate a rule the standard already covers |
| `## Tests` | Which test layers this project actually has |
| `## Never` | Prohibitions that will break *this* project and that an agent could not infer from the code. Be specific and short. |
| `## Decisions` | The running decision log — see below |

### Decision log

| Rule | Detail |
|---|---|
| When to log | After any response in which a decision was made — architectural, technical, or directional |
| Where | Append to the `## Decisions` section, dated with today's date |
| What to record | **What** was decided, not why |
| Superseding | If a new decision supersedes an earlier one, delete the earlier entry |
| When unsure | If a decision is ambiguous, ask before logging it |

## Repository layout


| Path | Contains |
|---|---|
| `./src` | **All** application source code |
| `./tests` | Test projects |
| `./scripts` | Helper/automation scripts, written in **PowerShell (`.ps1`)** |
| `./docs` | Documentation |
| `./specs` | How the product works, one spec per domain — see [Spec protocol](#spec-protocol) |
| `./tasks` | Task management — see [Task workflow](#task-workflow) |
| `./release` | Build output — compiled artifacts and script outputs intended for distribution. **Always gitignored.** |


## Versioning

Versions are computed by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)
from [`version.json`](version.json) plus the git height, and the patch bumps with every commit — see
[README.md](README.md#versioning).

| Action | How |
|---|---|
| Change major/minor | Hand-edit the `version` field in `version.json` — **`major.minor` only** |
| Bump patch | Automatic — it tracks git height |
| `nbgv set-version` | **Never run it** — drops this repo's `publicReleaseRefSpec` and `cloudBuild` settings |
| `<Version>` in `Directory.Build.props` / `.csproj` | **Never add it** — conflicts with the computed version |

`version.json` holds two components and no more — `"version": "2.5"`, never `"2.5.3"`. The
patch is the git height, so writing a third component pins the patch to a constant and
pushes the height into a fourth position, breaking the versioning scheme.

**Per-package versioning.** A project that ships as several independent deployments — for
example multiple NuGet packages released on their own cadence — can version each one
separately. Nerdbank.GitVersioning applies the nearest `version.json` up the folder tree,
so drop a `version.json` into each package's folder to give it its own `major.minor` and
git-height patch. Keep the root `version.json` for everything a package-level file doesn't
override.

| Rule | Detail |
|---|---|
| Use the bump tool | Any version bump must use the repository script at `src/scripts/bump-version.ps1`. Agents must copy the latest version into the local `scripts/bump-version.ps1` before running it. |
| Default bump | By default, bump the minor version. Only bump the major version when the task explicitly asks for it. |

## Production version file

Every app writes a **`ProductionVersion.json`** at build time, capturing the version and
the exact commit it was built from. It ships alongside the app and is the single source of
truth for "what is running here". Generate it during the build — never hand-edit it.

It contains at least these fields:

```json
{
  "version": "x.y.z",
  "commit": {
    "message": "the commit message of the commit that was built",
    "sha": "..."
  },
  "build": {
    "time": "{utc time of the build}"
  }
}
```

| Field | Value |
|---|---|
| `version` | The computed version (see [Versioning](#versioning)) |
| `commit.message` | Subject of the commit the build was cut from |
| `commit.sha` | Full SHA of that commit |
| `build.time` | Build timestamp in UTC (ISO 8601) |

How each app surface exposes it:

| App type | Exposes it via |
|---|---|
| **Web API** | A `GET /api/diagnostics/version` endpoint that returns the `ProductionVersion.json` contents as JSON |
| **Web interface** | The version, short commit SHA, and build time shown in the site footer (see below) |
| **CLI** | A `version` command that reads the file and prints the fields (see below) |

A **web interface** shows the identity in its footer as:

```
{version} - {commit-sha} - {build-time}
```

where `{commit-sha}` is the **first 8 characters** of the commit SHA — enough to identify the
commit at a glance without cluttering the footer. `ProductionVersion.json` and the
`/api/diagnostics/version` payload still carry the full SHA.

The CLI `version` command prints:

```
version: ...
commit-text: ...
commit-sha: ...
build-time: ...

To update: dotnet tool update -g grdev.<name>-cli
```

The update line is separated from `build-time` by one blank line — it's an action, not a
build fact.

## Branching & workflow

| Branch | Policy |
|---|---|
| `develop` | Where work happens. Branch off it for substantial features. |
| `release/{environment}` | Deploy branches — pushing triggers a deploy to that environment. |
| `release/production` | Deploy at production  |

**Commit or push only when the user explicitly asks.** Never run `git push` autonomously.

### Working on develop

`develop` keeps a linear history. Never merge into it in a way that creates a merge commit.

| Rule | Detail |
|---|---|
| Rebase | Rebase the feature branch onto `develop` before it lands — never merge `develop` into it |
| Fast-forward only | The integration must fast-forward. If it can't, rebase again. |
| Squash | Collapse the feature branch into a single commit as it lands |
| Pull requests | Expected on team projects. Committing directly to `develop` is acceptable on small projects. |

**GitHub enforcement.** The default branch (`develop`) carries a ruleset that **requires
linear history** while **leaving force pushes allowed**. Linear history is enforced on every
update — normal *and* forced — so history can be rewritten (`git push -f`) but never in a way
that introduces a merge commit; a non-linear update is rejected. No bypass is needed: the
point is to keep linear history invariant through rewrites, not to escape it.

Apply it with a ruleset carrying only the `required_linear_history` rule (omit the
`non_fast_forward` rule, which is what would block force pushes):

```bash
gh api -X POST repos/<owner>/<repo>/rulesets --input - <<'JSON'
{
  "name": "develop-linear",
  "target": "branch",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["~DEFAULT_BRANCH"], "exclude": [] } },
  "rules": [ { "type": "required_linear_history" } ]
}
JSON
```

### Detailed / Partial Release branches

A release branch is `release/` followed by the target environment. For complex
applications the path can be augmented with further segments to pin down exactly what is
being deployed and where:

```
release/{environment}/{version}/{tenant}/{microservice}
```

| Segment | Required | Example | Add it when |
|---|---|---|---|
| `{environment}` | **Always** | `production` | Always — it is the deploy target |
| `{version}` | Optional | `2.4` | Multiple versions run side by side |
| `{tenant}` | Optional | `acme` | The app is multi-tenant and tenants deploy independently |
| `{microservice}` | Optional | `billing-api` | Services are deployed separately rather than as one unit |

Use only the segments the project actually needs, always in that order.

| Shape | Branch |
|---|---|
| Single-tenant monolith | `release/production` |
| Versioned monolith | `release/production/2.4` |
| Multi-tenant microservices | `release/production/2.4/acme/billing-api` |

### Environments

| Environment | Purpose | Promoted from |
|---|---|---|
| `develop` | Local development environment — built and run locally, expected to be unstable | - |
| `staging` | Pre-production mirror — release-candidate verification against production-like data | `develop` |
| `uat` | User acceptance testing — business sign-off before release | `staging` |
| `production` | Live | `uat` |

### Releasing

**The default way to release is to fast-forward the release branch to the commit being
released.** A release branch always points at a commit that is part of `develop`, so what is
deployed is exactly what was built and verified there. Promotion moves the pointer along the
chain and creates no commits:

```
develop → release/staging → release/uat → release/production
```

```powershell
git switch release/staging
git merge --ff-only <commit>
```

| Rule | Detail |
|---|---|
| Fast-forward only | Use `--ff-only`. If the release branch can't fast-forward to the commit, it has deviated from `develop` after a hotfix — reconcile it as described under [Hotfixes](#hotfixes--per-deployment-commits), never by merging |
| Only `develop` commits | Release only commits that are on `develop`. Never commit to a release branch directly |
| Promote in order | Promote the commit that is already on the previous environment; production is promoted from `uat`, `uat` from `staging` |

### Hotfixes & per-deployment commits

Cherry-picking is the exception, reserved for special cases such as hotfixes. When a fix
must go out without the other commits waiting on `develop`, **cherry-pick that specific
commit onto the release branch**. The release branch is then allowed to deviate from
`develop`, and from that point it no longer fast-forwards from it.

**Reconciling a deviated branch.** The deviation is temporary. Bring the release branch back
in line by **force-resetting it to a commit on `develop`** — normally as part of the next
release, since that commit already contains the fix as its own `develop` commit:

```powershell
git switch release/production
git reset --hard <commit>
git push --force-with-lease origin release/production
```

This is the one place a force push to a release branch is expected. Use
`--force-with-lease`, and check that the commit you reset to contains every fix the branch
carries — a reset discards whatever it does not.

**Fixes still start on `develop`.** Commit the fix to `develop` first, then cherry-pick it
onto each release branch that needs it.

Where a deployment needs its own commit history, cut a **version branch** —
`release/production/2.5`. One branch per deployed version means a fix can target exactly
what is live without disturbing the next release.

| Situation | Do | Never |
|---|---|---|
| Normal release | Fast-forward the release branch to the commit | Cherry-pick, merge or rebase it |
| Hotfix that can't wait for the rest of `develop` | Commit to `develop`, then cherry-pick that commit onto the release branch | Commit to a release branch first |
| A hotfix that was already made on production | Cherry-pick the commit back to `develop` | Merge `release/production` back into `develop` |

**We never merge a release branch back.** A merge drags the whole branch's history and its
environment-specific state along with it; a cherry-pick moves exactly the one commit you
want. Cherry-picking a production hotfix back to `develop` is recovery from an
out-of-order fix — it is not the intended path, and the fix still has to reach every
environment that was skipped.

### Commit messages

The subject line must read as the continuation of *"If applied, this commit will …"* —
lowercase verb, imperative mood, short, single-line.

| ✅ Write | ❌ Not |
|---|---|
| `add deploy workflow` | `Add deploy workflow` |
| `fix session code validation` | `Fixed session code validation` |
| `move rules to src` | `Moves rules to src` / `Moving rules to src` |

## Development environment

**Windows is the default development environment.** Assume it unless a project's
`BRIEF.md` says otherwise. Several rules in this file follow from that:

| Consequence | Detail |
|---|---|
| Scripts | Helper and automation scripts are PowerShell (`.ps1`) — see [Repository layout](#repository-layout) |
| Git hooks | Still shell scripts run by `sh`, which Git for Windows ships. Write them as POSIX `sh` — not `bash`, not PowerShell |
| Line endings | CRLF, with one exception for the scripts `sh` executes — see [Coding conventions](#coding-conventions) |
| Executable bit | Not meaningful on a Windows filesystem, and git-bash reports every file as executable. Never use it as a runtime switch — see [Multiple checks per hook](#multiple-checks-per-hook) |

This describes the machine a developer sits at, not where the software runs. Cross-platform
code is still expected, and CI may well be Linux.

**Preferred terminal:** any agent should use PowerShell (`pwsh`) as the default terminal when
available, and prefer it over `cmd`, Bash, or other shells unless a project-specific rule or
required tool explicitly calls for another shell.

## Coding conventions

| Convention | Detail |
|---|---|
| Source location | Keep all source under `./src`; keep build artifacts out of source control |
| Line endings | Source and text files use **CRLF**, and end with a trailing newline. Set `end_of_line = crlf` and `insert_final_newline = true` in `.editorconfig` so it is enforced. One exception — see below |
| Local style wins | Match surrounding code — naming, formatting, comment density |
| Change size | Prefer small, reviewable changes |
| Framework defaults | Prefer the framework's or library's default components and behaviours; keep custom styling and hand-rolled layouts to a minimum, and only where there's a stated reason |
| Secrets | Never commit service-account keys or secrets — use environment configuration |

**The exception: scripts `sh` executes.** A git hook checked out with CRLF dies with
`bad interpreter: /bin/sh^M` — the shebang keeps the carriage return. Pin those files to LF
in `.gitattributes` at the repository root, which holds regardless of `core.autocrlf`:

```gitattributes
scripts/.githooks/** text eol=lf
```

Stack-specific rules that apply to only one project go in that project's `BRIEF.md`.

## Preferred packages

**Prefer the built-in over the third-party.** If the BCL or a `Microsoft.Extensions.*`
package covers the need, use it — a third-party dependency has to earn its place by doing
something the platform genuinely can't.

Reach for a new dependency only when nothing below and nothing built in covers the need.

| Purpose | Package | Strength |
|---|---|---|
| Unit testing | `xunit` (+ `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) | **Required** |
| Code coverage | `coverlet.collector` | Strongly suggested |
| CLI argument parsing | [`System.CommandLine`](https://learn.microsoft.com/dotnet/standard/commandline/) | Strongly suggested |
| JSON | `System.Text.Json` | Strongly suggested — never add `Newtonsoft.Json` to new code |
| Frontend | Vue 3 + Vite + Sass | Suggested — not enforced |

A package choice that applies to every project belongs here — change it upstream. One
project's own dependency choice goes in its `BRIEF.md`.

## Project & solution defaults

| Setting | Value | Strength |
|---|---|---|
| `Nullable` | `enable` | **Required** |
| `ImplicitUsings` | `enable` | **Required** |
| `TreatWarningsAsErrors` | `true` | Strongly suggested |
| Solution format | `.slnx` | Strongly suggested — prefer it over `.sln` for new solutions |

Set the shared ones once in `Directory.Build.props` rather than repeating them per project.

### InternalsVisibleTo

A project exposes its internals to its own unit-test project and nothing else. Declare it
in the `.csproj` — the assembly name follows the test-project naming convention:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="$(AssemblyName).UnitTests" />
</ItemGroup>
```

`$(AssemblyName)` keeps it correct if the project is ever renamed. Don't hand-write the
name, and don't widen the grant beyond the one test assembly.

## Packaging & distribution

**Ship public, distributable CLI apps as .NET global tools** unless the target environment
rules it out — for example a platform the tool can't run on, or a deployment that can't
invoke `dotnet tool`. Where that applies, say so in this file and describe what ships
instead.

This applies only to CLIs that are actually distributed. An internal or non-distributable
CLI — a build helper, a one-off maintenance tool, anything that never leaves the repo — is
under no obligation to be packaged as a .NET global tool.

### Naming

One name runs through the whole chain, so nothing has to be looked up:

| Thing | Rule | Example |
|---|---|---|
| Repository | The tool's name with a **`-cli`** suffix | `git-guard-cli` |
| `PackageId` | `grdev.` + the repository name | `grdev.git-guard-cli` |
| `ToolCommandName` | The repository name **without** the `-cli` suffix | `git-guard` |

Lowercase throughout, hyphen-separated, and no `grdev` prefix on the command itself — the
user types `git-guard`, not `grdev-git-guard`.

The suffix earns its place on the repository and the package, where a bare name would be
ambiguous among a shelf of them, and is dropped from the command, where the user is already
naming one specific tool. Deriving all three from one word is what stops a package called
`grdev.gitguard` from living in a repository called `git-guard`.

**A published `PackageId` cannot be renamed.** Only new versions of new ids, with the old
package left behind — so get the name right before the first publish, and deprecate the old
id on nuget.org pointing at the replacement if it ever changes.

### Where a tool stores things

Everything a tool keeps in the user's profile — configuration, credentials, caches, tokens —
goes in **one hidden folder named after the package id**:

```
%USERPROFILE%\.<PackageId>\
```

So `grdev.youtube-cli` keeps everything under `~/.grdev.youtube-cli/`, and files inside it
are named for what they are, not for the tool: `client-secret.json`, not
`grdev.youtube-client-secret.json`. The folder already says whose it is.

| Rule | Detail |
|---|---|
| One folder per tool | Never scatter files across the profile root, and never share a folder between tools |
| Named for the package | `.<PackageId>` exactly — the same name derived in [Naming](#naming) |
| Hidden | The leading dot keeps a user's home directory readable |
| Not `AppData` | One predictable location per tool, on every platform, that a user can find and back up |

The point is that a user can see everything a tool put on their machine, and delete it, in
one place. A credential file loose in the profile root and a token cache buried in `AppData`
are the same tool's state in two places the user will never associate with each other.

**Moving this path strands existing users.** Their configuration and their granted
credentials stay where they were, and the tool silently behaves as if it had never been set
up. If a path must change, move the existing folder as part of the change rather than leaving
the user to discover it.

| Property | Value |
|---|---|
| `PackAsTool` | `true` |
| `ToolCommandName` | See [Naming](#naming) |
| `PackageId` | See [Naming](#naming) |
| `PackageOutputPath` | Under `./release` — the repo's gitignored output directory |

Package metadata (`Title`, `Description`, `Authors`, `RepositoryUrl`,
`PackageLicenseExpression`, `PackageProjectUrl`, `PackageReadmeFile`) is filled in on the
same project, not left to defaults.

### Config command

Every tool that reads a config or settings file — as almost all do, since that file lives in
the one folder described above — exposes it through a `config` command. This is a minimum,
not a ceiling: `config` must carry at least the two subcommands below, and is free to carry
more — additional settings worth exposing individually, or a whole sub-area of state the tool
manages, are welcome under `config` too, at the implementer's judgment. Nothing here caps what
`config` may grow into; it only sets the floor every tool starts from:

```
<name> config path   Print the resolved path to the config file
<name> config edit   Open the config file in $VISUAL / $EDITOR
```

| Subcommand | Detail |
|---|---|
| `config path` | Prints the resolved config file path and nothing else, so a script or agent can locate it without knowing the tool's storage convention |
| `config edit` | Opens the file in `$VISUAL`, falling back to `$EDITOR`, falling back to a sane platform default (`notepad` on Windows, `vi` elsewhere). If the file does not exist yet, `config edit` creates it — a template with the right shape, not a valid config — rather than opening a blank file or failing |

### Shipping the agent guide

A CLI is driven by agents as much as by people, and an agent needs a guide it can *load* —
not prose it has to infer from `--help`. **Ship that guide inside the tool** and let the tool
print it, so installing the package is the only prerequisite:

```
dotnet tool install --global grdev.<name>-cli
<name> skill > ~/.claude/skills/<name>/SKILL.md
```

| Rule | Detail |
|---|---|
| One source of truth | The guide lives in the repository at `skills/<name>/SKILL.md` and is embedded in the assembly, so the file that ships is the file that is versioned |
| A `skill` command | `<command> skill` writes the guide to stdout and **nothing else**, so redirecting it produces a usable file. Under `--json`, the same content plus the path a harness expects it at |
| What it covers | How to drive the tool without a human, the exit-code contract, and — most importantly — what the agent must **not** attempt |
| Guard the embedding | A resource that stops being embedded fails silently: the command prints nothing useful and no build breaks. Assert in a test that the output opens with skill frontmatter |

This matters most where the repository is private or the docs are elsewhere: the package
becomes self-teaching, and an agent needs no access to either.

Pin console output to **UTF-8** in the entry point. A Windows console otherwise writes in a
legacy code page, which silently transliterates em dashes, arrows and emoji — fine on screen,
corrupted the moment anything is redirected to a file or piped into a parser.

## Code style

Every repository has a `.editorconfig` at its root, committed. Generate the baseline —
don't hand-write one:

```powershell
dotnet new editorconfig
```

That emits the full .NET rule set (naming, spacing, `var` preference, analyzer
severities). Adjust it; don't start from scratch.

`.editorconfig` on its own only advises the IDE. To make it binding:

| Mechanism | Does | Set it |
|---|---|---|
| `EnforceCodeStyleInBuild` | Runs the `IDExxxx` code-style analyzers during `dotnet build`, not just in the editor | `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>` in `Directory.Build.props` |
| `TreatWarningsAsErrors` | Promotes those warnings to build failures | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` |
| `dotnet format` | Formats and fixes violations in place | Run it before committing |
| `dotnet format --verify-no-changes` | Fails without modifying anything — the form for hooks and CI | Exits non-zero if anything is unformatted |

Per-rule severity lives in `.editorconfig`
(`dotnet_diagnostic.IDE0090.severity = warning`) — that is what decides which rules
actually break the build.

## .gitignore

Every repository has a `.gitignore` at its root. Generate it — don't hand-write one:

```powershell
dotnet new gitignore
```

This is the Microsoft-maintained .NET ignore file: `bin/`, `obj/`, user files, tool
output, IDE noise. Append project-specific entries at the bottom rather than editing what
it ships with.

Always add `release/` — build output is never committed. Any repository with Node tooling
also ignores `node_modules/` — it is restored from the lockfile with `npm ci`, never
committed. (The .NET ignore file does not cover it, so add it explicitly.)

## Pre-commit hooks

Checks that run locally before a commit is created, so unformatted or broken code never
reaches a branch. Git calls these **hooks**; the one that runs before a commit is the
`pre-commit` hook.

Hooks in `.git/hooks/` are not tracked by git, so they can't be shared. Keep them under
`./scripts/.githooks` — they are scripts — and point git at that directory, once per
clone:

```powershell
git config core.hooksPath scripts/.githooks
```

Add that call to a setup script in `./scripts` so a fresh clone is one command away from
being configured.

| Hook | Runs | Typical check |
|---|---|---|
| `pre-commit` | Before the commit is created | `dotnet format --verify-no-changes`, fast unit tests |
| `commit-msg` | After the message is written | Subject-line convention (see [Commit messages](#commit-messages)) |
| `pre-push` | Before a push | Full build and test suite |

Keep `pre-commit` fast. Anything slower than a few seconds gets bypassed with
`--no-verify`, and a hook people routinely skip is worse than no hook — put the slow
checks in `pre-push` or CI instead.

### Hook file format

A hook is a **single executable script named exactly after the hook** — `pre-commit`, not
`pre-commit.sh`. The interpreter comes from the shebang; exit `0` to proceed, non-zero to
abort. The `.sample` files git ships in `.git/hooks/` are inert precisely because of the
suffix.

Commit hooks with the executable bit set, or they won't run on Linux or macOS:

```powershell
git update-index --chmod=+x scripts/.githooks/pre-commit
```

Hooks are also the one place LF is required rather than CRLF — see
[Line endings](#coding-conventions).

### Multiple checks per hook

Git runs **one file per hook** — it will not iterate a directory. To run several checks,
make the hook a dispatcher over a `.d` folder:

```
scripts/.githooks/
├── pre-commit          ← the dispatcher git runs
└── pre-commit.d/
    ├── 10-format
    └── 20-tests
```

```sh
#!/bin/sh
# Run every script in pre-commit.d/ in filename order; report all failures.
dir="$(dirname "$0")/pre-commit.d"
[ -d "$dir" ] || exit 0

status=0
for hook in "$dir"/*; do
    [ -x "$hook" ] || continue
    echo "→ $(basename "$hook")"
    "$hook" "$@" || status=1
done
exit $status
```

Numeric prefixes fix the order, and each check is a file you can add or remove on its own.

**Disabling a check: do not rely on `chmod -x`.** The dispatcher's `[ -x "$hook" ]` test is
the right guard for skipping non-scripts, but it is not a usable switch on Windows —
git-bash reports every file as executable, so a check "disabled" that way still runs. Gate
anything that needs to be switchable on a git config value, inside the check itself:

```sh
if [ "$(git config --bool --get myproject.hooks.format)" != "true" ]; then
    echo "  skipped - enable with: git config myproject.hooks.format true"
    exit 0
fi
```

That behaves identically on every platform, is set per clone, and the skip message tells the
reader how to turn it on. A check the codebase cannot pass **yet** belongs behind such a
switch rather than committed in a failing state — a hook that always fails only teaches
people to reach for `--no-verify`.

> Capture the exit code before piping — `dotnet format ... | tail` reports the exit code
> of `tail`, not of `format`, so a piped check always passes. Use `$LASTEXITCODE` in
> PowerShell or `${PIPESTATUS[0]}` in bash, or don't pipe.

## Spec protocol

Specs describe **how the product works** — its target functionality — not how it is built.
They are the standing functional map of the product: one spec per **vertical slice** (or
domain), written in the present tense, living under `./specs`. Implementation belongs to
[tasks](#task-workflow); specs say only what the product does.

Specs come first. Write or adjust the spec before deciding how to build it — the spec is
the target, the task is the route to it.

| Rule | Detail |
|---|---|
| One per slice | A spec covers a vertical slice or domain (`specs/checkout.md`, `specs/auth.md`), not a single request or change |
| Describes behavior | Write what the product *does*, in the present tense. Say nothing about implementation — that is the task's job |
| Spec first | Write or update the spec before choosing an approach. The spec defines the target; the task implements it |
| Mark the unbuilt | Behavior that is planned but not yet built is tagged **`[not-yet-implemented]`** on the section that describes it — never let a reader assume a planned behavior already works. Drop the tag when the behavior ships |
| Keep it in sync | A task that changes how the product works updates the affected spec in the same change, so specs never drift from reality |
| Name the spec in the task | Every task names the spec(s) it touches, so the "update the spec when you change it" rule is checkable |
| Report contradictions | If a task contradicts a spec — including a not-yet-implemented one — **stop and report it for a decision before proceeding.** Never silently override a spec |

## Task workflow

Tasks are **how the product is built** — the logical steps that implement a
[spec](#spec-protocol). Every task names the spec(s) it touches.

| Path | Holds | Status |
|---|---|---|
| `tasks/tasks.md` | Quick capture. A single running checklist; add items as they come up, tick them as they complete. | — |
| `tasks/wishlist` \* | New ideas, basic selection only | — |
| `tasks/pending` \* | Approved tasks; refinement and implementation tracking | `refine`, `develop` |
| `tasks/completed` \* | Finished tasks | `done` |

\* Optional — add these folders only when `tasks.md` alone stops being enough.

## Test approach

Layers a project actually has are listed in its `BRIEF.md`; the naming below is the
convention wherever they exist.

| Project | Covers |
|---|---|
| `tests/<Project>.UnitTests` | Pure logic, no I/O |
| `tests/<Project>.IntegrationTests` | In-process service/API coverage |
| `tests/<Project>.E2ETests` | Browser-driven UI flows against a live app; keep the app black-box |

**xUnit is required** — see [Preferred packages](#preferred-packages). Everything else
(assertion library, AAA layout, `MethodName_Scenario_ExpectedResult` naming) is inferable
from any existing test file — open one rather than documenting it here.
