using System.Net;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.ExternalAccess;
using SearchIndexColumns = Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The ONE server create of a Dataverse record (field-mapping-server-write-path-r1 task 013, spec FR-06 / FR-07 / FR-12):
/// the twelve FR-06 steps, in order, for a CALLER principal (checked as the caller under their own token) or a SYSTEM
/// principal (a server writer that acts for nobody, or for a known person), ending in exactly ONE app-only create — or in
/// a typed <see cref="RecordCreationRefusal"/> with nothing written.
/// </summary>
/// <remarks>
/// <para><b>Ported, not redesigned.</b> Steps 1 and 5 to 7 and 11 are <c>OwnedChildWrite.CreateAsync</c>'s create
/// half (deleted in task 017 2d-5, once every caller was re-pointed here) (G5: check AS THE CALLER, create AS THE APP owned by the resolver's team), with the same order, the same owner
/// rule and the same refusal codes; the post-create steps are <c>ChildRecordEndpoints.CreateAsync</c>'s
/// (<c>CompleteRootCreateAsync</c>, the I-1 re-stamp, I-16, the sharee mirror) and the Office create's (the autonumber
/// retry and its number read-back, the membership event), in their order. Existing callers still call those cores: tasks
/// 017 and 018 re-point them. Field mapping (step 3) is task 014's (the partial <c>RecordCreationService.Mapping.cs</c>);
/// the owner (step 7, <see cref="DecideOwnerAsync"/>) and the search-index default (step 8,
/// <see cref="SearchIndexDefaultAsync"/>) are task 015's: record-first for every create, roots and children (FR-09), and
/// the OWNING business unit's search index, fill-only (FR-10).</para>
/// <para><b>Step 10 runs before the create, and again after it</b> (owner decision 2a, 2026-10-22; task 017). The I-1 stamp
/// columns the row is missing are derived from what it is filed under (<see cref="CoreAncestorRestamper.PlanMissingStampAsync"/>,
/// fill-only) and bound into the create payload; a derivation that fails refuses the create, nothing written
/// (<see cref="RecordCreationRefusal.StampUnderivableCode"/> 400 for a data condition,
/// <see cref="RecordCreationRefusal.StampUnavailableCode"/> 503 for a fault). I-16 (To Do, Event, Communication) is set in the
/// payload when it resolves and never refuses (display only). A document gets neither before the create (#2169). The
/// post-create re-derivation (<see cref="CoreAncestorRestamper.AfterWriteAsync"/>, then I-16) still runs inside step 12, in
/// its place. A system principal derives and refuses the same way; it has no caller to read the table's navigation metadata
/// as, so its derived stamp is bound by that post-create re-derivation (task 017 notes, Phase 2d-1).</para>
/// <para><b>Principals (FR-07, ADR-028).</b> A caller is named by WhoAmI under their own token; one Dataverse cannot name
/// is refused (403, <see cref="RecordCreationRefusalKind.PrincipalUnresolved"/>) before the payload is read. A system
/// principal skips the as-caller steps 2 and 5 only and records <see cref="RecordRequester"/> when it has one; every other
/// step is the caller's. Two things are checked only as a caller, so they refuse a system create (fail closed): a table's
/// pre-write rules, and filing a work assignment or project under a record (which may be secure, and is then shared to the
/// person after their own AppendTo).</para>
/// <para><b>Fail closed (NFR-01).</b> An exception or timeout in any step before the create refuses it (503) and writes
/// nothing; an owner decision that faults is <see cref="RecordCreationRefusal.OwnerUnavailableCode"/>. A create that faults
/// propagates — it may have landed, so it is never retried blindly (NFR-03): only an autonumber table's alternate-key
/// duplicate is retried, on the same id. Step 12 never throws: each post-create step logs its failure and the created id
/// is returned.</para>
/// <para><b>ADR-008, path A</b> (spec "ADR Tensions"): the payload-dependent checks live in
/// <see cref="CallerWriteAuthorization"/> and are called here, not written as endpoint filters. <b>ADR-010:</b> one
/// concrete scoped registration, no interface.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — two create cores, <c>OwnedChildWrite.CreateAsync</c>
/// (children, strong authorization) and the Office creation core (roots, weaker authorization); both are now deleted
/// (018c, 017 2d-5).
/// Extension — this merges them (spec §11 "Yes: merges the two existing cores"); tasks 017/018 re-pointed their callers,
/// and 018c deleted the Office core. Cost of doing nothing — the same row filed under the same parent gets a different owner, stamp and checks by
/// surface, and field mapping reaches 2 of about 30 create paths (spec §11).</para>
/// </remarks>
internal sealed partial class RecordCreationService
{
    /// <summary>Creates attempted per record on an autonumber table before the number is reported unavailable.</summary>
    internal const int MaxNumberAttempts = 3;

    /// <summary>
    /// ebilling-r1 correction (b), verbatim: what the chat create shows when a pre-write rule finds a record the row names
    /// missing or not the caller's to use. The ONE copy (task 017): <see cref="OwnedChildWrite"/>'s invoice-family step reads
    /// it too.
    /// </summary>
    internal const string NotAvailableToFileUnder =
        "A record this row names was not found, or you do not have permission to use it. Nothing was written.";

    /// <summary>
    /// The tables whose create publishes the owner membership event today (unified-access-control-r2 task 152: the four
    /// create sites — POST /api/v1/documents, POST /api/v1/events, Office quick-create matter, Office save). Tasks 017/018
    /// remove those sites' own publish when they re-point to this service.
    /// </summary>
    private static readonly IReadOnlySet<string> MembershipPublishingTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matter", "sprk_event", "sprk_document" };

    private readonly IRecordOwnershipResolver _ownership;
    private readonly IFieldMappingDataverseService _appOnly;
    private readonly IGenericEntityService _entities;
    private readonly SecureRootFilingGate _rootFiling;
    private readonly CoreAncestorRestamper _restamper;
    private readonly SecureChildShareSynchronizer _shares;
    private readonly IServiceScopeFactory _scopes;
    private readonly IMembershipEventPublisher _membership;
    private readonly ILogger<RecordCreationService> _logger;

    public RecordCreationService(
        IRecordOwnershipResolver ownership,
        IFieldMappingDataverseService appOnly,
        IGenericEntityService entities,
        SecureRootFilingGate rootFiling,
        CoreAncestorRestamper restamper,
        SecureChildShareSynchronizer shares,
        IServiceScopeFactory scopes,
        IMembershipEventPublisher membership,
        ILogger<RecordCreationService> logger)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _appOnly = appOnly ?? throw new ArgumentNullException(nameof(appOnly));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _rootFiling = rootFiling ?? throw new ArgumentNullException(nameof(rootFiling));
        _restamper = restamper ?? throw new ArgumentNullException(nameof(restamper));
        _shares = shares ?? throw new ArgumentNullException(nameof(shares));
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _membership = membership ?? throw new ArgumentNullException(nameof(membership));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The per-table rules this service reads (<see cref="RecordTableRules.Find"/>). A test seam only: a seam test registers
    /// a fixture pre-write rule here (the invoice-family rule is not on this branch — ebilling-r1).
    /// </summary>
    internal Func<string, RecordTableRule?> TableRules { get; init; } = RecordTableRules.Find;

    /// <summary>The user-facing noun for a table in refusal texts: the child-record route's, with the two Office roots and the
    /// budget inquiry's service request (task 017, 2d-5) named.</summary>
    internal static string NounOf(string? table) => (table ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "sprk_matter" => "matter",
        "sprk_project" => "project",
        "sprk_servicerequest" => "service request",
        var other => Sprk.Bff.Api.Api.ChildRecordEndpoints.Noun(other),
    };

    /// <summary>
    /// Runs the FR-06 pipeline for <paramref name="request"/> as <paramref name="principal"/>: the created id (with any
    /// post-create warnings), or the refusal. Every step is recorded in <see cref="RecordCreationOutcome.Steps"/>, in order.
    /// </summary>
    /// <exception cref="ArgumentException">A system principal with a Web API payload — only a caller's payload is mapped
    /// (as the caller); a server writer passes a mapped item. Or a system principal with a
    /// <see cref="RecordCreationRequest.Mapping"/>: field mapping is applied for a caller only (task 014; a system
    /// principal's mapping needs an app-only metadata reader, which task 033 adds with the first system creator).</exception>
    internal async Task<RecordCreationOutcome> CreateAsync(
        RecordCreationRequest request, RecordCreationPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Table);
        ArgumentNullException.ThrowIfNull(request.Payload);
        if (principal is RecordCreationPrincipal.SystemPrincipal && request.Payload is not RecordCreationPayload.Mapped)
        {
            throw new ArgumentException(
                "A system create passes a mapped item: a Web API payload is mapped as the caller, and a system principal has none.",
                nameof(request));
        }

        if (principal is RecordCreationPrincipal.SystemPrincipal && request.Mapping is not null)
        {
            throw new ArgumentException(
                "Field mapping is applied to a caller's create only: the merged row is mapped and checked as the caller.",
                nameof(request));
        }

        // Task 018a: references and post-mapping defaults are judged as the caller (step 2's reference pass, step 5).
        if (principal is RecordCreationPrincipal.SystemPrincipal
            && (request.ReferenceLookupColumns is { Count: > 0 } || request.PostMappingDefaults is { Count: > 0 }))
        {
            throw new ArgumentException(
                "References and post-mapping defaults are a caller's create only: each is checked as the caller.",
                nameof(request));
        }

        var table = request.Table.Trim().ToLowerInvariant();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId;
        var trace = new StepTrace(table, correlationId, _logger);
        var noun = NounOf(table);

        var rule = TableRules(table);
        if (rule is null)
        {
            return trace.Refused(new RecordCreationRefusal(RecordCreationRefusalKind.Invalid,
                RecordCreationRefusal.TableUnsupportedCode, $"'{request.Table}' records are not created here."));
        }

        ValidateReferenceLookupColumns(table, rule, request);
        var defaults = PostMappingDefaultValues(table, rule, request);

        Decision decision;
        try
        {
            decision = await DecideAsync(table, rule, request, principal, defaults, noun, trace, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // NFR-01: a check that faulted is never a pass. The fault's text stays in the log.
            _logger.LogError(ex,
                "[RECORD-CREATE] create of {Table} refused: a check before the create faulted at {Step} (correlationId={CorrelationId})",
                table, trace.Current, correlationId);
            return trace.Refused(new RecordCreationRefusal(RecordCreationRefusalKind.Unavailable,
                RecordCreationRefusal.CheckFailedCode,
                $"A check before the {noun} was created could not be completed, so it was not created."));
        }

        if (decision.Refusal is { } refused)
            return trace.Refused(refused);

        // ── Step 11: ONE app-only create (alternate-key retry on an autonumber table only) ─────────────────────────────
        trace.Begin(RecordCreationStep.Create);
        var id = Guid.NewGuid();
        // Task 018b-1s (G1): the keys of the body this create sends, exactly as sent (a copy: the create may keep the instance).
        var writtenColumns = decision.Fields!.Keys.ToArray();
        // Only the platform's own number is retried: a row that names its number (a server writer's mapped item; a caller's
        // is refused at step 2) collides on THAT number, which no retry changes.
        var retriesTheNumber = rule.AutonumberColumn is not null && !decision.Item!.Columns.Any(c => IsNumberColumn(rule, c));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _appOnly.UpdateRecordFieldsAsync(table, id, decision.Fields!, ct).ConfigureAwait(false);
                break;
            }
            catch (Exception ex) when (retriesTheNumber && IsAlternateKeyDuplicate(ex))
            {
                if (attempt >= MaxNumberAttempts)
                {
                    _logger.LogError(ex,
                        "record_number_unavailable: entity={Entity} attempts={Attempts} — every number the platform issued was " +
                        "already held by a row; run scripts/Set-RecordNumberingSchema.ps1 -Apply to move the seed past them",
                        table, attempt);
                    return trace.Refused(new RecordCreationRefusal(RecordCreationRefusalKind.Conflict,
                        RecordCreationRefusal.NumberUnavailableCode,
                        $"The new {noun} could not be given a number because the next numbers are already in use, so it was " +
                        "not created. Try again; if it keeps failing, ask an administrator to check record numbering."));
                }

                _logger.LogWarning(
                    "[RECORD-CREATE] The {Noun}'s number was already held by another row (attempt {Attempt} of {Max}); retrying " +
                    "with the next number (correlationId={CorrelationId}).", noun, attempt, MaxNumberAttempts, correlationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // NFR-03: a create that faulted may have landed, so it is never retried; it propagates as before.
                trace.Record(RecordCreationStep.Create, RecordCreationStepState.Failed, "the app-only create faulted");
                _logger.LogError(ex, "[RECORD-CREATE] the app-only create of {Table} faulted (correlationId={CorrelationId})",
                    table, correlationId);
                throw;
            }
        }

        trace.Record(RecordCreationStep.Create, RecordCreationStepState.Ran, "one app-only create");

        // ── Step 12: post-create, never thrown ────────────────────────────────────────────────────────────────────────
        trace.Begin(RecordCreationStep.PostCreate);
        LogDroppedSearchIndex(table, id, decision, correlationId);
        var warnings = new List<string>();
        SecureRootInheritResult? isolatedCompletion = null;
        var removed = await AfterCreateAsync(table, id, rule, decision, noun, correlationId, warnings, s => isolatedCompletion = s)
            .ConfigureAwait(false);
        if (removed is not null)
        {
            trace.Record(RecordCreationStep.PostCreate, RecordCreationStepState.Refused, "the isolated row was removed again");
            return trace.Outcome(removed, warnings) with { IsolatedCompletion = isolatedCompletion };
        }

