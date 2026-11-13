namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 191h (#2179 design v2 §3.3 "Callers", §3.10; #2189; #2369 part 2): THE external visibility
/// predicate (<see cref="ExternalVisibility"/>, task 191b) as a hide decision over the BFF's app-only readers, for the surfaces
/// that read as someone other than the caller or as the caller's own token: the external SPA's to-do and event lists
/// (<see cref="ExternalDataService"/>), the assistant's <c>dataverse.*</c> read tools and the AI retrieval trim
/// (<c>RetrievalAccessTrim</c>). Bound once, in <c>AddExternalAccess</c>, over task 191c's <c>ExternalVisibilityDecider</c>.
/// </summary>
/// <remarks>
/// <para><b>A hide decision only</b> (CLAUDE.md §6.5 path B for the <c>dataverse.*</c> tools, owner round 111, the same shape as
/// <see cref="AppOnlyDocumentNarrowing"/>): it answers which of the asked rows are externally visible and nothing else. No
/// app-only row or column reaches a caller; every row a surface returns still comes from that surface's own read. Its consumers
/// are an allow-list (<c>ExternalDocumentNarrowingGuardTests</c>, K7).</para>
/// <para><b>Fail closed.</b> Only <c>Visible</c> shows a row: Hidden and Undecided (a closure that reaches no root, a chain too
/// long, a row that does not come back) do not, and a row not asked is not visible. A read fault never throws out of the
/// decision: every row it could not decide is Undecided and <see cref="ExternalRowVisibility.Faulted"/> is set (a row a sound
/// rule already hid stays Hidden). Cancellation propagates.</para>
/// <para><b>Component Justification (CLAUDE.md §11):</b> (1) Existing — the predicate itself (191b) and its decider over the
/// app-only readers (191c's <c>ExternalVisibilityDecider</c>), which this binds as it is. (2) Extension — the decider and its
/// result are internal types, and the tool handlers, the retrieval trim and <see cref="ExternalDataService"/> are public
/// classes resolved by public constructors, so they cannot take it; a public delegate with a public answer is the narrowest
/// shape that reaches them without making the predicate's internals public. (3) Cost of doing nothing — the SPA lists, the
/// assistant's tools and AI retrieval keep re-deriving "internal-only" per surface, and a to-do, event, memo or analysis about
/// an internal-only communication, a hidden document or a Restricted root reaches an external caller (#2189, #2369 part 2).</para>
/// </remarks>
public delegate Task<ExternalRowVisibility> AppOnlyExternalVisibility(
    IReadOnlyCollection<(string Table, Guid Id)> rows, CancellationToken ct);

/// <summary>Task 191h: the answer of <see cref="AppOnlyExternalVisibility"/> for one batch of rows.</summary>
public sealed class ExternalRowVisibility
{
    private readonly ExternalVisibilityResult _result;

    internal ExternalRowVisibility(ExternalVisibilityResult result) =>
        _result = result ?? throw new ArgumentNullException(nameof(result));

    /// <summary>A read failed: no row the decision could not settle is visible.</summary>
    public bool Faulted => _result.Faulted;

    /// <summary>Whether the row is externally visible: only a <c>Visible</c> verdict is; a row not asked is not.</summary>
    public bool IsVisible(string table, Guid id) => _result.IsVisible(table, id);

    /// <summary>Whether the row is not visible because it could not be decided (Undecided: a fault, a closure past a bound,
    /// a row that does not come back, a closure that reaches no root), rather than because a rule hides it.</summary>
    public bool IsUndecided(string table, Guid id) => _result.For(table, id).Verdict == ExternalVisibilityVerdict.Undecided;

    /// <summary>uac-r2 task 191g: whether a not-visible row is not visible only for now — the decision faulted, or the row is
    /// Undecided for a reason a later read may answer (task 191c's transient set: a failed read, a row not found this time, an
    /// undecided parent, ...) — rather than because a rule hides it or its filing is structurally unresolvable. A caller that
    /// REVOKES on "not visible" keeps on this answer and retries (round 70: remove only on a positive answer).</summary>
    public bool MayAnswerLater(string table, Guid id) =>
        Faulted || (_result.For(table, id) is { Verdict: ExternalVisibilityVerdict.Undecided } decision
                    && Sprk.Bff.Api.Services.Access.ExternalTeamShareSynchronizer.IsTransient(decision.Reason));

    /// <summary>
    /// uac-r2 task 197b: whether the row is not visible ONLY because its closure reaches no root
    /// (<see cref="ExternalVisibilityReason.NoRoot"/>): no rule hides it, nothing it reached is undecidable, and the decision did not
    /// fault. The predicate reports <c>NoRoot</c> only when every finding of the row's closure is Visible
    /// (<c>ExternalVisibility.Run.Combine</c>), so this is never <c>true</c> for a Hidden row or for any other Undecided reason.
    /// <para>It is NOT "visible". Every caller keeps it hidden except one bounded by the owner (uac-r2 197b, design §5): the
    /// participants of a message positively owned by the Direct-message owner team, which is by definition filed under no record
    /// (<c>DirectMessageParticipantShares.DecideForDirectParticipantsAsync</c>).</para>
    /// </summary>
    public bool UndecidedOnlyForNoRoot(string table, Guid id) =>
        !Faulted
        && _result.For(table, id) is { Verdict: ExternalVisibilityVerdict.Undecided, Reason: ExternalVisibilityReason.NoRoot };

    /// <summary>The not-visible count per reason code for <paramref name="rows"/> (one log line; codes and counts only, ADR-015).</summary>
    public string Summary(IEnumerable<(string Table, Guid Id)> rows) =>
        ExternalVisibilityResult.Summary(rows.Select(r => _result.For(r.Table, r.Id)));

    /// <summary>A decision over <paramref name="rows"/> in which nothing was asked: every row reads not visible (fail closed).</summary>
    internal static ExternalRowVisibility NothingDecided { get; } =
        new(new ExternalVisibilityResult(new Dictionary<ExternalVisibility.Node, ExternalVisibilityDecision>(), faulted: true));
}
