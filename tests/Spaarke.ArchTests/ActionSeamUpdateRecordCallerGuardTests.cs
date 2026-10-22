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
}
