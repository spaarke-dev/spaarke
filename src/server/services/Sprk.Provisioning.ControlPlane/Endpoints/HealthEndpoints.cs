// -----------------------------------------------------------------------------
// HealthEndpoints.cs
//
// L2 CONTROL-PLANE PLACEHOLDER ENDPOINTS (Wave C3 scaffold).
//
// SCOPE (task 036 ONLY):
//   - GET  /ping         — anonymous, returns 200 "ok". Smoke-tests routing
//                          + hosting; not a full /healthz (task 037+ adds a
//                          real Cosmos + Service Bus health-check surface).
//   - POST /api/runs     — Operator policy required (401 without bearer;
//                          403 with a non-Operator token; 501 Not Implemented
//                          with a valid Operator token). Placeholder to prove
//                          the auth pipeline wiring end-to-end. The real
//                          handler that enqueues Service Bus messages + writes
//                          Cosmos state lands in Wave C5 (spec FR-21/FR-22).
//
// FR-20 acceptance mapping (verified by these endpoints):
//   - "L3 skill can auth via az account get-access-token"          → POST /api/runs 501 with valid Operator token.
//   - "Operator-role required for mutating endpoints returns 403"  → POST /api/runs 403 with valid Reader-only token.
//   - Standard bearer flow                                          → POST /api/runs 401 without any bearer.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Modules;

namespace Sprk.Provisioning.ControlPlane.Endpoints;

/// <summary>
/// Wave C3 placeholder endpoints. Task 037 replaces <c>/ping</c> with a real
/// <c>/healthz</c> surface; Wave C5 replaces <c>POST /api/runs</c> with the
/// initial-provisioning enqueue handler.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>Maps the two placeholder endpoints onto the application.</summary>
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

        // Placeholder mutating endpoint. Auth pipeline runs BEFORE the handler
        // body:
        //   - No bearer                    → 401 (JwtBearer authn middleware)
        //   - Bearer without Operator role → 403 (Operator policy)
        //   - Bearer with Operator role    → handler runs → 501 Not Implemented
        // Wave C5 (task 041+) replaces the body with the real enqueue path.
        app.MapPost("/api/runs", () => Results.StatusCode(StatusCodes.Status501NotImplemented))
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("CreateRunPlaceholder")
            .WithTags("Runs");

        return app;
    }
}
