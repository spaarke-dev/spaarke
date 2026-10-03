using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Delivery;
using Sprk.Bff.Api.Services.Communication;

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
/// <b>Authorization (unified-access-control-r2 task 161, route-sweep finding S-08).</b> The route used to check only
/// sign-in, then read ANY record of ANY table the caller named — every column, app-only — into the merge variables
/// it returned. Now <see cref="CommunicationRecordAuthorizationFilter"/> requires, as the caller, Read on the regarding
/// record (whose type must be in the live-verified <see cref="RegardingNameFields"/> catalogue) and a readable
/// template; and the merge read itself runs AS THE CALLER through <see cref="IImpersonatedCommunicationQuery"/>, so
/// Dataverse applies row AND field-level security to what can be rendered. The template body is still fetched
/// app-only inside <see cref="IEmailTemplateService"/> (owner G5: checked as the user, then read as the app), with the
/// Dataverse token from the central <see cref="TokenCredential"/> for <c>{EnvironmentUrl}/.default</c>.
/// </para>
/// </remarks>
public static class CommunicationTemplateEndpoints
{
    private const string FormattedValueSuffix = "@OData.Community.Display.V1.FormattedValue";

    /// <summary>An ISO-8601 date or date-time as Dataverse's Web API writes one (Edm.Date / Edm.DateTimeOffset).</summary>
    private static readonly Regex IsoDateValue = new(
        @"^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEndpointRouteBuilder MapCommunicationTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/communications/template/render", RenderTemplateAsync)
            .RequireAuthorization()
            .AddCommunicationRecordAuthorizationFilter(CommunicationRecordRoute.TemplateRender)
            .WithName("RenderCommunicationTemplate")
            .WithTags("Communications")
            .WithDescription("Render a Dataverse email template with {!entity.field} field-code merge from an optional regarding record, for the email composer's insert-template feature. The caller must hold Read on the regarding record and be able to read the template; the merge read runs as the caller (row and field-level security apply).")
            .Produces<CommunicationTemplateRenderResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// The response a template that does not exist gets — and, since task 161, one the caller cannot read. BOTH the
    /// record filter's deny and <see cref="RenderTemplateAsync"/>'s not-found branch return this, so the two bodies
    /// cannot drift apart even if <see cref="EmailTemplateService"/> rewords its error (the handler still recognises
    /// not-found by that error's "not found" text, as before). The detail is the text the service uses for a missing
    /// template.
    /// </summary>
    internal static IResult TemplateNotFound(Guid templateId) =>
        Results.Problem(
            detail: $"Email template not found: {templateId}",
            statusCode: StatusCodes.Status404NotFound,
            title: "Template Not Found");

    /// <summary>
    /// Renders the requested template. When a regarding record is supplied, its attributes — as the CALLER can read
    /// them — become the merge variables; when absent, the template is rendered with an empty variable set (a template
    /// with no field codes still renders). Delegates the fetch + render to <see cref="IEmailTemplateService"/>.
    /// </summary>
    internal static async Task<IResult> RenderTemplateAsync(
        CommunicationTemplateRenderRequest request,
        IEmailTemplateService emailTemplateService,
        IImpersonatedCommunicationQuery impersonatedQuery,
        ICallerSystemUserResolver callerResolver,
        TokenCredential credential,
        IOptions<DataverseOptions> dataverseOptions,
        ILogger<CommunicationTemplateRenderResponse> logger,
        HttpContext context,
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

        // 1. Resolve merge variables from the regarding record (optional), READ AS THE CALLER.
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hasRegarding = !string.IsNullOrWhiteSpace(request.RegardingEntityType)
            && request.RegardingRecordId is { } regardingId
            && regardingId != Guid.Empty;

        if (hasRegarding)
        {
            variables = await FetchRegardingVariablesAsync(
                impersonatedQuery,
                callerResolver,
                context,
                request.RegardingEntityType!,
                request.RegardingRecordId!.Value,
                logger,
                ct);
        }

        // 2. Resolve the Dataverse access token (app-only, central TokenCredential — ADR-028) for the template fetch.
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

            // A missing template answers EXACTLY what the record filter answers for an unreadable one (unknown equals
            // denied, owner round 9): one body, built in one place.
            if (isNotFound)
            {
                return TemplateNotFound(request.TemplateId);
            }

            return Results.Problem(
                detail: result.Error,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Template Render Failed");
        }

        return Results.Ok(new CommunicationTemplateRenderResponse
        {
            Subject = result.Subject,
            Body = result.Body,
            IsHtml = result.IsHtml,
        });
    }

