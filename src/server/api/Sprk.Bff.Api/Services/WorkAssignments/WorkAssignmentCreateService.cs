// spaarke-ontology-platform-r1 task 046 (D-21, D-59): the server-side create of a work assignment.
//
// Placement Justification (CLAUDE.md §10, .claude/constraints/bff-extensions.md): in the BFF. A work assignment is a
// secure-record ROOT and an invariant-bearing table, so its create belongs to the server write path (ADR-002 WP-1/WP-3);
// the invariant owners it composes (IRecordOwnershipResolver, SecureRootFilingGate / provisioning, the Assigned-To
// materializer) all live here. Request-scoped, inline (a user is waiting; ADR-001), no background work, no package, no AI
// capability (ADR-013).
//
// Component justification (CLAUDE.md §11):
//   (1) Existing — the create already exists for the chat tool: OwnedChildWrite.CreateAsync with the secure-create plan
//       (DataverseCreateRecordHandler.cs:341-373). The Create Work Assignment wizard writes through Xrm.WebApi (no server
//       invariants) and POST /api/v1/work-assignments was retired by unified-access-control-r2 task 166.
//   (2) Extension — this class adds NO create logic: it runs that same core in the same order the chat handler does
//       (G5 check as the caller → plan → app-only create, team-owned, creator stamped → complete an isolated create →
//       Assigned-To access), for the browser's Web API payload and for in-process callers (task 043).
//   (3) Cost of doing nothing — a work assignment created by the wizard is owned by the user in their own business unit
//       and, under a secure matter or project, readable by that unit until the 5-minute job secures it (I-13 L4); a
//       decision cannot create its Assign Work follow-on (D-21).

using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.WorkAssignments;

/// <summary>Why a work-assignment create was refused. Every kind means no row is left behind.</summary>
public enum WorkAssignmentCreateFailureKind
{
    /// <summary>The payload is not a create this route accepts (not a Web API payload, an unknown column, no name). 400.</summary>
    InvalidPayload,

    /// <summary>A record the payload binds does not exist or the caller cannot append to it — deliberately one answer. 404.</summary>
    ParentUnavailable,

    /// <summary>The caller's own rights refuse it (table privilege, a field-secured or server-owned column). 403.</summary>
    Denied,

    /// <summary>A question asked as the caller failed at Dataverse; <see cref="WorkAssignmentCreateFailure.StatusCode"/> is theirs.</summary>
    CallerFailure,

    /// <summary>No owner could be decided (the resolver's refusal). 409.</summary>
    OwnerRefused,

    /// <summary>Filed under a secure record and the caller may not create it there (walled off, no AppendTo). 403.</summary>
    SecureFilingRefused,

    /// <summary>Filed under a secure record and securing it could not be decided or completed; nothing is left. 500.</summary>
    SecureFilingFailed,
}

/// <summary>A refusal: the kind, a stable reason code, user-safe text and (for <see cref="WorkAssignmentCreateFailureKind.CallerFailure"/>) the status.</summary>
public sealed record WorkAssignmentCreateFailure(
    WorkAssignmentCreateFailureKind Kind, string Code, string Detail, int? StatusCode = null);

/// <summary>The created work assignment, or why it was not created.</summary>
public sealed record WorkAssignmentCreateResult
{
    /// <summary>The new <c>sprk_workassignmentid</c>; <see langword="null"/> on a refusal.</summary>
    public Guid? Id { get; init; }

    /// <summary>True when it was filed under a secure matter or project and so created secure (I-13 L1).</summary>
    public bool Isolated { get; init; }

    /// <summary>Non-fatal conditions the user should know (an isolated create not yet fully completed).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Set on a refusal.</summary>
    public WorkAssignmentCreateFailure? Failure { get; init; }

    internal static WorkAssignmentCreateResult Refused(
        WorkAssignmentCreateFailureKind kind, string code, string detail, int? status = null) =>
        new() { Failure = new WorkAssignmentCreateFailure(kind, code, detail, status) };
}

