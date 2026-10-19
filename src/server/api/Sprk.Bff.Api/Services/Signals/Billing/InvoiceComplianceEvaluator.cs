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
/// broken firm rule never silently removes a check.</para>
/// <para><b>Refusals (FR-24).</b> A version the gate refuses (logged and metered by the gate) or whose handler refuses it (logged
/// here as <see cref="OntologyWriterEvents.PolicyVersionInvalid"/> and metered) produces no finding; the other versions still run.
/// Only <see cref="BillingRuleRefusedException"/> is caught; any other exception is a defect and propagates.</para>
/// <para><b>Currency (D-20, D-115).</b> R1 judges money in USD only (<c>"$"</c> is read as USD). When the currency is missing, is
/// not USD, or its confidence (<c>HeaderConfidence["Currency"]</c>) is unknown or below the reliability threshold, ApprovedRate is
/// not run and ExpenseRule's amount-cap findings are dropped (its code check still runs); each such skip is logged with its reason
/// and listed in <see cref="BillingEvaluationResult.Skips"/>. When anything was skipped, ONE invoice-level finding says so (D-115:
/// "Rates and expense caps were not checked: the invoice currency is missing / unclear / not USD"), whether or not a RequiredData
/// rule is in force. Its reason (<see cref="BillingEvaluationReasons.CurrencyMissing"/>, <see cref="BillingEvaluationReasons.CurrencyUnreliable"/>,
/// <see cref="BillingEvaluationReasons.CurrencyNotUsd"/>) is flag-only: it never rejects or adjusts. It hangs on the first skipped
/// money policy (ordinal policy code, then version id) and cites every skipped version. With no money rule in force nothing is
/// skipped and no such finding is raised.</para>
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
        var loaded = await _loader.LoadAsync(request, ct).ConfigureAwait(false);
        return Evaluate(request, loaded);
    }

    /// <summary>The in-memory evaluation: no I/O, no clock. Internal so tests can drive it with loaded facts directly.</summary>
    internal BillingEvaluationResult Evaluate(BillingEvaluationRequest request, BillingLoadedFacts loaded)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(loaded);

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
        var candidates = loaded.Rules
            .Where(c => c.CanBeInForce)
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
        foreach (var candidate in candidates.Where(c => selectedBySubject.Any(s => s.Versions.Contains(c))))
        {
            // Task 073 (FR-26, I-5, D-89): the stored consequence goes through the gate too, so a row authored directly in Dataverse
            // with a consequence its type may not carry (Reject on an AiObservation rule) is refused here, not only at publish.
            var snapshot = new PolicyVersionSnapshot(
                candidate.PolicyVersionId, candidate.PolicyCode, candidate.RuleType, candidate.RuleBody, candidate.MessageTemplate,
                candidate.Consequence);
            if (_gate.TryPrepareBillingRule(snapshot, out var definition))
            {
                accepted[candidate.PolicyVersionId] = definition;
            }
            else
            {
                // The gate has logged (EventId 50301) and metered the refusal.
                refusals.Add(new BillingRuleRefusal(candidate.PolicyId, candidate.PolicyCode, candidate.PolicyVersionId, BillingEvaluationReasons.GateRefused));
            }
        }

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

            if (definition.RuleType == RuleType.ApprovedRate && currencySkip is not null)
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
        //    a firm version that cannot be evaluated leaves the client default in force).
        var subjectsByVersion = new Dictionary<Guid, HashSet<BillingSubject>>();
        foreach (var (subject, versions) in selectedBySubject)
        {
            var firmTypes = versions
                .Where(v => v.Scope == RuleSetScope.Firm
                            && accepted.TryGetValue(v.PolicyVersionId, out var firmDefinition)
                            && HandlerAccepts(v, firmDefinition))
                .Select(v => accepted[v.PolicyVersionId].RuleType)
                .ToHashSet();
            foreach (var version in versions)
            {
                if (!accepted.TryGetValue(version.PolicyVersionId, out var definition)
                    || (version.Scope != RuleSetScope.Firm && firmTypes.Contains(definition.RuleType))
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

            if (definition.RuleType == RuleType.ApprovedRate && currencySkip is not null)
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

            foreach (var finding in kept)
            {
                findings.Add((finding, findings.Count));
                rulesByVersion[candidate.PolicyVersionId] = candidate;
            }
        }

        // 6. D-115: money that was not judged is visible to the reviewer, as ONE flag-only invoice-level finding on the first skipped
        //    money policy (ordinal policy code, then version id), citing every skipped version. Nothing skipped, no finding.
        if (currencySkip is not null && skips.Count > 0)
        {
            var notChecked = RatesNotChecked(request, loaded.ReliabilityThreshold, currencySkip, skips, candidates);
            findings.Add((notChecked.Finding, findings.Count));
            rulesByVersion[notChecked.Rule.PolicyVersionId] = notChecked.Rule;
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

    /// <summary>
    /// D-20: why money cannot be judged on this invoice, or <c>null</c> when it can (USD, read reliably). USD is decided by the one
    /// shared normaliser (<see cref="BillingCurrency"/>, D-119: a bare <c>"$"</c> is USD), which RequiredData uses too; the
    /// confidence is <c>HeaderConfidence["Currency"]</c> under exactly that key.
    /// </summary>
    internal static string? CurrencySkipReason(BillingEvaluationRequest request, decimal reliabilityThreshold)
    {
        if (BillingCurrency.Normalise(request.Currency) is null)
        {
            return BillingEvaluationReasons.CurrencyMissing;
        }

        if (!BillingCurrency.IsUsd(request.Currency))
        {
            return BillingEvaluationReasons.CurrencyNotUsd;
        }

        // Unknown confidence counts as below the floor, as it does for every extracted value (HandlerContext).
        return request.HeaderConfidence.TryGetValue(BillingFieldNames.Currency, out var confidence) && confidence >= reliabilityThreshold
            ? null
            : BillingEvaluationReasons.CurrencyUnreliable;
    }

    /// <summary>
    /// D-115: the "rates and expense caps were not checked" finding. Its reason is the currency skip reason, which is flag-only
    /// (<see cref="BillingRuleCatalogue.IsFlagOnlyReason"/>): it never rejects or adjusts, whatever the version's consequence. It hangs
    /// on the first skipped money policy (ordinal policy code, then version id) and cites every skipped version as a reference value.
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
        var first = skipped[0];
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
