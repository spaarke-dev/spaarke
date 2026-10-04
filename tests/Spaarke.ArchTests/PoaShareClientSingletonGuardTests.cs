using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 060 — the POA share client stays consolidated.
/// </summary>
/// <remarks>
/// <para>Spaarke had TWO <c>principalobjectaccess</c> (POA) share clients: the systemuser-only,
/// revoke-less one behind <c>IDataverseAccessGrantService</c>, and a private teams-plus-revoke duplicate
/// inside <c>PlaybookSharingService</c>. Task 060 merged them into one seam parameterized by principal
/// kind. Doing the merge once is not the same as it STAYING merged: the cheapest way to add sharing to a
/// new surface is to paste a <c>GrantAccess</c> payload next to the code that needs it, which is exactly
/// how the second client appeared the first time.</para>
///
/// <para>So these guards pin the invariant as a build failure, per root CLAUDE.md §11 (default to reuse).
/// A genuinely new POA construction site is not "make the test pass" — it is a design decision to be
/// argued in review, and the fix is almost always to call the existing seam.</para>
///
/// <para><b>Task 063 added a third POA action</b>, <c>ModifyAccess</c> (a level change replaces the rights; a
/// GrantAccess on an existing share is not documented to). It was added to the canonical client, and the detector
/// below now names it too — otherwise a second client could be born through the one action the guard did not
/// know about.</para>
///
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): these scan text, not syntax. Each rule is
/// paired with the negative control that proves the detector actually fires — verified by seeding a
/// duplicate payload and watching the rule go red before the guard was committed.</para>
/// </remarks>
public class PoaShareClientSingletonGuardTests
{
    /// <summary>The one file allowed to build POA action payloads.</summary>
    private const string CanonicalClient = "src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs";

    /// <summary>The Dataverse POA actions, as the quoted action names a POST argument carries.</summary>
    private static readonly string[] PoaActions = { "\"GrantAccess\"", "\"ModifyAccess\"", "\"RevokeAccess\"" };

