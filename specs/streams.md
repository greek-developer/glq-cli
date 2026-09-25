# Streams

## Listing streams

`glq streams list [--filter <substring>]` lists the streams the token's user can read on
the active profile — name, ID and description — in any output format from
[output.md](output.md). `--filter` matches a case-insensitive substring of the name; `--refresh` bypasses the
cache.

Stream metadata is the only data, besides the server version, that `glq` caches — per
profile, for 10 minutes.
