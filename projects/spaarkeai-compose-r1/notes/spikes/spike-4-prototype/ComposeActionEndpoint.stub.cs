// SPIKE-LOCAL STUB — DO NOT COMPILE INTO Sprk.Bff.Api.
// This file is the design-locked sketch of the BFF dispatch endpoint for Compose
// AI actions. Phase 2 tasks 021 (ComposeService.cs) + 024 (ComposeEndpoints.cs)
// implement this in production.
//
// Status: design-locked by spaarkeai-compose-r1 spike-4 (2026-06-29).
// ADR-013 facade boundary: injects ONLY IConsumerRoutingService + IInvokePlaybookAi.
// No AI internals (IOpenAiClient, IPlaybookService, IPlaybookOrchestrationService,
// IPlaybookExecutionEngine) are injected. Verified via grep evidence (see
// spike-4-consumer-routing-jps.md § 6).

using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Spikes.Compose;

// ----------------------------------------------------------------------------
// Endpoint shape (minimal API per ADR-001; auth via filter per ADR-008):
// ----------------------------------------------------------------------------
//
// POST /api/compose/action/{consumerType}
//
//   - {consumerType} path segment: e.g., "compose-summarize"
//   - Body: ComposeActionRequest
//   - Auth: .RequireAuthorization() per ADR-008/028
//   - Rate limiting: .RequireRateLimiting("standard") (default policy)
//   - Returns: 200 ComposeActionResponse | 400 invalid input | 401 unauth |
//              404 unknown consumer | 503 dispatch unconfigured | 500 internal
//
// Notes:
//   - Route /api/compose/* groups all R1 Compose endpoints per ADR-019.
//   - {consumerType} is a discriminator, NOT a free-text passthrough — the handler
//     validates against ConsumerTypes.All before routing.
//   - 503 is returned when IConsumerRoutingService.ResolveAsync returns null
//     (no sprk_playbookconsumer row matched). The HTTP semantic matches the
//     existing chat-summarize pattern (see ConsumerRoutingService.cs precedent).
//   - The endpoint streams nothing — InvokePlaybookAsync is the non-streaming
//     facade per IInvokePlaybookAi contract (single aggregated response).
//   - R2+ may add /api/compose/action/{consumerType}/stream for SSE variants —
//     OUT OF SCOPE for R1.

public sealed record ComposeActionRequest
{
    // Per compose-document scope schema (compose-document.scope.json):
    public required string DocumentSpeId { get; init; }
    public string? DocumentVersionEtag { get; init; }
    public Guid? DocumentRecordId { get; init; }
    public Guid? MatterId { get; init; }
    public string? DocumentName { get; init; }
    public string? DocumentMimeType { get; init; }
    public required string SessionId { get; init; }

    // Per compose-selection scope schema (compose-selection.scope.json) — only
    // when consumerType implies selection-scoped action. compose-summarize is
    // a whole-document action; Selection is null for it. R2 actions like
    // compose-explain-clause populate Selection.
    public ComposeSelection? Selection { get; init; }
}

public sealed record ComposeSelection
{
    public required string SelectionText { get; init; }
    public required int SelectionAnchorStart { get; init; }
    public required int SelectionAnchorEnd { get; init; }
}