    /// <summary>
    /// Reads the regarding record AS THE CALLER (one impersonated top-1 query, every column the caller may read) and
    /// projects its attributes into a merge-variable dictionary (Dataverse logical name → value) via
    /// <see cref="ToMergeVariables"/>. A row the caller cannot see yields an empty dictionary — the filter has already
    /// required Read, so that is a record that changed between the check and the read. A read FAULT is a 502, never an
    /// empty render: rendering with blank variables would hide the failure behind a plausible result.
    /// </summary>
    private static async Task<Dictionary<string, object?>> FetchRegardingVariablesAsync(
        IImpersonatedCommunicationQuery impersonatedQuery,
        ICallerSystemUserResolver callerResolver,
        HttpContext context,
        string entityType,
        Guid recordId,
        ILogger logger,
        CancellationToken ct)
    {
        var logicalName = entityType.Trim().ToLowerInvariant();
        var entitySet = RegardingNameFields.EntitySetName(logicalName);
        if (entitySet is null)
        {
            // Unreachable behind CommunicationRecordAuthorizationFilter (it 400s first); kept so the handler never
            // guesses a set name if it is ever mapped without the filter.
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: $"'{entityType}' is not a supported regarding record type.",
                statusCode: 400);
        }

        var resolution = await callerResolver.ResolveAsync(context.User, ct);
        if (!resolution.IsResolved
            || !Guid.TryParse(resolution.SystemUserId, out var callerSystemUserId)
            || callerSystemUserId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "TEMPLATE_MERGE_FORBIDDEN",
                title: "Forbidden",
                detail: "The caller could not be resolved to a Dataverse user, so the regarding record cannot be read for the merge.",
                statusCode: 403);
        }

        IReadOnlyList<Dictionary<string, JsonElement>> rows;
        try
        {
            // Primary key = "{logicalName}id" for every type in RegardingNameFields (live-verified, task-161 note §2).
            rows = await impersonatedQuery.QueryAsync(
                entitySet, $"$filter={logicalName}id eq {recordId}&$top=1", callerSystemUserId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Regarding record {EntityType}:{RecordId} could not be read as the caller for template merge; refusing the render",
                logicalName, recordId);
            throw new SdapProblemException(
                code: "TEMPLATE_MERGE_READ_FAILED",
                title: "Template merge read failed",
                detail: "The regarding record could not be read for the template merge.",
                statusCode: 502);
        }

        if (rows.Count == 0)
        {
            logger.LogInformation(
                "Regarding record {EntityType}:{RecordId} not visible to the caller; rendering template with empty variables",
                logicalName, recordId);
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        }

        return ToMergeVariables(rows[0]);
    }

    /// <summary>
    /// Maps one impersonated OData row to the SAME merge variables the pre-161 SDK read produced, so every field code
    /// renders the text it rendered before (template merge parity, pinned by the characterization test):
    /// <list type="bullet">
    ///   <item>a lookup <c>_x_value</c> becomes key <c>x</c> with the related record's NAME (its FormattedValue
    ///   annotation), falling back to the id string — as <c>EntityReference.Name ?? Id</c> did;</item>
    ///   <item>a date or date-time (a string carrying a FormattedValue annotation, in ISO form) becomes a
    ///   <see cref="DateTime"/> of the same kind the SDK returned (UTC for a <c>Z</c> value);</item>
    ///   <item>a non-integral number becomes a <see cref="decimal"/> parsed from the JSON text, so a money or decimal
    ///   column keeps its scale ("25000.0000000000"); an integral one an <see cref="int"/> or <see cref="long"/>, as
    ///   an option set, whole number or big integer was;</item>
    ///   <item>a boolean stays a <see cref="bool"/>; a string stays a string; annotations are not variables.</item>
    /// </list>
    /// </summary>
    internal static Dictionary<string, object?> ToMergeVariables(IReadOnlyDictionary<string, JsonElement> row)
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in row)
        {
            if (key.Contains('@', StringComparison.Ordinal))
            {
                continue; // an annotation (FormattedValue, lookuplogicalname, @odata.etag) — not an attribute
            }

            row.TryGetValue(key + FormattedValueSuffix, out var formatted);
            var hasFormatted = formatted.ValueKind == JsonValueKind.String;

            if (key.StartsWith('_') && key.EndsWith("_value", StringComparison.Ordinal) && key.Length > "__value".Length)
            {
                var logicalName = key[1..^"_value".Length];
                variables[logicalName] = hasFormatted
                    ? formatted.GetString()
                    : value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                continue;
            }

            variables[key] = value.ValueKind switch
            {
                JsonValueKind.String when hasFormatted && TryParseIsoDate(value.GetString(), out var dateTime) => dateTime,
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => ToNumber(value),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }

        return variables;
    }

    private static bool TryParseIsoDate(string? text, out DateTime value)
    {
        value = default;
        return text is not null
               && IsoDateValue.IsMatch(text)
               && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    private static object ToNumber(JsonElement value)
    {
        var raw = value.GetRawText();
        if (raw.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0)
        {
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? d
                : value.GetDouble();
        }

        if (value.TryGetInt32(out var i))
            return i;
        if (value.TryGetInt64(out var l))
            return l;
        return value.GetDecimal();
    }
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
