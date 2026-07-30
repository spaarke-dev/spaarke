using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Job B — APPLY (FR-10, task 031). Applies a <b>confirmed</b> pending field-update proposal (produced by
/// task 030 as an open <c>sprk_emailreviewlog</c> <c>Proposed</c> row) to the associated record, and writes the
/// append-only human-decision audit row.
/// </summary>
/// <remarks>
/// <para><b>Write identity (owner decision 2026-07-29, Option 2 — see <c>notes/031-write-identity-decision.md</c>):</b>
/// the record PATCH runs AS the confirming user via <c>MSCRMCallerID</c> impersonation (threaded through the
/// blessed <see cref="IActionSeam.UpdateRecordAsync"/> as <c>ImpersonateSystemUserId</c>) — NOT app-only, never a
/// client-supplied identity. The caller is resolved server-side from <see cref="ClaimsPrincipal"/>; an unresolved
/// caller fails closed (403). Effective privileges = intersection of the BFF app user and the confirming user, and
/// native <c>modifiedby</c> = the confirming human (the "who accepted the change" requirement).</para>
/// <para><b>Guardrails (all re-checked at apply time — never trust the client to have gated):</b>
/// (1) the proposal must be an OPEN <c>Proposed</c> row; (2) its <c>(entity, field)</c> must still be an ENABLED
/// <c>sprk_emailupdatefield</c> allow-list row; (3) the cited source text must still exist (NFR-06); (4) the value
/// is coerced fail-loud; (5) exactly one append-only <c>Applied</c> audit row is written — a mutate-without-audit
/// code path does not exist. Nothing deadline-bearing / privilege-adjacent auto-finalizes (ADR-015); the privilege
/// flag is carried, never decided.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> Existing — the <c>suggest-associations</c> family is
/// read-only and 030's propose writer never applies; no existing service applies a confirmed proposal.
/// Extension — reuses the blessed <see cref="IActionSeam"/> write core (no bespoke Dataverse write) + the shipped
/// <see cref="CitationVerifier"/> / <see cref="EmailUpdateFieldCoercion"/> helpers; the allow-list re-validation is
/// a deliberate independent apply-time check (defense in depth). Cost-of-doing-nothing — without apply, no record
/// ever becomes current from email (FR-10 fails) and a confirmed proposal has nowhere to land.</para>
/// </remarks>
public interface ICommunicationProposalApplyService
{
    /// <summary>
    /// Applies the confirmed proposal identified by <paramref name="reviewLogId"/> (the open <c>Proposed</c>
    /// <c>sprk_emailreviewlog</c> row id from the queue feed) under the confirming caller's impersonation, and
    /// writes the <c>Applied</c> audit row. Throws <see cref="SdapProblemException"/> (RFC 7807) for an unresolved
    /// caller (403), a missing/already-resolved proposal (404/409), a non-allow-listed field (403), an
    /// unverifiable citation (422), or a failed coercion/PATCH (422).
    /// </summary>
    Task<ApplyProposalResult> ApplyAsync(Guid reviewLogId, ClaimsPrincipal? caller, CancellationToken ct);
}

/// <summary>Result of a successful <see cref="ICommunicationProposalApplyService.ApplyAsync"/>.</summary>
public sealed record ApplyProposalResult(
    Guid ReviewLogId,
    Guid AuditLogId,
    string TargetEntity,
    Guid TargetRecordId,
    string TargetField,
    IReadOnlyList<string> FieldsUpdated);

/// <inheritdoc />
public sealed class CommunicationProposalApplyService : ICommunicationProposalApplyService
{
    // As-built sprk_emailreviewlog option-set integers (task 012 verification + notes/schema-to-create.md).
    private const int ReviewActionProposed = 100000001;
    private const int ReviewActionApproved = 100000002;
    private const int ReviewActionOverriden = 100000003;
    private const int ReviewActionDismissed = 100000004;
    private const int ReviewActionApplied = 100000005;
    private const int ReviewActorTypeHuman = 100000001; // as-built: Machine=100000000, Human=100000001

    private const string ReviewLogEntity = "sprk_emailreviewlog";
    private const string AllowListEntity = "sprk_emailupdatefield";
    private const string RecordTypeRefEntity = "sprk_recordtype_ref";

    private static readonly JsonSerializerOptions SuggestionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly IGenericEntityService _genericEntityService;
    private readonly IActionSeam _actionSeam;
    private readonly ICommunicationEnvelopeReader _envelopeReader;
    private readonly ILogger<CommunicationProposalApplyService> _logger;

    public CommunicationProposalApplyService(
        ICallerSystemUserResolver callerResolver,
        IGenericEntityService genericEntityService,
        IActionSeam actionSeam,
        ICommunicationEnvelopeReader envelopeReader,
        ILogger<CommunicationProposalApplyService> logger)
    {
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _actionSeam = actionSeam ?? throw new ArgumentNullException(nameof(actionSeam));
        _envelopeReader = envelopeReader ?? throw new ArgumentNullException(nameof(envelopeReader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApplyProposalResult> ApplyAsync(Guid reviewLogId, ClaimsPrincipal? caller, CancellationToken ct)
    {
        // (1) Resolve the confirming caller server-side; fail closed (403) — NEVER app-only, never client-supplied.
        var resolution = await _callerResolver.ResolveAsync(caller, ct).ConfigureAwait(false);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "CALLER_NOT_RESOLVED",
                title: "Caller Not Resolved",
                detail: "The caller could not be resolved to a Dataverse systemuser; the apply is refused (fail closed — the write must run under the confirming user's identity, never app-only).",
                statusCode: 403);
        }

        // (2) Load the proposal row.
        var row = await LoadReviewLogRowAsync(reviewLogId, ct).ConfigureAwait(false);
        if (row is null)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_NOT_FOUND",
                title: "Proposal Not Found",
                detail: $"No sprk_emailreviewlog proposal was found for id {reviewLogId}.",
                statusCode: 404);
        }

        var communicationRef = row.GetAttributeValue<EntityReference>("sprk_communication");
        var targetEntity = row.GetAttributeValue<string>("sprk_targetentity")?.Trim();
        var targetField = row.GetAttributeValue<string>("sprk_targetfield")?.Trim();
        var targetRecordIdRaw = row.GetAttributeValue<string>("sprk_targetrecordid")?.Trim();
        var action = row.GetAttributeValue<OptionSetValue>("sprk_action")?.Value;

        if (communicationRef is null
            || string.IsNullOrWhiteSpace(targetEntity)
            || string.IsNullOrWhiteSpace(targetField)
            || !Guid.TryParse(targetRecordIdRaw, out var targetRecordId))
        {
            throw new SdapProblemException(
                code: "PROPOSAL_MALFORMED",
                title: "Proposal Malformed",
                detail: "The proposal row is missing a communication, target entity, target field, or a parseable target record id.",
                statusCode: 422);
        }

        if (action != ReviewActionProposed)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_NOT_PENDING",
                title: "Proposal Not Pending",
                detail: "The referenced review-log row is not a pending Proposed row.",
                statusCode: 409);
        }

        // (3) Confirm the proposal is still OPEN (no later terminal row for the same (communication, entity, field)).
        await EnsureStillOpenAsync(reviewLogId, communicationRef.Id, targetEntity, targetField, ct).ConfigureAwait(false);

        // (4) Parse the machine suggestion (old→new + citation + flags).
        var suggestionJson = row.GetAttributeValue<string>("sprk_aisuggestion");
        var suggestion = ParseSuggestion(suggestionJson);
        if (suggestion is null || string.IsNullOrWhiteSpace(suggestion.NewValue))
        {
            throw new SdapProblemException(
                code: "PROPOSAL_MALFORMED",
                title: "Proposal Malformed",
                detail: "The proposal's sprk_aisuggestion payload is missing or has no newValue.",
                statusCode: 422);
        }

        // (5) Apply-time allow-list re-validation (SOLE gate; never trust client gating). Also yields the
        //     authoritative field type for coercion.
        var allowedFieldType = await ResolveEnabledAllowListFieldTypeAsync(targetEntity, targetField, ct).ConfigureAwait(false);
        if (allowedFieldType is null)
        {
            throw new SdapProblemException(
                code: "FIELD_NOT_ALLOWED",
                title: "Field Not Allow-Listed",
                detail: $"'{targetField}' on '{targetEntity}' is not an enabled sprk_emailupdatefield allow-list entry; the update is refused at apply time.",
                statusCode: 403);
        }

        // Lookup application is out of scope for this iteration (resolving a label → target id is non-trivial and
        // security-sensitive). Refuse fail-closed rather than write an incorrect value to a lookup column.
        if (string.Equals(allowedFieldType, "Lookup", StringComparison.OrdinalIgnoreCase))
        {
            throw new SdapProblemException(
                code: "LOOKUP_APPLY_UNSUPPORTED",
                title: "Lookup Apply Unsupported",
                detail: $"'{targetField}' is a Lookup field; automated apply of lookup proposals is not supported in this iteration.",
                statusCode: 422);
        }

        // (6) Re-verify the cited source text still exists against the live message (NFR-06).
        var (message, _) = await _envelopeReader.ReconstructEnvelopeAsync(communicationRef.Id, ct).ConfigureAwait(false);
        var sourceText = CitationVerifier.BuildSourceText(message.Subject, message.BodyText, message.AttachmentText);
        if (!CitationVerifier.IsCitedTextPresent(sourceText, suggestion.Citation?.QuotedText))
        {
            throw new SdapProblemException(
                code: "CITATION_NOT_FOUND",
                title: "Citation Not Verifiable",
                detail: "The proposal's cited source text no longer exists in the communication; the apply is refused (NFR-06).",
                statusCode: 422);
        }

        // (7) Coerce the value fail-loud per the allow-listed field type.
        if (!EmailUpdateFieldCoercion.TryCoerce(allowedFieldType, suggestion.NewValue, out var coercedValue)
            || coercedValue is null)
        {
            throw new SdapProblemException(
                code: "VALUE_COERCION_FAILED",
                title: "Value Coercion Failed",
                detail: $"The proposed value '{suggestion.NewValue}' could not be coerced to field type '{allowedFieldType}'.",
                statusCode: 422);
        }

        // (8) Apply via the blessed write core UNDER THE CONFIRMING USER'S IMPERSONATION. The value is passed as a
        //     String mapping so UpdateRecordActionCore's metadata-driven coercion resolves Choice/Boolean/Number
        //     against the real column type (fail-loud) and passes Text/DateTime through verbatim.
        var updateResult = await _actionSeam.UpdateRecordAsync(
            new UpdateRecordRequest
            {
                EntityLogicalName = targetEntity,
                RecordId = targetRecordId,
                FieldMappings = new[]
                {
                    new ActionFieldMapping(targetField, ActionFieldType.String, coercedValue),
                },
                ImpersonateSystemUserId = callerSystemUserId,
            },
            ct).ConfigureAwait(false);

        if (!updateResult.Success)
        {
            throw new SdapProblemException(
                code: "APPLY_FAILED",
                title: "Apply Failed",
                detail: updateResult.Error ?? "The record update failed.",
                statusCode: 422);
        }

        // (9) Write EXACTLY ONE append-only Applied audit row (actor = the confirming human). A mutate-without-audit
        //     path does not exist: the record was just PATCHed; if this audit write fails we surface it loudly (the
        //     mutation details are logged) rather than swallowing it.
        Guid auditLogId;
        try
        {
            auditLogId = await WriteAppliedAuditRowAsync(
                communicationRef.Id, targetEntity, targetRecordId, targetField,
                suggestion, suggestionJson, callerSystemUserId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Job B apply MUTATED {Entity}({RecordId}).{Field} for caller {Caller} from proposal {ReviewLogId} but the Applied audit row write FAILED. Manual audit reconciliation required. Suggestion: {Suggestion}",
                targetEntity, targetRecordId, targetField, callerSystemUserId, reviewLogId, suggestionJson);
            throw new SdapProblemException(
                code: "AUDIT_WRITE_FAILED",
                title: "Audit Write Failed",
                detail: "The record was updated but the audit row could not be written; this has been logged for reconciliation.",
                statusCode: 500);
        }

        _logger.LogInformation(
            "Job B apply: {Entity}({RecordId}).{Field} updated under caller {Caller} impersonation from proposal {ReviewLogId}; audit row {AuditLogId} written.",
            targetEntity, targetRecordId, targetField, callerSystemUserId, reviewLogId, auditLogId);

        return new ApplyProposalResult(
            reviewLogId, auditLogId, targetEntity, targetRecordId, targetField, updateResult.FieldsUpdated);
    }

    private async Task<Entity?> LoadReviewLogRowAsync(Guid reviewLogId, CancellationToken ct)
    {
        try
        {
            return await _genericEntityService.RetrieveAsync(
                ReviewLogEntity,
                reviewLogId,
                new[]
                {
                    "sprk_communication", "sprk_action", "sprk_targetentity", "sprk_targetrecordid",
                    "sprk_targetfield", "sprk_confidence", "sprk_sourceref", "sprk_aisuggestion",
                },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "sprk_emailreviewlog {ReviewLogId} could not be retrieved (treated as not-found).", reviewLogId);
            return null;
        }
    }

    /// <summary>
    /// Walks all review-log rows for the proposal's <c>(communication, targetEntity, targetField)</c> key
    /// chronologically; the currently-open Proposed row MUST be <paramref name="reviewLogId"/>. A later terminal
    /// (Approved/Applied/Dismissed/Overriden) means it was already resolved (409) — an idempotency + double-apply guard.
    /// <para>
    /// <b>Concurrency (accepted, narrowed limitation — see notes/031-write-identity-decision.md):</b> this is a
    /// read-then-act check with no Dataverse transaction spanning walk → PATCH → audit, so two TRULY-CONCURRENT
    /// applies of the same proposal could both pass here. SEQUENTIAL re-apply IS blocked (the first Applied row
    /// closes the proposal). The practical guard is r5 disabling the confirm control on submit; a hard guard needs a
    /// Dataverse alternate-key / duplicate-detection rule on <c>sprk_emailreviewlog</c> (operator schema — deferred
    /// to task 060 deploy). The blast radius of the race is one duplicate field-write (idempotent for the same value)
    /// + one extra audit row, both under the confirming user's identity — not a privilege or allow-list bypass.
    /// </para>
    /// </summary>
    private async Task EnsureStillOpenAsync(
        Guid reviewLogId, Guid communicationId, string targetEntity, string targetField, CancellationToken ct)
    {
        var query = new QueryExpression(ReviewLogEntity)
        {
            ColumnSet = new ColumnSet("sprk_action", "sprk_targetentity", "sprk_targetfield", "createdon"),
        };
        query.Criteria.AddCondition("sprk_communication", ConditionOperator.Equal, communicationId);
        query.Criteria.AddCondition("sprk_targetentity", ConditionOperator.Equal, targetEntity);
        query.Criteria.AddCondition("sprk_targetfield", ConditionOperator.Equal, targetField);
        query.AddOrder("createdon", OrderType.Ascending);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        Guid openProposedId = Guid.Empty;
        foreach (var r in result.Entities)
        {
            switch (r.GetAttributeValue<OptionSetValue>("sprk_action")?.Value)
            {
                case ReviewActionProposed:
                    openProposedId = r.Id;
                    break;
                case ReviewActionApproved:
                case ReviewActionApplied:
                case ReviewActionDismissed:
                case ReviewActionOverriden:
                    openProposedId = Guid.Empty;
                    break;
            }
        }

        if (openProposedId != reviewLogId)
        {
            throw new SdapProblemException(
                code: "PROPOSAL_ALREADY_RESOLVED",
                title: "Proposal Already Resolved",
                detail: "This proposal is no longer the open pending proposal for its field (it was already applied, dismissed, or superseded).",
                statusCode: 409);
        }
    }

    /// <summary>
    /// Apply-time allow-list check: resolves the entity's <c>sprk_recordtype_ref</c> id, then looks for an ENABLED
    /// <c>sprk_emailupdatefield</c> row for <paramref name="fieldLogicalName"/>. Returns the authoritative
    /// <c>sprk_fieldtype</c> label when allowed, else null (refuse).
    /// </summary>
    private async Task<string?> ResolveEnabledAllowListFieldTypeAsync(
        string entityLogicalName, string fieldLogicalName, CancellationToken ct)
    {
        var refQuery = new QueryExpression(RecordTypeRefEntity)
        {
            ColumnSet = new ColumnSet(false),
            TopCount = 1,
        };
        refQuery.Criteria.AddCondition("sprk_recordlogicalname", ConditionOperator.Equal, entityLogicalName);
        var refResult = await _genericEntityService.RetrieveMultipleAsync(refQuery, ct).ConfigureAwait(false);
        var recordTypeRef = refResult.Entities.FirstOrDefault();
        if (recordTypeRef is null)
            return null;

        var query = new QueryExpression(AllowListEntity)
        {
            ColumnSet = new ColumnSet("sprk_fieldtype"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("sprk_enabled", ConditionOperator.Equal, true);
        query.Criteria.AddCondition("sprk_targetentity", ConditionOperator.Equal, recordTypeRef.Id);
        query.Criteria.AddCondition("sprk_targetfieldlogicalname", ConditionOperator.Equal, fieldLogicalName);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var allowRow = result.Entities.FirstOrDefault();
        if (allowRow is null)
            return null;

        var fieldTypeValue = allowRow.GetAttributeValue<OptionSetValue>("sprk_fieldtype")?.Value;
        return fieldTypeValue.HasValue ? EmailUpdateFieldTypes.FromOptionSet(fieldTypeValue.Value) : null;
    }

    private async Task<Guid> WriteAppliedAuditRowAsync(
        Guid communicationId, string targetEntity, Guid targetRecordId, string targetField,
        ProposalSuggestion suggestion, string? suggestionJson, Guid callerSystemUserId, CancellationToken ct)
    {
        var name = $"Applied update: {targetEntity}.{targetField}";
        var entity = new Entity(ReviewLogEntity)
        {
            ["sprk_name"] = Truncate(name, 850),
            ["sprk_communication"] = new EntityReference("sprk_communication", communicationId),
            ["sprk_actortype"] = new OptionSetValue(ReviewActorTypeHuman),
            ["sprk_action"] = new OptionSetValue(ReviewActionApplied),
            ["sprk_actor"] = Truncate(callerSystemUserId.ToString(), 200),
            ["sprk_targetentity"] = Truncate(targetEntity, 100),
            ["sprk_targetrecordid"] = Truncate(targetRecordId.ToString(), 100),
            ["sprk_targetfield"] = Truncate(targetField, 100),
            ["sprk_sourceref"] = Truncate(suggestion.Citation?.Locator ?? suggestion.Citation?.Source ?? string.Empty, 1000),
        };

        if (suggestion.Confidence.HasValue)
            entity["sprk_confidence"] = (decimal)suggestion.Confidence.Value;

        // Carry the machine suggestion forward so the Applied row is self-contained (AI proposed / human approved /
        // old→new / citation / confidence).
        if (!string.IsNullOrWhiteSpace(suggestionJson))
            entity["sprk_aisuggestion"] = suggestionJson;

        return await _genericEntityService.CreateAsync(entity, ct).ConfigureAwait(false);
    }

    private static ProposalSuggestion? ParseSuggestion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ProposalSuggestion>(json, SuggestionJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private sealed record ProposalSuggestion
    {
        [JsonPropertyName("field")] public string? Field { get; init; }
        [JsonPropertyName("fieldType")] public string? FieldType { get; init; }
        [JsonPropertyName("oldValue")] public string? OldValue { get; init; }
        [JsonPropertyName("newValue")] public string? NewValue { get; init; }
        [JsonPropertyName("citation")] public ProposalCitation? Citation { get; init; }
        [JsonPropertyName("reason")] public string? Reason { get; init; }
        [JsonPropertyName("confidence")] public double? Confidence { get; init; }
        [JsonPropertyName("requireConfirm")] public bool? RequireConfirm { get; init; }
        [JsonPropertyName("privilegeFlagged")] public bool? PrivilegeFlagged { get; init; }
    }

    private sealed record ProposalCitation
    {
        [JsonPropertyName("source")] public string? Source { get; init; }
        [JsonPropertyName("locator")] public string? Locator { get; init; }
        [JsonPropertyName("quotedText")] public string? QuotedText { get; init; }
    }
}
