using System.Security.Claims;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The FR-17 ranked-exceptions queue-feed (task 032) — the SOLE data source for r5's Exceptions Queue surface
/// (C-3: r1 supplies the feed, r5 builds no surface here). Composes TWO already-produced signals into ONE
/// dual-use <see cref="QueueFeedItem"/> shape (D-10):
/// <list type="bullet">
///   <item>Communications whose association still needs a human decision (unresolved / Suggested / Ambiguous —
///   <see cref="AssociationStatusCodes"/>, anything other than Resolved).</item>
///   <item>OPEN pending Job B field-update proposals (task 030's <c>sprk_emailreviewlog</c> <c>Proposed</c> rows
///   with no later terminal row for the same (communication, target entity, target field) — the SAME "open"
///   definition <c>CommunicationEnrichmentService.LoadOpenProposalFieldsAsync</c> uses, generalized here across a
///   scope of communications instead of one).</item>
/// </list>
/// <para>
/// <b>Access posture — mirrors <see cref="CommunicationThreadReadService"/> exactly (no new mechanism):</b> the
/// candidate communications are read IMPERSONATED (<c>MSCRMCallerID</c> = the caller's <c>systemuserid</c>) via
/// <see cref="IImpersonatedCommunicationQuery"/>, so Dataverse row-level security decides which communications the
/// caller may see. The SAME shared <see cref="ICommunicationAccessFilter"/> (internal-only + privilege) then runs
/// on top — no second/divergent filter. The <c>sprk_emailreviewlog</c> proposal lookup runs via
/// <see cref="IGenericEntityService"/> (app-only, the same seam task 030 uses to write these rows), but is
/// SCOPED EXCLUSIVELY to communication ids already proven visible by the impersonated read above — a proposal on a
/// communication the caller may not see is never queried for, let alone returned (no over-disclosure).
/// </para>
/// <para>
/// <b>Ranking (D-08 — no second scoring scheme):</b> every item ranks on the PARENT communication's
/// already-computed <c>sprk_triagepriority</c> (tasks 022–024) + <c>sprk_riconfidence</c> (tasks 024/025) — this
/// service performs NO re-scoring, NO new priority formula, and does not rank a proposal by its own extraction
/// confidence (that value is display-only on the DTO).
/// </para>
/// <para>
/// <b>Component Justification (CLAUDE.md §11):</b> (1) Existing — <see cref="CommunicationThreadReadService"/>
/// already composes the impersonated read + shared access filter for every other communication read surface, and
/// task 030 already writes the <c>sprk_emailreviewlog</c> Proposed-row store; there is no existing ranked,
/// scope-wide exceptions read. (2) Extension — this type is a THIN composition over those two existing primitives
/// (same query seam, same filter, same "open proposal" definition) — no new access filter, no new proposal store,
/// no new query mechanism. (3) Cost-of-doing-nothing — without it r5's Exceptions Queue surface has no data source
/// (FR-17 fails; the C-3 hand-off to r5 breaks).
/// </para>
/// </summary>
public sealed class CommunicationQueueFeedService
{
    // sprk_communication columns this feed projects (as-built, notes/messaging-schema-spec.md +
    // notes/schema-to-create.md triage fields).
    private const string CommunicationSet = "sprk_communications";
    private const string PkField = "sprk_communicationid";
    private const string SubjectField = "sprk_subject";
    private const string FromField = "sprk_from";
    private const string SentAtField = "sprk_sentat";
    private const string CreatedOnField = "createdon";
    private const string AssociationStatusField = "sprk_associationstatus";
    private const string TriagePriorityField = "sprk_triagepriority";
    private const string RiConfidenceField = "sprk_riconfidence";
    private const string InternalOnlyField = "sprk_isinternalonly";
    private const string PrivilegeField = "sprk_privilegeclassification";

