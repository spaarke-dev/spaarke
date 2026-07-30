using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Delivery;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// One thin endpoint backing the Spaarke email composer's "insert template" feature:
/// <c>POST /api/communications/template/render</c> renders a Dataverse <c>template</c> record
/// (subject + body) with <c>{!entity.field}</c> field-code merge from an optional regarding record.
/// </summary>
/// <remarks>
/// <para>
/// Reuses the existing <see cref="IEmailTemplateService"/> (Services/Ai/Delivery) as the fetch + render
/// facade — this endpoint does NOT re-implement template retrieval or slug substitution. The endpoint's
/// only added responsibility is resolving the merge variables from a regarding record and the
/// Dataverse URL + access token the service requires.
/// </para>
/// <para>
/// Auth/data posture (app-only, ADR-028 canonical server-outbound): the regarding record is read via
/// the same <see cref="IGenericEntityService"/> used by the sibling
/// <c>CommunicationEndpoints.GetCommunicationStatusAsync</c>; the Dataverse token is acquired from the
/// DI-injected central <see cref="TokenCredential"/> for <c>{EnvironmentUrl}/.default</c> — the same
/// app-only Dataverse token mechanism as <c>DataverseAccessDataSource</c>. No new Dataverse client is
/// introduced.
/// </para>
/// </remarks>
public static class CommunicationTemplateEndpoints
{
    public static IEndpointRouteBuilder MapCommunicationTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/communications/template/render", RenderTemplateAsync)
            .RequireAuthorization()
            .WithName("RenderCommunicationTemplate")
            .WithTags("Communications")
            .WithDescription("Render a Dataverse email template with {!entity.field} field-code merge from an optional regarding record, for the email composer's insert-template feature.")
            .Produces<CommunicationTemplateRenderResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Renders the requested template. When a regarding record is supplied, its attributes become the
    /// merge variables; when absent, the template is rendered with an empty variable set (a template
    /// with no field codes still renders). Delegates the fetch + render to <see cref="IEmailTemplateService"/>.
    /// </summary>
    internal static async Task<IResult> RenderTemplateAsync(
        CommunicationTemplateRenderRequest request,
        IEmailTemplateService emailTemplateService,
        IGenericEntityService genericEntityService,
        TokenCredential credential,
        IOptions<DataverseOptions> dataverseOptions,
        ILogger<CommunicationTemplateRenderResponse> logger,
        CancellationToken ct)
    {
        if (request is null || request.TemplateId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "templateId is required.",
                statusCode: 400);
        }

        var dataverseUrl = dataverseOptions.Value.EnvironmentUrl;
        if (string.IsNullOrWhiteSpace(dataverseUrl))
        {
            throw new SdapProblemException(
                code: "DATAVERSE_NOT_CONFIGURED",
                title: "Dataverse not configured",
                detail: "Dataverse:EnvironmentUrl is not configured on this server.",
                statusCode: 500);
        }

        // 1. Resolve merge variables from the regarding record (optional). Same read mechanism as the
        //    sibling communication endpoints (IGenericEntityService); ColumnSet(true) so any field code
        //    the template references is available without the caller enumerating columns.
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hasRegarding = !string.IsNullOrWhiteSpace(request.RegardingEntityType)
            && request.RegardingRecordId is { } regardingId
            && regardingId != Guid.Empty;

        if (hasRegarding)
        {
            variables = await FetchRegardingVariablesAsync(
                genericEntityService,
                request.RegardingEntityType!,
                request.RegardingRecordId!.Value,
                logger,
                ct);
        }

        // 2. Resolve the Dataverse access token (app-only, central TokenCredential — ADR-028).
        var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
        var accessToken = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);

        // 3. Delegate fetch + {!entity.field} render to the existing service.
        var result = await emailTemplateService.FetchAndRenderAsync(
            request.TemplateId,
            variables,
            dataverseUrl,
            accessToken.Token,
            ct);

        if (!result.Success)
        {
            var isNotFound = result.Error is not null
                && result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase);

            return Results.Problem(
                detail: result.Error,
                statusCode: isNotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                title: isNotFound ? "Template Not Found" : "Template Render Failed");
        }

        return Results.Ok(new CommunicationTemplateRenderResponse
        {
            Subject = result.Subject,
            Body = result.Body,
            IsHtml = result.IsHtml,
        });
    }

    /// <summary>
    /// Retrieves the regarding record with all columns and projects its attributes into a merge-variable
    /// dictionary (Dataverse logical name → value). Dataverse typed values are unwrapped to their scalar
    /// so template slugs render a readable value. A missing/invisible record yields an empty dictionary
    /// (the template still renders — field codes resolve to empty), never a hard failure.
    /// </summary>
    private static async Task<Dictionary<string, object?>> FetchRegardingVariablesAsync(
        IGenericEntityService genericEntityService,
        string entityType,
        Guid recordId,
        ILogger logger,
        CancellationToken ct)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Query by primary id with ColumnSet(true). Dataverse primary-key attribute follows the
            // universal {logicalName}id convention (holds for every ADR-024 regarding family + standard
            // entities), which lets us fetch all columns by id without a separate metadata round-trip.
            var query = new QueryExpression(entityType)
            {
                ColumnSet = new ColumnSet(true),
                TopCount = 1,
                Criteria = new FilterExpression(),
            };
            query.Criteria.AddCondition($"{entityType}id", ConditionOperator.Equal, recordId);

            var results = await genericEntityService.RetrieveMultipleAsync(query, ct);
            var record = results.Entities.Count > 0 ? results.Entities[0] : null;
            if (record is null)
            {
                logger.LogInformation(
                    "Regarding record {EntityType}:{RecordId} not found or not visible; rendering template with empty variables",
                    entityType, recordId);
                return variables;
            }

            foreach (var attribute in record.Attributes)
            {
                variables[attribute.Key] = UnwrapDataverseValue(attribute.Value);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: the composer's insert-template action should still return the template shell
            // even if the regarding read fails. Log and continue with whatever variables we have.
            logger.LogWarning(ex,
                "Failed to read regarding record {EntityType}:{RecordId} for template merge; rendering with available variables",
                entityType, recordId);
        }

        return variables;
    }

    /// <summary>Unwraps Dataverse SDK attribute types to a scalar suitable for text merge.</summary>
    private static object? UnwrapDataverseValue(object? value) => value switch
    {
        OptionSetValue osv => osv.Value,
        EntityReference er => (object?)er.Name ?? er.Id.ToString(),
        Money money => money.Value,
        AliasedValue av => UnwrapDataverseValue(av.Value),
        _ => value,
    };
}

/// <summary>
/// Request body for <c>POST /api/communications/template/render</c>. Regarding is optional — omit it to
/// render a template that has no field codes.
/// </summary>
public sealed record CommunicationTemplateRenderRequest
{
    /// <summary>The Dataverse <c>template</c> record id to fetch and render.</summary>
    public Guid TemplateId { get; init; }

    /// <summary>Regarding record entity logical name (e.g. <c>sprk_matter</c>). Optional.</summary>
    public string? RegardingEntityType { get; init; }

    /// <summary>Regarding record id whose fields supply the merge variables. Optional.</summary>
    public Guid? RegardingRecordId { get; init; }
}

/// <summary>Rendered template result returned to the composer.</summary>
public sealed record CommunicationTemplateRenderResponse
{
    /// <summary>Rendered email subject.</summary>
    public string? Subject { get; init; }

    /// <summary>Rendered email body.</summary>
    public string? Body { get; init; }

    /// <summary>Whether the body is HTML.</summary>
    public bool IsHtml { get; init; }
}
