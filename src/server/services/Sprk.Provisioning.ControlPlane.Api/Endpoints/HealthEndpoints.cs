// -----------------------------------------------------------------------------
// HealthEndpoints.cs
//
// L2 CONTROL-PLANE HEALTH endpoints (originally Wave C3 scaffold, task 036).
//
// SCOPE:
//   - GET  /ping    — anonymous, returns 200 "ok". Smoke-tests routing +
//                     hosting.
//   - GET  /healthz — anonymous, returns 200 "ok" (added by task 113,
//                     2026-08-19). Not a real Cosmos + Service Bus health
//                     probe surface (a future task can layer that in) — this
//                     is deliberately as trivial as /ping. It exists because
//                     TWO independent consumers already assumed it existed
//                     and were silently broken without it: (1)
//                     modules/controlplane-app-service.bicep's
//                     `healthCheckPath: '/healthz'` (task 033) — the Azure
//                     App Service PLATFORM-level instance health probe, which
//                     was 404ing against this host since the App Service was
//                     first deployed; (2) scripts/Deploy-ControlPlane.ps1
//                     (task 113) needs a uniform /healthz probe across BOTH
//                     the .Api and .Worker hosts (the Worker already ships
//                     /healthz — task 100 Program.cs) to avoid a per-target
//                     special case in its post-deploy verification. Fixed at
//                     discovery per the project's "fix drift at time of
//                     discovery" operating principle rather than deferred or
//                     special-cased around.
//
// HISTORY (task 057 — Wave C5, 2026-08-17):
//   The original scaffold also mapped a POST /api/runs PLACEHOLDER that
//   returned 501 Not Implemented under the Operator policy. Task 057 replaces
//   that with the real L2 REST surface (RunsEndpoints.MapRunsEndpoints +
//   RunLogsEndpoints.MapRunLogsEndpoints under /api/runs). The placeholder is
//   REMOVED here to avoid a duplicate-route registration collision with the
//   real POST /api/runs — ASP.NET Core throws AmbiguousMatchException at
//   request-dispatch time when two endpoints share the same method + template.
//
// FR-20 acceptance mapping is now proven by the REAL endpoints in
// RunsEndpoints.cs (401 no-bearer / 403 wrong-role / 202 valid-Operator).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Endpoints;

/// <summary>
/// L2 health-surface endpoints (/ping + /healthz — both anonymous smoke
/// tests). The real POST /api/runs handler moved to
/// <see cref="Api.RunsEndpoints"/> in Wave C5 (task 057).
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Maps the /ping and /healthz smoke-test endpoints onto the application.</summary>
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Smoke test — anonymous ping. Kept intentionally trivial: no config
        // reads, no service resolution, no downstream I/O. Successful GET
        // confirms only that hosting, routing, and the middleware pipeline
        // are wired.
        app.MapGet("/ping", () => Results.Text("ok", contentType: "text/plain"))
            .AllowAnonymous()
            .WithName("Ping")
            .WithTags("Health");

        // Platform + deploy-tooling health probe (task 113). Same
        // intentionally-trivial shape as /ping — see file header for why
        // this exists as its own route rather than an alias.
        app.MapGet("/healthz", () => Results.Text("ok", contentType: "text/plain"))
            .AllowAnonymous()
            .WithName("Healthz")
            .WithTags("Health");

        return app;
    }
}
