using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/provision-project
///
/// Provisions all infrastructure required for a Secure Project in a single
/// atomic-ish operation. Called by the Create Project wizard immediately after
/// creating the sprk_project record when the Secure Project toggle is enabled.
///
/// Provisioning sequence:
///   1. Validate request and confirm the project exists with sprk_issecure = true
///   2a. If UmbrellaBuId provided → resolve existing BU and its Account (skip creation)
///   2b. Otherwise → create child Business Unit named SP-{ProjectRef}
///   3. Create SPE container via SpeFileStore facade (ADR-007)
///   4. Create External Access Account owned by the new BU (unless umbrella reuse)
///   5. Store all three references on the project record
///
/// Rollback:
///   If SPE container creation or Account creation fails after the BU has been
///   created, the endpoint attempts to delete the newly created BU before
///   returning a 500 to leave the system in a consistent state.
///
/// Authentication: Azure AD JWT (RequireAuthorization via the adminGroup).
/// ADR-001: Minimal API — no controllers.
/// ADR-007: SPE container created through SpeFileStore facade.
/// ADR-008: Authorization applied at route group level in ExternalAccessEndpoints.
/// ADR-010: Concrete DI injections.
/// </summary>
public static class ProvisionProjectEndpoint
{
    private const string ProjectEntitySet = "sprk_projects";
    private const string BusinessUnitEntitySet = "businessunits";
    private const string AccountEntitySet = "accounts";

    /// <summary>
    /// Registers the provision-project endpoint on the external-access management group.
    /// </summary>
    public static RouteGroupBuilder MapProvisionProjectEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/provision-project", ProvisionProjectAsync)
            .WithName("ProvisionSecureProject")
            .WithSummary("Provision infrastructure for a new Secure Project")
            .WithDescription(
                "Creates a child Business Unit (SP-{ProjectRef}), an SPE container, and an " +
                "External Access Account for the project, then stores all references on the " +
                "sprk_project record. Supports umbrella BU reuse for multi-project organisations.")
            .Produces<ProvisionProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> ProvisionProjectAsync(
        ProvisionProjectRequest request,
        DataverseWebApiClient dataverseClient,
        SpeFileStore speFileStore,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.ProjectId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ProjectId is required and must be a valid GUID.");

        if (request.UmbrellaBuId == null && string.IsNullOrWhiteSpace(request.ProjectRef))
            return ProblemDetailsHelper.ValidationError(
                "ProjectRef is required when UmbrellaBuId is not provided.");

        var traceId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "[PROVISION] Starting secure project provisioning: ProjectId={ProjectId}, ProjectRef={ProjectRef}, " +
            "UmbrellaBuId={UmbrellaBuId}, TraceId={TraceId}",
            request.ProjectId, request.ProjectRef, request.UmbrellaBuId, traceId);

