# Output

## Formats

`-o/--output` selects the format for search results, stored-query runs and listings.

| Format | Default when | Shape |
|---|---|---|
| `table` | stdout is a terminal | Aligned columns of the selected fields; line breaks inside a value become spaces and cells are cut at 80 characters |
| `json` | — | One JSON array |
| `jsonl` | stdout is redirected or piped | One JSON object per line |
| `csv` | — | Header row of the selected fields, then one row per message |

Masking ([privacy.md](privacy.md)) applies identically to every format.

## Streams of output

Results go to stdout; errors, warnings and notices go to stderr, in plain language. Zero
results is success. Output is UTF-8 on every platform.
