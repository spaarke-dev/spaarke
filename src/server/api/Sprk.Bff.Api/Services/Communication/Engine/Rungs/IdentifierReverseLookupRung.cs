using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Engine.Rungs;

/// <summary>
/// Rung 0 (identifier reverse-lookup, FR-01) — extends deterministic identifier matching from matter-only
/// (<see cref="ExplicitReferenceRung"/>'s subject matter-token source) to ALL 7 core record types
/// (matter, project, invoice, work assignment, budget, service request, report card) by <b>value-based
/// reverse lookup against Dataverse</b>, driven entirely by the <c>sprk_recordtype_ref</c> catalog.
/// </summary>
/// <remarks>
/// <para>
/// <b>No numbering scheme in code (binding MUST).</b> The rung does NOT decode/parse any per-entity numbering
/// format to decide the target entity. It extracts identifier-SHAPED candidate tokens broadly from
/// subject + body + attachment text, then matches each token's raw VALUE against every core type's
/// catalog-named number field (<c>sprk_regardingrecordnumberfield</c>) via
/// <see cref="ICommunicationDataverseService.QueryRecordsByNumberFieldAsync"/>. The data decides the target;
/// onboarding a tenant with a different numbering convention needs ONLY catalog config, no code change.
/// </para>
/// <para>
/// <b>Catalog-driven roster, read defensively (per task 001).</b> The per-type roster (regarding write field +
/// number field) is built from <see cref="ICommunicationDataverseService.QueryAllRecordTypeRefsAsync"/>,
/// filtered to the 7 core deterministic types, and cached for the lifetime of this singleton. Rows with a
/// null/blank <c>sprk_regardingrecordnumberfield</c> (PERS, TODO) are skipped — which also side-steps the
/// contact-row <c>sprk_regardingcontact</c> anomaly (contact is not a core type and has a null number field).
/// The regarding WRITE field comes from <see cref="RegardingFieldMap"/> (the code source of truth, ADR-024),
/// never the catalog's <c>sprk_regardingfield</c> column — so a dirty catalog field name can never produce a
/// wrong-field write. A row is used only if it maps to a non-null <see cref="RegardingFieldMap.FieldFor"/>.
/// </para>
/// <para>
/// <b>Confidence policy (the reinforcement mechanism, C-1).</b> Emits with
/// <see cref="RungKind.ExplicitReference"/> at <see cref="Order"/> 0 (a well-formed explicit identifier IS a
/// rung-0-class explicit reference; reusing the kind means zero mapper/enum change — the mapper takes the
/// per-kind MAX per target, so this rung never double-counts with <see cref="ExplicitReferenceRung"/>):
/// <list type="bullet">
///   <item>A <b>well-formed</b> token (alpha prefix + separator, e.g. <c>PRJT.10001.01</c>, <c>INV-002</c>,
///   <c>CMRCL-441482</c>) matching exactly one record → <c>0.90</c> (≥ the 0.85 auto-file threshold ⇒ can
///   auto-file alone; the rung-0 explicit-ID case).</item>
///   <item>A <b>bare-numeric</b> token (pure digits, e.g. <c>441482</c>) matching one record → <c>0.65</c>
///   (BELOW threshold ⇒ NEVER auto-files alone; only clears the bar when the SAME field is reinforced by
///   another eligible rung — e.g. thread continuity inheriting the same regarding → noisy-OR → auto-file
///   "within the thread"). This is how "bare-numeric never auto-files alone" is enforced.</item>
///   <item><b>Multi-entity</b> — a token whose value matches records of MORE THAN ONE type → EVERY emitted
///   match for that token is capped to <c>0.65</c> (sub-threshold) so NONE auto-files; all surface as
///   Suggested review candidates (never a guessed single auto-file).</item>
///   <item><b>Same-field duplicates</b> — 2+ records match on the SAME field → each emitted at its normal
///   confidence; the mapper's same-field-2-targets-≥-threshold rule then yields <c>Ambiguous</c> (never a
///   guess).</item>
/// </list>
/// </para>
/// <para>
/// <b>Cost (NFR-08).</b> Runs only when there is at least one candidate token — no tokens ⇒ zero Dataverse
/// queries. Distinct (field, value) lookups are deduped; distinct tokens are capped at
/// <see cref="MaxDistinctTokens"/>. The per-message reverse-lookup query count is logged (structured
/// <c>QueryCount</c>). <b>Non-fatal (NFR-04)</b>: a roster-load failure yields an empty roster (no matches);
/// each per-lookup failure is caught and degrades to no-match — the rung never throws into the capture path.
/// </para>
/// </remarks>
public sealed class IdentifierReverseLookupRung : IAssociationRung
{
    private readonly ICommunicationDataverseService _communicationService;
    private readonly ILogger<IdentifierReverseLookupRung> _logger;

