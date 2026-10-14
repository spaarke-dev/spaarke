using System.Text.Json;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// THE rule for a row on the external plane that can be filed to more than one core root (project, matter, work
/// assignment): the caller reaches it through a root it can read (the route parent, or the dimension it matched), and in
/// addition must hold Read on every OTHER linked root that is <b>secure</b> (<c>sprk_issecure = true</c> on that root).
/// </summary>
/// <remarks>
/// <para><b>Owner decision 2026-10-12, option B "secure roots only"</b> (spaarke-SPA-external-access-platform-r3 task 039
/// notes section 12). It replaces the interim every-root rule (option A), which withheld a row filed to ANY ungranted
/// second root, secure or not. A secure matter's isolation is what the rule protects: a to-do under an invoice of secure
/// matter M and project P is stamped with both (<c>CoreAncestorResolver</c>), so without it a reader of P alone was given
/// M's to-do by <c>GET /projects/P/todos</c>. A linked root the caller cannot read that is NOT secure no longer hides the
/// row.</para>
/// <para><b>Fail closed.</b> A linked root whose secure state is not KNOWN to be false counts as secure: a flag read that
/// failed or threw, a record the read did not return (missing, or invisible to the app identity), an empty or masked
/// <c>sprk_issecure</c>, a lookup value that is not a GUID, and a root over the per-request lookup bound
/// (<see cref="MaxFlagLookups"/>). Only a successful read of <c>sprk_issecure = false</c> opens a row.</para>
/// <para><b>Data access.</b> A root the caller CAN read never needs a flag. The flags of the others are read APP-ONLY (the
/// caller's accessible set does not hold them, and the external plane has no OBO) by
/// <see cref="ExternalDataService.GetNonSecureRootsAsync"/>: one query per root entity per chunk of ids, over the DISTINCT
/// unreadable linked roots of the whole page or row set (<see cref="ReadFlagsAsync"/>), selecting only the id and
/// <c>sprk_issecure</c>; never one query per row. There is no cross-request cache, so a staff change to
/// <c>sprk_issecure</c> takes effect on the next request.</para>
/// <para><b>One implementation.</b> Every path calls <see cref="SecureRootsReadable(CallerPrincipal, SecureRootFlags, ValueTuple{ExternalGrantRootType, string}[])"/>
/// with flags from <see cref="ReadFlagsAsync"/>: the event, to-do and document lists, the single-document download and
/// versions routes, the message threads, the notification feed and the module grid's documents module.</para>
/// <para><b>Not covered.</b> The delete-own paths (task 032, <c>AuthorizeDeleteOwnAsync</c>) keep Read on EVERY parent
/// (FR-26); the invoice message thread stays any-parent (owner 2026-10-11). <c>sprk_regardingservicerequest</c> is not
/// consulted: a service request is reached through its requester and carries no secure flag.</para>
/// </remarks>
internal static class ExternalMultiRootRule
{
    /// <summary>
    /// The most distinct unreadable roots one request looks up (bounds the app-only reads to a few chunked queries per
    /// entity). A root past the bound is not looked up, so it counts as secure (fail closed) and its row is withheld.
    /// </summary>
    internal const int MaxFlagLookups = 500;

    /// <summary>
    /// Whether the caller may see a row filed to the given root lookups: every populated lookup must be a root the caller
    /// holds Read on, OR one <paramref name="flags"/> knows is NOT secure. A lookup that is null or blank is "not filed to
    /// this root" and is ignored; a row naming no root at all is unaffected; a value that is not a GUID is withheld.
    /// </summary>
    /// <param name="caller">The resolved principal.</param>
    /// <param name="flags">The secure flags read for this request (<see cref="ReadFlagsAsync"/>). <see cref="SecureRootFlags.None"/>
    /// knows nothing, so every unreadable root counts as secure.</param>
    /// <param name="roots">Each root lookup the row carries, as the raw <c>_..._value</c> GUID string.</param>
    public static bool SecureRootsReadable(
        CallerPrincipal caller, SecureRootFlags flags, params (ExternalGrantRootType Root, string? LookupValue)[] roots)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(flags);
        foreach (var (root, lookupValue) in roots)
        {
            if (string.IsNullOrWhiteSpace(lookupValue))
                continue; // not filed to this root
            if (!Guid.TryParse(lookupValue, out var rootId))
                return false; // malformed: its secure state cannot be known
            if (caller.RightsOn(root, rootId).HasFlag(AccessRights.Read))
                continue;
            if (!flags.IsKnownNotSecure(root, rootId))
                return false;
        }

