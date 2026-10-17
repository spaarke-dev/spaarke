using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Sprk.Bff.Api.Api.ExternalAccess;
using Xunit;
using AccessRights = Spaarke.Dataverse.AccessRights;

namespace Spaarke.ArchTests;

/// <summary>
/// The workforce privilege ceiling covers EVERY <c>/api/v1/external</c> endpoint (unified-access-control-r2 task 186,
/// review round 1 F1). Judged on the real BFF booted from <c>Program.cs</c> (<see cref="BootedBff"/>, as Development and as
/// Production): each endpoint under that prefix carries exactly one <see cref="WorkforceCeilingMetadata"/> — a requirement
/// set the one <see cref="WorkforceCeilingEndpointFilter"/> checks, a check in its handler, or a written exemption — and an
/// endpoint whose set the filter checks is on a group that runs the filter. A route added without a declaration fails the
/// build here instead of skipping the ceiling silently.
/// </summary>
/// <remarks>
/// Enforced by: this guard (build) and the filter's fail-closed default (run time: an undeclared endpoint denies a
/// workforce contact). Limit, stated exactly: presence is not correctness. An InHandler declaration is trusted to be
/// implemented; the route tests (WorkforceCeilingRouteTests) prove each one, and a wrong requirement set passes here.
/// </remarks>
[Collection(BootedBffCollection.Name)]
public sealed class WorkforceCeilingCoverageTests
{
    private const string Prefix = "/api/v1/external";

    private readonly BootedBff _booted;

    public WorkforceCeilingCoverageTests(BootedBff booted) => _booted = booted;

    [Fact(DisplayName = "Task 186 F1: every /api/v1/external endpoint declares its workforce ceiling (Development and Production boots)")]
    public void EveryExternalEndpointDeclaresItsWorkforceCeiling()
    {
        foreach (var app in new[] { _booted.Development, _booted.Production })
        {
            var external = app.Endpoints.Where(IsExternal).ToList();

            // Not vacuous: the external surface is ~60 routes; a boot that mapped none would pass every rule below.
            Assert.True(external.Count >= 50,
                $"{app.HostEnvironmentName}: only {external.Count} /api/v1/external endpoints were booted; a mapping gate is off.");

            var violations = Violations(app.Endpoints);
            Assert.True(violations.Count == 0,
                $"{app.HostEnvironmentName}: /api/v1/external endpoints without a sound workforce-ceiling declaration. Declare "
                + ".RequireWorkforceCeiling(<set>) (WorkforceRoleCeilingGate), .WorkforceCeilingInHandler(reason) with a route "
                + "test that proves it, or .ExemptFromWorkforceCeiling(reason) for a non-record operation:\n"
                + string.Join("\n", violations));
        }
    }

    [Fact(DisplayName = "Task 186 F1 must-fire: an undeclared, a doubly declared, and an unfiltered checked endpoint are each reported")]
    public void TheGuardFires_OnEachUnsoundDeclaration()
    {
        var reads = new[] { new WorkforceCeilingRequirement("sprk_project", AccessRights.Read) };
        var endpoints = new[]
        {
            Endpoint("/api/v1/external/undeclared"),
            Endpoint("/api/v1/external/twice", WorkforceCeilingMetadata.ForRoute(reads),
                WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.Exempt, "identity"), WorkforceCeilingFilterAttached.Instance),
            Endpoint("/api/v1/external/unfiltered", WorkforceCeilingMetadata.ForRoute(reads)),
            Endpoint("/api/v1/external/unfiltered-resolved", WorkforceCeilingMetadata.ForRequest(_ => reads)),
        };

        var violations = Violations(endpoints);

        Assert.Equal(4, violations.Count);
        Assert.Contains(violations, v => v.Contains("/undeclared", StringComparison.Ordinal) && v.Contains("no declaration", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("/twice", StringComparison.Ordinal) && v.Contains("2 declarations", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("/unfiltered ", StringComparison.Ordinal) && v.Contains("filter", StringComparison.Ordinal));
        Assert.Contains(violations, v => v.Contains("/unfiltered-resolved", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 186 F1 must-not-fire: sound declarations, and routes outside the prefix, are not reported")]
    public void TheGuardDoesNotFire_OnSoundDeclarations_OrOutsideThePrefix()
    {
        var reads = new[] { new WorkforceCeilingRequirement("sprk_project", AccessRights.Read) };
        var endpoints = new[]
        {
            Endpoint("/api/v1/external/checked", WorkforceCeilingMetadata.ForRoute(reads), WorkforceCeilingFilterAttached.Instance),
            Endpoint("/api/v1/external/resolved", WorkforceCeilingMetadata.ForRequest(_ => null), WorkforceCeilingFilterAttached.Instance),
            Endpoint("/api/v1/external/in-handler", WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.InHandler, "seam")),
            Endpoint("/api/v1/external/registration", WorkforceCeilingMetadata.Declared(WorkforceCeilingCoverage.Exempt, "bootstrap")),
            Endpoint("/api/v1/external-access/grant"),
            Endpoint("/api/v1/externalthing"),
        };

        Assert.Empty(Violations(endpoints));
    }

    private static bool IsExternal(RouteEndpoint endpoint)
    {
        var raw = endpoint.RoutePattern.RawText ?? string.Empty;
        return raw.Equals(Prefix, StringComparison.OrdinalIgnoreCase)
               || raw.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Violations(IEnumerable<RouteEndpoint> endpoints)
    {
        var violations = new List<string>();
        foreach (var endpoint in endpoints.Where(IsExternal))
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            var display = $"{(methods is { Count: > 0 } ? string.Join(",", methods) : "*")} {endpoint.RoutePattern.RawText} ";
            var declarations = endpoint.Metadata.GetOrderedMetadata<WorkforceCeilingMetadata>();

            if (declarations.Count == 0)
            {
                violations.Add(display + "has no declaration");
                continue;
            }

            if (declarations.Count > 1)
            {
                violations.Add(display + $"has {declarations.Count} declarations; exactly one decides");
                continue;
            }

            if (declarations[0].Coverage is WorkforceCeilingCoverage.Route or WorkforceCeilingCoverage.RouteResolved
                && endpoint.Metadata.GetMetadata<WorkforceCeilingFilterAttached>() is null)
            {
                violations.Add(display + "declares a set the filter checks, but its group does not run the WorkforceCeilingEndpointFilter");
            }
        }

        return violations;
    }

    private static RouteEndpoint Endpoint(string pattern, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0, new EndpointMetadataCollection(metadata), pattern);
}
