using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// An <see cref="IDataverseUserClient"/> (the documented mock boundary for user-OBO Dataverse access) that answers the
/// access read <c>RetrievalAccessTrim</c> makes: <c>GET sprk_documents?$select=sprk_documentid&amp;$filter=sprk_documentid
/// eq … or …</c>. It returns only the asked ids in <see cref="Readable"/>, the way Dataverse returns only rows the caller
/// can Read. Task 176 (#1511).
/// </summary>
/// <remarks>Global namespace, like <c>TestSessionOwner</c>. Only GET is supported; any other call fails the test.</remarks>
public sealed class ReadableDocumentsUserClient : IDataverseUserClient
{
    private static readonly Regex IdInFilter = new(
        @"sprk_documentid eq ([0-9a-fA-F-]{36})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ReadableDocumentsUserClient(params Guid[] readable)
    {
        Readable = new HashSet<Guid>(readable);
    }

    /// <summary>Documents the simulated caller can Read.</summary>
    public HashSet<Guid> Readable { get; }

    /// <summary>When set, every read fails with this status (e.g. 429, 500, or 0 for "no user context").</summary>
    public int? FailWithStatus { get; set; }

    /// <summary>When set, the response body is this raw JSON instead of the computed rows.</summary>
    public string? RawBody { get; set; }

    /// <summary>Ids asked for, one list per read, in the form they appeared in the filter.</summary>
    public List<IReadOnlyList<string>> Reads { get; } = new();

    public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!relativePath.StartsWith("sprk_documents?", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected Dataverse read: {relativePath}");
        }

        var asked = IdInFilter.Matches(relativePath).Select(m => m.Groups[1].Value).ToList();
        Reads.Add(asked);

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

        var json = RawBody ?? JsonSerializer.Serialize(new
        {
            value = asked
                .Select(Guid.Parse)
                .Where(Readable.Contains)
                .Select(id => new { sprk_documentid = id.ToString("D") })
                .ToArray(),
        });

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