        return true;
    }

    /// <summary>
    /// <see cref="SecureRootsReadable(CallerPrincipal, SecureRootFlags, ValueTuple{ExternalGrantRootType, string}[])"/> for
    /// the three stamp lookups every child row carries (<c>sprk_regardingproject</c>, <c>sprk_regardingmatter</c>,
    /// <c>sprk_regardingworkassignment</c>).
    /// </summary>
    public static bool SecureRootsReadable(
        CallerPrincipal caller, SecureRootFlags flags, string? project, string? matter, string? workAssignment) =>
        SecureRootsReadable(caller, flags, Stamps(project, matter, workAssignment));

    /// <summary>The three stamp lookups as rule input.</summary>
    public static (ExternalGrantRootType Root, string? LookupValue)[] Stamps(string? project, string? matter, string? workAssignment) =>
        new[]
        {
            (ExternalGrantRootType.Project, project),
            (ExternalGrantRootType.Matter, matter),
            (ExternalGrantRootType.WorkAssignment, workAssignment),
        };

    /// <summary>
    /// The linked roots of one row whose secure flag the rule needs: populated, a GUID, and NOT readable by the caller. A
    /// readable root never needs a lookup; a malformed one is withheld without one.
    /// </summary>
    internal static IEnumerable<(ExternalGrantRootType Root, Guid Id)> UnreadableRoots(
        CallerPrincipal caller, IEnumerable<(ExternalGrantRootType Root, string? LookupValue)> roots)
    {
        foreach (var (root, lookupValue) in roots)
        {
            if (!string.IsNullOrWhiteSpace(lookupValue)
                && Guid.TryParse(lookupValue, out var id)
                && id != Guid.Empty
                && !caller.RightsOn(root, id).HasFlag(AccessRights.Read))
            {
                yield return (root, id);
            }
        }
    }

    /// <summary>
    /// Reads, ONCE for a whole page or row set, the secure flags of the distinct linked roots the caller cannot read, and
    /// returns what the rule needs. No unreadable root means no read at all.
    /// </summary>
    /// <remarks>
    /// Fail closed: a reader that throws (other than the caller's own cancellation) yields <see cref="SecureRootFlags.None"/>,
    /// so every unreadable root counts as secure. Roots past <see cref="MaxFlagLookups"/> are not looked up (logged).
    /// </remarks>
    /// <param name="readNotSecure">The app-only flag read: answers which of the asked roots are KNOWN not secure
    /// (<see cref="ExternalDataService.GetNonSecureRootsAsync"/>).</param>
    /// <param name="caller">The resolved principal.</param>
    /// <param name="rowRoots">The root lookups of every row the request will decide on.</param>
    /// <param name="logger">Where a failed or bounded read is reported.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<SecureRootFlags> ReadFlagsAsync(
        NonSecureRootReader readNotSecure,
        CallerPrincipal caller,
        IEnumerable<IEnumerable<(ExternalGrantRootType Root, string? LookupValue)>> rowRoots,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(readNotSecure);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(rowRoots);

        var distinct = rowRoots.SelectMany(roots => UnreadableRoots(caller, roots)).Distinct().ToList();
        if (distinct.Count == 0)
            return SecureRootFlags.None;

        var complete = true;
        if (distinct.Count > MaxFlagLookups)
        {
            logger.LogWarning(
                "[EXT-MULTIROOT] {Count} distinct unreadable linked roots; looking up the first {Max}. The rest count as secure "
                + "(fail closed) and their rows are withheld.",
                distinct.Count, MaxFlagLookups);
            distinct = distinct.Take(MaxFlagLookups).ToList();
            complete = false;
        }

        try
        {
            var read = await readNotSecure(distinct, ct).ConfigureAwait(false);
            var asked = distinct.ToHashSet();
            // Only what was asked can be opened: an answer the reader made up for another id is ignored.
            return new SecureRootFlags(read.NotSecure.Where(asked.Contains), complete && read.Complete);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-MULTIROOT] The secure-flag read for {Count} linked root(s) failed. Failing CLOSED: every one counts as "
                + "secure, and rows filed to them are withheld.",
                distinct.Count);
            return SecureRootFlags.ReadFailed;
        }
    }

    // ── Task 187 (owner round 99, D-123; #1857): the LIVE parents of a child row ──────────────────────────────────────
    //
    // A child row's stamp (sprk_regardingproject / matter / workassignment) is a COPY. It goes stale when a parent is re-filed
    // outside the BFF, and a "carrier" row (its own pair names ordinary M; another lookup puts it under secure X) is never
    // restamped at all, while SecureChildReconciler has already moved it behind X (secure-if-any over every parent). So every
    // external child-row read also reads the row's parent lookups, resolves those parents' LIVE roots in one bounded batch
    // (ParentLineageWalk.TopsOfManyAsync over the ONE lineage map, ParentLineage.ChildFiling), and the rule above evaluates
    // {stamped roots} ∪ {live roots of every parent}: listed only when the caller holds Read on every SECURE root of that
    // union. A parent that cannot be resolved withholds the row (fail closed).

    /// <summary>
    /// The most distinct non-root parent records one request walks (task 187): the hard upper bound. Past it nothing more is
    /// read, and only a row whose filing needs an unread record is withheld (fail closed, logged, <c>Truncated</c> where the
    /// list has it).
    /// </summary>
    /// <remarks>
    /// Fix round 1 (R1): sized against the external list cap (<see cref="ExternalDataService.MaxCollectionRows"/>, 5,000 rows),
    /// four records per row, so a busy matter's full page (each to-do under its own communication, and that communication under
    /// its own thread: two records per row) never reaches it. The old bound of 500 emptied a page of 251 such to-dos. Cost at
    /// the bound: 20,000 / 200 ids per query = 100 filing queries, split further by table and level.
    /// </remarks>
    internal const int MaxLiveParentReads = 4 * ExternalDataService.MaxCollectionRows;

    /// <summary>
    /// The rule input standing for a parent whose live roots could not be resolved (a read fault, a parent that did not come
    /// back, the walk bound). Not a GUID, so <see cref="SecureRootsReadable(CallerPrincipal, SecureRootFlags, ValueTuple{ExternalGrantRootType, string}[])"/>
    /// withholds the row exactly as it withholds a malformed lookup: the secure state behind it cannot be known.
    /// </summary>
    internal const string UnresolvedParent = "unresolved-parent";

    /// <summary>
    /// The parent lookups of a child table whose live roots the rule takes in (task 187): every lineage lookup
    /// (<see cref="Sprk.Bff.Api.Services.Dataverse.ParentLineage.ChildFiling"/>, the map <c>SecureChildReconciler</c>'s
    /// secure-if-any follows) that targets a NON-root table. The root lookups are the row's own stamps or typed links, which
    /// every read already passes to the rule. Empty for a table with no lineage entry. Ordered by column.
    /// </summary>
    internal static IReadOnlyList<(string Column, string Table)> LiveParentLookupsOf(string childTable) =>
        Sprk.Bff.Api.Services.Dataverse.ParentLineage.ChildFiling.TryGetValue(childTable, out var lookups)
            ? lookups
                .Where(l => !Sprk.Bff.Api.Services.Access.SecureChildLineage.IsRoot(l.Value))
                .OrderBy(l => l.Key, StringComparer.Ordinal)
                .Select(l => (l.Key.ToLowerInvariant(), l.Value.ToLowerInvariant()))
                .ToArray()
            : Array.Empty<(string, string)>();

    /// <summary>The Web API <c>$select</c> columns (<c>_..._value</c>) of <see cref="LiveParentLookupsOf"/>.</summary>
    internal static IReadOnlyList<string> LiveParentSelect(string childTable) =>
        LiveParentLookupsOf(childTable).Select(l => $"_{l.Column}_value").ToArray();

    /// <summary>
    /// A Web API row's parent lookups (<see cref="LiveParentLookupsOf"/>), as raw values: absent or JSON null is "not filed
    /// there", a string is the lookup, and any other JSON value is malformed (withheld by the rule, fail closed).
    /// </summary>
    internal static LiveParentLookup[] LiveParentsOf(string childTable, IReadOnlyDictionary<string, JsonElement>? row) =>
        LiveParentsOf(childTable, column =>
        {
            if (row is null || !row.TryGetValue($"_{column}_value", out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : UnresolvedParent;
        });

    /// <summary>A row's parent lookups, each value read by <paramref name="valueOf"/> (by lookup LOGICAL name).</summary>
    internal static LiveParentLookup[] LiveParentsOf(string childTable, Func<string, string?> valueOf)
    {
        ArgumentNullException.ThrowIfNull(valueOf);
        return LiveParentLookupsOf(childTable)
            .Select(l => new LiveParentLookup(l.Table, valueOf(l.Column)))
            .Where(p => !string.IsNullOrWhiteSpace(p.LookupValue))
            .ToArray();
    }

    /// <summary>
    /// Resolves, ONCE for a whole page or row set, the live roots of the distinct parents its rows carry (task 187). No
    /// parent means no read at all.
    /// </summary>
    /// <remarks>
    /// Fail closed: no reader, a reader that throws (other than the caller's own cancellation), a fault inside the walk, and
    /// parents past <see cref="MaxLiveParentReads"/> leave those parents unresolved, so every row naming one is withheld
    /// (<see cref="ChildRowRoots.Of"/>). Logs carry counts only.
    /// </remarks>
    public static async Task<LiveParentRoots> ReadLiveParentsAsync(
        LiveParentRootReader? read,
        IEnumerable<IEnumerable<LiveParentLookup>> rowParents,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rowParents);
        var distinct = rowParents
            .SelectMany(parents => parents)
            .Select(p => Guid.TryParse(p.LookupValue, out var id) && id != Guid.Empty ? (Table: p.Table.ToLowerInvariant(), Id: id) : default)
            .Where(p => p.Id != Guid.Empty)
            .Distinct()
            .ToList();
        if (distinct.Count == 0)
            return LiveParentRoots.Empty;

        if (read is null)
        {
            logger.LogError(
                "[EXT-LIVEPARENT] No live-parent reader is configured; {Count} parent(s) cannot be resolved. Failing CLOSED: "
                + "every row filed under one is withheld.", distinct.Count);
            return LiveParentRoots.Unread;
        }

        var complete = true;
        if (distinct.Count > MaxLiveParentReads)
        {
            logger.LogWarning(
                "[EXT-LIVEPARENT] {Count} distinct parents; resolving the first {Max}. Rows filed under the rest are withheld "
                + "(fail closed).", distinct.Count, MaxLiveParentReads);
            distinct = distinct.Take(MaxLiveParentReads).ToList();
            complete = false;
        }

        try
        {
            var answer = await read(distinct, ct).ConfigureAwait(false);
            var asked = distinct.ToHashSet();
            // Only what was asked can be resolved: an answer the reader made up for another parent is ignored.
            var resolved = answer.Resolved
                .Where(kv => asked.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (resolved.Count < asked.Count)
            {
                complete = false;
                logger.LogWarning(
                    "[EXT-LIVEPARENT] {Unresolved} of {Count} parent(s) could not be resolved (faulted={Faulted}); rows filed "
                    + "under them are withheld (fail closed).", asked.Count - resolved.Count, asked.Count, answer.Faulted);
            }

            return new LiveParentRoots(resolved, complete && !answer.Faulted, answer.Faulted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-LIVEPARENT] The live-parent read for {Count} parent(s) failed. Failing CLOSED: every row filed under one "
                + "is withheld.", distinct.Count);
            return LiveParentRoots.Unread;
        }
    }

    /// <summary>
    /// THE decision for an external CHILD row (task 187): <see cref="SecureRootsReadable(CallerPrincipal, SecureRootFlags, ValueTuple{ExternalGrantRootType, string}[])"/>
    /// over <see cref="ChildRowRoots"/>, which carries the stamped roots AND the live roots of every parent. Child-row reads
    /// call this, never the stamp overloads (<c>ExternalChildRowLiveParentGuardTests</c>). A default (unbuilt) value is
    /// withheld.
    /// </summary>
    public static bool ChildRowReadable(CallerPrincipal caller, SecureRootFlags flags, ChildRowRoots row) =>
        row.IsBuilt && SecureRootsReadable(caller, flags, row.Links);
}

