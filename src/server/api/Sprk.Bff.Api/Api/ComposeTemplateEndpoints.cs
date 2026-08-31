using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using static Sprk.Bff.Api.Api.ComposeEndpoints;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Compose <b>template</b> route: <c>POST /documents/{documentSpeId}/apply-template</c>.
///
/// <para><b>Reason to change</b>: how a firm/matter template is RESOLVED (an org-shared Dataverse
/// asset read app-only through the ADR-013 <c>IComposeTemplateSource</c> facade, with
/// <c>{{variable}}</c> normalization) and merged as document chrome. It is the only Compose route
/// that mints a Dataverse token, and the only one whose failure surface includes template
/// resolution.</para>
/// </summary>
internal static class ComposeTemplateEndpoints
{
    /// <summary>Maps this cluster's routes onto the shared <c>/api/compose</c> group.</summary>
    internal static RouteGroupBuilder MapComposeTemplateEndpoints(this RouteGroupBuilder group)
    {
        // (3a) POST /api/compose/documents/{documentSpeId}/apply-template — FR-05 (task 032,
        //      spaarkeai-compose-r6): apply a firm/matter template's chrome to a PERSISTED Compose
        //      document. The ENDPOINT resolves the template via IComposeTemplateSource (task 031's
        //      ADR-013 PublicContracts facade — templates are org-shared assets read app-only, the
        //      SAME auth class as the email-template render at CommunicationTemplateEndpoints; the
        //      DOCUMENT bytes stay user-OBO), then IComposeService.ApplyTemplateAsync runs the ONE
        //      030 part-merge engine and persists a new SPE version through the existing replace
        //      path. Deterministic OOXML packaging — NOT an AI dispatch (ADR-039).
        group.MapPost("/documents/{documentSpeId}/apply-template", ApplyTemplate)
            .WithName("ComposeApplyTemplate")
            .WithSummary("Apply a firm/matter template's chrome to a persisted Compose document via the OOXML part-merge engine (FR-05)")
            // SPE persistence (writes a new version) → ai-persist, same bucket as sibling Save (3).
            .RequireRateLimiting("ai-persist")
            .Produces<ApplyComposeTemplateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        return group;
    }

