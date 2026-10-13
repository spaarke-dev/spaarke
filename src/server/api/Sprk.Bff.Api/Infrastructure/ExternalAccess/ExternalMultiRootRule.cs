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