/// <summary>
/// Task 187: one parent lookup of a child row whose LIVE roots the multi-root rule takes in: the parent's table and the raw
/// <c>_..._value</c> (null or blank: not filed there; not a GUID: withheld).
/// </summary>
public readonly record struct LiveParentLookup(string Table, string? LookupValue);

/// <summary>
/// Task 187: the app-only live-parent read (<see cref="ExternalDataService.GetLiveParentRootsAsync"/>): for each asked parent
/// it could resolve, the roots at the top of its filing. A parent left out is unresolved (fail closed).
/// </summary>
public delegate Task<LiveParentRootsRead> LiveParentRootReader(
    IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct);

/// <summary>What <see cref="LiveParentRootReader"/> answers.</summary>
/// <param name="Resolved">Each resolved parent's live roots (possibly none: a parent filed under no root).</param>
/// <param name="Faulted"><c>true</c> when a read failed (as opposed to a parent that does not exist or the bound).</param>
public sealed record LiveParentRootsRead(
    IReadOnlyDictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>> Resolved,
    bool Faulted = false);

/// <summary>
/// Task 187: the live roots one request resolved for the parents of its rows (<see cref="ExternalMultiRootRule.ReadLiveParentsAsync"/>).
/// A parent not resolved counts as unknowable, and a row filed under it is withheld.
/// </summary>
public sealed class LiveParentRoots
{
    private readonly IReadOnlyDictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>> _resolved;

