using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Step 0 covers EVERY <c>/api/finance</c> route (ebilling-r1 task 099; procedure-and-file-model section 5.3; owner
/// 2026-10-21, re-plan Q3: "step 0 applies to every route under /api/finance, so a future route cannot be missed"). Judged
/// on the real BFF booted from <c>Program.cs</c> (<see cref="BootedBff"/>, as Development and as Production): each endpoint
/// under that prefix carries <see cref="FinanceInternalCallersOnly"/> (set by <c>RequireInternalFinanceCaller()</c> on its
/// route group) AND runs <see cref="FinanceAuthorizationFilter"/> (<see cref="FinanceAuthorizationFilterAttached"/>), the
/// filter that honours the flag — a flag on a route without the filter would be inert. A route added without either fails
/// the build here.
/// </summary>
/// <remarks>
/// Enforced by: this guard (build) and the filter's fail-closed step 0 (run time: a flagged route whose resolvers are
/// missing or faulting refuses). Limit, stated exactly: presence is not behaviour. That the filter refuses on the flag is
/// proven by FinanceStepZeroTests and, per route, by the Finance contract tests; this guard proves only that every
/// <c>/api/finance</c> route asks for it.
/// </remarks>
[Collection(BootedBffCollection.Name)]
public sealed class FinanceStepZeroCoverageGuardTests
{
    // Compared without the leading slash: a group mapped as "api/finance" yields slash-less RawText.
    private const string Prefix = "api/finance";

    /// <summary>
    /// Routes under <see cref="Prefix"/> exempt from step 0, each with its reason (verb + parsed path → reason). EMPTY by
    /// owner decision 2026-10-21: the earlier exemptions (<c>/invoices/search</c>, <c>/matters/{matterId}/summary</c>,
    /// <c>/invoice-review/reject</c>, recalculate, rate cards, code-set mappings) were withdrawn. An entry needs an owner
    /// decision and uac-r2 review.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly BootedBff _booted;

    public FinanceStepZeroCoverageGuardTests(BootedBff booted) => _booted = booted;

    [Fact(DisplayName = "Task 099: every /api/finance endpoint carries the step-0 flag and runs the Finance authorization filter (Development and Production boots)")]
    public void EveryFinanceEndpointRunsStepZero()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var finance = app.Endpoints.Where(IsFinance).ToList();

            // Not vacuous: the /api/finance surface is ~35 routes; a boot that mapped none would pass every rule below.
            Assert.True(finance.Count >= 30,
                $"{app.HostEnvironmentName}: only {finance.Count} /api/finance endpoints were booted; a mapping gate is off.");

            var violations = Violations(app.Endpoints);
            Assert.True(violations.Count == 0,
                $"{app.HostEnvironmentName}: /api/finance endpoints without step 0. Add .RequireInternalFinanceCaller() to the route "
                + "group and authorize the route with .AddFinanceAuthorizationFilter(...) (section 5.3):\n"
                + string.Join("\n", violations));
        }
    }

    [Fact(DisplayName = "Task 099 must-fire: a planted /api/finance invoice route without the flag, one without the filter, and slash-less and ~/ forms are each reported")]
    public void TheGuardFires_OnAPlantedRouteWithoutStepZero()
    {
        var endpoints = new[]
        {
            Endpoint("/api/finance/invoices/{invoiceId}/planted", FinanceAuthorizationFilterAttached.Instance),
            Endpoint("/api/finance/invoices/{invoiceId}/unfiltered", FinanceInternalCallersOnly.Instance),
            Endpoint("api/finance/slashless"),
            Endpoint("~/API/FINANCE/upper"),
            Endpoint("/api/finance"),
        };

        var violations = Violations(endpoints);

        Assert.Equal(5, violations.Count);
        Assert.Contains(violations, v => v.Contains("/planted", StringComparison.Ordinal) && v.Contains("no step-0 flag", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("/unfiltered", StringComparison.Ordinal) && v.Contains("FinanceAuthorizationFilter", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("api/finance/slashless", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("~/API/FINANCE/upper", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 099 must-not-fire: flagged and filtered /api/finance routes, and routes outside the prefix, are not reported")]
    public void TheGuardDoesNotFire_OnCoveredRoutes_OrOutsideThePrefix()
    {
        var endpoints = new[]
        {
            Endpoint("/api/finance/invoices/{invoiceId}/review", FinanceInternalCallersOnly.Instance, FinanceAuthorizationFilterAttached.Instance),
            Endpoint("api/finance/rulesets", FinanceInternalCallersOnly.Instance, FinanceAuthorizationFilterAttached.Instance),
            Endpoint("/api/financeX/thing"),
            Endpoint("/api/v1/external/invoice-submissions/{id}"),
            Endpoint("/api/matters/{id}/recalculate-grades"),
            Endpoint("/api/other/finance"),
        };

        Assert.Empty(Violations(endpoints));
    }

    private static bool IsFinance(RouteEndpoint endpoint)
    {
        // Path-segment boundary from the parsed segments, not RawText: a pattern may begin "/", "~/" or nothing.
        var path = PathOf(endpoint);
        return path.Equals(Prefix, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string PathOf(RouteEndpoint endpoint) =>
        string.Join("/", endpoint.RoutePattern.PathSegments.Select(segment =>
            string.Concat(segment.Parts.Select(part => part is RoutePatternLiteralPart literal ? literal.Content : "{}"))));

    private static List<string> Violations(IEnumerable<RouteEndpoint> endpoints)
    {
        var violations = new List<string>();
        foreach (var endpoint in endpoints.Where(IsFinance))
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var verb = methods is { Count: > 0 } ? string.Join(",", methods) : "*";
            if (Exempt.ContainsKey($"{verb} {PathOf(endpoint)}"))
            {
                continue;
            }

            var display = $"{verb} {endpoint.RoutePattern.RawText} ";
            if (endpoint.Metadata.GetMetadata<FinanceInternalCallersOnly>() is null)
            {
                violations.Add(display + "has no step-0 flag (RequireInternalFinanceCaller)");
            }
            else if (endpoint.Metadata.GetMetadata<FinanceAuthorizationFilterAttached>() is null)
            {
                violations.Add(display + "is flagged but does not run FinanceAuthorizationFilter, so the flag is inert");
            }
        }

        return violations;
    }

    private static RouteEndpoint Endpoint(string pattern, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(metadata), pattern);
}
