using System.Collections;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// THE RUNTIME HALF OF THE ROUTE GUARD (unified-access-control-r2 task 167 f2-v2, main-session round 52).
///
/// <para>Round 52's principle: "verify BEHAVIOUR against the real application wherever a guard claims behaviour; source-text
/// heuristics remain only with exactly stated limits." Each claim below is judged against the REAL BFF booted in-process
/// (<see cref="BootedBff"/> — <c>Program.cs</c> end to end, as Development and as Production), not read from text:</para>
/// <list type="bullet">
///   <item><b>Sign-in is proven at run time</b> (round 52 item 1): an anonymous principal, evaluated through the booted app's
///   own <c>IAuthorizationService</c> and registered handlers, satisfies NO registered policy, not the default or the
///   fallback policy, and not the policy the authorization middleware builds for ANY endpoint that is not explicitly
///   anonymous; no registered authentication scheme authenticates a request that carries no credentials; and every stage of
///   the authorization pipeline resolves to the framework's own implementation
///   (<see cref="NoPolicyAndNoEndpointAdmitsAnAnonymousCallerAtRunTime"/>).</item>
///   <item><b>Development-only routes are absent in Production</b> (round 52 item 2): the app booted as Production maps none
///   of them, and the routes that differ between the two boots are exactly the pinned development-only ones
///   (<see cref="NoDevelopmentOnlyRouteIsMappedInProduction"/>).</item>
///   <item><b>Retired routes are absent from the running app</b> (round 52 item 3): a sweep entry whose key the scanner
///   no longer finds resolves only when neither boot maps its verb and path — checked by the ledger rule itself
///   (<see cref="TheSweepLedgerIsPinnedAndResolvesOnlyByCredit"/>).</item>
///   <item><b>The scanner and the app agree</b>: every route the guard rules on is mapped by the booted app, and every
///   route the app maps is one the guard rules on (<see cref="TheScannerAndTheBootedAppAgreeOnEveryRoute"/>). This is what
///   makes the two absence checks above meaningful — an absence read from a boot that maps less than the source (a gate
///   left off) would pass vacuously — and a route mapped from code the scanner does not read (a shared library, another
///   assembly) cannot escape every source rule. The explicitly anonymous surface at run time is exactly the pinned one
///   (<see cref="TheAnonymousSurfaceAtRunTimeIsThePinnedSurface"/>) — the one exception round 52 item 1 allows the sign-in
///   proof.</item>
/// </list>
///
/// <para><b>Limits, stated exactly.</b> The sign-in proof evaluates POLICIES with an anonymous principal; it does not send
/// HTTP requests, so it does not exercise middleware ORDER (a custom middleware placed before <c>UseAuthorization</c> that
/// answers a request itself is outside it — the source rules refuse the authorization-pipeline replacements that would
/// matter, and <c>EndpointMiddleware</c> throws for an endpoint with authorization metadata that no authorization
/// middleware saw). The booted endpoint table is the one THIS configuration produces: every feature gate that decides
/// whether a group is mapped is ON (<see cref="BootedBff"/>); a route gated on a setting no test sets would be absent from
/// both boots, and the agreement test then reports it as scanned but not mapped.</para>
/// </summary>
public partial class RouteAuthorizationGuardTests : IClassFixture<BootedBff>
{
    private readonly BootedBff _booted;

    public RouteAuthorizationGuardTests(BootedBff booted) => _booted = booted;

    // =============================================================================================
    // THE BOOTED ENDPOINT TABLE, AS ROUTE KEYS
    // =============================================================================================

    /// <summary>One booted endpoint: its HTTP methods (null = any method, as <c>MapHealthChecks</c> maps), the
    /// <see cref="RouteShape"/> of its pattern, and how to name it in a message.</summary>
    private sealed record BootedRoute(IReadOnlyList<string>? Methods, string PathShape, string Display, Endpoint Endpoint)
    {
        public bool Serves(string verb) => Methods is null || Methods.Contains(verb, StringComparer.OrdinalIgnoreCase);
    }