    // sprk_emailreviewlog columns + as-built sprk_action option-set integers (task 012 verification, mirrored
    // from CommunicationEnrichmentService — the SAME "open proposal" tuple-walk this feed generalizes).
    private const string ReviewLogEntity = "sprk_emailreviewlog";
    private const string ReviewCommunicationField = "sprk_communication";
    private const string ReviewActionField = "sprk_action";
    private const string ReviewTargetEntityField = "sprk_targetentity";
    private const string ReviewTargetRecordIdField = "sprk_targetrecordid";
    private const string ReviewTargetFieldField = "sprk_targetfield";
    private const string ReviewConfidenceField = "sprk_confidence";
    private const string ReviewSuggestionField = "sprk_aisuggestion";
    private const string ReviewCreatedOnField = "createdon";

    private const int ReviewActionProposed = 100000001;
    private const int ReviewActionApproved = 100000002;
    private const int ReviewActionOverriden = 100000003;
    private const int ReviewActionDismissed = 100000004;
    private const int ReviewActionApplied = 100000005;

    /// <summary>Bounds the candidate communication scan per feed request (NFR-07 — no per-row fan-out).</summary>
    private const int MaxCandidateScan = 200;

    private const int DefaultTop = 50;
    private const int MaxTop = 200;

    private static readonly JsonSerializerOptions SuggestionJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IImpersonatedCommunicationQuery _query;
    private readonly ICommunicationAccessFilter _accessFilter;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ISystemUserIdentityResolver _identityResolver;
    private readonly IGenericEntityService _genericEntityService;
    private readonly ILogger<CommunicationQueueFeedService> _logger;