        trace.Record(RecordCreationStep.PostCreate, RecordCreationStepState.Ran, warnings.Count == 0 ? null : $"{warnings.Count} warning(s)");
        _logger.LogInformation(
            "[RECORD-CREATE] created {Table} {Id} (owner team {Team}, isolated={Isolated}, warnings={Warnings}, mapping " +
            "warnings={MappingWarnings}, correlationId={CorrelationId})",
            table, id, decision.OwningTeamId, decision.IsolatedFor is not null, warnings.Count, decision.MappingWarnings.Count,
            correlationId);
        // The mapping step's warnings first (step 3 ran before the create), then step 8's, then the post-create ones.
        return trace.Created(id, decision.OwningTeamId, decision.IsolatedFor is not null,
            [.. decision.MappingWarnings.Select(w => w.Detail), .. decision.DefaultWarnings, .. warnings], decision.MappingWarnings,
            writtenColumns) with { IsolatedCompletion = isolatedCompletion };
    }

    /// <summary>What steps 1 to 10 decided: a refusal, or the fields to create and the facts step 12 needs.</summary>
    private sealed record Decision
    {
        public RecordCreationRefusal? Refusal { get; init; }
        public Dictionary<string, object?>? Fields { get; init; }
        public DataverseWriteItemMapper.MappedItem? Item { get; init; }
        public Guid? OwningTeamId { get; init; }

        /// <summary>The person an isolated root was created for (the plan's creator), when the plan isolated it.</summary>
        public Guid? IsolatedFor { get; init; }

        public string? GrantorOid { get; init; }

        /// <summary>Step 3's and step 5's warnings about the mapped values (task 014): what was not mapped, and why.</summary>
        public IReadOnlyList<RecordCreationWarning> MappingWarnings { get; init; } = [];

        /// <summary>Step 8's warning (task 015): the owning business unit's search-index defaults could not be read.</summary>
        public IReadOnlyList<string> DefaultWarnings { get; init; } = [];

        /// <summary>Task 039 (FR-38): the client-supplied search-index values dropped before step 5 (logged at step 12, when the id is known).</summary>
        public IReadOnlyList<RecordTableRules.DroppedSearchIndexValue> SearchIndexDrops { get; init; } = [];

        /// <summary>Task 039 (FR-38): the index step 8 applied (the catalog row id, the index name); both none when the unit has none.</summary>
        public (Guid? IndexId, string? IndexName) AppliedSearchIndex { get; init; }

        public static Decision Refuse(RecordCreationRefusal refusal) => new() { Refusal = refusal };
    }

    /// <summary>Steps 1 to 10. Any exception propagates to <see cref="CreateAsync"/>, which refuses (fail closed).</summary>
    private async Task<Decision> DecideAsync(
        string table, RecordTableRule rule, RecordCreationRequest request, RecordCreationPrincipal principal,
        IReadOnlyDictionary<string, PostMappingDefault> defaults, string noun, StepTrace trace, CancellationToken ct)
    {
        // ── Step 1: the principal ─────────────────────────────────────────────────────────────────────────────────────
        trace.Begin(RecordCreationStep.Principal);
        IDataverseUserClient? user = null;
        Guid? callerSystemUserId = null;
        Guid? ownerContextObjectId;
        RecordRequester? requestedBy;
        string? grantorOid;
        switch (principal)
        {
            case RecordCreationPrincipal.CallerPrincipal caller:
            {
                user = caller.Client;
                var who = await CallerWriteAuthorization.WhoAmIAsync(caller.Client, ct).ConfigureAwait(false);
                if (who.Failure is { } whoFailure)
                {
                    if (Sprk.Bff.Api.Api.ChildRecordEndpoints.IsUnresolvedCaller(whoFailure))
                    {
                        _logger.LogWarning(
                            "[RECORD-CREATE] create of {Table} refused: the caller has no Dataverse identity ({ErrorCode})",
                            table, whoFailure.ErrorCode);
                        return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.PrincipalUnresolved,
                            Sprk.Bff.Api.Api.ChildRecordEndpoints.CallerUnresolvedReasonCode,
                            $"You could not be identified in Dataverse, so the {noun} was not created.")
                        {
                            // Task 018b-1s (G2): which of the four WhoAmI failures it was (no token vs no Dataverse user).
                            ClientFailure = whoFailure,
                        });
                    }

                    return Decision.Refuse(Upstream(whoFailure, noun));
                }

                callerSystemUserId = who.SystemUserId;
                ownerContextObjectId = caller.ObjectId;
                // Task 146 c1-r1 (owner round 13 item 9): the caller asked for this row, so the app-only create records them.
                requestedBy = RecordRequester.Of(who.SystemUserId);
                grantorOid = caller.ObjectId?.ToString("D");
                trace.Record(RecordCreationStep.Principal, RecordCreationStepState.Ran, "caller (WhoAmI under their own token)");
                break;
            }

            case RecordCreationPrincipal.SystemPrincipal system:
                requestedBy = system.RequestedBy;
                ownerContextObjectId = requestedBy?.ObjectId;
                callerSystemUserId = requestedBy?.SystemUserId;
                grantorOid = requestedBy?.ObjectId?.ToString("D");
                trace.Record(RecordCreationStep.Principal, RecordCreationStepState.Ran,
                    requestedBy is null ? "system, for nobody" : "system, requested by a person");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(principal), principal.GetType().Name, "Unknown principal.");
        }

        // Task 017 r3 (UAC-r2 F1): a request that names no grantor hands the "Assigned *" step none (no invitation, no
        // sprk_grantedby user) - what the chat create did before the service, and what every other AI-tool write still does.
        if (request.Grantor == AssignedAccessGrantor.None)
            grantorOid = null;

        // ── Step 2: the table's rules — refuse a server-owned column in the CALLER's payload ────────────────────────────
        trace.Begin(RecordCreationStep.TableRules);
        DataverseWriteItemMapper.MappedItem item;
        var referenceWarnings = new List<RecordCreationWarning>();

        // Task 039 (FR-38): on a search-indexed table both index columns are the SERVER's (step 8 writes them from the final
        // owning business unit). Whatever the payload carries for either is DROPPED here, before anything is checked — a drop,
        // never a refusal, and never the blocked-column refusal below — so no AppendTo is ever asked on the catalog row (#2399).
        // What a mapping rule or an adapter's default fills is dropped again after step 3½; every drop is logged at step 12.
        var indexDrops = new List<RecordTableRules.DroppedSearchIndexValue>();
        if (user is null)
        {
            item = ((RecordCreationPayload.Mapped)request.Payload).Item;
            if (rule.SearchIndexed)
                item = RecordTableRules.WithoutSearchIndex(item, RecordTableRules.SearchIndexOriginCaller, indexDrops);

            // Task 017 (owner decision 2b): a system principal's row is the server's, so the caller's column policy is not
            // asked (FR-07) — except the table's own server-owned columns, which no principal names (a document's access
            // record), and a document's access record and narrowing as the app-only refusal states them
            // (InheritedAccessPermission.RefuseAppOnlyWrite: Access Permission, its record, the submission marker). A
            // caller may still fill a document's Access Permission at create (UAC-r2 D7); an app-only create may not.
            if (item.Columns.FirstOrDefault(c => rule.TableServerOwnedColumns.Contains(c)) is { } systemTableColumn)
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                    RecordCreationRefusal.BlockedColumnCode, $"Column '{systemTableColumn}' of a {noun} is set by Spaarke — omit it."));
            }

            if (InheritedAccessPermission.RefuseAppOnlyWrite(table, item.Columns) is
                { RefusalCode: InheritedAccessPermission.AppOnlyDocumentWriteReasonCode } appOnlyRefusal)
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                    RecordCreationRefusal.BlockedColumnCode, $"The {noun} was not created: {appOnlyRefusal.Reason}."));
            }

            // Recorded as before: the caller's column policy is the step, and a system principal skips it.
            trace.Record(RecordCreationStep.TableRules, RecordCreationStepState.Skipped, "system principal (FR-07)");
        }
        else
        {
            if (request.Payload is RecordCreationPayload.WebApi webApi)
            {
                // The caller's payload, mapped as the caller (navigation properties and binds from the table's metadata).
                var webApiBody = rule.SearchIndexed
                    ? RecordTableRules.WithoutSearchIndex(webApi.Body, RecordTableRules.SearchIndexOriginCaller, indexDrops)
                    : webApi.Body;
                var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(user, table, webApiBody, ct).ConfigureAwait(false);
                if (mapped.ValidationError is { } invalid)
                {
                    return Decision.Refuse(new RecordCreationRefusal(
                        RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode, invalid));
                }

                if (mapped.ClientFailure is { } mapFailure)
                    return Decision.Refuse(Upstream(mapFailure, noun));

                item = mapped.Item!;
            }
            else
            {
                item = ((RecordCreationPayload.Mapped)request.Payload).Item;
                if (rule.SearchIndexed)
                    item = RecordTableRules.WithoutSearchIndex(item, RecordTableRules.SearchIndexOriginCaller, indexDrops);
            }

            // The table's own server-owned columns first (the child-record route's order) — its autonumber among them: the
            // platform issues the number, as the Office create's protected list says — then the ones every app-only create
            // owns, with the creator person (the G5 core's first check, the same words).
            if (CallerBlockedColumnRefusal(rule, noun, item.Columns) is { } blockedColumn)
                return Decision.Refuse(blockedColumn);

            // Task 018a (K-3): the references the request names — optional values the caller chose — are asked alone, as the
            // caller; one the caller may not use is dropped with a warning (FR-08's mapped-value path), never refused. Before
            // step 3, so a mapping rule may still fill a dropped reference (and is then checked as a mapped value).
            var droppedReferences = 0;
            if (request.ReferenceLookupColumns is { Count: > 0 } references)
            {
                var checkedReferences = await DropUnusableReferencesAsync(
                        table, user, callerSystemUserId!.Value, item, references, request.ServerSetLookupColumns, noun, referenceWarnings, ct)
                    .ConfigureAwait(false);
                if (checkedReferences.Refusal is { } referenceRefusal)
                    return Decision.Refuse(referenceRefusal);

                droppedReferences = referenceWarnings.Count;
                item = checkedReferences.Item!;
            }

            trace.Record(RecordCreationStep.TableRules, RecordCreationStepState.Ran,
                droppedReferences == 0
                    ? "no server-owned column in the caller's payload"
                    : $"no server-owned column in the caller's payload; {droppedReferences} reference value(s) dropped");
        }

        // UAC-r2's review of 013 (K-2), a step-5 rule asked before step 3 reads anything (task 014 fix round 3, review
        // K-1): a server-set lookup costs no AppendTo, so a FILING lookup is never one. Server-set columns are never mapped,
        // so the caller's item already carries every server-set lookup the row will.
        if (user is not null && ServerSetFilingColumn(table, item, request.ServerSetLookupColumns) is { } serverSetFilingColumn)
        {
            trace.Begin(RecordCreationStep.CallerChecks);
            return Decision.Refuse(ServerSetFilingRefused(serverSetFilingColumn, noun));
        }

        // ── Step 3: field mapping (task 014) — fill-only, the source read as the caller; the mapped values' origin kept ──
        trace.Begin(RecordCreationStep.FieldMapping);
        var mappingWarnings = new List<RecordCreationWarning>();
        var callerItem = item;
        IReadOnlyDictionary<string, JsonElement> mappedValues = NoMappedValues;
        if (rule.MappingExclusion is { } exclusion)
        {
            trace.Record(RecordCreationStep.FieldMapping, RecordCreationStepState.Skipped, $"not mapped ({exclusion.Name})");
        }
        else if (request.Mapping is not { } mapping || user is null)
        {
            // Nothing asked: nothing read, nothing merged (a system principal never gets here with a mapping: CreateAsync).
            trace.Record(RecordCreationStep.FieldMapping, RecordCreationStepState.NoOp, "no field mapping was asked for");
        }
        else
        {
            MappingStep mapped;
            try
            {
                mapped = await MapFieldsAsync(table, rule, request, mapping, user, callerItem, mappingWarnings, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Mapping fails closed by mapping NOTHING (NFR-01): the caller's own row is still created, and told so —
                // once: what the faulted step had already said about single rules or values no longer applies.
                _logger.LogWarning(ex, "[RECORD-CREATE] field mapping for {Table} faulted; nothing was mapped", table);
                mappingWarnings.Clear();
                mappingWarnings.Add(new RecordCreationWarning(RecordCreationWarning.MappingUnavailableCode, null, MappingFaulted));
                mapped = MappingStep.Unmapped(callerItem);
            }

            // ebilling-r1's review of 013, item 1: the SAME local — the pre-write rules (5½) and the create (11) read this item.
            item = mapped.Item ?? callerItem;
            mappedValues = mapped.Values;
            trace.Record(RecordCreationStep.FieldMapping, RecordCreationStepState.Ran,
                $"{mappedValues.Count} column(s) mapped (fill-only), {mappingWarnings.Count} warning(s)");
        }

        // ── Step 3½ (task 018a, K-7): the adapter's post-mapping defaults, fill-only over what the caller and the mapping
        // supplied, merged with the mapped values so step 5 checks them the same way ─────────────────────────────────────
        // Task 037 (FR-36): with the table's copied defaults (RecordTableRules.CopiedDefaultsFor), read off the row AS MAPPED,
        // so they take this same path — filled only where the row leaves the column unset, checked at step 5 like any
        // default, written in the create (step 11) and so read back by the "Assigned *" materialisation (step 12). A copy of a
        // SERVER-set source names the same server-chosen record, so it is server-set too (steps 5 and 6 read the request's
        // set), exactly as on the owned create this replaced (OwnedChildWrite.CreateAsync, deleted in 017).
        if (user is not null)
        {
            (defaults, var copied) = WithCopiedDefaults(table, rule, item, defaults);
            var serverSetCopies = copied
                .Where(c => request.ServerSetLookupColumns?.Contains(c.Source) == true)
                .Select(c => c.Column)
                .ToArray();
            if (serverSetCopies.Length > 0)
            {
                request = request with
                {
                    ServerSetLookupColumns = new HashSet<string>(
                        request.ServerSetLookupColumns!.Concat(serverSetCopies), StringComparer.OrdinalIgnoreCase),
                };
            }
        }

        IReadOnlySet<string> defaultColumns = NoDefaultColumns;
        if (defaults.Count > 0 && user is not null)
        {
            var unfilled = DefaultsToFill(table, item, defaults);
            if (unfilled.Count > 0)
            {
                defaultColumns = unfilled.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Fix round (F4-3): only what the table can take, by field mapping's own checks; the rest dropped, named.
                var fitting = await FittingDefaultsAsync(user, table, unfilled, defaultColumns, mappingWarnings, ct)
                    .ConfigureAwait(false);

                // Fix round (K-4): mapped over the step-3 item, so a default that does not map costs only itself — the
                // step-3 item and its mapped values stand whatever the defaults come to.
                if (fitting.Count > 0)
                {
                    MappingStep withDefaults;
                    var warningsBeforeMerge = mappingWarnings.Count;
                    try
                    {
                        withDefaults = await MapMergedAsync(user, table, item, fitting, mappingWarnings, ct, defaultColumns)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A fault costs the defaults only: none is written, each named; the step-3 row goes on (as a
                        // mapping fault maps nothing, NFR-01 — a value that could not be checked is never written).
                        _logger.LogWarning(ex, "[RECORD-CREATE] the post-mapping defaults of a {Table} could not be mapped; none is written",
                            table);

                        // Re-check K-2: what the faulted merge had already said about single defaults no longer applies —
                        // each default is named once.
                        mappingWarnings.RemoveRange(warningsBeforeMerge, mappingWarnings.Count - warningsBeforeMerge);
                        foreach (var column in fitting.Keys)
                            mappingWarnings.Add(Dropped(column, CouldNotBeChecked, defaultColumns));
                        withDefaults = MappingStep.Unmapped(item);
                    }

                    if (withDefaults is { Item: { } merged, Values.Count: > 0 })
                    {
                        var combined = new Dictionary<string, JsonElement>(mappedValues, StringComparer.Ordinal);
                        foreach (var (column, value) in withDefaults.Values)
                            combined[column] = value;

                        item = merged;
                        mappedValues = combined;
                    }
                }
            }
        }

        // ── Task 039 (FR-38): what step 3 (a mapping rule, which may name a parent via an explicit mappingSource) and step 3½ (an
        // adapter's default) filled is dropped too, so step 5 never asks AppendTo on the catalog row and no step-3 or step-5 warning
        // mentions either column. Step 8 is the only writer of both. ─────────────────────────────────────────────────────────
        if (rule.SearchIndexed)
        {
            var lateDrops = new List<RecordTableRules.DroppedSearchIndexValue>();
            item = RecordTableRules.WithoutSearchIndex(item, RecordTableRules.SearchIndexOriginMapping, lateDrops);
            if (mappedValues.Keys.Any(k => RecordTableRules.SearchIndexColumnOf(k) is not null))
            {
                mappedValues = mappedValues
                    .Where(kv => RecordTableRules.SearchIndexColumnOf(kv.Key) is null)
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            }

            // An adapter's default is named as such; whatever else a late drop came from is a mapping rule's.
            indexDrops.AddRange(lateDrops.Select(d => d with
            {
                Origin = defaultColumns.Contains(d.Column) ? RecordTableRules.SearchIndexOriginDefault : d.Origin,
            }));
            if (defaultColumns.Any(c => RecordTableRules.SearchIndexColumnOf(c) is not null))
                defaultColumns = defaultColumns.Where(c => RecordTableRules.SearchIndexColumnOf(c) is null).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Matched on the warning's column, never on its text: a warning that merely mentions a column (a profile named
            // after it) is about something else and stays (round 3, G8).
            mappingWarnings.RemoveAll(w => RecordTableRules.SearchIndexColumnOf(w.Column) is not null);
        }

        // ── Step 4: the merged payload, mapped (the caller's or the server's item, with the mapped values merged in) ────
        var defaulted = mappedValues.Keys.Count(defaultColumns.Contains);
        trace.Record(RecordCreationStep.MapPayload, RecordCreationStepState.Ran,
            $"{item.Columns.Count} column(s) ({mappedValues.Count - defaulted} mapped), {item.Lookups.Count} lookup(s)" +
            (defaulted == 0 ? string.Empty : $"; {defaulted} default(s) filled"));

        // ── Step 5: the caller checks on the merged payload (CallerWriteAuthorization, ADR-008 path A) ──────────────────
        trace.Begin(RecordCreationStep.CallerChecks);
        if (user is null)
        {
            trace.Record(RecordCreationStep.CallerChecks, RecordCreationStepState.Skipped, "system principal (FR-07)");
        }
        else
        {
            // The K-2 guard (a server-set FILING lookup) ran before step 3.
            if (mappedValues.Count == 0)
            {
                var check = await CallerWriteAuthorization.CheckCallerMayCreateAsync(
                    user, callerSystemUserId!.Value, table, item, request.ServerSetLookupColumns, ct).ConfigureAwait(false);
                if (!check.Succeeded)
                    return Decision.Refuse(CallerCheckRefused(check, noun));

                trace.Record(RecordCreationStep.CallerChecks, RecordCreationStepState.Ran,
                    "Create/Append, AppendTo on every record named, no field-secured column");
            }
            else
            {
                var checkedMerge = await CheckMergedPayloadAsync(
                    table, user, callerSystemUserId!.Value, callerItem, item, mappedValues, request.ServerSetLookupColumns, noun,
                    mappingWarnings, ct, defaultColumns).ConfigureAwait(false);
                if (checkedMerge.Refusal is { } mergedRefusal)
                    return Decision.Refuse(mergedRefusal);

                var dropped = mappedValues.Count - checkedMerge.Values.Count;
                item = checkedMerge.Item!; // the SAME local again: what survived the checks is what is written
                mappedValues = checkedMerge.Values;
                trace.Record(RecordCreationStep.CallerChecks, RecordCreationStepState.Ran,
                    "Create/Append, AppendTo on every record named, no field-secured column — on the merged payload" +
                    (dropped == 0 ? string.Empty : $"; {dropped} mapped value(s) dropped"));
            }
        }

        // ── Step 5½: the table's pre-write rules, on the row as it will be written (ebilling-r1 recipe) ────────────────
        if (rule.PreWriteRules.Count > 0)
        {
            trace.Begin(RecordCreationStep.PreWriteRules);
            if (await RunPreWriteRulesAsync(table, rule, user, item, noun, ct).ConfigureAwait(false) is { } preWriteRefusal)
                return Decision.Refuse(preWriteRefusal);

            trace.Record(RecordCreationStep.PreWriteRules, RecordCreationStepState.Ran,
                string.Join(", ", rule.PreWriteRules.Select(r => r.Rule.Name)));
        }

        // ── Step 6: the secure-root plan (a work assignment or project filed under a secure record) ───────────────────
        trace.Begin(RecordCreationStep.SecureRootPlan);
        var creatorPerson = callerSystemUserId ?? Guid.Empty;
        Guid? isolatedTeam = null;
        if (SecureRootInheritance.Inherits(table))
        {
            // A work assignment or project filed under a record may be filed under a SECURE one: then it is created into
            // isolation and shared to the person it is made for, after that person's own AppendTo (G5) on the secure
            // record. A system principal has no caller to ask that as, and whether the record is secure is known only
            // once the plan reads it, so a system create that files one under anything is refused here, before the plan
            // (fail closed; task 013 fix round 1, OWNER/017-018: an app-only access check on RequestedBy is the
            // alternative). Filed under nothing, the plan answers Ordinary without a read.
            if (user is null && FilesUnderARecord(table, item))
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.Forbidden,
                    RecordCreationRefusal.SystemPrincipalUnsupportedCode,
                    $"A {noun} filed under another record may be filed under a secure record, and is then created secure " +
                    "and shared to the person it is made for once that person's own access is checked. This create acts " +
                    "for no caller whose access can be checked, so it was not created."));
            }

            // UAC-r2's review of 013 (K-2), task 014 review rounds 1 and 2: a filing lookup the SERVER set skipped step 5's
            // AppendTo, and the plan asks it only on a secure record — so it is asked here, as the caller, whatever the
            // record, and BEFORE the plan reads it app-only (a plan refusal given first, such as an unreadable flag, would
            // tell a record that exists from a missing one). Refused as the plan's own AppendTo callback refuses: the
            // uniform not-found, the same body as step 5's for an unmarked lookup.
            if (user is not null
                && ServerSetRootFilingLookups(table, item, request.ServerSetLookupColumns) is { Length: > 0 } serverSetFiling)
            {
                var appendTo = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(user, creatorPerson, serverSetFiling, ct)
                    .ConfigureAwait(false);
                if (appendTo.ClientFailure is not null)
                    return Decision.Refuse(CallerCheckRefused(appendTo, noun));
                if (!appendTo.Succeeded)
                {
                    return Decision.Refuse(PlanRefused(
                        RecordOwnerResolution.Refused(DataverseUserClientErrorCodes.AccessDenied, appendTo.Denied ?? NotAvailableToFileUnder),
                        noun));
                }
            }

            var plan = await _rootFiling.PlanCreateAsync(table, OwnedChildWrite.WritesOf(item), creatorPerson, ct,
                user is null ? null : CallerMayFileUnder(user, creatorPerson, item, request.ServerSetLookupColumns)).ConfigureAwait(false);
            if (plan.Refusal is { } planRefusal)
                return Decision.Refuse(PlanRefused(planRefusal, noun));

            if (plan is { Isolated: true, SecureOwnerTeamId: { } secureTeam })
                isolatedTeam = secureTeam;

            trace.Record(RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran,
                isolatedTeam is null ? "ordinary" : "into isolation (the named Secure Record Owners team)");
        }
        else
        {
            trace.Record(RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran, "ordinary (the table inherits no secure parent)");
        }

        // ── Step 7: the owner — record-first for every create, roots and children (FR-09, task 015) ──────────────────
        trace.Begin(RecordCreationStep.Owner);
        RecordOwnerResolution? owner = null;
        if (isolatedTeam is null)
        {
            var decided = await DecideOwnerAsync(table, rule, item, ownerContextObjectId, callerSystemUserId, requestedBy, noun, ct)
                .ConfigureAwait(false);
            if (decided.Refusal is { } ownerRefusal)
                return Decision.Refuse(ownerRefusal);

            owner = decided.Owner!;
            trace.Record(RecordCreationStep.Owner, RecordCreationStepState.Ran,
                owner.IsSecureOwner ? "the named Secure team (secure-if-any)" : "the resolver's business-unit team");
        }
        else
        {
            trace.Record(RecordCreationStep.Owner, RecordCreationStepState.Ran, "the plan's named Secure team (an isolated root)");
        }

        // ── Step 8: the search index — the FINAL owning business unit's, the ONLY writer of both columns (FR-38, I-5) ───
        trace.Begin(RecordCreationStep.SearchIndexDefault);
        var owningTeam = isolatedTeam ?? owner!.OwningTeamId!.Value;
        var searchIndex = await SearchIndexDefaultAsync(table, rule, owningTeam, noun, ct).ConfigureAwait(false);
        trace.Add(searchIndex.Step);

        // ── Step 9: the creator person ("for" person and table defaults: the adapters', owner-review item 3) ─────────────
        var fields = owner is not null
            ? OwnedCreateFields(owner, table, item)
            : IsolatedCreateFields(table, item, isolatedTeam!.Value, creatorPerson);
        foreach (var (column, value) in searchIndex.Fields)
        {
            // Nothing else can carry either column any more (dropped above), so this is the only writer. UAC-r2's review of 015
            // (K3), kept: the body's keys are case-sensitive, so any other spelling of the key is removed rather than sent beside
            // the canonical key Dataverse would then see twice.
            PutServerValue(fields, column, value);
        }
        trace.Record(RecordCreationStep.CreatorAndDefaults, RecordCreationStepState.Ran,
            RecordCreatorPerson.IsStamped(table)
                ? "creator person stamped when the requester resolved; \"for\" person and table defaults stay with the adapters"
                : "the table carries no creator person; \"for\" person and table defaults stay with the adapters");

        // ── Step 10: the I-1 stamp and I-16, derived into the payload before the create (owner decision 2a; see remarks) ──
        trace.Begin(RecordCreationStep.StampAndInheritance);
        var stamped = await StampBeforeCreateAsync(table, user, item, fields, noun, ct).ConfigureAwait(false);
        if (stamped.Refusal is { } stampRefusal)
            return Decision.Refuse(stampRefusal);

        trace.Record(RecordCreationStep.StampAndInheritance, stamped.State, stamped.Note);

        return new Decision
        {
            Fields = fields,
            Item = item,
            OwningTeamId = owningTeam,
            IsolatedFor = isolatedTeam is null ? null : creatorPerson,
            GrantorOid = grantorOid,
            MappingWarnings = referenceWarnings.Count == 0 ? mappingWarnings : [.. referenceWarnings, .. mappingWarnings],
            DefaultWarnings = searchIndex.Warning is { } searchIndexWarning ? [searchIndexWarning] : [],
            SearchIndexDrops = indexDrops,
            AppliedSearchIndex = (searchIndex.AppliedIndex, searchIndex.AppliedName),
        };
    }

    /// <summary>
    /// Step 10 (owner decision 2a, task 017): the I-1 stamp columns the merged row is MISSING, derived from what it is filed
    /// under (<see cref="CoreAncestorRestamper.PlanMissingStampAsync"/>, task 016's in-memory planner — fill-only, nothing
    /// written), bound into <paramref name="fields"/>; then I-16 from every lookup the row now names. A derivation that fails
    /// refuses: a data condition (<see cref="CoreAncestorStampUnderivableException.IsPermanent"/>) is
    /// <see cref="RecordCreationRefusal.StampUnderivableCode"/> (400), a derived stamp the table's metadata cannot bind is the
    /// non-retryable <see cref="RecordCreationRefusalKind.Undecided"/> (500, F-5), anything else
    /// <see cref="RecordCreationRefusal.StampUnavailableCode"/> (503). I-16 never refuses: it is display only, and a null is simply not set. A document gets neither (#2169).
    /// </summary>
    /// <remarks>Only <paramref name="fields"/> changes: the item (what the owner, the plan and step 12 read) is the row as the
    /// earlier steps decided it. The stamp is bound under the table's navigation property, read as the caller through
    /// <see cref="DataverseWriteItemMapper"/> (the read steps 1 and 4 make); a system principal has no caller, so its derived
    /// stamp is left to the post-create re-derivation (step 12), which runs for every principal.</remarks>
    private async Task<(RecordCreationRefusal? Refusal, RecordCreationStepState State, string? Note)> StampBeforeCreateAsync(
        string table, IDataverseUserClient? user, DataverseWriteItemMapper.MappedItem item, Dictionary<string, object?> fields,
        string noun, CancellationToken ct)
    {
        if (InheritedAccessPermission.RecordsOwnNarrowing(table))
            return (null, RecordCreationStepState.Skipped, "a document is stamped after the create only (#2169)");

        var stampsI1 = CoreAncestorResolver.IsStampedChildEntity(table);
        var takesI16 = InheritedAccessPermission.AppliesTo(table);
        if (!stampsI1 && !takesI16)
            return (null, RecordCreationStepState.Skipped, "the table carries no I-1 stamp and no inherited Access Permission");

        // The merged row in memory: every lookup it binds, and the ADR-024 pair id as written.
        var row = new Entity(table);
        foreach (var lookup in item.Lookups)
            row[lookup.Column] = new EntityReference(lookup.RelatedTable, lookup.RecordId);
        if (OwnedChildWrite.PairOf(item).IdText is { } pairId)
            row[CoreAncestorResolver.RegardingRecordIdColumn] = pairId;

        var notes = new List<string>();
        if (stampsI1)
        {
            Dictionary<string, object>? missing;
            try
            {
                missing = await _restamper.PlanMissingStampAsync(table, row, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (CoreAncestorStampUnderivableException ex) when (ex.IsPermanent)
            {
                _logger.LogWarning(ex, "[RECORD-CREATE] the I-1 stamp of a new {Table} cannot be derived ({Kind}); nothing was created",
                    table, ex.RefusalKind);
                return (StampUnderivable(noun), RecordCreationStepState.Refused, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RECORD-CREATE] the I-1 stamp of a new {Table} could not be derived (fault); nothing was created",
                    table);
                return (StampUnavailable(noun), RecordCreationStepState.Refused, null);
            }

            if (missing is null)
            {
                notes.Add("no I-1 stamp missing");
            }
            else
            {
                foreach (var (column, value) in missing)
                    row[column] = value; // I-16 below reads the row as it will be written

                if (user is null)
                {
                    notes.Add($"I-1 stamp derived ({missing.Count} column(s)); a system principal's is bound after the create");
                }
                else
                {
                    var (bound, permanent) = await BindStampAsync(user, table, missing, ct).ConfigureAwait(false);
                    if (bound is null && permanent)
                    {
                        // Task 017 review F-5: the table's metadata cannot bind the derived stamp, and a retry will not change
                        // that, so it is not "try again" (503) but the non-retryable Undecided (500). Ids only.
                        _logger.LogError("[RECORD-CREATE] the I-1 stamp of a new {Table} was derived but the table's metadata " +
                                         "cannot bind it (permanent); nothing was created", table);
                        return (StampUnbindable(noun), RecordCreationStepState.Refused, null);
                    }

                    if (bound is null)
                    {
                        _logger.LogError("[RECORD-CREATE] the I-1 stamp of a new {Table} was derived but could not be bound " +
                                         "(Dataverse did not answer); nothing was created", table);
                        return (StampUnavailable(noun), RecordCreationStepState.Refused, null);
                    }

                    foreach (var (key, value) in bound)
                        PutServerValue(fields, key, value);
                    notes.Add($"I-1 stamp derived into the payload ({missing.Count} column(s))");
                }
            }
        }

        if (takesI16)
        {
            var filed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var attribute in row.Attributes)
            {
                if (attribute.Value is EntityReference reference)
                    filed[attribute.Key] = reference;
            }

            // Never throws (the resolver's contract): a null is simply not set, and step 12 refreshes it after the create.
            if (await _restamper.ResolveInheritedAccessPermissionAsync(table, filed, ct).ConfigureAwait(false) is { } level)
            {
                PutServerValue(fields, InheritedAccessPermission.Column, level);
                notes.Add("I-16 set");
            }
            else
            {
                notes.Add("no I-16 before the create");
            }
        }

        return (null, RecordCreationStepState.Ran, string.Join("; ", notes));
    }

    /// <summary>
    /// The derived stamp columns as the create body binds them (<c>sprk_RegardingMatter@odata.bind</c>), mapped AS THE CALLER
    /// through <see cref="DataverseWriteItemMapper"/>, the one place a column's navigation property is read. Binds are
    /// <see langword="null"/> when the stamp could not be bound; <c>Permanent</c> says whether a retry would change that:
    /// <see langword="false"/> only when Dataverse did not answer (the mapper's client failure), <see langword="true"/> when the
    /// mapper refused the item or the table cannot bind a column (task 017 review F-5).
    /// </summary>
    private static async Task<(IReadOnlyList<KeyValuePair<string, string>>? Binds, bool Permanent)> BindStampAsync(
        IDataverseUserClient user, string table, IReadOnlyDictionary<string, object> stamp, CancellationToken ct)
    {
        var lookups = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (column, value) in stamp)
        {
            if (value is not EntityReference reference)
                return (null, true);
            lookups[column.ToLowerInvariant()] = new Dictionary<string, string>
            {
                ["relatedTable"] = reference.LogicalName,
                ["recordId"] = reference.Id.ToString("D"),
            };
        }

        var mapped = await DataverseWriteItemMapper.MapAsync(user, table, JsonSerializer.SerializeToElement(lookups), ct)
            .ConfigureAwait(false);
        if (mapped.Item is not { } item)
            return (null, Permanent: mapped.ClientFailure is null);

        using var body = JsonDocument.Parse(item.JsonBody);
        var binds = new List<KeyValuePair<string, string>>();
        foreach (var property in body.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                return (null, true);
            binds.Add(KeyValuePair.Create(property.Name, property.Value.GetString()!));
        }

        return binds.Count == lookups.Count ? (binds, false) : (null, true);
    }

    /// <summary>Task 017 review F-5: the derived stamp cannot be bound by the table's metadata. Not retryable, so the
    /// existing non-retryable <see cref="RecordCreationRefusalKind.Undecided"/> (500), not a "try again" 503.</summary>
    private static RecordCreationRefusal StampUnbindable(string noun) => new(RecordCreationRefusalKind.Undecided,
        RecordCreationRefusal.NoDecisionCode, $"The {noun} was not created.");

    private static RecordCreationRefusal StampUnderivable(string noun) => new(RecordCreationRefusalKind.Invalid,
        RecordCreationRefusal.StampUnderivableCode,
        $"The {noun} was not saved: the core record it belongs to cannot be determined from the record it is filed under " +
        "(that record names more than one, or no longer exists).");

    private static RecordCreationRefusal StampUnavailable(string noun) => new(RecordCreationRefusalKind.Unavailable,
        RecordCreationRefusal.StampUnavailableCode,
        $"The core record the {noun} belongs to could not be determined right now, so it was not created.");

    /// <summary>
    /// Step 7 (FR-09, task 015: record-first for every create, roots and children), ported from
    /// the owned create (<c>OwnedChildWrite.CreateAsync</c>, deleted in 017): the resolver's owner over every parent the row is filed under — its
    /// lookups, and on a table whose ADR-024 pair files it on its own (a project, <see cref="PairParentAsync"/>) the record
    /// the pair names — secure-if-any, then the filed-under record's business-unit team, then the acting user's, with the
    /// person who asked as <see cref="RecordOwnershipContext.RequestedBy"/>; then the two secure-filing refusals of that
    /// core, with its words. A pair whose record cannot be told refuses (<see cref="RecordOwnerRefusal.ParentUndetermined"/>),
    /// never "filed under nothing". A table's ownership exception (an unfiled communication keeps its creator, E1) cannot be
    /// honoured by an application create, so it refuses. A fault is <see cref="RecordCreationRefusal.OwnerUnavailableCode"/>
    /// (503), nothing written.
    /// </summary>
    private async Task<(RecordOwnerResolution? Owner, RecordCreationRefusal? Refusal)> DecideOwnerAsync(
        string table, RecordTableRule rule, DataverseWriteItemMapper.MappedItem item, Guid? callerObjectId,
        Guid? callerSystemUserId, RecordRequester? requestedBy, string noun, CancellationToken ct)
    {
        var parents = OwnedChildWrite.ParentsOf(table, item);
        if (rule.OwnershipException is { } exception && parents.Count == 0)
        {
            return (null, new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved,
                RecordCreationRefusal.OwnershipExceptionCode,
                $"a {noun} filed under nothing keeps its creator ({exception.Name}), which a record the application creates " +
                "cannot do; file it under a record"));
        }

        RecordOwnerResolution owner;
        try
        {
            var pair = OwnedChildWrite.PairOf(item);
            var resolved = await ResolveOwnerOverAsync(
                    table, OwnedChildWrite.LookupsOf(item).ToList(), pair.InvalidId, pair.IdText, pair.TypeId, callerObjectId,
                    callerSystemUserId, requestedBy, ct)
                .ConfigureAwait(false);
            if (resolved.Refusal is { } undetermined)
                return (null, undetermined);

            owner = resolved.Owner!;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORD-CREATE] the owner of a new {Table} could not be decided (fault); nothing was created", table);
            return (null, new RecordCreationRefusal(RecordCreationRefusalKind.Unavailable,
                RecordCreationRefusal.OwnerUnavailableCode,
                $"The owner of the new {noun} could not be decided right now, so it was not created."));
        }

        return OwnerDecided(table, owner);
    }

    /// <summary>
    /// Step 7's resolution, shared by Create (<see cref="DecideOwnerAsync"/>) and Repair (task 016,
    /// <see cref="RepairAsync"/>): the record the ADR-024 pair files the row under on its own (<see cref="PairParentAsync"/>,
    /// a project) joins <paramref name="parents"/>, then the resolver's owner over them — the acting user's team when they
    /// name nothing. A pair whose record cannot be told is refused (<see cref="RecordOwnerRefusal.ParentUndetermined"/>),
    /// never read as "filed under nothing". A Dataverse fault propagates: each caller refuses it as the owner fault it is.
    /// </summary>
    /// <remarks>UAC task 194b R4: through the one builder (<see cref="RecordOwnershipContext.ForRow"/>), every parent keyed under
    /// the REAL column that files the row there — its lookups (<paramref name="filed"/>; on Repair also a message's thread filing,
    /// under <c>sprk_communicationthread</c>), and the record a project's pair names under <c>sprk_regardingrecordid</c> — ranked
    /// by the table's order, so Create, Repair and the reconciler ask the same ordered question. <paramref name="subjectPairId"/>
    /// is the row's pair id as written when <paramref name="pairIdText"/> is not passed (Repair read the pair already).</remarks>
    private async Task<(RecordOwnerResolution? Owner, RecordCreationRefusal? Refusal)> ResolveOwnerOverAsync(
        string table, IReadOnlyList<KeyValuePair<string, RecordOwnershipParent?>> filed, bool pairInvalidId, string? pairIdText,
        Guid? pairTypeId, Guid? callerObjectId, Guid? callerSystemUserId, RecordRequester? requestedBy, CancellationToken ct,
        string? subjectPairId = null)
    {
        // Only where the pair files the row on its own (a project); elsewhere a malformed pair is not an owner question.
        var pairParent = pairInvalidId && SecureRootInheritance.PairNamesAParentOnItsOwn(table)
            ? (Parent: null, Undetermined: $"its {SecureRootInheritance.PairIdColumn} is not a record id")
            : await PairParentAsync(_entities, table, pairIdText, pairTypeId, ct).ConfigureAwait(false);
        if (pairParent.Undetermined is { } undetermined)
        {
            var refused = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                $"what it is filed under could not be told ({undetermined})");
            return (null, new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.ParentUndetermined,
                refused.Reason!) { OwnerDecision = refused });
        }

        // Owner decision 2026-10-29: a work assignment filed only under an invoice or event is owned with the matters and
        // projects that record belongs to — the parents the un-secure cascade names for it — ranked first; the invoice or event
        // stays among the parents (secure-if-any). One that cannot be read refuses, as the cascade's "unverifiable" does.
        var throughIntermediates = await _rootFiling.OwnerLookupsThroughIntermediatesAsync(table, filed, ct).ConfigureAwait(false);
        if (throughIntermediates.Unverifiable is { } unread)
        {
            var refused = RecordOwnerResolution.Refused(RecordOwnerRefusal.ParentUndetermined,
                $"what it is filed under could not be told ({unread})");
            return (null, new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.ParentUndetermined,
                refused.Reason!) { OwnerDecision = refused });
        }

        var owner = await _ownership.ResolveOwnerAsync(
            OwnerQuestionOf(table, throughIntermediates.Lookups.Concat(filed), pairParent.Parent, subjectPairId ?? pairIdText,
                callerObjectId, callerSystemUserId) with
            {
                RequestedBy = requestedBy,
            },
            ct).ConfigureAwait(false);
        return (owner, null);
    }

    /// <summary>
    /// UAC task 194b R4: Step 7's question — the row's parents keyed by the column that files it there (<paramref name="filed"/>),
    /// and the record a project's ADR-024 pair names (<paramref name="pairParent"/>) under <c>sprk_regardingrecordid</c>, which a
    /// project's order ranks first (<c>SecureChildLineage.RootLookupOrder</c>) — through the one builder.
    /// </summary>
    internal static RecordOwnershipContext OwnerQuestionOf(
        string table, IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> filed, RecordOwnershipParent? pairParent,
        string? subjectPairId, Guid? callerObjectId, Guid? callerSystemUserId) =>
        RecordOwnershipContext.ForRow(
            table,
            pairParent is { } named
                ? filed.Append(KeyValuePair.Create(SecureRootInheritance.PairIdColumn, (RecordOwnershipParent?)named))
                : filed,
            subjectPairId, callerObjectId, callerSystemUserId);

    /// <summary>
    /// Step 7's answer for a resolved owner, shared by Create and Repair (task 016): no owner is the resolver's refusal
    /// (<see cref="RecordOwnerRefusal.NoOwnerSource"/> when it gave none); then the G5 core's two secure-filing refusals.
    /// </summary>
    private static (RecordOwnerResolution? Owner, RecordCreationRefusal? Refusal) OwnerDecided(
        string table, RecordOwnerResolution owner)
    {
        if (!owner.IsOwned)
        {
            var refused = owner.IsRefused
                ? owner
                : RecordOwnerResolution.Refused(RecordOwnerRefusal.NoOwnerSource, owner.Reason ?? "no owner was resolved");
            return (null, new RecordCreationRefusal(RecordCreationRefusalKind.OwnerUnresolved,
                refused.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource, refused.Reason ?? "no owner could be resolved")
            {
                OwnerDecision = refused,
            });
        }

        // The Secure team may own only the CHILD tables of the ownership set. A work assignment or project filed under a
        // secure record was created into isolation by the plan; reaching here with a Secure owner means the plan found no
        // secure parent while the resolver did — the two cannot be reconciled now, so nothing is created (fail closed).
        if (owner.IsSecureOwner && SecureRootInheritance.Inherits(table))
        {
            return (null, SecureFilingRefused(owner,
                $"Whether this '{table}' is filed under a secure record could not be decided consistently " +
                "(the records it names changed while it was being created). It was NOT created. Try again."));
        }

        if (owner.IsSecureOwner && !RecordOwnershipResolver.IsReparentableChild(table))
        {
            return (null, SecureFilingRefused(owner, RecordOwnershipResolver.IsOwnershipParent(table)
                ? $"A '{table}' record cannot be created under a secure record from chat: it would itself have to be "
                  + "made secure (its own owner team, container and sharing — the record's Make Secure path). It was "
                  + "NOT created. Create it from the secure record itself."
                : $"A '{table}' record cannot be filed under a secure record from chat, and was NOT created. Create it "
                  + "from the record itself."));
        }

        return (owner, null);
    }

    // ── Repair (task 016): step 7 for an existing row. Here, beside the create's step 7, because it is the same decision
    // (ResolveOwnerOverAsync, OwnerDecided) and the owner-write census (RecordOwnerAssignmentCensusTests) proves a Routed
    // write against the resolver calls of its own file. The rest of Repair is RecordCreationService.Repair.cs. ──────────

    /// <summary>
    /// What step 7 decided for a repair: the business unit the row is owned in afterwards, the owner refusal, and — review
    /// F1-b — why the row's mapping must not run (its filing resolves to the Secure team that does not own it yet, or its
    /// filing's owner could not be decided), or <c>null</c>.
    /// </summary>
    private sealed record OwnerRepair(Guid? BusinessUnit, RecordCreationRefusal? Refusal, string? MappingBlocked = null);

    /// <summary>
    /// Step 7 for an existing row (Repair, task 016; FR-09), decided BEFORE the mapping (review F1-b). Only a
    /// systemuser-owned row (user- or application-owned) is re-owned; a team-owned row keeps its owner. The parents are the
    /// row's own (<see cref="RecordOwnershipContext.ParentsOf(Entity)"/>, plus a message's record-thread filing,
    /// <see cref="RecordThreadFiling"/>, as the reconciler and the reparent read them) and the create's resolution decides
    /// (<see cref="ResolveOwnerOverAsync"/>, <see cref="OwnerDecided"/>). A row filed under nothing is owned by the team of
    /// the PERSON who owns it (I-6, the acting user); an application-owned one has no such person and is refused
    /// (<see cref="RecordOwnerRefusal.NoOwnerSource"/>) — never given the application user's team. A table's ownership
    /// exception (an unfiled communication keeps its creator, E1) keeps the owner. Nothing is written here: the owner joins
    /// the repair's one patch.
    /// </summary>
    /// <remarks>
    /// <para><b>A secure-flagged root is never re-owned</b> (review F1-a): a matter, project or work assignment whose
    /// <c>sprk_issecure</c> is not an explicit <c>false</c> (empty reads as flagged, as the resolver reads it) is
    /// provisioned by task 144's transition, never by a bare re-own (owner S6).</para>
    /// <para><b>The mapping gate</b> (review F1-b): a FILED row's filing is resolved whatever happens to its owner. When it
    /// resolves to the Secure team (I-2) and that team does not own the row yet, or it cannot be decided, the mapping is
    /// blocked — Create never copies a secure record's data into a row that is not isolated.</para>
    /// <para><b>Escalation (task 016 trigger 2), fail closed until the owner decides:</b> an owner in another business unit
    /// than the row's is NOT written on a table whose rows can have children inheriting access (every ownership-parent
    /// table) — the move would carry those children's access with it. A leaf table (no rows filed under it) is re-owned:
    /// the move is the row's own.</para>
    /// </remarks>
    private async Task<OwnerRepair> RepairOwnerAsync(
        string table, RecordTableRule rule, Entity row, Dictionary<string, object> patch, List<RecordRepairField> report,
        List<RecordOwnershipParent> resolvedOver, CancellationToken ct)
    {
        var rowUnit = row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn) is { } unit && unit.Id != Guid.Empty
            ? unit.Id
            : (Guid?)null;
        var current = row.GetAttributeValue<EntityReference>(OwnerColumn);

        // Why the owner is kept, when it is (state, reason); null when this repair may re-own the row.
        (RecordRepairFieldState State, string Reason)? keep = null;
        if (current is null || current.Id == Guid.Empty)
            keep = (RecordRepairFieldState.Skipped, RecordRepairReason.OwnerUnknown);
        else if (!string.Equals(current.LogicalName, "systemuser", StringComparison.OrdinalIgnoreCase))
            keep = (RecordRepairFieldState.Present, RecordRepairReason.OwnerTeamOwned);
        else if (rule.Kind == RecordTableKind.Root && row.GetAttributeValue<bool?>(AccessFollowsParent.SecureFlagColumn) != false)
            keep = (RecordRepairFieldState.Skipped, RecordRepairReason.OwnerRootSecure);

        var noun = NounOf(table);
        var filed = false;
        RecordOwnerResolution owner;
        try
        {
            var threadParents = await RecordThreadFiling.ParentsAsync(_entities, table, RecordThreadFiling.ThreadOf(row), ct)
                .ConfigureAwait(false);
            var parents = RecordOwnershipContext.ParentsOf(row).Concat(threadParents).Distinct().ToArray();
            // UAC task 194b R4: the same parents keyed by the column that files the row there, for the one builder.
            var keyed = RecordOwnershipContext.LookupsOf(row).Concat(RecordThreadFiling.FilingLookupsOf(threadParents)).ToList();
            // (RecordThreadFiling.QuestionWithThreadFiling is this question without the pair; OwnerQuestionOf adds the pair.)
            var pairId = row.GetAttributeValue<string>(SecureRootInheritance.PairIdColumn);
            var rowPairId = pairId;
            var pairType = row.GetAttributeValue<EntityReference>(SecureRootInheritance.PairTypeColumn)?.Id;
            filed = parents.Length > 0 || !string.IsNullOrWhiteSpace(pairId);

            // Review F1-e (i): the records the owner is decided over are the only ones the mapping may copy from. The pair
            // counts only where it files the row on its own (a project, PairParentAsync); read here once, and handed to the
            // resolution as a parent. A pair that cannot be told is left to ResolveOwnerOverAsync, which refuses it.
            var pairParent = await PairParentAsync(_entities, table, pairId, pairType, ct).ConfigureAwait(false);
            if (pairParent.Parent is { } pairNamed)
            {
                parents = [.. parents, pairNamed];
                keyed.Add(KeyValuePair.Create(SecureRootInheritance.PairIdColumn, (RecordOwnershipParent?)pairNamed));
                pairId = null;
            }

            resolvedOver.AddRange(parents);
            if (keep is null && rule.OwnershipException is not null && parents.Length == 0)
                keep = (RecordRepairFieldState.Skipped, RecordRepairReason.OwnerKeptByException);

            if (keep is { } kept && !filed)
                return OwnerKept(rowUnit, kept, null, report); // nothing to re-own, and no filing to gate the mapping on

            // I-6 for an unfiled row: the PERSON who owns it acts. An application user, or one that cannot be told, is no one.
            Guid? actingUser = keep is null && parents.Length == 0
                && await ApplicationUserCheck.IsApplicationUserAsync(_entities, current!.Id, _logger, ct).ConfigureAwait(false) == false
                    ? current.Id
                    : null;

            var resolved = await ResolveOwnerOverAsync(
                    table, keyed, pairInvalidId: false, pairId, pairType, callerObjectId: null,
                    callerSystemUserId: actingUser, requestedBy: null, ct, subjectPairId: rowPairId)
                .ConfigureAwait(false);
            if (resolved.Refusal is { } undetermined)
            {
                return keep is { } keptOnRefusal
                    ? OwnerKept(rowUnit, keptOnRefusal, RecordRepairReason.MappingFilingUndecided, report)
                    : OwnerRefused(row, undetermined, report, filed ? RecordRepairReason.MappingFilingUndecided : null);
            }

            owner = resolved.Owner!;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORD-REPAIR] the owner of {Table} {Id} could not be decided (fault); it was not changed", table, row.Id);
            var blockedOnFault = filed ? RecordRepairReason.MappingFilingUndecided : null;
            return keep is { } keptOnFault
                ? OwnerKept(rowUnit, keptOnFault, blockedOnFault, report)
                : OwnerRefused(row, OwnerUnavailable(noun), report, blockedOnFault);
        }

        // F1-b: a filing that resolves to the Secure team which does not own the row yet — or to no owner at all.
        var mappingBlocked = !owner.IsOwned
            ? (filed ? RecordRepairReason.MappingFilingUndecided : null)
            : owner.IsSecureOwner && !(current is { LogicalName: "team" } && current.Id == owner.OwningTeamId)
                ? RecordRepairReason.MappingSourceSecure
                : null;

        if (keep is { } keptAfterResolution)
            return OwnerKept(rowUnit, keptAfterResolution, mappingBlocked, report);

        var (decided, refusal) = OwnerDecided(table, owner);
        if (refusal is not null)
            return OwnerRefused(row, refusal, report, mappingBlocked);

        var team = decided!.OwningTeamId!.Value;
        Guid teamUnit;
        try
        {
            teamUnit = await TeamBusinessUnitAsync(team, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORD-REPAIR] the business unit of the owner team {Team} of {Table} {Id} could not be read; " +
                                 "the owner was not changed", team, table, row.Id);
            return OwnerRefused(row, OwnerUnavailable(noun), report, mappingBlocked);
        }

        if (rowUnit != teamUnit && RecordOwnershipResolver.IsOwnershipParent(table))
        {
            _logger.LogWarning(
                "[RECORD-REPAIR] ESCALATION (task 016 trigger 2): re-owning {Table} {Id} would move it from business unit {From} " +
                "to {To} while rows filed under it inherit its access; the owner was not changed", table, row.Id, rowUnit, teamUnit);
            report.Add(RecordRepairField.Skipped(RecordRepairPart.Owner, OwnerColumn, RecordRepairReason.OwnerMovesBusinessUnit));
            return new OwnerRepair(rowUnit, null, mappingBlocked);
        }

        patch[OwnerColumn] = new EntityReference("team", team);
        report.Add(RecordRepairField.Repaired(RecordRepairPart.Owner, OwnerColumn));
        return new OwnerRepair(teamUnit, null, mappingBlocked);
    }

    /// <summary>The owner is kept for <paramref name="kept"/>'s reason; the row stays in its own business unit.</summary>
    private static OwnerRepair OwnerKept(
        Guid? rowUnit, (RecordRepairFieldState State, string Reason) kept, string? mappingBlocked, List<RecordRepairField> report)
    {
        report.Add(new RecordRepairField(RecordRepairPart.Owner, OwnerColumn, kept.State, kept.Reason));
        return new OwnerRepair(rowUnit, null, mappingBlocked);
    }

    /// <summary>The owner is unchanged, so the row stays in its own business unit; the refusal is reported, typed.</summary>
    private OwnerRepair OwnerRefused(
        Entity row, RecordCreationRefusal refusal, List<RecordRepairField> report, string? mappingBlocked)
    {
        _logger.LogInformation("[RECORD-REPAIR] the owner of {Table} {Id} was not changed: {Code}", row.LogicalName, row.Id, refusal.Code);
        report.Add(RecordRepairField.Refused(RecordRepairPart.Owner, OwnerColumn, refusal.Code));
        var unit = row.GetAttributeValue<EntityReference>(OwningBusinessUnitColumn) is { } held && held.Id != Guid.Empty
            ? held.Id
            : (Guid?)null;
        return new OwnerRepair(unit, refusal, mappingBlocked);
    }

    private static RecordCreationRefusal OwnerUnavailable(string noun) => new(
        RecordCreationRefusalKind.Unavailable, RecordCreationRefusal.OwnerUnavailableCode,
        $"The owner of this {noun} could not be decided right now, so it was not changed.");

    /// <summary>The G5 core's secure-filing refusal, answered as the owner refusal it is today (NoOwnerSource, 409).</summary>
    private static RecordCreationRefusal SecureFilingRefused(RecordOwnerResolution owner, string detail) =>
        new(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoOwnerSource, detail) { OwnerDecision = owner };

    /// <summary>The ADR-024 record-type catalog (the pair's type lookup target).</summary>
    private const string RecordTypeTable = "sprk_recordtype_ref";

    /// <summary>
    /// FR-09 (task 015): the record a row's ADR-024 pair files it under ON ITS OWN, as an ownership parent —
    /// <see cref="SecureRootInheritance.PairParentAsync"/> (moved there unchanged, so the ownership resolver's look-through
    /// reads a project's pair by the same rule, owner decision 2026-10-29).
    /// </summary>
    internal static Task<(RecordOwnershipParent? Parent, string? Undetermined)> PairParentAsync(
        IGenericEntityService entities, string table, string? idText, Guid? typeId, CancellationToken ct) =>
        SecureRootInheritance.PairParentAsync(entities, table, idText, typeId, ct);

    /// <summary>The search-index lookup's navigation property on the six search-indexed tables (matter, project, document, work assignment, event, invoice) —
    /// the bind every browser create already sends (<c>matterService.ts</c>, <c>DocumentRecordService.ts</c>: live).</summary>
    internal const string SearchIndexNavigationProperty = "sprk_AI_Search_Index";

    /// <summary>The entity set of the AI search index catalog (<c>sprk_aisearchindex</c>).</summary>
    internal const string SearchIndexEntitySet = "sprk_aisearchindexes";

    /// <summary>What step 8 decided: the step's record, the columns it writes (none, one or both), what it applied, and its warning.</summary>
    private sealed record SearchIndexFill(
        RecordCreationStepRecord Step, IReadOnlyDictionary<string, object?> Fields, string? Warning = null,
        Guid? AppliedIndex = null, string? AppliedName = null)
    {
        public static SearchIndexFill Nothing(RecordCreationStepState state, string note) =>
            new(new RecordCreationStepRecord(RecordCreationStep.SearchIndexDefault, state, note), new Dictionary<string, object?>());
    }

    /// <summary>
    /// Step 8 (FR-38 amending FR-10, I-5; tasks 015 and 039): on a table the rules registry marks search-indexed
    /// (<see cref="RecordTableRule.SearchIndexed"/>), the ONLY writer of both index columns: the business unit of the record's
    /// FINAL owning team (<paramref name="owningTeam"/>, after step 7: the resolver's team, a parent-inherited owner, or the
    /// named Secure Record Owners team of a root created into isolation) supplies its <c>sprk_searchindexname</c> and
    /// <c>sprk_ai_search_index</c> — the columns <see cref="Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver"/> reads at index
    /// time, by its own names. Nothing reaches this step carrying either column (dropped before step 5), so there is no
    /// fill-only check: it writes that unit's two values, or nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>Never the caller's business unit, never a walk up to the root, never a fallback.</b> A unit with no index
    /// leaves the column empty (not sent) and logs <c>search_index_unset_for_owner_bu</c>, ids only: the table, the owning team
    /// and the unit.</para>
    /// <para><b>Non-fatal</b>, as the Office create's business-unit defaults are: these are index ROUTING hints, not a storage
    /// location or an access boundary, and the resolver walks the record's owning business unit at index time anyway. A read
    /// that fails writes nothing and tells the caller once (the Office create's words); the create stands.</para>
    /// <para><b>No precedence chosen</b> between the two columns: each is copied from its own business-unit counterpart, so a
    /// unit whose two values disagree is passed on as it is and the resolver's lookup-first order decides at index time, as
    /// it does today.</para>
    /// </remarks>
    private async Task<SearchIndexFill> SearchIndexDefaultAsync(
        string table, RecordTableRule rule, Guid owningTeam, string noun, CancellationToken ct)
    {
        if (!rule.SearchIndexed)
            return SearchIndexFill.Nothing(RecordCreationStepState.NoOp, "the table is not search-indexed");

        try
        {
            var unit = await TeamBusinessUnitAsync(owningTeam, ct).ConfigureAwait(false);
            var defaults = await BusinessUnitSearchDefaultsAsync(unit, ct).ConfigureAwait(false);

            var fields = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (defaults.Name is { } indexName)
            {
                fields[SearchIndexColumns.LegacyTextColumn] = indexName;
            }

            if (defaults.Index is { } index)
            {
                fields[$"{SearchIndexNavigationProperty}@odata.bind"] = $"/{SearchIndexEntitySet}({index:D})";
            }

            if (defaults.Name is null || defaults.Index is null)
            {
                // Ids and states only: the table, the owning team, the unit and which column has none (the usual case in a
                // customer environment: catalog rows but no business-unit defaults, owner Q2 = (B)).
                _logger.LogInformation(
                    "[RECORD-CREATE] search_index_unset_for_owner_bu: table={Table} owningTeam={OwningTeam} businessUnit={BusinessUnit} " +
                    "lookupUnset={LookupUnset} nameUnset={NameUnset}",
                    table, owningTeam, unit, defaults.Index is null, defaults.Name is null);
            }

            return new SearchIndexFill(
                new RecordCreationStepRecord(RecordCreationStep.SearchIndexDefault, RecordCreationStepState.Ran,
                    $"{fields.Count} value(s) written from the owning business unit (the only writer)"),
                fields, AppliedIndex: defaults.Index, AppliedName: defaults.Name);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RECORD-CREATE] the owning business unit's search-index defaults for a new {Table} could not be read; it is " +
                "created without them and index routing falls back to its owning business unit", table);
            return new SearchIndexFill(
                new RecordCreationStepRecord(RecordCreationStep.SearchIndexDefault, RecordCreationStepState.Failed,
                    "the owning business unit's defaults could not be read; nothing filled"),
                new Dictionary<string, object?>(),
                "Business-unit search defaults could not be read, so they were not applied. Document indexing for this " +
                $"{noun} falls back to its business unit's settings.");
        }
    }

    /// <summary>
    /// The business unit of <paramref name="owningTeam"/>, read app-only (steps 7 and 8; shared with Repair, task 016).
    /// </summary>
    /// <exception cref="InvalidOperationException">The team reported no business unit.</exception>
    private async Task<Guid> TeamBusinessUnitAsync(Guid owningTeam, CancellationToken ct)
    {
        var team = await _entities.RetrieveAsync("team", owningTeam, ["businessunitid"], ct).ConfigureAwait(false);
        if (team?.GetAttributeValue<EntityReference>("businessunitid") is not { } unit || unit.Id == Guid.Empty)
            throw new InvalidOperationException($"The owner team {owningTeam:D} reported no business unit.");
        return unit.Id;
    }

    /// <summary>
    /// A business unit's two search-index defaults (FR-10), read app-only by the columns
    /// <see cref="Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver"/> reads at index time: the trimmed name (none when blank)
    /// and the catalog row (none when unset). Shared by step 8 and Repair (task 016).
    /// </summary>
    private async Task<(string? Name, Guid? Index)> BusinessUnitSearchDefaultsAsync(Guid businessUnit, CancellationToken ct)
    {
        var row = await _entities.RetrieveAsync(SearchIndexColumns.BusinessUnitEntity, businessUnit,
                [SearchIndexColumns.LegacyTextColumn, SearchIndexColumns.LookupColumn],
                ct)
            .ConfigureAwait(false);
        var name = row?.GetAttributeValue<string>(SearchIndexColumns.LegacyTextColumn)?.Trim() is { Length: > 0 } text ? text : null;
        var index = row?.GetAttributeValue<EntityReference>(SearchIndexColumns.LookupColumn) is { } reference && reference.Id != Guid.Empty
            ? reference.Id
            : (Guid?)null;
        return (name, index);
    }

    /// <summary>
    /// Step 12 (FR-38, UAC-r2 condition 4): logs <c>client_search_index_dropped</c> for each value dropped before step 5 that
    /// differs from the index step 8 applied, ids only (<see cref="RecordTableRules.LogSearchIndexDropped"/>). The record id is
    /// known only now. Never throws: a log must not fail a create that has already landed.
    /// </summary>
    private void LogDroppedSearchIndex(string table, Guid id, Decision decision, string correlationId)
    {
        if (decision.SearchIndexDrops.Count == 0)
            return;

        try
        {
            RecordTableRules.LogSearchIndexDropped(
                _logger, table, id, decision.SearchIndexDrops, decision.AppliedSearchIndex, correlationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "[RECORD-CREATE] the dropped search-index log of {Table} {Id} faulted", table, id);
        }
    }

    /// <summary>
    /// The create payload of an OWNED row: the item's body, owned by the resolver's team, with the person who asked as its
    /// creator person (task 146 c1-r1) — the deleted owned create's (<c>OwnedChildWrite.CreateAsync</c>), unchanged.
    /// </summary>
    private static Dictionary<string, object?> OwnedCreateFields(
        RecordOwnerResolution owner, string table, DataverseWriteItemMapper.MappedItem item)
    {
        var fields = OwnedChildWrite.BodyFields(item.JsonBody);
        RemoveOtherSpellings(fields, "ownerid@odata.bind");
        fields["ownerid@odata.bind"] = $"/teams({owner.OwningTeamId!.Value})";
        var creator = new Dictionary<string, object?>(StringComparer.Ordinal);
        owner.StampCreatorOn(creator, table);
        foreach (var (key, value) in creator)
            PutServerValue(fields, key, value);
        return fields;
    }

    /// <summary>
    /// The create payload of a ROOT created INTO isolation (task 158 r1, owner round 31 item 2): owned by the plan's named
    /// Secure Record Owners team, flagged in the create itself, with the person it is made for as its creator person —
    /// the deleted <c>OwnedChildWrite.CreateIntoIsolationAsync</c>'s (017 2d-5), unchanged. Provisioning's re-entry steps complete it (step 12).
    /// </summary>
    private static Dictionary<string, object?> IsolatedCreateFields(
        string table, DataverseWriteItemMapper.MappedItem item, Guid secureTeam, Guid creatorPerson)
    {
        var fields = OwnedChildWrite.BodyFields(item.JsonBody);
        RemoveOtherSpellings(fields, "ownerid@odata.bind");
        fields["ownerid@odata.bind"] = $"/teams({secureTeam})";
        PutServerValue(fields, "sprk_issecure", true);
        if (RecordCreatorPerson.IsStamped(table))
        {
            var creator = new Dictionary<string, object?>(StringComparer.Ordinal);
            RecordCreatorPerson.Bind(creator, creatorPerson);
            foreach (var (key, value) in creator)
                PutServerValue(fields, key, value);
        }

        return fields;
    }

    /// <summary>
    /// Writes a server-owned value under its canonical <paramref name="key"/>, first removing any other spelling of that key
    /// the row's body already carries. The body's keys are case-sensitive and a system principal's item is not checked by
    /// step 2, so without this the body could hold two keys that differ only in case (UAC-r2's review of 015, K3, for the
    /// search-index bind; #2238 / its review of 018a, K-c, for the owner, the creator person and the secure flag).
    /// </summary>
    private static void PutServerValue(Dictionary<string, object?> fields, string key, object? value)
    {
        RemoveOtherSpellings(fields, key);
        fields[key] = value;
    }

    /// <summary>
    /// <see cref="PutServerValue"/>'s first half, for the owner bind: its assignment stays a literal
    /// <c>fields["ownerid@odata.bind"] = …</c> where the owner is decided, which the owner-write census
    /// (<c>RecordOwnerAssignmentCensusTests</c>) reads.
    /// </summary>
    private static void RemoveOtherSpellings(Dictionary<string, object?> fields, string key)
    {
        foreach (var spelled in fields.Keys
                     .Where(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase) && !string.Equals(k, key, StringComparison.Ordinal))
                     .ToArray())
        {
            fields.Remove(spelled);
        }
    }

    /// <summary>
    /// The secure-create plan's own AppendTo check on the secure parents, as the caller — ported from
    /// the owned create (<c>OwnedChildWrite.CreateAsync</c>, deleted in 017): a parent counts as checked only when a lookup step 5 ASKED about names the
    /// same record (table and id); a parent the pair names is asked again.
    /// </summary>
    private static Func<IReadOnlyList<SecureFilingParent>, CancellationToken, Task<RecordOwnerResolution?>> CallerMayFileUnder(
        IDataverseUserClient user, Guid me, DataverseWriteItemMapper.MappedItem item, IReadOnlySet<string>? serverSetLookupColumns)
    {
        var checkedRecords = item.Lookups
            .Where(l => serverSetLookupColumns?.Contains(l.Column) != true)
            .Select(l => (Table: l.RelatedTable.ToLowerInvariant(), l.RecordId))
            .ToHashSet();
        return async (parents, token) =>
        {
            var parentLookups = parents
                .Where(p => !checkedRecords.Contains((p.Table.ToLowerInvariant(), p.Id)))
                .Select(p => new DataverseWriteItemMapper.MappedLookup(
                    "filed under", p.Table, SecureDesignationRemoval.EntitySetFor(p.Table), p.Id))
                .ToArray();
            var appendTo = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(user, me, parentLookups, token).ConfigureAwait(false);
            return appendTo.Succeeded
                ? null
                : RecordOwnerResolution.Refused(DataverseUserClientErrorCodes.AccessDenied,
                    appendTo.Denied ?? "you do not have permission to file this record under the secure record it names");
        };
    }

    /// <summary>Step 5's answer in this service's contract, in the child-record route's order.</summary>
    private static RecordCreationRefusal CallerCheckRefused(OwnedChildWrite.Outcome check, string noun) => check switch
    {
        { ClientFailure: { } failure } => Upstream(failure, noun),
        { Invalid: { } invalid } => new(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode, invalid),
        { ParentUnavailable: true } => new(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode,
            check.Denied ?? NotAvailableToFileUnder) { RefusedColumn = check.RefusedColumn },
        { Denied: { } denied } => new(RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.DeniedCode, denied),
        _ => new(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode, $"The {noun} was not created."),
    };

    /// <summary>
    /// The secure-create plan's refusal (task 158 r1), split as <c>ChildRecordEndpoints.PlanRefused</c> splits it today: no
    /// AppendTo on a secure parent is the uniform not-found; a caller walled off it, the secure flag or the access record
    /// is 403; a check that could not be made or a named team that could not be resolved is 500; anything else (an
    /// unreadable parent flag) is the owner refusal, 409 — never "not secure".
    /// </summary>
    private static RecordCreationRefusal PlanRefused(RecordOwnerResolution plan, string noun) => plan.RefusalCode switch
    {
        DataverseUserClientErrorCodes.AccessDenied => new(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode,
            plan.Reason ?? NotAvailableToFileUnder) { OwnerDecision = plan },
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccess
            or AccessFollowsParent.SecureFlagReasonCode
            or AccessInheritance.ServerOnlyReasonCode => new(RecordCreationRefusalKind.Forbidden, plan.RefusalCode!,
            $"The {noun} was not created: {plan.Reason ?? "it may not be filed there"}.") { OwnerDecision = plan },
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable
            or RecordOwnerRefusal.SecureOwnerTeamUnresolved => new(RecordCreationRefusalKind.Undecided, plan.RefusalCode!,
            $"The {noun} was not created: {plan.Reason ?? "whether it may be filed there could not be decided"}.") { OwnerDecision = plan },
        _ => new(RecordCreationRefusalKind.OwnerUnresolved, plan.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource,
            plan.Reason ?? "no owner could be resolved") { OwnerDecision = plan },
    };

    /// <summary>
    /// Step 5½: the table's pre-write rules in order; the first refusal wins, carried by reference (ebilling-r1 correction
    /// (c)). A not-found is the uniform not-found with the chat create's words (correction (b)); an undecided rule is
    /// <see cref="RecordCreationRefusalKind.Undecided"/> (500) with its code; any other coded refusal is
    /// <see cref="RecordCreationRefusalKind.Invalid"/> (400) with its code (task 017, F2). A system principal has no
    /// caller to ask as, so a table with rules refuses it (fail closed; task 012's open point).
    /// </summary>
    private static async Task<RecordCreationRefusal?> RunPreWriteRulesAsync(
        string table, RecordTableRule rule, IDataverseUserClient? user, DataverseWriteItemMapper.MappedItem item, string noun,
        CancellationToken ct)
    {
        if (user is null)
        {
            return new RecordCreationRefusal(RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.SystemPrincipalUnsupportedCode,
                $"A {noun} is checked as the person creating it ({string.Join(", ", rule.PreWriteRules.Select(r => r.Rule.Name))}), " +
                "and this create acts for no caller, so it was not created.");
        }

        var row = new PreWriteRow(user, table, item);
        foreach (var preWrite in rule.PreWriteRules)
        {
            var refusal = await preWrite.Check(row, ct).ConfigureAwait(false);
            if (refusal is null)
                continue;

            return refusal switch
            {
                { ParentUnavailable: true } => new RecordCreationRefusal(RecordCreationRefusalKind.NotFoundOrDenied,
                    RecordCreationRefusal.NotFoundCode, NotAvailableToFileUnder) { PreWrite = refusal },
                { ClientFailure: { } failure } => Upstream(failure, noun) with { PreWrite = refusal },
                // Task 017 (ebilling-r1 F2): a rule that could not decide is "not saved" (500) with its own code — never the
                // uniform not-found, which only NotFound() reaches (the arm above).
                { IsUndecided: true } => new RecordCreationRefusal(RecordCreationRefusalKind.Undecided, refusal.Code!, refusal.Detail!)
                {
                    PreWrite = refusal,
                },
                _ => new RecordCreationRefusal(RecordCreationRefusalKind.Invalid, refusal.Code!, refusal.Detail!) { PreWrite = refusal },
            };
        }

        return null;
    }

    /// <summary>A question asked as the caller failed at Dataverse: their failure, carried, never its text.</summary>
    private static RecordCreationRefusal Upstream(DataverseUserResponse failure, string noun) =>
        new(RecordCreationRefusalKind.Upstream, failure.ErrorCode ?? RecordCreationRefusal.UpstreamCode,
            $"Dataverse refused or failed a check made as you (HTTP {failure.StatusCode}), so the {noun} was not created.")
        {
            ClientFailure = failure,
        };

    /// <summary>
    /// The autonumber's alternate-key duplicate (<c>0x80060892</c>). On the Web API the create-by-upsert answers it 412
    /// (Precondition Failed: measured live for a POST by ContactIdentityStore; the PATCH upsert is task 013's assumption,
    /// to verify live) and <see cref="IFieldMappingDataverseService.UpdateRecordFieldsAsync"/> throws the status;
    /// <see cref="DataverseServiceClientImpl.IsAlternateKeyDuplicate"/> classifies the SDK fault and message forms.
    /// Dataverse rejects such a create whole, so the retry cannot double-create.
    /// </summary>
    private static bool IsAlternateKeyDuplicate(Exception ex) =>
        ex is HttpRequestException { StatusCode: HttpStatusCode.PreconditionFailed }
        || DataverseServiceClientImpl.IsAlternateKeyDuplicate(ex);

    /// <summary>
    /// Step 2's column policy for a CALLER's payload, with no I/O (so a chat create can be refused before it suspends for
    /// confirmation — task 017 review F-2 — from this rule, not a copy of it): the table's own server-owned columns and its
    /// autonumber first (the child-record route's order), then the ones every app-only create owns and the creator person
    /// (the G5 core's first check, the same words). Null when the payload names none.
    /// </summary>
    internal static RecordCreationRefusal? CallerBlockedColumnRefusal(RecordTableRule rule, string noun, IEnumerable<string> columns)
    {
        var named = columns as IReadOnlyCollection<string> ?? columns.ToList();
        if (named.FirstOrDefault(c => rule.TableServerOwnedColumns.Contains(c) || IsNumberColumn(rule, c)) is { } tableColumn)
        {
            return new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                RecordCreationRefusal.BlockedColumnCode, $"Column '{tableColumn}' of a {noun} is set by Spaarke — omit it.");
        }

        if (named.FirstOrDefault(c => RecordTableRules.CommonServerOwnedColumns.Contains(c) || RecordCreatorPerson.NamesColumn(c))
            is { } serverColumn)
        {
            return new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                RecordCreationRefusal.BlockedColumnCode,
                $"Column '{serverColumn}' is set by the server on a record the application creates for you — omit it.");
        }

        return null;
    }

    /// <summary>The table's autonumber column (<see cref="RecordTableRule.AutonumberColumn"/>), which the platform fills.</summary>
    private static bool IsNumberColumn(RecordTableRule rule, string column) =>
        rule.AutonumberColumn is { } number && string.Equals(column, number, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the row names a record it is filed under — the plan's own test before it reads anything
    /// (<see cref="SecureRootFilingGate.PlanCreateAsync"/>: a create that writes none of the table's filing columns is
    /// ordinary).
    /// </summary>
    private static bool FilesUnderARecord(string table, DataverseWriteItemMapper.MappedItem item)
    {
        var filing = SecureRootInheritance.FilingColumnsOf(table);
        return OwnedChildWrite.WritesOf(item).Any(w => filing.Contains(SecureRootInheritance.NormalizeColumn(w.Key)));
    }

    /// <summary>
    /// Step 12, in the existing paths' order: the number read-back (autonumber tables), the isolated root's completion, the
    /// "Assigned *" grants of a root, the I-1 re-stamp and I-16 (step 10), the secure child's sharee mirror, the membership
    /// event. Never throws; returns a refusal only when an isolated root had to be removed again.
    /// </summary>
    private async Task<RecordCreationRefusal?> AfterCreateAsync(
        string table, Guid id, RecordTableRule rule, Decision decision, string noun, string correlationId, List<string> warnings,
        Action<SecureRootInheritResult> isolatedCompleted)
    {
        if (rule.AutonumberColumn is { } numberColumn)
            await WarnIfNumberMissingAsync(table, id, numberColumn, noun, warnings).ConfigureAwait(false);

        if (decision.IsolatedFor is { } isolatedFor)
        {
            var secured = await _rootFiling.CompleteIsolatedCreateAsync(table, id, isolatedFor, correlationId).ConfigureAwait(false);
            isolatedCompleted(secured);
            if (secured.RowRemoved)
            {
                _logger.LogWarning("[RECORD-CREATE] isolated {Table} {Id} removed again: its creator could not be shared ({Code})",
                    table, id, secured.ReasonCode);
                return new RecordCreationRefusal(RecordCreationRefusalKind.Undecided,
                    secured.ReasonCode ?? SecureRootInheritance.ReasonUnexpectedResult,
                    $"The {noun} is filed under a secure record, so it is created secure and shared to you, and that share " +
                    $"could not be made, so the new {noun} was removed again. Nothing was created. " +
                    (secured.CompletesAutomatically
                        ? "Try again in a few minutes."
                        : $"Securing it was refused ({secured.ReasonCode}), and trying again will not change that: an " +
                          "administrator needs to review your access to the secure record it would be filed under."));
            }

            if (secured.RowStranded)
            {
                warnings.Add(
                    $"The {noun} was created as a secure record, but it could not be shared to you and could not be removed " +
                    $"again ({secured.ReasonCode}); " +
                    (secured.CompletesAutomatically
                        ? "it is shared to you automatically once that step succeeds (it is retried every few minutes)."
                        : "it will not be shared to you automatically — only an administrator can open it, and an administrator " +
                          "needs to review and remove it."));
            }
            else if (!secured.IsComplete)
            {
                warnings.Add(
                    $"The {noun} was created as a secure record shared to you, but securing it could not be finished yet " +
                    $"({secured.ReasonCode}); it is completed automatically within a few minutes.");
            }
        }

        if (rule.Kind == RecordTableKind.Root)
        {
            // I-12 L1: the "Assigned *" contacts' grant or share now. Never throws; a fault is the job's.
            await AssignedAccessMaterializer.RunAfterWriteAsync(
                _scopes, table, id, writtenColumns: null, decision.GrantorOid, _logger, CancellationToken.None).ConfigureAwait(false);
        }

        await AfterCreateStepAsync("core-ancestor stamp (I-1)", table, id, async () =>
        {
            var restamp = await _restamper.AfterWriteAsync(table, id, decision.Item!.Columns, CancellationToken.None).ConfigureAwait(false);
            if (!restamp.Complete)
            {
                _logger.LogWarning(
                    "[RECORD-CREATE] created {Table} {Id}, but its core-ancestor stamp did not finish (failures={Failures}); the " +
                    "stamp job completes it within one cycle", table, id, restamp.Failures.Count);
            }
        }).ConfigureAwait(false);

        await AfterCreateStepAsync("inherited Access Permission (I-16)", table, id,
            () => _restamper.RefreshInheritedAccessPermissionAsync(table, id, CancellationToken.None)).ConfigureAwait(false);

        if (SecureChildLineage.IsChild(table))
        {
            await AfterCreateStepAsync("secure record's sharee mirror", table, id, async () =>
            {
                var result = await _shares.SyncChildAsync(table, id, CancellationToken.None).ConfigureAwait(false);
                if (result.Status is SecureChildShareSyncStatus.Failed or SecureChildShareSyncStatus.Incomplete)
                {
                    _logger.LogWarning(
                        "[RECORD-CREATE] {Table} {Id}: the secure record's sharees were not mirrored ({Status}: {Detail}); the " +
                        "two-minute reconcile completes it", table, id, result.Status, result.Detail);
                }
            }).ConfigureAwait(false);
        }

        if (MembershipPublishingTables.Contains(table) && decision.OwningTeamId is { } team)
        {
            // ADR-034 Q2 fire-and-forget: never awaited by the create, never throws; the nightly reconciliation is the backstop.
            _ = MembershipOwnerEvents.PublishOwnerAddedAsync(
                _membership, _entities, table, id, new EntityReference("team", team), correlationId, _logger, CancellationToken.None);
        }

        return null;
    }

    /// <summary>One post-create step: a failure is logged and the create stands (step 12 never throws).</summary>
    private async Task AfterCreateStepAsync(string step, string table, Guid id, Func<Task> run)
    {
        try
        {
            await run().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] created {Table} {Id}, but its {Step} faulted; the reconciliation completes it",
                table, id, step);
        }
    }

    /// <summary>
    /// The Office create's number read-back (task 076): a blank number is a blank NAME in every lookup and list. The row
    /// exists when this runs, so a read that faults is logged and otherwise ignored.
    /// </summary>
    private async Task WarnIfNumberMissingAsync(string table, Guid id, string numberColumn, string noun, List<string> warnings)
    {
        Entity? created;
        try
        {
            created = await _entities.RetrieveAsync(table, id, [numberColumn], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] The new {Noun} {RecordId} could not be read back to confirm its number.", noun, id);
            return;
        }

        if (created is null || !string.IsNullOrWhiteSpace(created.GetAttributeValue<string>(numberColumn)))
            return;

        _logger.LogError(
            "record_number_unassigned: entity={Entity} id={RecordId} — created without a number, so it has a blank name; " +
            "{Column} has no autonumber in this environment (run scripts/Set-RecordNumberingSchema.ps1)", table, id, numberColumn);
        warnings.Add(
            $"The new {noun} was created without a number, so it shows without a name in lists. Ask an administrator to check " +
            "record numbering.");
    }

    /// <summary>The step record, with one log line per step (NFR-04).</summary>
    private sealed class StepTrace(string table, string correlationId, ILogger logger)
    {
        private readonly List<RecordCreationStepRecord> _steps = [];

        /// <summary>The step running now (for the fault log).</summary>
        public RecordCreationStep? Current { get; private set; }

        public void Begin(RecordCreationStep step) => Current = step;

        public void Record(RecordCreationStep step, RecordCreationStepState state, string? note) =>
            Add(new RecordCreationStepRecord(step, state, note));

        public void Add(RecordCreationStepRecord record)
        {
            Current = record.Step;
            _steps.Add(record);
            logger.Log(record.State is RecordCreationStepState.Refused or RecordCreationStepState.Failed ? LogLevel.Warning : LogLevel.Debug,
                "[RECORD-CREATE] {Table} step {Step}: {State} {Note} (correlationId={CorrelationId})",
                table, record.Step, record.State, record.Note, correlationId);
        }

        public RecordCreationOutcome Refused(RecordCreationRefusal refusal)
        {
            if (Current is { } step && (_steps.Count == 0 || _steps[^1].Step != step))
                Add(new RecordCreationStepRecord(step, RecordCreationStepState.Refused, refusal.Code));
            logger.LogWarning("[RECORD-CREATE] create of {Table} refused at {Step}: {Kind} {Code} (correlationId={CorrelationId})",
                table, Current, refusal.Kind, refusal.Code, correlationId);
            return Outcome(refusal, []);
        }

        public RecordCreationOutcome Outcome(RecordCreationRefusal refusal, IReadOnlyList<string> warnings) => new()
        {
            Refusal = refusal,
            Warnings = warnings,
            Steps = _steps.ToArray(),
            CorrelationId = correlationId,
        };

        public RecordCreationOutcome Created(
            Guid id, Guid? owningTeamId, bool isolated, IReadOnlyList<string> warnings,
            IReadOnlyList<RecordCreationWarning> mappingWarnings, IReadOnlyCollection<string> writtenColumns) => new()
        {
            CreatedId = id,
            WrittenColumns = writtenColumns,
            OwningTeamId = owningTeamId,
            Isolated = isolated,
            Warnings = warnings,
            MappingWarnings = mappingWarnings,
            Steps = _steps.ToArray(),
            CorrelationId = correlationId,
        };
    }
}

