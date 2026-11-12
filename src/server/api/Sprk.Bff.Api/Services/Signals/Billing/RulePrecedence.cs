namespace Sprk.Bff.Api.Services.Signals.Billing;

// ebilling-r1 task 084e (EB-D-219; ontology-r1 R1-R6, O-1 to O-7; notes/084-procedure-review-ui.md): the per-key computation of the billing
// evaluator's precedence step (step 4), extracted so the evaluator and the record's rules-in-force read run the SAME code.
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — InvoiceComplianceEvaluator step 4 held this loop inline; ProcedureResolver does no precedence; no record-level copy exists.
//   (2) Extension — an extraction, not a second implementation: the evaluator's step 4 calls it with its full Valid predicate, and
//       RecordProcedureReadService calls it with the record-level one. No rank loop exists anywhere else.
//   (3) Cost of doing nothing — the "Outranked by X" mark would need a second rank loop in the read service (R5 forbids it), or would not
//       exist and a billing admin could override a rule that never runs on the record.

/// <summary>How precedence treated one version of a precedence key (model 7.3).</summary>
public enum PrecedenceOutcome
{
    /// <summary>The version applies: it is at or above the top rank, or nothing displaces (Additive, or no version passes).</summary>
    Applies,

    /// <summary>Replace: a version of the key at a higher rank is valid, so this one never runs.</summary>
    DisplacedOutright,

    /// <summary>Split (ExpenseRule): a higher valid version that states a cap displaces this one's cap; its code check still runs.</summary>
    CapDisplaced,
}

/// <summary>
/// The outcome of <see cref="RulePrecedence.Resolve{T}"/> for one key. <see cref="Top"/>: the highest rank holding a displacing version, or null
/// (Additive, or no displacing version). <see cref="Outcomes"/>: every input version with its outcome, in INPUT order (a list, never a dictionary:
/// a version type with value equality may repeat, O-2). <see cref="Displacing"/>: every displacing version at <see cref="Top"/>, in input order
/// (R3: "by which rule" lists all of them; ties all apply).
/// </summary>
public sealed record PrecedenceResult<T>(ProcedureRank? Top, IReadOnlyList<(T Version, PrecedenceOutcome Outcome)> Outcomes, IReadOnlyList<T> Displacing);

/// <summary>
/// The precedence of ONE precedence key of ONE subject (model 7.3; 082c C5, C6; D-39, D-205 items 2 and 3; owner 2026-10-18; 072 review B2;
/// D-116). Pure: no I/O, no clock; it calls only the delegates it is given.
/// </summary>
public static class RulePrecedence
{
    /// <summary>
    /// Resolves one key. <paramref name="versions"/> MUST be one <see cref="BillingRuleDefinition.PrecedenceKey"/> group of one subject: grouping
    /// is the caller's job (O-4; a late-fee cap and an administrative-fee cap are different keys and never meet here).
    /// <para><b>Rule.</b> Replace (a limit): the versions ranked below the highest rank holding a DISPLACING version are displaced outright; a tie
    /// with that rank is not (ties all apply). Split (ExpenseRule): only a version that states a cap (<paramref name="statesCap"/>) can displace,
    /// and a lower version keeps its code check and loses its cap. Additive (a checklist): nothing is displaced. A version displaces only when it
    /// is <paramref name="valid"/>: a broken or downgraded specific rule never removes the general one. Ranks and ties are
    /// <see cref="ProcedureRank"/>'s own equality and <see cref="ProcedureRank.CompareTo"/> (O-3); only the rank decides, never the scope.</para>
    /// <para><b>Laziness (O-1).</b> <paramref name="valid"/> may have side effects (the evaluator's runs the handler, which logs, meters and records
    /// a refusal), so ranks are tried from the top and the walk stops at the first rank holding a displacing version. Neither
    /// <paramref name="statesCap"/> nor <paramref name="valid"/> is ever called on a version ranked below <see cref="PrecedenceResult{T}.Top"/>,
    /// and nothing is called under Additive. Under Split, <paramref name="valid"/> is not called on a version that states no cap. At the top rank
    /// EVERY version is asked (to list all of <see cref="PrecedenceResult{T}.Displacing"/>), where the inline step stopped at the first: those
    /// versions apply and are judged by the caller anyway, and the evaluator caches its answer, so the set of calls is unchanged; only their order
    /// relative to a lower Split version (judged later by the caller) can differ. Each version is asked at most once per call.</para>
    /// </summary>
    public static PrecedenceResult<T> Resolve<T>(
        BillingRulePrecedence precedence, IReadOnlyList<T> versions, Func<T, ProcedureRank> rankOf, Func<T, bool> statesCap, Func<T, bool> valid)
    {
        ArgumentNullException.ThrowIfNull(versions);
        ArgumentNullException.ThrowIfNull(rankOf);
        ArgumentNullException.ThrowIfNull(statesCap);
        ArgumentNullException.ThrowIfNull(valid);

        ProcedureRank? top = null;
        IReadOnlyList<T> displacing = [];
        if (precedence != BillingRulePrecedence.Additive)
        {
            // Under Split only a version that STATES a cap (MaxAmountPerItem) can displace a lower version's cap (D-205 ruling): a capless higher
            // version leaves the lower cap in force. A deliberate "no cap on this record" is a rule-level Exclude, or a record rule in the base's
            // slot, never precedence.
            bool Displaces(T v) => (precedence != BillingRulePrecedence.Split || statesCap(v)) && valid(v);
            foreach (var atRank in versions.GroupBy(rankOf).OrderByDescending(g => g.Key))
            {
                var found = atRank.Where(Displaces).ToList();
                if (found.Count > 0)
                {
                    top = atRank.Key;
                    displacing = found;
                    break;
                }
            }
        }

        var outcomes = new List<(T Version, PrecedenceOutcome Outcome)>(versions.Count);
        foreach (var version in versions)
        {
            // Below the top rank: Replace displaces it; Split keeps it for its code check and drops its cap. A tie with the top is not displaced.
            var below = top is { } t && rankOf(version).CompareTo(t) < 0;
            outcomes.Add((version, !below ? PrecedenceOutcome.Applies
                : precedence == BillingRulePrecedence.Replace ? PrecedenceOutcome.DisplacedOutright
                : PrecedenceOutcome.CapDisplaced));
        }

        return new PrecedenceResult<T>(top, outcomes, displacing);
    }
}
