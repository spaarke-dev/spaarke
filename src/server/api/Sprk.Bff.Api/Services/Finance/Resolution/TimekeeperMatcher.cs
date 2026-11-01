using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>The status choice of <c>sprk_timekeeper</c> (task 017 notes section 4).</summary>
public enum TimekeeperStatus
{
    Approved = 1,
    Pending = 659490000,
    Inactive = 2,
}

/// <summary>One roster row of the firm, as read from <c>sprk_timekeeper</c>.</summary>
/// <param name="FirmId">The row's firm; a row of any other firm (or none) is never used.</param>
/// <param name="IsActive"><c>statecode = 0</c>.</param>
/// <param name="StatusCode">The <c>statuscode</c> option value (see <see cref="TimekeeperStatus"/>).</param>
/// <param name="EffectiveFrom">First day approved to bill; <c>null</c> = no start.</param>
/// <param name="EffectiveTo">Last day (inclusive); <c>null</c> = open ended.</param>
/// <param name="Classification">The <c>sprk_timekeeperrole</c> option value.</param>
public sealed record RosterEntry(
    Guid Id,
    Guid? FirmId,
    string Name,
    bool IsActive,
    int? StatusCode,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    int? Classification);

/// <summary>One name to resolve: the timekeeper as the fee line prints it, and the line's date.</summary>
public sealed record TimekeeperMatchRequest(string? BilledName, DateOnly? LineDate);

/// <summary>
/// Matches the timekeeper names of an invoice to the firm's roster (FR-23): exact, then normalized, then AI-assisted. A line is matched
/// only to a timekeeper of the invoice's firm who is Approved and active on the line date; anything else stays unmatched and never
/// silently becomes another person.
/// </summary>
/// <remarks>
/// <para><b>Stages</b>, in order, each on the name effective for the line:
/// (1) exact, case-insensitive; (2) normalized (<see cref="TimekeeperNameParser"/>); (3) AI-assisted. A stage that finds exactly one
/// timekeeper decides; one that finds several leaves the line unmatched (<see cref="TimekeeperUnmatchedReason.Ambiguous"/>); one that
/// finds none passes to the next.</para>
/// <para><b>Initials.</b> A label that is initials only ("DL") or an initial and a family name ("Lockhart, D.") is expanded through the
/// invoice's own timekeeper summary names, when exactly one summary person fits. It is never expanded from the roster, because a roster
/// that holds Hannah Rosenthal would otherwise absorb a Hector Rosenthal who is not on it. Unexpanded, such a label cannot match at
/// stage 1 or 2 and is not sent to stage 3 (<see cref="TimekeeperUnmatchedReason.NameIncomplete"/>).</para>
/// <para><b>AI-assisted stage.</b> The embedding (<see cref="IInvoiceAi.GenerateEmbeddingAsync"/>, no Action, no prompt: D-52) of the
/// name is compared by cosine similarity with the embedding of each approved candidate whose family name is within
/// <see cref="MaxFamilyNameEdits"/> edits of the name's, whose generational suffix is the same and whose middle names do not contradict
/// the name's (candidate blocking: it bounds the embedding calls to the few plausible people instead of the whole roster, and keeps
/// "John Smith Jr" and "John B Smith" from being mapped to "John Smith" and "John A Smith" however close the vectors are). The best
/// candidate is used only when its similarity is at or above the reliability threshold and it
/// leads the second candidate by at least <see cref="MinimumLead"/>. Embeddings are memoized per call, so each distinct text is embedded
/// once per invoice however many lines use it. If the embedding call fails or AI is off, the line stays unmatched
/// (<see cref="TimekeeperUnmatchedReason.AiUnavailable"/>); resolution does not fail.</para>
/// <para><b>Known name on the roster but not approved.</b> If the effective name matches (exactly or normalized) a roster row of the firm
/// that is not Approved and active on the date, the line is unmatched with <see cref="TimekeeperUnmatchedReason.NotApprovedOnLineDate"/>
/// and the AI stage is skipped, so the AI cannot hop from a known unapproved person to a look-alike approved one. A roster row with exactly
/// the billed name that is not eligible ends the match before stage 2, so a normalized match cannot map it to an approved longer name.</para>
/// <para><b>Evidence for the AI threshold</b> (task notes): on the evaluation-set rosters no different-person pair reached 0.90.</para>
/// </remarks>
public sealed class TimekeeperMatcher
{
    /// <summary>
    /// How far ahead of the second candidate the best AI-assisted candidate must be. Assumption, not a setting (no column for it in
    /// task 025): the closest different-person pair on the evaluation-set rosters differs by 0.14 in similarity from the next, so 0.05 is
    /// a floor, not a tuned value. Calibrated in tasks 161 / 162.
    /// </summary>
    internal const decimal MinimumLead = 0.05m;

