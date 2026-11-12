using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Signals.Billing;

/// <summary>Why a rule version produced nothing for this submission although it was selected.</summary>
public static class BillingEvaluationReasons
{
    /// <summary>The billing gate (<see cref="PolicyVersionValidator.TryPrepareBillingRule"/>) refused the version (FR-24).</summary>
    public const string GateRefused = "billing.rule.gate_refused";

    /// <summary>D-20: no currency was read, so a money rule is not judged (a missing currency is never taken to be USD). Also the
    /// reason code of D-115's flag-only "rates and expense caps were not checked" finding.</summary>
    public const string CurrencyMissing = "billing.currency.missing";

    /// <summary>D-20: the currency read is not USD, so a money rule is not judged. Also a D-115 finding's reason code.</summary>
    public const string CurrencyNotUsd = "billing.currency.not_usd";

    /// <summary>D-20: the currency's confidence (<c>HeaderConfidence["Currency"]</c>) is unknown or below the reliability threshold, so
    /// a money rule is not judged. Also a D-115 finding's reason code.</summary>
    public const string CurrencyUnreliable = "billing.currency.unreliable";
}

/// <summary>A selected rule version that produced no findings because it was refused: by the gate, or by its handler.</summary>
/// <param name="ReasonCode"><see cref="BillingEvaluationReasons.GateRefused"/>, or the handler's
/// <see cref="BillingRuleRefusedException.ReasonCode"/>.</param>
public sealed record BillingRuleRefusal(Guid PolicyId, string PolicyCode, Guid PolicyVersionId, string ReasonCode);

/// <summary>A money check of a rule version that was not judged because of the invoice's currency (D-20).</summary>
/// <param name="ReasonCode">One of the currency reasons of <see cref="BillingEvaluationReasons"/>.</param>
public sealed record BillingRuleSkip(Guid PolicyId, string PolicyCode, Guid PolicyVersionId, RuleType RuleType, string ReasonCode);

/// <summary>
/// Where a refused rule version applied (101 B2a, F2): the check it stood for did NOT run on these subjects. <paramref name="RuleType"/> is null
/// when the stored type could not be read. <paramref name="AppliesToInvoice"/> means the invoice subject selected the version, so every line is
/// affected; otherwise <paramref name="LineIndexes"/> names the lines. Carries ids and a type, never a value.
/// </summary>
public sealed record BillingRuleRefusalScope(Guid PolicyVersionId, RuleType? RuleType, bool AppliesToInvoice, IReadOnlyList<int> LineIndexes);

/// <summary>
/// ebilling-r1 082c: an applicable Procedure with no version for a subject date (<see cref="ProcedureGap"/>, model 7.1), and the subjects judged on that
/// date: none of that Procedure's rules ran on them. <paramref name="AppliesToInvoice"/>: the invoice subject is dated on it; otherwise
/// <paramref name="LineIndexes"/> names the lines. <see cref="object.ToString"/> prints the gap's reader-safe text and the counts only.
/// </summary>
public sealed record BillingProcedureGap(ProcedureGap Gap, bool AppliesToInvoice, IReadOnlyList<int> LineIndexes)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Gap = ").Append(Gap).Append(", AppliesToInvoice = ").Append(AppliesToInvoice).Append(", Lines = ").Append(LineIndexes.Count);
        return true;
    }
}

/// <summary>
/// ebilling-r1 082d (model 7.4): how precedence (step 4) treated one selected, gate-accepted rule version, per subject. <paramref name="Applied"/>:
/// the subjects it judged (a Split version kept for its code check included). <paramref name="Displaced"/>: the subjects where a higher-ranked valid
/// version of its Replace key displaced it outright (it never ran there). <paramref name="CapDisplaced"/>: the Split subjects where it kept its code
/// check and lost its cap. <see cref="object.ToString"/> prints the source and the counts only (no id; 082b K2, 082d K6).
/// </summary>
public sealed record BillingRuleApplication(
    Guid PolicyVersionId, BillingRuleSource Source, IReadOnlyList<BillingSubject> Applied, IReadOnlyList<BillingSubject> Displaced,
    IReadOnlyList<BillingSubject> CapDisplaced)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Source = ").Append(Source).Append(", Applied = ").Append(Applied.Count).Append(", Displaced = ").Append(Displaced.Count)
            .Append(", CapDisplaced = ").Append(CapDisplaced.Count);
        return true;
    }
}

/// <summary>
/// ebilling-r1 082d (model 7.4): the basis of one evaluation, as the evaluator used it: the core-record chain the resolution read, the
/// <see cref="ProcedureRulesOnDate"/> of every distinct subject date (the C9 memo, in date order: the objects the evaluator judged with, never
/// re-resolved), and how precedence treated each selected rule version (both streams). The submission persists it as its snapshot
/// (<c>procedureSnapshot</c> in <c>sprk_findingsjson</c>); 083 cites it and 113 compares with it. INTERNAL: it holds record, row and Procedure
/// ids and override reasons; a reader-facing output uses <see cref="ProcedureRulesOnDate.ForReaders"/>. <see cref="object.ToString"/> prints the
/// counts only.
/// </summary>
public sealed record BillingProcedureBasis(
    IReadOnlyList<CoreRecordLink> Chain, IReadOnlyList<ProcedureRulesOnDate> Dates, IReadOnlyList<BillingRuleApplication> Applications)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Chain = ").Append(Chain.Count).Append(", Dates = ").Append(Dates.Count).Append(", Applications = ").Append(Applications.Count);
        return true;
    }
}

/// <summary>The outcome of one evaluation. Deterministic: the same request and facts give equal results in equal order.</summary>
public sealed record BillingEvaluationResult
{
    /// <summary>The findings, ordered: the invoice first, then by line index; within a subject by catalogue position, policy code
    /// (ordinal), policy id and version id; then in the handler's own order.</summary>
    public required IReadOnlyList<BillingFinding> Findings { get; init; }

