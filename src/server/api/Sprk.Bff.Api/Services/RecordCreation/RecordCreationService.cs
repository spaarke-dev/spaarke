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
using Sprk.Bff.Api.Services.Jobs.Handlers;

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
/// the owner (step 7, <see cref="DecideOwnerAsync"/>) is task 015's: record-first for every create, roots and children
/// (FR-09). Step 8 (the search index) is a recorded no-op since task 041 (FR-40, #2502): the index follows the content type,
/// so no create writes either index column and no business unit is read for it.</para>
/// <para><b>Step 10 runs before the create, and again after it</b> (owner decision 2a, 2026-10-22; task 017). The I-1 stamp
/// columns the row is missing are derived from what it is filed under (<see cref="CoreAncestorRestamper.PlanStampOfRowAsync"/>,
/// whose fill-only part is bound) and bound into the create payload. FR-41 (#2483, task 042b): the derivation runs at step 6½,
/// before the owner, and step 7 asks the owner over the item's lookups as the row ENDS (<see cref="AsTheRowEnds"/>: each
/// derived stamp column takes its root, replacing a sent value that disagrees, 042b F1 / owner decision A'), so a create under
/// a team-owned intermediate of a Secure root is owned as the secure-child reconciler owns the final row; step 10 binds the
/// fill-only part of that plan and sets I-16 from it, with no second derivation (the step-12 restamp overwrites the rest). A derivation that fails refuses the create, nothing written
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

        // Task 042c fix round 4 (owner decision 2026-11-04, ontology D-204): a table whose rows are created only by its own writer
        // is refused here, with the words of a table this service does not create, before anything is read, mapped, checked or
        // written, whoever asks (the caller or the system principal). ebilling-r1 task 164a-1: and a table whose generic create
        // alone is refused (GenericCreateRefused covers both; an invoice line's update and delete are not refused).
        var rule = TableRules(table);
        if (rule is null or { GenericCreateRefused: true })
        {
            return trace.Refused(new RecordCreationRefusal(RecordCreationRefusalKind.Invalid,
                RecordCreationRefusal.TableUnsupportedCode, $"'{request.Table}' records are not created here."));
        }

        ValidateReferenceLookupColumns(table, rule, request);
        ValidateRouteAuthorizedLookups(table, request, principal);
        ValidateSecureFromCreation(table, request, principal);
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
        // The mapping step's warnings first (step 3 ran before the create), then the post-create ones.
        return trace.Created(id, decision.OwningTeamId, decision.IsolatedFor is not null,
            [.. decision.MappingWarnings.Select(w => w.Detail), .. warnings], decision.MappingWarnings,
            writtenColumns) with
        { IsolatedCompletion = isolatedCompletion };
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

        /// <summary>Task 049 fix round 2: the caller asked for the record to be secure (the words of step 12's outcomes).</summary>
        public bool SecureFromCreation { get; init; }

        public string? GrantorOid { get; init; }

        /// <summary>The maker's <c>systemuserid</c> (the caller's, or the system create's requester), when known. Step 12 asks
        /// whether the root's "for" person is this person's linked contact (task 049 fix round 1).</summary>
        public Guid? MakerSystemUserId { get; init; }

        /// <summary>Step 3's and step 5's warnings about the mapped values (task 014): what was not mapped, and why.</summary>
        public IReadOnlyList<RecordCreationWarning> MappingWarnings { get; init; } = [];

        /// <summary>Task 039 (FR-38): the client-supplied search-index values dropped before step 5 (logged at step 12, when the id is known).</summary>
        public IReadOnlyList<RecordTableRules.DroppedSearchIndexValue> SearchIndexDrops { get; init; } = [];

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

        // Task 049c (NFR-02, option a): steps 2 and 3 read the same table metadata as the caller — step 3's merge re-reads the
        // table's relationships and the entity sets step 2 just read. Within these two steps each such read is asked once
        // (CallerMetadataReadMemo: schema only; access answers, record reads and failures are never kept). Step 3½, step 5
        // (its own memo), the pre-write rules and steps 6 to 10 keep the caller's own client, so their reads are asked as before.
        var mappingClient = user is null ? null : new CallerMetadataReadMemo(user);

        // Task 049c (NFR-02, option b): step 3's reads for a source the caller NAMED depend on nothing steps 2 and 3 decide, so
        // they start now, beside step 2, and are taken at step 3 (MappingReadsPrefetch). Refused before step 3: cancelled and
        // awaited when this method returns, their answers dropped.
        var callerMapping = request.Mapping;
        await using var earlyMappingReads = NamedMappingSource(rule, request, principal) is { } namedSource
            ? MappingReadsPrefetch.Start(namedSource,
                token => ReadMappingInputsAsync(table, callerMapping!, mappingClient!, namedSource, token), ct)
            : null;

        // ── Step 2: the table's rules — refuse a server-owned column in the CALLER's payload ────────────────────────────
        trace.Begin(RecordCreationStep.TableRules);
        DataverseWriteItemMapper.MappedItem item;
        var referenceWarnings = new List<RecordCreationWarning>();

        // Task 039 (FR-38), task 041 (FR-40): on a search-indexed table both index columns are the SERVER's, and the server
        // writes neither (the index follows the content type). Whatever the payload carries for either is DROPPED here, before anything is checked — a drop,
        // never a refusal, and never the blocked-column refusal below — so no AppendTo is ever asked on the catalog row (#2399).
        // What a mapping rule or an adapter's default fills is dropped again after step 3½; every drop is logged at step 12.
        var indexDrops = new List<RecordTableRules.DroppedSearchIndexValue>();
        if (request.PreDroppedSearchIndex is { Count: > 0 } preDropped)
            indexDrops.AddRange(preDropped);
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

            // ebilling-r1 task 165d fix round (EB-D-207): and a column only the table's typed writer sets (RecordTableRule.WriterOnlyColumns:
            // the invoice's AP export claim) - an invoice must not be created already claimed. Asked here as it is for a caller (below).
            if (RecordTableRules.RefuseWriterOnlyColumns(table, item.Columns) is { } systemWriterOnly)
                return Decision.Refuse(WriterOnlyColumnRefusal(noun, systemWriterOnly));

            if (InheritedAccessPermission.RefuseAppOnlyWrite(table, item.Columns) is
                { RefusalCode: InheritedAccessPermission.AppOnlyDocumentWriteReasonCode } appOnlyRefusal)
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                    RecordCreationRefusal.BlockedColumnCode, $"The {noun} was not created: {appOnlyRefusal.Reason}."));
            }

            // fm task 054b: a BFF-owned column of a communication or its thread is written only by the BFF's typed writers, for
            // every identity — a system principal's create included, except a column the server set itself (the "for" person).
            if (InheritedAccessPermission.RefuseBffOwnedCommunicationColumn(
                    table, item.Columns.Where(c => request.ServerSetLookupColumns?.Contains(SecureRootInheritance.NormalizeColumn(c).Trim()) != true))
                is { } bffOwnedRefusal)
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                    RecordCreationRefusal.BlockedColumnCode, $"The {noun} was not created: {bffOwnedRefusal.Reason}."));
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
                var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(mappingClient!, table, webApiBody, ct).ConfigureAwait(false);
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
            if (CallerBlockedColumnRefusal(rule, noun, item.Columns, request.ServerSetLookupColumns) is { } blockedColumn)
                return Decision.Refuse(blockedColumn);

            // Task 018a (K-3): the references the request names — optional values the caller chose — are asked alone, as the
            // caller; one the caller may not use is dropped with a warning (FR-08's mapped-value path), never refused. Before
            // step 3, so a mapping rule may still fill a dropped reference (and is then checked as a mapped value).
            var droppedReferences = 0;
            if (request.ReferenceLookupColumns is { Count: > 0 } references)
            {
                var checkedReferences = await DropUnusableReferencesAsync(
                        table, mappingClient!, callerSystemUserId!.Value, item, references, request.ServerSetLookupColumns, noun,
                        referenceWarnings, ct)
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

        // Task 022 fix round 2: the mapping source's gate read (when a value was mapped), checked again after step 7; and the
        // Secure Record owner team, asked at most once per create, so step 3 and that check read the same answer (a fault too).
        MappedSourceGate? mappingGate = null;
        Task<(Guid? TeamId, string? Refusal)>? secureOwnerTeamAnswer = null;
        Task<(Guid? TeamId, string? Refusal)> SecureOwnerTeamOnce(CancellationToken token) =>
            secureOwnerTeamAnswer ??= _shares.SecureOwnerTeamAsync(token);
        var copiedDefaults = new List<(string Column, string Source)>();

        // Task 022 fix round 3: the columns step 3 mapped, as step 3 left them. A copied default whose source is one of these
        // is a value from the mapping source, whatever step 5 later keeps of its source.
        IReadOnlySet<string> stepThreeMappedColumns = NoDefaultColumns;
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
                mapped = await MapFieldsAsync(
                        table, rule, request, mapping, mappingClient!, callerItem, mappingWarnings, earlyMappingReads,
                        SecureOwnerTeamOnce, ct)
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
            stepThreeMappedColumns = mapped.Values.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            // A mapped value with no gate read is checked as "not told" (fail closed), never skipped.
            mappingGate = mapped.Values.Count > 0 ? mapped.Gate ?? new MappedSourceGate(table, null) : null;
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
            copiedDefaults.AddRange(copied.Select(c => (c.Column, c.Source)));
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
        // mentions either column. Nothing writes either (step 8 is a no-op since task 041). ──────────────────────────────────
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
            // Step 4½ for the system principal (task 045a fix round 1, K2): there is no caller to read the catalog as, so a
            // type-only pair is never filled — it is cleared, with the same warning, and never written half.
            if (IsTypeOnlyRegardingPair(table, item))
            {
                item = WithoutRegardingPair(item);
                mappingWarnings.Add(new RecordCreationWarning(
                    RecordCreationWarning.RegardingTypeClearedCode, SecureRootInheritance.PairTypeColumn, RegardingTypeCleared(noun)));
                _logger.LogInformation(
                    "[RECORD-CREATE] step 4½: a {Table}'s regarding pair named a type only (system principal); type cleared", table);
            }

            trace.Record(RecordCreationStep.CallerChecks, RecordCreationStepState.Skipped, "system principal (FR-07)");
        }
        else
        {
            // ── Step 4½ (task 045a): a regarding pair sent with its type and no id is filled or cleared HERE, so every check
            // below (step 5's AppendTo on the pair, 5½, 6, 6½, 7) sees the pair exactly as it is written. Both items: step 5
            // checks the caller's own values (callerItem) as well as the merged row, and may rebuild the row from them. ───────
            var typeOnly = await CompleteTypeOnlyRegardingPairAsync(
                table, user, callerSystemUserId!.Value, callerItem, item, mappedValues, noun, mappingWarnings, ct).ConfigureAwait(false);
            if (typeOnly.Refusal is { } typeOnlyRefusal)
                return Decision.Refuse(typeOnlyRefusal);
            callerItem = typeOnly.CallerItem!;
            item = typeOnly.Item!;

            // Task 049 (NFR-02 Part C): a mapped row's step 5 checks the caller's own values, then the merged row (and on a
            // failure each value, then the row as written): every pass re-read the same table metadata. Within this step each
            // such read is asked once; access answers are never kept (CallerMetadataReadMemo). Scoped to step 5 only: the
            // other steps' reads, and the pre-write rules, keep the caller's own client.
            var checking = new CallerMetadataReadMemo(user);

            // The K-2 guard (a server-set FILING lookup) ran before step 3. Task 031 fix round 1 (F1): a route-authorised
            // lookup is asked Read as the caller here, and only its AppendTo is skipped below (appendToSkip).
            var routeAuthorized = await CheckRouteAuthorizedLookupsAsync(
                checking, callerSystemUserId!.Value, table, callerItem, item, request, ct).ConfigureAwait(false);
            if (routeAuthorized.Refusal is { } routeRefusal)
                return Decision.Refuse(CallerCheckRefused(routeRefusal, noun));
            var appendToSkip = routeAuthorized.SkipAppendTo;

            if (mappedValues.Count == 0)
            {
                var check = await CallerWriteAuthorization.CheckCallerMayCreateAsync(
                    checking, callerSystemUserId!.Value, table, item, appendToSkip, ct).ConfigureAwait(false);
                if (!check.Succeeded)
                    return Decision.Refuse(CallerCheckRefused(check, noun));

                trace.Record(RecordCreationStep.CallerChecks, RecordCreationStepState.Ran,
                    "Create/Append, AppendTo on every record named, no field-secured column");
            }
            else
            {
                var checkedMerge = await CheckMergedPayloadAsync(
                    table, checking, callerSystemUserId!.Value, callerItem, item, mappedValues, appendToSkip, noun,
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
        (RecordCreationRefusal? Refusal, Dictionary<string, object>? Plan)? earlyStampPlan = null;
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

            // Task 020 fix round 2 (F-2): the plan reads WritesOf, which leaves out a JSON-null body column, so a system item
            // naming the secure flag or the access record as null reached the create. Asked here, on the item, first.
            if (SecureRootServerColumnRefusal(table, item) is { } serverColumn)
                return Decision.Refuse(PlanRefused(serverColumn, noun));

            // Task 049 fix round 2 (UAC-r2 D1): a create the caller asks to be secure first passes the delegation rule
            // provision-project asks (Write and the table's Share privilege), on the record as an ordinary create would make it.
            if (request.SecureFromCreation)
            {
                var gate = await SecureFromCreationGateAsync(table, rule, item, user!, creatorPerson, ownerContextObjectId,
                    requestedBy, noun, trace.CorrelationId, ct).ConfigureAwait(false);
                if (gate.Refusal is { } gateRefusal)
                    return Decision.Refuse(gateRefusal);
                earlyStampPlan = gate.Planned;
            }

            var plan = await _rootFiling.PlanCreateAsync(table, OwnedChildWrite.WritesOf(item), creatorPerson, ct,
                    user is null ? null : CallerMayFileUnder(user, creatorPerson, item, request.ServerSetLookupColumns),
                    secureRequested: request.SecureFromCreation)
                .ConfigureAwait(false);
            if (plan.Refusal is { } planRefusal)
                return Decision.Refuse(PlanRefused(planRefusal, noun));

            if (plan is { Isolated: true, SecureOwnerTeamId: { } secureTeam })
                isolatedTeam = secureTeam;

            trace.Record(RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran,
                isolatedTeam is null ? "ordinary"
                : request.SecureFromCreation ? "into isolation (the named Secure Record Owners team), as the caller asked"
                : "into isolation (the named Secure Record Owners team)");
        }
        else
        {
            // Field-mapping task 020 fix round 1 (F-1; UAC-r2 rule, 2026-11-03): a matter is a secure root that inherits
            // nothing, so the plan above never asks it — but its secure flag is the transitions' alone (Make Secure / Remove
            // Secure), exactly as a project's: refused with the plan's own refusal, code, words and status, whatever the
            // principal and whatever the path (route, chat, Office, a system writer).
            if (SecureRootServerColumnRefusal(table, item) is { } serverColumn)
                return Decision.Refuse(PlanRefused(serverColumn, noun));

            trace.Record(RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran, "ordinary (the table inherits no secure parent)");
        }

        // ── Step 6½: the I-1 stamp the row is missing, derived BEFORE the owner (FR-41, #2483; see remarks) ──────────────
        // Its trace step is step 10's (StampAndInheritance), begun here: a refusal is recorded under it, with the same codes and
        // words as before; a derivation that succeeds is recorded once, at step 10, which binds it.
        trace.Begin(RecordCreationStep.StampAndInheritance);
        var planned = earlyStampPlan ?? await PlanStampBeforeOwnerAsync(table, item, noun, ct).ConfigureAwait(false);
        if (planned.Refusal is { } stampPlanRefusal)
            return Decision.Refuse(stampPlanRefusal);

        // 042b F1 (owner decision A'): step 7 asks over the row as it ENDS (the full plan replaces a held stamp that disagrees);
        // step 10's bind stays fill-only (only what the row is missing), and the step-12 restamp writes the rest.
        var missingStamp = CoreAncestorRestamper.MissingOf(MergedRowOf(table, item), planned.Plan);

        // ── Step 7: the owner — record-first for every create, roots and children (FR-09, task 015) ──────────────────
        trace.Begin(RecordCreationStep.Owner);
        RecordOwnerResolution? owner = null;
        if (isolatedTeam is null)
        {
            var decided = await DecideOwnerAsync(
                    table, rule, item, planned.Plan, ownerContextObjectId, callerSystemUserId, requestedBy, noun, ct)
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

        // ── Step 8: the search index — a recorded no-op (task 041, FR-40, #2502): the index follows the content type ─────
        // Kept as a step so steps 9 to 12 keep their numbers (the spec, the docs and the trace tests cite them). Neither column
        // is written and no business unit is read; a client's value was dropped above.
        var owningTeam = isolatedTeam ?? owner!.OwningTeamId!.Value;
        trace.Record(RecordCreationStep.SearchIndexDefault, RecordCreationStepState.NoOp,
            "the index follows the content type (#2502); nothing written");

        // Task 022 fix round 2 (the mapping-source rule, the invariant): values mapped from a secure source are written only into
        // a row owned by that source's secure owner. Asked here, with the owner decided and before anything is built from the
        // item, on every path that mapped a value; step 3's early answer only narrows it. Never changes the owner or the filing.
        if (mappingGate is { } sourceGate && mappedValues.Count > 0 && user is not null)
        {
            var enforced = await EnforceMappingSourceOwnerAsync(
                    table, rule, user, sourceGate, owningTeam, callerItem, item, mappedValues, defaultColumns, copiedDefaults,
                    stepThreeMappedColumns, SecureOwnerTeamOnce, mappingWarnings, noun, ct)
                .ConfigureAwait(false);
            if (enforced.Refusal is { } ownerCheckRefusal)
                return Decision.Refuse(ownerCheckRefusal);
            item = enforced.Item;
            mappedValues = enforced.Values;
        }

        // Task 022b (the Restricted-source rule): values mapped from a source whose Access Permission is Restricted, or from a
        // source table with the column whose value could not be read, are written only into a row that ends Restricted in this
        // create (step 10's own value, decided here once and handed to step 10). Otherwise every mapped value is removed, as
        // above. Never changes the owner, the filing or the caller's values.
        InCreateAccessPermission? decidedAccess = null;
        if (mappingGate is { } restrictedGate && mappedValues.Count > 0 && user is not null)
        {
            var restricted = await EnforceMappingSourceRestrictedAsync(
                    table, rule, user, restrictedGate, callerItem, item, mappedValues, defaultColumns, copiedDefaults,
                    stepThreeMappedColumns, planned.Plan, mappingWarnings, noun, ct)
                .ConfigureAwait(false);
            if (restricted.Refusal is { } restrictedRefusal)
                return Decision.Refuse(restrictedRefusal);
            item = restricted.Item;
            mappedValues = restricted.Values;
            decidedAccess = restricted.Access;
        }

        // ── Step 9: the creator person ("for" person and table defaults: the adapters', owner-review item 3) ─────────────
        var fields = owner is not null
            ? OwnedCreateFields(owner, table, item)
            : IsolatedCreateFields(table, item, isolatedTeam!.Value, creatorPerson);
        trace.Record(RecordCreationStep.CreatorAndDefaults, RecordCreationStepState.Ran,
            RecordCreatorPerson.IsStamped(table)
                ? "creator person stamped when the requester resolved; \"for\" person and table defaults stay with the adapters"
                : "the table carries no creator person; \"for\" person and table defaults stay with the adapters");

        // ── Step 10: the I-1 stamp step 6½ derived, bound into the payload, and I-16 (owner decision 2a; FR-41; see remarks) ──
        trace.Begin(RecordCreationStep.StampAndInheritance);
        var stamped = await StampBeforeCreateAsync(table, user, item, fields, missingStamp, planned.Plan, decidedAccess, noun, ct)
            .ConfigureAwait(false);
        if (stamped.Refusal is { } stampRefusal)
            return Decision.Refuse(stampRefusal);

        trace.Record(RecordCreationStep.StampAndInheritance, stamped.State, stamped.Note);

        return new Decision
        {
            Fields = fields,
            Item = item,
            OwningTeamId = owningTeam,
            IsolatedFor = isolatedTeam is null ? null : creatorPerson,
            SecureFromCreation = request.SecureFromCreation,
            GrantorOid = grantorOid,
            MakerSystemUserId = callerSystemUserId,
            MappingWarnings = referenceWarnings.Count == 0 ? mappingWarnings : [.. referenceWarnings, .. mappingWarnings],
            SearchIndexDrops = indexDrops,
        };
    }

    /// <summary>
    /// Step 6½ (FR-41, #2483; moved from step 10, owner decision 2a): the I-1 stamp plan of the merged row — every stamp
    /// column the step-12 restamp will write, from what the row is filed under
    /// (<see cref="CoreAncestorRestamper.PlanStampOfRowAsync"/>, task 016's in-memory planner, app-only, nothing written) —
    /// BEFORE the owner, so step 7 asks about the root the row ends with (042b F1, owner decision A': a held stamp that
    /// disagrees is replaced there). For every principal. A derivation that fails refuses: a data condition
    /// (<see cref="CoreAncestorStampUnderivableException.IsPermanent"/>) is <see cref="RecordCreationRefusal.StampUnderivableCode"/>
    /// (400), anything else <see cref="RecordCreationRefusal.StampUnavailableCode"/> (503) — the codes and words step 10 gave.
    /// A document is not derived before the create (#2169), nor is a table that carries no stamp: <c>Plan</c> is then <c>null</c>.
    /// </summary>
    /// <remarks>Nothing changes here: the item (what the owner, the plan and step 12 read) is the row as the earlier steps
    /// decided it. The plan travels to step 7 (the owner question, as the row ends) and its fill-only part
    /// (<see cref="CoreAncestorRestamper.MissingOf"/>) to step 10 (the bind and I-16) — never derived a second time. Its answers
    /// are read app-only and reach the caller only through step 7's owner and step 10's bind, both after step 5's AppendTo on
    /// the record the row is filed under (ADR-008 path A); the derived root is never asked as the caller.</remarks>
    private async Task<(RecordCreationRefusal? Refusal, Dictionary<string, object>? Plan)> PlanStampBeforeOwnerAsync(
        string table, DataverseWriteItemMapper.MappedItem item, string noun, CancellationToken ct)
    {
        if (InheritedAccessPermission.RecordsOwnNarrowing(table) || !CoreAncestorResolver.IsStampedChildEntity(table))
            return (null, null);

        try
        {
            return (null, await _restamper.PlanStampOfRowAsync(table, MergedRowOf(table, item), ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (CoreAncestorStampUnderivableException ex) when (ex.IsPermanent)
        {
            _logger.LogWarning(ex, "[RECORD-CREATE] the I-1 stamp of a new {Table} cannot be derived ({Kind}); nothing was created",
                table, ex.RefusalKind);
            return (StampUnderivable(noun), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[RECORD-CREATE] the I-1 stamp of a new {Table} could not be derived (fault); nothing was created",
                table);
            return (StampUnavailable(noun), null);
        }
    }

    /// <summary>The merged row in memory: every lookup it binds, and the ADR-024 pair id as written.</summary>
    private static Entity MergedRowOf(string table, DataverseWriteItemMapper.MappedItem item)
    {
        var row = new Entity(table);
        foreach (var lookup in item.Lookups)
            row[lookup.Column] = new EntityReference(lookup.RelatedTable, lookup.RecordId);
        if (OwnedChildWrite.PairOf(item).IdText is { } pairId)
            row[CoreAncestorResolver.RegardingRecordIdColumn] = pairId;
        return row;
    }

    /// <summary>
    /// 042b F1 (owner decision A', 2026-11-03, "replace, not add"): <paramref name="lookups"/> as the row ENDS once the
    /// step-12 restamp (create) or the reconciliation job (Repair) has written <paramref name="stampPlan"/>
    /// (<see cref="CoreAncestorRestamper.PlanStampOfRowAsync"/>). Each stamp column the plan names takes the planned value: a held
    /// value that disagrees is REPLACED, a planned clear removes it, a missing one is added. Every other lookup, and a held stamp
    /// that agrees (the plan does not name it), is kept unchanged. The owner question asks over this, so the owner equals the
    /// reconciler's answer for the final row in both directions.
    /// </summary>
    internal static List<KeyValuePair<string, RecordOwnershipParent?>> AsTheRowEnds(
        IEnumerable<KeyValuePair<string, RecordOwnershipParent?>> lookups, IReadOnlyDictionary<string, object>? stampPlan)
    {
        if (stampPlan is null || stampPlan.Count == 0)
            return lookups.ToList();

        var planned = new HashSet<string>(stampPlan.Keys, StringComparer.OrdinalIgnoreCase);
        var ends = lookups.Where(l => !planned.Contains(l.Key)).ToList();
        foreach (var (column, value) in stampPlan)
        {
            if (value is EntityReference root)
                ends.Add(KeyValuePair.Create(column, (RecordOwnershipParent?)new RecordOwnershipParent(root.LogicalName, root.Id)));
        }

        return ends;
    }

    /// <summary>
    /// Step 10 (owner decision 2a, task 017; FR-41): the I-1 stamp columns step 6½ derived (<paramref name="missing"/>), bound
    /// into <paramref name="fields"/>; then I-16 from every lookup the row will END with (the item's lookups with
    /// <paramref name="stampPlan"/> applied: 042b fix round 2). No second derivation. A derived stamp the
    /// table's metadata cannot bind is the non-retryable <see cref="RecordCreationRefusalKind.Undecided"/> (500, F-5); a bind
    /// Dataverse did not answer is <see cref="RecordCreationRefusal.StampUnavailableCode"/> (503). I-16 never refuses: it is
    /// display only, and a null is simply not set. A document gets no I-1 stamp before the create (#2169); task 022b: its
    /// Access Permission is set IN the create from its filing parents, by the document decision, with its record
    /// (<see cref="InCreateAccessPermissionAsync"/>). <paramref name="decidedAccess"/> is that answer when the Restricted-source
    /// check already decided it on this same item.
    /// </summary>
    /// <remarks>Only <paramref name="fields"/> changes: the item (what the owner, the plan and step 12 read) is the row as the
    /// earlier steps decided it. The stamp is bound under the table's navigation property, read as the caller through
    /// <see cref="DataverseWriteItemMapper"/> (the read steps 1 and 4 make); a system principal has no caller, so its derived
    /// stamp is left to the post-create re-derivation (step 12), which runs for every principal.</remarks>
    private async Task<(RecordCreationRefusal? Refusal, RecordCreationStepState State, string? Note)> StampBeforeCreateAsync(
        string table, IDataverseUserClient? user, DataverseWriteItemMapper.MappedItem item, Dictionary<string, object?> fields,
        IReadOnlyDictionary<string, object>? missing, IReadOnlyDictionary<string, object>? stampPlan,
        InCreateAccessPermission? decidedAccess, string noun, CancellationToken ct)
    {
        // A document is stamped after the create only (#2169); its Access Permission is decided IN the create (task 022b).
        var document = InheritedAccessPermission.RecordsOwnNarrowing(table);
        var stampsI1 = !document && CoreAncestorResolver.IsStampedChildEntity(table);
        var takesI16 = InheritedAccessPermission.AppliesTo(table);
        if (!stampsI1 && !takesI16)
            return (null, RecordCreationStepState.Skipped, "the table carries no I-1 stamp and no inherited Access Permission");

        var notes = new List<string>();
        if (document)
            notes.Add("a document is stamped after the create only (#2169)");

        if (stampsI1)
        {
            if (missing is null)
            {
                notes.Add("no I-1 stamp missing");
            }
            else
            {
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
            // Task 022b: decided once — by the Restricted-source check before the create when it asked (on this same item), or
            // here. Never throws (the resolver's contract): a value that cannot be decided is simply not set, and step 12
            // refreshes it after the create.
            var access = decidedAccess ?? await InCreateAccessPermissionAsync(table, item, stampPlan, ct).ConfigureAwait(false);
            BindInCreateAccessPermission(fields, access);
            notes.Add(access.Note);
        }

        return (null, RecordCreationStepState.Ran, string.Join("; ", notes));
    }

    /// <summary>
    /// Task 022b: the Access Permission a create writes (step 10), and whether the row ends Restricted in this create.
    /// </summary>
    /// <param name="Write">The value step 10 writes, or <see langword="null"/>: the row keeps what its payload carries.</param>
    /// <param name="Record">A document's own-narrowing record (<see cref="AccessInheritance.Column"/>), written with the value.</param>
    /// <param name="EndsRestricted">The row is Restricted once created. Never true when it could not be told.</param>
    /// <param name="Undetermined">What the parents give, or the row's own value, could not be told.</param>
    /// <param name="Note">Step 10's trace note.</param>
    internal sealed record InCreateAccessPermission(int? Write, string? Record, bool EndsRestricted, bool Undetermined, string Note);

    /// <summary>
    /// Task 022b (the target answer): the Access Permission a NEW row of <paramref name="table"/> holds once created, from the
    /// row as it ENDS (<paramref name="stampPlan"/> applied, 042b fix round 2: a caller-sent stamp that disagrees with the
    /// intermediate's root does not decide it):
    /// <list type="bullet">
    /// <item>To Do, Event, Communication: what the parents give (I-16) when every parent was read; a parentless row keeps its
    /// own value (round 81); parents that cannot be told decide nothing.</item>
    /// <item>Document: max(own, inherited), by the document decision (<see cref="InheritedAccessPermission.DecideDocument"/>
    /// through <see cref="CoreAncestorRestamper.DecideNewDocumentAccessPermissionAsync"/>), its record written with it; the
    /// value its payload carries is its own.</item>
    /// <item>Any other table with the column (a secure root): the value its payload carries.</item>
    /// <item>A table without the column: never Restricted.</item>
    /// </list>
    /// Fail closed: an answer that cannot be told is never Restricted. Never throws (cancellation aside).
    /// </summary>
    private async Task<InCreateAccessPermission> InCreateAccessPermissionAsync(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlyDictionary<string, object>? stampPlan, CancellationToken ct)
    {
        if (!InheritedAccessPermission.CarriesColumn(table))
            return new InCreateAccessPermission(null, null, false, false, "the table carries no Access Permission");

        var (ownReadable, own) = OwnAccessPermissionOf(item);
        var ownRestricted = ownReadable && own == InheritedAccessPermission.Restricted;
        if (!InheritedAccessPermission.AppliesTo(table))
            return new InCreateAccessPermission(null, null, ownRestricted, !ownReadable, "the row's own Access Permission");

        var filed = FiledAsTheRowEnds(table, item, stampPlan);
        if (InheritedAccessPermission.RecordsOwnNarrowing(table))
        {
            if (!ownReadable)
                return new InCreateAccessPermission(null, null, false, true, "the document's own Access Permission cannot be read");

            var decided = await _restamper.DecideNewDocumentAccessPermissionAsync(table, filed, own, ct).ConfigureAwait(false);
            return decided.Target is { } target
                ? new InCreateAccessPermission(decided.Write, decided.Record, target == InheritedAccessPermission.Restricted, false,
                    "the document's Access Permission decided in the create")
                : new InCreateAccessPermission(null, null, false, true, "the document's Access Permission not decided before the create");
        }

        var answer = await _restamper.ResolveInheritedAccessPermissionAsync(table, filed, ct).ConfigureAwait(false);
        return answer switch
        {
            { Status: ParentTopsStatus.Found, Value: { } level } =>
                new InCreateAccessPermission(level, null, level == InheritedAccessPermission.Restricted, false, "I-16 set"),
            { Status: ParentTopsStatus.NoParent } =>
                new InCreateAccessPermission(null, null, ownRestricted, !ownReadable, "no I-16 before the create"),
            _ => new InCreateAccessPermission(null, null, false, true, "no I-16 before the create"),
        };
    }

    /// <summary>Task 022b: every lookup of the row as it ENDS (042b fix round 2): the item's, with the stamp plan's
    /// replacements and clears applied.</summary>
    private static Dictionary<string, object> FiledAsTheRowEnds(
        string table, DataverseWriteItemMapper.MappedItem item, IReadOnlyDictionary<string, object>? stampPlan)
    {
        var row = MergedRowOf(table, item);
        if (stampPlan is not null)
        {
            foreach (var (column, value) in stampPlan)
            {
                if (value is EntityReference root)
                    row[column] = root;
                else
                    row.Attributes.Remove(column);
            }
        }

        var filed = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in row.Attributes)
        {
            if (attribute.Value is EntityReference reference)
                filed[attribute.Key] = reference;
        }

        return filed;
    }

    /// <summary>
    /// Task 022b: the row's own <see cref="InheritedAccessPermission.Column"/> as its payload carries it (the caller's value, or
    /// a mapped one): <c>(true, null)</c> when it names none, <c>(false, null)</c> when it names one that is not a whole number.
    /// Pure.
    /// </summary>
    internal static (bool Readable, int? Value) OwnAccessPermissionOf(DataverseWriteItemMapper.MappedItem item)
    {
        using var body = JsonDocument.Parse(item.JsonBody);
        if (body.RootElement.ValueKind != JsonValueKind.Object)
            return (true, null);

        foreach (var property in body.RootElement.EnumerateObject())
        {
            if (!string.Equals(property.Name.Trim(), InheritedAccessPermission.Column, StringComparison.OrdinalIgnoreCase))
                continue;
            return property.Value.ValueKind switch
            {
                JsonValueKind.Null => (true, null),
                JsonValueKind.Number when property.Value.TryGetInt32(out var level) => (true, level),
                _ => (false, null),
            };
        }

        return (true, null);
    }

    /// <summary>
    /// Task 022b: step 10's Access Permission into the create body — the ONE place the create writes the column. A
    /// document's value comes only from the document decision (<see cref="InheritedAccessPermission.DecideDocument"/>), with
    /// its record in the same create (task 189's rule); the other three tables take what their parents give.
    /// </summary>
    private static void BindInCreateAccessPermission(Dictionary<string, object?> fields, InCreateAccessPermission access)
    {
        if (access.Write is { } level)
        {
            RemoveOtherSpellings(fields, InheritedAccessPermission.Column);
            fields[InheritedAccessPermission.Column] = level;
        }

        if (access.Record is { } record)
        {
            RemoveOtherSpellings(fields, AccessInheritance.Column);
            fields[AccessInheritance.Column] = record;
        }
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
    /// <remarks>FR-41 (#2483): the parents are the row's lookups as the row ENDS (<see cref="AsTheRowEnds"/> over step 6½'s
    /// <paramref name="stampPlan"/>), under their real columns, so the create asks the question the secure-child reconciler
    /// asks of the final row (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>): a team-owned intermediate of a Secure
    /// root no longer hides the root, and a caller-sent stamp that disagrees with the intermediate's root (which the step-12
    /// restamp overwrites) no longer decides the owner (042b F1, owner decision A'). A sent stamp that agrees is unchanged. A
    /// message created in a record thread also takes the thread's filing under <c>sprk_communicationthread</c>
    /// (<see cref="RecordThreadFiling"/>, UAC-r2 Q3), as Repair and the reconciler do.</remarks>
    private async Task<(RecordOwnerResolution? Owner, RecordCreationRefusal? Refusal)> DecideOwnerAsync(
        string table, RecordTableRule rule, DataverseWriteItemMapper.MappedItem item,
        IReadOnlyDictionary<string, object>? stampPlan, Guid? callerObjectId, Guid? callerSystemUserId,
        RecordRequester? requestedBy, string noun, CancellationToken ct)
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
            // UAC-r2 Q3 (task 042b): a message created in a record thread takes the thread's filing, as Repair (RepairOwnerAsync)
            // and the reconciler ask it (RecordThreadFiling): a Direct thread adds nothing; one that cannot be read faults (503).
            var threadParents = await RecordThreadFiling.ParentsAsync(_entities, table,
                    item.Lookups.FirstOrDefault(l => string.Equals(l.Column, RecordThreadFiling.ThreadColumn, StringComparison.OrdinalIgnoreCase))?.RecordId,
                    ct)
                .ConfigureAwait(false);
            var filed = AsTheRowEnds(OwnedChildWrite.LookupsOf(item), stampPlan)
                .Concat(RecordThreadFiling.FilingLookupsOf(threadParents))
                .ToList();
            var resolved = await ResolveOwnerOverAsync(
                    table, filed, pair.InvalidId, pair.IdText, pair.TypeId, callerObjectId, callerSystemUserId, requestedBy, ct)
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
                refused.Reason!)
            { OwnerDecision = refused });
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
                refused.Reason!)
            { OwnerDecision = refused });
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
    /// What step 7 decided for a repair: the owner refusal, and — review
    /// F1-b — why the row's mapping must not run (its filing resolves to the Secure team that does not own it yet, or its
    /// filing's owner could not be decided), or <c>null</c>.
    /// </summary>
    private sealed record OwnerRepair(RecordCreationRefusal? Refusal, string? MappingBlocked = null);

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
    /// <para><b>FR-41 (#2483, task 042b):</b> the owner question asks over the row's lookups as it ends (<see cref="AsTheRowEnds"/>
    /// over the stamp plan made once before this step, <paramref name="stamp"/>): a missing stamp is added, and a held stamp that
    /// disagrees with its source's root is replaced (042b F1, owner decision A'); the stamp step adds the fill-only part of the
    /// same plan. The mapping's source set (<paramref name="resolvedOver"/>) is the row's own parents, unchanged. A planning fault keeps the
    /// owner (<see cref="RecordRepairReason.StampUnavailable"/>, logged with the row id) and blocks the mapping: the row is
    /// never re-owned over its lookups alone.</para>
    /// </remarks>
    private async Task<OwnerRepair> RepairOwnerAsync(
        string table, RecordTableRule rule, Entity row, Dictionary<string, object> patch, List<RecordRepairField> report,
        List<RecordOwnershipParent> resolvedOver, (IReadOnlyDictionary<string, object>? Plan, bool Faulted) stamp,
        CancellationToken ct)
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
            // FR-41 / 042b F1 (owner decision A'): the row's lookups as it ENDS — a missing stamp is added under its real
            // column, and a held stamp that disagrees with its source's root (the reconciliation job overwrites it) is replaced.
            var keyed = AsTheRowEnds(RecordOwnershipContext.LookupsOf(row), stamp.Plan)
                .Concat(RecordThreadFiling.FilingLookupsOf(threadParents)).ToList();

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
                return OwnerKept(kept, null, report); // nothing to re-own, and no filing to gate the mapping on

            // FR-41 (UAC-r2 condition (c)): the stamp could not be planned, so its root is unknown — the owner is kept, never
            // decided over the lookups alone, and the mapping is blocked (the filing's owner is undecided). Counted and logged.
            if (stamp.Faulted)
            {
                _logger.LogWarning(
                    "[RECORD-REPAIR] the owner of {Table} {Id} was not changed ({Reason}): the I-1 stamp it is missing could not be " +
                    "planned, so its owner question would leave out the stamp's root", table, row.Id, RecordRepairReason.StampUnavailable);
                return OwnerKept(keep ?? (RecordRepairFieldState.Skipped, RecordRepairReason.StampUnavailable),
                    RecordRepairReason.MappingFilingUndecided, report);
            }

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
                    ? OwnerKept(keptOnRefusal, RecordRepairReason.MappingFilingUndecided, report)
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
                ? OwnerKept(keptOnFault, blockedOnFault, report)
                : OwnerRefused(row, OwnerUnavailable(noun), report, blockedOnFault);
        }

        // F1-b: a filing that resolves to the Secure team which does not own the row yet — or to no owner at all.
        var mappingBlocked = !owner.IsOwned
            ? (filed ? RecordRepairReason.MappingFilingUndecided : null)
            : owner.IsSecureOwner && !(current is { LogicalName: "team" } && current.Id == owner.OwningTeamId)
                ? RecordRepairReason.MappingSourceSecure
                : null;

        if (keep is { } keptAfterResolution)
            return OwnerKept(keptAfterResolution, mappingBlocked, report);

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
            return new OwnerRepair(null, mappingBlocked);
        }

        patch[OwnerColumn] = new EntityReference("team", team);
        report.Add(RecordRepairField.Repaired(RecordRepairPart.Owner, OwnerColumn));
        return new OwnerRepair(null, mappingBlocked);
    }

    /// <summary>The owner is kept for <paramref name="kept"/>'s reason; the row stays in its own business unit.</summary>
    private static OwnerRepair OwnerKept(
        (RecordRepairFieldState State, string Reason) kept, string? mappingBlocked, List<RecordRepairField> report)
    {
        report.Add(new RecordRepairField(RecordRepairPart.Owner, OwnerColumn, kept.State, kept.Reason));
        return new OwnerRepair(null, mappingBlocked);
    }

    /// <summary>The owner is unchanged, so the row stays in its own business unit; the refusal is reported, typed.</summary>
    private OwnerRepair OwnerRefused(
        Entity row, RecordCreationRefusal refusal, List<RecordRepairField> report, string? mappingBlocked)
    {
        _logger.LogInformation("[RECORD-REPAIR] the owner of {Table} {Id} was not changed: {Code}", row.LogicalName, row.Id, refusal.Code);
        report.Add(RecordRepairField.Refused(RecordRepairPart.Owner, OwnerColumn, refusal.Code));
        return new OwnerRepair(refusal, mappingBlocked);
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

    /// <summary>
    /// The business unit of <paramref name="owningTeam"/>, read app-only (step 7; shared with Repair, task 016).
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
    /// Step 12 (FR-38, UAC-r2 condition 4): logs <c>client_search_index_dropped</c> for each value dropped before step 5 that
    /// differs from what was applied, ids only (<see cref="RecordTableRules.LogSearchIndexDropped"/>). Since task 041 (FR-40)
    /// nothing is applied (step 8 writes neither column), so every dropped value that names an index is logged. The record id
    /// is known only now. Never throws: a log must not fail a create that has already landed.
    /// </summary>
    private void LogDroppedSearchIndex(string table, Guid id, Decision decision, string correlationId)
    {
        if (decision.SearchIndexDrops.Count == 0)
            return;

        try
        {
            RecordTableRules.LogSearchIndexDropped(
                _logger, table, id, decision.SearchIndexDrops, applied: (null, null), correlationId);
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

    /// <summary>What step 4½ leaves for step 5: both items (unchanged, filled or cleared), or the caller's own failure.</summary>
    private readonly record struct TypeOnlyPairStep(
        DataverseWriteItemMapper.MappedItem? CallerItem, DataverseWriteItemMapper.MappedItem? Item, RecordCreationRefusal? Refusal);

    /// <summary>The words of <see cref="RecordCreationWarning.RegardingTypeClearedCode"/>: no record id and no access state.</summary>
    internal static string RegardingTypeCleared(string noun) =>
        $"The regarding record type was not saved: the {noun} names no single record of that type, so it was created " +
        "without a regarding record.";

    /// <summary>The pair's display columns, which describe the record the pair names: cleared with the type.</summary>
    private static readonly string[] RegardingPairDisplayColumns =
        ["sprk_regardingrecordname", "sprk_regardingrecordnumber", "sprk_regardingrecordurl"];

    /// <summary>
    /// Step 4½ (task 045a; owner decision 4 of 2026-11-04; the owner's 045 rulings Q2 (a) and Q3 (a) of 2026-11-07): a regarding
    /// pair sent with its TYPE and no id (absent, null, empty or whitespace) is never written half. The caller's own catalog
    /// read (<see cref="CallerWriteAuthorization.PairTypeTableAsync"/>) says which table the type names; then:
    /// <list type="bullet">
    /// <item>exactly one record of that table named by the item's agreement lookups
    /// (<see cref="RegardingPairFillRecords"/>) → the id is FILLED with it (lower-case "D"), silently;</item>
    /// <item>none, or more than one, or a type naming no table → the type is CLEARED (with the id and the display columns
    /// the item sent), with one <see cref="RecordCreationWarning.RegardingTypeClearedCode"/> warning — but first AppendTo is
    /// asked on the type's catalog row, as step 5 would have, and a refusal is step 5's own (fix round 1, F4);</item>
    /// <item>a catalog read answered 404 → the items are left as sent: step 5 then asks AppendTo on the type's own catalog
    /// row and answers exactly as before (NFR-07). A 403 is a type naming no table (fix round 2): AppendTo asked, then
    /// cleared. Any other failed answer is the caller's upstream refusal (fix round 1, F2); a thrown fault propagates (fail
    /// closed).</item>
    /// </list>
    /// A whole pair, an empty pair and an id with no type are untouched (nothing is read). The step runs before step 5, so the
    /// caller's AppendTo is asked on the pair as it is written. The system principal has no caller to read the catalog as: its
    /// type-only pair is cleared without a read (<see cref="WithoutRegardingPair"/>, fix round 1, K2).
    /// </summary>
    private async Task<TypeOnlyPairStep> CompleteTypeOnlyRegardingPairAsync(
        string table, IDataverseUserClient user, Guid callerSystemUserId, DataverseWriteItemMapper.MappedItem callerItem,
        DataverseWriteItemMapper.MappedItem item, IReadOnlyDictionary<string, JsonElement> mappedValues, string noun,
        List<RecordCreationWarning> warnings, CancellationToken ct)
    {
        var pair = OwnedChildWrite.PairOf(item);
        if (!IsTypeOnlyRegardingPair(table, item) || pair.TypeId is not { } type
            || mappedValues.Keys.Contains(SecureRootInheritance.PairTypeColumn, StringComparer.OrdinalIgnoreCase))
        {
            return new TypeOnlyPairStep(callerItem, item, null);
        }

        var (typeRow, pairTable) = await CallerWriteAuthorization.PairTypeTableAsync(user, type, pair.TypeEntitySet, ct)
            .ConfigureAwait(false);
        if (!typeRow.IsSuccess)
        {
            _logger.LogInformation(
                "[RECORD-CREATE] step 4½: a {Table}'s regarding pair names a type only, and its record type could not be read as " +
                "the caller (status {Status})", table, typeRow.StatusCode);

            // 404: left as sent — step 5 asks AppendTo on the type's own catalog row, whose existence read gives its uniform
            // 404. 403 (fix round 2): the caller may not read which table the type names, though they may still be able to
            // append to the row — so it is a type naming no table: AppendTo asked below, then cleared, never stored type-only.
            // Any other status (a 429, a 503, no answer) is the caller's own failure, as in PairTargetAsync: refused, so a
            // type-only pair is never written past a read that did not answer.
            if (typeRow.StatusCode == 404)
                return new TypeOnlyPairStep(callerItem, item, null);
            if (typeRow.StatusCode != 403)
                return new TypeOnlyPairStep(null, null, Upstream(typeRow, noun));
            // 403: pairTable is null (a failed read names no table), so there is no candidate and the type is cleared below.
        }

        var records = pairTable is null ? [] : RegardingPairFillRecords(table, item, pairTable, mappedValues);
        Guid? fill = records.Count == 1 ? records[0] : null;

        // A clear removes the type before step 5, so step 5 would no longer ask AppendTo on its catalog row: asked here, with
        // step 5's own question and answer (NFR-07: a caller who may not append to the type, or whose type row is missing,
        // still gets the uniform 404, never a create with a warning).
        if (fill is null)
        {
            var typeLookups = item.Lookups
                .Where(l => string.Equals(l.Column, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase));
            var appendTo = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(user, callerSystemUserId, typeLookups, ct)
                .ConfigureAwait(false);
            if (!appendTo.Succeeded)
                return new TypeOnlyPairStep(null, null, CallerCheckRefused(appendTo, noun));
        }

        // One memo for both rewrites: the table's relationships and the related entity sets are read once (schema only).
        var memo = new CallerMetadataReadMemo(user);
        var rewritten = await RewriteRegardingPairAsync(memo, table, item, fill, noun, ct).ConfigureAwait(false);
        if (rewritten.Refusal is not null)
            return new TypeOnlyPairStep(null, null, rewritten.Refusal);
        var rewrittenCaller = ReferenceEquals(callerItem, item)
            ? rewritten
            : await RewriteRegardingPairAsync(memo, table, callerItem, fill, noun, ct).ConfigureAwait(false);
        if (rewrittenCaller.Refusal is not null)
            return new TypeOnlyPairStep(null, null, rewrittenCaller.Refusal);

        if (fill is null)
        {
            warnings.Add(new RecordCreationWarning(
                RecordCreationWarning.RegardingTypeClearedCode, SecureRootInheritance.PairTypeColumn, RegardingTypeCleared(noun)));
        }

        // The table and the outcome only: never the record the pair names (042c coordinator condition 2).
        _logger.LogInformation("[RECORD-CREATE] step 4½: a {Table}'s regarding pair named a type only; {Outcome}",
            table, fill is null ? "type cleared" : "id filled");
        return new TypeOnlyPairStep(rewrittenCaller.Item, rewritten.Item, null);
    }

    /// <summary>
    /// Step 4½'s candidates (pure): the distinct records of <paramref name="pairTable"/> named by the item's lookups in the
    /// table's agreement columns (<see cref="OwnedChildWrite.PairAgreementColumns"/>, the columns the pair's readers match its
    /// id against), the caller's own or server-set ones only. Left out:
    /// <list type="bullet">
    /// <item>a MAPPED lookup (in <paramref name="mappedValues"/>): step 5 may drop it, and the pair never names a lookup that
    /// is not written;</item>
    /// <item>an I-1 stamp column (<see cref="CoreAncestorResolver.CoreAncestorLookups"/>) whenever the item binds an
    /// intermediate source (<see cref="CoreAncestorResolver.StampSourceColumns"/>): a pair naming the stamp would make it a
    /// direct root link (<see cref="CoreAncestorResolver.ClassifyStampSource"/>), and restamps would stop following the
    /// intermediate.</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<Guid> RegardingPairFillRecords(
        string table, DataverseWriteItemMapper.MappedItem item, string pairTable, IReadOnlyDictionary<string, JsonElement> mappedValues)
    {
        var agreement = OwnedChildWrite.PairAgreementColumns(table);
        var bindsAnIntermediate = CoreAncestorResolver.StampSourceColumns.TryGetValue(table, out var sources)
            && item.Lookups.Any(l => sources.Any(s => string.Equals(s.Column, l.Column, StringComparison.OrdinalIgnoreCase)));
        return item.Lookups
            .Where(l => agreement.ContainsKey(l.Column)
                        && string.Equals(l.RelatedTable, pairTable, StringComparison.OrdinalIgnoreCase)
                        && !mappedValues.Keys.Contains(l.Column, StringComparer.OrdinalIgnoreCase)
                        && !(bindsAnIntermediate && CoreAncestorResolver.CoreAncestorLookups.Any(
                            s => string.Equals(s.LookupAttribute, l.Column, StringComparison.OrdinalIgnoreCase))))
            .Select(l => l.RecordId)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Step 4½'s rewrite of one item, through the mapper as the caller (<see cref="MergedItem"/> then
    /// <see cref="DataverseWriteItemMapper.MapAsync"/>), so every bind keeps the table's own navigation property:
    /// <paramref name="fill"/> given → the pair's id is that record's; none → the type, the id and the display columns are left
    /// out. A mapping failure is the caller's own (the existing upstream refusal); nothing is written.
    /// </summary>
    private static async Task<(DataverseWriteItemMapper.MappedItem? Item, RecordCreationRefusal? Refusal)> RewriteRegardingPairAsync(
        IDataverseUserClient memo, string table, DataverseWriteItemMapper.MappedItem item, Guid? fill, string noun,
        CancellationToken ct)
    {
        string[] leftOut = fill is null
            ? [SecureRootInheritance.PairIdColumn, SecureRootInheritance.PairTypeColumn, .. RegardingPairDisplayColumns]
            : [SecureRootInheritance.PairIdColumn];
        KeyValuePair<string, JsonElement>[] filled = fill is { } id
            ? [KeyValuePair.Create(SecureRootInheritance.PairIdColumn, JsonSerializer.SerializeToElement(id.ToString("D")))]
            : [];
        var logical = MergedItem(item, leftOut, filled);
        if (logical.EnumerateObject().Any() is false)
            return (new DataverseWriteItemMapper.MappedItem("{}", Array.Empty<string>()), null);

        var mapped = await DataverseWriteItemMapper.MapAsync(memo, table, logical, ct).ConfigureAwait(false);
        return mapped switch
        {
            { Item: { } rewritten } => (rewritten, null),
            { ClientFailure: { } failure } => (null, Upstream(failure, noun)),
            _ => (null, new RecordCreationRefusal(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode,
                mapped.ValidationError ?? $"The {noun} could not be mapped.")),
        };
    }

    /// <summary>
    /// Whether <paramref name="item"/>'s regarding pair names a type and no id (absent, null, empty or whitespace). Only a table
    /// that holds the whole pair (<see cref="OwnedChildWrite.RegardingPairTables"/>, as the update planner) has a pair to keep
    /// whole: a table whose regarding type stands alone, with no id column (a matter), keeps its type and number as sent.
    /// </summary>
    private static bool IsTypeOnlyRegardingPair(string table, DataverseWriteItemMapper.MappedItem item)
    {
        if (!OwnedChildWrite.RegardingPairTables.Contains(table.Trim()))
            return false;
        var pair = OwnedChildWrite.PairOf(item);
        return pair.TypeId is not null && !pair.InvalidId && string.IsNullOrWhiteSpace(pair.IdText);
    }

    /// <summary>The pair's columns, in a body key's form (a column or <c>{nav}@odata.bind</c>), any case.</summary>
    private static bool IsRegardingPairKey(string key)
    {
        var name = key.EndsWith("@odata.bind", StringComparison.OrdinalIgnoreCase) ? key[..^"@odata.bind".Length] : key;
        return string.Equals(name, SecureRootInheritance.PairTypeColumn, StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, SecureRootInheritance.PairIdColumn, StringComparison.OrdinalIgnoreCase)
               || RegardingPairDisplayColumns.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The system principal's clear (no metadata read): <paramref name="item"/> without the pair's type, id and display
    /// columns — its body keys, columns, lookups and clears, as <see cref="RecordTableRules.WithoutSearchIndex(DataverseWriteItemMapper.MappedItem, string, ICollection{RecordTableRules.DroppedSearchIndexValue})"/> removes a column.
    /// </summary>
    private static DataverseWriteItemMapper.MappedItem WithoutRegardingPair(DataverseWriteItemMapper.MappedItem item)
    {
        using var body = JsonDocument.Parse(item.JsonBody);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in body.RootElement.EnumerateObject().Where(p => !IsRegardingPairKey(p.Name)))
                property.WriteTo(writer);
            writer.WriteEndObject();
        }

        return item with
        {
            JsonBody = System.Text.Encoding.UTF8.GetString(stream.ToArray()),
            Columns = item.Columns.Where(c => !IsRegardingPairKey(c)).ToList(),
            Lookups = item.Lookups.Where(l => !IsRegardingPairKey(l.Column)).ToList(),
            ClearedColumns = item.ClearedColumns.Where(c => !IsRegardingPairKey(c)).ToList(),
        };
    }

    /// <summary>Step 5's answer in this service's contract, in the child-record route's order.</summary>
    private static RecordCreationRefusal CallerCheckRefused(OwnedChildWrite.Outcome check, string noun) => check switch
    {
        { ClientFailure: { } failure } => Upstream(failure, noun),
        { Invalid: { } invalid } => new(RecordCreationRefusalKind.Invalid, RecordCreationRefusal.InvalidPayloadCode, invalid),
        { ParentUnavailable: true } => new(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode,
            check.Denied ?? NotAvailableToFileUnder)
        { RefusedColumn = check.RefusedColumn },
        { Denied: { } denied } => new(RecordCreationRefusalKind.Forbidden, RecordCreationRefusal.DeniedCode, denied),
        _ => new(RecordCreationRefusalKind.Undecided, RecordCreationRefusal.NoDecisionCode, $"The {noun} was not created."),
    };

    /// <summary>
    /// Task 020 fix rounds 1 and 2 (F-1, F-2): the ONE check, for every secure root (matter, project, work assignment), that a
    /// create names none of the columns only the transitions and the cascade write — <c>sprk_issecure</c> on all three, and
    /// <c>sprk_accessinheritance</c> on the two that carry it (project, work assignment). "Names" is every key the create could
    /// write: <see cref="OwnedChildWrite.WritesOf"/>, the item's own columns, and every property of its JSON body (round 3,
    /// review #8: <c>WritesOf</c> leaves out a JSON-null body column, and a hand-built item's columns may too). Answers with the
    /// secure-root plan's own refusals (code and words, <see cref="SecureRootInheritance.PlanCreateAsync"/>;
    /// <see cref="PlanRefused"/> makes it 403), in the plan's order. <c>null</c>: neither is named, or the table is no secure
    /// root. Pure.
    /// </summary>
    internal static RecordOwnerResolution? SecureRootServerColumnRefusal(string table, DataverseWriteItemMapper.MappedItem item)
    {
        var inherits = SecureRootInheritance.Inherits(table);
        if (!inherits && !SecureRootInheritance.IsParent(table))
            return null;

        IEnumerable<string> bodyKeys;
        using (var body = JsonDocument.Parse(item.JsonBody))
            bodyKeys = body.RootElement.ValueKind == JsonValueKind.Object
                ? body.RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : [];

        var named = OwnedChildWrite.WritesOf(item).Select(w => w.Key).Concat(item.Columns).Concat(bodyKeys)
            .Select(SecureRootInheritance.NormalizeColumn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (inherits && named.Contains(AccessInheritance.Column))
        {
            // The plan's words (UAC-r2 review note 1), with its noun.
            return RecordOwnerResolution.Refused(AccessInheritance.ServerOnlyReasonCode,
                $"{AccessInheritance.Column} is written only by Spaarke, so the {NounOf(table)} was not created");
        }

        return named.Contains(AccessFollowsParent.SecureFlagColumn)
            ? RecordOwnerResolution.Refused(AccessFollowsParent.SecureFlagReasonCode, SecureRootInheritance.SecureFlagRefusalText)
            : null;
    }

    /// <summary>
    /// The secure-create plan's refusal (task 158 r1), split as <c>ChildRecordEndpoints.PlanRefused</c> splits it today: no
    /// AppendTo on a secure parent is the uniform not-found; a caller walled off it, the secure flag or the access record
    /// is 403; a check that could not be made or a named team that could not be resolved is 500; anything else (an
    /// unreadable parent flag) is the owner refusal, 409 — never "not secure".
    /// </summary>
    private static RecordCreationRefusal PlanRefused(RecordOwnerResolution plan, string noun) => plan.RefusalCode switch
    {
        DataverseUserClientErrorCodes.AccessDenied => new(RecordCreationRefusalKind.NotFoundOrDenied, RecordCreationRefusal.NotFoundCode,
            plan.Reason ?? NotAvailableToFileUnder)
        { OwnerDecision = plan },
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccess
            or AccessFollowsParent.SecureFlagReasonCode
            or AccessInheritance.ServerOnlyReasonCode => new(RecordCreationRefusalKind.Forbidden, plan.RefusalCode!,
            $"The {noun} was not created: {plan.Reason ?? "it may not be filed there"}.")
            { OwnerDecision = plan },
        Sprk.Bff.Api.Api.ExternalAccess.ProvisionProjectEndpoint.ReasonCreatorNoAccessUnverifiable
            or RecordOwnerRefusal.SecureOwnerTeamUnresolved => new(RecordCreationRefusalKind.Undecided, plan.RefusalCode!,
            $"The {noun} was not created: {plan.Reason ?? "whether it may be filed there could not be decided"}.")
            { OwnerDecision = plan },
        _ => new(RecordCreationRefusalKind.OwnerUnresolved, plan.RefusalCode ?? RecordOwnerRefusal.NoOwnerSource,
            plan.Reason ?? "no owner could be resolved")
        { OwnerDecision = plan },
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
                    RecordCreationRefusal.NotFoundCode, NotAvailableToFileUnder)
                { PreWrite = refusal },
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
    /// <para>fm task 054b: then a BFF-owned column of a communication or its thread
    /// (<see cref="InheritedAccessPermission.BffOwnedCommunicationColumns"/>), the same words, unless the server set it itself
    /// (<paramref name="serverSet"/>: the "for" person the chat create and the email draft stamp, <c>sprk_sentby</c>).</para>
    /// </summary>
    internal static RecordCreationRefusal? CallerBlockedColumnRefusal(
        RecordTableRule rule, string noun, IEnumerable<string> columns, IReadOnlySet<string>? serverSet = null)
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

        if (InheritedAccessPermission.BffOwnedCommunicationColumnIn(
                rule.Table, named.Where(c => serverSet?.Contains(SecureRootInheritance.NormalizeColumn(c).Trim()) != true))
            is { } bffColumn)
        {
            return new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                RecordCreationRefusal.BlockedColumnCode,
                $"Column '{bffColumn}' is set by the server on a record the application creates for you — omit it.");
        }

        // ebilling-r1 task 165d fix round (EB-D-207): and a column only the table's typed writer sets (RecordTableRule.WriterOnlyColumns:
        // the invoice's AP export claim): a create naming it would make an invoice that is already claimed.
        return RecordTableRules.RefuseWriterOnlyColumns(rule.Table, named) is { } writerOnly
            ? WriterOnlyColumnRefusal(noun, writerOnly)
            : null;
    }

    /// <summary>The create-side refusal of a <see cref="RecordTableRule.WriterOnlyColumns"/> column: blocked (403 / access denied), with the
    /// column refusal's own code (it extends the <c>changed_only_by_its_writer</c> marker) and its reason.</summary>
    private static RecordCreationRefusal WriterOnlyColumnRefusal(string noun, RecordOwnerResolution refused) =>
        new(RecordCreationRefusalKind.BlockedColumn, refused.RefusalCode!, $"The {noun} was not created: {refused.Reason}.");

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
    /// The refusal text of a secure-from-creation create whose row had to be removed again (fix round 3): one neutral
    /// sentence; the reason code travels in the refusal's code, not in the words.
    /// </summary>
    internal static string SecureFromCreationNotSecuredDetail(string noun) =>
        $"The {noun} could not be secured, so it was not kept. Please try again or ask an administrator.";

    /// <summary>
    /// Step 12, in the existing paths' order: the number read-back (autonumber tables), the isolated root's completion, the
    /// "Assigned *" grants of a root (<see cref="GrantAssignedAccessAsync"/>), the I-1 re-stamp and I-16 (step 10), the secure
    /// child's sharee mirror, the membership event. Never throws; returns a refusal only when an isolated root had to be
    /// removed again.
    /// </summary>
    /// <remarks>Task 049 fix round 1 (UAC-r2 ruling, item 4): for an isolated root, the create-time pass waits for secure
    /// provisioning, so eligibility is decided on the final record. It runs only once <c>CompleteIsolatedCreateAsync</c>
    /// reports the securing complete; otherwise nothing is granted at create, and the assigned-access reconciliation job
    /// grants it later.</remarks>
    private async Task<RecordCreationRefusal?> AfterCreateAsync(
        string table, Guid id, RecordTableRule rule, Decision decision, string noun, string correlationId, List<string> warnings,
        Action<SecureRootInheritResult> isolatedCompleted)
    {
        if (rule.AutonumberColumn is { } numberColumn)
            await WarnIfNumberMissingAsync(table, id, numberColumn, noun, warnings).ConfigureAwait(false);

        if (decision.IsolatedFor is { } isolatedFor)
        {
            var secured = await _rootFiling.CompleteIsolatedCreateAsync(
                table, id, isolatedFor, correlationId, decision.SecureFromCreation).ConfigureAwait(false);
            isolatedCompleted(secured);
            if (secured.RowRemoved)
            {
                _logger.LogWarning("[RECORD-CREATE] isolated {Table} {Id} removed again: its creator could not be shared ({Code})",
                    table, id, secured.ReasonCode);
                return new RecordCreationRefusal(RecordCreationRefusalKind.Undecided,
                    secured.ReasonCode ?? SecureRootInheritance.ReasonUnexpectedResult,
                    decision.SecureFromCreation
                        ? SecureFromCreationNotSecuredDetail(noun)
                        : $"The {noun} is filed under a secure record, so it is created secure and shared to you, and that share " +
                          $"could not be made, so the new {noun} was removed again. Nothing was created. " +
                          (secured.CompletesAutomatically
                              ? "Try again in a few minutes."
                              : $"Securing it was refused ({secured.ReasonCode}), and trying again will not change that: an " +
                                "administrator needs to review your access to the secure record it would be filed under."));
            }

            if (secured.RowStranded)
            {
                // A secure-from-creation project is not visited by the secure-root inheritance job unless it is filed under a
                // secure parent, so no retry is promised for it: an administrator must act.
                warnings.Add(
                    $"The {noun} was created as a secure record, but it could not be shared to you and could not be removed " +
                    $"again ({secured.ReasonCode}); " +
                    (secured.CompletesAutomatically && !decision.SecureFromCreation
                        ? "it is shared to you automatically once that step succeeds (it is retried every few minutes)."
                        : "it will not be shared to you automatically — only an administrator can open it, and an administrator " +
                          "needs to review and remove it."));
            }
            else if (!secured.IsComplete)
            {
                warnings.Add(
                    $"The {noun} was created as a secure record shared to you, but securing it could not be finished yet " +
                    $"({secured.ReasonCode}); " +
                    (decision.SecureFromCreation
                        ? $"it is not finished automatically; an administrator can finish securing the {noun}."
                        : "it is completed automatically within a few minutes."));
            }

            if (rule.Kind == RecordTableKind.Root)
            {
                if (secured.IsComplete && !secured.RowStranded)
                {
                    await GrantAssignedAccessAsync(table, id, rule, decision, correlationId).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogInformation(
                        "[ASSIGNED-ACCESS] The create-time pass of {Table} {Id} waits for secure provisioning, so eligibility is " +
                        "decided on the final record; the assigned-access reconciliation job runs it (correlationId={CorrelationId}).",
                        table, id, correlationId);
                }
            }
        }
        else if (rule.Kind == RecordTableKind.Root)
        {
            await GrantAssignedAccessAsync(table, id, rule, decision, correlationId).ConfigureAwait(false);
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

    /// <summary>
    /// The I-12 L1 "Assigned *" pass of a created root. When the root's "for" person is its maker's own linked contact, the
    /// maker's own subject is granted inline (task 049 fix round 1, UAC-r2 ruling: the maker's own Assigned-To share stays
    /// immediate), and then the full pass is queued (task 049b, UAC-r2 ruling: every other subject, external grants included,
    /// takes the queued path; the full pass finds the maker's access already in place and writes nothing for it). Otherwise
    /// the full pass is queued (<see cref="QueueAssignedAccessGrantAsync"/>). Either way, when nothing could be queued the
    /// FULL pass runs inline, once, with the create's grantor (<see cref="RunFullPassNotQueuedAsync"/>). Never throws; a
    /// fault is the reconciliation job's.
    /// </summary>
    /// <remarks>Task 049b fix round 2 (UAC-r2 ruling b): in the maker case a failed enqueue no longer leaves the other
    /// subjects to the reconciliation job; it runs the same inline full pass as the other case, so an external subject is
    /// granted and invited in the request. A send that is merely slow and then succeeds is not a failure and runs nothing
    /// inline.</remarks>
    private async Task GrantAssignedAccessAsync(string table, Guid id, RecordTableRule rule, Decision decision, string correlationId)
    {
        if (await MakersForPersonContactAsync(table, id, rule, decision).ConfigureAwait(false) is { } makersContact)
        {
            await AssignedAccessMaterializer.RunAfterWriteAsync(
                _scopes, table, id, writtenColumns: null, decision.GrantorOid, _logger, CancellationToken.None,
                onlySubject: new AssignedSubject(AssignedSubjectKind.Contact, makersContact)).ConfigureAwait(false);
        }

        var queued = await QueueAssignedAccessGrantAsync(table, id, decision.GrantorOid, correlationId).ConfigureAwait(false);
        if (!queued.Queued)
            await RunFullPassNotQueuedAsync(table, id, decision.GrantorOid, correlationId, queued.SendFailure).ConfigureAwait(false);
    }

    /// <summary>
    /// Nothing was queued, so the full "Assigned *" pass runs inline, once, as the create's grantor (the grantor the queued
    /// job would carry, so an external subject's invite has its actor). When the send itself failed
    /// (<paramref name="sendFailure"/>), one <c>[ASSIGNED-ACCESS] QueueDownInlinePass</c> line records the root and how many
    /// assigned subjects the pass evaluated, so creates made while the queue is down can be counted; it names no contact,
    /// name or email. (The BFF logs with message tags, not event ids.)
    /// </summary>
    private async Task RunFullPassNotQueuedAsync(
        string table, Guid id, string? grantorOid, string correlationId, string? sendFailure)
    {
        var outcome = await AssignedAccessMaterializer.RunAfterWriteAsync(
            _scopes, table, id, writtenColumns: null, grantorOid, _logger, CancellationToken.None).ConfigureAwait(false);

        if (sendFailure is not null)
        {
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] QueueDownInlinePass {Table} {Id}: the create-time grant could not be queued ({ExceptionType}); " +
                "the full pass ran inline for {Subjects} assigned subject(s) (correlationId={CorrelationId}).",
                table, id, sendFailure, outcome?.Entries.Count ?? 0, correlationId);
        }
    }

    /// <summary>
    /// Task 049 fix round 1 (UAC-r2 ruling): the created root's "for" person (<see cref="RecordTableRule.ForPerson"/>,
    /// Assigned To) when it is its maker's linked contact, as <see cref="IIdentityNormalizationService.ResolveAsync"/>
    /// answers it: the resolver the chat and Office adapters fill that column from (cached); otherwise <c>null</c>. Only a
    /// root that sets the column asks. Fails closed: no maker, no resolver in this host, or a lookup that faults, outlasts
    /// <see cref="AssignedAccessCreateGrantJobHandler.EnqueueTimeout"/> or answers no contact is <c>null</c>, so the pass is
    /// queued and never granted inline on a guess.
    /// </summary>
    private async Task<Guid?> MakersForPersonContactAsync(string table, Guid id, RecordTableRule rule, Decision decision)
    {
        if (rule.ForPerson is not { } forPerson
            || !string.Equals(forPerson.RelatedTable, "contact", StringComparison.OrdinalIgnoreCase)
            || decision.MakerSystemUserId is not { } maker || maker == Guid.Empty)
        {
            return null;
        }

        var assignee = decision.Item?.Lookups
            .FirstOrDefault(l => string.Equals(l.Column, forPerson.Column, StringComparison.OrdinalIgnoreCase))?.RecordId;
        if (assignee is not { } forPersonId || forPersonId == Guid.Empty)
            return null;

        try
        {
            using var scope = _scopes.CreateScope();
            if (scope.ServiceProvider.GetService<IIdentityNormalizationService>() is not { } identity)
                return null;

            using var timeout = new CancellationTokenSource(AssignedAccessCreateGrantJobHandler.EnqueueTimeout);
            var person = await identity.ResolveAsync(maker, timeout.Token).ConfigureAwait(false);
            return person.ContactId is { } contact && contact == forPersonId ? forPersonId : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "[ASSIGNED-ACCESS] The maker of {Table} {Id} could not be matched to its Assigned To ({ExceptionType}); the " +
                "create-time pass is queued.", table, id, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>
    /// Task 049 Part B (NFR-02; UAC-r2's conditions, notes/task-049-notes.md): queues the I-12 L1 "Assigned *" pass of a
    /// created project, matter or work assignment (<see cref="AssignedAccessCreateGrantJobHandler"/>), so the create returns
    /// without it. Create only: every update path keeps its inline pass. The message names the root, the grantor and the
    /// cache tenants, never the assignees. <c>Queued</c> is <c>false</c> when nothing was queued (another table, no queue in
    /// this host, a failed send, or a send that outlasts the enqueue timeout); the caller then runs the full pass inline
    /// (<see cref="RunFullPassNotQueuedAsync"/>), in the maker case too (task 049b fix round 2). <c>SendFailure</c> is the
    /// exception type when the send itself failed or timed out; the caller logs it.
    /// </summary>
    private async Task<(bool Queued, string? SendFailure)> QueueAssignedAccessGrantAsync(
        string table, Guid id, string? grantorOid, string correlationId)
    {
        if (!AssignedAccessMaterializer.TryParseRootLogicalName(table, out var rootType))
            return (false, null);

        try
        {
            using var scope = _scopes.CreateScope();
            if (scope.ServiceProvider.GetService<Sprk.Bff.Api.Services.Jobs.JobSubmissionService>() is not { } jobs)
                return (false, null);

            var tenant = scope.ServiceProvider.GetService<IConfiguration>() is { } configuration
                ? ImpersonatedRootSetSource.DeploymentCacheTenant(configuration)
                : null;
            var job = AssignedAccessCreateGrantJobHandler.NewJob(
                new AssignedAccessCreateGrantPayload(rootType, id, grantorOid, tenant is null ? [] : [tenant]), correlationId);

            using var timeout = new CancellationTokenSource(AssignedAccessCreateGrantJobHandler.EnqueueTimeout);
            await jobs.SubmitJobAsync(job, timeout.Token).ConfigureAwait(false);
            _logger.LogInformation(
                "[ASSIGNED-ACCESS] Queued the create-time grant of {Table} {Id} (job {JobId}, correlationId={CorrelationId}).",
                table, id, job.JobId, correlationId);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.GetType().Name);
        }
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

    /// <summary>
    /// The step record, with one log line per step (NFR-04). Task 049c (NFR-02): each line also carries the step's time, the
    /// milliseconds since the previous step was recorded (the first: since the pipeline began), and the total so far — the
    /// split task 021 read from the gaps between these lines, now on the line itself.
    /// </summary>
    private sealed class StepTrace(string table, string correlationId, ILogger logger)
    {
        private readonly List<RecordCreationStepRecord> _steps = [];
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        private TimeSpan _lastRecorded = TimeSpan.Zero;

        /// <summary>The request's correlation id (for the logs of a step's own helpers).</summary>
        public string CorrelationId => correlationId;

        /// <summary>The step running now (for the fault log).</summary>
        public RecordCreationStep? Current { get; private set; }

        public void Begin(RecordCreationStep step) => Current = step;

        public void Record(RecordCreationStep step, RecordCreationStepState state, string? note) =>
            Add(new RecordCreationStepRecord(step, state, note));

        public void Add(RecordCreationStepRecord record)
        {
            Current = record.Step;
            _steps.Add(record);
            var now = _clock.Elapsed;
            var stepMs = (long)(now - _lastRecorded).TotalMilliseconds;
            _lastRecorded = now;
            logger.Log(record.State is RecordCreationStepState.Refused or RecordCreationStepState.Failed ? LogLevel.Warning : LogLevel.Debug,
                "[RECORD-CREATE] {Table} step {Step}: {State} {Note} (correlationId={CorrelationId}, stepMs={StepMs}, totalMs={TotalMs})",
                table, record.Step, record.State, record.Note, correlationId, stepMs, (long)now.TotalMilliseconds);
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

    private readonly IReadOnlyDictionary<string, Guid>? _routeAuthorizedLookups;

    /// <summary>
    /// Task 031 fix round 1 (F1, owner 2026-11-04; UAC-r2 2026-11-04): lookups of the CALLER's payload whose target the ROUTE
    /// has already authorised for this caller, each as (column, the record id the route decided). Step 5 asks the caller's
    /// Read on such a lookup (existence: the uniform not-found) but not its AppendTo, and only when the payload names exactly
    /// that record; any other record in the column keeps its AppendTo. Every other check is the caller's value's. Set by route
    /// code only, never bound from a request; a CLOSED list (<see cref="RecordCreationService.RouteAuthorizableLookups"/>):
    /// any other column, a filing lookup, an ownership-parent target or an empty id is an argument error. Columns are
    /// normalised as <see cref="ServerSetLookupColumns"/> are. <c>null</c> (the default): every lookup's AppendTo is asked.
    /// </summary>
    public IReadOnlyDictionary<string, Guid>? RouteAuthorizedLookups
    {
        get => _routeAuthorizedLookups;
        init => _routeAuthorizedLookups = RecordCreationService.NormaliseRouteAuthorizedLookups(value);
    }

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

    /// <summary>
    /// Task 049 fix round 2 (owner ruling 2026-11-05; UAC-r2's rulings D1 and D2): the caller asks for the record to be secure
    /// (the Create Project wizard's Secure choice, <c>?secure=true</c> on the child-record route). A project the wizard creates
    /// as secure is secure from creation: after the caller holds the rights the delegation rule asks on
    /// <c>provision-project</c> (Write and the table's Share privilege on the record as an ordinary create would make it), it
    /// is created INTO isolation like a row filed under a secure record, and completed the same way (step 12). Only a table
    /// whose create can be isolated (<see cref="SecureRootInheritance.Inherits"/>: a project or a work assignment), and only a
    /// caller's create; anything else is an argument error. <c>false</c> (the default): the create is exactly what it was.
    /// </summary>
    public bool SecureFromCreation { get; init; }

    /// <summary>
    /// Task 019 review: search-index values (FR-38) the caller's adapter already removed from the payload BEFORE it mapped it
    /// (the chat create drops them first so a malformed index lookup cannot fail the mapping, review R7). They are logged at
    /// step 12 with the other drops (record id, applied index, origin <c>caller</c>, only when they differ from what was
    /// applied — nothing since task 041), never at the adapter, which does not know either. <c>null</c> (the default): the adapter dropped nothing.
    /// </summary>
    internal IReadOnlyList<RecordTableRules.DroppedSearchIndexValue>? PreDroppedSearchIndex { get; init; }
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
    /// <param name="probe">The caller-rights probe.</param>
    /// <param name="callerBearerToken">The caller's bearer token.</param>
    /// <param name="sourceTable">The explicit source table, if the caller named one.</param>
    /// <param name="sourceId">The explicit source id, if the caller named one.</param>
    /// <param name="documentShown">Task 043: the route's answer to the external document narrowing for a <c>sprk_document</c>
    /// source (<see cref="MappingSourceReader.DocumentShown"/>); none means a document source maps nothing.</param>
    internal static RecordCreationMapping ForCaller(
        CallerRecordAccessProbe probe, string? callerBearerToken, string? sourceTable = null, Guid? sourceId = null,
        Func<Guid, CancellationToken, Task<bool>>? documentShown = null) =>
        new(MappingSourceReader.ForCaller(probe, callerBearerToken, documentShown), sourceTable, sourceId);
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

    /// <summary>Task 045a (owner decision 4 of 2026-11-04; the owner's 045 ruling Q3 (a), 2026-11-07): the regarding pair was sent
    /// with its record type (<see cref="Column"/>: <c>sprk_regardingrecordtype</c>) and no record id, and no single record of that
    /// type could be told from the row's own lookups, so the type (with any display columns sent) was not written: the row
    /// holds neither half of the pair. Filling the id from the one record that agrees is silent.</summary>
    internal const string RegardingTypeClearedCode = "record_creation.regarding_type_cleared";

    /// <summary>Task 022 fix round 2 (the mapping-source rule): the source is secure, or whether it is secure could not be told,
    /// and the row is not created in that source's isolation, so nothing mapped from it is written. The row is still created.</summary>
    internal const string MappingSourceSecureCode = "record_creation.mapping_source_secure";

    /// <summary>Task 022b (the Restricted-source rule): the source's Access Permission is Restricted, or could not be read, and
    /// the row is not Restricted once created (or that could not be told), so nothing mapped from it is written. The row is
    /// still created.</summary>
    internal const string MappingSourceRestrictedCode = "record_creation.mapping_source_restricted";
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

    /// <summary>8 — the search index: a recorded no-op since task 041 (FR-40), kept so steps 9 to 12 keep their numbers.</summary>
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
    /// (the owner bind, the creator person) and leaves out a mapped value that was dropped. An
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
