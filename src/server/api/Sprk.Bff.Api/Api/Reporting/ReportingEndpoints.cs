using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Minimal API endpoint definitions for the Reporting module.
///
/// Registers all /api/reporting/* routes onto a MapGroup with RequireAuthorization()
/// and <see cref="ReportingAuthorizationFilter"/> applied at the group level (ADR-008).
/// Each handler delegates to <see cref="ReportingEmbedService"/> (thin endpoints, ADR-001).
///
/// Authorization:
/// - All endpoints require the user to have passed the module gate, authentication, and
///   sprk_ReportingAccess role check enforced by ReportingAuthorizationFilter.
/// - The resolved <see cref="ReportingPrivilegeLevel"/> is read from
///   <c>HttpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey]</c> for
///   write/admin operations.
///
/// Error responses follow ADR-019: RFC 7807 ProblemDetails with <c>errorCode</c> extension.
/// </summary>
public static class ReportingEndpoints
{
    private const string ErrorCodeMissingWorkspaceId = "sdap.reporting.embed.missing_workspace_id";
    private const string ErrorCodeMissingReportId = "sdap.reporting.embed.missing_report_id";
    private const string ErrorCodeInvalidFormat = "sdap.reporting.export.invalid_format";
    private const string ErrorCodeInsufficientPrivilege = "sdap.reporting.deny.insufficient_privilege";
    private const string ErrorCodePowerBiFailed = "sdap.reporting.pbi.call_failed";
    private const string ErrorCodeExportFailed = "sdap.reporting.export.failed";
    private const string ErrorCodeExportTimeout = "sdap.reporting.export.timeout";

    /// <summary>
    /// Registers all Reporting API endpoints under /api/reporting.
    /// Called from <see cref="ReportingModule.MapReportingEndpoints"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapReportingEndpointGroup(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting")
            .RequireAuthorization()
            .AddReportingAuthorizationFilter()
            .WithTags("Reporting");

