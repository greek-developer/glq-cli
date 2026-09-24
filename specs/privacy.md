# Security & privacy

Graylog — production especially — holds customer identifiers and other personal data.
Everything here is a v1 requirement.

## Secrets `[not-yet-implemented]`

Tokens come only from `GLQ_TOKEN`, the profile's `token` in `config.json`, or
`token_command` output. `config.json` is the one file that holds them, and it is owner-only
([profiles.md](profiles.md)). Tokens never appear in any other file `glq` writes, or in any
output: results, `--raw`, `query export`, errors, logs, verbose or HTTP-debug traces, or
crash traces. The `Authorization` header is redacted wherever requests are shown.

## TLS `[not-yet-implemented]`

TLS verification is on by default. `--insecure` or `verify_tls = false` prints a warning to
stderr on every run. A custom CA bundle is supported through `ca_bundle`.

## Masking `[not-yet-implemented]`

Output is masked by default, in every format, unless `--no-mask` is given:

- email addresses and phone numbers, detected by pattern;
- fields named in the top-level `mask_fields` of `config.json` — by default `email`,
  `phone`, `username`, `customer_id`, `user_id`, `ip`, `card*`;
- fields named in the profile's `mask_fields`.

Masks are deterministic pseudonyms from a salted hash — e.g. `cust_…a91f` — so the same
value masks the same way across rows and correlation still works. The salt is generated on
first use and kept as `mask_salt` in `config.json`.

**Card numbers are always masked.** Any Luhn-valid sequence of 13–19 digits is masked,
`--no-mask` notwithstanding.

## Data minimization `[not-yet-implemented]`

Default field lists are narrow and the default limit is 100. No message content is cached;
only stream metadata and the server version are.

## Production awareness `[not-yet-implemented]`

Against a profile with `production = true`:

- every command prints one line to stderr:
  `querying PRODUCTION Graylog — results may contain personal data`;
- a stored query run with `--limit` above the confirmation threshold needs `--yes`, or an
  interactive confirmation when stdin is a terminal. Without either, it exits with code `1`.

## Read-only, local-only `[not-yet-implemented]`

`glq` can call only search, streams and system-info endpoints. It has no command that calls
an arbitrary endpoint, and no upload, webhook or share feature — results go to stdout or to
a file the user redirects to.
