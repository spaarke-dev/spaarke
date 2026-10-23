using System.Globalization;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// D-176 (#2118, ebilling-r1 task 119): which stored Duplicate findings' earlier-invoice detail the CALLER may see, for every BFF route that
/// returns the stored <c>sprk_findingsjson</c> findings: the Invoice Review read model (<see cref="InvoiceReviewReadService"/>,
/// <c>GET /api/finance/invoices/{id}/review</c>) and the blocked-submission view (<see cref="SubmissionOverrideService.GetAsync"/>,
/// <c>GET /api/finance/submissions/{id}/blocked</c>). One helper, so the two routes cannot diverge on which findings are Duplicates, how
/// their priors are referenced, how findings are grouped, or what a fault does.
/// </summary>
/// <remarks>
/// <para><b>The rule</b> is <see cref="EarlierInvoiceReadability.ReferencesOf"/> (what a finding is judged by, #2294 / DEF-043) and
/// <see cref="EarlierInvoiceReadability.ShownAsync{TKey}"/> (ontology task 157), both shared with the worklist's <see cref="SignalReadService"/>:
/// a group is shown only when it has at least one reference and the caller's own read returns every one. This helper only builds the
/// references and the group keys, makes the one call per response with the caller's client, and fails closed.</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <see cref="EarlierInvoiceReadability"/> owns the readability rule
/// and the reads; this adds none. Extension: that class is ontology-owned and speaks Signals and references, not the stored findings JSON's
/// (policy, subject, line) groups. Cost of doing nothing: the blocked view keeps returning the earlier tracking number and invoice id to a
/// caller who cannot read that invoice (F1 of 119's second review), or the two routes carry two copies of this logic that can drift.</para>
/// </remarks>
internal static class DuplicateDetailGate
{
    /// <summary><c>valuesRead</c> name of the earlier invoice's id (ebilling's <c>DuplicateHandler</c>, #2118).</summary>
    internal const string DuplicateOfInvoiceIdValue = "duplicateOf.invoiceId";

    /// <summary><c>valuesRead</c> name of the earlier submission's tracking number (ebilling's <c>DuplicateHandler</c>).</summary>
    internal const string DuplicateOfTrackingNumberValue = "duplicateOf.trackingNumber";

    /// <summary>A (policy, subject, line) group of the stored findings: the unit the persister scrubs (D-112) and the Signal plan words.</summary>
    internal readonly record struct FindingGroup(string? PolicyCode, string? Subject, int? Line);

    internal static readonly IReadOnlySet<FindingGroup> NoGroups = new HashSet<FindingGroup>();