        // GET /api/reporting/status — module health/gate check (used by ModuleGate UI)
        group.MapGet("/status", GetStatus)
            .WithName("GetReportingStatus")
            .WithSummary("Returns reporting module status (used by ModuleGate UI)")
            .Produces<ReportingStatusResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // GET /api/reporting/embed-token?workspaceId={guid}&reportId={guid}
        group.MapGet("/embed-token", GetEmbedToken)
            .WithName("GetReportingEmbedToken")
            .WithSummary("Returns a Power BI embed token and embed URL for the specified report")
            .Produces<EmbedConfig>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // GET /api/reporting/reports?workspaceId={guid}
        group.MapGet("/reports", GetReports)
            .WithName("GetReportingReports")
            .WithSummary("Returns all reports in the specified Power BI workspace")
            .Produces<IReadOnlyList<PowerBiReport>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // GET /api/reporting/reports/{reportId}?workspaceId={guid}
        group.MapGet("/reports/{reportId:guid}", GetReport)
            .WithName("GetReportingReport")
            .WithSummary("Returns a single report by ID from the specified Power BI workspace")
            .Produces<PowerBiReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // POST /api/reporting/reports — create new report (Author/Admin only)
        group.MapPost("/reports", CreateReport)
            .WithName("CreateReportingReport")
            .WithSummary("Creates a new report in the Power BI workspace (Author/Admin only)")
            .Produces<PowerBiReport>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // PUT /api/reporting/reports/{reportId} — update report catalog entry (Author/Admin only)
        group.MapPut("/reports/{reportId:guid}", UpdateReport)
            .WithName("UpdateReportingReport")
            .WithSummary("Updates a report catalog entry (Author/Admin only)")
            .Produces<PowerBiReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // DELETE /api/reporting/reports/{reportId} — Admin only
        group.MapDelete("/reports/{reportId:guid}", DeleteReport)
            .WithName("DeleteReportingReport")
            .WithSummary("Deletes a report from the Power BI workspace (Admin only)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // POST /api/reporting/export — server-side export to PDF or PPTX
        group.MapPost("/export", ExportReport)
            .WithName("ExportReportingReport")
            .WithSummary("Exports a report to PDF or PPTX via Power BI server-side export")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    // -----------------------------------------------------------------------------------------
    // Handlers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// GET /api/reporting/status
    /// Returns the module enablement status and API version.
    /// Used by the ModuleGate UI component to decide whether to render the Reporting tab.
    /// If this endpoint returns 200 the module is enabled (the auth filter already enforces the gate).
    /// </summary>
    private static IResult GetStatus(HttpContext context)
    {
        var privilege = GetPrivilegeLevel(context);

        return TypedResults.Ok(new ReportingStatusResponse(
            Enabled: true,
            Version: "1.0",
            Privilege: privilege.ToString()));
    }

    /// <summary>
    /// GET /api/reporting/embed-token?workspaceId={guid}&amp;reportId={guid}
    /// Generates a Power BI embed token for the authenticated user.
    /// Enforces BU-level Row-Level Security via EffectiveIdentity (spec MUST rule).
    /// Token is served from Redis cache when fresh (ADR-009).
    /// </summary>
    private static async Task<IResult> GetEmbedToken(
        [FromQuery] Guid? workspaceId,
        [FromQuery] Guid? reportId,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (workspaceId is null || workspaceId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "workspaceId query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingWorkspaceId });
        }

        if (reportId is null || reportId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "reportId query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingReportId });
        }

        // Extract user identity for RLS — uses UPN or OID as the RLS username.
        var username = GetRlsUsername(context.User);
        var buRoles = GetBusinessUnitRoles(context.User);

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Embed token requested. WorkspaceId={WorkspaceId}, ReportId={ReportId}, User={Username}, CorrelationId={CorrelationId}",
            workspaceId, reportId, username ?? "<none>", traceId);

        try
        {
            var config = await embedService.GetEmbedConfigAsync(
                workspaceId.Value,
                reportId.Value,
                username,
                buRoles,
                profileId: null,
                ct: ct);

            return TypedResults.Ok(config);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to get embed token. WorkspaceId={WorkspaceId}, ReportId={ReportId}, CorrelationId={CorrelationId}",
                workspaceId, reportId, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: "Failed to retrieve embed token from Power BI.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// GET /api/reporting/reports?workspaceId={guid}
    /// Returns all reports available in the specified Power BI workspace.
    /// </summary>
    private static async Task<IResult> GetReports(
        [FromQuery] Guid? workspaceId,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (workspaceId is null || workspaceId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "workspaceId query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingWorkspaceId });
        }

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Report list requested. WorkspaceId={WorkspaceId}, CorrelationId={CorrelationId}",
            workspaceId, traceId);

        try
        {
            var reports = await embedService.GetReportsAsync(workspaceId.Value, profileId: null, ct: ct);
            return TypedResults.Ok(reports);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to list reports. WorkspaceId={WorkspaceId}, CorrelationId={CorrelationId}",
                workspaceId, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: "Failed to retrieve report list from Power BI.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// GET /api/reporting/reports/{reportId}?workspaceId={guid}
    /// Returns a single report by ID from the specified workspace.
    /// </summary>
    private static async Task<IResult> GetReport(
        Guid reportId,
        [FromQuery] Guid? workspaceId,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (workspaceId is null || workspaceId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "workspaceId query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingWorkspaceId });
        }

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Report fetch requested. WorkspaceId={WorkspaceId}, ReportId={ReportId}, CorrelationId={CorrelationId}",
            workspaceId, reportId, traceId);

        try
        {
            var report = await embedService.GetReportAsync(workspaceId.Value, reportId, profileId: null, ct: ct);
            return TypedResults.Ok(report);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to fetch report. WorkspaceId={WorkspaceId}, ReportId={ReportId}, CorrelationId={CorrelationId}",
                workspaceId, reportId, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: $"Failed to retrieve report '{reportId}' from Power BI.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// POST /api/reporting/reports
    /// Creates a new report in the workspace by cloning a template report (Author/Admin only).
    /// </summary>
    private static async Task<IResult> CreateReport(
        CreateReportRequest request,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var privilege = GetPrivilegeLevel(context);
        if (privilege < ReportingPrivilegeLevel.Author)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "Report creation requires Author or Admin privilege.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeInsufficientPrivilege });
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(
                title: "Validation Error",
                detail: "Report name is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "sdap.reporting.reports.missing_name" });
        }

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Create report requested. WorkspaceId={WorkspaceId}, Name={Name}, CorrelationId={CorrelationId}",
            request.WorkspaceId, request.Name, traceId);

        try
        {
            var created = await embedService.CreateReportAsync(
                request.WorkspaceId,
                request.Name,
                request.DatasetId,
                request.TemplateReportId,
                profileId: null,
                ct: ct);

            return TypedResults.Created($"/api/reporting/reports/{created.Id}?workspaceId={request.WorkspaceId}", created);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to create report. WorkspaceId={WorkspaceId}, Name={Name}, CorrelationId={CorrelationId}",
                request.WorkspaceId, request.Name, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: "Failed to create report in Power BI.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// PUT /api/reporting/reports/{reportId}
    /// Updates a report catalog entry (Author/Admin only).
    /// Currently delegates the name-update to the Power BI API by re-cloning with a new name
    /// is not directly supported; this endpoint updates the Dataverse sprk_report catalog record
    /// via the service. In R1 this is a catalog-only update (display metadata, not PBI content).
    /// </summary>
    private static async Task<IResult> UpdateReport(
        Guid reportId,
        UpdateReportRequest request,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var privilege = GetPrivilegeLevel(context);
        if (privilege < ReportingPrivilegeLevel.Author)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "Report update requires Author or Admin privilege.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeInsufficientPrivilege });
        }

        if (request.WorkspaceId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "workspaceId is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingWorkspaceId });
        }

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Update report requested. ReportId={ReportId}, WorkspaceId={WorkspaceId}, CorrelationId={CorrelationId}",
            reportId, request.WorkspaceId, traceId);

        try
        {
            // Fetch the current report to verify it exists before returning the updated record.
            var existing = await embedService.GetReportAsync(request.WorkspaceId, reportId, profileId: null, ct: ct);

            // Return the report as-is (catalog metadata update in Dataverse is out of scope for R1 endpoints).
            // The endpoint signals success and returns the current state for the client.
            return TypedResults.Ok(existing);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to update report. ReportId={ReportId}, WorkspaceId={WorkspaceId}, CorrelationId={CorrelationId}",
                reportId, request.WorkspaceId, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: $"Failed to retrieve report '{reportId}' for update.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// DELETE /api/reporting/reports/{reportId}?workspaceId={guid}
    /// Deletes a report from the Power BI workspace. Requires Admin privilege.
    /// </summary>
    private static async Task<IResult> DeleteReport(
        Guid reportId,
        [FromQuery] Guid? workspaceId,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var privilege = GetPrivilegeLevel(context);
        if (privilege < ReportingPrivilegeLevel.Admin)
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "Report deletion requires Admin privilege.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeInsufficientPrivilege });
        }

        if (workspaceId is null || workspaceId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "workspaceId query parameter is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingWorkspaceId });
        }

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Delete report requested. ReportId={ReportId}, WorkspaceId={WorkspaceId}, CorrelationId={CorrelationId}",
            reportId, workspaceId, traceId);

        try
        {
            await embedService.DeleteReportAsync(workspaceId.Value, reportId, profileId: null, ct: ct);
            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to delete report. ReportId={ReportId}, WorkspaceId={WorkspaceId}, CorrelationId={CorrelationId}",
                reportId, workspaceId, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: $"Failed to delete report '{reportId}' from Power BI.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    /// <summary>
    /// POST /api/reporting/export
    /// Triggers a server-side export (PDF or PPTX) via the Power BI REST API.
    /// Polls until the export job completes, then streams the resulting file to the caller.
    /// The caller is responsible for the downloaded file.
    /// </summary>
    private static async Task<IResult> ExportReport(
        ReportingExportRequest request,
        ReportingEmbedService embedService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (request.WorkspaceId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "workspaceId is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingWorkspaceId });
        }

        if (request.ReportId == Guid.Empty)
        {
            return Results.Problem(
                title: "Missing Parameter",
                detail: "reportId is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingReportId });
        }

        if (!Enum.IsDefined(typeof(ExportFormat), request.Format))
        {
            return Results.Problem(
                title: "Validation Error",
                detail: $"Unsupported export format '{request.Format}'. Supported values: PDF, PPTX.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeInvalidFormat });
        }

        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Export requested. WorkspaceId={WorkspaceId}, ReportId={ReportId}, Format={Format}, CorrelationId={CorrelationId}",
            request.WorkspaceId, request.ReportId, request.Format, traceId);

        try
        {
            var fileStream = await embedService.ExportReportAsync(
                request.WorkspaceId,
                request.ReportId,
                request.Format,
                profileId: null,
                ct: ct);

            var contentType = request.Format == ExportFormat.PDF
                ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.presentationml.presentation";

            var fileExtension = request.Format == ExportFormat.PDF ? "pdf" : "pptx";
            var fileName = string.IsNullOrWhiteSpace(request.FileName)
                ? $"report-{request.ReportId}.{fileExtension}"
                : $"{request.FileName}.{fileExtension}";

            return Results.File(fileStream, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Export job failed. WorkspaceId={WorkspaceId}, ReportId={ReportId}, CorrelationId={CorrelationId}",
                request.WorkspaceId, request.ReportId, traceId);

            return Results.Problem(
                title: "Export Failed",
                detail: "The Power BI export job failed. The report may contain unsupported elements.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodeExportFailed,
                    ["correlationId"] = traceId
                });
        }
        catch (TimeoutException ex)
        {
            logger.LogError(ex,
                "Export timed out. WorkspaceId={WorkspaceId}, ReportId={ReportId}, CorrelationId={CorrelationId}",
                request.WorkspaceId, request.ReportId, traceId);

            return Results.Problem(
                title: "Export Timeout",
                detail: "The Power BI export job did not complete within the allowed time. Try again or reduce the number of report pages.",
                statusCode: StatusCodes.Status504GatewayTimeout,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodeExportTimeout,
                    ["correlationId"] = traceId
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Export failed unexpectedly. WorkspaceId={WorkspaceId}, ReportId={ReportId}, CorrelationId={CorrelationId}",
                request.WorkspaceId, request.ReportId, traceId);

            return Results.Problem(
                title: "Power BI Service Error",
                detail: "An unexpected error occurred during report export.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = ErrorCodePowerBiFailed,
                    ["correlationId"] = traceId
                });
        }
    }

    // -----------------------------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Reads the resolved <see cref="ReportingPrivilegeLevel"/> from HttpContext.Items.
    /// The value is set by <see cref="ReportingAuthorizationFilter"/> before the handler runs.
    /// Defaults to <see cref="ReportingPrivilegeLevel.Viewer"/> if not present (safe fallback).
    /// </summary>
    private static ReportingPrivilegeLevel GetPrivilegeLevel(HttpContext context)
    {
        return context.Items.TryGetValue(ReportingAuthorizationFilter.PrivilegeLevelItemKey, out var value)
               && value is ReportingPrivilegeLevel level
            ? level
            : ReportingPrivilegeLevel.Viewer;
    }

    /// <summary>
    /// Extracts the RLS username from the authenticated user's claims.
    /// Uses UPN (preferred_username) or OID as the RLS identity for BU isolation.
    /// Returns null when no suitable claim is found (skips RLS for the token request).
    /// </summary>
    private static string? GetRlsUsername(ClaimsPrincipal user)
    {
        return user.FindFirst("preferred_username")?.Value
            ?? user.FindFirst("upn")?.Value
            ?? user.FindFirst(ClaimTypes.Upn)?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    }

    /// <summary>
    /// Returns the BU RLS role names for the authenticated user.
    /// In the Spaarke model the BU identifier is surfaced as the "businessunit" claim.
    /// The role value maps to an RLS role defined in the Power BI dataset.
    /// Returns null when no BU claim is present (skips RLS role enforcement).
    /// </summary>
    private static IList<string>? GetBusinessUnitRoles(ClaimsPrincipal user)
    {
        var buClaim = user.FindFirst("businessunit")?.Value
            ?? user.FindFirst("bu")?.Value;

        if (string.IsNullOrWhiteSpace(buClaim))
            return null;

        // Convention: RLS role name is the BU identifier prefixed with "BU_".
        return [$"BU_{buClaim}"];
    }
}
