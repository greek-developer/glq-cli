# Profiles & authentication

A profile is one named Graylog target — a cluster, an environment, or a set of
credentials. Switching `--profile` changes the target and nothing else.

## Profile fields `[not-yet-implemented]`

Profiles live in `~/.grdev.glq-cli/config.json`, alongside `current_profile`.

| Field | Default | Meaning |
|---|---|---|
| `name` | — (required) | Unique profile name |
| `url` | — (required) | Graylog base URL |
| `auth_method` | `token` | `token` (keyring or `GLQ_TOKEN`) or `token_command` |
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

A profile never holds a token. A config file containing a token-like key is rejected on
load.

## Commands `[not-yet-implemented]`

| Command | Does |
|---|---|
| `glq profile add <name> --url <url> [options]` | Adds a profile |
| `glq profile list` | Lists profiles, marking the current one |
| `glq profile show <name>` | Prints the profile's settings and **where** its token comes from (keyring, env, or command) — never the token |
| `glq profile use <name>` | Sets `current_profile` |
| `glq profile remove <name>` | Removes the profile and its keyring entry |
| `glq profile test [<name>]` | Checks connectivity and authentication, and prints the Graylog server version and auth status |
| `glq profile set-token <name>` | Prompts for the token with hidden input and stores it in the keyring |

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
2. The OS keyring — service `grdev.glq-cli`, account = profile name
3. The output of `token_command`

Plaintext tokens in files are not supported. Username/password login is not supported.

The user documentation recommends a dedicated, least-privilege Graylog user that can read
only the streams it needs — never an admin account's token.
