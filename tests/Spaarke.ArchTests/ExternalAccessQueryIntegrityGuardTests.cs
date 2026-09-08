using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 025 — H6 seams 3 and 4: two load-bearing behaviours that no test
/// executed, pinned as fitness functions because both are transport-bound.
/// </summary>
/// <remarks>
/// <para><b>Why structural rather than behavioural.</b> ADR-038 bans <c>Mock&lt;HttpMessageHandler&gt;</c>
/// (B1), and both of these live behind an HTTP call: the grant query is a raw <c>$filter</c> string sent
/// by <c>ExternalParticipationService</c>'s own <c>HttpClient</c>, and the member list is a Graph SDK
/// fluent chain. Their existing tests assert the pure builders and the caller's reaction — which is the
/// right level for those things, and is exactly why the CALL SITES stayed invisible. A source-level
/// invariant is the honest instrument for "the call site must keep using the builder" and "this call
/// must not swallow its error"; the alternative was a transport mock the ADR forbids, or nothing, which
/// is what there was.</para>
///
/// <para><b>Both rules were perturbation-checked</b> against the exact one-line breaks the 2026-08-24
/// review specified, each of which failed ZERO tests before this file existed.</para>
/// </remarks>
public class ExternalAccessQueryIntegrityGuardTests
{
    private const string ParticipationServiceRelativePath =
        "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalParticipationService.cs";

    private const string MembershipServiceRelativePath =
        "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/SpeContainerMembershipService.cs";

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>
    /// Seam 3 — every grant query goes through the expiry-aware filter builders.
    /// </summary>
    /// <remarks>
    /// <para><b>The defect this pins (finding A-5, closed by task 007).</b> Grants carry
    /// <c>sprk_expiresdate</c>, and expiry is enforced in the <c>$filter</c> — server-side, so an expired
    /// row never crosses the wire. <c>BuildContactGrantFilter</c> / <c>BuildOrganizationGrantFilter</c>
    /// are what append <c>ExpiryPredicate</c>, and <c>GrantExpiryCharacterizationTests</c> asserts the
    /// strings they emit.</para>
    ///
    /// <para><b>But nothing asserted that the query USES them.</b> Inlining the pre-task-007 filter at
    /// the call site — <c>$"?$filter=_sprk_contact_value eq {contactId} and statecode eq 0"</c> — failed
    /// zero tests (measured 2026-09-08), and silently restored unbounded access for every expired grant
    /// while the builder tests stayed green. Asserting the builder without asserting its use tests a
    /// function nobody has to call.</para>
    /// </remarks>
    [Fact(DisplayName = "Task 025 seam 3: grant queries build their $filter, never inline it")]
    public void GrantQueriesUseTheExpiryAwareFilterBuilders()
    {
        var source = ReadSource(ParticipationServiceRelativePath);

        // Scoped to the GRANT TABLE specifically. An earlier draft keyed on `_sprk_contact_value`, which
        // also appears in the sprk_contactorganizations junction query (`:1068`) — a correct query that
        // rightly has no expiry predicate, because a membership row has no expiry. Expiry is a property
        // of a GRANT. Keying the rule on the entity set is what makes it mean what it says.
        var lines = source.Split('\n');

        var grantQueryStarts = lines
            .Select((text, index) => (Text: text, Index: index))
            .Where(l => l.Text.Contains("/sprk_externalrecordaccesses", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            grantQueryStarts.Count > 0,
            $"No query against sprk_externalrecordaccesses found in {ParticipationServiceRelativePath}. "
            + "Either the file moved or the queries were restructured — a guard that finds nothing passes "
            + "vacuously, so fix the guard rather than deleting it.");

        // The $filter is interpolated on the line(s) immediately after the entity set, in this file's
        // established `$"{apiUrl}/set" + $"?$filter=…" + $"&$select=…"` shape.
        var inlined = grantQueryStarts
            .Select(start => (start.Index, Window: string.Join(
                '\n', lines.Skip(start.Index).Take(4))))
            .Where(w => w.Window.Contains("$filter=", StringComparison.Ordinal))
            .Where(w => !w.Window.Contains("BuildContactGrantFilter", StringComparison.Ordinal)
                        && !w.Window.Contains("BuildOrganizationGrantFilter", StringComparison.Ordinal))
            .Select(w => (Line: w.Index + 1, Text: lines[w.Index].Trim()))
            .ToList();

        Assert.True(
            inlined.Count == 0,
            "A grant query builds its $filter inline instead of calling BuildContactGrantFilter / "
            + "BuildOrganizationGrantFilter. Those builders are what append ExpiryPredicate — an inlined "
            + "filter drops expiry enforcement (finding A-5) and every expired grant silently confers "
            + "access again."
            + $"{Environment.NewLine}  offending line(s): "
            + string.Join("; ", inlined.Select(l => $"{l.Line}: {l.Text.Trim()}")));
    }

    /// <summary>
    /// Seam 4 — reading container members must not swallow its Graph failure.
    /// </summary>
    /// <remarks>
    /// <para><b>The defect this pins (finding, task 016).</b> <c>ListExternalMembersAsync</c> once
    /// wrapped its Graph call in <c>try { } catch { return []; }</c>. A container whose members could not
    /// be READ therefore reported "no members", and the closure cascade reported "0 removed — clean"
    /// over a container whose external members still held file access. An empty list and a failed read
    /// are not the same answer, and the caller cannot tell them apart if the callee erases the
    /// difference.</para>
    ///
    /// <para><b>Restoring that catch failed zero tests</b> (measured 2026-09-08) — the original bug could
    /// be reintroduced in one line, invisibly. The method must let the exception propagate; its
    /// <c>&lt;exception&gt;</c> doc already promises exactly that.</para>
    /// </remarks>
    [Fact(DisplayName = "Task 025 seam 4: ListExternalMembersAsync propagates a Graph failure")]
    public void ListExternalMembersDoesNotSwallowItsGraphError()
    {
        var source = ReadSource(MembershipServiceRelativePath);

        var start = source.IndexOf("public virtual async Task<IReadOnlyList<SpeContainerMember>> ListExternalMembersAsync",
            StringComparison.Ordinal);

        Assert.True(start >= 0,
            "ListExternalMembersAsync was not found — the guard cannot pass vacuously. If the method was "
            + "renamed or moved, update this guard; the invariant still holds.");

        // Bound the scan to this method: from its signature to the start of the next member.
        var end = source.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];

        Assert.False(
            body.Contains("catch", StringComparison.Ordinal),
            "ListExternalMembersAsync must let a Graph failure propagate. A catch here is the original "
            + "task-016 defect: a container that could not be READ reports 'no members', and the closure "
            + "cascade then reports '0 removed — clean' while external members keep file access. "
            + "'Empty' and 'could not read' must stay distinguishable to the caller.");
    }
}