    /// <summary>
    /// D-176: the groups holding a Duplicate whose earlier invoice the caller may not see. One
    /// <see cref="EarlierInvoiceReadability.ShownAsync{TKey}"/> call for the whole response, with the caller's client (OBO, never app-only;
    /// ADR-028); it owns the rule (at least one reference, every one readable; nothing shown on a fault). Any exception other than the
    /// request's cancellation withholds every Duplicate group (fail closed) and is logged at Warning by type only, naming no earlier-invoice
    /// detail: the response never fails because of this check.
    /// </summary>
    /// <param name="findings">The stored findings of the response.</param>
    /// <param name="groupOf">A finding's (policy, subject, line) group.</param>
    /// <param name="valuesOf">A finding's values read, as (name, value).</param>
    /// <param name="reasonCodeOf">A finding's reason code: a same-invoice repeat is judged by <paramref name="currentInvoiceId"/>.</param>
    /// <param name="currentInvoiceId">The invoice the route reads (the review's invoice, the blocked submission's invoice): a same-invoice
    /// repeat's earlier invoice. No tracking number is ever looked up.</param>
    /// <param name="readability">The shared rule.</param>
    /// <param name="caller">The CALLER's Dataverse client.</param>
    /// <param name="logger">The calling service's logger.</param>
    /// <param name="surface">The route's name in the log line (for example "Invoice review").</param>
    /// <param name="ct">The request's cancellation; it propagates.</param>
    internal static async Task<IReadOnlySet<FindingGroup>> WithheldGroupsAsync<TFinding>(
        IEnumerable<TFinding> findings,
        Func<TFinding, FindingGroup> groupOf,
        Func<TFinding, IEnumerable<(string? Name, string? Value)>> valuesOf,
        Func<TFinding, string?> reasonCodeOf,
        Guid? currentInvoiceId,
        EarlierInvoiceReadability readability,
        IDataverseUserClient caller,
        ILogger logger,
        string surface,
        CancellationToken ct)
    {
        var referencesOf = References(findings, groupOf, valuesOf, reasonCodeOf, currentInvoiceId);
        if (referencesOf.Count == 0)
        {
            return NoGroups;
        }

        IReadOnlySet<FindingGroup> shown;
        try
        {
            shown = await readability.ShownAsync(caller, referencesOf, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "{Surface}: the earlier-invoice check faulted ({ExceptionType}); every Duplicate's earlier-invoice detail is withheld (D-176, fail closed).",
                surface, ex.GetType().Name);
            shown = NoGroups;
        }

        var withheld = referencesOf.Keys.Where(group => !shown.Contains(group)).ToHashSet();
        if (withheld.Count > 0)
        {
            logger.LogInformation(
                "{Surface}: the earlier-invoice detail of {Count} Duplicate group(s) is withheld (an earlier invoice the caller cannot read, an unresolved number, or a fault; D-176).",
                surface, withheld.Count);
        }

        return withheld;
    }

    /// <summary>
    /// The references (priors) of every group that holds a Duplicate: a finding whose values read name <c>duplicateOf.invoiceId</c> or
    /// <c>duplicateOf.trackingNumber</c> (names compared without regard to case, as the Signal read does). ONLY such groups are keyed: a group
    /// with no reference is withheld by the rule. Each finding's references are <see cref="EarlierInvoiceReadability.ReferencesOf"/>'s: its
    /// <c>duplicateOf.invoiceId</c> ids (the number is then ignored); else, for a same-invoice repeat, <paramref name="currentInvoiceId"/>;
    /// else (#2294: identified only by a tracking number, or by nothing) a reference that identifies nothing, so its group is withheld with
    /// the generic sentence. A finding withheld at write time carries no <c>duplicateOf.*</c> value and is not keyed: it is returned as stored.
    /// </summary>
    internal static Dictionary<FindingGroup, IReadOnlyCollection<EarlierInvoiceReadability.Reference>> References<TFinding>(
        IEnumerable<TFinding> findings,
        Func<TFinding, FindingGroup> groupOf,
        Func<TFinding, IEnumerable<(string? Name, string? Value)>> valuesOf,
        Func<TFinding, string?> reasonCodeOf,
        Guid? currentInvoiceId)
    {
        var groups = new Dictionary<FindingGroup, List<EarlierInvoiceReadability.Reference>>();
        foreach (var finding in findings)
        {
            var duplicate = false;
            var ids = new List<Guid>();
            foreach (var (name, value) in valuesOf(finding))
            {
                if (string.Equals(name, DuplicateOfInvoiceIdValue, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    if (Guid.TryParse(value, CultureInfo.InvariantCulture, out var id) && id != Guid.Empty)
                    {
                        ids.Add(id);
                    }
                }
                else if (string.Equals(name, DuplicateOfTrackingNumberValue, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                }
            }

            if (!duplicate)
            {
                continue;
            }

            var group = groupOf(finding);
            if (!groups.TryGetValue(group, out var references))
            {
                groups[group] = references = [];
            }

            references.AddRange(EarlierInvoiceReadability.ReferencesOf(
                ids, EarlierInvoiceReadability.IsSameInvoiceRepeat(reasonCodeOf(finding)), currentInvoiceId));
        }

        return groups.ToDictionary(g => g.Key, g => (IReadOnlyCollection<EarlierInvoiceReadability.Reference>)g.Value);
    }
}
