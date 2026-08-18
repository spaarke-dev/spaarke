// -----------------------------------------------------------------------------
// HealthEndpoints.cs
//
// L2 CONTROL-PLANE HEALTH endpoint (originally Wave C3 scaffold, task 036).
//
// SCOPE:
//   - GET  /ping — anonymous, returns 200 "ok". Smoke-tests routing +
//                  hosting; not a full /healthz (a real Cosmos + Service Bus
//                  health probe surface can layer on in a future task).
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
/// L2 health-surface endpoint (currently just /ping — anonymous smoke test).
/// The real POST /api/runs handler moved to <see cref="Api.RunsEndpoints"/>
/// in Wave C5 (task 057).
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Maps the /ping smoke-test endpoint onto the application.</summary>
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

        return app;
    }
}