/// <summary>What to create: the table and the payload, and the request's facts the pipeline needs.</summary>
internal sealed record RecordCreationRequest(string Table, RecordCreationPayload Payload)
{
    private readonly IReadOnlySet<string>? _serverSetLookupColumns;

    /// <summary>
    /// Lookups the SERVER added to the row (e.g. the email draft's sender): they name the caller and cost no AppendTo.
    /// Task 017 (014 review R3-1): normalised ONCE, here, as the request is built — each name trimmed and lower-cased, blanks
    /// dropped, compared case-insensitively — so the K-2 / F-B guards (<c>ServerSetFilingColumn</c>,
    /// <c>ServerSetRootFilingLookups</c>), the mapping protection (<c>ProtectedFromMapping</c>) and step 5's AppendTo skip
    /// (<see cref="CallerWriteAuthorization.CheckCallerMayCreateAsync"/>) all read the one set: a padded or mixed-case name
    /// can no longer be "server-set" to one reader and the caller's own to another.
    /// </summary>
    public IReadOnlySet<string>? ServerSetLookupColumns
    {
        get => _serverSetLookupColumns;
        init => _serverSetLookupColumns = NormaliseColumns(value);
    }

    /// <summary>The one normalisation of <see cref="ServerSetLookupColumns"/>; <c>null</c> stays <c>null</c>.</summary>
    internal static IReadOnlySet<string>? NormaliseColumns(IEnumerable<string>? columns) =>
        columns?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The request's trace id (the ProblemDetails <c>correlationId</c>); a fresh one when absent.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Task 014: the field mapping this create asks for (step 3), or <c>null</c> for none — step 3 then does nothing and
    /// asks nothing. A caller's create only (a system principal with one is refused as an argument error).
    /// </summary>
    public RecordCreationMapping? Mapping { get; init; }

