namespace Sprk.Bff.Api.Services.Insights.Precedents;

/// <summary>
/// Projects a <c>sprk_precedent</c> Dataverse row to the <c>spaarke-insights-index</c>
/// AI Search index with <c>artifactType = "precedent"</c> per SPEC §3.4.2 worked example.
/// Fires from the D-P3 admin endpoint when an SME promotes a Precedent to
/// <see cref="PrecedentStatus.Confirmed"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 — implementation imports
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IInsightsAi"/> ONLY (for embedding
/// generation via <c>EmbedTextAsync</c>, the third facade method added by task 042 to
/// resolve the §3.5 routing question raised in task 041 Step 3). All other dependencies
/// (<see cref="IPrecedentBoard"/>, <c>SearchIndexClient</c>) are Zone B-permitted: the
/// Dataverse board is itself Zone B (task 012), and <c>SearchIndexClient</c> is an Azure
/// SDK type, not in the §3.5.4 forbidden-imports list.
/// </para>
/// <para>
/// <b>Idempotency</b>: writes use <c>SearchClient.MergeOrUploadDocumentsAsync</c> with a
/// deterministic document id (<c>prec:{precedentId-as-N}:v1</c>). Re-projection of the
/// same Precedent overwrites in place; no duplicate rows.
/// </para>
/// <para>
/// <b>Status gate</b>: only Confirmed Precedents (option-set value
/// <see cref="PrecedentStatus.Confirmed"/>) are projected per D-P4 acceptance. Non-Confirmed
/// rows produce a structured log and return <see cref="PrecedentProjectionResult.Skipped"/>
/// without writing to the index. This keeps Tentative drafts and Deprecated entries out of
/// the synthesis retrieval surface.
/// </para>
/// <para>
/// <b>Fire-and-forget callers</b>: the admin endpoint (and the future SME promotion endpoint)
/// invoke this with a background <c>Task.Run</c> wrapper plus try/catch + structured log on
/// failure. Projection failures MUST NOT block the HTTP response (the row is already created
/// in Dataverse and re-projection is safe).
/// </para>
/// </remarks>
public interface IPrecedentProjectionSync
{
    /// <summary>
    /// Project a single Precedent row to <c>spaarke-insights-index</c>.
    /// </summary>
    /// <param name="precedentId">Dataverse row id of the <c>sprk_precedent</c> row.</param>
    /// <param name="tenantId">Required tenant identifier (per D-52). Written to the index
    /// row's <c>tenantId</c> field for retrieval scoping.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="PrecedentProjectionResult"/> indicating whether the projection
    /// wrote, skipped (non-Confirmed status), or could not find the row.</returns>
    /// <exception cref="ArgumentException">When <paramref name="precedentId"/> is empty,
    /// or <paramref name="tenantId"/> is null/whitespace.</exception>
    Task<PrecedentProjectionResult> ProjectAsync(
        Guid precedentId,
        string tenantId,
        CancellationToken ct);
}

/// <summary>
/// Outcome of a single projection attempt. Returned to the caller (typically the admin
/// endpoint background hook) so the operation can be logged with the appropriate detail.
/// </summary>
/// <param name="Outcome">Result classification: written / skipped (non-Confirmed) / not-found.</param>
/// <param name="DocumentId">The AI Search document id written or skipped. <c>null</c> for NotFound.</param>
/// <param name="StatusValue">The Precedent's option-set status value at projection time.
/// Useful for the "skipped because Tentative" path so the log line can report the actual status.</param>
public sealed record PrecedentProjectionResult(
    PrecedentProjectionOutcome Outcome,
    string? DocumentId,
    int? StatusValue);

/// <summary>
/// Projection outcome classification.
/// </summary>
public enum PrecedentProjectionOutcome
{
    /// <summary>The Precedent was Confirmed; one document was upserted to <c>spaarke-insights-index</c>.</summary>
    Written,

    /// <summary>The Precedent exists but its status is not <see cref="PrecedentStatus.Confirmed"/>;
    /// no document was written. The caller should log the actual status value.</summary>
    Skipped,

    /// <summary>The Precedent id was not found in Dataverse; no document was written.</summary>
    NotFound,
}
