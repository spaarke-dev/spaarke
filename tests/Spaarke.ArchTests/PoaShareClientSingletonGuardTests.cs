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
/// <para><b>Crude by design</b> (see <see cref="SourceScan"/>): these scan text, not syntax. Each rule is
/// paired with the negative control that proves the detector actually fires — verified by seeding a
/// duplicate payload and watching the rule go red before the guard was committed.</para>
/// </remarks>
public class PoaShareClientSingletonGuardTests
{
    /// <summary>The one file allowed to build POA action payloads.</summary>
    private const string CanonicalClient = "src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs";

    private static string RelativePath(string fullPath) =>
        Path.GetRelativePath(SourceScan.RepoRoot, fullPath).Replace('\\', '/');

    /// <summary>
    /// A file "builds a POA action payload" when it POSTs to the <c>GrantAccess</c> or <c>RevokeAccess</c>
    /// Dataverse action. The action name in a POST argument is the narrowest signature of the thing being
    /// forbidden — mentioning either word in prose or a method name is not a client and must not trip this.
    /// </summary>
    private static IEnumerable<string> FilesPostingPoaActions() =>
        SourceScan.ServerSourceFiles()
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return text.Contains("\"GrantAccess\"", StringComparison.Ordinal)
                    || text.Contains("\"RevokeAccess\"", StringComparison.Ordinal);
            })
            .Select(RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal);

    [Fact(DisplayName = "Task 060: exactly ONE server file constructs GrantAccess/RevokeAccess payloads")]
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