    /// <summary>
    /// Task 018a (UAC-r2 review of 013, K-3): lookups of the CALLER's payload that are optional REFERENCES (a matter type, a
    /// practice area, a project type), not values the create depends on. Each one bound is asked alone, as the caller, right
    /// after step 2: one the caller may not use (AppendTo, Append, field security), or whose check could not be made, is
    /// dropped with a <see cref="RecordCreationWarning.ReferenceValueDroppedCode"/> warning naming the column, and the create
    /// proceeds — the FR-08 path for a mapped value, not the refusal a caller's own lookup gets. Only the lookups the table
    /// declares (<see cref="RecordTableRule.OptionalReferenceLookups"/>) may be named; any other — the "for" person, an
    /// Assigned* or access column, a filing lookup, the regarding pair — is an argument error (task 018a fix round, F4-1).
    /// <c>null</c> (the default): every lookup is the caller's own, exactly as before. A caller's create only.
    /// </summary>
    public IReadOnlySet<string>? ReferenceLookupColumns { get; init; }

    /// <summary>
    /// Task 018a (task 014 review K-7): values an adapter supplies for columns the row may still lack AFTER field mapping —
    /// the last link of an order such as Office's "the contact the request names, else a mapped value, else the maker's
    /// contact". Each is written only when neither the caller nor a mapping rule supplied the column (fill-only, the FR-02
    /// test: a flag or choice whose key is present, an explicit null included, is supplied), in the form a mapping rule
    /// writes (a lookup as an <see cref="Microsoft.Xrm.Sdk.EntityReference"/>, a choice as an
    /// <see cref="Microsoft.Xrm.Sdk.OptionSetValue"/>, text, a number, a flag). A column the table does not have or that takes
    /// no value on create, or a value of the wrong kind, is dropped with a
    /// <see cref="RecordCreationWarning.DefaultValueDroppedCode"/> warning (field mapping's own checks); what is left is
    /// CHECKED at step 5 with the mapped values: one the caller may not set is dropped with that warning, a filing lookup the
    /// caller may not file under refuses. <c>null</c> (the default): nothing is defaulted. A caller's create only; a column
    /// field mapping may never write (<see cref="RecordCreationService.ProtectedFromMapping"/>, the regarding pair), two keys
    /// naming one column, a null or date value, or a table field mapping does not apply to, is an argument error.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? PostMappingDefaults { get; init; }