/// <summary>
/// Creates a <c>sprk_workassignment</c> through the server write path (task 046, D-21; owner decision D-59: on
/// unified-access-control-r2's root-create pattern, team-owned, creator stamped, secured at create under a secure parent).
/// </summary>
/// <remarks>
/// <para><b>Input.</b> The Dataverse Web API payload the Create Work Assignment wizard builds today
/// (<c>workAssignmentService.ts</c> <c>createWorkAssignment</c>): name, priority, description, response due date, the BU
/// search-index defaults, the regarding lookup with its ADR-024 resolver fields, field-mapped values, matter type,
/// practice area and the Assign Work lookups. The payload is mapped against the table's own metadata AS THE CALLER
/// (<see cref="DataverseWriteItemMapper.MapWebApiPayloadAsync"/>), so an unknown column or navigation property is a 400.</para>
/// <para><b>Authorization (G5), as the caller</b> (<see cref="OwnedChildWrite.CreateAsync"/>): the Create and Append
/// privileges on <c>sprk_workassignment</c>; <b>AppendTo on every record the payload binds — the regarding record every
/// time</b>, secure or not (D-59), and every Assign Work, matter-type, practice-area and record-type lookup — exactly what a
/// run-as-user create would have had Dataverse check; no owner, audit, creator-person or field-secured column. A record that
/// does not exist and one the caller cannot append to give the same answer.</para>
/// <para><b>Ownership</b> (intended differences from the wizard, parity table rows 13-16, accepted by D-59): the row is
/// created by the APPLICATION, owned by the team <see cref="IRecordOwnershipResolver"/> names (the regarding record's
/// business-unit team, else the caller's), with <c>sprk_createdbyperson</c> = the caller. Filed under a SECURE matter or
/// project (I-13), it is created INTO isolation — owned by the named Secure Record Owners team with <c>sprk_issecure</c> in
/// the create — and completed now by provisioning's own re-entry steps; a creator share that fails deletes the row again.</para>
/// <para><b>After the create</b>: the "Assigned *" contacts' access (I-12 L1,
/// <see cref="Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer"/>), never failing the create. The wizard's
/// own <c>syncAssignedAccess</c> call is therefore no longer needed for this create.</para>
/// <para><b>Not added</b> (wizard parity): the chat tool's "for person" default (<c>sprk_assignedtointernal</c> = the
/// caller's contact) — the wizard never sets that column, so this path does not either.</para>
/// <para>Request-scoped: <see cref="IDataverseUserClient"/> carries the caller's token. Task 043 calls
/// <see cref="CreateAsync"/> in-process from the decision commit route, inside the same request.</para>
/// </remarks>
public sealed class WorkAssignmentCreateService
{
    /// <summary>The table this service creates.</summary>
    public const string Table = "sprk_workassignment";

    /// <summary>The primary name column; a create without one is refused (ApplicationRequired is not enforced by the API).</summary>
    internal const string NameColumn = "sprk_name";

    /// <summary>The refusal code for a payload that names no work-assignment name.</summary>
    internal const string NameRequiredCode = "work_assignment.name_required";

    /// <summary>The refusal code for a body that is not a JSON object / Web API payload this route accepts.</summary>
    internal const string InvalidPayloadCode = "work_assignment.invalid_payload";

    /// <summary>The uniform not-found code: a bound record that does not exist or cannot be appended to.</summary>
    internal const string ParentUnavailableCode = "work_assignment.not_found";

    /// <summary>The caller's own rights refused the create.</summary>
    internal const string DeniedCode = "work_assignment.denied";

    private readonly IDataverseUserClient _user;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly IFieldMappingDataverseService _appOnly;
    private readonly SecureRootFilingGate _rootFiling;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<WorkAssignmentCreateService> _logger;

