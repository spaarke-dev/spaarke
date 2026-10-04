using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.Reporting;

/// <summary>
/// Minimal API endpoint definitions for the Reporting module.
///
/// Registers all /api/reporting/* routes onto a MapGroup with RequireAuthorization()
/// and <see cref="ReportingAuthorizationFilter"/> applied at the group level (ADR-008).
///
/// <para><b>The catalog-row contract — unified-access-control-r2 task 166 r1 (owner round 21 item 2, option A).</b>
/// Every report id a client sends is the <c>sprk_report</c> CATALOG ROW id. Before any Power BI call the row is read AS
/// THE CALLER (<see cref="IDataverseUserClient"/>, OBO — Dataverse applies the caller's own security), and the Power BI
/// report id, workspace id and dataset id are DERIVED from that row. An absent row and a row the caller cannot read are
/// the same answer (404 <c>sdap.reporting.deny.report_not_in_catalog</c>), and the Power BI service is never asked.
/// Before this, the routes took a Power BI workspace id and report id from the request and acted on them as the
/// service principal, so any Reporting user could mint an embed token or run an export for any report the service
/// principal could reach — while the shipped client sent the catalog row id and no workspace, so every call was a
/// 400.</para>
///
/// <para><b>Row-level security identity.</b> The embed token's <c>EffectiveIdentity</c> — and, since task 166 r2, the
/// export job's — is the caller's BUSINESS UNIT, computed server-side from the caller's own Dataverse <c>systemuser</c>
/// (WhoAmI as the caller), with the dataset role <see cref="RlsRoleName"/> — the identity the report models' DAX
/// (<c>USERNAME()</c> = business unit id) filters on. It used to come from a <c>businessunit</c>/<c>bu</c> token claim
/// that nothing produces, so no token carried an RLS identity at all. No identity, no token and no export: a caller
/// whose business unit cannot be read is refused.</para>
///
/// <para><b>Catalog writes run as the caller.</b> Create, update and delete of <c>sprk_report</c> rows go through the
/// same OBO client, so Dataverse enforces the caller's own Create / Write / Delete; the Author / Admin module roles
/// remain an additional gate. The update verb is PATCH — the verb the client sends. Create checks the caller's own
/// Create privilege BEFORE the app-only Power BI clone (owner round 9 write pattern), and only ever clones the SOURCE
/// row's report — no client-named Power BI report is registered. Delete removes the Power BI report only for a custom
/// row no other catalog row references (owner round 23 item 2).</para>
///
/// <para><b>The catalog's second door is closed too</b> (task 166 f1, owner round 25 item 6). The row is the authority, so
/// its four Power BI pointer columns (<see cref="CatalogPointerColumns"/>) are field-secured and writable by the BFF
/// identity only (<c>scripts/Set-ReportCatalogFieldSecurity.ps1</c>): Create registers the row as the caller WITHOUT them
/// and stamps them app-only. And before ANY action the row's workspace must be one of
/// <see cref="PowerBiOptions.AllowedWorkspaces"/> (for a customer-bound workspace, the caller's customer's) — a row forged
/// before the lock, or seeded at an arbitrary workspace, is "not in your catalog" and Power BI is never asked.</para>
///
/// Error responses follow ADR-019: RFC 7807 ProblemDetails with <c>errorCode</c> extension.
/// </summary>
public static class ReportingEndpoints
{
    private const string ErrorCodeMissingReportId = "sdap.reporting.embed.missing_report_id";
    private const string ErrorCodeInvalidFormat = "sdap.reporting.export.invalid_format";
    private const string ErrorCodeInsufficientPrivilege = "sdap.reporting.deny.insufficient_privilege";
    private const string ErrorCodePowerBiFailed = "sdap.reporting.pbi.call_failed";
    private const string ErrorCodeExportFailed = "sdap.reporting.export.failed";
    private const string ErrorCodeExportTimeout = "sdap.reporting.export.timeout";
    private const string ErrorCodeCatalogUnavailable = "sdap.reporting.catalog.unavailable";
    private const string ErrorCodeCatalogWriteFailed = "sdap.reporting.catalog.write_failed";
    private const string ErrorCodeRlsIdentityUnavailable = "sdap.reporting.rls.identity_unavailable";

    /// <summary>The ONE "no such report for you" reason code — absent and unreadable alike (task 166 r1).</summary>
    internal const string ReportNotInCatalogReasonCode = "sdap.reporting.deny.report_not_in_catalog";

    /// <summary>
    /// The dataset RLS role every embed token names (the report models' <c>BusinessUnitFilter</c>, whose DAX resolves
    /// <c>USERNAME()</c> as a business unit id — <c>projects/spaarke-powerbi-embedded-r1/design.md</c>,
    /// <c>reports/v1.0.0/*.pbix.md</c>).
    /// </summary>
    internal const string RlsRoleName = "BusinessUnitFilter";

    /// <summary>The <c>sprk_report</c> columns a catalog read selects.</summary>
    internal const string CatalogSelect =
        "sprk_reportid,sprk_name,sprk_pbi_reportid,sprk_workspaceid,sprk_datasetid,sprk_embedurl,sprk_category,sprk_iscustom";

