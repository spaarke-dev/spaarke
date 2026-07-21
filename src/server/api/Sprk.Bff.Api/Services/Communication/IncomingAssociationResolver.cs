using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;

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
        ILogger<IncomingAssociationResolver> logger)
    {
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
        _statusMapper = statusMapper;
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
        var decision = await EvaluateInternalAsync(message, context, communicationId, ct);

        await ApplyDecisionAsync(communicationId, decision, ct);
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
        => EvaluateInternalAsync(message, context, Guid.Empty, ct);

    /// <summary>
    /// Shared evaluate core: deterministic pass → ladder → cost-gated AI escalation → resolving-rung telemetry.
    /// Returns the decision; never writes. <paramref name="communicationId"/> is used only for telemetry
    /// correlation (the real id on the resolve path, <see cref="Guid.Empty"/> on the read-only preview path).
    /// </summary>
    private async Task<AssociationDecision> EvaluateInternalAsync(
        NormalizedMessage message,
        AssociationContext context,
        Guid communicationId,
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
             // ContactNameMatch (rung 3.6) is an exact, verified full-name→contact appearance (regex + exact
             // Dataverse lookup, no AI cost), so it belongs in the deterministic pass — even though the mapper
             // keeps it OUT of auto-file eligibility (owner: surface-for-review, never auto-file).
             or RungKind.ContactNameMatch;

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
        CancellationToken ct)
    {
        var fields = new Dictionary<string, object>();
        foreach (var (fieldName, target) in decision.RegardingWrites)
        {
            fields[fieldName] = target;
        }

        fields["sprk_associationstatus"] = new OptionSetValue(decision.Status);
        fields["sprk_associationprovenance"] = SerializeProvenance(decision.Provenance);

        // Populate polymorphic resolver fields whenever we asserted a regarding lookup (a Suggested
        // record surfaces the proposed target too, so the review UI needs the denormalized fields).
        if (decision.RegardingWrites.Count > 0)
        {
            await PopulateResolverFieldsAsync(fields, ct);
        }

        await _genericEntityService.UpdateAsync("sprk_communication", communicationId, fields, ct);

        _logger.LogDebug(
            "Applied association to communication {CommunicationId} | Status: {Status}, AutoFiled: {AutoFiled}, FieldCount: {FieldCount}",
            communicationId, AssociationStatusCodes.Name(decision.Status), decision.AutoFiled, fields.Count);
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
    ///   - sprk_regardingrecordtype  (Lookup → sprk_recordtype_ref)
    ///   - sprk_regardingrecordid    (Text — parent GUID)
    ///   - sprk_regardingrecordname  (Text — parent display name)
    ///   - sprk_regardingrecordurl   (URL — clickable link to parent record)
    /// </summary>
    private async Task PopulateResolverFieldsAsync(
        Dictionary<string, object> fields,
        CancellationToken ct)
    {
        // Find the primary regarding entity (highest priority field that was set). Priority order is the
        // single source of truth in RegardingFieldMap.All (ADR-024) — the rungs write the same fields.
        EntityReference? primaryRef = null;
        string? primaryEntityLogicalName = null;

        foreach (var (entityLogicalName, fieldName) in RegardingFieldMap.All)
        {
            if (fields.TryGetValue(fieldName, out var value) && value is EntityReference entityRef)
            {
                primaryRef = entityRef;
                primaryEntityLogicalName = entityLogicalName;
                break;
            }
        }

        if (primaryRef is null || primaryEntityLogicalName is null)
            return;

        try
        {
            // Set sprk_regardingrecordid (GUID as text)
            var cleanId = primaryRef.Id.ToString("D").ToLowerInvariant();
            fields["sprk_regardingrecordid"] = cleanId;

            // Set sprk_regardingrecordname (display name from the EntityReference or retrieve)
            var recordName = primaryRef.Name;
            if (string.IsNullOrEmpty(recordName))
            {
                // EntityReference.Name may not always be populated; try to retrieve it
                try
                {
                    var nameField = GetPrimaryNameField(primaryEntityLogicalName);
                    if (nameField is not null)
                    {
                        var record = await _genericEntityService.RetrieveAsync(
                            primaryEntityLogicalName, primaryRef.Id, [nameField], ct);
                        recordName = record.GetAttributeValue<string>(nameField) ?? "";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not retrieve name for {Entity} {Id}",
                        primaryEntityLogicalName, primaryRef.Id);
                }
            }
            fields["sprk_regardingrecordname"] = recordName ?? "";

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
    /// Map entity logical name to its primary name attribute.
    /// </summary>
    private static string? GetPrimaryNameField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_mattername",
        "sprk_project" => "sprk_projectname",
        "sprk_invoice" => "sprk_name",
        "sprk_event" => "sprk_eventname",
        "sprk_workassignment" => "sprk_name",
        "sprk_servicerequest" => "sprk_name",
        "sprk_budget" => "sprk_name",
        "sprk_analysis" => "sprk_name",
        "sprk_organization" => "sprk_name",
        "contact" => "fullname",
        "account" => "name",
        _ => null,
    };
}
