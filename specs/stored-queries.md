# Stored queries

A stored query is a named, reusable, optionally parameterized search. Stored-query files
are meant to be shared through git.

## Location `[not-yet-implemented]`

The file is `~/.grdev.glq-cli/queries.json`, unless overridden by `--queries-file <path>`
or `GLQ_QUERIES_FILE` — so a team can point at a shared file in a repository.

## Schema `[not-yet-implemented]`

| Field | Required | Meaning |
|---|---|---|
| `name` | Yes | Unique slug |
| `query` | Yes | Graylog/Lucene query, may contain `{{param}}` placeholders |
| `description` | No | Free text |
| `range` *or* `from`/`to` | No | Time range, as in [search.md](search.md) |
| `streams`, `fields`, `limit`, `sort` | No | As in [search.md](search.md) |
| `tags` | No | Labels for filtering |
| `profile` | No | Default profile; `--profile` overrides it |
| `params` | No | Parameter names, each with optional `default` and `description` |

A stored-query file never contains credentials.

## Parameters `[not-yet-implemented]`

Placeholders use `{{name}}`. `query run` fails with exit code `1` when a parameter without
a default is missing, and lists every missing one. Values are escaped so they cannot break
out of their Lucene clause; a value with spaces or special characters is quoted.

## Commands `[not-yet-implemented]`

| Command | Does |
|---|---|
| `glq query save <name> <query> [search options] [--description <text>] [--tag <t>]…` | Saves or replaces a query |
| `glq query list [--tag <t>]` | Lists queries, optionally filtered by tag |
| `glq query show <name>` | Prints one query |
| `glq query edit <name>` | Opens the entry in `$VISUAL`, then `$EDITOR`, then `notepad` / `vi`, and validates it on save |
| `glq query rm <name>` | Deletes a query |
| `glq query run <name> [--param k=v]… [search options]` | Runs it; any search option given overrides the stored value for this run only |
| `glq query export [<name>…]` | Writes queries as JSON to stdout |
| `glq query import <file>` | Adds queries from a file; rejects any file containing token-like keys |

```
glq query save svc-errors 'source:{{service}} AND level:<=3' --range 1h --description "Errors for a given service"
glq query run svc-errors --param service=payments --profile prod -o csv > out.csv
```

Running against a production profile with a limit above the confirmation threshold needs
`--yes` or interactive confirmation — see [privacy.md](privacy.md).