    /// <summary>The OBO path that reads ONE catalog row as the caller.</summary>
    internal static string CatalogRowPath(Guid rowId) => $"sprk_reports({rowId:D})?$select={CatalogSelect}";

    /// <summary>The OBO path that lists the active catalog rows the caller can read.</summary>
    internal const string CatalogListPath =
        "sprk_reports?$select=" + CatalogSelect + "&$filter=statecode eq 0&$orderby=sprk_name asc";

    /// <summary>The OBO collection path catalog rows are created in.</summary>
    internal const string CatalogCollectionApiPath = "/api/data/v9.2/sprk_reports";

    /// <summary><c>sprk_category</c> = Custom.</summary>
    private const int CustomCategoryValue = 100000004;

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

        // GET /api/reporting/embed-token?reportId={catalog row id}
        group.MapGet("/embed-token", GetEmbedToken)
            .WithName("GetReportingEmbedToken")
            .WithSummary("Returns a Power BI embed token (business-unit RLS) for a catalog report the caller can read")
            .Produces<EmbedConfig>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/reporting/reports — the catalog rows the caller can read
        group.MapGet("/reports", GetReports)
            .WithName("GetReportingReports")
            .WithSummary("Returns the sprk_report catalog entries the caller can read")
            .Produces<IReadOnlyList<ReportCatalogItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // GET /api/reporting/reports/{reportId} — one catalog row the caller can read
        group.MapGet("/reports/{reportId:guid}", GetReport)
            .WithName("GetReportingReport")
            .WithSummary("Returns one sprk_report catalog entry the caller can read")
            .Produces<ReportCatalogItem>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/reporting/reports — new catalog entry derived from a readable one (Author/Admin)
        group.MapPost("/reports", CreateReport)
            .WithName("CreateReportingReport")
            .WithSummary("Creates a report from a catalog entry the caller can read and registers it (Author/Admin)")
            .Produces<CreateReportResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // PATCH /api/reporting/reports/{reportId} — update the catalog row as the caller (Author/Admin).
        // Task 166 r1: PATCH, the verb the client sends (the route used to be PUT, so every client update 405ed).
        group.MapPatch("/reports/{reportId:guid}", UpdateReport)
            .WithName("UpdateReportingReport")
            .WithSummary("Updates a report catalog entry as the caller (Author/Admin)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // DELETE /api/reporting/reports/{reportId} — Admin only
        group.MapDelete("/reports/{reportId:guid}", DeleteReport)
            .WithName("DeleteReportingReport")
            .WithSummary("Deletes a catalog entry (as the caller) and its Power BI report (Admin only)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        // POST /api/reporting/export — server-side export to PDF or PPTX (business-unit RLS identity, task 166 r2)
        group.MapPost("/export", ExportReport)
            .WithName("ExportReportingReport")
            .WithSummary("Exports a catalog report the caller can read to PDF or PPTX (business-unit RLS) via Power BI server-side export")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
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
    /// GET /api/reporting/embed-token?reportId={catalog row id}
    /// Generates a Power BI embed token for a catalog report the caller can read, with the caller's business unit as
    /// the row-level-security identity. Token is served from Redis cache when fresh (ADR-009).
    /// </summary>
    private static async Task<IResult> GetEmbedToken(
        [FromQuery] Guid? reportId,
        [FromServices] ReportingEmbedService embedService,
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (reportId is null || reportId == Guid.Empty)
        {
            return MissingReportId("reportId query parameter is required.");
        }

        var traceId = context.TraceIdentifier;
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        var row = await ReadCatalogRowAsync(dataverseUser, reportId.Value, logger, ct);
        if (row is null)
        {
            return ReportNotInCatalog(traceId);
        }

        var businessUnitId = await ReadCallerBusinessUnitAsync(dataverseUser, logger, ct);
        if (businessUnitId is null)
        {
            // No identity, no token: a token without the business-unit RLS identity would show the whole dataset.
            return RlsIdentityUnavailable(
                "Your business unit could not be determined, so no report session could be issued. Try again.", traceId);
        }

        if (!await IsWorkspaceAllowedAsync(powerBiOptions.Value, row, businessUnitId, context, logger, ct))
        {
            return ReportNotInCatalog(traceId);
        }

        logger.LogInformation(
            "Embed token requested. CatalogRow={CatalogRowId}, PbiReport={ReportId}, Workspace={WorkspaceId}, CorrelationId={CorrelationId}",
            row.RowId, row.PbiReportId, row.WorkspaceId, traceId);

        try
        {
            var config = await embedService.GetEmbedConfigAsync(
                row.WorkspaceId,
                row.PbiReportId,
                RlsUsername(businessUnitId.Value),
                [RlsRoleName],
                profileId: null,
                ct: ct);

            return TypedResults.Ok(config with { WorkspaceId = row.WorkspaceId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to get embed token. CatalogRow={CatalogRowId}, CorrelationId={CorrelationId}",
                row.RowId, traceId);

            return PowerBiFailed("Failed to retrieve embed token from Power BI.", traceId);
        }
    }

    /// <summary>GET /api/reporting/reports — the active catalog entries the CALLER can read (Dataverse trims the list).</summary>
    private static async Task<IResult> GetReports(
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        DataverseUserResponse response;
        try
        {
            response = await dataverseUser.GetAsync(CatalogListPath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Report catalog read faulted. CorrelationId={CorrelationId}", traceId);
            return CatalogUnavailable(traceId);
        }

        if (!response.IsSuccess || response.Body is not { } body
            || !body.TryGetProperty("value", out var rows) || rows.ValueKind != JsonValueKind.Array)
        {
            // An unreadable catalog is an error, never an empty list — "no reports" would hide the fault.
            logger.LogError(
                "Report catalog read failed ({Status} {ErrorCode}). CorrelationId={CorrelationId}",
                response.StatusCode, response.ErrorCode, traceId);
            return CatalogUnavailable(traceId);
        }

        var parsed = rows.EnumerateArray()
            .Select(ParseCatalogRow)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        // A row whose workspace this deployment may not act on is not offered (task 166 f1): the server would refuse
        // every action on it.
        var businessUnitId = parsed.Count > 0 ? await ReadCallerBusinessUnitAsync(dataverseUser, logger, ct) : null;
        var items = new List<ReportCatalogItem>(parsed.Count);
        foreach (var row in parsed)
        {
            if (await IsWorkspaceAllowedAsync(powerBiOptions.Value, row, businessUnitId, context, logger, ct))
            {
                items.Add(row.ToItem());
            }
        }

        return TypedResults.Ok<IReadOnlyList<ReportCatalogItem>>(items);
    }

    /// <summary>GET /api/reporting/reports/{reportId} — one catalog entry the caller can read.</summary>
    private static async Task<IResult> GetReport(
        Guid reportId,
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        var row = await ReadActionableRowAsync(dataverseUser, powerBiOptions.Value, reportId, context, logger, ct);
        return row is null
            ? ReportNotInCatalog(traceId)
            : TypedResults.Ok(row.ToItem());
    }

    /// <summary>
    /// POST /api/reporting/reports — a new catalog entry derived from a source entry the caller can read
    /// (Author/Admin). The workspace and dataset come from the SOURCE row; the client names neither. The new Power BI
    /// report is always a server-side CLONE of the source row's report: a client-named Power BI report id ("Save As"
    /// registration) is no longer accepted (task 166 r2, owner round 23 item 2 — view-only embed tokens cannot create a
    /// report, so that path could only alias an existing report into a catalog row the caller owned).
    /// </summary>
    private static async Task<IResult> CreateReport(
        CreateReportRequest request,
        [FromServices] ReportingEmbedService embedService,
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] CallerRecordAccessProbe accessProbe,
        [FromServices] IGenericEntityService entityService,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var privilege = GetPrivilegeLevel(context);
        if (privilege < ReportingPrivilegeLevel.Author)
        {
            return InsufficientPrivilege("Report creation requires Author or Admin privilege.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem(
                title: "Validation Error",
                detail: "Report name is required.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "sdap.reporting.reports.missing_name" });
        }

        if (request.SourceReportId == Guid.Empty)
        {
            return MissingReportId("sourceReportId (the catalog report the new one is based on) is required.");
        }

        var traceId = context.TraceIdentifier;
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        // Owner round 9 write pattern (task 166 r2): the caller's OWN right to create the catalog row is checked AS THE
        // CALLER before the app-only Power BI clone — a caller who may not create the row never causes a clone. The row
        // create below still runs as the caller (Dataverse decides again); the compensating delete stays for a create
        // that fails for any other reason.
        if (!await CallerMayCreateCatalogRowAsync(accessProbe, context, logger, ct))
        {
            return InsufficientPrivilege("You do not have permission to create report catalog entries.");
        }

        // The SOURCE must be a row this deployment may act on (task 166 f1, allowed-workspace check): the clone runs in
        // the source's workspace as the service principal.
        var source = await ReadActionableRowAsync(dataverseUser, powerBiOptions.Value, request.SourceReportId, context, logger, ct);
        if (source is null || source.DatasetId is not { } datasetId)
        {
            return ReportNotInCatalog(traceId);
        }

        // Clone the SOURCE row's Power BI report into the SOURCE's workspace, always.
        PowerBiReport created;
        try
        {
            created = await embedService.CreateReportAsync(
                source.WorkspaceId, request.Name.Trim(), datasetId, source.PbiReportId, profileId: null, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Create report failed in Power BI. SourceRow={SourceRowId}, CorrelationId={CorrelationId}",
                source.RowId, traceId);
            return PowerBiFailed("Failed to create the report in Power BI.", traceId);
        }

        // Register it AS THE CALLER — Dataverse enforces their Create on sprk_report — WITHOUT the four Power BI pointer
        // columns: they are field-secured, writable by the BFF identity only (owner round 25 item 6,
        // scripts/Set-ReportCatalogFieldSecurity.ps1), so the BFF stamps them app-only once the row exists. A row with no
        // pointer columns is unusable (ReadCatalogRowAsync answers "not in your catalog") until then.
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["sprk_name"] = request.Name.Trim(),
            ["sprk_embedurl"] = created.EmbedUrl,
            ["sprk_category"] = CustomCategoryValue,
        });

        DataverseUserResponse write;
        try
        {
            write = await dataverseUser.PostAsync(CatalogCollectionApiPath, payload, preferRepresentation: true, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog row create faulted. CorrelationId={CorrelationId}", traceId);
            write = DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError, ex.Message);
        }

        var newRowId = write.IsSuccess && write.Body is { } createdBody
            && createdBody.TryGetProperty("sprk_reportid", out var idProperty)
            && Guid.TryParse(idProperty.GetString(), out var parsedId)
                ? parsedId
                : Guid.Empty;

        if (newRowId == Guid.Empty)
        {
            logger.LogError(
                "Catalog row create failed ({Status} {ErrorCode}). CorrelationId={CorrelationId}",
                write.StatusCode, write.ErrorCode, traceId);

            // The clone was OURS — do not leave an uncatalogued report behind.
            try
            {
                await embedService.DeleteReportAsync(source.WorkspaceId, created.Id, profileId: null, ct: ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Compensating delete of cloned report {ReportId} failed. CorrelationId={CorrelationId}",
                    created.Id, traceId);
            }

            return CatalogWriteFailed(write, "The report could not be registered in the catalog.", traceId);
        }

        // Stamp the Power BI pointer columns APP-ONLY (the BFF identity is their only writer under field-level security).
        // The values are server-derived: the clone the BFF just made, in the SOURCE row's (allowed) workspace.
        try
        {
            await entityService.UpdateAsync("sprk_report", newRowId, CatalogPointerFields(
                created.Id, source.WorkspaceId, created.DatasetId != Guid.Empty ? created.DatasetId : datasetId, isCustom: true), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Catalog row {CatalogRowId} was created but its Power BI pointer could not be stamped; removing the row and "
                + "the clone {ReportId}. CorrelationId={CorrelationId}", newRowId, created.Id, traceId);

            // The half-made row is the BFF's own (it never carried a pointer); remove it app-only, then the clone.
            try
            {
                await entityService.DeleteAsync("sprk_report", newRowId, ct);
            }
            catch (Exception deleteEx)
            {
                logger.LogError(deleteEx, "Compensating delete of catalog row {CatalogRowId} failed. CorrelationId={CorrelationId}",
                    newRowId, traceId);
            }

            try
            {
                await embedService.DeleteReportAsync(source.WorkspaceId, created.Id, profileId: null, ct: ct);
            }
            catch (Exception deleteEx)
            {
                logger.LogError(deleteEx, "Compensating delete of cloned report {ReportId} failed. CorrelationId={CorrelationId}",
                    created.Id, traceId);
            }

            return PowerBiFailed("The report could not be registered in the catalog.", traceId);
        }

        return TypedResults.Created(
            $"/api/reporting/reports/{newRowId:D}",
            new CreateReportResponse(newRowId, created.EmbedUrl, request.Name.Trim()));
    }

    /// <summary>
    /// PATCH /api/reporting/reports/{reportId} — update the catalog row AS THE CALLER (Author/Admin). Dataverse
    /// enforces the caller's Write.
    /// </summary>
    private static async Task<IResult> UpdateReport(
        Guid reportId,
        UpdateReportRequest request,
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var privilege = GetPrivilegeLevel(context);
        if (privilege < ReportingPrivilegeLevel.Author)
        {
            return InsufficientPrivilege("Report update requires Author or Admin privilege.");
        }

        var traceId = context.TraceIdentifier;
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        var row = await ReadActionableRowAsync(dataverseUser, powerBiOptions.Value, reportId, context, logger, ct);
        if (row is null)
        {
            return ReportNotInCatalog(traceId);
        }

        // Writing the name (unchanged when not supplied) also stamps modifiedon — the client's "keep the catalog in
        // sync after an in-place save".
        var name = string.IsNullOrWhiteSpace(request.Name) ? row.Name : request.Name.Trim();
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?> { ["sprk_name"] = name });

        DataverseUserResponse write;
        try
        {
            write = await dataverseUser.PatchAsync($"sprk_reports({row.RowId:D})", payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog row update faulted. CorrelationId={CorrelationId}", traceId);
            write = DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError, ex.Message);
        }

        return write.IsSuccess
            ? TypedResults.NoContent()
            : CatalogWriteFailed(write, "The catalog entry could not be updated.", traceId);
    }

    /// <summary>
    /// DELETE /api/reporting/reports/{reportId} — Admin only. The catalog row is deleted AS THE CALLER first (Dataverse
    /// enforces their Delete); only then may the derived Power BI report be deleted, so a caller who may not delete the
    /// row can never cause the report itself to be deleted.
    /// </summary>
    /// <remarks>
    /// <b>Which Power BI reports this route may delete</b> (task 166 r2, owner round 23 item 2): ONLY the report of a
    /// CUSTOM row (<c>sprk_iscustom</c> — one this module cloned) that NO other catalog row references. A standard
    /// (non-custom) report is shared and managed outside this route, and a report another row still points at is that
    /// row's too; in both cases the catalog row is deleted and the Power BI report is kept. "Does another row reference
    /// it?" is a server invariant, so it is read APP-ONLY (<see cref="IGenericEntityService"/>): a caller-scoped read
    /// would miss rows the caller cannot see and delete a report they still depend on. It returns nothing to the caller;
    /// an unreadable answer keeps the report (fail closed toward not destroying shared content).
    /// </remarks>
    private static async Task<IResult> DeleteReport(
        Guid reportId,
        [FromServices] ReportingEmbedService embedService,
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] IGenericEntityService entityService,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var privilege = GetPrivilegeLevel(context);
        if (privilege < ReportingPrivilegeLevel.Admin)
        {
            return InsufficientPrivilege("Report deletion requires Admin privilege.");
        }

        var traceId = context.TraceIdentifier;
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        // Task 166 f1 (owner round 25 item 6): the allowed-workspace check runs before ANY delete — a row naming a
        // workspace this deployment may not act on deletes nothing, not even itself.
        var row = await ReadActionableRowAsync(dataverseUser, powerBiOptions.Value, reportId, context, logger, ct);
        if (row is null)
        {
            return ReportNotInCatalog(traceId);
        }

        DataverseUserResponse deleted;
        try
        {
            deleted = await dataverseUser.DeleteAsync($"sprk_reports({row.RowId:D})", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog row delete faulted. CorrelationId={CorrelationId}", traceId);
            deleted = DataverseUserResponse.Fail(0, DataverseUserClientErrorCodes.ServiceError, ex.Message);
        }

        if (!deleted.IsSuccess)
        {
            return CatalogWriteFailed(deleted, "The catalog entry could not be deleted.", traceId);
        }

        if (!row.IsCustom)
        {
            logger.LogInformation(
                "Catalog row {CatalogRowId} deleted; its Power BI report {ReportId} is a standard (non-custom) report and is kept. "
                + "CorrelationId={CorrelationId}", row.RowId, row.PbiReportId, traceId);
            return TypedResults.NoContent();
        }

        if (await IsReferencedByAnotherCatalogRowAsync(entityService, row, logger, ct) is not false)
        {
            logger.LogInformation(
                "Catalog row {CatalogRowId} deleted; its Power BI report {ReportId} is kept (another catalog row references it, "
                + "or that could not be determined). CorrelationId={CorrelationId}", row.RowId, row.PbiReportId, traceId);
            return TypedResults.NoContent();
        }

        try
        {
            await embedService.DeleteReportAsync(row.WorkspaceId, row.PbiReportId, profileId: null, ct: ct);
            return TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Catalog row {CatalogRowId} deleted but Power BI report {ReportId} could not be. CorrelationId={CorrelationId}",
                row.RowId, row.PbiReportId, traceId);
            return PowerBiFailed(
                "The catalog entry was removed, but the Power BI report could not be deleted. An administrator should remove it.",
                traceId);
        }
    }

