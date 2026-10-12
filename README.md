# glq

`glq` lets you search your Graylog logs from the command line.

Instead of opening the Graylog web interface or hand-crafting `curl` calls, you type a
search and get the matching log messages back — as a readable table, or as JSON or CSV for
scripts and spreadsheets.

```
glq search 'source:api-gateway AND http_status:>=500' --range 15m
```

## Why it's handy

- **One flag to switch clusters.** Set up `dev`, `staging` and `prod` once, then pick one
  with `--profile prod`.
- **Saved searches.** Give a search you run often a name, leave blanks in it to fill in
  later, and run it with one short command. Share your saved searches with colleagues as a
  small file.
- **Careful with personal data.** Production clusters are clearly flagged, and large runs
  against them need confirmation. Results are printed as Graylog returns them.
- **Only reads.** `glq` can search and list streams. It cannot change anything in Graylog.
- **Works for AI agents too.** `glq skill` prints a guide that coding agents can load, so
  they can search logs safely on your behalf.

## Get started

```
dotnet tool install --global grdev.glq-cli
glq profile add staging --url https://graylog.staging.example.com
glq profile set-token staging
glq profile test
glq search 'level:<=3' --range 1h
```

The [user guide](docs/user-guide.md) covers installing, creating a Graylog token, profiles,
saved searches and the security details.

## Versioning

Versions are `major.minor.patch`. The major and minor numbers are set by hand in
`version.json`; the patch number goes up with every commit. `glq version` shows exactly which
build you are running.
