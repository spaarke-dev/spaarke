using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Endpoints.Filters;
using Sprk.Bff.Api.Models.Registration;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Endpoints;

/// <summary>
/// Registration endpoints following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Provides demo access request submission (unauthenticated), approval, and rejection.
/// </summary>
public static class RegistrationEndpoints
{
    public static IEndpointRouteBuilder MapRegistrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/registration")
            .WithTags("Registration");

        // POST /api/registration/demo-request — Submit new demo request (UNAUTHENTICATED)
        group.MapPost("/demo-request", SubmitDemoRequest)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous")
            .WithName("SubmitDemoRequest")
            .WithSummary("Submit a new demo access request")
            .WithDescription("Creates a new demo access request. Validates input, checks for duplicate emails, blocks disposable email domains, creates a Dataverse record, and sends admin notification. No authentication required.")
            .Produces<DemoRequestResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(400)
            .ProducesProblem(409)
            .ProducesProblem(429)
            .ProducesProblem(500);

        // POST /api/registration/requests/{id}/approve — Approve a pending request (ADMIN ONLY)
        group.MapPost("/requests/{id:guid}/approve", ApproveRequest)
            .RequireAuthorization()
            .AddRegistrationAuthorizationFilter()
            .WithName("ApproveRegistrationRequest")
            .WithSummary("Approve a pending demo registration request")
            .WithDescription("Validates the request exists and is in Submitted status, then provisions demo access. Requires admin role.")
            .Produces<ApproveResponseDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        // POST /api/registration/requests/{id}/reject — Reject a pending request (ADMIN ONLY)
        group.MapPost("/requests/{id:guid}/reject", RejectRequest)
            .RequireAuthorization()
            .AddRegistrationAuthorizationFilter()
            .WithName("RejectRegistrationRequest")
            .WithSummary("Reject a pending demo registration request")
            .WithDescription("Validates the request exists and is in Submitted status, then updates status to Rejected with a reason. Requires admin role.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Submit a new demo access request (unauthenticated).
    /// POST /api/registration/demo-request
    /// </summary>
    private static async Task<IResult> SubmitDemoRequest(
        DemoRequestDto request,
        RegistrationDataverseService dataverseService,
        RegistrationEmailService emailService,
        EmailDomainValidator domainValidator,
        IOptions<DemoProvisioningOptions> options,
        ILogger<RegistrationDataverseService> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.FirstName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "First name is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Email address is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        if (!request.ConsentAccepted)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Consent must be accepted to submit a demo request.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // Block disposable email domains
        if (domainValidator.IsDisposableDomain(request.Email))
        {
            logger.LogWarning(
                "Demo request blocked: disposable email domain detected for {Email}, TraceId={TraceId}",
                request.Email, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Please use a business or personal email address. Disposable email addresses are not accepted.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // Check for duplicate active/pending requests by email
        try
        {
            var existingRequest = await dataverseService.CheckDuplicateByEmailAsync(request.Email, cancellationToken);
            if (existingRequest != null)
            {
                logger.LogInformation(
                    "Duplicate demo request detected for {Email}, existing TrackingId={TrackingId}, TraceId={TraceId}",
                    request.Email, existingRequest.TrackingId, httpContext.TraceIdentifier);

                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Duplicate Request",
                    detail: "A demo request for this email address is already pending or active. Please check your email for access details.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check duplicate email for {Email}, TraceId={TraceId}",
                request.Email, httpContext.TraceIdentifier);
            // Continue with submission — duplicate check is not critical
        }

        // Create the registration request record in Dataverse
        try
        {
            var createRequest = new RegistrationRequestCreate
            {
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                Organization = request.Organization,
                JobTitle = request.JobTitle,
                Phone = request.Phone,
                UseCase = ParseUseCase(request.UseCase),
                ReferralSource = ParseReferralSource(request.ReferralSource),
                Notes = request.Notes,
                ConsentAccepted = request.ConsentAccepted
            };

            var (recordId, trackingId) = await dataverseService.CreateRequestAsync(createRequest, cancellationToken);

            logger.LogInformation(
                "Demo request created: RecordId={RecordId}, TrackingId={TrackingId}, Email={Email}, TraceId={TraceId}",
                recordId, trackingId, request.Email, httpContext.TraceIdentifier);

            // Send admin notification (fire-and-forget — do not block the response)
            _ = SendAdminNotificationAsync(
                emailService, options.Value, trackingId, request, recordId, logger, httpContext.TraceIdentifier,
                dataverseUrl: null, appId: null);

            // Send acknowledgement email to applicant (fire-and-forget)
            _ = SendAcknowledgementEmailAsync(
                emailService, trackingId, request, logger, httpContext.TraceIdentifier);

            return Results.Accepted(
                uri: null,
                value: new DemoRequestResponse
                {
                    TrackingId = trackingId,
                    Message = "Your demo request has been submitted successfully. You will receive an email when your access is ready."
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create demo request for {Email}, TraceId={TraceId}",
                request.Email, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to submit demo request. Please try again later.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }
    }

    /// <summary>
    /// Approve a pending registration request (admin only).
    /// POST /api/registration/requests/{id}/approve
    /// </summary>
    private static async Task<IResult> ApproveRequest(
        Guid id,
        RegistrationDataverseService dataverseService,
        DataverseEnvironmentService environmentService,
        DemoProvisioningService provisioningService,
        ILogger<RegistrationDataverseService> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Validate the request exists
        var existingRequest = await dataverseService.GetRequestByIdAsync(id, cancellationToken);
        if (existingRequest == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"Registration request {id} not found.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // Validate status is Submitted
        if (existingRequest.Status != Services.Registration.RegistrationStatus.Submitted)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: $"Registration request cannot be approved because its status is '{existingRequest.Status}'. Only requests with 'Submitted' status can be approved.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // FR-09: Validate environment is linked to the request
        if (!existingRequest.DataverseEnvironmentId.HasValue)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "No target environment selected for this registration request. Please select a Target Environment on the request form before approving.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        try
        {
            // FR-08: Read environment config from Dataverse using the lookup ID
            var environment = await environmentService.GetByIdAsync(
                existingRequest.DataverseEnvironmentId.Value, cancellationToken);

            if (environment == null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The linked target environment was not found in Dataverse. It may have been deleted.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
            }

            if (!environment.IsActive)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: $"The target environment '{environment.Name}' is inactive. Please select an active environment.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
            }

            // FR-12: Validate license config JSON (throws JsonException if malformed)
            Services.Registration.LicenseConfig? licenseConfig = null;
            try
            {
                licenseConfig = environment.ParseLicenseConfig();
            }
            catch (System.Text.Json.JsonException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Configuration",
                    detail: $"Invalid license configuration for environment '{environment.Name}': {ex.Message}",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
            }

            // Map DataverseEnvironmentRecord → DemoEnvironmentConfig for provisioning service
            var envConfig = new DemoEnvironmentConfig
            {
                Name = environment.Name ?? "Unknown",
                DataverseUrl = environment.DataverseUrl ?? throw new InvalidOperationException("Environment is missing DataverseUrl"),
                BusinessUnitName = environment.BusinessUnitName ?? throw new InvalidOperationException("Environment is missing BusinessUnitName"),
                TeamName = environment.TeamName ?? throw new InvalidOperationException("Environment is missing TeamName"),
                SpeContainerId = environment.SpeContainerId ?? throw new InvalidOperationException("Environment is missing SpeContainerId"),
                AppId = environment.AppId,
                DefaultDemoDurationDays = environment.DefaultDurationDays ?? 14
            };

            logger.LogInformation(
                "Approving registration request {RequestId} for {Email}, Environment={Environment}, TraceId={TraceId}",
                id, existingRequest.Email, envConfig.Name, httpContext.TraceIdentifier);

            var provisionResult = await provisioningService.ProvisionDemoAccessAsync(
                existingRequest, envConfig, cancellationToken);

            logger.LogInformation(
                "Registration request {RequestId} approved and provisioned: Username={Username}, ExpiresOn={ExpirationDate}, TraceId={TraceId}",
                id, provisionResult.Username, provisionResult.ExpirationDate, httpContext.TraceIdentifier);

            return Results.Ok(provisionResult);
        }
        catch (DemoProvisioningException ex)
        {
            logger.LogError(ex,
                "Demo provisioning partially failed for request {RequestId}. Completed steps: [{CompletedSteps}], TraceId={TraceId}",
                id, string.Join(", ", ex.CompletedSteps), httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Provisioning Failed",
                detail: $"Demo provisioning failed: {ex.InnerException?.Message ?? ex.Message}",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = httpContext.TraceIdentifier,
                    ["completedSteps"] = ex.CompletedSteps,
                    ["failedAfterStep"] = ex.CompletedSteps.Count > 0 ? ex.CompletedSteps[^1] : "none",
                    ["entraUserId"] = ex.EntraUserId,
                    ["upn"] = ex.Upn
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to approve registration request {RequestId}, TraceId={TraceId}",
                id, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to provision demo access. Please try again.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }
    }

    /// <summary>
    /// Reject a pending registration request (admin only).
    /// POST /api/registration/requests/{id}/reject
    /// </summary>
    private static async Task<IResult> RejectRequest(
        Guid id,
        RejectRequestDto request,
        RegistrationDataverseService dataverseService,
        ILogger<RegistrationDataverseService> logger,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Rejection reason is required.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // Validate the request exists
        var existingRequest = await dataverseService.GetRequestByIdAsync(id, cancellationToken);
        if (existingRequest == null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"Registration request {id} not found.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // Validate status is Submitted
        if (existingRequest.Status != Services.Registration.RegistrationStatus.Submitted)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: $"Registration request cannot be rejected because its status is '{existingRequest.Status}'. Only requests with 'Submitted' status can be rejected.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        try
        {
            logger.LogInformation(
                "Rejecting registration request {RequestId} for {Email}, Reason={Reason}, TraceId={TraceId}",
                id, existingRequest.Email, request.Reason, httpContext.TraceIdentifier);

            await dataverseService.UpdateRequestStatusAsync(
                id,
                Services.Registration.RegistrationStatus.Rejected,
                new Dictionary<string, object?>
                {
                    ["sprk_rejectionreason"] = request.Reason,
                    ["sprk_reviewdate"] = DateTimeOffset.UtcNow
                },
                cancellationToken);

            logger.LogInformation(
                "Registration request {RequestId} rejected, TraceId={TraceId}",
                id, httpContext.TraceIdentifier);

            return Results.Ok(new { status = "Rejected", requestId = id });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reject registration request {RequestId}, TraceId={TraceId}",
                id, httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to reject registration request. Please try again.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }
    }

    #region Helpers

    /// <summary>
    /// Sends admin notification email as fire-and-forget (must not block the HTTP response).
    /// </summary>
    private static async Task SendAdminNotificationAsync(
        RegistrationEmailService emailService,
        DemoProvisioningOptions options,
        string trackingId,
        DemoRequestDto request,
        Guid recordId,
        ILogger logger,
        string traceIdentifier,
        string? dataverseUrl = null,
        string? appId = null)
    {
        try
        {
            // Build record URL: prefer explicit parameters, fall back to Environments config (legacy)
            string envUrl;
            string? envAppId;
            if (!string.IsNullOrEmpty(dataverseUrl))
            {
                envUrl = dataverseUrl;
                envAppId = appId;
            }
            else if (options.Environments.Length > 0)
            {
                var defaultEnv = options.Environments.FirstOrDefault(e => e.Name == options.DefaultEnvironment)
                    ?? options.Environments.First();
                envUrl = defaultEnv.DataverseUrl;
                envAppId = defaultEnv.AppId;
            }
            else
            {
                // No environment config available — use a generic URL
                envUrl = "https://spaarkedev1.crm.dynamics.com";
                envAppId = null;
            }
            var appIdParam = !string.IsNullOrEmpty(envAppId) ? $"appid={envAppId}&" : "";
            var recordUrl = $"{envUrl.TrimEnd('/')}/main.aspx?{appIdParam}pagetype=entityrecord&etn=sprk_registrationrequest&id={recordId}";

            await emailService.SendAdminNotificationAsync(
                adminEmails: options.AdminNotificationEmails,
                trackingId: trackingId,
                firstName: request.FirstName,
                lastName: request.LastName,
                email: request.Email,
                organization: request.Organization,
                useCase: request.UseCase,
                requestDate: DateTimeOffset.UtcNow,
                recordUrl: recordUrl);

            logger.LogInformation(
                "Admin notification sent for TrackingId={TrackingId}, TraceId={TraceId}",
                trackingId, traceIdentifier);
        }
        catch (Exception ex)
        {
            // Log but never throw — notification failure must not affect the caller.
            logger.LogError(
                ex,
                "Failed to send admin notification for TrackingId={TrackingId}, TraceId={TraceId} — {ErrorMessage}",
                trackingId, traceIdentifier, ex.Message);
        }
    }

    /// <summary>
    /// Sends acknowledgement email to applicant as fire-and-forget (must not block the HTTP response).
    /// </summary>
    private static async Task SendAcknowledgementEmailAsync(
        RegistrationEmailService emailService,
        string trackingId,
        DemoRequestDto request,
        ILogger logger,
        string traceIdentifier)
    {
        try
        {
            await emailService.SendAcknowledgementEmailAsync(
                recipientEmail: request.Email,
                firstName: request.FirstName,
                lastName: request.LastName,
                organization: request.Organization,
                trackingId: trackingId);

            logger.LogInformation(
                "Acknowledgement email sent for TrackingId={TrackingId}, Recipient={Recipient}, TraceId={TraceId}",
                trackingId, request.Email, traceIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to send acknowledgement email for TrackingId={TrackingId}, Recipient={Recipient}, TraceId={TraceId} — {ErrorMessage}",
                trackingId, request.Email, traceIdentifier, ex.Message);
        }
    }

    private static UseCaseOption? ParseUseCase(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.ToLowerInvariant() switch
        {
            "documentmanagement" or "document-management" or "document_management" => UseCaseOption.DocumentManagement,
            "aianalysis" or "ai-analysis" or "ai_analysis" => UseCaseOption.AiAnalysis,
            "financialintelligence" or "financial-intelligence" or "financial_intelligence" => UseCaseOption.FinancialIntelligence,
            "general" => UseCaseOption.General,
            _ => Enum.TryParse<UseCaseOption>(value, true, out var result) ? result : null
        };
    }

    private static ReferralSourceOption? ParseReferralSource(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.ToLowerInvariant() switch
        {
            "conference" => ReferralSourceOption.Conference,
            "website" => ReferralSourceOption.Website,
            "referral" => ReferralSourceOption.Referral,
            "search" => ReferralSourceOption.Search,
            "other" => ReferralSourceOption.Other,
            _ => Enum.TryParse<ReferralSourceOption>(value, true, out var result) ? result : null
        };
    }

    #endregion
}