    public CommunicationQueueFeedService(
        IImpersonatedCommunicationQuery query,
        ICommunicationAccessFilter accessFilter,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        IGenericEntityService genericEntityService,
        ILogger<CommunicationQueueFeedService> logger)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _accessFilter = accessFilter ?? throw new ArgumentNullException(nameof(accessFilter));
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _genericEntityService = genericEntityService ?? throw new ArgumentNullException(nameof(genericEntityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the caller's ranked exceptions feed. <paramref name="regarding"/> (optional, <c>{entityType}:{guid}</c>)
    /// narrows the scope to one record's communications (matter / project / etc. — any ADR-024 regarding family);
    /// omitted, the scope is every communication the caller may see (bounded by <see cref="MaxCandidateScan"/>,
    /// the SAME NFR-07 bound pattern <see cref="CommunicationThreadReadService"/> uses). <paramref name="top"/>
    /// caps the returned, already-ranked item count (default <see cref="DefaultTop"/>, ceiling <see cref="MaxTop"/>).
    /// </summary>
    public async Task<QueueFeedResult> GetQueueFeedAsync(
        string? regarding,
        int? top,
        ClaimsPrincipal? caller,
        CancellationToken ct)
    {
        var callerSystemUserId = await ResolveCallerOrThrowAsync(caller, ct).ConfigureAwait(false);
        var pageSize = NormalizeTop(top);

        // 1) Impersonated candidate read — Dataverse row-level security decides which communications the caller
        // may see. Optional regarding facet narrows the scope; omitted, every visible communication is a candidate.
        var select = string.Join(',', new[]
        {
            PkField, SubjectField, FromField, SentAtField, CreatedOnField,
            AssociationStatusField, TriagePriorityField, RiConfidenceField, InternalOnlyField, PrivilegeField,
        });
        var filterPart = string.IsNullOrWhiteSpace(regarding) ? string.Empty : $"&$filter={BuildRegardingClause(regarding)}";
        var odata = $"$select={select}{filterPart}&$orderby={CreatedOnField} desc&$top={MaxCandidateScan}";
        var rows = await _query.QueryAsync(CommunicationSet, odata, callerSystemUserId, ct).ConfigureAwait(false);

        var parsed = rows.Select(ParseCandidateRow).ToList();

        // 2) SAME shared access filter as every other communication read (internal-only + privilege) — no second
        // filter, no over-disclosure (NFR-03/NFR-06).
        var isExternal = await _identityResolver.IsExternalAsync(callerSystemUserId, ct).ConfigureAwait(false);
        var context = new CommunicationAccessContext(CallerSystemUserId: callerSystemUserId, IsInternalUser: !isExternal);
        var filtered = _accessFilter.FilterMessages(context, parsed.Select(p => p.Entity).ToList());

        var visibleIds = new HashSet<Guid>(filtered.Decisions.Where(d => d.Decision.IsVisible).Select(d => d.Message.Id));
        var visibleById = parsed.Where(p => visibleIds.Contains(p.CommunicationId)).ToDictionary(p => p.CommunicationId);

        // 3) Association-exception items — visible communications whose ladder status still needs a human
        // decision (anything other than Resolved). No re-scoring: the status + rank inputs are read straight off
        // the already-persisted row.
        var exceptionItems = visibleById.Values
            .Where(c => c.AssociationStatus.HasValue && c.AssociationStatus.Value != AssociationStatusCodes.Resolved)
            .Select(c => new QueueFeedItem(
                CommunicationId: c.CommunicationId,
                Kind: QueueFeedItemKinds.AssociationException,
                Subject: c.Subject,
                From: c.From,
                SentAt: c.SentAt,
                TriagePriority: c.TriagePriority,
                RiConfidence: c.RiConfidence,
                AssociationStatus: AssociationStatusCodes.Name(c.AssociationStatus!.Value),
                ReviewLogId: null,
                TargetEntity: null,
                TargetRecordId: null,
                TargetField: null,
                FieldType: null,
                OldValue: null,
                NewValue: null,
                ProposalConfidence: null,
                CitationSource: null,
                CitationLocator: null,
                CitationQuotedText: null,
                Reason: null,
                RequireConfirm: null,
                PrivilegeFlagged: null))
            .ToList();

        // 4) Pending-proposal items — task 030's "open" definition (a Proposed row with no later terminal row for
        // the same (communication, target entity, target field)), generalized across the visible scope. Scoped
        // EXCLUSIVELY to visible communication ids (step 2) — a proposal on a communication the caller may not see
        // is never queried for.
        var proposalItems = visibleById.Count == 0
            ? new List<QueueFeedItem>()
            : await LoadOpenProposalItemsAsync(visibleById, ct).ConfigureAwait(false);

        // 5) Rank: ascending sprk_triagepriority (lower int = more urgent; untriaged sorts last), then descending
        // sprk_riconfidence, then descending recency. Reuses existing signals only (D-08).
        var ranked = exceptionItems.Concat(proposalItems)
            .OrderBy(i => i.TriagePriority ?? int.MaxValue)
            .ThenByDescending(i => i.RiConfidence ?? 0.0)
            .ThenByDescending(i => i.SentAt ?? DateTimeOffset.MinValue)
            .Take(pageSize)
            .ToList();

        _logger.LogDebug(
            "[QUEUE-FEED] caller={Caller} regarding={Regarding} candidates={Candidates} visible={Visible} exceptions={Exceptions} proposals={Proposals} returned={Returned}",
            callerSystemUserId, regarding ?? "(none)", rows.Count, visibleById.Count, exceptionItems.Count, proposalItems.Count, ranked.Count);

        return new QueueFeedResult(ranked, ranked.Count);
    }

    // ── pending proposals (task 030's "open" definition, generalized across a scope) ───────────────────

    private async Task<List<QueueFeedItem>> LoadOpenProposalItemsAsync(
        IReadOnlyDictionary<Guid, CandidateCommunication> visibleById,
        CancellationToken ct)
    {
        var query = new QueryExpression(ReviewLogEntity)
        {
            ColumnSet = new ColumnSet(
                ReviewCommunicationField, ReviewActionField, ReviewTargetEntityField, ReviewTargetRecordIdField,
                ReviewTargetFieldField, ReviewConfidenceField, ReviewSuggestionField, ReviewCreatedOnField),
        };
        query.Criteria.AddCondition(ReviewCommunicationField, ConditionOperator.In, visibleById.Keys.Cast<object>().ToArray());
        query.AddOrder(ReviewCreatedOnField, OrderType.Ascending);

        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        // Walk chronologically per (communication, targetEntity, targetField): a Proposed row opens (tracked as
        // the current open row); a terminal row (Approved/Applied/Dismissed/Overriden) closes it. Whatever remains
        // open at the end of the walk is what this feed surfaces — the SAME definition
        // CommunicationEnrichmentService.LoadOpenProposalFieldsAsync uses per-communication (030 note), generalized
        // here to walk many communications' rows in one pass.
        var openByKey = new Dictionary<(Guid CommunicationId, string TargetEntity, string TargetField), Entity>();
        foreach (var row in result.Entities)
        {
            var communicationRef = row.GetAttributeValue<EntityReference>(ReviewCommunicationField);
            var targetEntity = row.GetAttributeValue<string>(ReviewTargetEntityField)?.Trim();
            var targetField = row.GetAttributeValue<string>(ReviewTargetFieldField)?.Trim();
            if (communicationRef is null || string.IsNullOrEmpty(targetEntity) || string.IsNullOrEmpty(targetField))
            {
                continue;
            }

            var key = (communicationRef.Id, targetEntity, targetField);
            var action = row.GetAttributeValue<OptionSetValue>(ReviewActionField)?.Value;
            switch (action)
            {
                case ReviewActionProposed:
                    openByKey[key] = row;
                    break;
                case ReviewActionApproved:
                case ReviewActionApplied:
                case ReviewActionDismissed:
                case ReviewActionOverriden:
                    openByKey.Remove(key);
                    break;
            }
        }

        var items = new List<QueueFeedItem>(openByKey.Count);
        foreach (var row in openByKey.Values)
        {
            var communicationRef = row.GetAttributeValue<EntityReference>(ReviewCommunicationField);
            if (communicationRef is null || !visibleById.TryGetValue(communicationRef.Id, out var candidate))
            {
                continue; // defensive — the id came from visibleById's own key set
            }

            var suggestion = TryParseSuggestion(row.GetAttributeValue<string>(ReviewSuggestionField));
            var proposalConfidence = row.GetAttributeValue<decimal?>(ReviewConfidenceField);

            items.Add(new QueueFeedItem(
                CommunicationId: candidate.CommunicationId,
                Kind: QueueFeedItemKinds.PendingProposal,
                Subject: candidate.Subject,
                From: candidate.From,
                SentAt: candidate.SentAt,
                TriagePriority: candidate.TriagePriority,
                RiConfidence: candidate.RiConfidence,
                AssociationStatus: null,
                ReviewLogId: row.Id,
                TargetEntity: row.GetAttributeValue<string>(ReviewTargetEntityField),
                TargetRecordId: row.GetAttributeValue<string>(ReviewTargetRecordIdField),
                TargetField: row.GetAttributeValue<string>(ReviewTargetFieldField),
                FieldType: suggestion?.FieldType,
                OldValue: suggestion?.OldValue,
                NewValue: suggestion?.NewValue,
                ProposalConfidence: proposalConfidence.HasValue ? (double)proposalConfidence.Value : null,
                CitationSource: suggestion?.Citation?.Source,
                CitationLocator: suggestion?.Citation?.Locator,
                CitationQuotedText: suggestion?.Citation?.QuotedText,
                Reason: suggestion?.Reason,
                RequireConfirm: suggestion?.RequireConfirm,
                PrivilegeFlagged: suggestion?.PrivilegeFlagged));
        }

        return items;
    }

    /// <summary>Best-effort parse of task 030's <c>sprk_aisuggestion</c> JSON — a malformed/missing payload degrades
    /// to null fields (the item still surfaces with its typed columns) rather than failing the whole feed.</summary>
    private ProposalSuggestion? TryParseSuggestion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProposalSuggestion>(json, SuggestionJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "[QUEUE-FEED] Failed to parse sprk_aisuggestion JSON (non-fatal; proposal surfaces with typed columns only).");
            return null;
        }
    }