        // ── Step 1: Confirm project exists with sprk_issecure = true ─────────
        ProjectRow? projectRow;
        try
        {
            var rows = await dataverseClient.QueryAsync<ProjectRow>(
                ProjectEntitySet,
                filter: $"sprk_projectid eq {request.ProjectId}",
                select: "sprk_projectid,sprk_projectname,sprk_issecure",
                top: 1,
                cancellationToken: ct);

            projectRow = rows.FirstOrDefault();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to query project {ProjectId} from Dataverse", request.ProjectId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to retrieve project record from Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }

        if (projectRow == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"Project {request.ProjectId} not found.",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
        }

        if (projectRow.sprk_issecure != true)
        {
            return ProblemDetailsHelper.ValidationError(
                $"Project {request.ProjectId} is not a Secure Project (sprk_issecure is false or null). " +
                "Enable the Secure Project toggle before provisioning.");
        }

        var projectName = projectRow.sprk_projectname ?? request.ProjectRef ?? request.ProjectId.ToString();

        // ── Step 2: Business Unit resolution ─────────────────────────────────
        Guid buId;
        string buName;
        Guid accountId;
        string accountName;
        bool wasUmbrellaBu = false;
        Guid? newlyCreatedBuId = null;   // Track for rollback

        if (request.UmbrellaBuId.HasValue && request.UmbrellaBuId.Value != Guid.Empty)
        {
            // ── 2a: Umbrella BU — resolve existing BU and its Account ────────
            logger.LogInformation(
                "[PROVISION] Using umbrella BU {UmbrellaBuId} for project {ProjectId}",
                request.UmbrellaBuId.Value, request.ProjectId);

            BuRow? buRow;
            try
            {
                var buRows = await dataverseClient.QueryAsync<BuRow>(
                    BusinessUnitEntitySet,
                    filter: $"businessunitid eq {request.UmbrellaBuId.Value}",
                    select: "businessunitid,name",
                    top: 1,
                    cancellationToken: ct);

                buRow = buRows.FirstOrDefault();
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[PROVISION] Failed to query umbrella BU {UmbrellaBuId}", request.UmbrellaBuId.Value);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Failed to retrieve umbrella Business Unit from Dataverse.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            if (buRow == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"Umbrella Business Unit {request.UmbrellaBuId.Value} not found.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            buId = request.UmbrellaBuId.Value;
            buName = buRow.name ?? $"BU-{buId}";
            wasUmbrellaBu = true;

            // Resolve the Account owned by the umbrella BU
            var resolvedAccount = await ResolveAccountForBuAsync(
                dataverseClient, buId, logger, ct);

            if (resolvedAccount == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Not Found",
                    detail: $"No Account found owned by umbrella Business Unit {buId}. " +
                            "The umbrella BU must have an associated Account record.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            accountId = resolvedAccount.Value.Id;
            accountName = resolvedAccount.Value.Name;

            logger.LogInformation(
                "[PROVISION] Resolved umbrella BU {BuId} ({BuName}) and Account {AccountId} ({AccountName})",
                buId, buName, accountId, accountName);
        }
        else
        {
            // ── 2b: Create a new child Business Unit ──────────────────────────
            var buDisplayName = $"SP-{request.ProjectRef!.Trim()}";

            logger.LogInformation(
                "[PROVISION] Creating child Business Unit '{BuName}' for project {ProjectId}",
                buDisplayName, request.ProjectId);

            try
            {
                // Resolve the root BU ID — required as parent for the new child BU
                var rootBuId = await ResolveRootBusinessUnitIdAsync(dataverseClient, ct);

                if (rootBuId == null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Internal Server Error",
                        detail: "Failed to resolve root Business Unit. Ensure the organisation has a root BU.",
                        extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
                }

                var buPayload = new Dictionary<string, object?>
                {
                    ["name"] = buDisplayName,
                    ["parentbusinessunitid@odata.bind"] = $"/businessunits({rootBuId})",
                    ["description"] = $"Secure Project isolation BU for project: {projectName}",
                };

                buId = await dataverseClient.CreateAsync(BusinessUnitEntitySet, buPayload, ct);
                newlyCreatedBuId = buId;
                buName = buDisplayName;

                logger.LogInformation(
                    "[PROVISION] Created child Business Unit {BuId} ({BuName})", buId, buName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[PROVISION] Failed to create child Business Unit '{BuName}' for project {ProjectId}",
                    $"SP-{request.ProjectRef}", request.ProjectId);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Failed to create Business Unit in Dataverse.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            // ── Step 3: Create SPE container ──────────────────────────────────
            // (Moved here so rollback can delete the BU if SPE creation fails)
            var containerResult = await CreateSpeContainerAsync(
                speFileStore, configuration, projectName, request.ProjectId, buId,
                newlyCreatedBuId, dataverseClient, logger, traceId, ct);

            if (containerResult.Error != null)
                return containerResult.Error;

            var speContainerId = containerResult.ContainerId!;

            // ── Step 4: Create External Access Account owned by BU ────────────
            logger.LogInformation(
                "[PROVISION] Creating External Access Account for BU {BuId}", buId);

            try
            {
                var accountPayload = new Dictionary<string, object?>
                {
                    ["name"] = $"External Access — {projectName}",
                    ["description"] = $"External access account for Secure Project: {projectName}",
                    ["owningbusinessunit@odata.bind"] = $"/businessunits({buId})",
                };

                accountId = await dataverseClient.CreateAsync(AccountEntitySet, accountPayload, ct);
                accountName = $"External Access — {projectName}";

                logger.LogInformation(
                    "[PROVISION] Created External Access Account {AccountId} ({AccountName})",
                    accountId, accountName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "[PROVISION] Failed to create External Access Account for BU {BuId}. Rolling back BU {BuId}.",
                    buId, buId);

                await AttemptRollbackBuAsync(dataverseClient, buId, logger, ct);

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Failed to create External Access Account in Dataverse. Business Unit has been rolled back.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId });
            }

            // ── Step 5: Store references on project record ────────────────────
            await StoreProjectReferencesAsync(
                dataverseClient, request.ProjectId, buId, speContainerId, accountId, logger, ct);

            logger.LogInformation(
                "[PROVISION] Provisioning complete for project {ProjectId}: " +
                "BU={BuId}, Container={ContainerId}, Account={AccountId}",
                request.ProjectId, buId, speContainerId, accountId);

            return TypedResults.Ok(new ProvisionProjectResponse(
                BusinessUnitId: buId,
                BusinessUnitName: buName,
                SpeContainerId: speContainerId,
                AccountId: accountId,
                AccountName: accountName,
                WasUmbrellaBu: false));
        }

        // ── For umbrella BU path: Steps 3 and 5 ──────────────────────────────
        var umbrellaContainerResult = await CreateSpeContainerAsync(
            speFileStore, configuration, projectName, request.ProjectId, buId,
            newlyCreatedBuId, dataverseClient, logger, traceId, ct);

        if (umbrellaContainerResult.Error != null)
            return umbrellaContainerResult.Error;

        var umbrellaContainerId = umbrellaContainerResult.ContainerId!;

        await StoreProjectReferencesAsync(
            dataverseClient, request.ProjectId, buId, umbrellaContainerId, accountId, logger, ct);

        logger.LogInformation(
            "[PROVISION] Provisioning complete (umbrella BU) for project {ProjectId}: " +
            "BU={BuId}, Container={ContainerId}, Account={AccountId}",
            request.ProjectId, buId, umbrellaContainerId, accountId);

        return TypedResults.Ok(new ProvisionProjectResponse(
            BusinessUnitId: buId,
            BusinessUnitName: buName,
            SpeContainerId: umbrellaContainerId,
            AccountId: accountId,
            AccountName: accountName,
            WasUmbrellaBu: wasUmbrellaBu));
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// Creates the SPE container via SpeFileStore facade (ADR-007).
    /// On failure, attempts to roll back any newly created BU before returning the error result.
    /// </summary>
    private static async Task<SpeContainerCreationResult> CreateSpeContainerAsync(
        SpeFileStore speFileStore,
        IConfiguration configuration,
        string projectName,
        Guid projectId,
        Guid buId,
        Guid? newlyCreatedBuId,
        DataverseWebApiClient dataverseClient,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        var containerTypeIdStr = configuration["SharePointEmbedded:ContainerTypeId"];
        if (!Guid.TryParse(containerTypeIdStr, out var containerTypeId))
        {
            logger.LogError(
                "[PROVISION] SharePointEmbedded:ContainerTypeId is not configured or invalid: '{Value}'",
                containerTypeIdStr);

            if (newlyCreatedBuId.HasValue)
                await AttemptRollbackBuAsync(dataverseClient, newlyCreatedBuId.Value, logger, ct);

            return new SpeContainerCreationResult(null, Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "SPE ContainerTypeId is not configured on the BFF API.",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId }));
        }

        logger.LogInformation(
            "[PROVISION] Creating SPE container for project {ProjectId} (BU {BuId})", projectId, buId);

        try
        {
            var containerDisplayName = $"Secure Project — {projectName}";
            var containerDescription = $"Isolated document container for Secure Project: {projectName}";

            var container = await speFileStore.CreateContainerAsync(
                containerTypeId, containerDisplayName, containerDescription, ct);

            if (container == null)
            {
                logger.LogError(
                    "[PROVISION] SpeFileStore.CreateContainerAsync returned null for project {ProjectId}",
                    projectId);

                if (newlyCreatedBuId.HasValue)
                    await AttemptRollbackBuAsync(dataverseClient, newlyCreatedBuId.Value, logger, ct);

                return new SpeContainerCreationResult(null, Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Failed to provision SPE container — Graph API returned null.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = traceId }));
            }

