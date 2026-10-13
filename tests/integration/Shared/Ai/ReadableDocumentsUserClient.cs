using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// An <see cref="IDataverseUserClient"/> (the documented mock boundary for user-OBO Dataverse access) that answers the
/// access reads <c>RetrievalAccessTrim</c> makes: <c>GET {entitySet}?$select={key}&amp;$filter={key} eq … or …</c>. It
/// returns only the asked keys the simulated caller can Read, the way Dataverse returns only rows the caller can Read.
/// Task 176 (#1511).
/// </summary>
/// <remarks>Global namespace, like <c>TestSessionOwner</c>. Only GET is supported; any other call fails the test.</remarks>
public sealed class ReadableDocumentsUserClient : IDataverseUserClient
{
    private static readonly Regex Path = new(
        @"^(?<set>[a-z_]+)\?\$select=(?<field>[a-z_]+)&\$filter=(?<filter>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Term = new(
        @"(?<field>[a-z_]+) eq (?:'(?<text>[^']*)'|(?<guid>[0-9a-fA-F-]{36}))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>A caller who can read the given <c>sprk_document</c> ids.</summary>
    public ReadableDocumentsUserClient(params Guid[] readableDocuments)
    {
        foreach (var id in readableDocuments)
        {
            Allow("sprk_documents", id.ToString("D"));
        }
    }

    /// <summary>Readable keys per entity set (GUIDs in bare lowercase form; other keys verbatim).</summary>
    public Dictionary<string, HashSet<string>> Readable { get; } = new(StringComparer.Ordinal);

    /// <summary>When set, every read fails with this status (e.g. 429, 500, or 0 for "no user context").</summary>
    public int? FailWithStatus { get; set; }

    /// <summary>When set, the response body is this raw JSON instead of the computed rows.</summary>
    public string? RawBody { get; set; }

    /// <summary>Keys asked for, one list per read, as they appeared in the filter.</summary>
    public List<IReadOnlyList<string>> Reads { get; } = new();

    /// <summary>Entity sets read, one per read.</summary>
    public List<string> ReadSets { get; } = new();

    public ReadableDocumentsUserClient Allow(string entitySet, string key)
    {
        if (!Readable.TryGetValue(entitySet, out var set))
        {
            Readable[entitySet] = set = new HashSet<string>(StringComparer.Ordinal);
        }

        set.Add(key);
        return this;
    }

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        var match = Path.Match(relativePath);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Unexpected Dataverse read: {relativePath}");
        }

        var entitySet = match.Groups["set"].Value;
        var field = match.Groups["field"].Value;
        var asked = Term.Matches(Uri.UnescapeDataString(match.Groups["filter"].Value))
            .Select(m => m.Groups["guid"].Success ? m.Groups["guid"].Value : m.Groups["text"].Value)
            .ToList();
        Reads.Add(asked);
        ReadSets.Add(entitySet);

        if (FailWithStatus is { } status)
        {
            var code = status switch
            {
                0 => DataverseUserClientErrorCodes.UserContextRequired,
                429 => DataverseUserClientErrorCodes.RateLimited,
                403 => DataverseUserClientErrorCodes.AccessDenied,
                _ => DataverseUserClientErrorCodes.ServiceError,
            };
            return Task.FromResult(DataverseUserResponse.Fail(status, code, "simulated failure"));
        }

        var readable = Readable.TryGetValue(entitySet, out var r) ? r : new HashSet<string>();
        var rows = asked
            .Where(readable.Contains)
            .Select(k => new Dictionary<string, string> { [field] = k })
            .ToArray();
        var json = RawBody ?? JsonSerializer.Serialize(new { value = rows });

        using var doc = JsonDocument.Parse(json);
        return Task.FromResult(DataverseUserResponse.Ok(200, doc.RootElement.Clone()));
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The access trim never writes.");

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The access trim never writes.");

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The access trim never writes.");

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The access trim never writes.");
}
