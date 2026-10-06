using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The <b>Association Engine</b> (ADR-045 / FR-09/FR-11). Resolves regarding associations for a
/// communication by evaluating <see cref="IAssociationRung"/> rungs over the channel-neutral
/// <see cref="NormalizedMessage"/> envelope — NEVER over <c>Microsoft.Graph.Message</c> — then maps the
/// aggregated matches to a <c>sprk_associationstatus</c> + auto-file decision via
/// <see cref="AssociationStatusMapper"/> (the FR-11 confidence→status ladder).
///
/// <para>
/// <b>Task 015 rework:</b> the pre-015 engine took the first rung that produced a writable match and
/// marked the record <c>Resolved</c> unconditionally. That threw away (a) the confidence→status ladder,
/// (b) cross-rung signal reinforcement, and (c) same-field conflict detection. The engine now runs ALL
/// deterministic rungs (0–3) unconditionally — so independent rungs reinforce and the always-run
/// structural-detector pass records category/obligations regardless of the association outcome — then
/// applies the ladder. AI rungs (4–5, W3) are evaluated ONLY when the deterministic pass did not
/// auto-file (cost control), and they can never auto-file (enforced in the mapper).
/// </para>
///
/// <para>
/// Non-fatal: association failure never prevents communication record creation (NFR-06). Each rung is
/// evaluated defensively — a rung that throws is treated as a non-match. Registered as a concrete type
/// in AddCommunicationModule() per ADR-010.
/// </para>
/// </summary>
public sealed class IncomingAssociationResolver
{
    private readonly IGenericEntityService _genericEntityService;
    private readonly ICommunicationDataverseService _communicationService;
    private readonly AssociationStatusMapper _statusMapper;

    /// <summary>FR-26 core-ancestor derivation for the inbound association write (task 052).</summary>
    private readonly Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver _coreAncestors;

    /// <summary>
    /// unified-access-control-r2 task 146: filing an EXISTING communication is a reparent — its owner is re-derived
    /// from its parents after the change (the named Secure team when one is secure) before the change is written.
    /// </summary>
    private readonly Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver _ownership;

    private readonly ILogger<IncomingAssociationResolver> _logger;

    /// <summary>Deterministic rungs (Kind 0–3), evaluated unconditionally, in ascending Order.</summary>
    private readonly IReadOnlyList<IAssociationRung> _deterministicRungs;

    /// <summary>AI rungs (Kind 4–5), evaluated only when the deterministic pass did not auto-file.</summary>
    private readonly IReadOnlyList<IAssociationRung> _aiRungs;

    /// <summary>Max length of the <c>sprk_associationprovenance</c> column (data-model doc).</summary>
    private const int ProvenanceMaxLength = 10000;

    private static readonly JsonSerializerOptions ProvenanceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Structured EventId for the per-rung telemetry record (DEC-8 / NFR-05) — dashboard filter.</summary>
    private static readonly EventId RungTelemetryEventId = new(4501, "AssociationRungTelemetry");

    /// <summary>Structured EventId for the per-envelope "resolving rung" summary (DEC-8 / NFR-05).</summary>
    private static readonly EventId ResolvingRungEventId = new(4502, "AssociationResolvingRung");

    public IncomingAssociationResolver(
        IEnumerable<IAssociationRung> rungs,
        ICommunicationDataverseService communicationService,
        IGenericEntityService genericEntityService,
        AssociationStatusMapper statusMapper,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver coreAncestors,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownership,
        ILogger<IncomingAssociationResolver> logger)
    {
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
        _statusMapper = statusMapper;
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _logger = logger;

        // Rungs are DI-registered (CommunicationModule). Partition into deterministic (0–3) and AI (4–5),
        // each ordered by Order. Deterministic rungs always run (reinforcement + always-run detectors);
        // AI rungs are a cost-gated escalation. Tasks 012–014 supply deterministic content; W3 (030/031)
        // registers the AI rungs — no engine change needed then.
        var ordered = rungs.OrderBy(r => r.Order).ToArray();
        _deterministicRungs = ordered.Where(r => IsDeterministic(r.Kind)).ToArray();
        _aiRungs = ordered.Where(r => !IsDeterministic(r.Kind)).ToArray();
    }

