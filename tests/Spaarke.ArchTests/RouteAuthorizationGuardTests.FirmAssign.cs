using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095g (notes 095g-assign-firm.md section 6, "seeded proofs"): the firm-assign routes pass Rule A only by the credit of
/// their Finance authorization filter. A copy of the REAL <c>CoreRecordFirmEndpoints.cs</c> with that filter removed from one route fails
/// Rule A on exactly that route; the unmodified copy passes.
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private const string FirmAssignFile = "Api/Finance/CoreRecordFirmEndpoints.cs";

    [Theory(DisplayName = "095g negative control: removing the Finance filter from either firm-assign route fails Rule A on that route")]
    [InlineData(".AddFinanceAuthorizationFilter(ResolveAssignTargets, FinanceDenial.UniformNotFoundFailClosed)",
        "POST /api/finance/core-records/{entityType}/{entityId:guid}/firms")]
    [InlineData(".AddFinanceAuthorizationFilter(ResolveListTargets, FinanceDenial.UniformNotFoundFailClosed)", "GET /api/finance/firms")]
    public void FirmAssign_RuleA_FiresWithoutTheFinanceFilter(string marker, string route)
    {
        var real = File.ReadAllText(Path.Combine(BffRoot, "Api", "Finance", "CoreRecordFirmEndpoints.cs"));
        // Searched in the comment-blanked text (same offsets as the raw file), so a comment naming the call is never what is removed.
        var at = Lex(real).Text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at > 0, "the fixture needs the real filter call to remove");

        var violations = FirmAssignRuleAViolations(real.Remove(at, marker.Length));

        Assert.Single(violations);
        Assert.StartsWith(route, violations[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "095g control: the unmodified firm-assign routes pass Rule A (must not fire)")]
    public void FirmAssign_RuleA_PassesAsShipped()
    {
        var real = File.ReadAllText(Path.Combine(BffRoot, "Api", "Finance", "CoreRecordFirmEndpoints.cs"));

        Assert.Empty(FirmAssignRuleAViolations(real));
    }

    private static List<string> FirmAssignRuleAViolations(string source)
    {
        var routes = ScanFixtures(new[]
        {
            (FirmAssignFile, source),
            ("Program.cs", "var app = builder.Build();\napp.MapCoreRecordFirmEndpoints();"),
        }, FirmAssignFile);
        Assert.Equal(2, routes.Count(r => !r.Unparseable));
        return RuleAViolations(routes.Select(r => Assess(r, Array.Empty<HandlerDecision>())), Waivers);
    }
}