    /// <summary>
    /// Task 017 r3 (UAC-r2 review of PR #2486, F1): who the "Assigned *" step (I-12 L1) records as the person behind a root's
    /// create. The default, <see cref="AssignedAccessGrantor.Caller"/>, is the principal's own oid (the caller's, or a system
    /// principal's requester): the "Assigned To" contacts are invited and <c>sprk_grantedby</c> names that person.
    /// <see cref="AssignedAccessGrantor.None"/> hands the step no person: the grants and shares are still made, but no
    /// invitation is sent and no user is recorded - the AI chat create's behaviour, as it was before the service.
    /// </summary>
    public AssignedAccessGrantor Grantor { get; init; } = AssignedAccessGrantor.Caller;
}

/// <summary>Who a create names as the person behind its "Assigned *" access (see <see cref="RecordCreationRequest.Grantor"/>).</summary>
internal enum AssignedAccessGrantor
{
    /// <summary>The principal's own oid: the caller (or a system principal's requester). The default.</summary>
    Caller = 0,

    /// <summary>Nobody: no invitation is sent and no <c>sprk_grantedby</c> user is recorded.</summary>
    None = 1,
}

/// <summary>
/// The field mapping a caller's create asks for (FR-03, FR-04): the caller's own Read probe on the source, and the
/// explicit <c>mappingSource</c> when the caller names one (else the source is inferred from the row: the regarding pair,
/// then the single typed parent).
/// </summary>
/// <remarks>A class, not a record, and built only by <see cref="ForCaller"/>: it carries the caller's token (inside
/// <see cref="MappingSourceReader"/>), so it has no generated <c>ToString</c> or equality that could print it, and no way
/// to hold the system reader (which skips the Read probe).</remarks>
internal sealed class RecordCreationMapping
{
    private RecordCreationMapping(MappingSourceReader reader, string? sourceTable, Guid? sourceId)
    {
        Reader = reader;
        SourceTable = sourceTable;
        SourceId = sourceId;
    }