            logger.LogInformation(
                "[PROVISION] Created SPE container {ContainerId} ('{DisplayName}') for project {ProjectId}",
                container.Id, containerDisplayName, projectId);

            return new SpeContainerCreationResult(container.Id, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Failed to create SPE container for project {ProjectId}", projectId);

            if (newlyCreatedBuId.HasValue)
                await AttemptRollbackBuAsync(dataverseClient, newlyCreatedBuId.Value, logger, ct);

            return new SpeContainerCreationResult(null, Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to provision SPE container. Business Unit has been rolled back.",
                extensions: new Dictionary<string, object?> { ["traceId"] = traceId }));
        }
    }

    /// <summary>
    /// Stores the three infrastructure references on the sprk_project record.
    /// Non-fatal on failure — logs a warning but does not halt provisioning.
    /// </summary>
    private static async Task StoreProjectReferencesAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
        Guid buId,
        string speContainerId,
        Guid accountId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var updatePayload = new Dictionary<string, object?>
            {
                // Child BU ID for Dataverse row-level security isolation
                ["sprk_securitybuid@odata.bind"] = $"/businessunits({buId})",
                // SPE container ID for document storage
                ["sprk_specontainerid"] = speContainerId,
                // External Access Account linked to BU
                ["sprk_externalaccountid@odata.bind"] = $"/accounts({accountId})",
            };

            await dataverseClient.UpdateAsync(ProjectEntitySet, projectId, updatePayload, ct);

            logger.LogInformation(
                "[PROVISION] Stored references on project {ProjectId}: BU={BuId}, Container={ContainerId}, Account={AccountId}",
                projectId, buId, speContainerId, accountId);
        }
        catch (Exception ex)
        {
            // Log and continue — the response already contains the provisioned IDs
            // so the caller can retry storing them separately if needed.
            logger.LogWarning(ex,
                "[PROVISION] Failed to store infrastructure references on project {ProjectId}. " +
                "Infrastructure was created successfully — references can be stored manually. " +
                "BU={BuId}, Container={ContainerId}, Account={AccountId}",
                projectId, buId, speContainerId, accountId);
        }
    }

    /// <summary>
    /// Resolves the root (top-level) Business Unit ID for the organisation.
    /// The root BU has no parent — identified by a null parentbusinessunitid.
    /// </summary>
    private static async Task<Guid?> ResolveRootBusinessUnitIdAsync(
        DataverseWebApiClient dataverseClient,
        CancellationToken ct)
    {
        try
        {
            var rows = await dataverseClient.QueryAsync<BuRow>(
                BusinessUnitEntitySet,
                filter: "parentbusinessunitid eq null",
                select: "businessunitid,name",
                top: 1,
                cancellationToken: ct);

            var root = rows.FirstOrDefault();
            return root?.businessunitid;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the Account owned by the given Business Unit.
    /// Returns null if no Account is found.
    /// </summary>
    private static async Task<(Guid Id, string Name)?> ResolveAccountForBuAsync(
        DataverseWebApiClient dataverseClient,
        Guid buId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var rows = await dataverseClient.QueryAsync<AccountRow>(
                AccountEntitySet,
                filter: $"_owningbusinessunit_value eq {buId}",
                select: "accountid,name",
                top: 1,
                cancellationToken: ct);

            var account = rows.FirstOrDefault();
            if (account?.accountid == null) return null;

            return (account.accountid.Value, account.name ?? $"Account-{account.accountid.Value}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PROVISION] Error resolving Account for BU {BuId}", buId);
            return null;
        }
    }

    /// <summary>
    /// Best-effort rollback: attempt to delete a newly created BU on provisioning failure.
    /// Logs but does not throw — rollback failure is non-blocking.
    /// </summary>
    private static async Task AttemptRollbackBuAsync(
        DataverseWebApiClient dataverseClient,
        Guid buId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await dataverseClient.DeleteAsync(BusinessUnitEntitySet, buId, ct);
            logger.LogInformation("[PROVISION] Rollback: deleted Business Unit {BuId}", buId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[PROVISION] Rollback FAILED: could not delete Business Unit {BuId}. " +
                "Manual cleanup may be required.", buId);
        }
    }

    // =========================================================================
    // Private record types
    // =========================================================================

    /// <summary>Internal result wrapper for SPE container creation with optional error result.</summary>
    private sealed record SpeContainerCreationResult(string? ContainerId, IResult? Error);

    // ── Dataverse row DTOs ────────────────────────────────────────────────

    private sealed class ProjectRow
    {
        [JsonPropertyName("sprk_projectid")]
        public Guid sprk_projectid { get; set; }

        [JsonPropertyName("sprk_projectname")]
        public string? sprk_projectname { get; set; }

        [JsonPropertyName("sprk_issecure")]
        public bool? sprk_issecure { get; set; }
    }

    private sealed class BuRow
    {
        [JsonPropertyName("businessunitid")]
        public Guid? businessunitid { get; set; }

        [JsonPropertyName("name")]
        public string? name { get; set; }
    }

    private sealed class AccountRow
    {
        [JsonPropertyName("accountid")]
        public Guid? accountid { get; set; }

        [JsonPropertyName("name")]
        public string? name { get; set; }
    }
}
