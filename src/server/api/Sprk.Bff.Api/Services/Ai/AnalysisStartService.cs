using System.Text.Json;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.RecordCreation;

namespace Sprk.Bff.Api.Services.Ai;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 037a (owner decisions D-037-results and D-037-engine). The analysis create core of
// POST /api/ai/analysis/create, MOVED unchanged out of Api/Ai/AnalysisEndpoints (task 031's CreateAnalysisRecordAsync and
// StartAnalysisAsCallerAsync) so the route handler, spaarke_start_analysis and the Copilot playbook start call ONE create.
// Placement and component justification: Services/Ai/PublicContracts/IAnalysisStartAi.cs (the facade this implements).
// The route's payload (AnalysisEndpoints.BuildCreatePayload) and its refusal rendering (AnalysisEndpoints.CreateRefusalResult, shared
// with the promote route) stay with the route and are read from here, so each rule still has one home.
// =====================================================================================================================

/// <summary>
/// THE analysis create (see <see cref="IAnalysisStartAi"/>). Stateless: every collaborator is the caller's own request services, as the
/// route's, so one singleton serves every host.
/// </summary>
internal sealed class AnalysisStartService : IAnalysisStartAi
{
    /// <inheritdoc />
    /// <remarks>
    /// The route's rule and the route's create, in the route's order: the shared create decision
    /// (<see cref="AnalysisAuthorizationFilter.AuthorizeAnalysisCreateAsync"/>), then <see cref="CreateAnalysisRecordAsync"/> with what it
    /// allowed. No scope rows are associated (the hosts name none) and nothing is executed: no orchestration or playbook service is resolved.
    /// </remarks>
    public async Task<AnalysisStartResult> StartAsCallerAsync(
        HttpContext? httpContext, string? name, Guid documentId, Guid? playbookId, CancellationToken cancellationToken)
    {
        var request = new CreateAnalysisRequest(name!, documentId, [], [], [], playbookId);
        var decision = await AnalysisAuthorizationFilter.AuthorizeAnalysisCreateAsync(httpContext, request, cancellationToken);
        switch (decision.Outcome)
        {
            case AnalysisCreateAuthorizationOutcome.UserContextRequired:
                return AnalysisStartResult.Failure(AnalysisStartOutcome.UserContextRequired, decision.ReasonCode, StatusCodes.Status401Unauthorized, null);
            case AnalysisCreateAuthorizationOutcome.Invalid:
                return AnalysisStartResult.Failure(AnalysisStartOutcome.Invalid, null, StatusCodes.Status400BadRequest, decision.Message);
            case AnalysisCreateAuthorizationOutcome.Allowed:
                break;
            default:
                return AnalysisStartResult.Failure(
                    AnalysisStartOutcome.Refused, decision.ReasonCode, StatusCodes.Status403Forbidden, null);
        }

        // The caller was identified by the decision, so the context and its services are there.
        var services = httpContext!.RequestServices;
        var logger = services.GetRequiredService<ILogger<AnalysisOrchestrationService>>();
        try
        {
            var outcome = await CreateAnalysisRecordAsync(
                request, decision.AuthorizedPlaybookId, decision.AuthorizedDocumentId,
                services.GetRequiredService<RecordCreationService>(), services.GetRequiredService<IDataverseUserClient>(),
                logger, httpContext, cancellationToken);

            if (outcome.Refusal is { } refusal)
            {
                logger.LogWarning(
                    "Refused analysis start: {Kind} {Code} (correlationId={CorrelationId})", refusal.Kind, refusal.Code, outcome.CorrelationId);
                var (status, reasonCode) = StatusAndReasonOf(AnalysisEndpoints.CreateRefusalResult(refusal));
                return reasonCode is null
                    ? AnalysisStartResult.Failure(AnalysisStartOutcome.Failed, AnalysisStartReasonCodes.CreateFailed, StatusCodes.Status500InternalServerError, null)
                    : AnalysisStartResult.Failure(AnalysisStartOutcome.Refused, reasonCode, status, null);
            }

            if (outcome.CreatedId is not { } analysisId)
            {
                logger.LogError("Analysis start ended without a decision (correlationId={CorrelationId})", outcome.CorrelationId);
                return AnalysisStartResult.Failure(AnalysisStartOutcome.Failed, AnalysisStartReasonCodes.CreateFailed, StatusCodes.Status500InternalServerError, null);
            }

            return AnalysisStartResult.Created(analysisId, outcome.Warnings.Count);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only.
            logger.LogError("Analysis start failed ({ExceptionType}) (correlationId={CorrelationId})", ex.GetType().Name, httpContext.TraceIdentifier);
            return AnalysisStartResult.Failure(AnalysisStartOutcome.Failed, AnalysisStartReasonCodes.CreateFailed, StatusCodes.Status500InternalServerError, null);
        }
    }

