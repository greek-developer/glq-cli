# Tool surface

Commands every grdev CLI carries, and the process contract.

## `glq version` `[not-yet-implemented]`

Prints the build identity from `ProductionVersion.json`:

```
version: ...
commit-text: ...
commit-sha: ...
build-time: ...

To update: dotnet tool update -g grdev.glq-cli
```

The Graylog server version is reported by `glq profile test`, not here.

## `glq config` `[not-yet-implemented]`

| Command | Does |
|---|---|
| `glq config path` | Prints the resolved path of `config.json` and nothing else |
| `glq config edit` | Opens `config.json` in `$VISUAL`, then `$EDITOR`, then `notepad` / `vi`; creates a template with the right shape first if the file is missing |

## `glq skill` `[not-yet-implemented]`

Prints the agent guide — how to drive `glq` unattended, the exit-code contract, and what an
agent must not attempt — to stdout and nothing else. Under `--json`, the same content plus
the path a harness expects it at.

```
glq skill > ~/.claude/skills/glq/SKILL.md
```

## Exit codes `[not-yet-implemented]`

| Code | Meaning |
|---|---|
| `0` | Success, including zero results |
| `1` | Usage or configuration error |
| `2` | Authentication or authorization error (401/403) |
| `3` | Network or TLS error |
| `4` | Graylog API or query error |
