using System.Net;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The ONE server create of a Dataverse record (field-mapping-server-write-path-r1 task 013, spec FR-06 / FR-07 / FR-12):
/// the twelve FR-06 steps, in order, for a CALLER principal (checked as the caller under their own token) or a SYSTEM
/// principal (a server writer that acts for nobody, or for a known person), ending in exactly ONE app-only create — or in
/// a typed <see cref="RecordCreationRefusal"/> with nothing written.
/// </summary>
/// <remarks>
/// <para><b>Ported, not redesigned.</b> Steps 1 and 5 to 7 and 11 are <see cref="OwnedChildWrite.CreateAsync"/>'s create
/// half (G5: check AS THE CALLER, create AS THE APP owned by the resolver's team), with the same order, the same owner
/// rule and the same refusal codes; the post-create steps are <c>ChildRecordEndpoints.CreateAsync</c>'s
/// (<c>CompleteRootCreateAsync</c>, the I-1 re-stamp, I-16, the sharee mirror) and the Office create's (the autonumber
/// retry and its number read-back, the membership event), in their order. Existing callers still call those cores: tasks
/// 017 and 018 re-point them. Field mapping (step 3) is task 014's (the partial <c>RecordCreationService.Mapping.cs</c>);
/// the search-index default (step 8) is a recorded no-op seam that task 015 fills; the owner decision is ONE private
/// method (<see cref="DecideOwnerAsync"/>) that task 015 swaps.</para>
/// <para><b>Step 10 runs after the create.</b> Spec FR-06 places "I-1 stamp + I-16 derived into the payload" before the
/// create; every existing path (the child-record route, the chat create's G5 core) derives both AFTER the create through
/// <see cref="CoreAncestorRestamper"/>. Porting kept that order, so step 10 is recorded <see cref="RecordCreationStepState.Deferred"/>
/// in its place and runs inside step 12, in the route's position (task 013 notes, "Step 10").</para>
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
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — two create cores, <see cref="OwnedChildWrite.CreateAsync"/>
/// (children, strong authorization) and the Office <c>OfficeRecordCreationService</c> (roots, weaker authorization).
/// Extension — this merges them (spec §11 "Yes: merges the two existing cores"); both stay until 017/018 re-point their
/// callers. Cost of doing nothing — the same row filed under the same parent gets a different owner, stamp and checks by
/// surface, and field mapping reaches 2 of about 30 create paths (spec §11).</para>
/// </remarks>
internal sealed partial class RecordCreationService
{
    /// <summary>Creates attempted per record on an autonumber table before the number is reported unavailable.</summary>
    internal const int MaxNumberAttempts = 3;

    /// <summary>
    /// ebilling-r1 correction (b), verbatim: what the chat create shows when a pre-write rule finds a record the row names
    /// missing or not the caller's to use (<c>OwnedChildWrite.NotAvailableToFileUnder</c> on <c>work/ebilling-r1</c>).
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

