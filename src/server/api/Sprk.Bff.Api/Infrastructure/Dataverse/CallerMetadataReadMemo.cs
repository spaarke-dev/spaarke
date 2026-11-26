using System.Collections.Concurrent;
using System.Text.Json;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// The caller's Dataverse for ONE record create's caller checks (RecordCreationService step 5), with each table-metadata
/// read (<c>EntityDefinitions(…)</c>) asked once: a later identical read is answered with the first successful answer
/// (field-mapping-server-write-path-r1 task 049, NFR-02 Part C).
/// </summary>
/// <remarks>
/// <para><b>What is remembered.</b> Only a SUCCESSFUL answer to a GET whose path starts with <c>EntityDefinitions(</c> — a
/// table's privileges' names, its columns, its relationships, its entity set, its ownership type: data about the schema, read
/// under the caller's token. A failure is never remembered, so the next identical read asks again, as before. Nothing else is
/// remembered: <c>RetrievePrincipalAccess</c>, <c>RetrieveUserSetOfPrivilegesByNames</c>, record reads and every other
/// caller question still go to Dataverse every time (ADR-009: no authorization answer is cached). A write (POST, PATCH,
/// DELETE) passes through and forgets everything remembered.</para>
/// <para><b>For how long, for whom.</b> One instance wraps one caller's client for one create's step 5, and (task 049c) a
/// second instance for the same create's steps 2 and 3 (the caller's payload, then the merge of the mapped values, which
/// re-read the table's relationships and the entity sets step 2 read); each is then dropped (locals of
/// <c>RecordCreationService.DecideAsync</c>): nothing is shared across those groups, creates, requests or callers. The
/// other steps (3½, the pre-write rules, 6 to 10) keep the caller's own client, so a read they make is asked as before (a
/// stamp bind whose metadata read fails still fails), and a pre-write rule still receives the caller's own client. Each hit returns its own freshly parsed body
/// (a parsed body is not shared across threads), and the store is concurrent, because the caller checks ask in parallel
/// (task 049 Part A).</para>
/// <para><b>Repeats it removes</b> (measured from the source): a mapped create's step 5 asks the same table's privilege
/// names and each organization-owned target's ownership metadata once per pass (the caller's own values, then the merged
/// row, and on a failure each value, then the row as written), and a re-map within it re-reads the table's relationships
/// and the related tables' entity sets.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing: <see cref="CallerWhoAmIMemo"/> (the caller's
/// <c>WhoAmI</c> only, per request, in <c>HttpContext.Items</c>) and <c>Spaarke.Core.Cache.RequestCache</c> (scoped, not
/// thread-safe, and absent when the service runs off a request — a chat tool or a job). Extension: neither can hold this
/// without becoming thread-safe and request-bound, and the create service runs off-request too; this is a decorator with
/// no DI registration. Cost of doing nothing: each repeat is a sequential Dataverse round trip (about 80 ms live) inside
/// the create's NFR-02 budget.</para>
/// </remarks>
internal sealed class CallerMetadataReadMemo(IDataverseUserClient inner) : IDataverseUserClient
{
    private readonly ConcurrentDictionary<string, (int StatusCode, string? RawJson)> _answers = new(StringComparer.Ordinal);

    /// <summary>Whether a GET path reads table metadata (and so may be remembered).</summary>
    internal static bool IsMetadataRead(string relativePath) =>
        relativePath.TrimStart('/').StartsWith("EntityDefinitions(", StringComparison.Ordinal);

    public async Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (!IsMetadataRead(relativePath))
            return await inner.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);

        if (_answers.TryGetValue(relativePath, out var known))
            return Answer(known);

        var response = await inner.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
            _answers.TryAdd(relativePath, (response.StatusCode, response.Body?.GetRawText()));
        return response;
    }

    public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken)
    {
        _answers.Clear();
        return inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PostAsync(
        string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken)
    {
        _answers.Clear();
        return inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken)
    {
        _answers.Clear();
        return inner.PatchAsync(relativePath, jsonBody, cancellationToken);
    }

    public Task<DataverseUserResponse> PatchAsync(
        string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken)
    {
        _answers.Clear();
        return inner.PatchAsync(relativePath, jsonBody, expectedVersion, cancellationToken);
    }

    public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        _answers.Clear();
        return inner.DeleteAsync(relativePath, cancellationToken);
    }

    private static DataverseUserResponse Answer((int StatusCode, string? RawJson) known)
    {
        if (known.RawJson is null)
            return DataverseUserResponse.Ok(known.StatusCode, body: null);

        using var document = JsonDocument.Parse(known.RawJson);
        return DataverseUserResponse.Ok(known.StatusCode, document.RootElement.Clone());
    }
}