    /// <summary>
    /// POST /api/reporting/export
    /// Triggers a server-side export (PDF or PPTX) of a catalog report the caller can read, polls until the export job
    /// completes, then streams the resulting file to the caller.
    /// </summary>
    private static async Task<IResult> ExportReport(
        ReportingExportRequest request,
        [FromServices] ReportingEmbedService embedService,
        [FromServices] IDataverseUserClient dataverseUser,
        [FromServices] IOptions<PowerBiOptions> powerBiOptions,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (request.ReportId == Guid.Empty)
        {
            return MissingReportId("reportId is required.");
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
        if (WorkspacesUnconfigured(powerBiOptions.Value, traceId) is { } unconfigured)
        {
            return unconfigured;
        }

        var row = await ReadCatalogRowAsync(dataverseUser, request.ReportId, logger, ct);
        if (row is null)
        {
            return ReportNotInCatalog(traceId);
        }

        // Task 166 r2: an export is the same data path as an embed, so it carries the same server-computed RLS
        // identity (the caller's business unit). No identity, no export — an export without it would return every
        // business unit's rows.
        var businessUnitId = await ReadCallerBusinessUnitAsync(dataverseUser, logger, ct);
        if (businessUnitId is null)
        {
            return RlsIdentityUnavailable(
                "Your business unit could not be determined, so the report could not be exported. Try again.", traceId);
        }

        if (!await IsWorkspaceAllowedAsync(powerBiOptions.Value, row, businessUnitId, context, logger, ct))
        {
            return ReportNotInCatalog(traceId);
        }

        logger.LogInformation(
            "Export requested. CatalogRow={CatalogRowId}, Format={Format}, CorrelationId={CorrelationId}",
            row.RowId, request.Format, traceId);

        try
        {
            var fileStream = await embedService.ExportReportAsync(
                row.WorkspaceId,
                row.PbiReportId,
                request.Format,
                RlsUsername(businessUnitId.Value),
                [RlsRoleName],
                profileId: null,
                ct: ct);

            var contentType = request.Format == ExportFormat.PDF
                ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.presentationml.presentation";

            var fileExtension = request.Format == ExportFormat.PDF ? "pdf" : "pptx";
            var baseName = string.IsNullOrWhiteSpace(request.FileName) ? row.Name : request.FileName;
            var fileName = $"{SafeFileName(baseName)}.{fileExtension}";

            return Results.File(fileStream, contentType, fileName);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Export job failed. CatalogRow={CatalogRowId}, CorrelationId={CorrelationId}", row.RowId, traceId);

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
                "Export timed out. CatalogRow={CatalogRowId}, CorrelationId={CorrelationId}", row.RowId, traceId);

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
                "Export failed unexpectedly. CatalogRow={CatalogRowId}, CorrelationId={CorrelationId}", row.RowId, traceId);

            return PowerBiFailed("An unexpected error occurred during report export.", traceId);
        }
    }

    // -----------------------------------------------------------------------------------------
    // The catalog binding (task 166 r1)
    // -----------------------------------------------------------------------------------------

    /// <summary>One <c>sprk_report</c> row the caller could read, with its Power BI ids parsed.</summary>
    internal sealed record CatalogRow(
        Guid RowId,
        string Name,
        Guid PbiReportId,
        Guid WorkspaceId,
        Guid? DatasetId,
        string? EmbedUrl,
        int? Category,
        bool IsCustom)
    {
        public ReportCatalogItem ToItem() => new(
            RowId,
            Name,
            EmbedUrl ?? string.Empty,
            DatasetId?.ToString("D"),
            CategoryName(Category),
            IsCustom);
    }

    /// <summary>
    /// Reads ONE catalog row AS THE CALLER and parses its Power BI ids, or <see langword="null"/> — the uniform
    /// "not in your catalog" — when the row is absent, unreadable to the caller, the read fails or faults, or the row
    /// does not carry a usable Power BI report id and workspace id. Never falls back to an app-only read.
    /// </summary>
    internal static async Task<CatalogRow?> ReadCatalogRowAsync(
        IDataverseUserClient dataverseUser, Guid rowId, ILogger logger, CancellationToken ct)
    {
        if (rowId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var response = await dataverseUser.GetAsync(CatalogRowPath(rowId), ct);
            if (!response.IsSuccess || response.Body is not { } body || body.ValueKind != JsonValueKind.Object)
            {
                logger.LogInformation(
                    "Catalog row {CatalogRowId} not readable by the caller ({Status} {ErrorCode}); answering not-in-catalog.",
                    rowId, response.StatusCode, response.ErrorCode);
                return null;
            }

            var row = ParseCatalogRow(body);
            if (row is null || row.RowId != rowId)
            {
                logger.LogWarning(
                    "Catalog row {CatalogRowId} lacks a usable Power BI report or workspace id; answering not-in-catalog.", rowId);
                return null;
            }

            return row;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Catalog row {CatalogRowId} read faulted; answering not-in-catalog (fail closed).", rowId);
            return null;
        }
    }

    // -----------------------------------------------------------------------------------------
    // The allowed-workspace check (task 166 f1; owner round 25 item 6)
    // -----------------------------------------------------------------------------------------

    /// <summary>The configuration error: no workspace is allowed, so no report can be acted on (fail closed).</summary>
    internal const string WorkspacesUnconfiguredCode = "sdap.reporting.config.workspaces_unconfigured";

    /// <summary>
    /// The four <c>sprk_report</c> columns that point a catalog row at Power BI — field-secured, writable by the BFF
    /// identity only (<c>scripts/Set-ReportCatalogFieldSecurity.ps1</c>), so only server code ever chooses them.
    /// </summary>
    internal static readonly string[] CatalogPointerColumns =
        ["sprk_pbi_reportid", "sprk_workspaceid", "sprk_datasetid", "sprk_iscustom"];

    /// <summary>The app-only write of a catalog row's Power BI pointer (see <see cref="CatalogPointerColumns"/>).</summary>
    internal static Dictionary<string, object> CatalogPointerFields(Guid pbiReportId, Guid workspaceId, Guid datasetId, bool isCustom)
        => new()
        {
            ["sprk_pbi_reportid"] = pbiReportId.ToString("D"),
            ["sprk_workspaceid"] = workspaceId.ToString("D"),
            ["sprk_datasetid"] = datasetId.ToString("D"),
            ["sprk_iscustom"] = isCustom,
        };

    /// <summary>503 before anything is read when no workspace is configured; null when the check can run.</summary>
    internal static IResult? WorkspacesUnconfigured(PowerBiOptions options, string traceId) =>
        options.AllowedWorkspaces is { Count: > 0 }
            ? null
            : Results.Problem(
                title: "Reporting Not Configured",
                detail: "No Power BI workspace is configured for this deployment, so no report can be opened. An "
                        + "administrator must set PowerBi:AllowedWorkspaces.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = WorkspacesUnconfiguredCode,
                    ["correlationId"] = traceId
                });

    /// <summary>
    /// May this deployment act on <paramref name="row"/>'s Power BI workspace for this caller? The workspace must be one of
    /// <see cref="PowerBiOptions.AllowedWorkspaces"/>; an entry bound to a customer business unit admits only a caller
    /// whose business unit is that unit or beneath it (read through the one business-unit hierarchy reader,
    /// <see cref="RecordContainerResolver.IsBusinessUnitInSubtreeAsync"/>). Anything unknown — no entry, an unreadable
    /// caller business unit for a bound entry, no resolver, a fault — is NO (the caller answers "not in your catalog").
    /// </summary>
    internal static async Task<bool> IsWorkspaceAllowedAsync(
        PowerBiOptions options, CatalogRow row, Guid? callerBusinessUnit, HttpContext context, ILogger logger, CancellationToken ct)
    {
        var entries = options.AllowedWorkspaces.Where(w => w.WorkspaceId == row.WorkspaceId && w.WorkspaceId != Guid.Empty).ToList();
        if (entries.Count == 0)
        {
            logger.LogWarning(
                "Catalog row {CatalogRowId} names Power BI workspace {WorkspaceId}, which is not an allowed workspace of this "
                + "deployment (PowerBi:AllowedWorkspaces); answering not-in-catalog.", row.RowId, row.WorkspaceId);
            return false;
        }

        if (entries.Any(w => w.CustomerBusinessUnitId is null))
        {
            return true;
        }

        if (callerBusinessUnit is not { } unit
            || context.RequestServices.GetService<RecordContainerResolver>() is not { } hierarchy)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (await hierarchy.IsBusinessUnitInSubtreeAsync(unit, entry.CustomerBusinessUnitId!.Value, ct))
            {
                return true;
            }
        }

        logger.LogWarning(
            "Catalog row {CatalogRowId}'s workspace {WorkspaceId} belongs to another customer than the caller's business unit "
            + "{BusinessUnitId}; answering not-in-catalog.", row.RowId, row.WorkspaceId, unit);
        return false;
    }

    /// <summary>
    /// <see cref="ReadCatalogRowAsync"/> plus the allowed-workspace check: the row, or <see langword="null"/> — the uniform
    /// "not in your catalog" — when it is not readable OR names a workspace this deployment may not act on for the caller.
    /// </summary>
    internal static async Task<CatalogRow?> ReadActionableRowAsync(
        IDataverseUserClient dataverseUser, PowerBiOptions options, Guid rowId, HttpContext context, ILogger logger,
        CancellationToken ct)
    {
        var row = await ReadCatalogRowAsync(dataverseUser, rowId, logger, ct);
        if (row is null)
        {
            return null;
        }

        // The caller's business unit is needed only for a customer-bound workspace.
        var bound = options.AllowedWorkspaces.Any(w => w.WorkspaceId == row.WorkspaceId && w.CustomerBusinessUnitId is not null);
        var businessUnit = bound ? await ReadCallerBusinessUnitAsync(dataverseUser, logger, ct) : null;
        return await IsWorkspaceAllowedAsync(options, row, businessUnit, context, logger, ct) ? row : null;
    }

    private static CatalogRow? ParseCatalogRow(JsonElement row)
    {
        if (!Guid.TryParse(Str(row, "sprk_reportid"), out var rowId)
            || !Guid.TryParse(Str(row, "sprk_pbi_reportid"), out var pbiReportId) || pbiReportId == Guid.Empty
            || !Guid.TryParse(Str(row, "sprk_workspaceid"), out var workspaceId) || workspaceId == Guid.Empty)
        {
            return null;
        }

        Guid? datasetId = Guid.TryParse(Str(row, "sprk_datasetid"), out var ds) && ds != Guid.Empty ? ds : null;
        int? category = row.TryGetProperty("sprk_category", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetInt32()
            : null;
        var isCustom = row.TryGetProperty("sprk_iscustom", out var custom) && custom.ValueKind == JsonValueKind.True;

        return new CatalogRow(
            rowId, Str(row, "sprk_name") ?? string.Empty, pbiReportId, workspaceId, datasetId,
            Str(row, "sprk_embedurl"), category, isCustom);
    }

    private static string? Str(JsonElement row, string property)
        => row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string CategoryName(int? category) => category switch
    {
        100000000 => "Financial",
        100000001 => "Operational",
        100000002 => "Compliance",
        100000003 => "Documents",
        _ => "Custom",
    };

    /// <summary>
    /// The caller's business unit, from WhoAmI issued AS THE CALLER — the server-computed RLS identity. Null when it
    /// cannot be read (no token, OBO failure, any fault): the embed-token route then issues nothing.
    /// </summary>
    internal static async Task<Guid?> ReadCallerBusinessUnitAsync(
        IDataverseUserClient dataverseUser, ILogger logger, CancellationToken ct)
    {
        try
        {
            var response = await dataverseUser.GetAsync("WhoAmI", ct);
            if (response.IsSuccess && response.Body is { } body
                && Guid.TryParse(Str(body, "BusinessUnitId"), out var businessUnitId)
                && businessUnitId != Guid.Empty)
            {
                return businessUnitId;
            }

            logger.LogWarning("WhoAmI as the caller returned no business unit ({Status} {ErrorCode}).",
                response.StatusCode, response.ErrorCode);
            return null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WhoAmI as the caller faulted; no RLS identity.");
            return null;
        }
    }

    /// <summary>The RLS username: the business unit id the report models' DAX looks up (lowercase "D").</summary>
    internal static string RlsUsername(Guid businessUnitId) => businessUnitId.ToString("D"); // "D" is lowercase hex

    /// <summary>
    /// The live Create privilege on <c>sprk_report</c> (read-only check on spaarkedev1, 2026-10-04:
    /// <c>privileges?$filter=name eq 'prvCreatesprk_Report'</c> → id <c>4ea28bbd…</c>, accessright 32 = Create).
    /// </summary>
    internal const string CreateReportPrivilege = "prvCreatesprk_Report";

    /// <summary>
    /// Does the caller hold <see cref="CreateReportPrivilege"/>, asked AS THE CALLER (OBO WhoAmI +
    /// RetrieveUserSetOfPrivilegesByNames — the task-130 G5 precedent)? No token, OBO failure, any fault → false.
    /// </summary>
    internal static async Task<bool> CallerMayCreateCatalogRowAsync(
        CallerRecordAccessProbe accessProbe, HttpContext context, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await accessProbe.CallerHoldsPrivilegeAsync(
                TokenHelper.ExtractBearerTokenOrNull(context), CreateReportPrivilege, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The {Privilege} check faulted; refusing the create (fail closed).", CreateReportPrivilege);
            return false;
        }
    }

    /// <summary>
    /// Does a catalog row OTHER than <paramref name="row"/> reference <paramref name="row"/>'s Power BI report? Read
    /// APP-ONLY (a server invariant: the caller may not see every row that depends on the report), active and inactive
    /// rows alike. <see langword="true"/> = referenced, <see langword="false"/> = no other row, <see langword="null"/> =
    /// could not be determined (the caller keeps the report).
    /// </summary>
    internal static async Task<bool?> IsReferencedByAnotherCatalogRowAsync(
        IGenericEntityService entityService, CatalogRow row, ILogger logger, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_report")
        {
            ColumnSet = new ColumnSet("sprk_reportid"),
            TopCount = 1,
            Criteria = new FilterExpression(LogicalOperator.And)
            {
                Conditions =
                {
                    new ConditionExpression("sprk_pbi_reportid", ConditionOperator.Equal, row.PbiReportId.ToString("D")),
                    new ConditionExpression("sprk_reportid", ConditionOperator.NotEqual, row.RowId),
                },
            },
        };

        try
        {
            var others = await entityService.RetrieveMultipleAsync(query, ct);
            return others?.Entities is { } entities ? entities.Count > 0 : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Whether another catalog row references Power BI report {ReportId} could not be read; keeping the report.",
                row.PbiReportId);
            return null;
        }
    }

    // -----------------------------------------------------------------------------------------
    // Responses
    // -----------------------------------------------------------------------------------------

    /// <summary>The ONE answer for an absent catalog report and one the caller cannot read (task 166 r1).</summary>
    internal static IResult ReportNotInCatalog(string traceId) =>
        Results.Problem(
            title: "Report Not Found",
            detail: "The report was not found or you do not have access to it.",
            statusCode: StatusCodes.Status404NotFound,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ReportNotInCatalogReasonCode,
                ["reasonCode"] = ReportNotInCatalogReasonCode,
                ["correlationId"] = traceId
            });

    /// <summary>No business-unit RLS identity could be computed: no token is issued and no export runs.</summary>
    private static IResult RlsIdentityUnavailable(string detail, string traceId) =>
        Results.Problem(
            title: "Report Identity Unavailable",
            detail: detail,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ErrorCodeRlsIdentityUnavailable,
                ["correlationId"] = traceId
            });

    private static IResult MissingReportId(string detail) =>
        Results.Problem(
            title: "Missing Parameter",
            detail: detail,
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeMissingReportId });

    private static IResult InsufficientPrivilege(string detail) =>
        Results.Problem(
            title: "Forbidden",
            detail: detail,
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorCodeInsufficientPrivilege });

    private static IResult PowerBiFailed(string detail, string traceId) =>
        Results.Problem(
            title: "Power BI Service Error",
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ErrorCodePowerBiFailed,
                ["correlationId"] = traceId
            });

    private static IResult CatalogUnavailable(string traceId) =>
        Results.Problem(
            title: "Report Catalog Unavailable",
            detail: "The report catalog could not be read. Try again.",
            statusCode: StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ErrorCodeCatalogUnavailable,
                ["correlationId"] = traceId
            });

    /// <summary>A catalog write refused or failed AS THE CALLER: 403 when Dataverse denied it, else 502.</summary>
    private static IResult CatalogWriteFailed(DataverseUserResponse write, string detail, string traceId) =>
        Results.Problem(
            title: write.ErrorCode == DataverseUserClientErrorCodes.AccessDenied ? "Forbidden" : "Report Catalog Error",
            detail: write.ErrorCode == DataverseUserClientErrorCodes.AccessDenied
                ? detail + " You do not have permission to change the report catalog."
                : detail,
            statusCode: write.ErrorCode == DataverseUserClientErrorCodes.AccessDenied
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status502BadGateway,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = ErrorCodeCatalogWriteFailed,
                ["correlationId"] = traceId
            });

    /// <summary>A download file name: the report name with path and reserved characters removed.</summary>
    private static string SafeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((name ?? "report").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "report" : cleaned;
    }

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
}
