# Architecture — glq

## Layout

One application project, `src/Glq.Cli`, a .NET global tool (assembly `glq`, so help reads
`Usage: glq`). Namespaces follow folders:

| Folder / namespace | Holds |
|---|---|
| `Commands` | `System.CommandLine` definitions — `search`, `profile`, `query`, `streams`, `config`, `version`, `skill` — and the global options |
| `Configuration` | The `config.json` model (`GlqConfig`, `Profile`), `ConfigStore` (load, atomic owner-only save), `OwnerOnly` (Unix modes, Windows ACLs), `ActiveProfile` resolution, and `GlqPaths` for `~/.grdev.glq-cli/` |
| `Auth` | Token resolution — `GLQ_TOKEN`, `config.json`, `token_command` — behind `ITokenSource` |
| `Client` | `GraylogClient`, the typed HTTP client; `GraylogConnection`, which builds it through `IHttpClientFactory` with the profile's TLS, timeout and token; `ProfileCache` |
| `Backends` | `ISearchBackend` with `ViewsBackend` and `LegacyBackend`, and `SearchService`, which picks one |
| `Search` | The search pipeline shared by `search` and `query run`: `SearchOptions`, `TimeRange`, `StreamResolver`, `SearchRunner` |
| `Queries` | Stored-query model, `QueryCatalog` (shared vs profile-only), `{{param}}` templating and Lucene escaping |
| `Output` | `table`, `json`, `jsonl`, `csv` formatters and `RedactingTextWriter` |

`skills/glq/SKILL.md` is an embedded resource of `Glq.Cli`, printed by `glq skill`.

## Process

`Program.Main` pins stdout to UTF-8, builds a `CliContext` and runs the command tree.
`CliContext` is the seam for everything outside a command: stdout/stderr, stdin, the
environment, the glq folder, the terminal, the editor, `token_command` execution and the
primary HTTP handler. Unit tests build one over a temporary folder, captured writers and a
fake Graylog handler; nothing in a command touches `Console` directly.

Failures are thrown as `GlqException`, which carries the exit code; `Program` prints its
message as one stderr line. System.CommandLine's own exception handler is off so no stack
trace ever reaches output. Parse errors are exit code `1`.

## Secrets

- A resolved token — and the Basic credential built from it — is registered in the
  context's `SecretRegistry`. Stdout and stderr are wrapped in `RedactingTextWriter`, which
  replaces every registered secret with `***`, and `ProfileCache` redacts before writing.
- `GraylogConnection.TraceHandler` (`--verbose`) prints request and response lines with the
  `Authorization` value replaced by `[redacted]`.
- `ConfigStore` writes through a protected temporary file moved over the target, so
  `config.json` is never readable by others, even briefly. `OwnerOnly.FindExposure` checks
  on every load: any group/other bit on Unix, any allow-read entry for another SID on Windows.
- The home directory is `USERPROFILE` on Windows and `HOME` elsewhere, falling back to the
  platform's profile folder; there is no glq-specific path override. The integration tests
  set the home variable to isolate the child `glq` process.

## Graylog API

Every endpoint is verified against the target instance's API browser
(`https://<graylog>/api/api-browser`) — endpoints differ by Graylog version.

| Purpose | Endpoint |
|---|---|
| Search — preferred | `POST /api/views/search/sync` — a one-query search (`query`, `timerange`, a stream `filter`) with one `messages` search type (`limit`, `sort: [{field, order}]`, `fields`); nothing is saved on the server. JSON response `results.{query}.search_types.{id}.messages[].message` |
| Search — fallback | `GET /api/search/universal/relative`, `GET /api/search/universal/absolute` — `query`, `range` or `from`/`to`, `limit`, `fields`, `sort`, `filter=streams:<id> OR …` |
| Streams | `GET /api/streams` |
| Server version | `GET /api/system` |

- **Backend selection.** `SearchService` tries `ViewsBackend` first. A 404 without a Graylog
  error body means the endpoint is missing: it switches to `LegacyBackend` and records
  `views_api: false` in the profile cache. A cached server version below 4.1 (from
  `profile test`) goes straight to legacy.
- **Headers.** Every request sends `Accept: application/json`; non-GET requests also send
  `X-Requested-By: glq` (Graylog's CSRF protection requires it).
- **Auth.** HTTP Basic, token as username and `token` as password.
- **Errors.** 401/403 → exit `2`; timeouts, connection and TLS failures → exit `3`; any other
  non-success → exit `4` with Graylog's `message`.
- **TLS.** `SocketsHttpHandler`: system trust by default; `ca_bundle` adds a custom-root
  chain check (host-name mismatches are never forgiven); `verify_tls = false` or
  `--insecure` accepts any certificate and warns on every run.
- **Read-only by construction.** `GraylogClient` exposes exactly search (views and legacy),
  list streams and system info. There is no method that takes an arbitrary path.
- **Limits.** Result size is bounded by `--limit` and the `max_limit` cap.

## Caching

Per profile, `~/.grdev.glq-cli/cache/<profile>.json`: the stream list (10-minute TTL), the
server version, and whether the views API exists. An entry records the profile URL and is
ignored if the URL changes. Nothing else is cached — never message content.
