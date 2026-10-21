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

    /// <summary>Money checks not judged because of the currency (D-20), in evaluation order. Each is logged.</summary>
    public required IReadOnlyList<BillingRuleSkip> Skips { get; init; }

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
/// <para><b>Firm over client default (D-39).</b> For one subject, when a firm rule set's version and a client default's version of
/// the same rule type both apply, only the firm's is evaluated. Only a firm version that both the gate and its handler accept counts
/// for this: when the firm version cannot be evaluated, the client default still applies (owner, 2026-10-18; 072 review B2), so a
/// broken firm rule never silently removes a check. "The same rule type" means the same <see cref="BillingRuleDefinition.PrecedenceKey"/>: the
/// rule type, and for AddedFee the rule type plus its fee kind (owner, 2026-10-21), so a firm late-fee cap leaves the client default's
/// administrative-fee cap in force.</para>
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
        var candidates = loaded.Rules
            .Where(c => c.CanBeInForce && !IsSystemRule(c))
            .OrderBy(c => c.PolicyCode, StringComparer.Ordinal)
            .ThenBy(c => c.PolicyId)
            .ThenBy(c => c.VersionNumber)
            .ThenBy(c => c.PolicyVersionId)
            .ToList();
        var byPolicy = candidates.GroupBy(c => c.PolicyId).Select(g => g.ToList()).ToList();

        // 1. The version of each policy that applies to each subject.
        // A3: with no line date and no invoice date, the received date (data, never the clock) is the judging date.
        var invoiceDate = request.InvoiceDate ?? request.ReceivedOn;
        var subjects = new List<(BillingSubject Subject, DateOnly Date)> { (BillingSubject.ForInvoice(), invoiceDate) };
        subjects.AddRange(request.Lines.OrderBy(l => l.Index).Select(l => (BillingSubject.ForLine(l.Index), l.Date ?? invoiceDate)));
        var selectedBySubject = subjects
            .Select(s => (s.Subject, Versions: byPolicy.Select(versions => SelectVersion(versions, s.Date)).OfType<BillingRuleCandidate>().ToList()))
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

        // 3. Each version's handler, at most once, on the whole fact set, and only when it is needed: a firm version's to decide
        //    precedence, any other's when it applies. A handler refusal is logged and metered here (FR-24). ApprovedRate on a
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

        // 4. D-39 precedence per subject, among the versions both the gate and the handler accept (072 review B2, owner 2026-10-18:
        //    a firm version that cannot be evaluated leaves the client default in force). A downgraded firm version (its stored
        //    consequence is one its type may not carry, evaluated as Flag) is not a valid firm rule for this purpose either (072 W
        //    review F1-b, D-116): it still runs, as a Flag, but the client default of its type stays in force beside it, so a
        //    client Reject is never turned into a Flag by a row written around publish.
        var subjectsByVersion = new Dictionary<Guid, HashSet<BillingSubject>>();
        foreach (var (subject, versions) in selectedBySubject)
        {
            // The replacement key is the rule type, and for AddedFee the rule type plus its fee kind (owner, 2026-10-21;
            // BillingRuleDefinition.PrecedenceKey): a firm late-fee cap displaces the client default's late-fee cap only. For the
            // kind 'other' the key also carries the fee-code set (ontology-r1 F3): a courier cap never displaces a postage cap.
            var firmKeys = versions
                .Where(v => v.Scope == RuleSetScope.Firm
                            && !downgraded.ContainsKey(v.PolicyVersionId)
                            && accepted.TryGetValue(v.PolicyVersionId, out var firmDefinition)
                            && HandlerAccepts(v, firmDefinition))
                .Select(v => accepted[v.PolicyVersionId].PrecedenceKey)
                .ToHashSet();
            foreach (var version in versions)
            {
                if (!accepted.TryGetValue(version.PolicyVersionId, out var definition)
                    || (version.Scope != RuleSetScope.Firm && firmKeys.Contains(definition.PrecedenceKey))
                    || !HandlerAccepts(version, definition))
                {
                    continue;
                }

                if (!subjectsByVersion.TryGetValue(version.PolicyVersionId, out var set))
                {
                    subjectsByVersion[version.PolicyVersionId] = set = [];
                }

                set.Add(subject);
            }
        }

        // The handler refusals follow the gate's, in the stable candidate order.
        refusals.AddRange(candidates.Where(c => handlerRefusals.ContainsKey(c.PolicyVersionId)).Select(c => handlerRefusals[c.PolicyVersionId]));

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
            if (definition.RuleType == RuleType.ExpenseRule && currencySkip is not null)
            {
                // D-20: the cap is a USD amount; the code check does not depend on the currency.
                kept = kept.Where(f => f.ReasonCode != BillingReasonCodes.ExpenseAmountOverCap);
                if (definition.MaxAmountPerItem is not null)
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
            Skips = skips,
            Facts = facts,
        };
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
