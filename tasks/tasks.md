# Tasks

## Scaffolding

- [ ] Create the solution (`.slnx`), `src/Glq.Cli`, `tests/Glq.Cli.UnitTests`, `tests/Glq.Cli.IntegrationTests`, `Directory.Build.props` — tool packaging, `version.json`, `ProductionVersion.json` generation — specs: tool
- [ ] Generate `.gitignore` (`dotnet new gitignore`, plus `release/`) and `.editorconfig` (`dotnet new editorconfig`)
- [ ] Add `scripts/.githooks` with a setup script, and CI on Linux, macOS and Windows with the vulnerability scan
- [ ] Write `README.md` (plain language) and the user guide in `docs/` — install, least-privilege token setup, config and query examples, security notes

## v1 acceptance

- [ ] `profile add/use/test` works against a local Graylog; `test` reports server version and auth status — specs: profiles
- [ ] Switching `--profile` changes the target cluster and nothing else — specs: profiles
- [ ] Ad-hoc search with relative and absolute ranges, stream names, fields, limit, and all four output formats — specs: search, output
- [ ] Stored queries: save, list, show, edit, run with params and overrides, export, import; missing params give a clear error — specs: stored-queries
- [ ] Tokens never on disk in plaintext or in any output, proven by a test that greps all captured output — specs: privacy
- [ ] Masking on by default; card-like numbers masked even with `--no-mask` — specs: privacy
- [ ] Production notice shown; large production runs need confirmation — specs: privacy
- [ ] Views API used when available, legacy fallback on 404, both paths tested — specs: search
- [ ] `version`, `config path|edit` and `skill` behave per the standard — specs: tool

## v1.1 (stretch)

- [ ] `--follow` — tail by polling at an interval — specs: search
- [ ] Simple aggregations, e.g. count by field — specs: search
- [ ] Shell completion for profile, stored-query and stream names — specs: tool

## Open questions — confirm with the requester

- [ ] Which Graylog version(s) run here? Decides whether the legacy fallback is needed at all
- [ ] Publish to nuget.org or an internal feed?
- [ ] Should shared stored-query files live in a specific GitLab repository?
- [ ] Are users external (SSO) accounts? Then access tokens for external users must be enabled
- [ ] Which log-schema field names belong on the default masking list?
- [ ] Which keyring dependency or per-OS implementation to use
