using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 130 (defect C8): the caller check for the four recalculate routes lives at
/// the ENDPOINT (FinanceAuthorizationFilter), and the two services behind them stay caller-free.
/// </summary>
/// <remarks>
/// <para><b>Why this is a boundary, not a preference.</b> <c>SpendSnapshotGenerationJobHandler</c> calls
/// <c>FinanceRollupService</c> from a background job with no caller token. A caller check moved INTO the service
/// would either break that job or tempt an app-only fallback — the exact disclosure task 130 closes. So neither
/// service may take a dependency on anything that carries a caller: the authorization seam, the access data
/// source, the HTTP context, or claims.</para>
/// <para><b>And the writes stay update-only.</b> A plain Web API PATCH upserts, so a parent deleted between the
/// endpoint's check and the write would be recreated. The source scan pins that both services write through the
/// <c>If-Match: *</c> method and not the upsert one; <c>RecalculateWriteNoCreateTests</c> pins what that method
/// sends.</para>
/// </remarks>
public class RecalculateServicesCallerFreeGuardTests
{
    private static readonly string[] RecalculateServices =
    {
        "Sprk.Bff.Api.Services.Finance.FinanceRollupService",
        "Sprk.Bff.Api.Services.ScorecardCalculatorService",
    };

    private static readonly string[] CallerContextTypes =
    {
        "Spaarke.Core.Auth",                          // AuthorizationService, AuthorizationContext
        "Spaarke.Dataverse.IAccessDataSource",
        "Microsoft.AspNetCore.Http",                  // HttpContext, IHttpContextAccessor
        "System.Security.Claims",                     // ClaimsPrincipal
        "Sprk.Bff.Api.Infrastructure.Auth",           // TokenHelper
        "Sprk.Bff.Api.Infrastructure.Authentication", // CallerResolution
        "Sprk.Bff.Api.Api.Filters",                   // FinanceAuthorizationFilter
    };

    [Fact(DisplayName = "Task 130: the recalculate services consult no caller — the check stays at the endpoint")]
    public void RecalculateServicesDoNotDependOnCallerContext()
    {
        var selected = Types.InAssembly(typeof(Program).Assembly)
            .That().HaveNameMatching("^(FinanceRollupService|ScorecardCalculatorService)$")
            .GetTypes()
            .Select(t => t.FullName)
            .ToList();

        // Non-vacuity: a rename must fail here rather than leave the rule examining nothing.
        Assert.Equal(RecalculateServices.OrderBy(n => n), selected.OrderBy(n => n));

        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().HaveNameMatching("^(FinanceRollupService|ScorecardCalculatorService)$")
            .ShouldNot().HaveDependencyOnAny(CallerContextTypes)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            "A recalculate service took a dependency on caller context. The caller check belongs in the route's "
            + "FinanceAuthorizationFilter: SpendSnapshotGenerationJobHandler calls FinanceRollupService with no "
            + "caller, so a check inside the service breaks the job or invites an app-only fallback. Failing types: "
            + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Theory(DisplayName = "Task 130: the recalculate writes are update-only (If-Match), never the upserting PATCH")]
    [InlineData("Services/Finance/FinanceRollupService.cs")]
    [InlineData("Services/ScorecardCalculatorService.cs")]
    public void RecalculateWritesAreUpdateOnly(string relativePath)
    {
        var path = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        var code = string.Join('\n', File.ReadAllLines(path)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                        && !l.TrimStart().StartsWith("*", StringComparison.Ordinal)));

        Assert.Contains(".UpdateExistingRecordFieldsAsync(", code);
        Assert.DoesNotContain(".UpdateRecordFieldsAsync(", code);
    }
}