    internal LiveParentRoots(
        IReadOnlyDictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>> resolved,
        bool isComplete, bool faulted)
    {
        _resolved = resolved;
        IsComplete = isComplete;
        Faulted = faulted;
    }

    /// <summary>No parent was needed: nothing to resolve, and nothing missing.</summary>
    public static LiveParentRoots Empty { get; } = new(
        new Dictionary<(string, Guid), IReadOnlyList<(ExternalGrantRootType, Guid)>>(), isComplete: true, faulted: false);

    /// <summary>Nothing could be read (no reader, or the read failed): every parent is unresolved.</summary>
    internal static LiveParentRoots Unread { get; } = new(
        new Dictionary<(string, Guid), IReadOnlyList<(ExternalGrantRootType, Guid)>>(), isComplete: false, faulted: true);

    /// <summary><c>true</c> when every parent asked for was resolved (a withheld row was withheld for its roots, not for a
    /// read that could not decide). <c>false</c>: a list that withheld a row says <c>Truncated</c>.</summary>
    public bool IsComplete { get; }

    /// <summary><c>true</c> when a read failed (a single-row write route answers "unavailable" rather than "refused").</summary>
    public bool Faulted { get; }

    internal bool TryGetRoots((string Table, Guid Id) parent, out IReadOnlyList<(ExternalGrantRootType Root, Guid Id)> roots)
    {
        if (_resolved.TryGetValue((parent.Table.ToLowerInvariant(), parent.Id), out var found))
        {
            roots = found;
            return true;
        }

        roots = Array.Empty<(ExternalGrantRootType, Guid)>();
        return false;
    }
}

