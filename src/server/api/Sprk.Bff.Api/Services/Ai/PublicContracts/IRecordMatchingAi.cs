using Sprk.Bff.Api.Models.Ai.RecordSearch;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for AI-driven record matching against the Dataverse records index
/// (matters, projects, invoices, organizations, people).
/// </summary>
/// <remarks>
/// <para>
/// Per refined ADR-013 (2026-05-20), external CRUD code MUST consume AI through this
/// facade rather than reaching for <see cref="Sprk.Bff.Api.Services.Ai.RecordSearch.IRecordSearchService"/>
/// directly. The facade preserves the request/response shape because record matching
/// uses domain DTOs (<see cref="RecordSearchRequest"/> / <see cref="RecordSearchResponse"/>),
/// not raw AI primitives — there is nothing to translate further.
/// </para>
/// <para>
/// Current consumers (Phase 1 inventory, 2026-05-24): none in the CRUD-external set
/// (Workspace, Finance, Jobs handlers outside <c>Services/Ai/</c>, Filters, non-AI
/// Endpoints). Scaffolded ahead of consumer migration so that future CRUD-side record-
/// matching needs land on the facade by default rather than re-injecting the internal
/// service. The Phase 4 FR-C6 CI guard (task 082) will enforce this once it lands.
/// </para>
/// </remarks>
public interface IRecordMatchingAi
{
    /// <summary>
    /// Execute a hybrid (keyword + vector + RRF) record search against the
    /// <c>spaarke-records-index</c>. Results include confidence scores and match
    /// reasons derived from field-overlap analysis.
    /// </summary>
    /// <param name="request">Search request (query, record types, optional filters,
    /// hybrid-mode selection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search response (results + metadata).</returns>
    Task<RecordSearchResponse> SearchAsync(
        RecordSearchRequest request,
        CancellationToken cancellationToken = default);
}
