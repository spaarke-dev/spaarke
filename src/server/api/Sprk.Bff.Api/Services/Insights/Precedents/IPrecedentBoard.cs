namespace Sprk.Bff.Api.Services.Insights.Precedents;

/// <summary>
/// Lifecycle abstraction over the <c>sprk_precedent</c> Dataverse entity.
/// Phase 1 supports the manual SME authoring flow (D-P3 / D-61 mode A) only:
/// admin creates a Precedent as <c>Tentative</c>; the SME later promotes it to
/// <c>Confirmed</c> from inside the Dataverse model-driven view.
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary placement</b>: Zone B per SPEC §3.5 deliverable placement table —
/// this service consumes <c>IDataverseService</c> (an existing shared Spaarke
/// abstraction) directly. It does NOT import any AI internals (no playbook
/// engine, no LLM client, no node executor). The §3.5.4 forbidden-imports
/// grep is asserted in CI/locally before merge.
/// </para>
/// <para>
/// <b>Promotion semantics</b>: <see cref="ConfirmAsync"/> exists for completeness
/// (used by the D-P4 projection sync trigger and admin promotion flow once it
/// lands) but in Phase 1 the typical promotion path is the SME editing the
/// row's <c>sprk_status</c> from inside Dataverse — see SPEC §3.1 D-P3 note
/// "(out of Phase 1 D-A27 admin flow scope)".
/// </para>
/// </remarks>
public interface IPrecedentBoard
{
    /// <summary>
    /// Create a new Precedent in <c>Tentative</c> status. Used by the admin
    /// endpoint for manual SME authoring (D-P3 Phase 1 mode of D-61).
    /// </summary>
    /// <param name="request">Pattern statement, scope, supporting matters, reviewer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The new Precedent's Dataverse row id.</returns>
    Task<Guid> CreateTentativeAsync(CreatePrecedentRequest request, CancellationToken ct);

    /// <summary>
    /// Retrieve a Precedent by id with key fields populated. Returns <c>null</c>
    /// when the row is not found. Used by integration tests and admin UI.
    /// </summary>
    Task<PrecedentRecord?> GetAsync(Guid precedentId, CancellationToken ct);

    /// <summary>
    /// Promote a <c>Tentative</c> Precedent to <c>Confirmed</c>. Phase 1 manual
    /// flow: typically performed via the Dataverse model-driven view; this
    /// method exists for the future admin-promote endpoint and the D-P4 sync
    /// path. Returns when the update is persisted.
    /// </summary>
    Task ConfirmAsync(Guid precedentId, Guid confirmedByUserId, CancellationToken ct);

    /// <summary>
    /// Mark a Precedent <c>Deprecated</c> (no longer recommended for retrieval
    /// in synthesis). Phase 1 manual flow only.
    /// </summary>
    Task DeprecateAsync(Guid precedentId, CancellationToken ct);
}

/// <summary>
/// Request to create a new Tentative Precedent.
/// Maps to <c>sprk_precedent</c> columns: <c>sprk_name</c> (derived from first
/// 200 chars of statement), <c>sprk_patternstatement</c>, <c>sprk_status</c>
/// (Tentative=100000000), <c>sprk_reviewerby</c>, <c>sprk_reviewdate</c>
/// (today), <c>sprk_producedby</c> ("manual-sme-author"), and the
/// <c>sprk_precedent_matter</c> N:N to <c>sprk_matter</c>.
/// </summary>
/// <param name="PatternStatement">
///   Full pattern claim (Memo 4000). Stored on <c>sprk_patternstatement</c>;
///   the primary name <c>sprk_name</c> is derived from this (first 200 chars).
/// </param>
/// <param name="Scope">
///   Scope discriminator string (e.g. <c>ip-licensing-bigfirm-llp</c>). Optional;
///   carried into <c>sprk_clusterdefinition</c> as a small JSON tag for now —
///   Phase 1.5 may introduce a dedicated scope entity.
/// </param>
/// <param name="SupportingMatterIds">
///   IDs of <c>sprk_matter</c> rows whose Observations support this Precedent
///   (D-46). Associated via the <c>sprk_precedent_matter</c> N:N relationship
///   created in task 011. May be empty (Tentative Precedents authored before
///   sufficient supporting matters exist are allowed in Phase 1).
/// </param>
/// <param name="ReviewerByUserId">
///   Optional <c>systemuser</c> id of the SME reviewer. When null, the calling
///   admin user's id is used (resolved by the endpoint, not this DTO).
/// </param>
public sealed record CreatePrecedentRequest(
    string PatternStatement,
    string? Scope,
    IReadOnlyCollection<Guid> SupportingMatterIds,
    Guid? ReviewerByUserId);

/// <summary>
/// Read shape returned by <see cref="IPrecedentBoard.GetAsync"/>. Mirrors the
/// minimum set of <c>sprk_precedent</c> columns the admin endpoint and
/// integration tests need to verify creation succeeded.
/// </summary>
/// <param name="Id">Dataverse row id (<c>sprk_precedentid</c>).</param>
/// <param name="Name">Primary name (<c>sprk_name</c>).</param>
/// <param name="PatternStatement">Full pattern claim (<c>sprk_patternstatement</c>).</param>
/// <param name="StatusValue">
///   Option-set value from <c>sprk_precedentstatus</c>:
///   Tentative=100000000 / Confirmed=100000001 / UnderDriftReview=100000002 /
///   Deprecated=100000003 / Retired=100000004.
/// </param>
/// <param name="ReviewerByUserId">Reviewer systemuser id (<c>sprk_reviewerby</c>).</param>
/// <param name="ProducedBy">Producer tag (<c>sprk_producedby</c>; e.g. <c>manual-sme-author</c>).</param>
public sealed record PrecedentRecord(
    Guid Id,
    string Name,
    string PatternStatement,
    int StatusValue,
    Guid? ReviewerByUserId,
    string? ProducedBy);

/// <summary>
/// Canonical option-set values for <c>sprk_precedentstatus</c>, as provisioned
/// in task 011. Kept as plain ints to avoid leaking a generated enum across
/// the Zone B boundary.
/// </summary>
public static class PrecedentStatus
{
    public const int Tentative = 100000000;
    public const int Confirmed = 100000001;
    public const int UnderDriftReview = 100000002;
    public const int Deprecated = 100000003;
    public const int Retired = 100000004;
}
