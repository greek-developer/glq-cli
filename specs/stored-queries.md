# Stored queries

A stored query is a named, reusable, optionally parameterized search.

## Where they live `[not-yet-implemented]`

Stored queries live in `~/.grdev.glq-cli/config.json` ([profiles.md](profiles.md)), in one
of two places:

| Scope | Location | Available on |
|---|---|---|
| Shared | Top-level `queries` | Every profile |
| Profile-only | `profiles.<name>.queries` | That profile only |

Names are unique within a scope. When a profile-only query has the same name as a shared
one, the profile-only query wins on that profile. Queries are shared between people with
`query export` / `query import`.

## Schema `[not-yet-implemented]`

The query's name is its key.

| Field | Required | Meaning |
|---|---|---|
| `query` | Yes | Graylog/Lucene query, may contain `{{param}}` placeholders |
| `description` | No | Free text |
| `range` *or* `from`/`to` | No | Time range, as in [search.md](search.md) |
| `streams`, `fields`, `limit`, `sort` | No | As in [search.md](search.md) |
| `tags` | No | Labels for filtering |
| `params` | No | Parameter names, each with optional `default` and `description` |

## Parameters `[not-yet-implemented]`

Placeholders use `{{name}}`. `query run` fails with exit code `1` when a parameter without
a default is missing, and lists every missing one. Values are escaped so they cannot break
out of their Lucene clause; a value with spaces or special characters is quoted.

## Commands `[not-yet-implemented]`

Every command works on the active profile's view: its profile-only queries plus the shared
ones.

| Command | Does |
|---|---|
| `glq query save <name> <query> [search options] [--description <text>] [--tag <t>]… [--profile-only]` | Saves or replaces a query — shared, or on the active profile with `--profile-only` |
| `glq query list [--tag <t>]` | Lists queries with their scope, optionally filtered by tag |
| `glq query show <name>` | Prints one query |
| `glq query edit <name>` | Opens the entry in `$VISUAL`, then `$EDITOR`, then `notepad` / `vi`, and validates it on save |
| `glq query rm <name>` | Deletes a query |
| `glq query run <name> [--param k=v]… [search options]` | Runs it; any search option given overrides the stored value for this run only |
| `glq query export [<name>…]` | Writes queries as JSON to stdout — never tokens or any other profile setting |
| `glq query import <file> [--profile-only]` | Adds queries from a file as shared, or to the active profile; rejects any file containing token-like keys |

```
glq query save svc-errors 'source:{{service}} AND level:<=3' --range 1h --description "Errors for a given service"
glq query run svc-errors --param service=payments --profile prod -o csv > out.csv
```

Running against a production profile with a limit above the confirmation threshold needs
`--yes` or interactive confirmation — see [privacy.md](privacy.md).