    private sealed record ProposalSuggestion(
        string? Field,
        string? FieldType,
        string? OldValue,
        string? NewValue,
        ProposalCitation? Citation,
        string? Reason,
        double? Confidence,
        bool? RequireConfirm,
        bool? PrivilegeFlagged);

    private sealed record ProposalCitation(string? Source, string? Locator, string? QuotedText);

    // ── regarding-scope facet (mirrors CommunicationThreadReadService.BuildRegardingClause — same pattern,
    // no new query mechanism) ───────────────────────────────────────────────────────────────────────────

    private static string BuildRegardingClause(string regarding)
    {
        var separator = regarding.IndexOf(':');
        if (separator <= 0 || separator == regarding.Length - 1)
        {
            throw BadRequest("'regarding' must be '{entityType}:{guid}' (e.g. sprk_matter:<guid>).");
        }

        var entityType = regarding[..separator].Trim();
        var idText = regarding[(separator + 1)..].Trim();

        var regardingField = RegardingFieldMap.FieldFor(entityType);
        if (regardingField is null)
        {
            throw BadRequest($"'{entityType}' is not a supported regarding entity type (ADR-024 family).");
        }

        if (!Guid.TryParse(idText, out var recordId) || recordId == Guid.Empty)
        {
            throw BadRequest("'regarding' record id must be a GUID.");
        }

        return $"_{regardingField}_value eq {recordId}";
    }

