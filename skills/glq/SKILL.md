---
name: glq
description: Search Graylog logs read-only with the glq CLI - ad-hoc Lucene searches, stored parameterized queries, and stream listings across named profiles (dev, staging, prod). Use when the user wants log messages, errors or events from Graylog, wants to run or save a named log query, or asks which Graylog streams exist. Also use when a glq command has failed and its exit code needs interpreting.
---

# glq — read-only Graylog queries

`glq` searches Graylog through its REST API. It only reads: it can search messages, list
streams and read the server version. It cannot change anything in Graylog.

## Before you start

- A human sets up profiles and tokens. Check what exists with `glq profile list -o jsonl`.
- Check a profile works with `glq profile test [<name>]` — it prints `auth: ok` and the
  server version, or fails with exit code `2` (auth) or `3` (network/TLS).
- Pick the target with `--profile <name>` on every command. Without it, `GLQ_PROFILE` and
  then `current_profile` decide — be explicit so you never query the wrong cluster.

## Searching

```
glq search '<lucene query>' --profile <name> --range 15m -o jsonl
glq search 'source:api-gateway AND http_status:>=500' --range 1h --fields timestamp,source,message --limit 200 -o jsonl
glq search 'level:<=3' --from 2026-09-24T08:00:00Z --to 2026-09-24T09:00:00Z --stream "API Gateway" -o jsonl
```

| Option | Meaning |
|---|---|
| `--range 30s\|15m\|2h\|1d\|1w` | Relative range ending now |
| `--from`/`--to` | Absolute ISO 8601 range; UTC unless an offset or `--tz <zone>` is given. Never combine with `--range` |
| `--stream <name or id>` | Repeatable. An ambiguous name is an error listing the IDs to use instead |
| `--fields a,b,c` | Fields to return (default: the profile's `default_fields`) |
| `--limit <n>` | Default 100; capped by `max_limit` |
| `--sort field:asc\|desc` | Default `timestamp:desc` |
| `-o table\|json\|jsonl\|csv` | Use `jsonl` or `json` when you parse the output |

Zero results is success (exit `0`, empty output in `jsonl`). Results go to stdout; notices,
warnings and errors go to stderr.

## Stored queries

```
glq query list -o jsonl                      # name, scope, params, query
glq query show <name>                        # full definition as JSON
glq query run <name> --param service=payments -o jsonl
glq query run <name> --range 6h --limit 50   # options override the stored values for this run
```

A missing parameter fails with exit `1` and names every missing one. Parameter values are
escaped by glq — pass them raw, never pre-quote them.

## Streams

```
glq streams list -o jsonl [--filter <text>]
```

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| `0` | Success, including zero results | — |
| `1` | Usage or configuration error | Read stderr; fix the arguments. Configuration problems are for the human |
| `2` | Authentication or authorization (401/403) | Stop and tell the human; the token or its permissions need fixing |
| `3` | Network or TLS error | Report it; do not retry with `--insecure` |
| `4` | Graylog API or query error | Usually a Lucene syntax error — fix the query |

## Privacy — what you must know

- Output is **not masked**: results are exactly what Graylog returns and may contain
  personal data. Do not paste it elsewhere unless the human asks for it.
- On a production profile glq prints `querying PRODUCTION Graylog …` to stderr, and a large
  `--limit` needs `--yes`.

## Never

- Never pass `--yes` to get past the production confirmation unless the human explicitly
  approved that run.
- Never pass `--insecure` to get past a TLS error — report the error instead.
- Never read, print, edit or copy `config.json` (`glq config path` shows where it is): it
  holds plaintext tokens. Never run `glq config edit` or `glq profile set-token`.
- Never put a token on a command line or in a file. `GLQ_TOKEN` is for CI, set by humans.
- Never write query results to files unless asked — they may contain personal data.
- Never try to reach Graylog endpoints other than through glq, and don't look for a way to
  write to Graylog: glq is read-only by design.