    private static List<BootedRoute> BootedRoutesOf(IEnumerable<RouteEndpoint> endpoints)
        => endpoints.Select(e =>
        {
            var raw = e.RoutePattern.RawText ?? throw new InvalidOperationException($"endpoint '{e.DisplayName}' has no route pattern text");
            var methods = e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var shape = PathShapeOf(raw);
            var display = $"{(methods is { Count: > 0 } ? string.Join(",", methods) : "*")} {raw}";
            return new BootedRoute(methods is { Count: > 0 } ? methods.ToList() : null, shape, display, (Endpoint)e);
        }).ToList();

    /// <summary>The path half of <see cref="RouteShape"/>: "/a/{}/b".</summary>
    private static string PathShapeOf(string path) => RouteShape("X " + path)[2..];

    /// <summary>The booted routes that serve route key <paramref name="key"/> ("VERB /path"), compared by verb and by
    /// path shape (parameter names and constraints erased, literal segments case-insensitive).</summary>
    private static List<BootedRoute> Serving(IEnumerable<BootedRoute> booted, string key)
    {
        var space = key.IndexOf(' ');
        var verb = key[..space];
        var shape = PathShapeOf(key[(space + 1)..]);
        return booted.Where(b => b.PathShape == shape && b.Serves(verb)).ToList();
    }

    private IReadOnlyList<BootedRoute> DevelopmentRoutes => BootedRoutesOf(_booted.Development.Endpoints);

    private IReadOnlyList<BootedRoute> ProductionRoutes => BootedRoutesOf(_booted.Production.Endpoints);

    /// <summary>Where the booted BFF maps <paramref name="key"/> ("Development: GET /api/x/{id}; …"), or null when neither
    /// boot does — the ledger's retired-route rule asks this (round 52 item 3).</summary>
    private string? MappedAtRunTime(string key)
    {
        var where = Serving(DevelopmentRoutes, key).Select(b => $"Development: {b.Display}")
            .Concat(Serving(ProductionRoutes, key).Select(b => $"Production: {b.Display}"))
            .ToList();
        return where.Count == 0 ? null : string.Join("; ", where);
    }

    private static IReadOnlySet<string> DevelopmentOnlyRoutes => ExplicitlyAnonymousRoutes
        .Where(r => r.Controls.Any(c => c.Kind == AnonymousControlKind.DevelopmentOnly))
        .Select(r => r.Route)
        .ToHashSet(StringComparer.Ordinal);

    [Fact(DisplayName = "Task 167 f2-v2: each boot is the environment it was asked for — the runtime checks below are about Development and Production, not about whatever the host chose")]
    public void EachBootIsTheEnvironmentItWasAskedFor()
    {
        Assert.Equal("Development", _booted.Development.HostEnvironmentName);
        Assert.Equal("Production", _booted.Production.HostEnvironmentName);
        Assert.True(_booted.Development.Endpoints.Count > 300, $"the Development boot mapped only {_booted.Development.Endpoints.Count} endpoints");
        Assert.True(_booted.Production.Endpoints.Count > 300, $"the Production boot mapped only {_booted.Production.Endpoints.Count} endpoints");
    }

    // =============================================================================================
    // THE SCANNER AND THE BOOTED APP AGREE (task 167 f2-v2)
    // =============================================================================================

    private static List<string> AgreementViolations(IReadOnlyList<string> scannedLiveKeys, IReadOnlyList<BootedRoute> development)
    {
        var violations = new List<string>();
        foreach (var key in scannedLiveKeys)
        {
            if (Serving(development, key).Count == 0)
            {
                violations.Add($"{key}: the scanner rules on it, but the booted app (Development, every mapping gate on) does not map it "
                               + "— the scanner is reading a registration that never runs, or the app's configuration gates it off");
            }
        }

        foreach (var route in development)
        {
            if (!scannedLiveKeys.Any(key => Serving(new[] { route }, key).Count > 0))
            {
                violations.Add($"{route.Display}: the booted app maps it, but the source scanner never found it — a route registered "
                               + "in a form the scanner cannot read is judged by no rule of this guard (register it with Map{Verb} on a "
                               + "route or group chain in a governed endpoint file)");
            }
        }

        return violations;
    }