    private static string RelativePath(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');

    /// <summary>
    /// A file "builds a POA action payload" when it POSTs to the <c>GrantAccess</c>, <c>ModifyAccess</c> or
    /// <c>RevokeAccess</c> Dataverse action. The action name in a POST argument is the narrowest signature of the
    /// thing being forbidden — mentioning any of these words in prose or a method name is not a client and must not
    /// trip this.
    /// </summary>
    private static IEnumerable<string> FilesPostingPoaActions() =>
        SourceScan.ServerSourceFiles()
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return PoaActions.Any(action => text.Contains(action, StringComparison.Ordinal));
            })
            .Select(RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal);

    [Fact(DisplayName = "Task 060: exactly ONE server file constructs GrantAccess/ModifyAccess/RevokeAccess payloads")]
    public void PoaActionPayloadsAreBuiltInExactlyOnePlace()
    {
        var sites = FilesPostingPoaActions().ToList();

        Assert.True(
            sites.Count == 1 && sites[0] == CanonicalClient,
            "task 060 consolidated the two POA share clients into one principal-parameterized seam "
            + "(CLAUDE.md §11). A second construction site means a third client is being born — call "
            + "IDataverseRecordShareService instead of pasting a payload."
            + $"{Environment.NewLine}  expected: {CanonicalClient}"
            + $"{Environment.NewLine}  found   : {string.Join(", ", sites)}");
    }

    [Fact(DisplayName = "Task 060: the POA seam exposes revoke, so internal shares stay revocable")]
    public void TheSeamExposesRevoke()
    {
        var seam = Path.Combine(
            SourceScan.RepoRoot,
            "src/server/api/Sprk.Bff.Api/Services/Access/IDataverseRecordShareService.cs");

        Assert.True(
            File.Exists(seam),
            "the consolidated POA seam is the write path FR-28 (share-only secure projects) and FR-29 "
            + "(the '+ User' picker) depend on; expected it at " + seam);

        var text = File.ReadAllText(seam);

        Assert.True(
            text.Contains("Task RevokeAccessAsync(", StringComparison.Ordinal),
            "a grant-only seam is what made internal shares permanently unrevocable from the UI — the "
            + "concrete cost-of-doing-nothing task 060 exists to remove");
        Assert.Contains("Task GrantAccessAsync(", text, StringComparison.Ordinal);
        Assert.Contains(
            "Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(",
            text,
            StringComparison.Ordinal);
    }

    // Task 063 deliberately adds NO rule asserting that the seam DECLARES ModifyAccessAsync or
    // GetPrincipalAccessOrThrowAsync. The compiler already enforces both — every implementation and every call site
    // fails to build without them — and a text-scan rule asserting signatures the same commit added can never fail,
    // which is the shape tests/CLAUDE.md warns about ("a detector nobody has seen fail is a detector nobody knows
    // works"); it would also false-red the moment someone legally wraps a signature across lines. The invariant
    // worth guarding is the one above: exactly ONE file builds POA payloads, ModifyAccess included — and that rule
    // has a verified negative control (task 063 perturbation P10 seeded a second "ModifyAccess" POST and watched it
    // go red).

    // =============================================================================================
    // Task 132 (batch 4 integration residual) — every share WRITE goes through the seam that evicts
    // ---------------------------------------------------------------------------------------------
    // A POA grant / rights change / revoke changes who can read a record exactly as an owner change does, so the access
    // caches must be evicted after it. The eviction lives in the ONE seam (DataverseRecordShareService calls
    // IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync after every write). It is only "by construction" if no
    // writer can reach POA another way: through the concrete DataverseWebApiService (which builds the payload but does
    // not evict), or through the SDK's GrantAccess/ModifyAccess/RevokeAccess messages. These rules pin that.
    // =============================================================================================

    /// <summary>The seam — the one file allowed to call the concrete client's POA writes.</summary>
    private const string EvictingSeam = "src/server/api/Sprk.Bff.Api/Services/Access/IDataverseRecordShareService.cs";

    private static readonly System.Text.RegularExpressions.Regex PoaWriteCall = new(
        @"(?<receiver>[A-Za-z_][A-Za-z0-9_]*)\s*\.\s*(?<method>GrantAccessAsync|ModifyAccessAsync|RevokeAccessAsync)\s*\(",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static readonly System.Text.RegularExpressions.Regex SdkPoaMessage = new(
        @"\b(GrantAccess|ModifyAccess|RevokeAccess)Request\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The POA write calls in one file's text: each <c>x.GrantAccessAsync(</c> / <c>ModifyAccessAsync(</c> /
    /// <c>RevokeAccessAsync(</c>, with whether its receiver <c>x</c> is declared in that file as an
    /// <c>IDataverseRecordShareService</c> (a field, parameter or local — <c>_owner._recordShare</c> resolves to the
    /// last identifier). Line comments are stripped first. Crude by design (see <see cref="SourceScan"/>); paired with
    /// <see cref="Detector_FlagsAWriteOnTheConcreteClient_AndPassesAWriteThroughTheSeam"/>.
    /// </summary>
    internal static IReadOnlyList<(string Receiver, string Method, bool ThroughSeam)> PoaWriteCalls(string text)
    {
        var code = string.Join('\n', text.Split('\n').Select(SourceScan.StripLineComment));
        return PoaWriteCall.Matches(code)
            .Select(m =>
            {
                var receiver = m.Groups["receiver"].Value;
                var declaredAsSeam = new System.Text.RegularExpressions.Regex(
                    @"\bIDataverseRecordShareService\??\s+" + System.Text.RegularExpressions.Regex.Escape(receiver) + @"\b",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant).IsMatch(code);
                return (receiver, m.Groups["method"].Value, declaredAsSeam);
            })
            .ToList();
    }

    /// <summary>Whether one file's text sends a Dataverse SDK POA message (the SDK route around the Web API seam).</summary>
    internal static bool UsesSdkPoaMessages(string text)
    {
        var code = string.Join('\n', text.Split('\n').Select(SourceScan.StripLineComment));
        return code.Contains("Microsoft.Crm.Sdk.Messages", StringComparison.Ordinal) && SdkPoaMessage.IsMatch(code);
    }

    [Fact(DisplayName = "Task 132: every POA share write in src/server goes through the evicting seam")]
    public void EveryPoaShareWriteGoesThroughTheEvictingSeam()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Select(file => (Path: RelativePath(file), Text: File.ReadAllText(file)))
            .Where(f => f.Path != EvictingSeam && f.Path != CanonicalClient)
            .SelectMany(f => PoaWriteCalls(f.Text)
                .Where(c => !c.ThroughSeam)
                .Select(c => $"{f.Path}: {c.Receiver}.{c.Method}(…)"))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "a POA share write that does not go through IDataverseRecordShareService skips the access-cache eviction "
            + "(task 132): the sharee's impersonated root set and every snapshot of the record stay stale for their TTLs — "
            + "an unshare that keeps access. Inject IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", offenders)}");
    }

    /// <summary>
    /// The share writers the batch 4 integration named (plus the provisioning file, whose creator share, its restore and
    /// the resume error paths all live there), each still VISIBLE to the rule above — at least one write found, every
    /// one through the seam. A refactor that hid a writer from the detector would otherwise pass the rule vacuously.
    /// </summary>
    [Theory(DisplayName = "Task 132: each named share-writing path writes POA only through the evicting seam")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/ExternalAccess/InternalShareEndpoints.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProvisionProjectEndpoint.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Api/ExternalAccess/UnsecureProjectEndpoint.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Access/SecureChildShareSynchronizer.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/ExternalAccess/AssignedAccessMaterializer.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/NoAccessShareEnforcer.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Communication/Access/DirectThreadAccessService.cs")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs")]
    public void EachNamedShareWriterWritesOnlyThroughTheSeam(string path)
    {
        var calls = PoaWriteCalls(File.ReadAllText(Path.Combine(SourceScan.RepoRoot, path)));

        Assert.True(calls.Count > 0, $"{path}: no POA share write is visible to the detector any more — re-check it.");
        Assert.True(
            calls.All(c => c.ThroughSeam),
            $"{path}: {string.Join(", ", calls.Where(c => !c.ThroughSeam).Select(c => $"{c.Receiver}.{c.Method}"))} "
            + "does not go through IDataverseRecordShareService, so it skips the share-change eviction (task 132).");
    }

    [Fact(DisplayName = "Task 132: no server file sends the SDK's GrantAccess/ModifyAccess/RevokeAccess messages")]
    public void NoServerFileSendsSdkPoaMessages()
    {
        var offenders = SourceScan.ServerSourceFiles()
            .Where(file => UsesSdkPoaMessages(File.ReadAllText(file)))
            .Select(RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "an SDK POA message bypasses the one seam — and with it the access-cache eviction (task 132) and the payload "
            + "consolidation (task 060). Use IDataverseRecordShareService."
            + $"{Environment.NewLine}  {string.Join(", ", offenders)}");
    }

    /// <summary>Negative and positive controls for the two detectors above.</summary>
    [Fact(DisplayName = "Task 132: the share-write detectors fire on a bypass and pass the sanctioned shape")]
    public void Detector_FlagsAWriteOnTheConcreteClient_AndPassesAWriteThroughTheSeam()
    {
        const string bypass = """
            private readonly DataverseWebApiService _dataverse;
            public Task X() => _dataverse.GrantAccessAsync("sprk_projects", id, principal, "ReadAccess");
            """;
        const string throughSeam = """
            internal static async Task Y(IDataverseRecordShareService recordShare)
            {
                await recordShare
                    .RevokeAccessAsync("sprk_projects", id, principal, ct);
                // _dataverse.ModifyAccessAsync( in a comment is not a call
            }
            """;
        const string nestedReceiver = """
            private readonly IDataverseRecordShareService _recordShare;
            void Z() => _owner._recordShare.ModifyAccessAsync(set, id, p, "ReadAccess", _ct);
            """;

        Assert.Equal(new[] { ("_dataverse", "GrantAccessAsync", false) }, PoaWriteCalls(bypass));
        Assert.Equal(new[] { ("recordShare", "RevokeAccessAsync", true) }, PoaWriteCalls(throughSeam));
        Assert.Equal(new[] { ("_recordShare", "ModifyAccessAsync", true) }, PoaWriteCalls(nestedReceiver));

        Assert.True(UsesSdkPoaMessages("using Microsoft.Crm.Sdk.Messages;\nvar r = new GrantAccessRequest { Target = t };"));
        Assert.False(UsesSdkPoaMessages("using Microsoft.Crm.Sdk.Messages;\nvar r = new RetrievePrincipalAccessRequest();"),
            "an SDK message that does not write a share is not a bypass");
        Assert.False(UsesSdkPoaMessages("public record GrantAccessRequest(Guid ContactId);"),
            "the external-access DTO of the same name is not the SDK message");
    }

    [Fact(DisplayName = "Task 060: PlaybookSharingService holds no private POA client")]
    public void PlaybookSharingServiceDelegatesRatherThanDuplicating()
    {
        var text = File.ReadAllText(Path.Combine(
            SourceScan.RepoRoot,
            "src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSharingService.cs"));

        Assert.False(
            text.Contains("\"GrantAccess\"", StringComparison.Ordinal),
            "its private team-grant payload was deleted in favour of the seam (task 060)");
        Assert.False(
            text.Contains("\"RevokeAccess\"", StringComparison.Ordinal),
            "its private team-revoke payload was deleted in favour of the seam (task 060)");
        Assert.False(
            text.Contains("principalobjectaccessset", StringComparison.Ordinal),
            "its private POA read was deleted in favour of IDataverseRecordShareService.GetPrincipalAccessAsync");
        Assert.True(
            text.Contains("_recordShare.", StringComparison.Ordinal),
            "it must reach POA through the one seam");
    }
}