public sealed record ComposeActionResponse
{
    public required Guid RunId { get; init; }
    public required bool Success { get; init; }
    public string? TextContent { get; init; }
    public System.Text.Json.JsonElement? StructuredData { get; init; }
    public IReadOnlyList<object> Citations { get; init; } = Array.Empty<object>();
    public double? Confidence { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorCode { get; init; }
}

// ----------------------------------------------------------------------------
// Service shape (Phase 2 task 021 implements ComposeService.cs in production):
// ----------------------------------------------------------------------------
//
// public sealed class ComposeService
// {
//     private readonly IConsumerRoutingService _routing;       // PublicContracts
//     private readonly IInvokePlaybookAi _invokePlaybook;       // PublicContracts
//     private readonly ILogger<ComposeService> _logger;
//
//     public ComposeService(
//         IConsumerRoutingService routing,
//         IInvokePlaybookAi invokePlaybook,
//         ILogger<ComposeService> logger)
//     {
//         _routing = routing ?? throw new ArgumentNullException(nameof(routing));
//         _invokePlaybook = invokePlaybook ?? throw new ArgumentNullException(nameof(invokePlaybook));
//         _logger = logger ?? throw new ArgumentNullException(nameof(logger));
//     }
//
//     public async Task<ComposeActionResponse> InvokeAsync(
//         string consumerType,
//         ComposeActionRequest request,
//         HttpContext httpContext,
//         CancellationToken ct)
//     {
//         // 1. Validate consumer type against the known catalog (defense-in-depth
//         //    above the routing layer; gives 404 fast for typos).
//         if (!ConsumerTypes.All.Contains(consumerType))
//         {
//             throw new InvalidOperationException($"Unknown consumer type '{consumerType}'.");
//         }
//
//         // 2. Resolve playbook via consumer routing (PublicContracts facade).
//         var playbookId = await _routing.ResolveAsync(
//             consumerType,
//             consumerCode: "default",
//             context: BuildRoutingContext(request),
//             environment: null,
//             ct);
//
//         if (playbookId is null)
//         {
//             return ComposeActionResponse.Unconfigured(consumerType);
//         }
//
//         // 3. Build parameters from the scope payload (compose-document /
//         //    compose-selection) for template substitution in playbook nodes.
//         var parameters = BuildParameters(consumerType, request);
//
//         // 4. Build invocation context.
//         var invocationContext = new PlaybookInvocationContext
//         {
//             TenantId = ResolveTenantId(httpContext),
//             HttpContext = httpContext,
//             CorrelationId = httpContext.TraceIdentifier,
//         };
//
//         // 5. Invoke via PublicContracts facade (NOT IPlaybookOrchestrationService).
//         var result = await _invokePlaybook.InvokePlaybookAsync(
//             playbookId.Value, parameters, invocationContext, ct);
//
//         return ProjectToResponse(result);
//     }
//
//     private static IRoutingContext? BuildRoutingContext(ComposeActionRequest request) =>
//         string.IsNullOrEmpty(request.DocumentMimeType)
//             ? null
//             : new RoutingContext { MimeType = request.DocumentMimeType };
//
//     private static IReadOnlyDictionary<string, string> BuildParameters(
//         string consumerType,
//         ComposeActionRequest request)
//     {
//         var p = new Dictionary<string, string>(StringComparer.Ordinal)
//         {
//             ["documentSpeId"] = request.DocumentSpeId,
//             ["sessionId"] = request.SessionId,
//         };
//         if (request.DocumentRecordId is { } rec) p["documentRecordId"] = rec.ToString();
//         if (request.MatterId is { } mat) p["matterId"] = mat.ToString();
//         if (request.DocumentName is { } dn) p["documentName"] = dn;
//         if (request.Selection is { } s)
//         {
//             p["selectionText"] = s.SelectionText;
//             p["selectionAnchorStart"] = s.SelectionAnchorStart.ToString();
//             p["selectionAnchorEnd"] = s.SelectionAnchorEnd.ToString();
//         }
//         return p;
//     }
//
//     // ResolveTenantId / ProjectToResponse / Unconfigured factory are routine
//     // glue — omitted from the spike sketch.
// }
//
// ----------------------------------------------------------------------------
// DI registration (Phase 2 task 025 — Program.cs):
//
//   builder.Services.AddScoped<ComposeService>();
//
// IConsumerRoutingService + IInvokePlaybookAi are already registered in
// AnalysisServicesModule (per HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md §2 step 3).
// No additional facade registration needed.
//
// Endpoint mapping (Phase 2 task 024 — Api/ComposeEndpoints.cs):
//
//   public static IEndpointRouteBuilder MapComposeEndpoints(this IEndpointRouteBuilder app)
//   {
//       var group = app.MapGroup("/api/compose")
//                      .RequireAuthorization()
//                      .RequireRateLimiting("standard");
//
//       group.MapPost("/action/{consumerType}", async (
//           string consumerType,
//           ComposeActionRequest request,
//           ComposeService composeService,
//           HttpContext httpContext,
//           CancellationToken ct) =>
//       {
//           try
//           {
//               var response = await composeService.InvokeAsync(consumerType, request, httpContext, ct);
//               return Results.Ok(response);
//           }
//           catch (InvalidOperationException ex)
//           {
//               return Results.Problem(detail: ex.Message, statusCode: 404, title: "Unknown consumer type");
//           }
//       });
//
//       // ... 6 other Compose endpoints per design.md §12 ...
//
//       return app;
//   }