    /// <summary>The rule version behind each finding, by <see cref="BillingFinding.PolicyVersionId"/> (for the Signal mapping, its
    /// lane, severity, headline and template; and for task 073, its consequence).</summary>
    public required IReadOnlyDictionary<Guid, BillingRuleCandidate> RulesByVersion { get; init; }

    /// <summary>Selected versions refused by the gate or by their handler, in evaluation order. Each is logged and metered.</summary>
    public required IReadOnlyList<BillingRuleRefusal> Refusals { get; init; }

    /// <summary>Where each of <see cref="Refusals"/> applied, in the same order (101 B2a: a check that did not run must not leave a line Clean).
    /// A separate member so <see cref="BillingRuleRefusal"/> and its equality stay as 072 defined them.</summary>
    public IReadOnlyList<BillingRuleRefusalScope> RefusalScopes { get; init; } = [];

    /// <summary>Money checks not judged because of the currency (D-20), in evaluation order. Each is logged.</summary>
    public required IReadOnlyList<BillingRuleSkip> Skips { get; init; }

    /// <summary>
    /// 082c: the Procedure gaps on the subjects' dates, one per (Procedure, date), ordered by date then Procedure. A gapped Procedure judged nothing
    /// on those subjects, so an invoice with any gap is never a pass: in 082c-1 the submission processor refuses to judge it (interim), and 082c-2
    /// holds it.
    /// </summary>
    public IReadOnlyList<BillingProcedureGap> Gaps { get; init; } = [];

    /// <summary>082d (model 7.4): the basis the submission stores as its snapshot. Null only on a result built elsewhere (a test double).</summary>
    public BillingProcedureBasis? ProcedureBasis { get; init; }

    /// <summary>The fact set the handlers judged.</summary>
    public required BillingFactSet Facts { get; init; }
}

/// <summary>
/// Evaluates an extracted, resolved invoice against the billing rules in force (ebilling-r1 task 072, FR-25, D-23 in-memory
/// strategy): selects for the invoice and for each line the published rule versions in force on its date, the firm's rule over the
/// client default's of the same type (D-39), passes each through the billing gate (FR-24), runs task 071's handlers and returns
/// deterministic findings with their suggested adjustments. Writes nothing: task 049 persists the findings and maps them to Signals
/// with <see cref="BillingFindingSignalMapper"/>.
/// </summary>
/// <remarks>
/// <para><b>Which version, on which date.</b> A line is judged on its own date, else the invoice date; the invoice on the invoice
/// date. A subject with no date at all (no line date and no invoice date) is judged on the date the submission was received
/// (<see cref="BillingEvaluationRequest.ReceivedOn"/>, data from task 049, never a clock read; 072 review A3), so a current version
/// published with a later effective date does not judge an invoice received before it. A version applies when it is in force on
/// that date (<see cref="BillingRuleCandidate.IsInForceOn"/>, task 076 K3); a draft, a rejected draft or an abandoned edit never
/// applies. When two versions of one policy are in force on one date (overlapping ranges left by a failed publish), the current
/// version wins, else the highest version number.</para>
/// <para><b>Two streams (082c).</b> The legacy rules (<see cref="BillingLoadedFacts.Rules"/>) are picked per subject date by
/// <see cref="SelectVersion"/>; the Procedure rules by <see cref="ProcedureResolution.RulesOn"/>, once per distinct subject date, from the one
/// resolution the loader read (C9). A Procedure rule never goes through the legacy version chain. A version in both streams is a fault (C4).</para>
/// <para><b>Precedence (082c C5, C6; model 7.3; ontology D-172 Q7, D-205 item 2).</b> Per subject and per
/// <see cref="BillingRuleDefinition.PrecedenceKey"/> (the rule type; for AddedFee also its fee kind, and for 'other' its fee codes), by
/// <see cref="BillingRuleCatalogue.PrecedenceOf"/> and <see cref="BillingRuleCandidate.Rank"/>, for legacy and Procedure rules alike (a legacy
/// Firm rule ranks above a legacy Client default one). Replace (a limit): the versions below the highest VALID rank are displaced; a tie means all
/// apply. Additive (a checklist): all apply. Split (ExpenseRule): a displaced version keeps its code check and loses its amount-cap findings.
/// Valid means the gate and the handler accept the version and its consequence was not downgraded: a broken or downgraded specific rule never
/// removes the general one (owner, 2026-10-18; 072 review B2; D-116). <see cref="BillingRuleCatalogue.PrecedenceOf"/> is called only on a
/// gate-accepted definition (C7): a stored row of a non-billing type is a gate refusal, and the other rules still run.</para>
/// <para><b>Refusals (FR-24).</b> A version the gate refuses (logged and metered by the gate) or whose handler refuses it (logged
/// here as <see cref="OntologyWriterEvents.PolicyVersionInvalid"/> and metered) produces no finding; the other versions still run.
/// Only <see cref="BillingRuleRefusedException"/> is caught; any other exception is a defect and propagates.</para>
/// <para><b>A stored consequence the type may not carry is downgraded, not dropped</b> (072 part W, the 073 review's K2). Publish,
/// accept and edit refuse such a consequence; a row written around them is still evaluated, its consequence read as Flag (in
/// <see cref="BillingEvaluationResult.RulesByVersion"/>, which 073 and the mapper read), logged as a Warning and counted on
/// <c>billing.rule.consequence_downgraded</c>. Dropping it would leave its type unchecked when it is the client default, or a firm
/// rule of a type the default does not cover. It is not a refusal and is not listed in <see cref="BillingEvaluationResult.Refusals"/>.
/// A version whose body the gate refuses is still refused. A downgraded FIRM version runs (as a Flag) but does not displace the
/// client default of its type (D-39, D-116; 072 W review F1-b): the client default stays in force beside it.</para>
/// <para><b>Currency (D-20, D-115).</b> R1 judges money in USD only (<c>"$"</c>, <c>"US$"</c> and <c>"USD$"</c> are read as USD). When the currency is missing, is
/// not USD, or its confidence (<c>HeaderConfidence["Currency"]</c>) is unknown or below the reliability threshold, ApprovedRate and
/// Discount (task 115) are not run, ExpenseRule's amount-cap findings are dropped (its code check still runs) and so are an AddedFee
/// amount cap's (task 115; a percent cap still runs); each such skip is logged with its reason
/// and listed in <see cref="BillingEvaluationResult.Skips"/>. When anything was skipped, ONE invoice-level finding says so (D-115:
/// "Rates and expense caps were not checked: the invoice currency is missing / unclear / not USD"), whether or not a RequiredData
/// rule is in force. Its reason (<see cref="BillingEvaluationReasons.CurrencyMissing"/>, <see cref="BillingEvaluationReasons.CurrencyUnreliable"/>,
/// <see cref="BillingEvaluationReasons.CurrencyNotUsd"/>) is flag-only: it never rejects or adjusts. It hangs on a skipped
/// ApprovedRate version when there is one, else on a skipped ExpenseRule version (ordinal policy code, then version id within the
/// type), so its Signal never merges with a Reject reason (049 rule 5), and it cites every skipped version. With no money rule in
/// force nothing is skipped and no such finding is raised.</para>
/// <para><b>Determinism.</b> No clock is read; all ordering is explicit and ordinal.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>SignalEvaluationService</c> (Ontology policies, predicate
/// compiler, which refuses every billing type) and task 071's handlers (judge one rule, select nothing). Extension: no; billing rule
/// selection by line date, rule-set scope and firm has no counterpart in the Ontology evaluator, and the handlers are pure by
/// contract. Cost of doing nothing: no component decides which billing rules apply to an invoice, so 049 cannot produce findings
/// (FR-25).</para>
/// </remarks>
public sealed class InvoiceComplianceEvaluator
{
    private readonly BillingFactLoader _loader;
    private readonly PolicyVersionValidator _gate;
    private readonly ILogger<InvoiceComplianceEvaluator> _logger;
    private readonly Func<BillingRuleDefinition, BillingRuleOrigin, BillingFactSet, IReadOnlyList<BillingFinding>> _handlers;