    /// <summary>Who reads the source: always the caller (Read-as-caller first, FR-04).</summary>
    internal MappingSourceReader Reader { get; }

    /// <summary>The explicit <c>mappingSource</c> table, if the caller named one.</summary>
    internal string? SourceTable { get; }

    /// <summary>The explicit <c>mappingSource</c> id, if the caller named one.</summary>
    internal Guid? SourceId { get; }

    /// <summary>Mapping read as the caller: <paramref name="probe"/> asks their Read on the source under their token.</summary>
    internal static RecordCreationMapping ForCaller(
        CallerRecordAccessProbe probe, string? callerBearerToken, string? sourceTable = null, Guid? sourceId = null) =>
        new(MappingSourceReader.ForCaller(probe, callerBearerToken), sourceTable, sourceId);
}

/// <summary>
/// A warning about a create that stands (task 014, ADR-019): a stable <see cref="Code"/>, the <see cref="Column"/> it is
/// about when there is one, and a user-facing <see cref="Detail"/> that names no source record and carries no value.
/// </summary>
internal sealed record RecordCreationWarning(string Code, string? Column, string Detail)
{
    /// <summary>The mapping source is missing, not readable by the caller, or could not be read — one outcome (FR-04).</summary>
    internal const string MappingSourceUnavailableCode = "record_creation.mapping_source_unavailable";

