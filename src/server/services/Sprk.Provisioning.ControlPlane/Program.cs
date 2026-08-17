// -----------------------------------------------------------------------------
// Program.cs
//
// L2 CONTROL-PLANE ENTRY POINT (Sprk.Provisioning.ControlPlane).
//
// Wave C3 scaffold ONLY (customer-provisioning-orchestration-r1 task 036):
//   - Minimal API WebApplication builder.
//   - Module composition (AddAuthModule + AddSwaggerModule) — extension
//     methods per feature area to avoid god-class DI (parity with the BFF
//     *Module.cs pattern; respects NFR-07 god-class ratchet).
//   - Auth pipeline: UseAuthentication() → UseAuthorization().
//   - Swagger UI at /swagger.
//   - Health endpoints: GET /ping (anon, 200 "ok") + POST /api/runs
//     placeholder (Operator policy required; returns 501 Not Implemented).
//
// EXPLICITLY DEFERRED (do NOT wire here):
//   - Cosmos client + spaarke-provisioning DB / runs container (task 037).
//   - Service Bus client + IJobHandler enqueue path (task 038).
//   - App Insights exporter wiring (UseAzureMonitor + AzureMonitorGuard) —
//     task 039.
//
// PLACEMENT: L2 is a PEER service to Sprk.Bff.Api, not a BFF extension
// (ADR-010 DI minimalism; project MUST NOT rule — no reference to
// Sprk.Bff.Api assemblies from here).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Endpoints;
using Sprk.Provisioning.ControlPlane.Modules;

var builder = WebApplication.CreateBuilder(args);

// ---- Services registration (module composition per ADR-010) ----
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddSwaggerModule();

var app = builder.Build();

// ---- Middleware pipeline ----
// Swagger UI is enabled in ALL environments for the scaffold — the L2
// surface is Spaarke-internal and the OpenAPI schema is a required output
// (FR-21). Task 039 may gate this behind IsDevelopment() if operator policy
// changes; for now discoverability trumps hiding.
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Sprk.Provisioning.ControlPlane v1");
    o.RoutePrefix = "swagger";
});

app.UseAuthentication();
app.UseAuthorization();

// ---- Endpoints ----
app.MapHealthEndpoints();

app.Run();

// Expose Program for future WebApplicationFactory-based integration tests
// (parity with BFF Program.cs; test project is not yet created).
public partial class Program
{
}
