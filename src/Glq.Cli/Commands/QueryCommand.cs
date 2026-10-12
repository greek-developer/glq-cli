using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Glq.Cli.Configuration;
using Glq.Cli.Output;
using Glq.Cli.Queries;
using Glq.Cli.Search;

namespace Glq.Cli.Commands;

/// <summary><c>glq query save | list | show | edit | rm | run | export | import</c> - see specs/stored-queries.md.</summary>
internal static partial class QueryCommand
{
    internal static Command Create(CliContext context) => new("query", "Save, share and run named, parameterized searches.")
    {
        Save(context),
        List(context),
        Show(context),
        Edit(context),
        Remove(context),
        Run(context),
        Export(context),
        Import(context),
    };

    private static Argument<string> NameArgument() => new("name") { Description = "The stored query's name." };

    private static Option<bool> ProfileOnlyOption(string description) => new("--profile-only") { Description = description };

    private static Command Save(CliContext context)
    {
        var name = NameArgument();
        var query = new Argument<string>("query") { Description = "Graylog/Lucene query; may contain {{param}} placeholders." };
        var description = new Option<string?>("--description", "-d") { Description = "Free-text description." };
        var tags = new Option<string[]>("--tag") { Description = "Label for filtering. Repeatable." };
        var profileOnly = ProfileOnlyOption("Save it on the active profile only, instead of shared.");
        var search = new SearchOptions();

        var command = new Command("save", "Save or replace a stored query.") { name, query, description, tags, profileOnly };
        search.AddTo(command);

        command.SetAction(parseResult =>
        {
            var queryName = parseResult.GetValue(name)!;
            var config = context.Store.Load();
            var spec = search.Read(parseResult);

            // An absolute range given with --tz is stored with its offset, so it means the same
            // instant wherever the query is run later.
            var zone = TimeRange.ParseZone(spec.TimeZone);
            var stored = new StoredQuery
            {
                Query = parseResult.GetValue(query),
                Description = parseResult.GetValue(description),
                Range = spec.Range,
                From = spec.From is null ? null : TimeRange.ParseAbsolute(spec.From, zone).ToString("o"),
                To = spec.To is null ? null : TimeRange.ParseAbsolute(spec.To, zone).ToString("o"),
                Streams = spec.Streams is null ? null : [.. spec.Streams],
                Fields = spec.Fields is null ? null : [.. spec.Fields],
                Limit = spec.Limit,
                Sort = spec.Sort,
                Tags = parseResult.GetValue(tags) is { Length: > 0 } given ? [.. given] : null,
            };
            QueryCatalog.Validate(queryName, stored, config);

            var owner = parseResult.GetValue(profileOnly)
                ? ActiveProfile.Resolve(context, parseResult, config).Name
                : null;
            var scope = QueryCatalog.ScopeOf(config, owner);
            var replaced = scope.ContainsKey(queryName);
            scope[queryName] = stored;
            context.Store.Save(config);

            context.Err.WriteLine(
                $"{(replaced ? "Replaced" : "Saved")} query '{queryName}' ({(owner is null ? "shared" : $"profile '{owner}' only")}).");
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command List(CliContext context)
    {
        var tag = new Option<string?>("--tag") { Description = "Show only queries with this tag." };
        var output = Formatter.CreateOption();
        var json = Formatter.CreateJsonOption();
        var command = new Command("list", "List the stored queries visible on the active profile.") { tag, output, json };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var profileName = ExistingSelectedProfile(context, parseResult, config);
            var wanted = parseResult.GetValue(tag);

            var rows = QueryCatalog.Visible(config, profileName)
                .Where(q => wanted is null || q.Query.Tags?.Contains(wanted, StringComparer.OrdinalIgnoreCase) == true)
                .OrderBy(q => q.Name, StringComparer.Ordinal)
                .Select(q => new JsonObject
                {
                    ["name"] = q.Name,
                    ["scope"] = q.Scope,
                    ["description"] = q.Query.Description,
                    ["tags"] = string.Join(",", q.Query.Tags ?? []),
                    ["params"] = string.Join(",", QueryTemplate.Placeholders(q.Query.Query ?? string.Empty)),
                    ["query"] = q.Query.Query,
                })
                .ToList();

            Formatter.Write(
                context.Out,
                Formatter.Resolve(parseResult.GetValue(output), parseResult.GetValue(json), context.StdoutIsTerminal),
                ["name", "scope", "description", "tags", "params", "query"],
                rows);
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Show(CliContext context)
    {
        var name = NameArgument();
        var command = new Command("show", "Print one stored query as JSON.") { name };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var found = QueryCatalog.Find(config, ExistingSelectedProfile(context, parseResult, config), parseResult.GetValue(name)!);

            var json = JsonSerializer.SerializeToNode(found.Query, ConfigJson.Options)!.AsObject();
            var shown = new JsonObject { ["name"] = found.Name, ["scope"] = found.Scope };
            foreach (var (key, value) in json)
            {
                shown[key] = value?.DeepClone();
            }

            shown["params"] ??= new JsonArray([.. QueryTemplate.Placeholders(found.Query.Query!).Select(p => (JsonNode?)p)]);
            context.Out.WriteLine(shown.ToJsonString(ConfigJson.Options));
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Edit(CliContext context)
    {
        var name = NameArgument();
        var command = new Command("edit", "Edit a stored query in $VISUAL / $EDITOR; it is validated before it is saved.") { name };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var found = QueryCatalog.Find(config, ExistingSelectedProfile(context, parseResult, config), parseResult.GetValue(name)!);

            // The draft stays in the glq folder, owner-only, and is removed afterwards.
            var draft = Path.Combine(context.Paths.Folder, $"query-{found.Name}.edit.json");
            context.Store.WriteOwnerOnly(draft, JsonSerializer.Serialize(found.Query, ConfigJson.Options) + "\n");
            try
            {
                var exitCode = context.RunEditor(draft);
                if (exitCode != 0)
                {
                    throw GlqException.Usage($"The editor exited with code {exitCode}; the query was not changed.");
                }

                StoredQuery edited;
                try
                {
                    edited = JsonSerializer.Deserialize<StoredQuery>(File.ReadAllText(draft), ConfigJson.Options)
                        ?? throw GlqException.Usage("The edited query is empty; the query was not changed.");
                }
                catch (JsonException exception)
                {
                    throw GlqException.Usage($"The edited query is not valid JSON ({exception.Message}); the query was not changed.");
                }

                QueryCatalog.Validate(found.Name, edited, config);
                QueryCatalog.ScopeOf(config, found.ProfileName)[found.Name] = edited;
                context.Store.Save(config);
                context.Err.WriteLine($"Saved query '{found.Name}' ({found.Scope}).");
                return ExitCodes.Success;
            }
            finally
            {
                File.Delete(draft);
            }
        });
        return command;
    }

    private static Command Remove(CliContext context)
    {
        var name = NameArgument();
        var command = new Command("rm", "Delete a stored query.") { name };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var found = QueryCatalog.Find(config, ExistingSelectedProfile(context, parseResult, config), parseResult.GetValue(name)!);
            QueryCatalog.ScopeOf(config, found.ProfileName).Remove(found.Name);
            context.Store.Save(config);
            context.Err.WriteLine($"Deleted query '{found.Name}' ({found.Scope}).");
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Run(CliContext context)
    {
        var name = NameArgument();
        var parameters = new Option<string[]>("--param", "-P") { Description = "A parameter value as name=value. Repeatable." };
        var search = new SearchOptions();
        var run = new RunOptions();

        var command = new Command("run", "Run a stored query; any search option given overrides the stored value for this run.") { name, parameters };
        search.AddTo(command);
        run.AddTo(command);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var config = context.Store.Load();
            var active = ActiveProfile.Resolve(context, parseResult, config);
            var found = QueryCatalog.Find(config, active.Name, parseResult.GetValue(name)!);
            var stored = found.Query;

            var values = ParseParams(parseResult.GetValue(parameters) ?? []);
            var query = QueryTemplate.Render(stored.Query!, values, stored.Params);

            var spec = new SearchSpec(stored.Range, stored.From, stored.To, null, stored.Streams, stored.Fields, stored.Limit, stored.Sort)
                .OverriddenBy(search.Read(parseResult));

            return SearchRunner.RunAsync(context, parseResult, active, query, spec, run, cancellationToken);
        });
        return command;
    }

    private static Command Export(CliContext context)
    {
        var names = new Argument<string[]>("names") { Description = "Queries to export (default: all visible).", Arity = ArgumentArity.ZeroOrMore };
        var command = new Command("export", "Write stored queries as JSON to stdout - never tokens or profile settings.") { names };
        command.SetAction(parseResult =>
        {
            var config = context.Store.Load();
            var profileName = ExistingSelectedProfile(context, parseResult, config);
            var requested = parseResult.GetValue(names) ?? [];
            var selected = requested.Length == 0
                ? QueryCatalog.Visible(config, profileName)
                : [.. requested.Select(n => QueryCatalog.Find(config, profileName, n))];

            var queries = new JsonObject();
            foreach (var query in selected.OrderBy(q => q.Name, StringComparer.Ordinal))
            {
                queries[query.Name] = JsonSerializer.SerializeToNode(query.Query, ConfigJson.Options);
            }

            context.Out.WriteLine(new JsonObject { ["queries"] = queries }.ToJsonString(ConfigJson.Options));
            return ExitCodes.Success;
        });
        return command;
    }

    private static Command Import(CliContext context)
    {
        var file = new Argument<FileInfo>("file") { Description = "A file written by glq query export." };
        var profileOnly = ProfileOnlyOption("Add the queries to the active profile only, instead of shared.");
        var command = new Command("import", "Add stored queries from a file; files with token-like keys are rejected.") { file, profileOnly };
        command.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(file)!;
            if (!path.Exists)
            {
                throw GlqException.Usage($"{path.FullName} does not exist.");
            }

            JsonNode? document;
            try
            {
                document = JsonNode.Parse(File.ReadAllText(path.FullName), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (JsonException exception)
            {
                throw GlqException.Usage($"{path.Name} is not valid JSON: {exception.Message}");
            }

            if (FindSecretKey(document) is { } secretKey)
            {
                throw GlqException.Usage($"{path.Name} was rejected: it contains the key '{secretKey}', which looks like a secret. Exports never carry tokens.");
            }

            if (document is not JsonObject root || root.Count != 1 || root["queries"] is not JsonObject incoming)
            {
                throw GlqException.Usage($"{path.Name} is not a query export: expected {{ \"queries\": {{ ... }} }}.");
            }

            var config = context.Store.Load();
            var parsed = new List<(string Name, StoredQuery Query)>();
            foreach (var (queryName, node) in incoming)
            {
                StoredQuery query;
                try
                {
                    query = node.Deserialize<StoredQuery>(ConfigJson.Options)
                        ?? throw GlqException.Usage($"Query '{queryName}' in {path.Name} is empty.");
                }
                catch (JsonException exception)
                {
                    throw GlqException.Usage($"Query '{queryName}' in {path.Name} is not valid: {exception.Message}");
                }

                QueryCatalog.Validate(queryName, query, config);
                parsed.Add((queryName, query));
            }

            var owner = parseResult.GetValue(profileOnly) ? ActiveProfile.Resolve(context, parseResult, config).Name : null;
            var scope = QueryCatalog.ScopeOf(config, owner);
            var replaced = parsed.Where(q => scope.ContainsKey(q.Name)).Select(q => q.Name).ToList();
            foreach (var (queryName, query) in parsed)
            {
                scope[queryName] = query;
            }

            context.Store.Save(config);
            context.Err.WriteLine(
                $"Imported {parsed.Count} quer{(parsed.Count == 1 ? "y" : "ies")} ({(owner is null ? "shared" : $"profile '{owner}' only")})" +
                (replaced.Count == 0 ? "." : $"; replaced: {string.Join(", ", replaced)}."));
            return ExitCodes.Success;
        });
        return command;
    }

    internal static Dictionary<string, string> ParseParams(IEnumerable<string> pairs)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                throw GlqException.Usage($"'{pair}' is not a parameter: use --param name=value.");
            }

            values[pair[..separator].Trim()] = pair[(separator + 1)..];
        }

        return values;
    }

    /// <summary>The first key anywhere in the document that looks like it holds a secret.</summary>
    internal static string? FindSecretKey(JsonNode? node) => node switch
    {
        JsonObject obj => obj
            .Select(pair => SecretKey().IsMatch(pair.Key) ? pair.Key : FindSecretKey(pair.Value))
            .FirstOrDefault(key => key is not null),
        JsonArray array => array.Select(FindSecretKey).FirstOrDefault(key => key is not null),
        _ => null,
    };

    // The selected profile when it exists; the query view is shared-only otherwise.
    private static string? ExistingSelectedProfile(CliContext context, ParseResult parseResult, GlqConfig config) =>
        ActiveProfile.SelectedName(context, parseResult, config) is { } name && config.ProfilesOrEmpty.ContainsKey(name)
            ? name
            : null;

    [GeneratedRegex("token|secret|passw|api[_-]?key|authorization|credential|bearer", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();
}