    /// <summary>Something was named as the source, but no single record can be told from it (FR-03).</summary>
    internal const string MappingSourceUndeterminedCode = "record_creation.mapping_source_undetermined";

    /// <summary>The field-mapping configuration or the table's column metadata could not be read: nothing was mapped.</summary>
    internal const string MappingUnavailableCode = "record_creation.mapping_unavailable";

    /// <summary>A field-mapping rule was skipped (a protected or regarding target, a rule that failed, a profile with no rules).</summary>
    internal const string MappingRuleSkippedCode = "record_creation.mapping_rule_skipped";

    /// <summary>A mapped value was not written (FR-08): the caller may not set it, it could not be checked, or the column cannot take it.</summary>
    internal const string MappedValueDroppedCode = "record_creation.mapped_value_dropped";

    /// <summary>Task 018a (K-3): a reference the caller chose (<see cref="RecordCreationRequest.ReferenceLookupColumns"/>) was not
    /// written: the caller may not use it, or it could not be checked.</summary>
    internal const string ReferenceValueDroppedCode = "record_creation.reference_value_dropped";

    /// <summary>Task 018a (K-7): a post-mapping default (<see cref="RecordCreationRequest.PostMappingDefaults"/>) was not written:
    /// the caller may not set it, it could not be checked, or the column cannot take it.</summary>
    internal const string DefaultValueDroppedCode = "record_creation.default_value_dropped";
}