    /// <summary>
    /// spaarkeai-mcp-bff-api-r2 task 031 (spec FR-06): THE analysis create — the one <see cref="RecordCreationService.CreateAsync"/>
    /// call of <c>POST /api/ai/analysis/create</c>, shared by that route's handler and <see cref="StartAsCallerAsync"/>, so a
    /// tool-created analysis is the same row a REST-created one is for the same inputs. <paramref name="authorizedPlaybookId"/> and
    /// <paramref name="authorizedDocumentId"/> are what the create decision allowed for this caller (the route filter's record, or
    /// <see cref="AnalysisAuthorizationFilter.AuthorizeAnalysisCreateAsync"/>'s answer) and nothing else
    /// (<c>RouteAuthorizedLookupsCallSiteTests</c> pins the two callers).
    /// </summary>
    internal static Task<RecordCreationOutcome> CreateAnalysisRecordAsync(
        CreateAnalysisRequest request,
        Guid? authorizedPlaybookId,
        Guid? authorizedDocumentId,
        RecordCreationService recordCreation,
        IDataverseUserClient user,
        ILogger logger,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        recordCreation.CreateAsync(
            new RecordCreationRequest(
                AnalysisEndpoints.AnalysisTable,
                new RecordCreationPayload.WebApi(
                    JsonSerializer.SerializeToElement(AnalysisEndpoints.BuildCreatePayload(request))))
            {
                CorrelationId = httpContext.TraceIdentifier,
                // FR-19 / FR-03: the mapping profile of the document the analysis is filed under, if one exists; read as
                // the caller first (FR-04). No profile: a silent no-op. Fill-only: every value of the request wins.
                // Task 043 (owner 2026-11-04): a Document to Analysis profile maps when the caller can read the document. The
                // service asks the caller's Read on it; the external document narrowing is the create decision on the
                // body document, reused here (MappingDocumentDecisionFor), so it is not asked twice.
                Mapping = Sprk.Bff.Api.Api.Events.EventEndpoints.CallerMappingOf(
                    httpContext, logger, MappingDocumentDecisionFor(authorizedDocumentId)),
                // Task 031 fix round 1 (F1, owner 2026-11-04; UAC-r2 2026-11-04): the create decision's playbook-use answer
                // (public, or the caller's Read on the row) authorised ONE playbook id, so the service asks no AppendTo on
                // that record (only Spaarke AI Analysis Admin holds it). The id is the decision's, not the body's: the service
                // exempts the payload's playbook only when it is that record. Set only when the decision authorised one.
                RouteAuthorizedLookups =
                    authorizedPlaybookId is { } authorisedPlaybook
                        ? new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { [AnalysisEndpoints.PlaybookColumn] = authorisedPlaybook }
                        : null,
            },
            RecordCreationPrincipal.ForCaller(
                user,
                Guid.TryParse(CallerResolution.ResolveObjectId(httpContext.User), out var oid) && oid != Guid.Empty ? oid : null),
            cancellationToken);

    /// <summary>
    /// spaarkeai-mcp-bff-api-r2 task 031: the narrowing answer for one allowed document: it is shown, any other is not; <c>null</c>
    /// (a document source maps nothing) when no document was allowed.
    /// </summary>
    internal static Func<Guid, CancellationToken, Task<bool>>? MappingDocumentDecisionFor(Guid? allowedDocumentId) =>
        allowedDocumentId is { } allowed
            ? (documentId, _) => Task.FromResult(documentId == allowed)
            : null;

    /// <summary>The status and <c>reasonCode</c> of a route ProblemDetails result (<c>null</c> code when it carries none).</summary>
    private static (int Status, string? ReasonCode) StatusAndReasonOf(IResult result) =>
        result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem
            ? (problem.StatusCode,
               problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code) ? code as string ?? code?.ToString() : null)
            : (StatusCodes.Status500InternalServerError, null);
}
