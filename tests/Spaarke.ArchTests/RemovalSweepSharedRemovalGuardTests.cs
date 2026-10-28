using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// C4: the removal sweep reaches a person's removal ONLY through the shared removal, <c>ExternalPersonRemovalService.RemoveAsync</c>
/// (the method <c>POST /remove-person</c> calls). The sweep (<c>ExternalAccessRemovalSweep.cs</c>) and the job that runs it
/// (<c>IdentityLinkReconciliationJob.cs</c>) may READ, but neither calls any write the removal is made of: the marker write, the
/// grant revoke and its row deactivation, the container-permission removals, the cache evictions, the invite, access and
/// standing-grant clears, or a raw Dataverse write.
/// </summary>
/// <remarks>
/// <para><b>What counts.</b> An invocation in the Roslyn syntax tree whose called member name is in
/// <see cref="RemovalParts"/> (comments and literals are not invocations). And the sweep must call <c>RemoveAsync</c> exactly
/// once, on the removal it is given as <c>Func&lt;ExternalPersonRemovalService&gt;</c>, so it cannot drop the shared call and
/// stay green. Known limit: the rule reads names, not types; a removal part reached by reflection or through a new helper in
/// another file is not seen. Must-fire and must-not-fire controls below. Enforced by: this guard (ArchTests, CI and the Stop
/// hook).</para>
/// </remarks>
public class RemovalSweepSharedRemovalGuardTests
{
    private const string SweepFile = "src/server/api/Sprk.Bff.Api/Services/ExternalAccess/ExternalAccessRemovalSweep.cs";
    private const string JobFile = "src/server/api/Sprk.Bff.Api/Services/ExternalAccess/IdentityLinkReconciliationJob.cs";

    /// <summary>The writes a person's removal is made of; only <c>ExternalPersonRemovalService</c> may call them for the sweep.</summary>
    private static readonly string[] RemovalParts =
    {
        "WriteRemovedMarkerAsync", "WriteRemovedMarkerIfUnchangedAsync", "ClearRemovedPersonAccessStateAsync", "ClearStandingGrantAsync", "ClearPendingInviteAsync",
        "RevokeAsync", "RemovePersonOrganizationGrantPermissionsAsync", "DeactivateAsync", "RevokeMembershipAsync",
        "RemoveMembershipsAsync", "InvalidateGrantSetsAsync", "InvalidateUserAccessAsync", "MarkGrantRevokedAsync",
        "UpdateAsync", "CreateAsync", "PatchAsync", "DeleteAsync", "UpsertAsync",
    };

    /// <summary>The removal-part invocations in one file: "Name at line N" for each.</summary>
    internal static IReadOnlyList<string> Violations(string code)
        => Invocations(code)
            .Where(i => RemovalParts.Contains(i.Name, StringComparer.Ordinal))
            .Select(i => $"{i.Name} at line {i.Line}")
            .ToList();

    /// <summary>How many times <c>RemoveAsync</c> is invoked on <c>_removal()</c>, the shared removal the sweep is given.</summary>
    internal static int SharedRemovalCalls(string code)
        => CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Count(i => i.Expression is MemberAccessExpressionSyntax
            {
                Name.Identifier.ValueText: "RemoveAsync",
                Expression: InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "_removal" } },
            });

    private static IEnumerable<(string Name, int Line)> Invocations(string code)
        => CSharpSyntaxTree.ParseText(code).GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Select(i => (Name: i.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
                MemberBindingExpressionSyntax b => b.Name.Identifier.ValueText,
                IdentifierNameSyntax n => n.Identifier.ValueText,
                GenericNameSyntax g => g.Identifier.ValueText,
                _ => string.Empty,
            }, Line: i.GetLocation().GetLineSpan().StartLinePosition.Line + 1));

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    // ── The rule over the real files ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SweepFile)]
    [InlineData(JobFile)]
    public void TheSweepAndItsJob_CallNoPartOfTheRemoval_Directly(string file)
        => Assert.Empty(Violations(Read(file)));

    [Fact]
    public void TheSweep_CallsTheSharedRemoval_ExactlyOnce_AsTheExternalPersonRemovalServiceItIsGiven()
    {
        var sweep = Read(SweepFile);
        Assert.Equal(1, SharedRemovalCalls(sweep));
        Assert.Contains("Func<ExternalPersonRemovalService> removal", sweep, StringComparison.Ordinal);
    }

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("await _store.WriteRemovedMarkerAsync(id, now, ct);", "WriteRemovedMarkerAsync")]
    [InlineData("await new ExternalGrantRevocationService(a, b, c, d, e, f, g, h).RevokeAsync(row, ct);", "RevokeAsync")]
    [InlineData("await ExternalGrantLifecycle.DeactivateAsync(_dataverse, ids, _logger, ct);", "DeactivateAsync")]
    [InlineData("await _dataverse.UpdateAsync(\"contacts\", id, new { x = 1 }, ct);", "UpdateAsync")]
    [InlineData("await _store?.ClearStandingGrantAsync(id, ct);", "ClearStandingGrantAsync")]
    public void MustFire_ADirectCallToAPartOfTheRemoval(string statement, string expected)
    {
        var code = $"class S {{ async System.Threading.Tasks.Task M() {{ {statement} }} }}";
        Assert.Contains(Violations(code), v => v.StartsWith(expected + " at line", StringComparison.Ordinal));
    }

    [Fact]
    public void MustFire_ASweepThatNoLongerCallsTheSharedRemoval()
        => Assert.Equal(0, SharedRemovalCalls("class S { async System.Threading.Tasks.Task M() { await Other().RemoveAsync(id, c, ct); } }"));

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MustNotFire_ReadsAndTheSharedRemoval()
    {
        const string code = """
            class S
            {
                async System.Threading.Tasks.Task M()
                {
                    // _store.WriteRemovedMarkerAsync(id) in a comment is not a call
                    var text = "UpdateAsync";
                    var lookup = await _store.GetContactAsync(id, ct);
                    var marker = await _store.ReadRemovedMarkerAsync(id, ct);
                    var rows = await _dataverse.QueryAsync<Row>("contacts", filter: f, top: 1, cancellationToken: ct);
                    var outcome = await _removal().RemoveAsync(id, correlationId, ct);
                }
            }
            """;
        Assert.Empty(Violations(code));
        Assert.Equal(1, SharedRemovalCalls(code));
    }
}