    /// <summary>Largest edit distance between family names for a roster row to be an AI-assisted candidate.</summary>
    internal const int MaxFamilyNameEdits = 2;

    private readonly IInvoiceAi _ai;
    private readonly ILogger<TimekeeperMatcher> _logger;

    public TimekeeperMatcher(IInvoiceAi ai, ILogger<TimekeeperMatcher> logger)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Resolves every request against one roster read. The result has one entry per request, in order.</summary>
    /// <param name="similarityThreshold">The reliability threshold: the lowest embedding similarity an AI-assisted match may have.</param>
    public async Task<IReadOnlyList<TimekeeperResolution>> MatchAsync(
        Guid firmId,
        IReadOnlyList<RosterEntry> roster,
        IReadOnlyList<string> summaryNames,
        IReadOnlyList<TimekeeperMatchRequest> requests,
        decimal similarityThreshold,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(summaryNames);
        ArgumentNullException.ThrowIfNull(requests);

        // A row of another firm (or of no firm) is dropped before anything else: it is not a candidate and not evidence of anything.
        var firmRoster = roster
            .Where(r => r.FirmId == firmId)
            .Select(r => new Candidate(r, TimekeeperNameParser.Parse(r.Name), RawKey(r.Name)))
            .ToList();

        var summary = summaryNames
            .Select(n => (Raw: n, Parsed: TimekeeperNameParser.Parse(n)))
            .Where(s => s.Parsed.IsFullName)
            .ToList();

        var session = new Session(firmRoster, summary, similarityThreshold);
        var results = new List<TimekeeperResolution>(requests.Count);
        foreach (var request in requests)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ResolveAsync(session, request, ct));
        }

        return results;
    }

    private async Task<TimekeeperResolution> ResolveAsync(Session session, TimekeeperMatchRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BilledName))
        {
            return Unmatched(TimekeeperUnmatchedReason.NameMissing);
        }

        if (request.LineDate is not { } date)
        {
            return Unmatched(TimekeeperUnmatchedReason.LineDateMissing);
        }

        var memoKey = (RawKey(request.BilledName), date);
        if (session.Memo.TryGetValue(memoKey, out var memoized))
        {
            return memoized;
        }

        var resolution = await ResolveCoreAsync(session, request.BilledName, date, ct);
        session.Memo[memoKey] = resolution;
        return resolution;
    }

    private async Task<TimekeeperResolution> ResolveCoreAsync(Session session, string billedName, DateOnly date, CancellationToken ct)
    {
        var effectiveRaw = billedName.Trim();
        var effective = TimekeeperNameParser.Parse(effectiveRaw);

        if (!effective.IsFullName)
        {
            switch (TryExpandThroughSummary(session.Summary, effectiveRaw, effective))
            {
                case { Status: Expansion.Ambiguous }:
                    return Unmatched(TimekeeperUnmatchedReason.Ambiguous);
                case { Status: Expansion.Expanded } expanded:
                    effectiveRaw = expanded.Raw!;
                    effective = expanded.Parsed!;
                    break;
                default:
                    // Initials or a single word that nothing in the invoice explains: stages 1-3 cannot be trusted with it.
                    if (!SameKey(effectiveRaw, session.Roster))
                    {
                        return Unmatched(TimekeeperUnmatchedReason.NameIncomplete);
                    }

                    break;
            }
        }

        var key = RawKey(effectiveRaw);
        var eligible = session.Roster.Where(c => IsEligible(c.Entry, date)).ToList();

        // Stage 1: exact, case-insensitive.
        var exact = eligible.Where(c => c.Key == key).ToList();
        if (exact.Count > 0)
        {
            return exact.Count == 1
                ? Matched(exact[0].Entry, TimekeeperMatchStage.Exact, 1m)
                : Unmatched(TimekeeperUnmatchedReason.Ambiguous);
        }

        // A roster person with exactly the billed name who is not approved on this date ends the match before stage 2: stage 2 would
        // otherwise map "David Lee" (Inactive, Pending or out of date) to an Approved "David K. Lee" (a middle name on one side only is
        // treated as missing). Only the exact key is used: a row that merely normalizes to the billed name (an ended 2026 row of a person
        // whose 2027 row is eligible, billed "Doe, Jane") must not block the eligible row's normalized match.
        if (session.Roster.Any(c => c.Key == key && !IsEligible(c.Entry, date)))
        {
            return Unmatched(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
        }

        // Stage 2: normalized.
        var normalized = eligible.Where(c => TimekeeperNameParser.AreSameName(effective, c.Parsed)).ToList();
        if (normalized.Count > 0)
        {
            return normalized.Count == 1
                ? Matched(normalized[0].Entry, TimekeeperMatchStage.Normalized, 1m)
                : Unmatched(TimekeeperUnmatchedReason.Ambiguous);
        }

        // A known roster person who is not approved on this date: say so, and do not let stage 3 pick someone similar.
        var known = session.Roster.Any(c => !IsEligible(c.Entry, date)
            && (c.Key == key || TimekeeperNameParser.AreSameName(effective, c.Parsed)));
        if (known)
        {
            return Unmatched(TimekeeperUnmatchedReason.NotApprovedOnLineDate);
        }

        // Stage 3: AI-assisted. Needs a full name; nothing else is worth a model call.
        if (!effective.IsFullName)
        {
            return Unmatched(TimekeeperUnmatchedReason.NotOnRoster);
        }

        // A generational suffix and a middle name that both sides print are identity: "John Smith Jr" is never "John Smith", and
        // "John B Smith" is never "John A Smith", however close the embeddings are.
        var candidates = eligible
            .Where(c => c.Parsed.IsFullName
                && c.Parsed.Suffix == effective.Suffix
                && TimekeeperNameParser.MiddleNamesAgree(effective, c.Parsed)
                && TimekeeperNameParser.EditDistance(effective.Family, c.Parsed.Family, MaxFamilyNameEdits) <= MaxFamilyNameEdits)
            .ToList();
        if (candidates.Count == 0)
        {
            return Unmatched(TimekeeperUnmatchedReason.NotOnRoster);
        }

        return await MatchByEmbeddingAsync(session, effective, candidates, ct);
    }

    private async Task<TimekeeperResolution> MatchByEmbeddingAsync(
        Session session, ParsedName effective, List<Candidate> candidates, CancellationToken ct)
    {
        float[] nameVector;
        var scored = new List<(Candidate Candidate, decimal Similarity)>(candidates.Count);
        try
        {
            nameVector = await EmbedAsync(session, effective.Canonical, ct);
            foreach (var candidate in candidates)
            {
                var vector = await EmbedAsync(session, candidate.Parsed.Canonical, ct);
                scored.Add((candidate, Cosine(nameVector, vector)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // AI off (Null facade), throttled, or a transport failure. The line stays unmatched; the invoice still resolves.
            // Logged without the name (ADR-015).
            _logger.LogWarning(
                "Timekeeper AI-assisted match unavailable ({ExceptionType}); the line stays unmatched",
                ex.GetType().Name);
            return Unmatched(TimekeeperUnmatchedReason.AiUnavailable);
        }

        // Highest similarity first; the roster id breaks an exact tie so the order never depends on read order.
        scored.Sort((x, y) =>
        {
            var bySimilarity = y.Similarity.CompareTo(x.Similarity);
            return bySimilarity != 0 ? bySimilarity : x.Candidate.Entry.Id.CompareTo(y.Candidate.Entry.Id);
        });

        var best = scored[0];
        if (best.Similarity < session.SimilarityThreshold)
        {
            return Unmatched(TimekeeperUnmatchedReason.AiBelowThreshold);
        }

        if (scored.Count > 1 && best.Similarity - scored[1].Similarity < MinimumLead)
        {
            return Unmatched(TimekeeperUnmatchedReason.AiNotClearlyAhead);
        }

        return Matched(best.Candidate.Entry, TimekeeperMatchStage.AiAssisted, best.Similarity);
    }

    private async Task<float[]> EmbedAsync(Session session, string text, CancellationToken ct)
    {
        if (session.Embeddings.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var vector = (await _ai.GenerateEmbeddingAsync(text, cancellationToken: ct)).ToArray();
        session.Embeddings[text] = vector;
        return vector;
    }

    /// <summary>Cosine similarity, 0 when either vector is empty or they differ in length (a broken embedding must not look similar).</summary>
    internal static decimal Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
        {
            return 0m;
        }

        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            normA += (double)a[i] * a[i];
            normB += (double)b[i] * b[i];
        }

        if (normA <= 0 || normB <= 0)
        {
            return 0m;
        }

        var similarity = dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
        return (decimal)Math.Clamp(similarity, 0.0, 1.0);
    }

    private enum Expansion
    {
        None,
        Expanded,
        Ambiguous,
    }

    private readonly record struct ExpansionResult(Expansion Status, string? Raw = null, ParsedName? Parsed = null);

    /// <summary>Expands initials, or an initial plus family name, to the one full name of the invoice's summary that fits.</summary>
    private static ExpansionResult TryExpandThroughSummary(
        IReadOnlyList<(string Raw, ParsedName Parsed)> summary, string billedRaw, ParsedName billed)
    {
        IEnumerable<(string Raw, ParsedName Parsed)> fits;
        if (TimekeeperNameParser.TryGetBareInitials(billedRaw, out var initials))
        {
            fits = summary.Where(s => TimekeeperNameParser.InitialsOf(s.Parsed).Contains(initials));
        }
        else if (billed.Family.Length > 0 && billed.Given.Count > 0 && billed.Given[0].Length == 1)
        {
            fits = summary.Where(s => s.Parsed.Family == billed.Family
                && s.Parsed.Suffix == billed.Suffix
                && s.Parsed.Given[0].StartsWith(billed.Given[0], StringComparison.Ordinal)
                && TimekeeperNameParser.MiddleNamesAgree(billed, s.Parsed));
        }
        else
        {
            return new ExpansionResult(Expansion.None);
        }

        // The same person listed twice (or in two spellings of the same canonical name) is one person.
        var people = fits.GroupBy(f => f.Parsed.Canonical).Select(g => g.First()).ToList();
        return people.Count switch
        {
            0 => new ExpansionResult(Expansion.None),
            1 => new ExpansionResult(Expansion.Expanded, people[0].Raw.Trim(), people[0].Parsed),
            _ => new ExpansionResult(Expansion.Ambiguous),
        };
    }

    private static bool SameKey(string raw, IReadOnlyList<Candidate> roster)
    {
        var key = RawKey(raw);
        return roster.Any(c => c.Key == key);
    }

    private static bool IsEligible(RosterEntry entry, DateOnly date) =>
        entry.IsActive
        && entry.StatusCode == (int)TimekeeperStatus.Approved
        && (entry.EffectiveFrom is null || entry.EffectiveFrom <= date)
        && (entry.EffectiveTo is null || date <= entry.EffectiveTo);

    /// <summary>Trimmed, whitespace collapsed, upper-cased invariantly: the "exact, case-insensitive" key.</summary>
    private static string RawKey(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static TimekeeperResolution Matched(RosterEntry entry, TimekeeperMatchStage stage, decimal confidence) =>
        new()
        {
            TimekeeperId = entry.Id,
            Stage = stage,
            MatchConfidence = confidence,
            Classification = entry.Classification,
        };

    private static TimekeeperResolution Unmatched(TimekeeperUnmatchedReason reason) =>
        new() { Stage = TimekeeperMatchStage.None, MatchConfidence = 0m, Reason = reason };

    private sealed record Candidate(RosterEntry Entry, ParsedName Parsed, string Key);

    private sealed class Session
    {
        public Session(
            IReadOnlyList<Candidate> roster,
            IReadOnlyList<(string Raw, ParsedName Parsed)> summary,
            decimal similarityThreshold)
        {
            Roster = roster;
            Summary = summary;
            SimilarityThreshold = similarityThreshold;
        }

        public IReadOnlyList<Candidate> Roster { get; }

        public IReadOnlyList<(string Raw, ParsedName Parsed)> Summary { get; }

        public decimal SimilarityThreshold { get; }

        public Dictionary<(string, DateOnly), TimekeeperResolution> Memo { get; } = [];

        public Dictionary<string, float[]> Embeddings { get; } = new(StringComparer.Ordinal);
    }
}
