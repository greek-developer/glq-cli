# Search

`glq search <query>` runs an ad-hoc search in Graylog/Lucene syntax against the active
profile and prints matching messages.

```
glq search 'source:api-gateway AND http_status:>=500' --range 15m --fields timestamp,source,message --limit 200
glq search 'level:3' --from 2026-09-24T08:00:00Z --to 2026-09-24T09:00:00Z --stream "API Gateway" -o jsonl
```

## Time range

| Option | Meaning |
|---|---|
| `--range 15m\|2h\|1d\|…` | Relative range ending now |
| `--from <iso8601> --to <iso8601>` | Absolute range; UTC unless the value carries an offset or `--tz` is given |
| `--tz <zone>` | Time zone for absolute values without an offset |

`--range` and `--from/--to` are mutually exclusive — combining them is a usage error. With
neither, the profile's `default_range` applies.

## Streams

`--stream` (`-s`) is repeatable and takes a stream name or ID; a 24-hex-digit value is used
as an ID as is. Names resolve to IDs through the
streams API, matching the name in any case; the name→ID map is cached per profile for 10
minutes, and a name missing from the cache is looked up again before it is reported as
unknown. A name matching more than one stream is an error that lists the candidates. With no `--stream`, the profile's
`default_streams` apply.

## Fields, limit, sort

| Option | Default |
|---|---|
| `--fields a,b,c` | Profile `default_fields` |
| `--limit <n>` | Profile `default_limit` (100) |
| `--sort <field>:asc\|desc` | `timestamp:desc` |

`--limit` has a hard maximum — `max_limit` in `config.json`, default 10,000. A larger
value is rejected with exit code `1`.

## Output

`-o/--output` selects the format — see [output.md](output.md). `--raw` prints the
untransformed API response for debugging; it is still subject to token redaction
([privacy.md](privacy.md)).

On a terminal with no matches, the table output prints `No messages matched.` to stderr.

## Backends

`glq` uses the views search API (`POST /api/views/search/sync`, which runs a search without saving it, with a `messages` search type so that Graylog applies the sort and the limit) and falls back to the legacy
universal search API when the views API answers 404 — a 404 that carries a Graylog error
message is reported as an error instead. A fallback is remembered per profile, and a server
whose cached version (from `glq profile test`) predates 4.1 goes straight to the legacy API.

## Permissions

A 403 exits with code `2` and explains that the token's user needs search and stream-read
permission on the requested streams.
