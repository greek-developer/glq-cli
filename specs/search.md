# Search

`glq search <query>` runs an ad-hoc search in Graylog/Lucene syntax against the active
profile and prints matching messages.

```
glq search 'source:api-gateway AND http_status:>=500' --range 15m --fields timestamp,source,message --limit 200
glq search 'level:3' --from 2026-09-24T08:00:00Z --to 2026-09-24T09:00:00Z --stream "API Gateway" -o jsonl
```

## Time range `[not-yet-implemented]`

| Option | Meaning |
|---|---|
| `--range 15m\|2h\|1d\|…` | Relative range ending now |
| `--from <iso8601> --to <iso8601>` | Absolute range; UTC unless the value carries an offset or `--tz` is given |
| `--tz <zone>` | Time zone for absolute values without an offset |

`--range` and `--from/--to` are mutually exclusive — combining them is a usage error. With
neither, the profile's `default_range` applies.

## Streams `[not-yet-implemented]`

`--stream` is repeatable and takes a stream name or ID. Names resolve to IDs through the
streams API; the name→ID map is cached per profile for 10 minutes. A name matching more than
one stream is an error that lists the candidates. With no `--stream`, the profile's
`default_streams` apply.

## Fields, limit, sort `[not-yet-implemented]`

| Option | Default |
|---|---|
| `--fields a,b,c` | Profile `default_fields` |
| `--limit <n>` | Profile `default_limit` (100) |
| `--sort <field>:asc\|desc` | `timestamp:desc` |

`--limit` has a hard maximum — `max_limit` in `config.json`, default 10,000. A larger
value is rejected with exit code `1`.

## Output `[not-yet-implemented]`

`-o/--output` selects the format — see [output.md](output.md). `--raw` prints the
untransformed API response for debugging; it is still subject to token redaction and
card-number masking ([privacy.md](privacy.md)).

## Permissions `[not-yet-implemented]`

A 403 exits with code `2` and explains that the token's user needs search and stream-read
permission on the requested streams.