    [Fact(DisplayName = "Task 167 f2-v2: the routes the guard rules on are exactly the routes the booted BFF maps")]
    public void TheScannerAndTheBootedAppAgreeOnEveryRoute()
    {
        var scanned = Real.Live.Select(r => r.Key).ToList();
        var violations = AgreementViolations(scanned, DevelopmentRoutes);
        Assert.True(
            violations.Count == 0,
            "The route guard's verdicts are about the routes its source scanner finds. These differ from the endpoint table of "
            + "the BFF booted from its own Program.cs (BootedBff), so a verdict would be about a route that does not exist, or "
            + "a mapped route would have no verdict at all.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: hundreds of routes on both sides.
        Assert.True(scanned.Count > 300, $"the scanner found only {scanned.Count} live routes");
    }

    [Fact(DisplayName = "Task 167 f2-v2 controls: a mapped route the scanner missed, and a scanned route the app does not map, each fail")]
    public void ScannerAgreement_NegativeControl_EachDisagreementFails()
    {
        RouteEndpoint Endpoint(string pattern, params string[] methods)
            => new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
                methods.Length == 0 ? EndpointMetadataCollection.Empty : new EndpointMetadataCollection(new HttpMethodMetadata(methods)), pattern);

        var booted = BootedRoutesOf(new[] { Endpoint("/api/a/{id:guid}", "GET"), Endpoint("/healthz") });

        // POSITIVE: a parameter renamed or re-constrained is the same route; a method-less endpoint (MapHealthChecks) serves GET.
        Assert.Empty(AgreementViolations(new[] { "GET /api/a/{x}", "GET /healthz" }, booted));

        // NEGATIVE: a route the scanner missed (mapped, never ruled on); a route the scanner rules on that the app does not
        // map; the same path under another verb.
        Assert.Contains(AgreementViolations(new[] { "GET /healthz" }, booted), v => v.Contains("GET /api/a/{id:guid}: the booted app maps it", StringComparison.Ordinal));
        Assert.Contains(AgreementViolations(new[] { "GET /api/a/{x}", "GET /healthz", "POST /api/b" }, booted),
            v => v.StartsWith("POST /api/b: the scanner rules on it", StringComparison.Ordinal));
        Assert.Contains(AgreementViolations(new[] { "POST /api/a/{x}", "GET /healthz" }, booted), v => v.StartsWith("POST /api/a/{x}", StringComparison.Ordinal));
    }

    // =============================================================================================
    // DEVELOPMENT-ONLY ROUTES ARE ABSENT IN PRODUCTION (round 52 item 2)
    // =============================================================================================

