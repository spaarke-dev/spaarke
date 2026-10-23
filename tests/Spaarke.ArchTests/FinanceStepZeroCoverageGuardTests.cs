using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
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
/// <para><b>Three selectors, same table.</b> A route is judged when its path is under <c>api/finance</c> (the prefix selector),
/// OR its handler is declared in the <c>Sprk.Bff.Api.Api.Finance</c> namespace (read from the endpoint's
/// <see cref="MethodInfo"/> metadata), OR it runs <see cref="FinanceAuthorizationFilter"/>
/// (<see cref="FinanceAuthorizationFilterAttached"/>) from a handler not listed in <see cref="NonFinanceFilterUsers"/> - so a
/// Finance handler mapped under another prefix (e.g. <c>/api/{area}/...</c>) cannot be missed. The filter is not Finance-only,
/// hence that list.</para>
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

    /// <summary>The namespace whose handlers are finance handlers wherever they are mapped.</summary>
    private const string FinanceHandlerNamespace = "Sprk.Bff.Api.Api.Finance";

    /// <summary>
    /// Handler classes OUTSIDE the Finance namespace that deliberately run <see cref="FinanceAuthorizationFilter"/> (it is the
    /// platform's one multi-id record-rights filter, not finance-only) on routes that are NOT <c>/api/finance</c> routes, so step 0
    /// does not apply to them (owner 2026-10-21: step 0 is every <c>/api/finance</c> route). Keyed by the outermost declaring type
    /// of the handler. An entry needs an owner decision and uac-r2 review, and must still exist (a stale entry fails).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NonFinanceFilterUsers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Api.ScorecardCalculatorEndpoints"] = "scorecard grade recalculation (/api/matters, /api/projects); not a finance route",
        ["Sprk.Bff.Api.Api.Insights.InsightsSearchEndpoint"] = "insights search (/api/insights); not a finance route",
        ["Sprk.Bff.Api.Api.Workspace.WorkspaceAiEndpoints"] = "workspace AI summary (/api/workspace/ai); not a finance route",
    };

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

            // The handler selector is not vacuous either: the Finance-namespace handlers are the same ~35 routes.
            var byHandler = app.Endpoints.Where(HandlerIsFinance).ToList();
            Assert.True(byHandler.Count >= 30,
                $"{app.HostEnvironmentName}: only {byHandler.Count} endpoints with a {FinanceHandlerNamespace} handler were found; the handler selector reads nothing (MethodInfo metadata missing?).");

            var violations = Violations(app.Endpoints);
            Assert.True(violations.Count == 0,
                $"{app.HostEnvironmentName}: finance endpoints without step 0. Add .RequireInternalFinanceCaller() to the route "
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

    [Fact(DisplayName = "Task 090 must-not-fire: a flagged /api/finance route that runs the Stored File access filter (a step-0 filter) is not reported")]
    public void TheGuardDoesNotFire_OnAFlaggedRouteRunningTheStoredFileAccessFilter()
    {
        var endpoints = new[]
        {
            Endpoint("/api/finance/files/{fileId}/content", FinanceInternalCallersOnly.Instance, Sprk.Bff.Api.Api.Finance.StoredFileAccessFilterAttached.Instance),
        };

        Assert.Empty(Violations(endpoints));
    }

    [Fact(DisplayName = "Task 090 must-fire: the Stored File access filter without the flag, and the flag with a marker that is not a step-0 filter, are each reported")]
    public void TheGuardFires_OnTheStoredFileRouteWithoutTheFlag_OrWithANonStepZeroMarker()
    {
        var endpoints = new[]
        {
            Endpoint("/api/finance/files/{fileId}/content", Sprk.Bff.Api.Api.Finance.StoredFileAccessFilterAttached.Instance),
            Endpoint("/api/finance/files/{fileId}/other", FinanceInternalCallersOnly.Instance, new object()),
        };

        var violations = Violations(endpoints);

        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, v => v.Contains("/content", StringComparison.Ordinal) && v.Contains("no step-0 flag", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("/other", StringComparison.Ordinal) && v.Contains("inert", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 090: the real table maps the Stored File download with the step-0 flag and the Stored File access filter, in both boots")]
    public void TheStoredFileDownloadIsMappedWithStepZero()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var download = Assert.Single(app.Endpoints, e => PathOf(e).Equals("api/finance/files/{}/content", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(download.Metadata.GetMetadata<FinanceInternalCallersOnly>());
            Assert.NotNull(download.Metadata.GetMetadata<Sprk.Bff.Api.Api.Finance.StoredFileAccessFilterAttached>());
        }
    }

    /// <summary>
    /// The markers trusted to mean "this route's filter runs step 0 first". The coverage check above accepts ANY implementer of
    /// <see cref="IFinanceStepZeroFilter"/>, so a new one must be a reviewed edit HERE (task 090, review pass 1): its filter must call
    /// <c>FinanceAuthorizationFilter.EvaluateStepZeroAsync</c> before any record read, proven by its own must-fire tests.
    /// </summary>
    private static readonly string[] TrustedStepZeroMarkers =
    [
        "Sprk.Bff.Api.Api.Filters.FinanceAuthorizationFilterAttached",
        "Sprk.Bff.Api.Api.Finance.StoredFileAccessFilterAttached",
    ];

    [Fact(DisplayName = "Task 090: the implementers of IFinanceStepZeroFilter are exactly the reviewed set (a new step-0 marker is a reviewed edit, never silent)")]
    public void StepZeroMarkersAreAClosedSet()
    {
        var implementers = typeof(IFinanceStepZeroFilter).Assembly.GetTypes()
            .Where(t => t is { IsClass: true } && typeof(IFinanceStepZeroFilter).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(TrustedStepZeroMarkers.OrderBy(n => n, StringComparer.Ordinal), implementers);

        // Must-fire control: the comparison notices one more implementer.
        Assert.NotEqual(TrustedStepZeroMarkers.OrderBy(n => n, StringComparer.Ordinal), implementers.Append("Planted.Marker").ToArray());
    }

    [Fact(DisplayName = "Task 099: the NonFinanceFilterUsers list is current - the real table holds exactly those non-finance users of the Finance filter, no more, no fewer")]
    public void TheNonFinanceFilterUsersListIsCurrent()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var outsiders = app.Endpoints
                .Where(e => !IsFinance(e) && !HandlerIsFinance(e) && e.Metadata.GetMetadata<FinanceAuthorizationFilterAttached>() is not null)
                .Select(e => OutermostHandlerType(e)?.FullName ?? "<unknown handler>")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(NonFinanceFilterUsers.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList(), outsiders);
        }
    }

    [Fact(DisplayName = "Task 099 must-fire: a real Finance-namespace handler mapped OUTSIDE /api/finance (method group and lambda), and an unlisted handler that runs the Finance filter, are reported")]
    public void TheGuardFires_OnAFinanceHandlerUnderAnotherPrefix()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        using var app = builder.Build();
        app.MapGet("/api/other/{id}/planted-method-group", Sprk.Bff.Api.Api.Finance.PlantedFinanceHandlers.Planted);
        app.MapGet("/api/other/planted-lambda", Sprk.Bff.Api.Api.Finance.PlantedFinanceHandlers.PlantedLambda());
        app.MapGet("/api/other/not-a-finance-handler", () => "ok").WithMetadata(FinanceAuthorizationFilterAttached.Instance);
        app.MapGet("/api/other/innocent", () => "ok");
        app.MapGet("/api/other/flagged", Sprk.Bff.Api.Api.Finance.PlantedFinanceHandlers.Planted)
            .WithMetadata(FinanceInternalCallersOnly.Instance, FinanceAuthorizationFilterAttached.Instance);

        var violations = Violations(EndpointsOf(app));

        Assert.Equal(3, violations.Count);
        Assert.Contains(violations, v => v.Contains("planted-method-group", StringComparison.Ordinal) && v.Contains("no step-0 flag", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("planted-lambda", StringComparison.Ordinal) && v.Contains("no step-0 flag", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("not-a-finance-handler", StringComparison.Ordinal) && v.Contains("no step-0 flag", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains("innocent", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, v => v.Contains("/flagged", StringComparison.Ordinal)); // the same handler, flagged and filtered: not reported
    }

    [Fact(DisplayName = "Task 099 must-not-fire: a flagged and filtered Finance-namespace handler under another prefix is not reported, and neither is any endpoint of the real table")]
    public void TheGuardDoesNotFire_OnACoveredFinanceHandlerUnderAnotherPrefix_OrOnTheRealTable()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        using var app = builder.Build();
        app.MapGet("/api/other/covered", Sprk.Bff.Api.Api.Finance.PlantedFinanceHandlers.Planted)
            .WithMetadata(FinanceInternalCallersOnly.Instance, FinanceAuthorizationFilterAttached.Instance);

        Assert.Empty(Violations(EndpointsOf(app)));
        Assert.Empty(Violations(_booted.Development.Endpoints));
        Assert.Empty(Violations(_booted.Production.Endpoints));
    }

    [Fact(DisplayName = "Task 099: a request scope resolves the real caller resolvers step 0 needs, in the Development and Production boots")]
    public void TheStepZeroResolversResolveInEveryBoot()
    {
        AssertStepZeroResolvers(_booted.Development);
        AssertStepZeroResolvers(_booted.Production);
    }

    [Fact(DisplayName = "Task 099: with NO AI module composed, the Communication and Notifications modules alone register the caller resolvers step 0 needs (the AI module is not the only registrant)")]
    public void TheStepZeroResolversAreRegisteredWithoutTheAiModule()
    {
        // Step 0 fails closed when a resolver is missing, which would refuse every caller on every /api/finance route. The AI
        // module registers ICallerSystemUserResolver only inside AddAnalysisOrchestrationServices (Analysis AND DocumentIntelligence
        // on); the unconditional registrant is CommunicationModule (TryAddScoped), ISystemUserIdentityResolver is NotificationsModule's.
        // A booted host cannot stand in for "AI off": that combination does not start (GateCombinationConstructibilityTests ledger;
        // ChatEndpoints binds an unregistered IChatClient), so the two modules are composed alone and the descriptors read.
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        Sprk.Bff.Api.Infrastructure.DI.CommunicationModule.AddCommunicationModule(services, configuration);
        Sprk.Bff.Api.Infrastructure.DI.NotificationsModule.AddNotificationsModule(services, configuration);

        var caller = Assert.Single(services, d => d.ServiceType == typeof(ICallerSystemUserResolver));
        Assert.Equal(typeof(CallerSystemUserResolver), caller.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, caller.Lifetime);
        var identity = Assert.Single(services, d => d.ServiceType == typeof(ISystemUserIdentityResolver));
        Assert.Equal(typeof(SystemUserIdentityResolver), identity.ImplementationType);
    }

    private static void AssertStepZeroResolvers(BootedApp app)
    {
        using var scope = app.Services.CreateScope();
        var caller = scope.ServiceProvider.GetService<ICallerSystemUserResolver>();
        var identity = scope.ServiceProvider.GetService<ISystemUserIdentityResolver>();

        Assert.True(caller is not null,
            $"{app.Environment}: no ICallerSystemUserResolver is registered, so step 0 would refuse every caller (fail closed). CommunicationModule must TryAddScoped it.");
        Assert.True(identity is not null,
            $"{app.Environment}: no ISystemUserIdentityResolver is registered, so step 0 would refuse every caller (fail closed). NotificationsModule registers it.");
        Assert.IsType<CallerSystemUserResolver>(caller);
        Assert.IsType<SystemUserIdentityResolver>(identity);
    }

    private static List<RouteEndpoint> EndpointsOf(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>().ToList();

    private static bool IsFinance(RouteEndpoint endpoint)
    {
        // Path-segment boundary from the parsed segments, not RawText: a pattern may begin "/", "~/" or nothing.
        var path = PathOf(endpoint);
        return path.Equals(Prefix, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The handler's outermost declaring type (a lambda's closure class is nested in the class that maps it).</summary>
    private static Type? OutermostHandlerType(RouteEndpoint endpoint)
    {
        var type = endpoint.Metadata.GetMetadata<MethodInfo>()?.DeclaringType;
        while (type?.DeclaringType is { } outer)
        {
            type = outer;
        }

        return type;
    }

    /// <summary>Second selector: the handler is declared in <see cref="FinanceHandlerNamespace"/>, whatever the route path.</summary>
    private static bool HandlerIsFinance(RouteEndpoint endpoint) =>
        OutermostHandlerType(endpoint)?.Namespace is { } ns
        && (ns.Equals(FinanceHandlerNamespace, StringComparison.Ordinal) || ns.StartsWith(FinanceHandlerNamespace + ".", StringComparison.Ordinal));

    /// <summary>
    /// Third selector: the route runs <see cref="FinanceAuthorizationFilter"/> from a handler that is not on
    /// <see cref="NonFinanceFilterUsers"/>, so a new, unlisted user of the filter outside Finance has to be decided.
    /// </summary>
    private static bool RunsFinanceFilterUnlisted(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<FinanceAuthorizationFilterAttached>() is not null
        && !(OutermostHandlerType(endpoint)?.FullName is { } name && NonFinanceFilterUsers.ContainsKey(name));

    private static bool IsJudged(RouteEndpoint endpoint) => IsFinance(endpoint) || HandlerIsFinance(endpoint) || RunsFinanceFilterUnlisted(endpoint);

    private static string PathOf(RouteEndpoint endpoint) =>
        string.Join("/", endpoint.RoutePattern.PathSegments.Select(segment =>
            string.Concat(segment.Parts.Select(part => part is RoutePatternLiteralPart literal ? literal.Content : "{}"))));

    private static List<string> Violations(IEnumerable<RouteEndpoint> endpoints)
    {
        var violations = new List<string>();
        foreach (var endpoint in endpoints.Where(IsJudged))
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
            else if (endpoint.Metadata.GetMetadata<IFinanceStepZeroFilter>() is null)
            {
                // ebilling-r1 task 090: any filter whose marker implements IFinanceStepZeroFilter runs step 0 first (FinanceAuthorizationFilter,
                // StoredFileAccessFilter); a marker may implement it only when its own tests prove that.
                violations.Add(display + "is flagged but does not run FinanceAuthorizationFilter (or another step-0 filter), so the flag is inert");
            }
        }

        return violations;
    }

    private static RouteEndpoint Endpoint(string pattern, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(metadata), pattern);
}
