using System.Text.Json;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Thin test-seam over <see cref="DataverseWebApiService.RetrieveMultipleImpersonatedAsync"/> for the messaging
/// read path (task 050). Issues an OData GET AS the calling user (<c>MSCRMCallerID</c> impersonation) so Dataverse
/// applies row-level security natively — the returned rows are EXACTLY what that user may read (ownership, role
/// depth, BU, teams, sharing, hierarchy), in one query (owner decision 2026-07-16, <c>notes/access-model-decision.md</c>).
/// <para>
/// <b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <c>RetrieveMultipleImpersonatedAsync</c> already
/// exists on the concrete <see cref="DataverseWebApiService"/>, but it is NOT on any interface, so it is not a
/// mockable seam; ADR-038 bans <c>Mock&lt;HttpMessageHandler&gt;</c>, and the no-leak negative cases (private /
/// internal-only / privilege) are non-negotiable (NFR-06, security-sensitive). (2) Extension — a pure pass-through
/// adapter over the existing method; no second Dataverse client, no new query logic. (3) Cost-of-doing-nothing —
/// without this seam the security-critical negative access-filter tests cannot be authored against the read
/// service (ADR-038 KEEP-path). This is the ADR-010 "interface as a testing seam only" pattern.
/// </para>
/// </summary>
public interface IImpersonatedCommunicationQuery
{
    /// <summary>
    /// Runs <paramref name="odataQuery"/> against <paramref name="entitySetName"/> impersonating
    /// <paramref name="callerSystemUserId"/>, returning the raw attribute-keyed rows. Fail-closed: a
    /// <see cref="Guid.Empty"/> caller MUST be rejected by the implementation (no app-only fallback).
    /// </summary>
    Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
        string entitySetName,
        string? odataQuery,
        Guid callerSystemUserId,
        CancellationToken ct);
}

/// <summary>
/// Default <see cref="IImpersonatedCommunicationQuery"/> — a pass-through to the shared
/// <see cref="DataverseWebApiService"/> impersonated read. Holds no state; safe as a singleton over the
/// singleton <see cref="DataverseWebApiService"/>.
/// </summary>
public sealed class DataverseImpersonatedCommunicationQuery : IImpersonatedCommunicationQuery
{
    private readonly DataverseWebApiService _dataverse;

    public DataverseImpersonatedCommunicationQuery(DataverseWebApiService dataverse)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
        string entitySetName,
        string? odataQuery,
        Guid callerSystemUserId,
        CancellationToken ct)
        => _dataverse.RetrieveMultipleImpersonatedAsync(entitySetName, odataQuery, callerSystemUserId, ct);
}