/// <summary>The payload of a create: a caller's Dataverse Web API body, or an item already mapped (a chat tool, a server writer).</summary>
internal abstract record RecordCreationPayload
{
    private RecordCreationPayload()
    {
    }

    /// <summary>The Web API body a browser writer would have sent (<c>{NavigationProperty}@odata.bind</c> for lookups).</summary>
    internal sealed record WebApi(JsonElement Body) : RecordCreationPayload;

    /// <summary>A payload already mapped by <see cref="DataverseWriteItemMapper"/>.</summary>
    internal sealed record Mapped(DataverseWriteItemMapper.MappedItem Item) : RecordCreationPayload;
}

/// <summary>Who the create is for (FR-07): the caller, checked as themselves, or the system.</summary>
internal abstract record RecordCreationPrincipal
{
    private RecordCreationPrincipal()
    {
    }

    /// <summary>A caller: every check is asked through <paramref name="Client"/> under their own token (ADR-028).</summary>
    /// <param name="ObjectId">Their Entra object id (the <c>oid</c> claim), when the path has it.</param>
    internal sealed record CallerPrincipal(IDataverseUserClient Client, Guid? ObjectId) : RecordCreationPrincipal;

    /// <summary>A server writer: no as-caller check (steps 2 and 5); <paramref name="RequestedBy"/> is recorded when known.</summary>
    internal sealed record SystemPrincipal(RecordRequester? RequestedBy) : RecordCreationPrincipal;

    public static RecordCreationPrincipal ForCaller(IDataverseUserClient client, Guid? objectId) =>
        new CallerPrincipal(client ?? throw new ArgumentNullException(nameof(client)), objectId);

    public static RecordCreationPrincipal ForSystem(RecordRequester? requestedBy = null) => new SystemPrincipal(requestedBy);
}

/// <summary>The FR-06 steps, in the order they run.</summary>
internal enum RecordCreationStep
{
    /// <summary>1 — resolve the principal (WhoAmI under the caller's token; a system principal's requester).</summary>
    Principal,

    /// <summary>2 — the table's rules: refuse a server-owned column in the caller's payload (caller only).</summary>
    TableRules,

    /// <summary>3 — field mapping (task 014): fill-only, from the source the caller may read; a no-op when none is asked for.</summary>
    FieldMapping,

    /// <summary>4 — the merged payload, mapped.</summary>
    MapPayload,

    /// <summary>5 — the caller checks on the merged payload (caller only).</summary>
    CallerChecks,

    /// <summary>5½ — the table's pre-write rules; recorded only for a table that has some.</summary>
    PreWriteRules,

    /// <summary>6 — the secure-root plan.</summary>
    SecureRootPlan,

    /// <summary>7 — the owner.</summary>
    Owner,

    /// <summary>8 — the search-index default: the owning business unit's, fill-only, on a search-indexed table (task 015).</summary>
    SearchIndexDefault,

    /// <summary>9 — the creator person, the "for" person and the table defaults.</summary>
    CreatorAndDefaults,

    /// <summary>10 — the I-1 stamp and I-16, derived into the payload before the create (re-derived after it; see the service's remarks).</summary>
    StampAndInheritance,

    /// <summary>11 — the one app-only create.</summary>
    Create,

    /// <summary>12 — the post-create steps.</summary>
    PostCreate,
}

/// <summary>What one step did.</summary>
internal enum RecordCreationStepState
{
    /// <summary>The step's rule was applied.</summary>
    Ran,

    /// <summary>The step does not apply to this principal or table.</summary>
    Skipped,

    /// <summary>A seam a later task fills; nothing happens yet.</summary>
    NoOp,

    /// <summary>The step runs later in the pipeline (step 10, after the create).</summary>
    Deferred,

    /// <summary>The step refused the create.</summary>
    Refused,

    /// <summary>The step faulted.</summary>
    Failed,
}

/// <summary>One step as it ran.</summary>
internal sealed record RecordCreationStepRecord(RecordCreationStep Step, RecordCreationStepState State, string? Note);

/// <summary>What a create came to: the created id and its warnings, or the refusal — with every step, in order.</summary>
internal sealed record RecordCreationOutcome
{
    public Guid? CreatedId { get; init; }

    public RecordCreationRefusal? Refusal { get; init; }

    /// <summary>
    /// What the caller is told about a create that stands: the mapping step's warnings (their
    /// <see cref="RecordCreationWarning.Detail"/>, in <see cref="MappingWarnings"/> order) and then the post-create conditions.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Task 014 (FR-06 step 3, FR-08; ADR-019): the mapping step's warnings with their stable codes and, for a dropped mapped
    /// value, the column — never the source record's id, name or any value. Empty when nothing was mapped or a create was
    /// refused.
    /// </summary>
    public IReadOnlyList<RecordCreationWarning> MappingWarnings { get; init; } = [];

    public IReadOnlyList<RecordCreationStepRecord> Steps { get; init; } = [];

    /// <summary>
    /// Task 018b-1s (G1): the keys of the one app-only create's body (step 11), exactly as sent — a plain column by its
    /// logical name (<c>sprk_mattername</c>) and a lookup by its bind key (<c>sprk_MatterType@odata.bind</c>, the metadata's
    /// own navigation-property spelling; never expanded or rewritten). It is the final row, so it holds what the server added
    /// (the owner bind, the creator person, a search-index default) and leaves out a mapped value that was dropped. An
    /// adapter asks whether a column was written by comparing case-insensitively with and without the bind suffix. Empty
    /// for every refusal, including one after the create (an isolated root removed again) — the row no longer exists.
    /// </summary>
    public IReadOnlyCollection<string> WrittenColumns { get; init; } = [];

    /// <summary>The team the create bound as owner.</summary>
    public Guid? OwningTeamId { get; init; }

    /// <summary>The row was created INTO isolation (a work assignment or project under a secure record).</summary>
    public bool Isolated { get; init; }

    /// <summary>
    /// Task 017 (2d-3, additive): with <see cref="Isolated"/>, provisioning's answer for the isolated root's completion (the
    /// caller's share, its container, its parents' sharees) — its <see cref="SecureRootInheritResult.ReasonCode"/> included —
    /// so an adapter whose contract reports a stranded or unfinished completion as a refusal (the chat create) keeps its code.
    /// Set whenever the completion ran — on the created row, and on the refusal for a row removed again
    /// (<see cref="SecureRootInheritResult.RowRemoved"/>); <c>null</c> when the row was not created into isolation and on every
    /// refusal before the create.
    /// </summary>
    public SecureRootInheritResult? IsolatedCompletion { get; init; }

    /// <summary>The request's correlation id (the ProblemDetails <c>correlationId</c> of a refusal).</summary>
    public string CorrelationId { get; init; } = string.Empty;

    public bool Succeeded => CreatedId is not null && Refusal is null;
}