    /// <summary>
    /// Resolves associations for a communication over the normalized envelope and applies the FR-11
    /// confidence→status ladder (status + regarding writes + provenance JSON). Direction-symmetric: the
    /// same path serves inbound and outbound (the mapper records direction but does not branch on it).
    /// </summary>
    /// <param name="communicationId">The <c>sprk_communication</c> record to update.</param>
    /// <param name="message">The normalized envelope (channel-neutral).</param>
    /// <param name="context">Ambient association context (mailbox account, tenant key, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ResolveAsync(
        Guid communicationId,
        NormalizedMessage message,
        AssociationContext context,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting association resolution for communication {CommunicationId} | Direction: {Direction}",
            communicationId, message.Direction);

        // Evaluate the rungs + ladder (no write), then apply the decision (the Dataverse write). Split into
        // EvaluateAsync (task 074) so the on-demand suggestion endpoint can preview the decision WITHOUT
        // writing. The real communicationId is threaded through so per-rung + resolving-rung telemetry is
        // unchanged on this path (behavior-preserving).
        var decision = await EvaluateInternalAsync(message, context, communicationId, admitTargets: null, ct);

        // An EXISTING row being filed: a reparent (task 146) — the owner is re-derived before the write.
        await ApplyDecisionAsync(communicationId, decision, ownerAlreadyResolved: false, ct);
    }

    /// <summary>
    /// Applies a decision evaluated BEFORE the communication was created to the row just created with the owner
    /// that decision resolved (unified-access-control-r2 task 146 — the inbound and upload-capture paths evaluate
    /// first so a secure email is owned by the named Secure team from its first write, never exposed in between).
    /// Writes the same fields <see cref="ResolveAsync"/> does; skips the reparent re-derivation, because the
    /// caller created the row from these very parents.
    /// </summary>
    public Task ApplyToNewRecordAsync(Guid communicationId, AssociationDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return ApplyDecisionAsync(communicationId, decision, ownerAlreadyResolved: true, ct);
    }

    /// <summary>
    /// The ownership question for a NEW communication that <paramref name="decision"/> will file (task 146): every
    /// lookup <see cref="ApplyToNewRecordAsync"/> will write is a parent — each regarding the decision writes AND the
    /// FR-26 core-ancestor stamps derived from them (secure-if-any: an email filed to an intermediate record — an
    /// invoice, an event, a document — whose ancestor is a secure matter is the named Secure team's even while the
    /// intermediate itself is not yet re-owned). With none, the communication keeps its creator (E1).
    /// </summary>
    /// <remarks>
    /// r1 (verifier item 4): the stamps used to be derived only when the decision was APPLIED, after the owner had been
    /// resolved from the regarding writes alone, so they never reached the resolver. The derivation here is the same one
    /// <see cref="ApplyDecisionAsync"/> writes (<see cref="DeriveCoreAncestorStampsAsync"/>). A derivation failure THROWS
    /// (NFR-01, fail closed) — the caller treats it as "the parent cannot be determined" (inbound: HELD; upload capture:
    /// skipped), never as unfiled.
    /// </remarks>
    public async Task<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext> OwnershipContextForNewRecordAsync(
        AssociationDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);

        // Batch 4 integration (task 156 x task 146): the stamps are classified over the row as it will be written —
        // its regarding writes, plus the pair id when BuildDecisionFieldsAsync writes a pair (any non-Ambiguous
        // decision), after the same intermediate withholding. The pair's id is all the classification rule reads, so no
        // resolver-field read happens here. The parents stay every regarding the decision writes (secure-if-any).
        var row = new Dictionary<string, object>();
        foreach (var (fieldName, target) in decision.RegardingWrites)
        {
            row[fieldName] = target;
        }

        if (decision.Status != AssociationStatusCodes.Ambiguous && PrimaryRegarding(row) is { } primary)
            row[CoreAncestorResolver.RegardingRecordIdColumn] = primary.Reference.Id.ToString("D").ToLowerInvariant();

        WithholdIntermediateTheRowCannotPlace(row);

        var stamps = await DeriveCoreAncestorStampsAsync(Guid.Empty, row, decision, ct).ConfigureAwait(false);
        var parents = decision.RegardingWrites.Values
            .OfType<EntityReference>()
            .Concat(stamps.Values)
            .Select(r => new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipParent(r.LogicalName, r.Id));

        return Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.ForParents(parents) with
        {
            WhenUnfiled = Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.KeepCreator,
        };
    }

    /// <summary>
    /// Evaluate-only path (task 074, Path C): runs the deterministic + (cost-gated) AI rungs and applies the
    /// FR-11 confidence→status ladder, returning the <see cref="AssociationDecision"/> <b>WITHOUT</b> writing
    /// to Dataverse. Powers the on-demand suggestion endpoint — a read-only preview of what the engine would
    /// file for a stored communication. <see cref="ResolveAsync"/> = <c>EvaluateAsync</c> + <c>ApplyDecisionAsync</c>.
    /// </summary>
    /// <param name="message">The normalized envelope (channel-neutral).</param>
    /// <param name="context">Ambient association context (mailbox account, tenant key, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<AssociationDecision> EvaluateAsync(
        NormalizedMessage message,
        AssociationContext context,
        CancellationToken ct)
        // No record id on the read-only path; telemetry logs with an empty id (preview, no prior behavior to
        // preserve). ResolveAsync uses the id-carrying overload so inbound/outbound telemetry is unchanged.
        => EvaluateInternalAsync(message, context, Guid.Empty, admitTargets: null, ct);

    /// <summary>
    /// Evaluate-only path RESTRICTED to the regarding targets <paramref name="admitTargets"/> admits
    /// (unified-access-control-r2 task 161). The SAME rungs and the SAME ladder run as in
    /// <see cref="EvaluateAsync(NormalizedMessage, AssociationContext, CancellationToken)"/>; the one difference is
    /// that, after every rung has run, the distinct regarding targets the rungs proposed are handed to
    /// <paramref name="admitTargets"/> once, and the final ladder decision is taken over the matches whose target
    /// it admitted (metadata-only signals with no target are always kept).
    /// </summary>
    /// <remarks>
    /// <para><b>Why the ladder re-runs rather than the response being trimmed afterwards.</b> The suggestion
    /// previews must carry nothing about a candidate the caller may not read. Removing a hidden candidate from the
    /// projected list is not enough: <c>Status</c>, <c>AutoFileEligible</c> and each candidate's <c>Conflict</c> /
    /// <c>Written</c> flag were computed WITH it, so an "Ambiguous" status beside one visible candidate would still
    /// say a second, hidden one exists. Deciding over the admitted matches gives exactly the answer the engine
    /// would give if the hidden records did not exist.</para>
    /// <para>The engine itself makes no access decision: the admission callback is the caller's (the endpoint's
    /// candidate-access helper, which reads each target AS THE CALLER). The inbound/outbound filing path
    /// (<see cref="ResolveAsync"/>) never passes one, so system callers are unaffected.</para>
    /// </remarks>
    /// <param name="message">The normalized envelope (channel-neutral).</param>
    /// <param name="context">Ambient association context (mailbox account, tenant key, etc.).</param>
    /// <param name="admitTargets">Receives the distinct proposed targets; returns the subset to keep. A fault propagates.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<AssociationDecision> EvaluateAsync(
        NormalizedMessage message,
        AssociationContext context,
        Func<IReadOnlyList<EntityReference>, CancellationToken, Task<IReadOnlyCollection<EntityReference>>> admitTargets,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(admitTargets);
        return EvaluateInternalAsync(message, context, Guid.Empty, admitTargets, ct);
    }

    /// <summary>
    /// Shared evaluate core: deterministic pass → ladder → cost-gated AI escalation → resolving-rung telemetry.
    /// Returns the decision; never writes. <paramref name="communicationId"/> is used only for telemetry
    /// correlation (the real id on the resolve path, <see cref="Guid.Empty"/> on the read-only preview path).
    /// <paramref name="admitTargets"/> (preview path only) restricts the final ladder decision to the targets it
    /// admits — see the restricted <see cref="EvaluateAsync(NormalizedMessage, AssociationContext, Func{IReadOnlyList{EntityReference}, CancellationToken, Task{IReadOnlyCollection{EntityReference}}}, CancellationToken)"/>.
    /// </summary>
    private async Task<AssociationDecision> EvaluateInternalAsync(
        NormalizedMessage message,
        AssociationContext context,
        Guid communicationId,
        Func<IReadOnlyList<EntityReference>, CancellationToken, Task<IReadOnlyCollection<EntityReference>>>? admitTargets,
        CancellationToken ct)
    {
        // Deterministic pass: run every deterministic rung and collect all matches (writable + signals).
        var matches = new List<RungMatch>();
        await EvaluateRungsAsync(_deterministicRungs, message, context, communicationId, matches, ct);

        var decision = _statusMapper.Decide(matches, message.Direction, context.TenantKey);

        // AI pass (W3): ALWAYS run — a multi-association engine must search for the substantive targets
        // (matter / project / invoice, via semantic search + classification) even when a deterministic
        // participant/contact match already auto-filed. A contact match ("the sender is known") must not
        // short-circuit finding the matter the email is actually about. AI matches join the aggregation and
        // can raise Pending Review → Suggested / surface create-suggestions, but never auto-file
        // (mapper-enforced). Cost is bounded by the per-rung kill-switches + ADR-016 budget + ADR-014 cache.
        if (_aiRungs.Count > 0)
        {
            await EvaluateRungsAsync(_aiRungs, message, context, communicationId, matches, ct);
            decision = _statusMapper.Decide(matches, message.Direction, context.TenantKey);
        }

        // Task 161: a caller-scoped preview decides over the targets the caller may read — see the restricted
        // EvaluateAsync overload. Distinct by (logical name, id); a match with no target (a signal) is kept.
        if (admitTargets is not null)
        {
            static (string Entity, Guid Id) KeyOf(EntityReference t) => ((t.LogicalName ?? string.Empty).ToLowerInvariant(), t.Id);

            var proposed = matches
                .Where(m => m.Target is not null)
                .Select(m => m.Target!)
                .GroupBy(KeyOf)
                .Select(g => g.First())
                .ToList();

            var admitted = await admitTargets(proposed, ct).ConfigureAwait(false);
            var admittedKeys = admitted.Select(KeyOf).ToHashSet();

            var kept = matches
                .Where(m => m.Target is null || admittedKeys.Contains(KeyOf(m.Target)))
                .ToList();

            decision = _statusMapper.Decide(kept, message.Direction, context.TenantKey);
        }

        // Per-envelope "resolving rung" telemetry (DEC-8 / NFR-05): a single dashboard-able signal showing
        // which rung(s) produced the winning tier, emitted for EVERY envelope regardless of outcome.
        EmitResolvingRungTelemetry(communicationId, decision);

        return decision;
    }

    /// <summary>
    /// Evaluates a set of rungs defensively (NFR-06: a rung that throws is treated as a non-match) and
    /// appends their matches to <paramref name="sink"/>. Emits one structured per-rung telemetry record per
    /// rung attempt (DEC-8 / NFR-05).
    /// </summary>
    private async Task EvaluateRungsAsync(
        IReadOnlyList<IAssociationRung> rungs,
        NormalizedMessage message,
        AssociationContext context,
        Guid communicationId,
        List<RungMatch> sink,
        CancellationToken ct)
    {
        foreach (var rung in rungs)
        {
            var startTs = Stopwatch.GetTimestamp();
            try
            {
                var matches = await rung.EvaluateAsync(message, context, ct);
                var elapsed = Stopwatch.GetElapsedTime(startTs);
                // "fired" ⇒ the rung returned ≥1 match; "skipped" ⇒ zero (disabled kill-switch, empty
                // input, or genuine non-match — the engine treats them uniformly).
                EmitRungTelemetry(rung, communicationId,
                    outcome: matches.Count > 0 ? "fired" : "skipped",
                    matchCount: matches.Count, elapsed: elapsed, error: null);
                if (matches.Count > 0)
                    sink.AddRange(matches);
            }
            catch (Exception ex)
            {
                var elapsed = Stopwatch.GetElapsedTime(startTs);
                EmitRungTelemetry(rung, communicationId,
                    outcome: "error", matchCount: 0, elapsed: elapsed, error: ex);
                _logger.LogWarning(ex,
                    "Association rung {Rung} (order {Order}) failed for communication {CommunicationId}; skipping",
                    rung.Kind, rung.Order, communicationId);
            }
        }
    }

    /// <summary>
    /// Emits one structured per-rung telemetry record (DEC-8 / NFR-05) routed through the injected
    /// <see cref="ILogger{TCategoryName}"/> (the existing BFF sink — no new pipeline). Carries: communication
    /// id, rung kind + order, whether it is an AI rung, outcome (<c>fired</c>/<c>skipped</c>/<c>error</c>),
    /// match count, and latency. Emission is cheap (structured log fields only).
    /// </summary>
    /// <remarks>
    /// <b>Token/cost + cache hit/miss for AI rungs (4–5) is a documented follow-up.</b> The AI facades
    /// (<c>IRecordMatchingAi</c>, <c>ICommunicationClassificationAi</c>, and the underlying
    /// <c>IOpenAiClient.GetStructuredCompletionAsync</c>) do not currently surface token usage or cache
    /// hit/miss to the caller. Plumbing them would change the PublicContracts seam (blast radius) and is out
    /// of scope for this task; <c>IsAiRung</c> is emitted so a dashboard can partition deterministic vs AI
    /// cost today, and the token/cache dimension can be added once the facades return usage.
    /// </remarks>
    private void EmitRungTelemetry(
        IAssociationRung rung, Guid communicationId, string outcome, int matchCount, TimeSpan elapsed, Exception? error)
    {
        _logger.Log(
            error is null ? LogLevel.Information : LogLevel.Warning,
            RungTelemetryEventId,
            error,
            "Rung telemetry | CommunicationId: {CommunicationId}, Rung: {Rung}, RungOrder: {RungOrder}, " +
            "IsAiRung: {IsAiRung}, Outcome: {Outcome}, MatchCount: {MatchCount}, ElapsedMs: {ElapsedMs}",
            communicationId, rung.Kind, rung.Order, !IsDeterministic(rung.Kind), outcome, matchCount,
            (long)elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Emits the per-envelope "resolving rung" summary (DEC-8 / NFR-05): the resulting tier + which rung(s)
    /// contributed to the written (winning) target(s), so the deterministic-first ladder is observable on
    /// real volume. Derived from the decision provenance already computed — no re-evaluation cost.
    /// </summary>
    private void EmitResolvingRungTelemetry(Guid communicationId, AssociationDecision decision)
    {
        var resolvingRungs = decision.Provenance.Candidates
            .Where(c => c.Written)
            .SelectMany(c => c.Contributors.Select(contrib => contrib.Rung))
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        var resolvingRungsText = resolvingRungs.Length > 0 ? string.Join(",", resolvingRungs) : "(none)";
        var rungsFiredText = decision.Provenance.RungsFired.Count > 0
            ? string.Join(",", decision.Provenance.RungsFired)
            : "(none)";

        _logger.Log(
            LogLevel.Information,
            ResolvingRungEventId,
            null,
            "Association resolving-rung summary | CommunicationId: {CommunicationId}, Tier: {Tier}, " +
            "AutoFiled: {AutoFiled}, ResolvingRungs: {ResolvingRungs}, RungsFired: {RungsFired}",
            communicationId, AssociationStatusCodes.Name(decision.Status), decision.AutoFiled,
            resolvingRungsText, rungsFiredText);
    }

    // Which rungs run in the DETERMINISTIC pass (always, before the cost-gated AI escalation). Includes
    // RecordNameMatch (rung 3.5) — its match is an exact, verified name/number appearance, so it belongs in the
    // deterministic pass even though the mapper keeps it OUT of auto-file eligibility (owner: surface-for-review,
    // never auto-file). Kept in sync with AssociationStatusMapper's classification (which governs auto-file).
    private static bool IsDeterministic(RungKind kind) =>
        kind is RungKind.ExplicitReference or RungKind.ThreadContinuity
             or RungKind.ParticipantCorrelation or RungKind.StructuralDetector
             or RungKind.RecordNameMatch
             // RecipientAlias (FR-A2) — a per-record intake address, parsed + resolved with zero AI cost,
             // belongs in the always-run deterministic pass (and IS auto-file-eligible per the mapper).
             or RungKind.RecipientAlias
             // ContactNameMatch (rung 3.6) is an exact, verified full-name→contact appearance (regex + exact
             // Dataverse lookup, no AI cost), so it belongs in the deterministic pass — even though the mapper
             // keeps it OUT of auto-file eligibility (owner: surface-for-review, never auto-file).
             or RungKind.ContactNameMatch
             // Affinity (FR-A4) is deterministic frequency counting over the sprk_affinity store (one query, no
             // AI cost), so it runs in the deterministic pass to surface a learned suggestion — even though the
             // mapper keeps it OUT of auto-file AND deterministic-write eligibility (SUGGEST-ONLY, never
             // auto-file), exactly like RecordNameMatch / ContactNameMatch.
             or RungKind.Affinity;

    // ═════════════════════════════════════════════════════════════════════════════
    // Apply the ladder decision to the communication record
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Writes the regarding lookups, status, and provenance JSON. Populates the ADR-024 polymorphic
    /// resolver fields whenever a regarding lookup was written (Resolved or Suggested). Non-fatal writes.
    /// </summary>
    private async Task ApplyDecisionAsync(
        Guid communicationId,
        AssociationDecision decision,
        bool ownerAlreadyResolved,
        CancellationToken ct)
    {
        var fields = await BuildDecisionFieldsAsync(communicationId, decision, ct);

        var parentChanges = Sprk.Bff.Api.Services.Dataverse.RecordReparent.ParentChangesIn(fields);
        if (ownerAlreadyResolved || parentChanges.Count == 0)
        {
            await _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct);
        }
        else
        {
            // Task 146 — a REPARENT of an existing communication: re-derive its owner from its parents AFTER this
            // change (additive: the engine never clears a sibling regarding), BEFORE the write, and reassign it
            // (separately, read back) when it moves — into a secure record's named team, or anywhere else. A refusal
            // throws before anything is written: this class's existing fail-closed contract (the communication
            // survives, unassociated — NFR-06).
            var reparent = await _ownership.ReparentAsync(
                new Sprk.Bff.Api.Services.Dataverse.RecordReparent
                {
                    EntityLogicalName = "sprk_communication",
                    RecordId = communicationId,
                    ParentChanges = parentChanges,
                    WhenUnfiled = Sprk.Bff.Api.Services.Dataverse.UnfiledOwnership.KeepCreator,
                },
                token => _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, token),
                ct);

            if (reparent.IsRefused)
                throw new Sprk.Bff.Api.Services.Dataverse.RecordOwnerUnresolvedException("sprk_communication", reparent);
        }

        _logger.LogDebug(
            "Applied association to communication {CommunicationId} | Status: {Status}, AutoFiled: {AutoFiled}, FieldCount: {FieldCount}",
            communicationId, AssociationStatusCodes.Name(decision.Status), decision.AutoFiled, fields.Count);
    }

    /// <summary>
    /// The fields a decision writes onto a communication that is about to be CREATED with them — the regarding lookups,
    /// association status, provenance, ADR-024 resolver fields and FR-26 core-ancestor stamps, exactly what
    /// <see cref="ApplyToNewRecordAsync"/> would write afterwards (task 146 r2, verifier item 9).
    /// </summary>
    /// <remarks>
    /// A communication whose OWNER comes from its filing must be created WITH that filing. Written separately, a failed
    /// filing write (non-fatal by NFR-06) left a row owned by a secure record's memberless team and filed under nothing —
    /// a record nobody can see, which no sharee mirror can reach (owner amendment R3). Throws on any failure (a stamp
    /// derivation, a resolver-field read): the caller refuses in its own contract (inbound: HOLD; upload capture: skip).
    /// </remarks>
    public Task<Dictionary<string, object>> BuildNewRecordFieldsAsync(AssociationDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return BuildDecisionFieldsAsync(Guid.Empty, decision, ct);
    }

    /// <summary>The fields a decision writes (see <see cref="ApplyDecisionAsync"/>); <paramref name="communicationId"/> is
    /// log context only (<see cref="Guid.Empty"/> for a row not yet created).</summary>
    private async Task<Dictionary<string, object>> BuildDecisionFieldsAsync(
        Guid communicationId,
        AssociationDecision decision,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, object>();
        foreach (var (fieldName, target) in decision.RegardingWrites)
        {
            fields[fieldName] = target;
        }

        fields["sprk_associationstatus"] = new OptionSetValue(decision.Status);

        // Populate polymorphic resolver fields whenever we asserted a regarding lookup (a Suggested
        // record surfaces the proposed target too, so the review UI needs the denormalized fields).
        // P2b (061 UAT round-2, 2026-07-30): but NOT on Ambiguous. Ambiguous means the engine explicitly
        // refused to pick among conflicting matters (they are not written); crowning whatever leftover
        // non-conflicting record IS written (an incidental invoice, a fallback) as the denormalized "primary
        // Regarding" headline is exactly the misfile the UAT flagged ("why is the logic favoring invoices?").
        // The typed regarding lookups are still written for the review surface; the headline stays empty until
        // the reviewer picks the primary.
        if (decision.RegardingWrites.Count > 0 && decision.Status != AssociationStatusCodes.Ambiguous)
        {
            await PopulateResolverFieldsAsync(fields, ct);
        }

        // Task 156 (verifier round 1 item 9): with NO pair (an Ambiguous decision), the row cannot say that a root the
        // engine wrote is its DIRECT choice and an intermediate written beside it only evidence — the one classification
        // rule (CoreAncestorResolver.ClassifyStampSource, rule 5) reads the lone intermediate as what the row is filed
        // under and the root as its COPY, which the restamper and the reconciliation job would then overwrite or clear.
        // So such an intermediate is not written: the engine's explicit root stands, and the intermediate stays a review
        // candidate (provenance, Written = false). Only reachable when an operator widens CoreWritableEntities to an
        // intermediate (the shipped set writes roots only).
        var withheld = WithholdIntermediateTheRowCannotPlace(fields);
        var provenance = withheld is { } w
            ? decision.Provenance with
            {
                Candidates = decision.Provenance.Candidates
                    .Select(c => c.Field == w.Field && c.TargetId == w.TargetId ? c with { Written = false } : c)
                    .ToList(),
            }
            : decision.Provenance;
        fields["sprk_associationprovenance"] = SerializeProvenance(provenance);

        // FR-26 core-ancestor stamps (task 052) - LAST, so nothing above can overwrite them, and only for
        // CHILD-class targets: a rung that wrote a core lookup directly has already written its own stamp.
        // Every rung funnels here, which is why convergence happens at this single write rather than in the
        // ~10 rungs that merely PROPOSE a RegardingFieldName.
        //
        // ADDITIVE, deliberately. The engine never clears a sibling regarding field (task-042 semantics), so
        // this does not null a stale ancestor either: an inbound suggestion pass must not silently unfile a
        // human's manual filing. Clearing on reparent belongs to the deliberate reparent paths (tasks
        // 050/051), not to this engine.
        await ApplyCoreAncestorStampsAsync(communicationId, fields, decision, ct);

        return fields;
    }

    /// <summary>
    /// Derives and applies the FR-26 core-record ancestor stamp for the regarding target the communication is FILED
    /// UNDER — decided by the one classification rule (<see cref="CoreAncestorResolver.ClassifyStampSource"/>) over the
    /// row exactly as it will be written (task 052; task 156 verifier round 1 item 9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decision may write several targets at once (the review surface shows all candidates). The row's pair (written
    /// by <see cref="PopulateResolverFieldsAsync"/> for the highest-priority target — a root before any intermediate)
    /// says which one the communication is filed under; the stamp is that record's root, and only it:
    /// </para>
    /// <list type="bullet">
    /// <item>The pair names an intermediate (or there is no pair and exactly one intermediate) → that intermediate's root
    /// is copied, except a core lookup a rung wrote EXPLICITLY, which always wins (the rung observed evidence about this
    /// message while the stamp is only an inherited pointer).</item>
    /// <item>The pair names a ROOT → the root is the direct filing and every intermediate beside it is a CARRIER: nothing
    /// is copied from a carrier. (Before task 156's verifier round, the carrier's OTHER root types were copied onto the
    /// row — partial copies the restamper never refreshes, so they went stale as an access over-grant. The Office carrier
    /// to-do never copied from its carrier either.)</item>
    /// <item>Two intermediates and nothing saying which one → nothing is copied (never a guess); the storage resolver
    /// refuses the row as ambiguous until a reviewer files it.</item>
    /// </list>
    /// <para>
    /// <b>Fail closed (NFR-01).</b> A derivation failure throws, which this class's defensive caller treats
    /// as a failed association (NFR-06: the communication itself survives, unassociated). Writing the
    /// regarding lookups anyway would file the message against a child record while leaving it invisible to
    /// everyone whose access comes from that child's matter - a silent under-grant that looks like success.
    /// </para>
    /// </remarks>
    private async Task ApplyCoreAncestorStampsAsync(
        Guid communicationId,
        Dictionary<string, object> fields,
        AssociationDecision decision,
        CancellationToken ct)
    {
        foreach (var (lookupAttribute, stamp) in await DeriveCoreAncestorStampsAsync(communicationId, fields, decision, ct))
        {
            fields[lookupAttribute] = stamp;
        }
    }

    /// <summary>
    /// The FR-26 core-ancestor stamps the communication carries, keyed by the stamp's lookup attribute — the fields
    /// <see cref="ApplyCoreAncestorStampsAsync"/> writes, and (task 146 r1) the extra parents
    /// <see cref="OwnershipContextForNewRecordAsync"/> resolves the owner over. The stamp source is decided by the one
    /// classification rule (<see cref="CoreAncestorResolver.ClassifyStampSource"/>) over <paramref name="fields"/>, the
    /// row exactly as it will be written (task 156 verifier round 1 item 9). A core lookup a rung wrote explicitly is
    /// never overwritten by a derived stamp. Throws on a derivation failure (NFR-01, fail closed).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, EntityReference>> DeriveCoreAncestorStampsAsync(
        Guid communicationId,
        Dictionary<string, object> fields,
        AssociationDecision decision,
        CancellationToken ct)
    {
        var stamps = new Dictionary<string, EntityReference>(StringComparer.OrdinalIgnoreCase);
        if (decision.RegardingWrites.Count == 0)
            return stamps;

        var filing = CoreAncestorResolver.ClassifyStampSource(
            CommunicationEntity, RowAsWritten(fields), CoreAncestorResolver.PartyRegardingColumnNames(CommunicationEntity));
        if (filing.Kind != StampSourceKind.Source)
            return stamps; // a direct root (carriers carry no copy), nothing filed, or no single filing (never a guess)

        // Snapshot the lookups the rungs wrote explicitly - these are never overwritten below.
        var explicitLookups = new HashSet<string>(decision.RegardingWrites.Keys, StringComparer.OrdinalIgnoreCase);
        var source = filing.Source!;

        var outcome = await _coreAncestors
            .DeriveForHostAsync(CommunicationEntity, source.Intermediate, source.Id, ct)
            .ConfigureAwait(false);

        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException(
                $"Core-ancestor derivation failed for {source.Intermediate}({source.Id:D}) while "
                + $"associating communication {communicationId:D}; refusing to write an unstamped "
                + $"regarding (FR-26 / NFR-01). {outcome.Error}");
        }

        foreach (var stamp in outcome.Stamps)
        {
            if (explicitLookups.Contains(stamp.LookupAttribute))
                continue; // a rung asserted this core target directly - its evidence outranks inheritance

            stamps[stamp.LookupAttribute] = new EntityReference(stamp.EntityType, stamp.RecordId);
        }

        return stamps;
    }

    private const string CommunicationEntity = "sprk_communication";

    /// <summary>
    /// With NO pair on the row: a lone intermediate written beside an explicit root of a type that intermediate can carry
    /// is removed from <paramref name="fields"/> (see <see cref="ApplyDecisionAsync"/>), and returned so the provenance can
    /// say it was not written. <see langword="null"/> when nothing is withheld.
    /// </summary>
    private static (string Field, string TargetId)? WithholdIntermediateTheRowCannotPlace(Dictionary<string, object> fields)
    {
        if (fields.ContainsKey(CoreAncestorResolver.RegardingRecordIdColumn))
            return null; // the pair says what the row is filed under

        var filing = CoreAncestorResolver.ClassifyStampSource(
            CommunicationEntity, RowAsWritten(fields), CoreAncestorResolver.PartyRegardingColumnNames(CommunicationEntity));
        if (filing.Kind != StampSourceKind.Source)
            return null;

        var source = filing.Source!;
        var carriable = CoreAncestorResolver.CarriableRootTypes(source.Intermediate);
        var explicitRoot = CoreAncestorResolver.CoreAncestorLookups
            .Any(l => carriable.Contains(l.EntityType) && fields.ContainsKey(l.LookupAttribute));
        if (!explicitRoot)
            return null;

        fields.Remove(source.Column);
        return (source.Column, source.Id.ToString("D"));
    }

    /// <summary>The communication row exactly as <paramref name="fields"/> will write it (its lookups and its pair id).</summary>
    private static Entity RowAsWritten(Dictionary<string, object> fields)
    {
        // An in-memory projection for the classification rule — never written. Built with an explicit (empty) id: a
        // one-argument construction of a child table reads as a CREATE to RecordOwnerAssignmentCensusTests (batch 4
        // integration, task 156 x task 146), and this row is not one.
        var row = new Entity(CommunicationEntity, Guid.Empty);
        foreach (var (column, value) in fields)
        {
            if (value is EntityReference || (value is string && column == CoreAncestorResolver.RegardingRecordIdColumn))
                row[column] = value;
        }

        return row;
    }

    /// <summary>
    /// Serialize the provenance to JSON, bounded to the <c>sprk_associationprovenance</c> column length.
    /// If the (rare) full document would exceed the cap, fall back to a compact decision-only document so
    /// the record still carries the essential audit trail.
    /// </summary>
    private static string SerializeProvenance(AssociationProvenance provenance)
    {
        var json = JsonSerializer.Serialize(provenance, ProvenanceJsonOptions);
        if (json.Length <= ProvenanceMaxLength)
            return json;

        var compact = new AssociationProvenance
        {
            Version = provenance.Version,
            Direction = provenance.Direction,
            Decision = provenance.Decision,
            RungsFired = provenance.RungsFired,
            Candidates = Array.Empty<CandidateTrace>(),
            Signals = Array.Empty<SignalTrace>(),
        };
        var compactJson = JsonSerializer.Serialize(compact, ProvenanceJsonOptions);
        return compactJson.Length <= ProvenanceMaxLength
            ? compactJson
            : compactJson[..ProvenanceMaxLength];
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Polymorphic Resolver (ADR-024)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// In-memory cache for sprk_recordtype_ref lookups (entity logical name → GUID + display name).
    /// Populated lazily, lives for the lifetime of the singleton service. Concurrent because the
    /// resolver is a singleton and inbound/outbound messages resolve on parallel threads (task 018 —
    /// a plain Dictionary here was a data race). Read-then-write is not atomic, but the value is
    /// deterministic per key, so a rare double-query just writes the same entry twice — no corruption.
    /// </summary>
    private readonly ConcurrentDictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();

    /// <summary>
    /// Populates the 4 denormalized resolver fields based on the highest-priority
    /// entity-specific regarding field that was set.
    ///
    /// Fields set:
    ///   - sprk_regardingrecordtype   (Lookup → sprk_recordtype_ref)
    ///   - sprk_regardingrecordid     (Text — parent GUID)
    ///   - sprk_regardingrecordname   (Text — parent display name)
    ///   - sprk_regardingrecordnumber (Text — parent reference number, when the entity has one)
    ///   - sprk_regardingrecordurl    (URL — clickable link to parent record)
    /// </summary>
    /// <summary>
    /// Identity "fallback" regarding fields (contact / organization / account) — matching a sender/recipient
    /// is NOT what an email is <i>about</i>. Mirrors <see cref="Engine.AssociationStatusMapper"/>'s
    /// <c>FallbackFields</c>. A fallback must never become the denormalized <b>primary</b> Regarding (P2, FR-12
    /// UAT): before P1/P2, when the substantive matters went Ambiguous (not written), the primary-pick fell
    /// through priority order to a written fallback (contact) — or a spurious sub-threshold invoice — and put a
    /// misleading headline "Regarding" on the record.
    /// </summary>
    private static readonly HashSet<string> FallbackRegardingFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_regardingperson",       // contact
        "sprk_regardingorganization", // organization
        "sprk_regardingaccount",      // account
    };

    private async Task PopulateResolverFieldsAsync(
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        // Find the primary regarding entity (highest priority field that was set). Priority order is the
        // single source of truth in RegardingFieldMap.All (ADR-024) — the rungs write the same fields.
        // P2 (FR-12 UAT): consider SUBSTANTIVE fields only — a fallback identity match (contact/org/account)
        // must never be the headline Regarding. When the substantive matters conflict (Ambiguous → not
        // written), leaving the denormalized fields unset is the correct outcome: the record shows no headline
        // Regarding and the review surface resolves the candidates from provenance.
        if (PrimaryRegarding(fields) is not { } primary)
            return;

        var primaryRef = primary.Reference;
        var primaryEntityLogicalName = primary.EntityLogicalName;

        try
        {
            // Set sprk_regardingrecordid (GUID as text)
            var cleanId = primaryRef.Id.ToString("D").ToLowerInvariant();
            fields["sprk_regardingrecordid"] = cleanId;

            // Set sprk_regardingrecordname (the record's actual NAME) + sprk_regardingrecordnumber (its
            // reference NUMBER). Retrieve BOTH directly from the primary record rather than trusting
            // EntityReference.Name: a number-match rung (e.g. RecordNameMatchRung) attaches the record
            // NUMBER as the reference Name, which previously landed in sprk_regardingrecordname with the
            // number field left null — inbound emails then showed "Regarding Name: LITG-119896" with no
            // number (UAT R5 / task 132). Mirror the outbound denormalization exactly: name = name,
            // number = number.
            var nameField = GetPrimaryNameField(primaryEntityLogicalName);
            var numberField = GetReferenceNumberField(primaryEntityLogicalName);
            string? retrievedName = null;
            string? retrievedNumber = null;
            if (nameField is not null || numberField is not null)
            {
                try
                {
                    var columns = new List<string>(2);
                    if (nameField is not null) columns.Add(nameField);
                    if (numberField is not null) columns.Add(numberField);
                    var record = await _genericEntityService.RetrieveAsync(
                        primaryEntityLogicalName, primaryRef.Id, columns.ToArray(), ct);
                    if (nameField is not null)
                        retrievedName = record.GetAttributeValue<string>(nameField);
                    if (numberField is not null)
                        retrievedNumber = record.GetAttributeValue<string>(numberField);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve name/number for {Entity} {Id}",
                        primaryEntityLogicalName, primaryRef.Id);
                }
            }

            // Name: the record's actual primary name wins; fall back to the reference Name only when the
            // retrieve yielded nothing (never leave the field holding the record NUMBER).
            var recordName = !string.IsNullOrWhiteSpace(retrievedName) ? retrievedName : primaryRef.Name;
            fields["sprk_regardingrecordname"] = recordName ?? "";

            // Number: the record's reference number, when the entity has one and it is populated.
            if (!string.IsNullOrWhiteSpace(retrievedNumber))
                fields["sprk_regardingrecordnumber"] = retrievedNumber;

            // Set sprk_regardingrecordurl
            fields["sprk_regardingrecordurl"] = BuildRecordUrl(primaryEntityLogicalName, cleanId);

            // Set sprk_regardingrecordtype (Lookup to sprk_recordtype_ref)
            var recordTypeRef = await ResolveRecordTypeRefAsync(primaryEntityLogicalName, ct);
            if (recordTypeRef.HasValue)
            {
                fields["sprk_regardingrecordtype"] = new EntityReference(
                    "sprk_recordtype_ref", recordTypeRef.Value.Id)
                {
                    Name = recordTypeRef.Value.DisplayName
                };
            }

            _logger.LogDebug(
                "Populated resolver fields: Entity={Entity}, Name={Name}, RecordType={RecordType}",
                primaryEntityLogicalName, recordName ?? "(unknown)",
                recordTypeRef?.DisplayName ?? "(not found)");
        }
        catch (Exception ex)
        {
            // Non-fatal — resolver fields are for display, not critical data
            _logger.LogWarning(ex, "Failed to populate resolver fields for {Entity}", primaryEntityLogicalName);
        }
    }

    /// <summary>
    /// The row's primary regarding — the highest-priority SUBSTANTIVE regarding field that is set (the order is
    /// <see cref="RegardingFieldMap.All"/>; fallback identity fields never headline). The target the pair names;
    /// shared by <see cref="PopulateResolverFieldsAsync"/> and <see cref="OwnershipContextForNewRecordAsync"/>.
    /// </summary>
    private static (EntityReference Reference, string EntityLogicalName)? PrimaryRegarding(Dictionary<string, object> fields)
    {
        foreach (var (entityLogicalName, fieldName) in RegardingFieldMap.All)
        {
            if (FallbackRegardingFields.Contains(fieldName))
                continue;
            if (fields.TryGetValue(fieldName, out var value) && value is EntityReference entityRef)
                return (entityRef, entityLogicalName);
        }

        return null;
    }

    /// <summary>
    /// Resolve the sprk_recordtype_ref GUID for an entity logical name. Cached.
    /// </summary>
    private async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        var record = await _communicationService.QueryRecordTypeRefAsync(entityLogicalName, ct);
        if (record is not null)
        {
            var entry = (
                Id: record.Id,
                DisplayName: record.GetAttributeValue<string>("sprk_recorddisplayname") ?? entityLogicalName
            );
            _recordTypeRefCache[entityLogicalName] = entry;
            return entry;
        }

        _recordTypeRefCache[entityLogicalName] = null;
        return null;
    }

    /// <summary>
    /// Build a Dataverse record URL for the resolver.
    /// Uses the Dataverse environment base URL from the service client connection.
    /// </summary>
    private static string BuildRecordUrl(string entityLogicalName, string recordId)
    {
        // On the server side we don't have Xrm context, but we know the org URL
        // from the service client. Use a relative URL that works in model-driven apps.
        return $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }

    /// <summary>
    /// Map entity logical name to its primary name attribute. Delegates to the shared
    /// <see cref="RegardingNameFields"/> (single source of truth — task 042 reuse).
    /// </summary>
    private static string? GetPrimaryNameField(string entityLogicalName) =>
        RegardingNameFields.PrimaryNameField(entityLogicalName);

    /// <summary>
    /// Map entity logical name to its reference-number attribute (the human-facing record number that
    /// belongs in <c>sprk_regardingrecordnumber</c>). Null for entity types with no reference-number
    /// convention (contact / account / organization / …), in which case the number field is left unset.
    /// </summary>
    private static string? GetReferenceNumberField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_matternumber",
        "sprk_project" => "sprk_projectnumber",
        "sprk_invoice" => "sprk_invoicenumber",
        _ => null,
    };
}
