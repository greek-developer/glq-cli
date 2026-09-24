# Architecture — glq

Planned design. Nothing is built yet; update this file as the code lands.

## Layout

One application project, `src/Glq.Cli`, a .NET global tool. Namespaces follow folders:

| Folder / namespace | Holds |
|---|---|
| `Commands` | `System.CommandLine` definitions — `profile`, `search`, `query`, `streams`, `config`, `version`, `skill` |
| `Configuration` | The `config.json` model — profiles, tokens, shared and profile-only queries — loading, validation, owner-only permission checks, and the `~/.grdev.glq-cli/` paths |
| `Auth` | Token resolution — `GLQ_TOKEN`, `config.json`, `token_command` — behind `ITokenSource` |
| `Client` | The typed Graylog HTTP client: headers, TLS, timeouts, error → exit-code mapping, server-version detection |
| `Backends` | `ISearchBackend` with `ViewsBackend` and `LegacyBackend` |
| `Queries` | Stored-query model, `{{param}}` templating, Lucene escaping |
| `Output` | `table`, `json`, `jsonl`, `csv` formatters |
| `Masking` | Pattern, field-name and Luhn masking; deterministic salted pseudonyms |

`skills/glq/SKILL.md` is an embedded resource of `Glq.Cli`.

## Graylog API

Every endpoint is verified against the target instance's API browser
(`https://<graylog>/api/api-browser`) — endpoints differ by Graylog version.

| Purpose | Endpoint |
|---|---|
| Search — preferred | `POST /api/views/search/messages` — body carries streams, query, timerange, fields, limit; returns JSON or CSV |
| Search — fallback | `GET /api/search/universal/relative`, `GET /api/search/universal/absolute` — legacy; some installs 404 |
| Streams | `GET /api/streams` |
| Server version | `GET /api/system` |

- **Backend selection.** `ViewsBackend` is tried first; a 404 switches to `LegacyBackend`.
  The server version from `profile test` is cached per profile and informs the choice.
- **Headers.** Every request sends `Accept`; non-GET requests also send `Content-Type` and
  `X-Requested-By: glq` (Graylog's CSRF protection requires it).
- **Auth.** HTTP Basic, token as username and `token` as password. The header value never
  passes through logging; a delegating handler redacts `Authorization` in any trace.
- **Read-only by construction.** The typed client exposes exactly three operations —
  search, list streams, system info. There is no method that takes an arbitrary path.
- **Limits.** Result size is bounded by the API's limit and the `max_limit` cap.

## Caching

Per profile, under `~/.grdev.glq-cli/cache/`: stream name→ID map (10-minute TTL) and the
server version. Nothing else is cached — never message content.
