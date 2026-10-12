# glq user guide

`glq` is a read-only command-line tool for searching Graylog. This guide covers installing
it, connecting it to Graylog, searching, saved searches, and how it protects tokens and
personal data.

## Install

`glq` is a .NET global tool. It needs the .NET 10 runtime.

```
dotnet tool install --global grdev.glq-cli
glq version
```

Update with `dotnet tool update --global grdev.glq-cli`.

For AI coding agents, install the bundled guide as a skill:

```
glq skill > ~/.claude/skills/glq/SKILL.md
```

## Create a Graylog token

`glq` signs in with a Graylog **access token**, never a password.

1. Ask a Graylog admin for a **dedicated, least-privilege user** for `glq` — one that can
   read only the streams you need. Don't use an admin account's token: anything the token
   can read, `glq` can print.
2. Sign in to Graylog as that user and open *System → Users and Teams*, then the user's
   *Edit tokens* page.
3. Create a token named after the machine, e.g. `glq-laptop`, and copy it.

If your organisation signs in to Graylog through SSO, an admin may first need to allow
access tokens for external users (*System → Configurations → Users*).

## Profiles

A profile is one Graylog target. Add one per cluster:

```
glq profile add dev     --url https://graylog.dev.example.com
glq profile add staging --url https://graylog.staging.example.com
glq profile add prod    --url https://graylog.example.com --production
glq profile set-token staging        # prompts; input is hidden
glq profile test staging             # auth status and Graylog version
```

The first profile you add becomes the current one. Change it with `glq profile use prod`,
or pick one per command with `--profile prod`. `GLQ_PROFILE` does the same for a whole
shell session.

| Command | Does |
|---|---|
| `glq profile list` | Lists profiles; `*` marks the current one |
| `glq profile show [name]` | Shows a profile's settings and where its token comes from — never the token |
| `glq profile remove <name>` | Removes a profile, its token and its own saved searches (asks first; `--yes` skips) |

`profile add` also takes `--default-range`, `--default-stream`, `--default-limit`,
`--default-fields`, `--proxy`, `--timeout-seconds`, `--ca-bundle` and
`--token-command`.

### Getting the token from a password manager

Instead of storing the token, a profile can run a command that prints it:

```
glq profile add prod --url https://graylog.example.com --production --token-command "op read op://Ops/graylog-prod/token"
```

### CI

Set `GLQ_TOKEN` — it takes precedence over the profile's stored token — and select the
profile with `--profile` or `GLQ_PROFILE`.

## Searching

```
glq search 'source:api-gateway AND http_status:>=500' --range 15m
glq search 'level:<=3' --from 2026-09-24T08:00:00 --to 2026-09-24T09:00:00 --tz Europe/Athens
glq search 'message:"payment declined"' --stream Payments --fields timestamp,source,message --limit 500 -o csv > declined.csv
```

| Option | Meaning |
|---|---|
| `--range 30s\|15m\|2h\|1d\|1w` | The last stretch of time (default: the profile's `default_range`, `10m` unless set) |
| `--from`, `--to` | An exact window, ISO 8601. UTC unless the value has an offset or `--tz` is given |
| `--stream` | Stream name or ID; repeat for several. `glq streams list` shows them |
| `--fields` | Comma-separated fields to show (default: `timestamp,source,message`) |
| `--limit` | How many messages (default 1000, at most `max_limit`, 10,000 unless changed) |
| `--sort` | `field:asc` or `field:desc` (default `timestamp:desc`) |
| `-o` | `table` (default on a terminal), `jsonl` (default when piped), `json` or `csv` |
| `--raw` | The untransformed Graylog response, for debugging |

## Saved searches

Save a search under a name, with `{{placeholders}}` for the parts that change:

```
glq query save svc-errors 'source:{{service}} AND level:<=3' --range 1h --description "Errors for one service" --tag ops
glq query run svc-errors --param service=payments
glq query run svc-errors --param service=payments --range 24h -o csv > payments.csv
```

Options given to `query run` override the saved ones for that run only. Parameter values
are escaped, so a value can never change the meaning of the rest of the search.

Saved searches are **shared** across all profiles by default. Add `--profile-only` to keep
one on the current profile; on that profile it wins over a shared search of the same name.

| Command | Does |
|---|---|
| `glq query list [--tag ops]` | Lists saved searches and whether each is shared or profile-only |
| `glq query show <name>` | Prints one as JSON |
| `glq query edit <name>` | Opens it in your editor and checks it before saving |
| `glq query rm <name>` | Deletes it |
| `glq query export [names…] > team.json` | Writes saved searches to a file — never tokens or profile settings |
| `glq query import team.json [--profile-only]` | Adds saved searches from a file |

A saved search can declare defaults for its parameters (edit it with `glq query edit`):

```json
{
  "query": "source:{{service}} AND level:<={{level}}",
  "range": "1h",
  "params": { "level": { "default": "3", "description": "Highest syslog level" } }
}
```

## The config file

Everything lives in one file: `~/.grdev.glq-cli/config.json` (`glq config path` prints it,
`glq config edit` opens it).

```json
{
  "current_profile": "staging",
  "max_limit": 10000,
  "confirm_limit": 1000,
  "queries": {
    "api-5xx": { "query": "source:api-gateway AND http_status:>=500", "range": "15m" }
  },
  "profiles": {
    "staging": {
      "url": "https://graylog.staging.example.com",
      "token": "…",
      "default_streams": ["API Gateway"],
      "queries": {
        "svc-errors": { "query": "source:{{service}} AND level:<=3", "range": "1h" }
      }
    },
    "prod": {
      "url": "https://graylog.example.com",
      "production": true,
      "auth_method": "token_command",
      "token_command": "op read op://Ops/graylog-prod/token"
    }
  }
}
```

`glq` keeps a small cache of stream names and the Graylog version in
`~/.grdev.glq-cli/cache/`. It never stores log messages.

## Security notes

- **Tokens.** The token is stored in plain text in `config.json`, so `glq` creates that file
  readable by you alone — mode `0600` on macOS and Linux, a user-only access list on
  Windows — and refuses to run if anyone else can read it. `glq config protect` restores
  the permissions. Tokens never appear in output, logs, errors, `--verbose` traces or
  exports.
- **TLS.** Certificates are always verified. For an internal CA, set `ca_bundle` to a PEM
  file. `--insecure` or `"verify_tls": false` turns verification off and prints a warning
  on every run — avoid it.
- **Proxy.** If Graylog is only reachable through a proxy, add the profile with
  `--proxy http://host:port` (stored as `proxy`). It applies to that profile only; without it
  the system or `HTTPS_PROXY` environment proxy is used.
- **Personal data.** Results are printed exactly as Graylog returns them — nothing is
  masked. Production profiles print a notice, and large production runs need `--yes`. Use
  a least-privilege token and treat the output as sensitive.
- **Production.** On a profile with `"production": true`, every command that talks to
  Graylog prints `querying PRODUCTION Graylog — results may contain personal data`, and a
  search or saved-search run with `--limit` above `confirm_limit` (default 1,000) asks for
  confirmation — or needs `--yes` when there is no terminal to ask.
- **Read-only.** `glq` calls only Graylog's search, streams and system-info endpoints. It
  has no command that sends data anywhere; results go only to your screen or a file you
  redirect them to. Treat such files as personal data.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success, including zero results |
| `1` | Usage or configuration error |
| `2` | Authentication or authorization error (401/403) |
| `3` | Network or TLS error |
| `4` | Graylog API or query error |