    // ── candidate row parsing (impersonated OData JSON rows) ───────────────────────────────────────────

    private static CandidateCommunication ParseCandidateRow(Dictionary<string, JsonElement> row)
    {
        var communicationId = TryGuid(row, PkField) ?? Guid.Empty;
        var isInternalOnly = TryBool(row, InternalOnlyField);
        var privilege = TryInt(row, PrivilegeField);

        var entity = new Entity("sprk_communication", communicationId);
        if (isInternalOnly.HasValue)
        {
            entity[InternalOnlyField] = isInternalOnly.Value;
        }
        if (privilege.HasValue)
        {
            entity[PrivilegeField] = new OptionSetValue(privilege.Value);
        }

        return new CandidateCommunication(
            CommunicationId: communicationId,
            Subject: TryString(row, SubjectField),
            From: TryString(row, FromField),
            SentAt: TryDateTimeOffset(row, SentAtField),
            AssociationStatus: TryInt(row, AssociationStatusField),
            TriagePriority: TryInt(row, TriagePriorityField),
            RiConfidence: TryDouble(row, RiConfidenceField),
            Entity: entity);
    }

    private sealed record CandidateCommunication(
        Guid CommunicationId,
        string? Subject,
        string? From,
        DateTimeOffset? SentAt,
        int? AssociationStatus,
        int? TriagePriority,
        double? RiConfidence,
        Entity Entity);

    // ── caller resolution (fail-closed — mirrors CommunicationThreadReadService.ResolveCallerOrThrowAsync) ──────

    private async Task<Guid> ResolveCallerOrThrowAsync(ClaimsPrincipal? caller, CancellationToken ct)
    {
        var resolution = await _callerResolver.ResolveAsync(caller, ct).ConfigureAwait(false);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var systemUserId)
            || systemUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "[QUEUE-FEED] caller has no resolvable Dataverse systemuserid ({Reason}) — refusing the read (fail closed; no app-only fallback).",
                resolution.UnresolvedReason ?? "unresolved");
            throw new SdapProblemException(
                code: "QUEUE_FEED_FORBIDDEN",
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so the queue feed cannot be read.",
                statusCode: 403);
        }

        return systemUserId;
    }

    private static int NormalizeTop(int? top) => top is null or <= 0 ? DefaultTop : Math.Min(top.Value, MaxTop);

    private static SdapProblemException BadRequest(string detail) =>
        new(code: "VALIDATION_ERROR", title: "Validation Error", detail: detail, statusCode: 400);

    // ── OData / JSON helpers (mirror CommunicationThreadReadService's row-parsing helpers) ────────────────

    private static string? TryString(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryInt(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static double? TryDouble(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static bool? TryBool(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static Guid? TryGuid(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g) ? g : null;

    private static DateTimeOffset? TryDateTimeOffset(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var dto) ? dto : null;
}