    public WorkAssignmentCreateService(
        IDataverseUserClient user,
        IRecordOwnershipResolver ownership,
        IFieldMappingDataverseService appOnly,
        SecureRootFilingGate rootFiling,
        IServiceScopeFactory scopes,
        ILogger<WorkAssignmentCreateService> logger)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _appOnly = appOnly ?? throw new ArgumentNullException(nameof(appOnly));
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates the work assignment described by <paramref name="webApiPayload"/>. A refusal is returned as data, with
    /// nothing written; a Dataverse transport fault on the app-only create itself propagates.
    /// </summary>
    /// <param name="webApiPayload">The Web API create payload (see the class remarks).</param>
    /// <param name="callerObjectId">The caller's Entra object id (the resolver's fallback key and the access grantor).</param>
    /// <param name="traceId">The request's correlation id, carried into an isolated create's completion.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<WorkAssignmentCreateResult> CreateAsync(
        JsonElement webApiPayload, Guid? callerObjectId, string? traceId, CancellationToken ct)
    {
        if (webApiPayload.ValueKind != JsonValueKind.Object)
        {
            return WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.InvalidPayload, InvalidPayloadCode,
                "Send the work assignment's fields as a JSON object. Nothing was created.");
        }

        if (!HasName(webApiPayload))
        {
            return WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.InvalidPayload, NameRequiredCode,
                "A work assignment needs a name. Nothing was created.");
        }

        // The payload mapped AS THE CALLER against the table's own metadata (navigation properties, lookup targets).
        var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(_user, Table, webApiPayload, ct).ConfigureAwait(false);
        if (mapped.ValidationError is { } invalid)
            return WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.InvalidPayload, InvalidPayloadCode, invalid);
        if (mapped.ClientFailure is { } mapFailure)
            return CallerFailure(mapFailure);

        // G5 as the caller, then the secure-create plan, then the app-only create owned by the resolver's team — the
        // chat tool's sequence (DataverseCreateRecordHandler.cs:341), with the gate passed so I-13 applies at create.
        var owned = await OwnedChildWrite.CreateAsync(
                _user, _ownership, _appOnly, Table, mapped.Item!, serverSetLookupColumns: null, callerObjectId, ct, _rootFiling)
            .ConfigureAwait(false);

        if (owned.CreatedId is not { } id)
            return Refusal(owned);

        var warnings = new List<string>();
        if (owned.Isolated is not null)
        {
            // Completed now through provisioning's own re-entry steps: the creator's share (read back), its own container,
            // its secure parents' sharees. The person is the one the plan checked and the create stamped — never asked again.
            var secured = await _rootFiling.CompleteIsolatedCreateAsync(Table, id, owned.IsolatedFor!.Value, traceId)
                .ConfigureAwait(false);

            if (secured.RowRemoved)
            {
                _logger.LogWarning(
                    "[WORK-ASSIGNMENT] isolated create {Id} removed again: the creator share failed ({Code})", id, secured.ReasonCode);
                return WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.SecureFilingFailed,
                    secured.ReasonCode ?? SecureRootInheritance.ReasonUnexpectedResult,
                    "The work assignment is filed under a secure record, so it is created secure and shared to you, and that " +
                    "share could not be made, so it was removed again. Nothing was created. " +
                    (secured.CompletesAutomatically
                        ? "Try again in a few minutes."
                        : $"Securing it was refused ({secured.ReasonCode}), and trying again will not change that: an " +
                          "administrator needs to review your access to the secure record it would be filed under."));
            }

            if (secured.RowStranded)
            {
                warnings.Add(
                    "The work assignment was created as a secure record, but it could not be shared to you and could not be " +
                    $"removed again ({secured.ReasonCode}); " +
                    (secured.CompletesAutomatically
                        ? "it is shared to you automatically once that step succeeds (it is retried every few minutes)."
                        : "it will not be shared to you automatically — an administrator needs to review it."));
            }
            else if (!secured.IsComplete)
            {
                warnings.Add(
                    "The work assignment was created as a secure record shared to you, but securing it could not be finished " +
                    $"yet ({secured.ReasonCode}); it is completed automatically within a few minutes.");
            }
        }

        // I-12 L1: the "Assigned *" contacts get their access now. Never throws; a fault is left to the job.
        await Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer.RunAfterWriteAsync(
                _scopes, Table, id, writtenColumns: null, grantorOid: callerObjectId?.ToString("D"), _logger, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "[WORK-ASSIGNMENT] created {Id} (G5, team-owned, isolated={Isolated}, warnings={Warnings})",
            id, owned.Isolated is not null, warnings.Count);

        return new WorkAssignmentCreateResult { Id = id, Isolated = owned.Isolated is not null, Warnings = warnings };
    }

    private static bool HasName(JsonElement payload) =>
        payload.EnumerateObject().Any(p =>
            string.Equals(p.Name.Trim(), NameColumn, StringComparison.OrdinalIgnoreCase)
            && p.Value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(p.Value.GetString()));

    private static WorkAssignmentCreateResult CallerFailure(DataverseUserResponse response) =>
        WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.CallerFailure,
            response.ErrorCode ?? "work_assignment.dataverse",
            response.ErrorMessage ?? "The work assignment could not be saved.",
            response.StatusCode is >= 400 and < 600 ? response.StatusCode : StatusCodes.Status502BadGateway);

    /// <summary>Maps the core's refusal onto this service's contract (nothing was written for any of them).</summary>
    private static WorkAssignmentCreateResult Refusal(OwnedChildWrite.Outcome owned) => owned switch
    {
        { ClientFailure: { } failure } => CallerFailure(failure),
        { ParentUnavailable: true } => WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.ParentUnavailable,
            ParentUnavailableCode, "A record this work assignment is filed under or names was not found. Nothing was created."),
        { Denied: { } denied } => WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.Denied, DeniedCode, denied),
        { PlanRefusal: { } plan } => PlanRefusal(plan),
        { OwnerRefusal: { } refusal } => WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.OwnerRefused,
            refusal.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource, refusal.Reason ?? "no owner could be resolved"),
        { SecureFilingRefused: { } secure } => WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.SecureFilingFailed,
            RecordOwnerRefusal.NoOwnerSource, secure),
        _ => WorkAssignmentCreateResult.Refused(WorkAssignmentCreateFailureKind.SecureFilingFailed,
            "work_assignment.no_decision", "The work assignment was not created."),
    };

    /// <summary>
    /// The secure-create plan's refusal (I-13 L1), split as <c>RecordCreationService.KindFor</c> splits a project's: a caller
    /// walled off a secure parent (No Access) or without AppendTo on a secure parent named only by the polymorphic pair is
    /// refused (403); a check that could not be made or a named team that could not be resolved is a failure (500); a parent
    /// whose secure flag cannot be read is the owner refusal (409) — never "not secure".
    /// </summary>
    private static WorkAssignmentCreateResult PlanRefusal(RecordOwnerResolution plan)
    {
        var code = plan.RefusalCode ?? RecordOwnerRefusal.ParentUndetermined;
        var kind = code switch
        {
            Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccess
                or DataverseUserClientErrorCodes.AccessDenied => WorkAssignmentCreateFailureKind.SecureFilingRefused,
            Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable
                or RecordOwnerRefusal.SecureOwnerTeamUnresolved => WorkAssignmentCreateFailureKind.SecureFilingFailed,
            _ => WorkAssignmentCreateFailureKind.OwnerRefused,
        };
        return WorkAssignmentCreateResult.Refused(kind, code,
            $"The work assignment was not created: {plan.Reason ?? "whether it may be filed there could not be decided"}.");
    }
}
