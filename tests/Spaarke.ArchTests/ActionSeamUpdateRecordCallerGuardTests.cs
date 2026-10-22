using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// #2257 fix round 2 (K5): <c>IActionSeam.UpdateRecordAsync</c> with an impersonated user is treated as the apply of a
/// proposal THAT person confirmed (<c>UpdateRecordPayloadSource.ConfirmedProposal</c>), so it skips the configuration
/// deny-lists (<c>InheritedAccessPermission.RefuseAppOnlyWrite</c>). A new caller that handed it a model- or
/// configuration-built payload would lower a document's Access Permission, re-point its D7 links or re-own a row as
/// whichever person it impersonated. The callers are therefore an allow-list: the two Job B apply services, which write
/// only what their user confirmed.
/// </summary>
/// <remarks>
/// <para><b>Signal and limits.</b> A source scan of <c>src/server/**</c> code text (line comments stripped) for a member access
/// <c>.UpdateRecordAsync</c> (a call or a method group). No other server type declares a member of that name today, so every
/// match is the seam; a new type that declares one shows here and is either renamed or listed. A call through reflection or
/// a delegate built elsewhere is not seen. Enforced by: this guard (ArchTests in CI and the Stop hook) and the remarks on
/// <c>IActionSeam.UpdateRecordAsync</c>.</para>
/// </remarks>
public class ActionSeamUpdateRecordCallerGuardTests
{
    /// <summary>The allowed callers, each with why its payload is one its user confirmed.</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedCallers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationProposalApplyService.cs"] =
            "Job B apply of an email-review proposal the confirming user approved",
        ["src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationCreateTaskApplyService.cs"] =
            "Job B apply of a create-task proposal the confirming user approved",
    };

    private static readonly Regex SeamUpdateCall = new(
        @"\.\s*UpdateRecordAsync\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The number of <c>.UpdateRecordAsync</c> member accesses in <paramref name="source"/>'s code.</summary>
    internal static int SeamUpdateCalls(string source) =>
        SeamUpdateCall.Matches(SourceScan.CodeText(source.Split('\n'))).Count;

    [Fact(DisplayName = "#2257 K5: only the two Job B apply services call IActionSeam.UpdateRecordAsync")]
    public void OnlyTheConfirmedProposalAppliers_CallTheSeamsUpdate()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var calls = SeamUpdateCalls(File.ReadAllText(file));
            if (calls == 0)
                continue;

            seen.Add(relative);
            if (!AllowedCallers.ContainsKey(relative))
                problems.Add($"{relative}: {calls} call(s) of IActionSeam.UpdateRecordAsync outside the confirmed-proposal appliers");
        }

        foreach (var (allowed, why) in AllowedCallers)
        {
            if (!seen.Contains(allowed))
                problems.Add($"{allowed}: listed ({why}) but calls IActionSeam.UpdateRecordAsync nowhere: the scan lost its signal or the entry is stale");
        }

        Assert.True(problems.Count == 0,
            "IActionSeam.UpdateRecordAsync with an impersonated user skips the configuration deny-lists, so it applies only a "
            + "proposal its person confirmed (#2257 fix round 2):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2257 K5 guard control (must fire): a call or method group outside the allow-list is counted")]
    public void Control_ACallElsewhere_IsCounted()
    {
        Assert.Equal(1, SeamUpdateCalls("var r = await _actionSeam.UpdateRecordAsync(request, ct);"));
        Assert.Equal(1, SeamUpdateCalls("var r = await _actionSeam\n    .UpdateRecordAsync(request, ct);"));
        Assert.Equal(1, SeamUpdateCalls("Func<UpdateRecordRequest, CancellationToken, Task<UpdateRecordResult>> f = seam.UpdateRecordAsync;"));
    }

    [Fact(DisplayName = "#2257 K5 guard control (must not fire): a comment, a declaration or another name is not a call")]
    public void Control_NonCalls_AreNotCounted()
    {
        Assert.Equal(0, SeamUpdateCalls("// _actionSeam.UpdateRecordAsync(request, ct);"));
        Assert.Equal(0, SeamUpdateCalls("public async Task<UpdateRecordResult> UpdateRecordAsync(UpdateRecordRequest request)"));
        Assert.Equal(0, SeamUpdateCalls("await _service.UpdateRecordFieldsAsync(table, id, fields, ct);"));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // #2257 fix round 3 (K, rule 2): only ActionSeam claims UpdateRecordPayloadSource.ConfirmedProposal.
    //
    // The claim is what makes UpdateRecordActionCore skip RefuseAppOnlyWrite for an impersonated write. The caller allow-list
    // above pins who reaches the seam; this pins who may hand the core the claim directly (a new consumer of
    // UpdateRecordActionCore that passed ConfirmedProposal would bypass the seam and its allow-list). Signal: the qualified
    // name used as a VALUE (an argument, an assignment, an initializer, a return) in src/server/** code text, line comments
    // stripped. A comparison (==, !=, is, case) only READS the claim, as the core's own check does, and is not counted; the
    // enum's declaration (`ConfirmedProposal = 1` inside UpdateRecordPayloadSource) is not qualified and is not counted.
    // Limits: a `using static` import with the bare member, a cast from an int, and code outside src/server are not seen.
    // Enforced by: this guard (ArchTests in CI and the Stop hook) and the remarks on IActionSeam.UpdateRecordAsync.
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The one file that may claim a confirmed proposal, and why.</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedConfirmedProposalClaimers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ActionSeam.cs"] =
            "the seam's UpdateRecordAsync, reached only by the confirmed-proposal appliers above, claims it only with an impersonated user",
    };

    private static readonly Regex ConfirmedProposalClaim = new(
        @"(?<!(?:==|!=|\bis|\bnot|\bcase)\s*)(?<![\w.])(?:[\w.]+\.)?UpdateRecordPayloadSource\s*\.\s*ConfirmedProposal\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The number of claims of <c>UpdateRecordPayloadSource.ConfirmedProposal</c> in <paramref name="source"/>'s code.</summary>
    internal static int ConfirmedProposalClaims(string source) =>
        ConfirmedProposalClaim.Matches(SourceScan.CodeText(source.Split('\n'))).Count;

    [Fact(DisplayName = "#2257 fix round 3: only ActionSeam claims UpdateRecordPayloadSource.ConfirmedProposal")]
    public void OnlyActionSeam_ClaimsAConfirmedProposal()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var claims = ConfirmedProposalClaims(File.ReadAllText(file));
            if (claims == 0)
                continue;

            seen.Add(relative);
            if (!AllowedConfirmedProposalClaimers.ContainsKey(relative))
                problems.Add($"{relative}: {claims} claim(s) of UpdateRecordPayloadSource.ConfirmedProposal outside ActionSeam");
        }

        foreach (var (allowed, why) in AllowedConfirmedProposalClaimers)
        {
            if (!seen.Contains(allowed))
                problems.Add($"{allowed}: listed ({why}) but claims ConfirmedProposal nowhere: the scan lost its signal or the entry is stale");
        }

        Assert.True(problems.Count == 0,
            "UpdateRecordPayloadSource.ConfirmedProposal with an impersonated user skips every configuration deny-list, so only "
            + "ActionSeam (behind its caller allow-list) may claim it (#2257 fix round 3):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2257 fix round 3 guard control (must fire): a claim as an argument, an assignment or an initializer is counted")]
    public void Control_AClaimElsewhere_IsCounted()
    {
        Assert.Equal(1, ConfirmedProposalClaims("var input = new UpdateRecordInput(table, id, fields, user, UpdateRecordPayloadSource.ConfirmedProposal);"));
        Assert.Equal(1, ConfirmedProposalClaims("source = Sprk.Bff.Api.Services.Ai.Nodes.ActionCore.UpdateRecordPayloadSource.ConfirmedProposal;"));
        Assert.Equal(1, ConfirmedProposalClaims("var x = new Input { PayloadSource = UpdateRecordPayloadSource\n    .ConfirmedProposal };"));
        Assert.Equal(1, ConfirmedProposalClaims("return flag ? UpdateRecordPayloadSource.ConfirmedProposal : UpdateRecordPayloadSource.Configuration;"));
    }

    [Fact(DisplayName = "#2257 fix round 3 guard control (must not fire): a comparison, the declaration, a comment or another member is not a claim")]
    public void Control_NonClaims_AreNotCounted()
    {
        Assert.Equal(0, ConfirmedProposalClaims("if (input.PayloadSource != UpdateRecordPayloadSource.ConfirmedProposal || x) {"));
        Assert.Equal(0, ConfirmedProposalClaims("var b = input.PayloadSource == UpdateRecordPayloadSource.ConfirmedProposal;"));
        Assert.Equal(0, ConfirmedProposalClaims("if (s is UpdateRecordPayloadSource.ConfirmedProposal) { } if (s is not UpdateRecordPayloadSource.ConfirmedProposal) { }"));
        Assert.Equal(0, ConfirmedProposalClaims("case UpdateRecordPayloadSource.ConfirmedProposal: break;"));
        Assert.Equal(0, ConfirmedProposalClaims("public enum UpdateRecordPayloadSource { Configuration = 0, ConfirmedProposal = 1, }"));
        Assert.Equal(0, ConfirmedProposalClaims("// pass UpdateRecordPayloadSource.ConfirmedProposal here"));
        Assert.Equal(0, ConfirmedProposalClaims("var s = UpdateRecordPayloadSource.Configuration;"));
    }
}