    // FR-05 (task 032, spaarkeai-compose-r6): apply-template. The endpoint's own responsibilities
    // mirror CommunicationTemplateEndpoints.RenderTemplateAsync exactly for the TEMPLATE leg:
    // resolve dataverseUrl from IOptions<DataverseOptions>.EnvironmentUrl and mint the app-only
    // Dataverse token from the DI-injected central TokenCredential ({url}/.default — ADR-028
    // canonical server-outbound; templates are org-shared assets, the same class as email
    // templates). The DOCUMENT leg (download/replace) stays user-OBO inside
    // IComposeService.ApplyTemplateAsync. IComposeTemplateSource is the ADR-013-sanctioned
    // PublicContracts facade — no AI internals are injected here.
    private static async Task<IResult> ApplyTemplate(
        string documentSpeId,
        [FromBody] ApplyComposeTemplateBody? body,
        IComposeService composeService,
        IComposeTemplateSource templateSource,
        TokenCredential credential,
        IOptions<DataverseOptions> dataverseOptions,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ComposeEndpoints");

        if (string.IsNullOrWhiteSpace(documentSpeId)) return BadRequest("documentSpeId is required.");
        if (body is null) return BadRequest("Request body is required.");
        if (string.IsNullOrWhiteSpace(body.DriveId)) return BadRequest("driveId is required in the request body.");
        if (string.IsNullOrWhiteSpace(body.TemplateIdOrName)) return BadRequest("templateIdOrName is required in the request body.");

        var dataverseUrl = dataverseOptions.Value.EnvironmentUrl;
        if (string.IsNullOrWhiteSpace(dataverseUrl))
        {
            logger.LogError("Compose apply-template: Dataverse:EnvironmentUrl is not configured. TraceId={TraceId}",
                httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Dataverse Not Configured",
                detail: "Dataverse:EnvironmentUrl is not configured on this server.");
        }

        logger.LogInformation(
            "Compose apply-template: drive={DriveId} item={DocumentSpeId} template={TemplateIdOrName} variables={VariableCount} TraceId={TraceId}",
            body.DriveId, documentSpeId, body.TemplateIdOrName, body.Variables?.Count ?? 0, httpContext.TraceIdentifier);

        try
        {
            // 1) App-only Dataverse token for the org-shared template read (ADR-028; mirrors
            //    CommunicationTemplateEndpoints step 2).
            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            var accessToken = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct)
                .ConfigureAwait(false);

            // 2) Resolve the firm/matter template (fetch + optional {{variable}} render — task 031).
            var resolved = await templateSource.ResolveAsync(
                    body.TemplateIdOrName,
                    NormalizeTemplateVariables(body.Variables),
                    dataverseUrl,
                    accessToken.Token,
                    ct)
                .ConfigureAwait(false);

            if (resolved is null)
            {
                logger.LogWarning(
                    "Compose apply-template: template '{TemplateIdOrName}' not found or has no stored attachment. TraceId={TraceId}",
                    body.TemplateIdOrName, httpContext.TraceIdentifier);
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Template Not Found",
                    detail: $"Template '{body.TemplateIdOrName}' was not found or has no stored .dotx/.docx attachment.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
            }

            // 3) Merge + persist + re-project (user-OBO document leg — the 030 engine inside).
            var result = await composeService.ApplyTemplateAsync(
                    httpContext, body.DriveId, documentSpeId, resolved.TemplateBytes, resolved.TemplateName, ct)
                .ConfigureAwait(false);

            return Results.Ok(new ApplyComposeTemplateResponse(
                DocumentSpeId: result.DocumentSpeId,
                DriveId: result.DriveId,
                TemplateName: result.TemplateName,
                VersionId: result.VersionId,
                ETag: result.ETag,
                Size: result.Size,
                CorrelationId: httpContext.TraceIdentifier,
                MergeWarnings: MapWarningResponses(result.MergeWarnings),
                ContentModel: result.ContentModel,
                ContentModelWarnings: MapWarningResponses(result.ContentModelWarnings)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ComposePdfIntakeException ex)
        {
            // Step-9.5 A-MEDIUM-1 (task 041 review): a PDF item cannot take a template merge — honest
            // typed ProblemDetails instead of a deep OOXML failure as a generic 500. MUST precede the
            // InvalidOperationException catch below (this type derives from it).
            logger.LogWarning(ex, "Compose apply-template: PDF target refused. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: ex.Unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status422UnprocessableEntity,
                title: ex.Unavailable ? "PDF Intake Unavailable" : "Template Cannot Apply To A PDF",
                detail: ex.Message);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Compose apply-template: SPE drive-item not found. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Document Not Found",
                detail: $"SPE drive-item '{documentSpeId}' was not found or is unreadable.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Compose apply-template: OBO denied. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller lacks SPE ACL permission for this drive-item.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.3");
        }
        catch (Sprk.Bff.Api.Infrastructure.Graph.DocumentLockedByWordException ex)
        {
            // Same honest 423 copy as the Save path — the replace leg hits the identical co-authoring lock.
            logger.LogWarning(ex, "Compose apply-template: drive-item locked by Word co-authoring (423). TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status423Locked,
                title: "Open in Word",
                detail: "This document is open in Word — close it there, then try again. It also releases " +
                        "automatically within a few minutes.",
                type: "https://tools.ietf.org/html/rfc4918#section-11.3");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Compose apply-template: unexpected failure. TraceId={TraceId}", httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while applying the template.");
        }
    }

    /// <summary>FR-05 (task 032): normalizes the request's JSON variable values (deserialized as
    /// <see cref="JsonElement"/>) to the scalar shapes the template engine's <c>{{variable}}</c>
    /// render expects — strings/numbers/bools pass through as scalars; anything else degrades to its
    /// raw JSON text (never a <c>JsonElement.ToString()</c> surprise).</summary>
    private static Dictionary<string, object?>? NormalizeTemplateVariables(Dictionary<string, JsonElement>? variables)
    {
        if (variables is null || variables.Count == 0) return null;

        var normalized = new Dictionary<string, object?>(variables.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in variables)
        {
            normalized[key] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => value.GetRawText(),
            };
        }
        return normalized;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Request / response DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Request body for <c>POST /api/compose/documents/{id}/apply-template</c> (FR-05, task 032).
/// <c>templateIdOrName</c> is a template record id (GUID) or the exact template <c>title</c>;
/// <c>variables</c> optionally feeds the <c>{{placeholder}}</c> render (raw stored bytes when
/// absent — byte-faithful for variable-free house templates).</summary>
public sealed record ApplyComposeTemplateBody(
    [property: JsonPropertyName("driveId")] string DriveId,
    [property: JsonPropertyName("templateIdOrName")] string TemplateIdOrName,
    [property: JsonPropertyName("variables")] Dictionary<string, JsonElement>? Variables = null);

/// <summary>Response shape for <c>POST /api/compose/documents/{id}/apply-template</c> (FR-05,
/// task 032) — the new SPE version the merged document was persisted as, the 030 engine's
/// <c>template-merge-*</c> degradation warnings (codes + counts only — the Detail never crosses
/// the wire), and the post-merge canonical content model the client re-mounts on. Additive JSON,
/// optional trailing fields (ADR-040).</summary>
public sealed record ApplyComposeTemplateResponse(
    [property: JsonPropertyName("documentSpeId")] string DocumentSpeId,
    [property: JsonPropertyName("driveId")] string? DriveId,
    [property: JsonPropertyName("templateName")] string TemplateName,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("eTag")] string? ETag,
    [property: JsonPropertyName("size")] long? Size,
    [property: JsonPropertyName("correlationId")] string CorrelationId,
    [property: JsonPropertyName("mergeWarnings")] IReadOnlyList<ComposeProjectionWarningResponse>? MergeWarnings = null,
    [property: JsonPropertyName("contentModel")] ComposeContentModel? ContentModel = null,
    [property: JsonPropertyName("contentModelWarnings")] IReadOnlyList<ComposeProjectionWarningResponse>? ContentModelWarnings = null);
