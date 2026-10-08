// -----------------------------------------------------------------------------
// ControlPlaneProblems.cs
//
// The one way the L2 /api/runs surface builds an error response (ADR-019):
// RFC 7807 ProblemDetails carrying a stable `errorCode` and the request's
// `correlationId`. `errorCode` is a required argument, so an endpoint cannot
// return a failure the /provision-environment skill can only tell apart by
// parsing `detail` text.
//
// Codes are kebab-case like the handler rejection codes (H13RejectionCodes etc.)
// and are part of the API contract: renaming one breaks callers that branch on it.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Api;

/// <summary>Stable <c>errorCode</c> values returned in L2 ProblemDetails responses.</summary>
internal static class ControlPlaneErrorCodes
{
    // --- POST /api/runs: request shape -------------------------------------------------
    public const string RequestBodyRequired = "request-body-required";
    public const string CustomerIdRequired = "customer-id-required";
    public const string CustomerIdNonStandard = "customer-id-nonstandard";
    public const string CustomerIdReserved = "customer-id-reserved";
    public const string EnvironmentIdRequired = "environment-id-required";
    public const string TenancyModelRequired = "tenancy-model-required";
    public const string ProfileRequired = "profile-required";
    public const string TenancyProfileInvalid = "tenancy-profile-invalid";

    // --- POST /api/runs: nonSecretParameters (task 245a run-context contract) -------------
    public const string IntakeUnknownKey = "intake-unknown-key";
    public const string IntakeInvalidEnvironmentName = "intake-invalid-environment-name";
    public const string IntakeInvalidSolutionPackageType = "intake-invalid-solution-package-type"; // T218b
    public const string TenantIdRequired = "tenant-id-required";
    public const string SubscriptionIdRequired = "subscription-id-required";
    // T228: the operator-created Dataverse environment, and the container type (G19) — required for every model.
    public const string DataverseEnvUrlInvalid = "dataverse-env-url-invalid";
    public const string ContainerTypeIdRequired = "container-type-id-required";
    public const string CommunicationDefaultMailboxInvalid = "intake-communication-default-mailbox-invalid";
    // Task 245c: an intake value a HANDLER would refuse is refused with that handler's own rejection code —
    // H11Rejections (UserProvisioningIntake) for identityPreset / usersJson, H14aRejections.MissingPolicyScopeGroupId,
    // H14bRejections.NoWebhookTargetsConfigured — so a caller sees one code for one rule wherever it fires.

    // --- POST /api/runs: registry + concurrency ---------------------------------------
    public const string RegistryCustomerMismatch = "registry-customer-mismatch";
    public const string RegistrySetupStatusNotInProgress = "registry-setup-status-not-in-progress";
    public const string CustomerRunInFlight = "customer-run-in-flight";
    public const string RunGuardUnavailable = "run-guard-unavailable";
    public const string RunIdCollision = "run-id-collision";

    // --- /api/runs/{id}/... ------------------------------------------------------------
    public const string RunIdRequired = "run-id-required";
    public const string CustomerIdQueryRequired = "customer-id-query-required";
    public const string RunNotFound = "run-not-found";
    public const string GateIdRequired = "gate-id-required";
    public const string ReasonRequired = "reason-required";
    public const string RunNotQuarantined = "run-not-quarantined";
    public const string RunConcurrentlyModified = "run-concurrently-modified";
    public const string PhaseIdRequired = "phase-id-required";
    public const string PhaseNotCompleted = "phase-not-completed";
}

/// <summary>Builds L2 ProblemDetails responses (ADR-019).</summary>
internal static class ControlPlaneProblems
{
    /// <summary>
    /// A ProblemDetails result with <c>errorCode</c> and <c>correlationId</c> extensions.
    /// <paramref name="extensions"/> adds endpoint-specific members (e.g. the I5 guard's <c>winningRunId</c>).
    /// </summary>
    public static IResult Create(
        HttpContext httpContext,
        int statusCode,
        string errorCode,
        string detail,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        var members = new Dictionary<string, object?>
        {
            ["errorCode"] = errorCode,
            ["correlationId"] = httpContext.TraceIdentifier,
        };
        if (extensions is not null)
        {
            foreach (var (name, value) in extensions)
            {
                members[name] = value;
            }
        }

        var (title, type) = statusCode switch
        {
            StatusCodes.Status400BadRequest => ("Bad Request", "https://tools.ietf.org/html/rfc7231#section-6.5.1"),
            StatusCodes.Status404NotFound => ("Not Found", "https://tools.ietf.org/html/rfc7231#section-6.5.4"),
            StatusCodes.Status409Conflict => ("Conflict", "https://tools.ietf.org/html/rfc7231#section-6.5.8"),
            StatusCodes.Status502BadGateway => ("Bad Gateway", "https://tools.ietf.org/html/rfc7231#section-6.6.3"),
            _ => ((string?)null, (string?)null),
        };

        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            type: type,
            extensions: members);
    }

    public static IResult BadRequest(HttpContext httpContext, string errorCode, string detail) =>
        Create(httpContext, StatusCodes.Status400BadRequest, errorCode, detail);

    public static IResult NotFound(HttpContext httpContext, string errorCode, string detail) =>
        Create(httpContext, StatusCodes.Status404NotFound, errorCode, detail);

    public static IResult Conflict(HttpContext httpContext, string errorCode, string detail) =>
        Create(httpContext, StatusCodes.Status409Conflict, errorCode, detail);
}