/// <summary>
/// Task 187: the multi-root rule's input for ONE external child row, built only by <see cref="Of"/>: its stamped (or typed)
/// root lookups ∪ the live roots of every parent lookup it carries, with an unresolvable parent entered as
/// <see cref="ExternalMultiRootRule.UnresolvedParent"/> (withheld). Enumerates as the rule input, so the one secure-flag read
/// (<see cref="ExternalMultiRootRule.ReadFlagsAsync"/>) covers the live roots too.
/// </summary>
public readonly struct ChildRowRoots : IEnumerable<(ExternalGrantRootType Root, string? LookupValue)>
{
    private readonly (ExternalGrantRootType Root, string? LookupValue)[]? _links;

    private ChildRowRoots((ExternalGrantRootType Root, string? LookupValue)[] links) => _links = links;

    /// <summary>Whether this value was built by <see cref="Of"/> (a default value is withheld).</summary>
    internal bool IsBuilt => _links is not null;

    /// <summary>The rule input: stamped roots, then the live roots.</summary>
    internal (ExternalGrantRootType Root, string? LookupValue)[] Links => _links ?? Array.Empty<(ExternalGrantRootType, string?)>();

    /// <summary>
    /// {<paramref name="stamped"/>} ∪ {live roots of every <paramref name="parents"/> entry}. A parent whose value is not a
    /// GUID, or that <paramref name="live"/> did not resolve, enters as <see cref="ExternalMultiRootRule.UnresolvedParent"/>.
    /// </summary>
    public static ChildRowRoots Of(
        IEnumerable<(ExternalGrantRootType Root, string? LookupValue)> stamped,
        IEnumerable<LiveParentLookup>? parents,
        LiveParentRoots live)
    {
        ArgumentNullException.ThrowIfNull(stamped);
        ArgumentNullException.ThrowIfNull(live);
        var links = stamped.ToList();
        foreach (var parent in parents ?? Array.Empty<LiveParentLookup>())
        {
            if (string.IsNullOrWhiteSpace(parent.LookupValue))
                continue; // not filed there
            if (!Guid.TryParse(parent.LookupValue, out var id) || id == Guid.Empty
                || !live.TryGetRoots((parent.Table, id), out var roots))
            {
                links.Add((ExternalGrantRootType.Project, ExternalMultiRootRule.UnresolvedParent));
                continue;
            }

            links.AddRange(roots.Select(r => (r.Root, (string?)r.Id.ToString("D"))));
        }

        return new ChildRowRoots(links.ToArray());
    }

    public IEnumerator<(ExternalGrantRootType Root, string? LookupValue)> GetEnumerator() =>
        ((IEnumerable<(ExternalGrantRootType Root, string? LookupValue)>)Links).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The app-only secure-flag read the multi-root rule needs: of <paramref name="roots"/>, the ones whose EFFECTIVE secure
/// status was read and is not secure. Anything not returned counts as secure.
/// </summary>
public delegate Task<NonSecureRootsRead> NonSecureRootReader(
    IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)> roots, CancellationToken ct);