    private static List<string> DevelopmentOnlyViolations(
        IReadOnlyList<BootedRoute> development, IReadOnlyList<BootedRoute> production, IReadOnlySet<string> pinnedDevelopmentOnly)
    {
        var violations = new List<string>();
        foreach (var key in pinnedDevelopmentOnly.OrderBy(k => k, StringComparer.Ordinal))
        {
            foreach (var mapped in Serving(production, key))
            {
                violations.Add($"{key}: pinned as an IsDevelopment()-only mapping, but the BFF booted as PRODUCTION maps it ({mapped.Display}) "
                               + "— its development-only control is not in force");
            }
        }

        // The routes that differ between the two boots are exactly the pinned development-only routes: any other
        // environment-dependent mapping is a development-only route nobody pinned, reviewed or rate limited.
        foreach (var route in development.Where(d => !production.Any(p => p.PathShape == d.PathShape && SameMethods(p, d))))
        {
            if (!pinnedDevelopmentOnly.Any(key => Serving(new[] { route }, key).Count > 0))
            {
                violations.Add($"{route.Display}: mapped when the BFF boots as Development but not as Production — an environment-"
                               + "dependent route that ExplicitlyAnonymousRoutes does not pin as DevelopmentOnlyControl()");
            }
        }

        foreach (var route in production.Where(p => !development.Any(d => d.PathShape == p.PathShape && SameMethods(p, d))))
        {
            violations.Add($"{route.Display}: mapped ONLY when the BFF boots as Production — an environment-dependent route the "
                           + "Development boot (and every developer and test host) never sees");
        }

        return violations;

        static bool SameMethods(BootedRoute a, BootedRoute b)
            => a.Methods is null ? b.Methods is null
                : b.Methods is not null && a.Methods.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(b.Methods.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Task 167 f2-v2: the BFF booted as Production maps no development-only route (round 52 item 2)")]
    public void NoDevelopmentOnlyRouteIsMappedInProduction()
    {
        var pinned = DevelopmentOnlyRoutes;
        var violations = DevelopmentOnlyViolations(DevelopmentRoutes, ProductionRoutes, pinned);
        Assert.True(
            violations.Count == 0,
            "A development-only route is one the BFF maps only under IsDevelopment(). Read from source, that control can be "
            + "defeated by anything that changes the environment the check reads; here the REAL app is booted with "
            + "EnvironmentName = Production and its endpoint table is read.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the pinned development-only route really is mapped in Development, and only there.
        Assert.NotEmpty(pinned);
        foreach (var key in pinned)
        {
            Assert.NotEmpty(Serving(DevelopmentRoutes, key));
            Assert.Empty(Serving(ProductionRoutes, key));
        }
    }

    [Fact(DisplayName = "Task 167 f2-v2 controls: a development-only route mapped in Production, and an unpinned environment-dependent route, each fail")]
    public void DevelopmentOnly_NegativeControl_EachEnvironmentLeakFails()
    {
        RouteEndpoint Endpoint(string pattern, string method)
            => new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(new HttpMethodMetadata(new[] { method })), pattern);

        var common = Endpoint("/api/a", "GET");
        var debug = Endpoint("/api/office/save-debug", "POST");
        var pinned = new HashSet<string>(StringComparer.Ordinal) { "POST /api/office/save-debug" };

        // POSITIVE: the pinned route only in Development.
        Assert.Empty(DevelopmentOnlyViolations(BootedRoutesOf(new[] { common, debug }), BootedRoutesOf(new[] { common }), pinned));

        // NEGATIVE: the verifier's seed — the environment flipped before the IsDevelopment() check, so Production maps it.
        Assert.Contains(DevelopmentOnlyViolations(BootedRoutesOf(new[] { common, debug }), BootedRoutesOf(new[] { common, debug }), pinned),
            v => v.Contains("the BFF booted as PRODUCTION maps it", StringComparison.Ordinal));

        // NEGATIVE: another environment-dependent route, not pinned; and a Production-only route.
        var other = Endpoint("/api/debug/dump", "GET");
        Assert.Contains(DevelopmentOnlyViolations(BootedRoutesOf(new[] { common, debug, other }), BootedRoutesOf(new[] { common }), pinned),
            v => v.StartsWith("GET /api/debug/dump: mapped when the BFF boots as Development but not as Production", StringComparison.Ordinal));
        Assert.Contains(DevelopmentOnlyViolations(BootedRoutesOf(new[] { common, debug }), BootedRoutesOf(new[] { common, other }), pinned),
            v => v.StartsWith("GET /api/debug/dump: mapped ONLY when the BFF boots as Production", StringComparison.Ordinal));
    }

    // =============================================================================================
    // THE ANONYMOUS SURFACE AT RUN TIME
    // =============================================================================================

    private static List<string> RuntimeAnonymousSurfaceViolations(IReadOnlyList<BootedRoute> booted, IReadOnlyCollection<string> expected, string label)
    {
        var anonymous = booted.Where(b => b.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null).ToList();
        var violations = new List<string>();
        violations.AddRange(anonymous.Where(a => !expected.Any(key => Serving(new[] { a }, key).Count > 0))
            .Select(a => $"{label}: {a.Display} carries AllowAnonymous at run time but is not in ExplicitlyAnonymousRoutes"));
        violations.AddRange(expected.Where(key => Serving(anonymous, key).Count == 0)
            .Select(key => $"{label}: {key} is pinned in ExplicitlyAnonymousRoutes but no endpoint serving it carries AllowAnonymous at run time"));
        return violations;
    }

    [Fact(DisplayName = "Task 167 f2-v2: the explicitly anonymous surface at run time is exactly the pinned surface (Development: all of it; Production: all but the development-only routes)")]
    public void TheAnonymousSurfaceAtRunTimeIsThePinnedSurface()
    {
        var all = ExplicitlyAnonymousRoutes.Select(r => r.Route).ToList();
        var violations = RuntimeAnonymousSurfaceViolations(DevelopmentRoutes, all, "Development")
            .Concat(RuntimeAnonymousSurfaceViolations(ProductionRoutes, all.Except(DevelopmentOnlyRoutes).ToList(), "Production"))
            .ToList();
        Assert.True(
            violations.Count == 0,
            "The routes a caller reaches without signing in, read from the booted app's endpoint metadata, differ from the pinned "
            + "ExplicitlyAnonymousRoutes.\n\n  " + string.Join("\n  ", violations));
    }

    // =============================================================================================
    // SIGN-IN IS PROVEN AT RUN TIME (round 52 item 1)
    // =============================================================================================

    /// <summary>The framework's own implementation of each authorization-pipeline stage. A replacement of any of them could
    /// decide a request differently from the policies evaluated below.</summary>
    private static readonly IReadOnlyList<(Type Service, Type Implementation)> FrameworkAuthorizationStages = new[]
    {
        (typeof(IAuthorizationService), typeof(DefaultAuthorizationService)),
        (typeof(IAuthorizationPolicyProvider), typeof(DefaultAuthorizationPolicyProvider)),
        (typeof(IAuthorizationHandlerProvider), typeof(DefaultAuthorizationHandlerProvider)),
        (typeof(IAuthorizationEvaluator), typeof(DefaultAuthorizationEvaluator)),
        (typeof(IAuthorizationHandlerContextFactory), typeof(DefaultAuthorizationHandlerContextFactory)),
        (typeof(IPolicyEvaluator), typeof(PolicyEvaluator)),
        (typeof(IAuthorizationMiddlewareResultHandler), typeof(AuthorizationMiddlewareResultHandler)),
    };

    /// <summary>Every policy NAME registered in <paramref name="options"/> — <c>AddPolicy</c>, the AuthorizationBuilder forms,
    /// any spelling — read from the options' own policy map (a private member; this fails loudly if the framework renames it).</summary>
    private static IReadOnlyList<string> RegisteredPolicyNames(AuthorizationOptions options)
    {
        var maps = typeof(AuthorizationOptions)
            .GetMembers(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(m => m switch
            {
                PropertyInfo p when p.GetIndexParameters().Length == 0 => p.GetValue(options),
                FieldInfo f => f.GetValue(options),
                _ => null,
            })
            .OfType<IDictionary>()
            .Distinct()
            .ToList();
        if (maps.Count != 1)
        {
            throw new InvalidOperationException(
                $"AuthorizationOptions holds {maps.Count} private dictionaries, expected exactly 1 (its policy map) — the framework "
                + "changed; update RegisteredPolicyNames rather than letting the runtime sign-in proof read nothing");
        }

        return maps[0].Keys.Cast<string>().ToList();
    }

    /// <summary>The policy ASP.NET Core's <c>AuthorizationMiddleware</c> builds for <paramref name="endpoint"/> (ASP.NET Core 10):
    /// its <c>IAuthorizeData</c> and <c>AuthorizationPolicy</c> metadata combined through the policy provider — which applies
    /// the DEFAULT policy for a bare <c>RequireAuthorization()</c> and the FALLBACK policy when the endpoint declares nothing —
    /// then its <c>IAuthorizationRequirementData</c> requirements added.</summary>
    private static async Task<AuthorizationPolicy?> MiddlewarePolicyAsync(IAuthorizationPolicyProvider provider, Endpoint endpoint)
    {
        var policy = await AuthorizationPolicy.CombineAsync(
            provider, endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
        var requirementData = endpoint.Metadata.GetOrderedMetadata<IAuthorizationRequirementData>();
        if (requirementData.Count > 0)
        {
            var builder = new AuthorizationPolicyBuilder();
            foreach (var data in requirementData)
            {
                foreach (var requirement in data.GetRequirements())
                {
                    builder.AddRequirements(requirement);
                }
            }

            policy = policy is null ? builder.Build() : AuthorizationPolicy.Combine(policy, builder.Build());
        }

        return policy;
    }

    /// <summary>
    /// "label: what — why" for every way the app in <paramref name="root"/> lets an ANONYMOUS caller through: a registered
    /// policy, the default or fallback policy, or the middleware policy of a non-anonymous endpoint that an anonymous
    /// principal satisfies (evaluated through the app's own <c>IAuthorizationService</c>, with an <c>HttpContext</c> as the
    /// resource exactly as the middleware passes it); an evaluation that throws (unprovable); an endpoint with no policy at
    /// all; an authentication scheme that authenticates a request with no credentials; and a pipeline stage that is not the
    /// framework's own.
    /// </summary>
    private static async Task<List<string>> AnonymousAdmissionViolations(
        IServiceProvider root, IEnumerable<RouteEndpoint> endpoints, string label, List<string>? evaluated = null)
    {
        var violations = new List<string>();
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;

        foreach (var (service, implementation) in FrameworkAuthorizationStages)
        {
            // The framework's own type, or a framework-internal subclass of it from the SAME framework assembly (ASP.NET
            // Core 10 registers DefaultAuthorizationServiceImpl, its metrics-emitting DefaultAuthorizationService).
            var resolved = services.GetService(service)?.GetType();
            if (resolved is null || !implementation.IsAssignableFrom(resolved) || resolved.Assembly != implementation.Assembly)
            {
                violations.Add($"{label}: {service.Name} resolves to {resolved?.FullName ?? "nothing"}, not the framework's "
                               + $"{implementation.Name} — a replaced pipeline stage can decide a request differently from every policy");
            }
        }

        var authorization = services.GetRequiredService<IAuthorizationService>();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var anonymous = new[] { ("a principal with no identity", new ClaimsPrincipal()), ("an unauthenticated identity", new ClaimsPrincipal(new ClaimsIdentity())) };

        async Task CheckAsync(string what, AuthorizationPolicy? policy, Endpoint? endpoint)
        {
            evaluated?.Add(what);
            if (policy is null)
            {
                violations.Add($"{label}: {what} — no authorization policy applies at all (no metadata, and no fallback policy)");
                return;
            }

            foreach (var (who, user) in anonymous)
            {
                var http = new DefaultHttpContext { RequestServices = services, User = user };
                if (endpoint is not null)
                {
                    http.SetEndpoint(endpoint);
                }

                AuthorizationResult result;
                try
                {
                    result = await authorization.AuthorizeAsync(user, http, policy);
                }
                catch (Exception ex)
                {
                    violations.Add($"{label}: {what} — evaluating it for {who} threw {ex.GetType().Name}: {ex.Message} (unprovable)");
                    return;
                }

                if (result.Succeeded)
                {
                    violations.Add($"{label}: {what} ADMITS AN ANONYMOUS CALLER ({who}) — requirements: "
                                   + string.Join(", ", policy.Requirements.Select(r => r.GetType().Name)));
                    return;
                }
            }
        }

        var options = services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        foreach (var name in RegisteredPolicyNames(options).Order(StringComparer.Ordinal))
        {
            await CheckAsync($"policy '{name}'", await provider.GetPolicyAsync(name), null);
        }

        await CheckAsync("the default policy (a bare RequireAuthorization())", await provider.GetDefaultPolicyAsync(), null);
        await CheckAsync("the fallback policy (an endpoint that declares nothing)", await provider.GetFallbackPolicyAsync(), null);

        foreach (var endpoint in endpoints.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null))
        {
            AuthorizationPolicy? policy;
            try
            {
                policy = await MiddlewarePolicyAsync(provider, endpoint);
            }
            catch (Exception ex)
            {
                violations.Add($"{label}: endpoint {endpoint.RoutePattern.RawText} — its policy cannot be built: {ex.Message}");
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            await CheckAsync($"endpoint {(methods is { Count: > 0 } ? string.Join(",", methods) : "*")} {endpoint.RoutePattern.RawText}", policy, endpoint);
        }

        // Authentication: a request that carries NO credentials must authenticate under no scheme.
        var authentication = services.GetRequiredService<IAuthenticationService>();
        foreach (var scheme in await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync())
        {
            evaluated?.Add($"scheme {scheme.Name}");
            var http = new DefaultHttpContext { RequestServices = services };
            AuthenticateResult result;
            try
            {
                result = await authentication.AuthenticateAsync(http, scheme.Name);
            }
            catch (Exception ex)
            {
                violations.Add($"{label}: authentication scheme '{scheme.Name}' threw {ex.GetType().Name} on a request with no credentials: {ex.Message} (unprovable)");
                continue;
            }

            if (result.Succeeded)
            {
                violations.Add($"{label}: authentication scheme '{scheme.Name}' AUTHENTICATES a request that carries no credentials "
                               + $"(as '{result.Principal?.Identity?.Name}') — an anonymous request becomes signed in");
            }
        }

        return violations;
    }

    [Fact(DisplayName = "Task 167 f2-v2: no policy and no endpoint of the booted BFF admits an anonymous caller (round 52 item 1)")]
    public async Task NoPolicyAndNoEndpointAdmitsAnAnonymousCallerAtRunTime()
    {
        var violations = new List<string>();
        var evaluated = new List<string>();
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            violations.AddRange(await AnonymousAdmissionViolations(app.Services, app.Endpoints, app.Environment, evaluated));
        }

        Assert.True(
            violations.Count == 0,
            "Sign-in is proven at run time: an anonymous principal is evaluated, through the booted BFF's own authorization "
            + "service and registered handlers, against every registered policy, the default and fallback policies, and the "
            + "policy the authorization middleware builds for every endpoint that is not explicitly anonymous. Any success is a "
            + "route an anonymous caller reaches — whatever shape the handler that allowed it has.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the policies the routes name are there, and the endpoints were evaluated.
        var options = _booted.Production.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var names = RegisteredPolicyNames(options);
        Assert.Contains("SystemAdmin", names);
        Assert.True(names.Count > 20, $"only {names.Count} registered policies were read");
        Assert.NotNull(options.FallbackPolicy);
        var endpointsEvaluated = evaluated.Count(e => e.StartsWith("endpoint ", StringComparison.Ordinal));
        Assert.True(endpointsEvaluated > 700, $"only {endpointsEvaluated} endpoint policies were evaluated across both boots");
        Assert.Contains(evaluated, e => e.StartsWith("scheme ", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------------------------------
    // The verifier's seeds, compiled — the same function evaluated over a REAL ASP.NET Core authorization stack (not the
    // BFF's), so the proof is shown to fire on every build without editing the BFF.
    // ---------------------------------------------------------------------------------------------

    public sealed class SeedOpenRequirement : IAuthorizationRequirement
    {
    }

    /// <summary>The f2-v1 verifier's handler: succeeds every requirement through a lambda parameter named like the handler's.</summary>
    public sealed class SeedOpenHandler : AuthorizationHandler<SeedOpenRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SeedOpenRequirement requirement)
        {
            context.Requirements.ToList().ForEach(requirement => context.Succeed(requirement));
            return Task.CompletedTask;
        }
    }

    /// <summary>The verifier's local-function variant.</summary>
    public sealed class SeedLocalFunctionHandler : AuthorizationHandler<SeedOpenRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SeedOpenRequirement requirement)
        {
            void Approve(IAuthorizationRequirement requirement) => context.Succeed(requirement);
            foreach (var each in context.Requirements.ToList())
            {
                Approve(each);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>The verifier's reflection variant.</summary>
    public sealed class SeedReflectionHandler : AuthorizationHandler<SeedOpenRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, SeedOpenRequirement requirement)
        {
            foreach (var r in context.Requirements.ToList())
            {
                typeof(AuthorizationHandlerContext).GetMethod("Succeed")!.Invoke(context, new object[] { r });
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A replaced authorization service that allows everything.</summary>
    public sealed class SeedAllowEverythingService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    [Fact(DisplayName = "Task 167 f2-v2 controls: a handler that succeeds every requirement (lambda, local function, reflection), a credential-less scheme and a replaced service each fail the runtime proof")]
    public async Task RuntimeSignIn_NegativeControl_EachAdmissionFails()
    {
        RouteEndpoint Endpoint(string pattern, params object[] metadata)
            => new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
                new EndpointMetadataCollection(metadata.Append(new HttpMethodMetadata(new[] { "GET" }))), pattern);

        var endpoints = new[]
        {
            Endpoint("/api/admin/x", new AuthorizeAttribute("SystemAdmin")),
            Endpoint("/api/bare", new AuthorizeAttribute()),
            Endpoint("/api/nothing"),
            Endpoint("/healthz", new AllowAnonymousAttribute()),
        };

        ServiceProvider Build(Action<IServiceCollection>? seed = null, bool seedRequirement = false)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddAuthentication();
            services.AddAuthorization(o =>
            {
                Sprk.Bff.Api.Infrastructure.DI.AuthorizationModule.ApplyFallbackPolicy(o);
                o.AddPolicy("SystemAdmin", p =>
                {
                    p.RequireAuthenticatedUser();
                    if (seedRequirement)
                    {
                        p.Requirements.Add(new SeedOpenRequirement());
                    }
                });
            });
            seed?.Invoke(services);
            return services.BuildServiceProvider();
        }

        // POSITIVE: the BFF's shapes — a proven SystemAdmin, the default policy, the fallback — admit nobody.
        await using (var clean = Build())
        {
            Assert.Empty(await AnonymousAdmissionViolations(clean, endpoints, "control"));
        }

        // NEGATIVE: the verifier's three handler seeds — SystemAdmin gains SeedOpenRequirement (an add-only statement the
        // source rule allows) and a handler for it succeeds EVERY requirement, sign-in included.
        foreach (var handler in new[] { typeof(SeedOpenHandler), typeof(SeedLocalFunctionHandler), typeof(SeedReflectionHandler) })
        {
            await using var seeded = Build(s => s.AddSingleton(typeof(IAuthorizationHandler), handler), seedRequirement: true);
            var violations = await AnonymousAdmissionViolations(seeded, endpoints, "control");
            Assert.Contains(violations, v => v.Contains("policy 'SystemAdmin' ADMITS AN ANONYMOUS CALLER", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("endpoint GET /api/admin/x ADMITS AN ANONYMOUS CALLER", StringComparison.Ordinal));
        }

        // NEGATIVE: a scheme that signs in a request with no credentials; a replaced authorization service.
        await using (var everyone = Build(s => s.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SeedEveryoneSchemeHandler>("Seed", _ => { })))
        {
            Assert.Contains(await AnonymousAdmissionViolations(everyone, endpoints, "control"),
                v => v.Contains("authentication scheme 'Seed' AUTHENTICATES a request that carries no credentials", StringComparison.Ordinal));
        }

        await using (var replaced = Build(s => s.AddSingleton<IAuthorizationService, SeedAllowEverythingService>()))
        {
            var violations = await AnonymousAdmissionViolations(replaced, endpoints, "control");
            Assert.Contains(violations, v => v.Contains("IAuthorizationService resolves to", StringComparison.Ordinal));
            Assert.Contains(violations, v => v.Contains("the fallback policy (an endpoint that declares nothing) ADMITS", StringComparison.Ordinal));
        }

        // NEGATIVE: no fallback policy — an endpoint that declares nothing has no policy at all.
        await using (var noFallback = Build(s => s.Configure<AuthorizationOptions>(o => o.FallbackPolicy = null)))
        {
            Assert.Contains(await AnonymousAdmissionViolations(noFallback, endpoints, "control"),
                v => v.Contains("endpoint GET /api/nothing — no authorization policy applies at all", StringComparison.Ordinal));
        }
    }

    /// <summary>The credential-less scheme, as an <see cref="AuthenticationHandler{TOptions}"/>.</summary>
    public sealed class SeedEveryoneSchemeHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public SeedEveryoneSchemeHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "nobody") }, Scheme.Name)), Scheme.Name)));
    }
}
