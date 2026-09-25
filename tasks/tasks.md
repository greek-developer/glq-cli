# Tasks

## Scaffolding

- [x] Create the solution (`.slnx`), `src/Glq.Cli`, `tests/Glq.Cli.UnitTests`, `tests/Glq.Cli.IntegrationTests`, `Directory.Build.props` — tool packaging, `version.json`, `ProductionVersion.json` generation — specs: tool
- [x] Generate `.gitignore` (`dotnet new gitignore`, plus `release/`) and `.editorconfig` (`dotnet new editorconfig`)
- [x] Add `scripts/.githooks` with a setup script, and CI on Linux, macOS and Windows with the vulnerability scan
- [x] Write `README.md` (plain language) and the user guide in `docs/` — install, least-privilege token setup, config and query examples, security notes

## v1 acceptance

- [ ] `profile add/use/test` works against a local Graylog; `test` reports server version and auth status — specs: profiles — covered by `tests/Glq.Cli.IntegrationTests`; not yet run, needs Docker (`scripts/itest.ps1`)
- [x] Switching `--profile` changes the target cluster and nothing else — specs: profiles
- [x] Ad-hoc search with relative and absolute ranges, stream names, fields, limit, and all four output formats — specs: search, output
- [x] Stored queries, shared and profile-only: save, list, show, edit, run with params and overrides, export, import; missing params give a clear error — specs: stored-queries
- [x] `config.json` is created owner-only, and `glq` refuses to run when it is readable by others — specs: profiles
- [x] Tokens never in any file but `config.json`, or in any output, proven by a test that greps all captured output — specs: privacy
- [x] Masking on by default; card-like numbers masked even with `--no-mask` — specs: privacy
- [x] Production notice shown; large production runs need confirmation — specs: privacy
- [x] Views API used when available, legacy fallback on 404, both paths tested — specs: search
- [x] `version`, `config path|edit` and `skill` behave per the standard — specs: tool

## v1.1 (stretch)

- [ ] `--follow` — tail by polling at an interval — specs: search
- [ ] Simple aggregations, e.g. count by field — specs: search
- [ ] Shell completion for profile, stored-query and stream names — specs: tool

## Open questions — confirm with the requester

- [ ] Which Graylog version(s) run here? Decides whether the legacy fallback is needed at all
- [x] Publish to nuget.org or an internal feed? — nuget.org, via Trusted Publishing
- [ ] Are users external (SSO) accounts? Then access tokens for external users must be enabled
- [ ] Which log-schema field names belong on the default masking list?
