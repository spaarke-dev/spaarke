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
        // also appears in the sprk_contactorganizations junction query (QueryOrganizationMembershipsAsync).
        // That query rightly carries no grant-expiry predicate — but NOT because a membership has no end:
        // it does (`sprk_enddate`, and `sprk_startdate`), and since task 109 (owner D-2 / D-10) a
        // membership outside those dates confers nothing. The bound is applied IN MEMORY to the
        // conferring set (ExternalParticipationService.MembershipConfersOn), never in the junction
        // $filter, because the same read also defines the FR-23 wall-subject set, which must NOT be
        // date-bounded. That shape is pinned by its own guard below. Keying THIS rule on the grant entity
        // set is what makes it mean what it says.
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
    /// Task 109 — every membership-junction query builds its <c>$filter</c> through
    /// <c>BuildOrganizationMembershipFilter</c>, never inline.
    /// </summary>
    /// <remarks>
    /// <para><b>Why the call site needs its own guard.</b> One junction read serves the additive
    /// CONFERRING set and the FR-23 deny-veto WALL set. The wall must stay bounded on <c>statecode</c>
    /// ONLY (owner D-2 part 2, D-10); the date bounds belong to the conferring set and are applied in
    /// memory. <c>OrganizationMembershipReadTests</c> asserts that the builder carries no date term — but,
    /// exactly as seam 3 found for grants, asserting a builder proves nothing about a call site that
    /// stops using it.</para>
    ///
    /// <para><b>What this guard catches — and what it does NOT.</b> It catches a junction query whose
    /// <c>$filter</c> is built WITHOUT the builder: an inlined <c>… and (sprk_enddate eq null or
    /// sprk_enddate ge …)</c> in place of the call. It only checks that the builder's NAME appears near the
    /// entity set, so it does <b>not</b> catch a term APPENDED after the builder call
    /// (<c>$"?$filter={BuildOrganizationMembershipFilter(contactId)} and (sprk_enddate …)"</c>) — that
    /// passed this guard and every unit test (verifier finding, 2026-10-01). That form is caught on the
    /// wire instead: <c>OrganizationMembershipReadTests</c> asserts the <c>$filter</c> the junction request
    /// actually sent EQUALS the builder's output, in both the over-match theory and the one-read test. The
    /// two are complementary: the wire assertion covers the production call site against any edit; this
    /// guard covers a further junction query added to this file that no behavioural test drives.</para>
    ///
    /// <para><b>Both directions, both spellings (task 137 r2).</b> Task 137 added the organization → members
    /// read (<c>ReadOrganizationMemberPageAsync</c>, the grant-cache fan-out) and spells its entity set through
    /// the constant <c>{ExternalOrganizationMembership.EntitySet}</c>, which a scan for the literal
    /// <c>/sprk_contactorganizations</c> never saw — a verifier found the guard blind to exactly the kind of
    /// query its remarks say it exists for. It now matches either spelling, accepts the builder of each
    /// direction (<c>BuildOrganizationMembershipFilter</c> for contact → organizations,
    /// <c>ExternalOrganizationMembership.ActiveMembersFilter</c> for organization → members — the shared filter
    /// the revoke path's SPE sweep also uses), and requires BOTH reads to be found, so it cannot go vacuous for
    /// either one. The member read's wire shape is also pinned behaviourally, by the two
    /// <c>OrganizationMembershipReadTests.OrgFanOut_TheRealMemberPageRead_*</c> tests.</para>
    /// </remarks>
    /// <para><b>The third direction (task 140 r-final).</b> The contact-side grant resolves a colleague by email through
    /// ONE junction read scoped to the grantor's organizations (<c>ReadColleagueMembershipRowsAsync</c>), whose filter is
    /// built by <c>BuildColleagueByEmailFilter</c> — organizations × WALL state clause × the contact's state and email, no
    /// date term (the conferring dates are decided in memory by <c>ProjectColleaguesByEmail</c>). It is accepted here as
    /// the builder of that direction, is required to be found (not vacuous), and the builder itself is held to the wall
    /// rule: it names <c>WallMembershipStateClause</c> and no date column. Its wire shape is pinned behaviourally by
    /// <c>ColleagueByEmailWireTests</c> (the filter the server receives EQUALS the builder's output).</para>
    [Fact(DisplayName = "Task 109: membership-junction queries build their $filter, never inline it")]
    public void MembershipJunctionQueriesUseTheWallSafeFilterBuilder()
    {
        var source = ReadSource(ParticipationServiceRelativePath);
        var lines = source.Split('\n');

        var junctionQueryStarts = lines
            .Select((text, index) => (Text: text, Index: index))
            // Either spelling of the entity set: the literal, or the shared constant interpolated (task 137 r2).
            .Where(l => l.Text.Contains("/sprk_contactorganizations", StringComparison.Ordinal)
                        || l.Text.Contains("/{ExternalOrganizationMembership.EntitySet}", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            junctionQueryStarts.Count > 0,
            $"No query against sprk_contactorganizations found in {ParticipationServiceRelativePath}. Either "
            + "the file moved or the query was restructured — a guard that finds nothing passes vacuously, "
            + "so fix the guard rather than deleting it.");

        var windows = junctionQueryStarts
            .Select(start => (start.Index, Window: string.Join('\n', lines.Skip(start.Index).Take(4))))
            .Where(w => w.Window.Contains("$filter=", StringComparison.Ordinal))
            .ToList();

        // Not vacuous for EITHER direction: each read must be found, through its own builder.
        Assert.True(
            windows.Any(w => w.Window.Contains("BuildOrganizationMembershipFilter", StringComparison.Ordinal)),
            "The contact -> organizations junction read (BuildOrganizationMembershipFilter) was not found in "
            + $"{ParticipationServiceRelativePath}. If it moved or was renamed, fix this guard rather than deleting it.");
        Assert.True(
            windows.Any(w => w.Window.Contains("ExternalOrganizationMembership.ActiveMembersFilter", StringComparison.Ordinal)),
            "The organization -> members junction read (ExternalOrganizationMembership.ActiveMembersFilter, the "
            + $"task-137 grant-cache fan-out) was not found in {ParticipationServiceRelativePath}. If it moved or "
            + "was renamed, fix this guard rather than deleting it.");

        Assert.True(
            windows.Any(w => w.Window.Contains("BuildColleagueByEmailFilter", StringComparison.Ordinal)),
            "The (organizations, email) -> colleagues junction read (BuildColleagueByEmailFilter, the task-140 contact-side "
            + $"grant) was not found in {ParticipationServiceRelativePath}. If it moved or was renamed, fix this guard rather "
            + "than deleting it.");

        // The third builder is held to the wall rule here, because no other assertion in this file sees its body.
        var builderStart = Array.FindIndex(lines, l => l.Contains("internal static string BuildColleagueByEmailFilter(", StringComparison.Ordinal));
        Assert.True(builderStart >= 0, $"BuildColleagueByEmailFilter was not found in {ParticipationServiceRelativePath}.");
        var builderEnd = Array.FindIndex(lines, builderStart, l => l.TrimEnd().EndsWith("\";", StringComparison.Ordinal));
        Assert.True(builderEnd >= builderStart, "The end of BuildColleagueByEmailFilter's expression body was not found.");
        var colleagueBuilder = string.Join('\n', lines[builderStart..(builderEnd + 1)]);
        Assert.Contains("WallMembershipStateClause", colleagueBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("sprk_enddate", colleagueBuilder, StringComparison.Ordinal);
        Assert.DoesNotContain("sprk_startdate", colleagueBuilder, StringComparison.Ordinal);

        var inlined = windows
            .Where(w => !w.Window.Contains("BuildOrganizationMembershipFilter", StringComparison.Ordinal)
                        && !w.Window.Contains("ExternalOrganizationMembership.ActiveMembersFilter", StringComparison.Ordinal)
                        && !w.Window.Contains("BuildColleagueByEmailFilter", StringComparison.Ordinal))
            .Select(w => (Line: w.Index + 1, Text: lines[w.Index].Trim()))
            .ToList();

        Assert.True(
            inlined.Count == 0,
            "A sprk_contactorganizations query builds its $filter inline instead of calling "
            + "BuildOrganizationMembershipFilter (contact -> organizations), "
            + "ExternalOrganizationMembership.ActiveMembersFilter (organization -> members) or BuildColleagueByEmailFilter "
            + "((organizations, email) -> colleagues, task 140). The first defines the "
            + "FR-23 WALL-subject set and must stay statecode-only; a date bound added there narrows the ethical "
            + "wall (owner D-2 part 2 / D-10). The second defines whom a grant-cache invalidation reaches; a filter "
            + "inlined there can silently leave members on the 60-second TTL. Date bounds belong to the conferring "
            + "set, in memory (MembershipConfersOn)."
            + $"{Environment.NewLine}  offending line(s): "
            + string.Join("; ", inlined.Select(l => $"{l.Line}: {l.Text}")));
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
    [Fact(DisplayName = "Task 025 seam 4: the member-listing path propagates a Graph failure")]
    public void ListExternalMembersDoesNotSwallowItsGraphError()
    {
        var source = ReadSource(MembershipServiceRelativePath);

        // ⚠️ WIDENED BY TASK 024, NARROWED BY TASK 166 f1. This guard used to scan ListExternalMembersAsync
        // alone; task 024 split the read into a worker (ReadExternalMembersAsync) over a paged reader
        // (ReadPermissionsAsync) and the guard followed every frame. Task 166 f1 DELETED the first two (no
        // production caller after task 166 removed RemoveAllExternalMembersAsync); the paged reader is now the
        // ONE frame every member read goes through (RemoveMembershipsAsync, RevokeMembershipAsync), so it is
        // the frame that must never swallow.
        string[] listingPathSignatures =
        [
            "private async Task<PermissionReadResult> ReadPermissionsAsync",
        ];

        foreach (var signature in listingPathSignatures)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);

            Assert.True(start >= 0,
                $"'{signature}' was not found — the guard cannot pass vacuously. It is one frame of the "
                + "member-listing path (task 025 seam 4, widened by task 024). If it was renamed or "
                + "moved, update this list; the invariant still holds.");

            // Bound the scan to this method: from its signature to the start of the next member.
            var end = source.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
            var body = end > start ? source[start..end] : source[start..];

            Assert.False(
                body.Contains("catch", StringComparison.Ordinal),
                $"'{signature}' must let a Graph failure propagate. A catch anywhere on the listing path "
                + "is the original task-016 defect: a container that could not be READ reports 'no "
                + "members', and the closure cascade then reports '0 removed — clean' while external "
                + "members keep file access. 'Empty' and 'could not read' must stay distinguishable to "
                + "the caller. (RevokeMembershipAsync is deliberately NOT on this list — it catches by "
                + "contract, returning a result instead of throwing.)");
        }
    }

    /// <summary>
    /// Seam 5 — every container-permission read goes through the ONE paged reader.
    /// </summary>
    /// <remarks>
    /// <para><b>The defect this pins (finding M1, review 2026-08-24; fixed by task 024).</b> Both reads in
    /// <c>SpeContainerMembershipService</c> issued a single <c>.GetAsync()</c> and used
    /// <c>permissions?.Value</c> directly, following <c>@odata.nextLink</c> nowhere. Anything past the
    /// first page was invisible, so a partially cleared container could report clean — defeating the exact
    /// guard tasks 016 and 017 were built to provide.</para>
    ///
    /// <para><b>Why structural.</b> The behavioural proof lives in <c>SpeContainerPagingTests</c>, which
    /// drives the real Graph request builders over a fake Kiota <c>IRequestAdapter</c>. But that proves
    /// the reader; it cannot stop a FUTURE call site from going around it. Re-adding a bare
    /// <c>.Permissions.GetAsync(...)</c> somewhere else in this class would restore the single-page defect
    /// while every one of those tests stayed green — the same invisibility that made seams 3 and 4
    /// necessary. This is the rule that survives the next edit.</para>
    ///
    /// <para><b>Not vacuous:</b> the reader must exist AND must itself contain the pattern, so deleting or
    /// renaming it fails here rather than silently satisfying an empty scan.</para>
    /// </remarks>
    [Fact(DisplayName = "Task 024 seam 5: container permissions are read only through the paged reader")]
    public void ContainerPermissionReadsAllGoThroughThePagedReader()
    {
        var source = ReadSource(MembershipServiceRelativePath);

        const string ReaderSignature =
            "private async Task<PermissionReadResult> ReadPermissionsAsync";

        var start = source.IndexOf(ReaderSignature, StringComparison.Ordinal);

        Assert.True(start >= 0,
            "ReadPermissionsAsync was not found — the guard cannot pass vacuously. It is the single paged "
            + "reader every container-permission read must go through (task 024, finding M1). If it was "
            + "renamed, update this guard; the invariant still holds.");

        var end = source.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        var readerBody = end > start ? source[start..end] : source[start..];

        // The reader must actually page: fetch, then follow the server's link.
        Assert.Contains("OdataNextLink", readerBody, StringComparison.Ordinal);
        Assert.Contains("WithUrl", readerBody, StringComparison.Ordinal);
        Assert.True(
            Normalize(readerBody).Contains(".Permissions .GetAsync(", StringComparison.Ordinal),
            "ReadPermissionsAsync must be the place the permission collection is actually fetched. If the "
            + "fetch moved, this guard is pointed at the wrong method and would pass vacuously.");

        // …and it must be the ONLY place. Excise the reader, then scan what is left.
        var everywhereElse = Normalize(source.Remove(start, readerBody.Length));

        Assert.False(
            everywhereElse.Contains(".Permissions .GetAsync(", StringComparison.Ordinal),
            "A container-permission collection is being read outside ReadPermissionsAsync. That is the "
            + "task-024 defect (finding M1) returning: a bare .Permissions.GetAsync() sees only page one, "
            + "so a member past it is invisible — the revoke reports 'no permission found' for a "
            + "permission that is right there, and the closure guard reports a container cleared that is "
            + "not. Route the read through ReadPermissionsAsync and honour its EnumerationComplete flag.");
    }

    /// <summary>
    /// Collapses runs of whitespace so a fluent Graph chain broken across lines matches regardless of how
    /// it happens to be wrapped — the invariant is about the CALL, not its formatting.
    /// </summary>
    private static string Normalize(string source) =>
        System.Text.RegularExpressions.Regex.Replace(source, @"\s+", " ");
}