/// <summary>
/// What <see cref="NonSecureRootReader"/> answers: the roots KNOWN not to be secure, and whether the read COMPLETED.
/// </summary>
/// <param name="NotSecure">The roots read and found not (effectively) secure.</param>
/// <param name="Complete"><c>false</c> when a flag chunk, page or filing walk failed or could not decide, so a root may
/// count as secure only because it could not be read. The list readers use it to say a list may be missing rows
/// (<c>Truncated</c>); the rule itself is unchanged (unknown is secure).</param>
public sealed record NonSecureRootsRead(IReadOnlySet<(ExternalGrantRootType Root, Guid Id)> NotSecure, bool Complete = true);

/// <summary>
/// The secure flags one request read for the multi-root rule (<see cref="ExternalMultiRootRule.ReadFlagsAsync"/>): the
/// linked roots KNOWN not to be secure. Every other root counts as secure (fail closed).
/// </summary>
public sealed class SecureRootFlags
{
    private readonly HashSet<(ExternalGrantRootType Root, Guid Id)> _notSecure;

    internal SecureRootFlags(IEnumerable<(ExternalGrantRootType Root, Guid Id)> notSecure, bool isComplete = true)
    {
        _notSecure = new HashSet<(ExternalGrantRootType Root, Guid Id)>(notSecure);
        IsComplete = isComplete;
    }

    /// <summary>Nothing is known: every root the caller cannot read counts as secure.</summary>
    public static SecureRootFlags None { get; } = new(Array.Empty<(ExternalGrantRootType, Guid)>());

    /// <summary>The read itself failed or was cut short: nothing is known, and a withheld row may be withheld only because
    /// of that (<see cref="IsComplete"/> is <c>false</c>).</summary>
    internal static SecureRootFlags ReadFailed { get; } = new(Array.Empty<(ExternalGrantRootType, Guid)>(), isComplete: false);

    /// <summary><c>true</c> when every flag chunk and filing walk the request needed succeeded (or none was needed). When
    /// <c>false</c> a root may count as secure only because it could not be read, so a list that withheld a row says
    /// <c>Truncated</c>.</summary>
    public bool IsComplete { get; }

    /// <summary>Whether <paramref name="id"/> was read and found NOT secure.</summary>
    public bool IsKnownNotSecure(ExternalGrantRootType root, Guid id) => _notSecure.Contains((root, id));
}