    public InvoiceComplianceEvaluator(BillingFactLoader loader, PolicyVersionValidator gate, ILogger<InvoiceComplianceEvaluator> logger)
        : this(loader, gate, logger, BillingRuleHandlers.Evaluate)
    {
    }

    /// <summary>Test seam: the handler dispatch. A handler refusal cannot be produced through the gate with a valid body, so the
    /// refusal path is driven by substituting the dispatch.</summary>
    internal InvoiceComplianceEvaluator(
        BillingFactLoader loader, PolicyVersionValidator gate, ILogger<InvoiceComplianceEvaluator> logger,
        Func<BillingRuleDefinition, BillingRuleOrigin, BillingFactSet, IReadOnlyList<BillingFinding>> handlers)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    /// <summary>Loads the facts for <paramref name="request"/> (<see cref="BillingFactLoader"/>) and evaluates it.</summary>
    public async Task<BillingEvaluationResult> EvaluateAsync(BillingEvaluationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireReceivedOn(request);
        var loaded = await _loader.LoadAsync(request, ct).ConfigureAwait(false);
        return Evaluate(request, loaded);
    }

    /// <summary>The in-memory evaluation: no I/O, no clock. Internal so tests can drive it with loaded facts directly.</summary>
    internal BillingEvaluationResult Evaluate(BillingEvaluationRequest request, BillingLoadedFacts loaded)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loaded);
        RequireReceivedOn(request);

        var facts = new BillingFactSet
        {
            Invoice = new BillingInvoiceFacts
            {
                SubmissionId = request.SubmissionId,
                SecureRoots = loaded.InvoiceSecureRoots,
                TrackingNumber = request.TrackingNumber,
                FirmInvoiceNumber = request.FirmInvoiceNumber,
                InvoiceDate = request.InvoiceDate,
                BillingPeriodStart = request.BillingPeriodStart,
                BillingPeriodEnd = request.BillingPeriodEnd,
                Currency = request.Currency,
                FieldConfidence = request.HeaderConfidence,
            },
            Lines = request.Lines,
            CoreRecord = loaded.CoreRecord,
            // K1 / D-112: the submission is never one of its own priors, whatever the loader returned.
            PriorSubmissions = loaded.PriorSubmissions.Where(p => p.SubmissionId != request.SubmissionId).ToList(),
            ReliabilityThreshold = loaded.ReliabilityThreshold,
        };

        // Stable order for every pass below: the loader's order is not relied on.
        // D-125: a system rule (SubmissionIntegrity) is never judged from a rule body: 049 creates its findings directly. A stored row of
        // that type that reached the loader is skipped, not refused, so it can neither crash nor hold an evaluation.
        if (loaded.Rules.FirstOrDefault(c => c.Source != BillingRuleSource.Legacy) is { } stray)
        {
            throw new InvalidOperationException(
                $"BillingLoadedFacts.Rules holds a {stray.Source} rule ({stray.PolicyCode}); Procedure rules come from BillingLoadedFacts.Procedures only.");
        }

        var legacy = loaded.Rules
            .Where(c => c.CanBeInForce && !IsSystemRule(c))
            .OrderBy(c => c.PolicyCode, StringComparer.Ordinal)
            .ThenBy(c => c.PolicyId)
            .ThenBy(c => c.VersionNumber)
            .ThenBy(c => c.PolicyVersionId)
            .ToList();
        var byPolicy = legacy.GroupBy(c => c.PolicyId).Select(g => g.ToList()).ToList();

        // 1. The version of each policy that applies to each subject.
        // A3: with no line date and no invoice date, the received date (data, never the clock) is the judging date.
        var invoiceDate = request.InvoiceDate ?? request.ReceivedOn;
        var subjects = new List<(BillingSubject Subject, DateOnly Date)> { (BillingSubject.ForInvoice(), invoiceDate) };
        subjects.AddRange(request.Lines.OrderBy(l => l.Index).Select(l => (BillingSubject.ForLine(l.Index), l.Date ?? invoiceDate)));

        // 082c C9: the Procedure stream, resolved once per distinct subject date from the one resolution the loader read. Its rules are the
        // resolver's choice for the date: never IsCurrent, IsInForceOn or SelectVersion. A resolution fault on a date propagates.
        var onDate = subjects.Select(s => s.Date).Distinct().Order().ToDictionary(d => d, d => loaded.Procedures.RulesOn(d));
        var procedureCandidates = new Dictionary<Guid, BillingRuleCandidate>();
        BillingRuleCandidate ProcedureCandidateOf(ResolvedRule rule)
        {
            var mapped = BillingFactLoader.ProcedureCandidate(rule);
            if (procedureCandidates.TryGetValue(mapped.PolicyVersionId, out var known))
            {
                // One policy version is one candidate, whatever the date (its Procedure, rank and content are fixed per version).
                return known == mapped
                    ? known
                    : throw new ProcedureResolutionFaultException(
                        $"Policy version {mapped.PolicyVersionId} resolved to two different rules on two dates; the rules that judge the invoice cannot be told.");
            }

            procedureCandidates[mapped.PolicyVersionId] = mapped;
            return mapped;
        }

        // 082c K4, ontology D-205 rule (c): the invoice subject keeps the invoice date for a Procedure's version whenever that Procedure has a
        // version on it (the 072 rule). A Procedure with NO version on the invoice date (a hole, after its end, or not yet applicable) is judged,
        // for the invoice subject only, by its version on the LATEST dated line's work date; the invoice-date gap stands only when that date has
        // none either, or when no line is dated. Legacy selection and line subjects are unchanged. No new RulesOn call: the line date is already
        // resolved (C9's one resolution per distinct date).
        var datedLines = request.Lines.Where(l => l.Date is not null).Select(l => l.Date!.Value).ToList();
        DateOnly? latestWorkDate = datedLines.Count > 0 ? datedLines.Max() : null;
        // The fallback is narrowed to ontology's three kinds (K2): a ConflictingParents gap or a pin gap on the invoice date stands. The one
        // definition is ProcedureRulesOnDate.FallbackProcedureIds; the adjustment options use the same.
        var onLatestWorkDate = latestWorkDate is { } fallbackDate && fallbackDate != invoiceDate ? onDate[fallbackDate] : null;
        var invoiceFallback = onDate[invoiceDate].FallbackProcedureIds(onLatestWorkDate);

        IEnumerable<ResolvedRule> ProcedureRulesOf(BillingSubject subject, DateOnly date) =>
            subject.Kind == BillingSubjectKind.Invoice ? onDate[date].RulesWithFallback(onLatestWorkDate) : onDate[date].Rules;

        var selectedBySubject = subjects
            .Select(s => (s.Subject, Versions: byPolicy.Select(versions => SelectVersion(versions, s.Date)).OfType<BillingRuleCandidate>()
                .Concat(ProcedureRulesOf(s.Subject, s.Date).Select(ProcedureCandidateOf).Where(c => !IsSystemRule(c)))
                .ToList()))
            .ToList();

        // 082c C4: the streams are disjoint (the loader checks the whole read too).
        var legacyVersions = loaded.Rules.Select(c => c.PolicyVersionId).ToHashSet();
        if (procedureCandidates.Keys.FirstOrDefault(legacyVersions.Contains) is var shared && shared != Guid.Empty)
        {
            throw new ProcedureResolutionFaultException(
                $"Policy version {shared} is both a legacy rule and a Procedure rule; the rules that judge the invoice cannot be told.");
        }

        var candidates = legacy.Concat(procedureCandidates.Values)
            .OrderBy(c => c.PolicyCode, StringComparer.Ordinal)
            .ThenBy(c => c.PolicyId)
            .ThenBy(c => c.VersionNumber)
            .ThenBy(c => c.PolicyVersionId)
            .ToList();
        var candidateById = candidates.GroupBy(c => c.PolicyVersionId).ToDictionary(g => g.Key, g => g.First());

        // 082c: the gaps on the subjects' dates, with the subjects dated on each (nothing of that Procedure judged them).
        var gaps = onDate.Values
            .SelectMany(d => d.Gaps)
            .OrderBy(g => g.WorkDate)
            .ThenBy(g => g.ProcedureId)
            .Select(g => new BillingProcedureGap(
                g,
                !invoiceFallback.Contains(g.ProcedureId) && subjects.Any(s => s.Subject.Kind == BillingSubjectKind.Invoice && s.Date == g.WorkDate),
                subjects.Where(s => s.Subject.LineIndex is not null && s.Date == g.WorkDate).Select(s => s.Subject.LineIndex!.Value).Order().ToList()))
            .Where(g => g.AppliesToInvoice || g.LineIndexes.Count > 0)
            .ToList();

        // 2. The gate, once per distinct selected version, in a stable order (FR-24).
        var refusals = new List<BillingRuleRefusal>();
        var accepted = new Dictionary<Guid, BillingRuleDefinition>();
        var downgraded = new Dictionary<Guid, BillingRuleCandidate>();
        foreach (var candidate in candidates.Where(c => selectedBySubject.Any(s => s.Versions.Contains(c))))
        {
            // Task 073 (FR-26, I-5, D-89) refuses a consequence the type may not carry at publish, accept and edit. A stored row that
            // carries one anyway (written around them) is NOT dropped here (072 part W, the 073 review's K2): dropping it would leave
            // the type unchecked when it is the client default, or a firm rule of a type the default does not cover. Its body still
            // goes through the gate (refused as before when invalid); its consequence is evaluated as Flag.
            var disallowed = DisallowedConsequenceType(candidate);
            var snapshot = new PolicyVersionSnapshot(
                candidate.PolicyVersionId, candidate.PolicyCode, candidate.RuleType, candidate.RuleBody, candidate.MessageTemplate,
                disallowed is null ? candidate.Consequence : null);
            if (_gate.TryPrepareBillingRule(snapshot, out var definition))
            {
                accepted[candidate.PolicyVersionId] = definition;
                if (disallowed is { } type)
                {
                    downgraded[candidate.PolicyVersionId] = candidate with { Consequence = (int)BillingConsequence.Flag };
                    _logger.LogWarning(
                        OntologyWriterEvents.PolicyVersionConsequenceDowngraded,
                        "Billing evaluation of submission {SubmissionId}: policy {PolicyCode} version {PolicyVersionId} ({RuleType}) stores consequence {Consequence}, which its type may not carry; it is evaluated with Flag (reason={Reason}).",
                        request.SubmissionId, candidate.PolicyCode, candidate.PolicyVersionId, type, candidate.Consequence,
                        OntologyWriterFailureReason.ConsequenceNotAllowed);
                    OntologyWriterTelemetry.RecordBillingRuleConsequenceDowngraded(type.ToString());
                }
            }
            else
            {
                // The gate has logged (EventId 50301) and metered the refusal.
                refusals.Add(new BillingRuleRefusal(candidate.PolicyId, candidate.PolicyCode, candidate.PolicyVersionId, BillingEvaluationReasons.GateRefused));
            }
        }

        // The version as 073 and the mapper must see it: a downgraded one carries Flag.
        BillingRuleCandidate Effective(BillingRuleCandidate candidate) =>
            downgraded.TryGetValue(candidate.PolicyVersionId, out var flagged) ? flagged : candidate;

        // 3. Each version's handler, at most once, on the whole fact set, and only when it is needed: to find the top valid rank of a key
        //    (step 4), or when the version applies. A handler refusal is logged and metered here (FR-24). ApprovedRate on a
        //    currency it cannot judge is not run (D-20, step 5) and still counts as a valid firm rule.
        var currencySkip = CurrencySkipReason(request, loaded.ReliabilityThreshold);
        var judgedByVersion = new Dictionary<Guid, IReadOnlyList<BillingFinding>?>();
        var handlerRefusals = new Dictionary<Guid, BillingRuleRefusal>();
        bool HandlerAccepts(BillingRuleCandidate candidate, BillingRuleDefinition definition)
        {
            if (judgedByVersion.TryGetValue(candidate.PolicyVersionId, out var done))
            {
                return done is not null;
            }

            if (IsRateComparison(definition.RuleType) && currencySkip is not null)
            {
                judgedByVersion[candidate.PolicyVersionId] = [];
                return true;
            }

            try
            {
                judgedByVersion[candidate.PolicyVersionId] = _handlers(
                    definition, new BillingRuleOrigin(candidate.PolicyId, candidate.ClauseReference, candidate.ClauseLocation), facts);
                return true;
            }
            catch (BillingRuleRefusedException ex)
            {
                _logger.LogError(
                    OntologyWriterEvents.PolicyVersionInvalid,
                    "Billing evaluation of submission {SubmissionId}: the handler refused policy {PolicyCode} version {PolicyVersionId} ({RuleType}): {ReasonCode}. The rule produces no finding; the other rules still run.",
                    request.SubmissionId, candidate.PolicyCode, candidate.PolicyVersionId, ex.RuleType, ex.ReasonCode);
                OntologyWriterTelemetry.RecordBillingRuleInvalid(ex.ReasonCode == BillingRuleRefusedException.NoHandler
                    ? OntologyWriterFailureReason.RuleTypeUnsupported
                    : OntologyWriterFailureReason.SchemaInvalid);
                judgedByVersion[candidate.PolicyVersionId] = null;
                handlerRefusals[candidate.PolicyVersionId] =
                    new BillingRuleRefusal(candidate.PolicyId, candidate.PolicyCode, candidate.PolicyVersionId, ex.ReasonCode);
                return false;
            }
        }

        // 4. Precedence per subject (082c C5, C6; model 7.3; D-172 Q7 and D-205 item 2: legacy rules too), per PrecedenceKey: the rule type,
        //    and for AddedFee the rule type plus its fee kind (owner, 2026-10-21), and for the kind 'other' the fee-code set (ontology-r1 F3), so
        //    a late-fee cap never displaces an administrative-fee cap. Only the rank decides (never Scope). A version displaces another only
        //    when it is VALID: the gate and the handler accept it (072 review B2, owner 2026-10-18: a broken specific rule leaves the general one
        //    in force) and its consequence was not downgraded (072 W review F1-b, D-116: a row written around publish never turns a general
        //    Reject into a Flag). C7 (K3): only gate-accepted definitions reach PrecedenceOf; a refused version takes no part here.
        var subjectsByVersion = new Dictionary<Guid, HashSet<BillingSubject>>();
        var capDisplaced = new Dictionary<Guid, HashSet<BillingSubject>>();
        var displacedOutright = new Dictionary<Guid, HashSet<BillingSubject>>(); // 082d: for the snapshot only; decides nothing
        bool Valid(BillingRuleCandidate version) =>
            !downgraded.ContainsKey(version.PolicyVersionId) && HandlerAccepts(version, accepted[version.PolicyVersionId]);
        static void Add(Dictionary<Guid, HashSet<BillingSubject>> into, Guid versionId, BillingSubject subject)
        {
            if (!into.TryGetValue(versionId, out var set))
            {
                into[versionId] = set = [];
            }

            set.Add(subject);
        }

        foreach (var (subject, versions) in selectedBySubject)
        {
            foreach (var key in versions.Where(v => accepted.ContainsKey(v.PolicyVersionId)).GroupBy(v => accepted[v.PolicyVersionId].PrecedenceKey))
            {
                // 084e: the one shared precedence function (RulePrecedence.Resolve; the record's rules-in-force read calls it too). Ranks are
                // tried from the top and Valid is asked lazily (O-1), so a version displaced outright (Replace) is never run.
                var resolved = RulePrecedence.Resolve(
                    BillingRuleCatalogue.PrecedenceOf(key.Key.RuleType), key.ToList(), v => v.Rank,
                    v => accepted[v.PolicyVersionId].MaxAmountPerItem is not null, Valid);

                foreach (var (version, outcome) in resolved.Outcomes)
                {
                    // Below the top valid rank: Replace displaces it; Split keeps it for its code check and drops its cap (step 5). A tie
                    // with the top is not displaced: ties all apply.
                    if (outcome == PrecedenceOutcome.DisplacedOutright)
                    {
                        Add(displacedOutright, version.PolicyVersionId, subject);
                        continue;
                    }

                    if (!HandlerAccepts(version, accepted[version.PolicyVersionId]))
                    {
                        continue;
                    }

                    Add(subjectsByVersion, version.PolicyVersionId, subject);
                    if (outcome == PrecedenceOutcome.CapDisplaced)
                    {
                        Add(capDisplaced, version.PolicyVersionId, subject);
                    }
                }
            }
        }

        // The handler refusals follow the gate's, in the stable candidate order.
        refusals.AddRange(candidates.Where(c => handlerRefusals.ContainsKey(c.PolicyVersionId)).Select(c => handlerRefusals[c.PolicyVersionId]));

        // C8 (101 B2a): the Replace keys judged on each subject, at the highest rank that judged them (step 4's survivors). A refused version's
        // subject counts as covered, and is not reported as unchecked, only when a version of the SAME Replace key judged it at a strictly HIGHER
        // rank: a limit judged by the rule that would have won anyway. A tie does not cover (ontology D-205: ties all apply, so the refused
        // version's own limit was a second limit that nobody checked). For an Additive or Split key its own items were not checked, so its subjects
        // stay unchecked. A rate comparison skipped for the currency (step 5) judged nothing, so it covers nothing.
        var judgedReplace = new Dictionary<BillingSubject, Dictionary<(RuleType, string?, string), ProcedureRank>>();
        foreach (var (versionId, appliesTo) in subjectsByVersion)
        {
            var judgedDefinition = accepted[versionId];
            if ((IsRateComparison(judgedDefinition.RuleType) && currencySkip is not null)
                || BillingRuleCatalogue.PrecedenceOf(judgedDefinition.RuleType) != BillingRulePrecedence.Replace)
            {
                continue;
            }

            var rank = candidateById[versionId].Rank;
            foreach (var subject in appliesTo)
            {
                if (!judgedReplace.TryGetValue(subject, out var keys))
                {
                    judgedReplace[subject] = keys = [];
                }

                if (!keys.TryGetValue(judgedDefinition.PrecedenceKey, out var known) || known.CompareTo(rank) < 0)
                {
                    keys[judgedDefinition.PrecedenceKey] = rank;
                }
            }
        }

        // 5. Keep each applicable version's findings for the subjects it applies to.
        var skips = new List<BillingRuleSkip>();
        var findings = new List<(BillingFinding Finding, int Sequence)>();
        var rulesByVersion = new Dictionary<Guid, BillingRuleCandidate>();
        foreach (var candidate in candidates.Where(c => subjectsByVersion.ContainsKey(c.PolicyVersionId)))
        {
            var definition = accepted[candidate.PolicyVersionId];
            var applies = subjectsByVersion[candidate.PolicyVersionId];

            if (IsRateComparison(definition.RuleType) && currencySkip is not null)
            {
                Skip(candidate, definition.RuleType, currencySkip, skips);
                continue;
            }

            var judged = judgedByVersion[candidate.PolicyVersionId]!;
            var kept = judged.Where(f => applies.Contains(f.Subject));
            if (capDisplaced.TryGetValue(candidate.PolicyVersionId, out var capped))
            {
                // C6 Split (ExpenseRule, ontology F3-c, D-205 item 3): on a subject where a higher-ranked valid version of its key that states a
                // cap applies, a displaced version keeps its code check (the non-reimbursable codes add) and loses its cap (the cap replaces). A
                // capless higher version displaces nothing (D-205 ruling): a deliberate "no cap on this record" is a rule-level Exclude or a
                // record rule in the base's slot, not precedence. "Per category" has
                // no key today: maxAmountPerItem caps every expense line, whatever its code. It becomes per category only if the owner's six-gap
                // triage adds a per-code cap (D-205 item 3).
                kept = kept.Where(f => !(f.ReasonCode == BillingReasonCodes.ExpenseAmountOverCap && capped.Contains(f.Subject)));
            }
            if (definition.RuleType == RuleType.ExpenseRule && currencySkip is not null)
            {
                // D-20: the cap is a USD amount; the code check does not depend on the currency.
                kept = kept.Where(f => f.ReasonCode != BillingReasonCodes.ExpenseAmountOverCap);
                // A cap displaced on every subject it applies to (C6 Split) is not in force, so it was not skipped either.
                if (definition.MaxAmountPerItem is not null && !(capDisplaced.TryGetValue(candidate.PolicyVersionId, out var allDisplaced) && applies.All(allDisplaced.Contains)))
                {
                    Skip(candidate, definition.RuleType, currencySkip, skips);
                }
            }

            if (definition.RuleType == RuleType.AddedFee && definition.CapAmount is not null && currencySkip is not null)
            {
                // Task 115, D-20: an amount cap is a USD amount. A percent cap compares the invoice's own amounts and still runs.
                kept = kept.Where(f => f.ReasonCode != BillingReasonCodes.AddedFeeOverCap);
                Skip(candidate, definition.RuleType, currencySkip, skips);
            }

            foreach (var finding in kept)
            {
                findings.Add((finding, findings.Count));
                rulesByVersion[candidate.PolicyVersionId] = Effective(candidate);
            }
        }

        // 6. D-115: money that was not judged is visible to the reviewer, as ONE flag-only invoice-level finding on a skipped
        //    ApprovedRate version, else a skipped ExpenseRule one, citing every skipped version. Nothing skipped, no finding.
        if (currencySkip is not null && skips.Count > 0)
        {
            var notChecked = RatesNotChecked(request, loaded.ReliabilityThreshold, currencySkip, skips, candidates);
            findings.Add((notChecked.Finding, findings.Count));
            rulesByVersion[notChecked.Rule.PolicyVersionId] = Effective(notChecked.Rule);
        }

        var ordered = findings
            .OrderBy(f => f.Finding.Subject.Kind == BillingSubjectKind.Invoice ? 0 : 1)
            .ThenBy(f => f.Finding.Subject.LineIndex ?? -1)
            .ThenBy(f => BillingRuleCatalogue.CataloguePosition(f.Finding.RuleType))
            .ThenBy(f => f.Finding.PolicyCode, StringComparer.Ordinal)
            .ThenBy(f => f.Finding.PolicyId)
            .ThenBy(f => f.Finding.PolicyVersionId)
            .ThenBy(f => f.Sequence)
            .Select(f => f.Finding)
            .ToList();

        return new BillingEvaluationResult
        {
            Findings = ordered,
            RulesByVersion = rulesByVersion,
            Refusals = refusals,
            RefusalScopes = refusals.Select(r => RefusalScopeOf(r, candidates, selectedBySubject, accepted, judgedReplace)).ToList(),
            Skips = skips,
            Facts = facts,
            Gaps = gaps,
            ProcedureBasis = new BillingProcedureBasis(
                loaded.Procedures.Chain,
                onDate.OrderBy(d => d.Key).Select(d => d.Value).ToList(),
                candidates
                    .Where(c => subjectsByVersion.ContainsKey(c.PolicyVersionId) || displacedOutright.ContainsKey(c.PolicyVersionId))
                    .Select(c => new BillingRuleApplication(
                        c.PolicyVersionId, c.Source, SubjectsOf(subjectsByVersion, c.PolicyVersionId), SubjectsOf(displacedOutright, c.PolicyVersionId),
                        SubjectsOf(capDisplaced, c.PolicyVersionId)))
                    .ToList()),
        };

        // The invoice first, then by line index (the findings' order).
        static IReadOnlyList<BillingSubject> SubjectsOf(Dictionary<Guid, HashSet<BillingSubject>> bySubject, Guid versionId) =>
            bySubject.TryGetValue(versionId, out var set)
                ? set.OrderBy(s => s.Kind == BillingSubjectKind.Invoice ? 0 : 1).ThenBy(s => s.LineIndex ?? -1).ToList()
                : [];
    }

    /// <summary>
    /// A default <see cref="BillingEvaluationRequest.ReceivedOn"/> (0001-01-01, a request built without it) has no version in force,
    /// so every undated subject would silently be judged by nothing: a caller bug, refused before anything is read (072 part W).
    /// </summary>
    private static void RequireReceivedOn(BillingEvaluationRequest request)
    {
        if (request.ReceivedOn == default)
        {
            throw new ArgumentException(
                "BillingEvaluationRequest.ReceivedOn is the default date (0001-01-01); set it to the submission's received date (UTC).",
                nameof(request));
        }
    }

    /// <summary>
    /// The billing rule type of a version whose stated consequence that type may not carry (or that is none of the three defined
    /// values), else <c>null</c>: no consequence stated, a type the gate refuses anyway (unknown or not billing), or an allowed one.
    /// </summary>
    private static bool IsSystemRule(BillingRuleCandidate candidate) =>
        RuleBodySchemaValidator.TryParseRuleType(candidate.RuleType, out var type) && BillingRuleCatalogue.IsSystemType(type);

    /// <summary>
    /// Where a refused version's check did not run (101 B2a, F2; 082c C8). A subject is dropped only when a version of the same Replace
    /// <see cref="BillingRuleDefinition.PrecedenceKey"/> judged it at a strictly higher rank (the limit that would have won anyway was checked; a
    /// tie does not cover, ties all apply);
    /// a refused Additive or Split version keeps every subject, since its own items were not checked. The key of a handler-refused version is
    /// read from its accepted definition. A gate-refused version has no definition: a non-AddedFee type keys on the type alone, but an AddedFee
    /// one's fee kind and fee codes are in the body the gate refused, so its key is unreadable and the scope stays whole (fail closed), as it does
    /// for an unreadable rule type. Its class is never asked of it (C7): only a judged, gate-accepted version's key is in
    /// <paramref name="judgedReplace"/>. A scope left with no subject is kept (not dropped): no lines and not the invoice, so the consumer flags
    /// nothing for it.
    /// </summary>
    private static BillingRuleRefusalScope RefusalScopeOf(
        BillingRuleRefusal refusal, IReadOnlyList<BillingRuleCandidate> candidates,
        IReadOnlyList<(BillingSubject Subject, List<BillingRuleCandidate> Versions)> selectedBySubject,
        IReadOnlyDictionary<Guid, BillingRuleDefinition> accepted,
        IReadOnlyDictionary<BillingSubject, Dictionary<(RuleType, string?, string), ProcedureRank>> judgedReplace)
    {
        var candidate = candidates.First(c => c.PolicyVersionId == refusal.PolicyVersionId);
        var ruleType = RuleBodySchemaValidator.TryParseRuleType(candidate.RuleType, out var type) ? type : (RuleType?)null;
        (RuleType, string?, string)? key = accepted.TryGetValue(refusal.PolicyVersionId, out var definition)
            ? definition.PrecedenceKey
            : ruleType is { } readable && readable != RuleType.AddedFee ? (readable, null, string.Empty) : null;

        var subjects = selectedBySubject
            .Where(s => s.Versions.Contains(candidate)
                        && !(key is { } k && judgedReplace.TryGetValue(s.Subject, out var judged)
                             && judged.TryGetValue(k, out var judgedRank) && judgedRank.CompareTo(candidate.Rank) > 0))
            .Select(s => s.Subject)
            .ToList();
        return new BillingRuleRefusalScope(
            refusal.PolicyVersionId,
            ruleType,
            subjects.Any(s => s.Kind == BillingSubjectKind.Invoice),
            subjects.Where(s => s.LineIndex is not null).Select(s => s.LineIndex!.Value).Distinct().Order().ToList());
    }

    private static RuleType? DisallowedConsequenceType(BillingRuleCandidate candidate) =>
        candidate.Consequence is { } stated
        && RuleBodySchemaValidator.TryParseRuleType(candidate.RuleType, out var type)
        && RuleDomains.IsBilling(type)
        && !BillingRuleCatalogue.IsConsequenceAllowed(type, stated)
            ? type
            : null;

    /// <summary>
    /// The version of one policy that applies on <paramref name="date"/>: in force on it (current preferred, then the highest
    /// version number). <c>null</c> when none applies.
    /// </summary>
    internal static BillingRuleCandidate? SelectVersion(IReadOnlyList<BillingRuleCandidate> versions, DateOnly date) =>
        versions
            .Where(v => v.IsInForceOn(date))
            .OrderByDescending(v => v.IsCurrent)
            .ThenByDescending(v => v.VersionNumber)
            .ThenBy(v => v.PolicyVersionId)
            .FirstOrDefault();

    /// <summary>D-20: a type whose every check compares a billed rate with a rate-card rate (ApprovedRate; Discount, task 115). It is not
    /// run at all on an invoice whose currency cannot be judged.</summary>
    private static bool IsRateComparison(RuleType ruleType) => ruleType is RuleType.ApprovedRate or RuleType.Discount;

    /// <summary>
    /// D-20: why money cannot be judged on this invoice, or <c>null</c> when it can (USD, read reliably). USD is decided by the one
    /// shared normaliser (<see cref="BillingCurrency"/>, D-119: a bare <c>"$"</c>, <c>"US$"</c> or <c>"USD$"</c> is USD), which RequiredData
    /// uses too; the confidence is <c>HeaderConfidence["Currency"]</c> under exactly that key. Order: missing, then unclear (confidence
    /// unknown or below the floor, whatever the code says), then not USD.
    /// </summary>
    internal static string? CurrencySkipReason(BillingEvaluationRequest request, decimal reliabilityThreshold)
    {
        if (BillingCurrency.Normalise(request.Currency) is null)
        {
            return BillingEvaluationReasons.CurrencyMissing;
        }

        // The confidence is checked BEFORE the code (072 W review): a "EUR" read with doubt may be a misread "USD", so it is unclear,
        // not "not USD". Unknown confidence counts as below the floor, as it does for every extracted value (HandlerContext).
        if (!(request.HeaderConfidence.TryGetValue(BillingFieldNames.Currency, out var confidence) && confidence >= reliabilityThreshold))
        {
            return BillingEvaluationReasons.CurrencyUnreliable;
        }

        return BillingCurrency.IsUsd(request.Currency) ? null : BillingEvaluationReasons.CurrencyNotUsd;
    }

    /// <summary>
    /// D-115: the "rates and expense caps were not checked" finding. Its reason is the currency skip reason, which is flag-only
    /// (<see cref="BillingRuleCatalogue.IsFlagOnlyReason"/>): it never rejects or adjusts, whatever the version's consequence. It hangs
    /// on the first skipped ApprovedRate version (ordinal policy code, then version id), else on the first skipped ExpenseRule version,
    /// so the Signal group it lands in carries no other reason; it cites every skipped version as a reference value.
    /// It reads the currency (the extracted header field, with its confidence).
    /// </summary>
    private static (BillingFinding Finding, BillingRuleCandidate Rule) RatesNotChecked(
        BillingEvaluationRequest request, decimal reliabilityThreshold, string reason, IReadOnlyList<BillingRuleSkip> skips,
        IReadOnlyList<BillingRuleCandidate> candidates)
    {
        var skipped = skips
            .DistinctBy(s => s.PolicyVersionId)
            .OrderBy(s => s.PolicyCode, StringComparer.Ordinal)
            .ThenBy(s => s.PolicyVersionId)
            .ToList();

        // 072 part W (049 rule 5: never merged into a Reject): hang it on a skipped ApprovedRate version. ApprovedRate is not run at
        // all on such an invoice, so its invoice-level group holds this reason alone. Only when no ApprovedRate version was skipped
        // (none in force) does it fall back to an ExpenseRule version, whose handler gives no invoice-level reason either.
        var first = skipped.FirstOrDefault(s => s.RuleType == RuleType.ApprovedRate) ?? skipped[0];
        var rule = candidates.First(c => c.PolicyVersionId == first.PolicyVersionId);

        decimal? confidence = request.HeaderConfidence.TryGetValue(BillingFieldNames.Currency, out var c) ? c : null;
        var values = new List<BillingValueRead>
        {
            new()
            {
                Name = "invoice.currency",
                Value = string.IsNullOrWhiteSpace(request.Currency) ? null : request.Currency.Trim(),
                ExtractedField = new FieldRef(null, BillingFieldNames.Currency),
                Confidence = confidence,
                BelowReliabilityThreshold = confidence is not { } known || known < reliabilityThreshold,
            },
        };
        values.AddRange(skipped.Select(s => new BillingValueRead
        {
            Name = "skippedPolicyVersion",
            Value = s.PolicyCode + ":" + s.PolicyVersionId.ToString("D").ToLowerInvariant(),
        }));

        var finding = new BillingFinding
        {
            RuleType = first.RuleType,
            PolicyId = rule.PolicyId,
            PolicyCode = rule.PolicyCode,
            PolicyVersionId = rule.PolicyVersionId,
            ClauseReference = rule.ClauseReference,
            ClauseLocation = rule.ClauseLocation,
            Subject = BillingSubject.ForInvoice(),
            ReasonCode = reason,
            ValuesRead = values,
            FactValues = new Dictionary<string, string>(StringComparer.Ordinal),
        };
        return (finding, rule);
    }

    private void Skip(BillingRuleCandidate candidate, RuleType ruleType, string reason, List<BillingRuleSkip> skips)
    {
        _logger.LogWarning(
            "Billing evaluation: the money check of policy {PolicyCode} version {PolicyVersionId} ({RuleType}) is not judged: {ReasonCode} (D-20, R1 judges USD only). No rate or cap finding and no adjustment; the flag-only \"not checked\" finding says so (D-115).",
            candidate.PolicyCode, candidate.PolicyVersionId, ruleType, reason);
        skips.Add(new BillingRuleSkip(candidate.PolicyId, candidate.PolicyCode, candidate.PolicyVersionId, ruleType, reason));
    }
}
