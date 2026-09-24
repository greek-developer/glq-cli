# Profiles & authentication

A profile is one named Graylog target — a cluster, an environment, or a set of
credentials. Switching `--profile` changes the target and nothing else.

## The config file `[not-yet-implemented]`

Everything `glq` is configured with lives in **one file**, `~/.grdev.glq-cli/config.json`:
profiles, their tokens, and stored queries ([stored-queries.md](stored-queries.md)).

```json
{
  "current_profile": "staging",
  "max_limit": 10000,
  "mask_fields": ["email", "phone", "username", "customer_id", "user_id", "ip", "card*"],
  "mask_salt": "…generated on first use…",
  "queries": { "api-5xx": { "query": "source:api-gateway AND http_status:>=500", "range": "15m" } },
  "profiles": {
    "staging": {
      "url": "https://graylog.staging.example.internal",
      "token": "…",
      "queries": { "svc-errors": { "query": "source:{{service}} AND level:<=3", "range": "1h" } }
    }
  }
}
```

The file holds tokens in plaintext, so its permissions are **owner-only**: `glq` creates
the folder `0700` and the file `0600` on macOS and Linux, and restricts the file's ACL to
the current user on Windows. On every load `glq` checks this and refuses to run — exit
code `1`, with the fix — if the file is readable by anyone else.

## Profile fields `[not-yet-implemented]`

| Field | Default | Meaning |
|---|---|---|
| `url` | — (required) | Graylog base URL |
| `auth_method` | `token` | `token` (stored in this file, or `GLQ_TOKEN`) or `token_command` |
| `token` | — | The access token — written by `profile set-token`, never printed |
| `token_command` | — | Shell command that prints the token, e.g. a password-manager CLI |
| `verify_tls` | `true` | TLS certificate verification |
| `ca_bundle` | — | Path to a custom CA bundle |
| `timeout_seconds` | `30` | Per-request timeout |
| `production` | `false` | Marks a production target — see [privacy.md](privacy.md) |
| `default_range` | `5m` | Relative range used when a search gives none |
| `default_streams` | `[]` | Stream names or IDs used when a search gives none |
| `default_limit` | `100` | Result count used when a search gives none |
| `default_fields` | narrow built-in set | Fields used when a search gives none |
| `mask_fields` | `[]` | Extra field names to mask — see [privacy.md](privacy.md) |
| `queries` | `{}` | Stored queries that belong to this profile only |

The profile's name is its key under `profiles`.

## Commands `[not-yet-implemented]`

| Command | Does |
|---|---|
| `glq profile add <name> --url <url> [options]` | Adds a profile |
| `glq profile list` | Lists profiles, marking the current one |
| `glq profile show <name>` | Prints the profile's settings and **where** its token comes from (config, env, or command) — never the token |
| `glq profile use <name>` | Sets `current_profile` |
| `glq profile remove <name>` | Removes the profile, its token and its profile-only queries, after confirmation (`--yes` skips it) |
| `glq profile test [<name>]` | Checks connectivity and authentication, and prints the Graylog server version and auth status |
| `glq profile set-token <name>` | Prompts for the token with hidden input and writes it to the profile's `token` |

## Profile resolution `[not-yet-implemented]`

The active profile is the first of:

1. `--profile <name>`
2. `GLQ_PROFILE`
3. `current_profile` in the config file

If none is set, the command fails with exit code `1` and says how to select a profile.

## Authentication `[not-yet-implemented]`

Requests authenticate with a Graylog access token over HTTP Basic: the token as the
username, the literal `token` as the password. Tokens are created in Graylog under
*System → Users and Teams → Tokens*.

The token is the first of:

1. `GLQ_TOKEN` — for CI
2. The profile's `token` in `config.json`
3. The output of the profile's `token_command`

Username/password login is not supported.

The user documentation recommends a dedicated, least-privilege Graylog user that can read
only the streams it needs — never an admin account's token.