    /// <summary>
    /// The 7 core record types in deterministic identifier scope (FR-01). This is a SET of entity logical
    /// names — NOT a numbering scheme (no format is encoded here). The number field for each is read from the
    /// catalog; onboarding a NEW numbering convention for these types needs no code change. Adding an 8th core
    /// type is the only code change this rung ever needs (plus its <see cref="RegardingFieldMap"/> entry).
    /// </summary>
    private static readonly IReadOnlySet<string> CoreDeterministicTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_matter",
        "sprk_project",
        "sprk_invoice",
        "sprk_workassignment",
        "sprk_budget",
        "sprk_servicerequest",
        "sprk_reportcard",
    };

    /// <summary>Max distinct candidate tokens scanned per message (NFR-08 cost cap).</summary>
    private const int MaxDistinctTokens = 25;

    private const double WellFormedConfidence = 0.90;
    private const double BareNumericConfidence = 0.65;
    /// <summary>Sub-threshold cap applied to every match of a token whose value spans multiple entity types.</summary>
    private const double MultiEntityCap = 0.65;

    /// <summary>
    /// Well-formed record-number token: a 2+ letter prefix, a <c>-</c> or <c>.</c> separator, then
    /// alphanumerics/dots/hyphens ending on an alphanumeric. Matches e.g. <c>PRJT.10001.01</c>, <c>INV-002</c>,
    /// <c>CMRCL-441482</c>, <c>MAT-123</c>. Generalizes <see cref="RecordNameMatchRung"/>'s shape (2–6 letter,
    /// hyphen-only) to allow dot separators + longer prefixes. Precision comes from the EXACT reverse lookup,
    /// not this pattern — an over-match simply resolves to no record.
    /// </summary>
    private static readonly Regex WellFormedTokenPattern =
        new(@"\b[A-Za-z]{2,}[-.][A-Za-z0-9][A-Za-z0-9.\-]*[A-Za-z0-9]\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Bare-numeric token: 4+ digits (shorter is too noisy). Never auto-files alone (0.65).</summary>
    private static readonly Regex BareNumericTokenPattern =
        new(@"\b\d{4,}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Lazily-built, cached roster (7 core types → regarding write field + number field). Deterministic per
    /// process, so a rare concurrent double-load just recomputes the same value (mirrors
    /// <c>IncomingAssociationResolver._recordTypeRefCache</c>'s benign-race stance). <c>volatile</c> so a
    /// completed load publishes to other threads.
    /// </summary>
    private volatile IReadOnlyList<RosterEntry>? _rosterCache;

    public IdentifierReverseLookupRung(
        ICommunicationDataverseService communicationService,
        ILogger<IdentifierReverseLookupRung> logger)
    {
        _communicationService = communicationService;
        _logger = logger;
    }

    // Reuse the rung-0 kind: a well-formed explicit identifier IS an explicit reference (C-1). Same Kind +
    // Order as ExplicitReferenceRung — the engine runs both (it aggregates all deterministic rungs) and the
    // mapper collapses same-kind matches per target to their MAX, so there is no double-count.
    public RungKind Kind => RungKind.ExplicitReference;
    public int Order => 0;

    public async Task<IReadOnlyList<RungMatch>> EvaluateAsync(
        NormalizedMessage message, AssociationContext context, CancellationToken ct)
    {
        // NFR-08 gate: extract candidate tokens FIRST; if the envelope carries none, do zero Dataverse work.
        // (AssociationContext carries no "already resolved" flag today; the zero-token gate is the cost guard.)
        var tokens = ExtractTokens(message);
        if (tokens.Count == 0)
            return Array.Empty<RungMatch>();

        var roster = await GetRosterAsync(ct);
        if (roster.Count == 0)
            return Array.Empty<RungMatch>();

        var startTs = Stopwatch.GetTimestamp();
        var queryCount = 0;

        // token → its candidate matches (record per core type). Grouped so the confidence policy (single vs
        // multi-entity) is applied per token.
        var perToken = new List<(TokenCandidate Token, List<Candidate> Matches)>();

        // Dedup identical (numberField, value) lookups across tokens/types — two core types never share a
        // number field, so (numberField, value) uniquely identifies a query.
        var queried = new Dictionary<(string Field, string Value), IReadOnlyList<Entity>>();

        foreach (var token in tokens)
        {
            var matches = new List<Candidate>();
            foreach (var entry in roster)
            {
                var key = (entry.NumberField, token.Value);
                if (!queried.TryGetValue(key, out var records))
                {
                    records = await SafeQueryAsync(entry, token.Value, ct);
                    queried[key] = records;
                    queryCount++;
                }

                foreach (var record in records)
                {
                    if (record.Id == Guid.Empty)
                        continue;
                    matches.Add(new Candidate(entry, record.Id));
                }
            }

            if (matches.Count > 0)
                perToken.Add((token, matches));
        }

        var result = BuildMatches(perToken);

        var elapsed = Stopwatch.GetElapsedTime(startTs);
        _logger.LogInformation(
            "Rung 0 (identifier reverse-lookup) fired | Tokens: {TokenCount}, QueryCount: {QueryCount}, " +
            "Matches: {MatchCount}, ElapsedMs: {ElapsedMs}",
            tokens.Count, queryCount, result.Count, (long)elapsed.TotalMilliseconds);

        return result;
    }

    // ── Token extraction ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts distinct identifier-shaped candidate tokens from subject + body + attachment text, each
    /// classified well-formed (alpha-prefixed/separator-bearing) vs bare-numeric (4+ pure digits). Well-formed
    /// is scanned first so a token that could satisfy both keeps the stronger classification (the patterns are
    /// disjoint in practice — well-formed requires an alpha prefix, bare-numeric is pure digits). Capped at
    /// <see cref="MaxDistinctTokens"/>. Best-effort (NFR-04): a regex timeout yields no tokens for that section.
    /// </summary>
    private static IReadOnlyList<TokenCandidate> ExtractTokens(NormalizedMessage message)
    {
        var byValue = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // value → isWellFormed
        var order = new List<string>();

        void Scan(Regex pattern, bool wellFormed, string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || byValue.Count >= MaxDistinctTokens)
                return;
            try
            {
                foreach (Match m in pattern.Matches(text))
                {
                    var value = m.Value.Trim();
                    if (value.Length == 0)
                        continue;
                    if (!byValue.ContainsKey(value))
                    {
                        byValue[value] = wellFormed;
                        order.Add(value);
                        if (byValue.Count >= MaxDistinctTokens)
                            return;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Defensive (NFR-04): a pathological input yields no tokens for this section.
            }
        }

        // Well-formed first (stronger classification), across all three text sources; then bare-numeric.
        Scan(WellFormedTokenPattern, true, message.Subject);
        Scan(WellFormedTokenPattern, true, message.BodyText);
        Scan(WellFormedTokenPattern, true, message.AttachmentText);
        Scan(BareNumericTokenPattern, false, message.Subject);
        Scan(BareNumericTokenPattern, false, message.BodyText);
        Scan(BareNumericTokenPattern, false, message.AttachmentText);

        return order.Select(v => new TokenCandidate(v, byValue[v])).ToArray();
    }

    // ── Confidence policy ──────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the C-1 confidence policy per token and dedups the emitted matches by (field, target) keeping
    /// the highest confidence (the mapper collapses per-kind max anyway, so max-wins is safe):
    /// well-formed = 0.90, bare-numeric = 0.65, multi-entity token capped to 0.65.
    /// </summary>
    private static IReadOnlyList<RungMatch> BuildMatches(
        IReadOnlyList<(TokenCandidate Token, List<Candidate> Matches)> perToken)
    {
        var best = new Dictionary<(string Field, Guid Id), RungMatch>();

        foreach (var (token, matches) in perToken)
        {
            var distinctFields = matches.Select(m => m.Entry.RegardingField).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var multiEntity = distinctFields > 1;

            var baseConfidence = token.IsWellFormed ? WellFormedConfidence : BareNumericConfidence;
            var confidence = multiEntity ? Math.Min(baseConfidence, MultiEntityCap) : baseConfidence;

            foreach (var candidate in matches)
            {
                var provenance =
                    $"explicit:id-reverse-lookup:{candidate.Entry.EntityLogicalName}:" +
                    $"number={candidate.Entry.NumberField}:" +
                    $"value=\"{RungProvenanceFormat.EscapeValue(token.Value)}\":" +
                    $"class={(token.IsWellFormed ? "well-formed" : "bare-numeric")}" +
                    (multiEntity ? ":multi-entity" : string.Empty);

                var match = new RungMatch
                {
                    RegardingFieldName = candidate.Entry.RegardingField,
                    Target = new EntityReference(candidate.Entry.EntityLogicalName, candidate.Id),
                    Confidence = confidence,
                    Provenance = provenance,
                    Rung = RungKind.ExplicitReference,
                };

                var dedupKey = (candidate.Entry.RegardingField, candidate.Id);
                if (!best.TryGetValue(dedupKey, out var existing) || match.Confidence > existing.Confidence)
                    best[dedupKey] = match;
            }
        }

        return best.Values.ToArray();
    }

    // ── Roster (catalog-driven, cached, defensive) ─────────────────────────────────

    private async Task<IReadOnlyList<RosterEntry>> GetRosterAsync(CancellationToken ct)
    {
        var cached = _rosterCache;
        if (cached is not null)
            return cached;

        IReadOnlyList<Entity> rows;
        try
        {
            rows = await _communicationService.QueryAllRecordTypeRefsAsync(ct) ?? Array.Empty<Entity>();
        }
        catch (Exception ex)
        {
            // Non-fatal (NFR-04): a catalog read failure degrades to an empty roster (no matches). Do NOT
            // cache a failure — a later message can retry once the transient clears.
            _logger.LogWarning(ex, "Rung 0 (identifier reverse-lookup) — sprk_recordtype_ref roster read failed; degrading to no-match.");
            return Array.Empty<RosterEntry>();
        }

        var roster = new List<RosterEntry>(CoreDeterministicTypes.Count);
        foreach (var row in rows)
        {
            // Defensive read (task 001): trim; restrict to the 7 core types; require a non-blank number field
            // (skips PERS/TODO — which also side-steps the contact-row anomaly); take the WRITE field from the
            // code source of truth (RegardingFieldMap), never the catalog's sprk_regardingfield column.
            var logicalName = row.GetAttributeValue<string>("sprk_recordlogicalname")?.Trim();
            if (string.IsNullOrWhiteSpace(logicalName) || !CoreDeterministicTypes.Contains(logicalName))
                continue;

            var numberField = row.GetAttributeValue<string>("sprk_regardingrecordnumberfield")?.Trim();
            if (string.IsNullOrWhiteSpace(numberField))
                continue; // null/blank number field → never a reverse-lookup target

            var regardingField = RegardingFieldMap.FieldFor(logicalName);
            if (string.IsNullOrWhiteSpace(regardingField))
                continue; // unmapped in code → do not write an unknown lookup

            roster.Add(new RosterEntry(logicalName, regardingField, numberField));
        }

        var built = roster.ToArray();
        _rosterCache = built;

        _logger.LogDebug("Rung 0 (identifier reverse-lookup) roster built | CoreTypes: {CoreTypeCount}", built.Length);
        return built;
    }

    private async Task<IReadOnlyList<Entity>> SafeQueryAsync(RosterEntry entry, string value, CancellationToken ct)
    {
        try
        {
            return await _communicationService.QueryRecordsByNumberFieldAsync(
                entry.EntityLogicalName, entry.NumberField, value, ct) ?? Array.Empty<Entity>();
        }
        catch (Exception ex)
        {
            // Non-fatal (NFR-04): a single reverse-lookup failure degrades to no-match for that (type, value).
            _logger.LogWarning(ex,
                "Rung 0 (identifier reverse-lookup) — {Entity}.{Field} lookup failed; treating as no-match.",
                entry.EntityLogicalName, entry.NumberField);
            return Array.Empty<Entity>();
        }
    }

    // ── Internal working types ─────────────────────────────────────────────────────

    /// <summary>A catalog roster entry for one core type: its regarding write field + reverse-lookup number field.</summary>
    private sealed record RosterEntry(string EntityLogicalName, string RegardingField, string NumberField);

    /// <summary>A candidate identifier token + its classification.</summary>
    private readonly record struct TokenCandidate(string Value, bool IsWellFormed);

    /// <summary>A record matched for a token (which core type it belongs to + its id).</summary>
    private readonly record struct Candidate(RosterEntry Entry, Guid Id);
}
