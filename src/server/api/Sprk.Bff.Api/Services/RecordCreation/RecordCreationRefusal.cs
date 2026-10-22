using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The closed set of reasons <see cref="RecordCreationService"/> refuses a create (field-mapping-server-write-path-r1 task
/// 013, spec FR-12). <see cref="RecordCreationProblemMap"/> maps every member to one HTTP status; a member it does not map
/// fails <c>RecordCreationProblemMapTests</c>.
/// </summary>
internal enum RecordCreationRefusalKind
{
    /// <summary>The caller could not be named in Dataverse (WhoAmI under their token). 403.</summary>
    PrincipalUnresolved,

    /// <summary>The caller's payload names a column the server owns (FR-11, step 2). 403.</summary>
    BlockedColumn,

    /// <summary>The caller's own rights, or a filing rule, refuse the create. 403.</summary>
    Forbidden,

    /// <summary>A record the row names does not exist or the caller cannot use it — one uniform answer. 404.</summary>
    NotFoundOrDenied,

    /// <summary>The payload is not one this service accepts, or a pre-write rule refused it. 400.</summary>
    Invalid,

    /// <summary>No owner could be decided (the resolver's refusal, or a secure filing this path may not make). 409.</summary>
    OwnerUnresolved,

    /// <summary>The create collided with existing data (the autonumber's alternate key, every attempt). 409.</summary>
    Conflict,

    /// <summary>Whether the create may stand could not be decided, and nothing is left. 500.</summary>
    Undecided,

    /// <summary>A check or the owner decision faulted before anything was written (fail closed, NFR-01). 503.</summary>
    Unavailable,

    /// <summary>A question asked as the caller failed at Dataverse (their own failure, never its text). 400/403/404/429/503/504.</summary>
    Upstream,
}

/// <summary>
/// Why <see cref="RecordCreationService"/> created nothing: a <see cref="Kind"/>, a stable <see cref="Code"/> and a
/// <see cref="Detail"/> in words the caller can act on. Nothing is written when a refusal is returned before the create
/// (steps 1 to 11); a refusal after it (an isolated root that had to be removed again) names a row that no longer exists.
/// </summary>
/// <remarks>
/// <para><b>One map.</b> Every HTTP adapter renders a refusal through <see cref="RecordCreationProblemMap"/>; the chat
/// adapters (task 017) read <see cref="Code"/> and <see cref="Detail"/>. <see cref="ClientFailure"/>,
/// <see cref="PreWrite"/> and <see cref="OwnerDecision"/> carry the decided objects for those adapters (ebilling-r1
/// correction (c): the pre-write rule's own refusal object, not a copy); the HTTP map never renders their text.</para>
/// </remarks>
internal sealed record RecordCreationRefusal
{
    /// <summary>The uniform not-found's stable code (a record the row names that is missing or not the caller's to use).</summary>
    internal const string NotFoundCode = "record_creation.not_found";

    /// <summary>A column the server owns was named in the caller's payload.</summary>
    internal const string BlockedColumnCode = "record_creation.blocked_column";

    /// <summary>The caller's own rights refused the create (a table privilege, a field-secured column).</summary>
    internal const string DeniedCode = "record_creation.denied";

    /// <summary>The payload is not a Dataverse Web API payload this service accepts.</summary>
    internal const string InvalidPayloadCode = "record_creation.invalid_payload";

    /// <summary>The table has no entry in <see cref="RecordTableRules"/>: this service does not create it.</summary>
    internal const string TableUnsupportedCode = "record_creation.table_unsupported";

    /// <summary>A check before the create faulted (an exception or timeout): nothing was created (NFR-01).</summary>
    internal const string CheckFailedCode = "record_creation.check_failed";

    /// <summary>The owner decision faulted: nothing was created (UAC-r2 condition (b), "faulted → 503 owner_unavailable").</summary>
    internal const string OwnerUnavailableCode = "record_creation.owner_unavailable";

    /// <summary>A system principal asked for a table whose pre-write rules are checked as the caller (fail closed).</summary>
    internal const string SystemPrincipalUnsupportedCode = "record_creation.system_principal_unsupported";

    /// <summary>A row the table's ownership exception keeps with its creator, which an application create cannot do.</summary>
    internal const string OwnershipExceptionCode = "record_creation.ownership_exception";

    /// <summary>The caller's checks ended without a decision (no arm matched): nothing was created.</summary>
    internal const string NoDecisionCode = "record_creation.no_decision";

    /// <summary>A caller's Dataverse failure that carried no mapped code of its own.</summary>
    internal const string UpstreamCode = "record_creation.dataverse";

    /// <summary>Every number the autonumber issued was already held (the Office create's code, kept for its clients).</summary>
    internal const string NumberUnavailableCode = "record_number_unavailable";

    /// <summary>
    /// Task 014 (unified-access-control-r2's review of 013, K-2): a server writer marked a lineage or ownership-parent lookup
    /// as server-set, which would skip the caller's AppendTo on the record it files the row under. Refused, nothing written
    /// (500: the writer's defect, not the caller's input).
    /// </summary>
    internal const string ServerSetFilingLookupCode = "record_creation.server_set_filing_lookup";

    public RecordCreationRefusal(RecordCreationRefusalKind kind, string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Kind = kind;
        Code = code;
        Detail = detail;
    }

    /// <summary>Why, as a closed kind (the HTTP status follows from it).</summary>
    public RecordCreationRefusalKind Kind { get; }

    /// <summary>The stable code (the ProblemDetails <c>errorCode</c>).</summary>
    public string Code { get; }

    /// <summary>What happened, for the caller. Never an upstream exception's or Dataverse's own text.</summary>
    public string Detail { get; }

    /// <summary>
    /// The caller's Dataverse failure; its text is never rendered. Set on an <see cref="RecordCreationRefusalKind.Upstream"/>
    /// refusal and, since task 018b-1s (G2), on a <see cref="RecordCreationRefusalKind.PrincipalUnresolved"/> refusal, where
    /// it is the WhoAmI failure (<c>AccessDenied</c>, <c>UserContextRequired</c>, <c>OboExchangeFailed</c> or
    /// <c>OboNotConfigured</c> as its <see cref="DataverseUserResponse.ErrorCode"/>) so an adapter can tell "no token" from "no
    /// Dataverse user". The refusal's code and status do not follow from it.
    /// </summary>
    public DataverseUserResponse? ClientFailure { get; init; }

    /// <summary>
    /// Task 018b-1s (G5): on a step-5 <see cref="RecordCreationRefusalKind.NotFoundOrDenied"/> refusal, the lookup column of
    /// the caller's payload whose record was refused — the logical column name without <c>@odata.bind</c>
    /// (<c>sprk_regardingmatter</c>), or <c>sprk_regardingrecordid</c> for the ADR-024 regarding pair's record. <c>null</c> for
    /// every other refusal. Never the record's id; the detail, code and status are unchanged by it, and the HTTP map does not
    /// render it (the uniform not-found stays uniform).
    /// </summary>
    public string? RefusedColumn { get; init; }

    /// <summary>The pre-write rule's own refusal, by reference (ebilling-r1 correction (c)).</summary>
    public PreWriteRefusal? PreWrite { get; init; }

    /// <summary>The owner or secure-create plan answer that refused, when one did.</summary>
    public RecordOwnerResolution? OwnerDecision { get; init; }
}
