namespace Glq.Cli.Queries;

/// <summary>A named, reusable, optionally parameterized search - see specs/stored-queries.md.</summary>
internal sealed class StoredQuery
{
    public string? Query { get; set; }

    public string? Description { get; set; }

    public string? Range { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public List<string>? Streams { get; set; }

    public List<string>? Fields { get; set; }

    public int? Limit { get; set; }

    public string? Sort { get; set; }

    public List<string>? Tags { get; set; }

    public SortedDictionary<string, QueryParam>? Params { get; set; }
}

/// <summary>One declared parameter of a stored query.</summary>
internal sealed class QueryParam
{
    public string? Default { get; set; }

    public string? Description { get; set; }
}