    /// <summary>The user-facing noun for a table in refusal texts: the child-record route's, with the two Office roots named.</summary>
    internal static string NounOf(string? table) => (table ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "sprk_matter" => "matter",
        "sprk_project" => "project",
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

        Decision decision;
        try
        {
            decision = await DecideAsync(table, rule, request, principal, noun, trace, ct).ConfigureAwait(false);
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
        var warnings = new List<string>();
        var removed = await AfterCreateAsync(table, id, rule, decision, noun, correlationId, warnings).ConfigureAwait(false);
        if (removed is not null)
        {
            trace.Record(RecordCreationStep.PostCreate, RecordCreationStepState.Refused, "the isolated row was removed again");
            return trace.Outcome(removed, warnings);
        }

        trace.Record(RecordCreationStep.PostCreate, RecordCreationStepState.Ran, warnings.Count == 0 ? null : $"{warnings.Count} warning(s)");
        _logger.LogInformation(
            "[RECORD-CREATE] created {Table} {Id} (owner team {Team}, isolated={Isolated}, warnings={Warnings}, mapping " +
            "warnings={MappingWarnings}, correlationId={CorrelationId})",
            table, id, decision.OwningTeamId, decision.IsolatedFor is not null, warnings.Count, decision.MappingWarnings.Count,
            correlationId);
        // The mapping step's warnings first (step 3 ran before the create), then the post-create ones.
        return trace.Created(id, decision.OwningTeamId, decision.IsolatedFor is not null,
            [.. decision.MappingWarnings.Select(w => w.Detail), .. warnings], decision.MappingWarnings);
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

        public static Decision Refuse(RecordCreationRefusal refusal) => new() { Refusal = refusal };
    }

    /// <summary>Steps 1 to 10. Any exception propagates to <see cref="CreateAsync"/>, which refuses (fail closed).</summary>
    private async Task<Decision> DecideAsync(
        string table, RecordTableRule rule, RecordCreationRequest request, RecordCreationPrincipal principal, string noun,
        StepTrace trace, CancellationToken ct)
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
                            $"You could not be identified in Dataverse, so the {noun} was not created."));
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

        // ── Step 2: the table's rules — refuse a server-owned column in the CALLER's payload ────────────────────────────
        trace.Begin(RecordCreationStep.TableRules);
        DataverseWriteItemMapper.MappedItem item;
        if (user is null)
        {
            item = ((RecordCreationPayload.Mapped)request.Payload).Item;
            trace.Record(RecordCreationStep.TableRules, RecordCreationStepState.Skipped, "system principal (FR-07)");
        }
        else
        {
            if (request.Payload is RecordCreationPayload.WebApi webApi)
            {
                // The caller's payload, mapped as the caller (navigation properties and binds from the table's metadata).
                var mapped = await DataverseWriteItemMapper.MapWebApiPayloadAsync(user, table, webApi.Body, ct).ConfigureAwait(false);
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
            }

            // The table's own server-owned columns first (the child-record route's order) — its autonumber among them: the
            // platform issues the number, as the Office create's protected list says — then the ones every app-only create
            // owns, with the creator person (the G5 core's first check, the same words).
            if (item.Columns.FirstOrDefault(c => rule.TableServerOwnedColumns.Contains(c) || IsNumberColumn(rule, c)) is { } tableColumn)
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                    RecordCreationRefusal.BlockedColumnCode, $"Column '{tableColumn}' of a {noun} is set by Spaarke — omit it."));
            }

            if (item.Columns.FirstOrDefault(c => RecordTableRules.CommonServerOwnedColumns.Contains(c) || RecordCreatorPerson.NamesColumn(c))
                is { } serverColumn)
            {
                return Decision.Refuse(new RecordCreationRefusal(RecordCreationRefusalKind.BlockedColumn,
                    RecordCreationRefusal.BlockedColumnCode,
                    $"Column '{serverColumn}' is set by the server on a record the application creates for you — omit it."));
            }

            trace.Record(RecordCreationStep.TableRules, RecordCreationStepState.Ran, "no server-owned column in the caller's payload");
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

        // ── Step 4: the merged payload, mapped (the caller's or the server's item, with the mapped values merged in) ────
        trace.Record(RecordCreationStep.MapPayload, RecordCreationStepState.Ran,
            $"{item.Columns.Count} column(s) ({mappedValues.Count} mapped), {item.Lookups.Count} lookup(s)");

        // ── Step 5: the caller checks on the merged payload (CallerWriteAuthorization, ADR-008 path A) ──────────────────
        trace.Begin(RecordCreationStep.CallerChecks);
        if (user is null)
        {
            trace.Record(RecordCreationStep.CallerChecks, RecordCreationStepState.Skipped, "system principal (FR-07)");
        }
        else
        {
            // UAC-r2's review of 013 (K-2): a server-set lookup costs no AppendTo, so a FILING lookup is never one.
            if (ServerSetFilingColumn(table, request.ServerSetLookupColumns) is { } serverSetFiling)
                return Decision.Refuse(ServerSetFilingRefused(serverSetFiling, noun));

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
                    mappingWarnings, ct).ConfigureAwait(false);
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

            var plan = await _rootFiling.PlanCreateAsync(table, OwnedChildWrite.WritesOf(item), creatorPerson, ct,
                user is null ? null : CallerMayFileUnder(user, creatorPerson, item, request.ServerSetLookupColumns)).ConfigureAwait(false);
            if (plan.Refusal is { } planRefusal)
                return Decision.Refuse(PlanRefused(planRefusal, noun));

            // UAC-r2's review of 013 (K-2), task 014 fix round 1: a filing lookup the SERVER set skipped step 5's AppendTo,
            // and the plan asks it only on a secure record — so it is asked here, as the caller, whatever the record. A
            // refusal is step 5's own uniform answer (a record the caller may not file under and a missing one alike).
            if (user is not null
                && ServerSetRootFilingLookups(table, item, request.ServerSetLookupColumns) is { Length: > 0 } serverSetFiling)
            {
                var appendTo = await CallerWriteAuthorization.CheckCallerMayAppendToAsync(user, creatorPerson, serverSetFiling, ct)
                    .ConfigureAwait(false);
                if (!appendTo.Succeeded)
                    return Decision.Refuse(CallerCheckRefused(appendTo, noun));
            }

            if (plan is { Isolated: true, SecureOwnerTeamId: { } secureTeam })
                isolatedTeam = secureTeam;

            trace.Record(RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran,
                isolatedTeam is null ? "ordinary" : "into isolation (the named Secure Record Owners team)");
        }
        else
        {
            trace.Record(RecordCreationStep.SecureRootPlan, RecordCreationStepState.Ran, "ordinary (the table inherits no secure parent)");
        }

        // ── Step 7: the owner (ONE method; task 015 swaps it) ─────────────────────────────────────────────────────────
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

        // ── Step 8: the search-index default (ONE method; task 015 fills it) ──────────────────────────────────────────
        trace.Add(SearchIndexDefault(rule));

        // ── Step 9: the creator person ("for" person and table defaults: the adapters', owner-review item 3) ─────────────
        var fields = owner is not null
            ? OwnedCreateFields(owner, table, item)
            : IsolatedCreateFields(table, item, isolatedTeam!.Value, creatorPerson);
        trace.Record(RecordCreationStep.CreatorAndDefaults, RecordCreationStepState.Ran,
            RecordCreatorPerson.IsStamped(table)
                ? "creator person stamped when the requester resolved; \"for\" person and table defaults stay with the adapters"
                : "the table carries no creator person; \"for\" person and table defaults stay with the adapters");

        // ── Step 10: the I-1 stamp and I-16 — derived after the create, in the route's position (see remarks) ───────────
        trace.Record(RecordCreationStep.StampAndInheritance, RecordCreationStepState.Deferred,
            "derived after the create (step 12), as every existing path does");

        return new Decision
        {
            Fields = fields,
            Item = item,
            OwningTeamId = isolatedTeam ?? owner!.OwningTeamId,
            IsolatedFor = isolatedTeam is null ? null : creatorPerson,
            GrantorOid = grantorOid,
            MappingWarnings = mappingWarnings,
        };
    }

    /// <summary>
    /// Step 7, ported from <see cref="OwnedChildWrite.CreateAsync"/>: the resolver's owner over every parent the row is filed
    /// under (secure-if-any, record-first), with the person who asked as <see cref="RecordOwnershipContext.RequestedBy"/>;
    /// then the two secure-filing refusals of that core, with its words. A table's ownership exception (an unfiled
    /// communication keeps its creator, E1) cannot be honoured by an application create, so it refuses. A fault is
    /// <see cref="RecordCreationRefusal.OwnerUnavailableCode"/> (503), nothing written.
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
            owner = await _ownership.ResolveOwnerAsync(
                RecordOwnershipContext.ForParents(parents, callerObjectId, callerSystemUserId) with { RequestedBy = requestedBy },
                ct).ConfigureAwait(false);
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

    /// <summary>The G5 core's secure-filing refusal, answered as the owner refusal it is today (NoOwnerSource, 409).</summary>
    private static RecordCreationRefusal SecureFilingRefused(RecordOwnerResolution owner, string detail) =>
        new(RecordCreationRefusalKind.OwnerUnresolved, RecordOwnerRefusal.NoOwnerSource, detail) { OwnerDecision = owner };

    /// <summary>Step 8 — the owning business unit's search-index default (FR-10, I-5). A recorded no-op seam until task 015.</summary>
    private static RecordCreationStepRecord SearchIndexDefault(RecordTableRule rule) => new(
        RecordCreationStep.SearchIndexDefault, RecordCreationStepState.NoOp,
        rule.SearchIndexed ? "seam: task 015 fills the owning business unit's search index here" : "the table is not search-indexed");

    /// <summary>
    /// The create payload of an OWNED row: the item's body, owned by the resolver's team, with the person who asked as its
    /// creator person (task 146 c1-r1) — <see cref="OwnedChildWrite.CreateAsync"/>'s, unchanged.
    /// </summary>
    private static Dictionary<string, object?> OwnedCreateFields(
        RecordOwnerResolution owner, string table, DataverseWriteItemMapper.MappedItem item)
    {
        var fields = OwnedChildWrite.BodyFields(item.JsonBody);
        fields["ownerid@odata.bind"] = $"/teams({owner.OwningTeamId!.Value})";
        owner.StampCreatorOn(fields, table);
        return fields;
    }

    /// <summary>
    /// The create payload of a ROOT created INTO isolation (task 158 r1, owner round 31 item 2): owned by the plan's named
    /// Secure Record Owners team, flagged in the create itself, with the person it is made for as its creator person —
    /// <c>OwnedChildWrite.CreateIntoIsolationAsync</c>'s, unchanged. Provisioning's re-entry steps complete it (step 12).
    /// </summary>
    private static Dictionary<string, object?> IsolatedCreateFields(
        string table, DataverseWriteItemMapper.MappedItem item, Guid secureTeam, Guid creatorPerson)
    {
        var fields = OwnedChildWrite.BodyFields(item.JsonBody);
        fields["ownerid@odata.bind"] = $"/teams({secureTeam})";
        fields["sprk_issecure"] = true;
        if (RecordCreatorPerson.IsStamped(table))
            RecordCreatorPerson.Bind(fields, creatorPerson);
        return fields;
    }

    /// <summary>
    /// The secure-create plan's own AppendTo check on the secure parents, as the caller — ported from
    /// <see cref="OwnedChildWrite.CreateAsync"/>: a parent counts as checked only when a lookup step 5 ASKED about names the
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
            check.Denied ?? NotAvailableToFileUnder),
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
    /// (c)). A not-found is the uniform not-found with the chat create's words (correction (b)). A system principal has no
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
        string table, Guid id, RecordTableRule rule, Decision decision, string noun, string correlationId, List<string> warnings)
    {
        if (rule.AutonumberColumn is { } numberColumn)
            await WarnIfNumberMissingAsync(table, id, numberColumn, noun, warnings).ConfigureAwait(false);

        if (decision.IsolatedFor is { } isolatedFor)
        {
            var secured = await _rootFiling.CompleteIsolatedCreateAsync(table, id, isolatedFor, correlationId).ConfigureAwait(false);
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
            IReadOnlyList<RecordCreationWarning> mappingWarnings) => new()
        {
            CreatedId = id,
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
    /// <summary>Lookups the SERVER added to the row (e.g. the email draft's sender): they name the caller and cost no AppendTo.</summary>
    public IReadOnlySet<string>? ServerSetLookupColumns { get; init; }

    /// <summary>The request's trace id (the ProblemDetails <c>correlationId</c>); a fresh one when absent.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Task 014: the field mapping this create asks for (step 3), or <c>null</c> for none — step 3 then does nothing and
    /// asks nothing. A caller's create only (a system principal with one is refused as an argument error).
    /// </summary>
    public RecordCreationMapping? Mapping { get; init; }
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

    /// <summary>8 — the search-index default (task 015; a recorded no-op seam here).</summary>
    SearchIndexDefault,

    /// <summary>9 — the creator person, the "for" person and the table defaults.</summary>
    CreatorAndDefaults,

    /// <summary>10 — the I-1 stamp and I-16 (derived after the create; see the service's remarks).</summary>
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

    /// <summary>The team the create bound as owner.</summary>
    public Guid? OwningTeamId { get; init; }

    /// <summary>The row was created INTO isolation (a work assignment or project under a secure record).</summary>
    public bool Isolated { get; init; }

    /// <summary>The request's correlation id (the ProblemDetails <c>correlationId</c> of a refusal).</summary>
    public string CorrelationId { get; init; } = string.Empty;

    public bool Succeeded => CreatedId is not null && Refusal is null;
}
