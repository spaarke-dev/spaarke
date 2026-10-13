using System.Text.Json;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Thin test-seam over <see cref="DataverseWebApiService.RetrieveMultipleAppOnlyAsync"/> for the EXTERNAL-plane message
/// read (spaarke-SPA-external-access-platform-r3 task 033, FR-04). The sibling of <see cref="IImpersonatedCommunicationQuery"/>:
/// same row shape (attribute-keyed JSON with FormattedValue annotations), but issued as the BFF's own identity, because a
/// contact caller has no <c>systemuser</c> to impersonate and the external plane forbids OBO (ADR-028, NFR-01).
/// <para>
/// <b>Not an access-scoped read.</b> Its only consumer, <see cref="CommunicationThreadReadService.ReadRegardingAppOnlyAsync"/>,
/// is called after the external endpoint has checked the record against the caller's composed accessible set, and its
/// query is keyed on that one record. It is never a fallback for an unresolved impersonated caller.
/// </para>
/// <para>
/// <b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="IImpersonatedCommunicationQuery"/> is the
/// impersonated twin; its contract is "no app-only fallback", so adding an app-only member to it would contradict the
/// contract its callers and test doubles rely on. (2) Extension — a pure pass-through over the existing
/// <see cref="DataverseWebApiService"/> GET (the impersonated and app-only reads share one private core); no second
/// Dataverse client, no new query logic. (3) Cost of doing nothing — the internal-only filter on the external read is a
/// security rule with non-negotiable negative cases (FR-04: a CIAM caller never sees an internal-only message), and
/// ADR-038 bans <c>Mock&lt;HttpMessageHandler&gt;</c>, so without this seam those cases cannot be tested against the read
/// service. ADR-010 "interface as a testing seam only" (the <see cref="IImpersonatedCommunicationQuery"/> precedent).
/// </para>
/// </summary>
public interface IAppOnlyCommunicationQuery
{
    /// <summary>
    /// Runs <paramref name="odataQuery"/> against <paramref name="entitySetName"/> app-only, returning the raw
    /// attribute-keyed rows (FormattedValue annotations included).
    /// </summary>
    Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
        string entitySetName,
        string? odataQuery,
        CancellationToken ct);
}

/// <summary>
/// Default <see cref="IAppOnlyCommunicationQuery"/> — a pass-through to the shared <see cref="DataverseWebApiService"/>
/// app-only read. Holds no state; safe as a singleton over the singleton <see cref="DataverseWebApiService"/>.
/// </summary>
public sealed class DataverseAppOnlyCommunicationQuery : IAppOnlyCommunicationQuery
{
    private readonly DataverseWebApiService _dataverse;

    public DataverseAppOnlyCommunicationQuery(DataverseWebApiService dataverse)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
        string entitySetName,
        string? odataQuery,
        CancellationToken ct)
        => _dataverse.RetrieveMultipleAppOnlyAsync(entitySetName, odataQuery, ct);
}
