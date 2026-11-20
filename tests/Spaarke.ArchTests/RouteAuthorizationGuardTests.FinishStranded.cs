using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 ISS-034 (task iss034a, EB-D-244): the operator-finish route <c>POST /api/finance/submissions/{submissionId:guid}/finish-stranded</c>
/// passes Rule A only by the credit of its Finance authorization filter. A copy of the REAL <c>SubmissionOverrideEndpoints.cs</c> with that
/// filter removed from the new route fails Rule A on exactly that route; the unmodified copy passes (the three existing routes too).
/// </summary>
public partial class RouteAuthorizationGuardTests
{
    private const string SubmissionOverrideFile = "Api/Finance/SubmissionOverrideEndpoints.cs";

    [Fact(DisplayName = "iss034a negative control: removing the Finance filter from the finish-stranded route fails Rule A on that route")]
    public void FinishStranded_RuleA_FiresWithoutTheFinanceFilter()
    {
        var real = File.ReadAllText(Path.Combine(BffRoot, "Api", "Finance", "SubmissionOverrideEndpoints.cs"));
        const string route = "group.MapPost(\"/{submissionId:guid}/finish-stranded\", FinishStrandedAsync)";
        const string marker = ".AddFinanceAuthorizationFilter(\"ebilling.submission.decide\", FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, \"submissionId\")";
        // Searched in the comment-blanked text (same offsets as the raw file), so a comment naming the call is never what is removed.
        var text = Lex(real).Text;
        var routeAt = text.IndexOf(route, StringComparison.Ordinal);
        Assert.True(routeAt > 0, "the fixture needs the real finish-stranded route");
        var at = text.IndexOf(marker, routeAt, StringComparison.Ordinal);
        Assert.True(at > routeAt, "the fixture needs the real filter call on that route");

        var violations = SubmissionOverrideRuleAViolations(real.Remove(at, marker.Length));

        Assert.Single(violations);
        Assert.StartsWith("POST /api/finance/submissions/{submissionId:guid}/finish-stranded", violations[0], StringComparison.Ordinal);
    }

    [Fact(DisplayName = "iss034a control: the unmodified blocked-submission routes, finish-stranded included, pass Rule A (must not fire)")]
    public void FinishStranded_RuleA_PassesAsShipped()
    {
        var real = File.ReadAllText(Path.Combine(BffRoot, "Api", "Finance", "SubmissionOverrideEndpoints.cs"));

        Assert.Empty(SubmissionOverrideRuleAViolations(real));
    }

    private static List<string> SubmissionOverrideRuleAViolations(string source)
    {
        var routes = ScanFixtures(new[]
        {
            (SubmissionOverrideFile, source),
            ("Program.cs", "var app = builder.Build();\napp.MapSubmissionOverrideEndpoints();"),
        }, SubmissionOverrideFile);
        Assert.Equal(4, routes.Count(r => !r.Unparseable));
        return RuleAViolations(routes.Select(r => Assess(r, Array.Empty<HandlerDecision>())), Waivers);
    }
}
