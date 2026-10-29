using Sprk.Bff.Api.Services.Ontology;

namespace Sprk.Bff.Api.Api.Ontology;

/// <summary>
/// The rule-authoring routes of the Ontology admin group (ontology platform R1 task 101, D-22 slice b, FR-58), mapped onto the
/// <c>/api/v1/ontology/admin</c> group <see cref="OntologyAdminEndpoints"/> creates (authorization and rate limit inherited):
/// <list type="bullet">
/// <item><c>GET /authoring/catalog</c> — the authorable condition catalog, generated from the compiler (#46).</item>
/// <item><c>POST /authoring/dry-run</c> — evaluate a draft over current data as the evaluator, rows filtered by the caller (#39).</item>
/// <item><c>POST /authoring/validate</c> — compile a draft only (task 102a): field errors and the template-eligible fields; nothing is
/// written or evaluated. Gated exactly as the dry run.</item>
/// <item><c>POST /authoring/policies</c> — save a new rule, Off (FR-10), with its first version.</item>
/// <item><c>POST /authoring/policies/{policyId}/versions</c> — publish a new version (supersede, re-raise, stamp; D-13, D-14).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Authorization (D-22, NFR-10, D-29).</b> Every handler's only I/O is one <see cref="OntologyAdminAuthoringService"/> call,
/// whose every read the caller acts on and every write runs AS THE CALLER through <c>IDataverseUserClient</c>: Dataverse enforces the
/// Spaarke Ontology Administrator role on each write (a Console User is refused, 403) and the audit names the human. The capability
/// probe's answer is display only and is never read here. The dry run writes nothing, so Dataverse checks no role on it: the service
/// asks, as the caller and fresh on every request, whether the caller holds <c>prvCreatesprk_PolicyVersion</c>, and refuses (403
/// <c>ontology.admin.forbidden</c>, nothing evaluated) when not (F1-1). An unresolved caller is the single 403; a policy that is
/// absent, unreadable or not an Ontology policy is the uniform 404 with zero writes.</para>
/// <para><b>Placement (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> In the BFF: the admin writes must run as the
/// caller through the OBO client the BFF owns, and the dry run must run the evaluator, which lives here. One scoped service and its
/// evaluator seam, registered unconditionally in <c>SignalsModule</c> beside the services they wrap (bff-extensions.md F.1).</para>
/// </remarks>
public static class OntologyAdminAuthoringEndpoints
{
    /// <summary>Maps the authoring routes onto the Ontology admin group.</summary>
    public static void MapOntologyAdminAuthoringEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/authoring/catalog", GetCatalogAsync)
            .WithName("GetOntologyAuthoringCatalog")
            .WithSummary("The authorable rule conditions, generated from the predicate compiler")
            .Produces<OntologyAuthoringCatalog>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/authoring/dry-run", DryRunAsync)
            .WithName("DryRunOntologyRule")
            .WithSummary("Runs a draft rule over current data as the evaluator; writes nothing; rows filtered by the caller's access")
            .Produces<OntologyDryRunResult>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/authoring/validate", ValidateAsync)
            .WithName("ValidateOntologyRule")
            .WithSummary("Compiles a draft rule only: field errors and the fields its message may name; writes and evaluates nothing")
            .Produces<OntologyRuleValidation>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/authoring/policies", SavePolicyAsync)
            .WithName("SaveOntologyPolicy")
            .WithSummary("Saves a new rule, Off, with its first version, as the caller")
            .Produces<OntologyPolicySaved>(201)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/authoring/policies/{policyId:guid}/versions", PublishVersionAsync)
            .WithName("PublishOntologyPolicyVersion")
            .WithSummary("Publishes a new version of a rule as the caller: supersedes open items and re-raises those that still hold")
            .Produces<OntologyVersionPublished>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> GetCatalogAsync(
        HttpContext httpContext, OntologyAdminAuthoringService authoring, CancellationToken ct) =>
        ToResult(httpContext, await authoring.GetCatalogAsync(ct).ConfigureAwait(false));

    internal static async Task<IResult> DryRunAsync(
        HttpContext httpContext, OntologyDryRunRequest request, OntologyAdminAuthoringService authoring, CancellationToken ct) =>
        ToResult(httpContext, await authoring.DryRunAsync(httpContext.User, request, ct).ConfigureAwait(false));

    internal static async Task<IResult> ValidateAsync(
        HttpContext httpContext, OntologyValidateRequest request, OntologyAdminAuthoringService authoring, CancellationToken ct) =>
        ToResult(httpContext, await authoring.ValidateAsync(request, ct).ConfigureAwait(false));

    internal static async Task<IResult> SavePolicyAsync(
        HttpContext httpContext, OntologyNewPolicyRequest request, OntologyAdminAuthoringService authoring, CancellationToken ct)
    {
        var result = await authoring.SavePolicyAsync(httpContext.User, request, ct).ConfigureAwait(false);
        return result is { Outcome: OntologyAdminAuthoringOutcome.Created, Value: { } saved }
            ? Results.Created($"/api/v1/ontology/admin/authoring/policies/{saved.PolicyId:D}", saved)
            : ToResult(httpContext, result);
    }

    internal static async Task<IResult> PublishVersionAsync(
        HttpContext httpContext, Guid policyId, OntologyPublishRequest request, OntologyAdminAuthoringService authoring, CancellationToken ct) =>
        ToResult(httpContext, await authoring.PublishVersionAsync(httpContext.User, policyId, request, ct).ConfigureAwait(false));

    private static IResult ToResult<T>(HttpContext httpContext, OntologyAdminAuthoringResult<T> result)
        where T : class =>
        result.Outcome switch
        {
            OntologyAdminAuthoringOutcome.Ok or OntologyAdminAuthoringOutcome.Created when result.Value is not null => Results.Ok(result.Value),
            OntologyAdminAuthoringOutcome.Invalid => Results.ValidationProblem(
                result.Errors ?? new Dictionary<string, string[]>(),
                title: "The rule cannot be saved",
                detail: "Nothing was written. Fix the fields named below.",
                // Task 102a: a refusal with a stable code (ontology.admin.column_not_authorable) carries it, as the validate route does.
                extensions: result.ReasonCode is { } code ? new Dictionary<string, object?> { ["reasonCode"] = code } : null),
            OntologyAdminAuthoringOutcome.CallerUnresolved => Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden",
                "The caller could not be resolved to a Dataverse user.", result.ReasonCode),
            OntologyAdminAuthoringOutcome.Refused => Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden",
                "Dataverse refused this for your account. Nothing was written.", result.ReasonCode),
            OntologyAdminAuthoringOutcome.Forbidden => Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden",
                "Your account cannot author rules. Nothing was run.", result.ReasonCode),
            OntologyAdminAuthoringOutcome.NotFound => Problem(httpContext, StatusCodes.Status404NotFound, "Not Found",
                "The rule was not found.", result.ReasonCode),
            OntologyAdminAuthoringOutcome.Conflict => Problem(httpContext, StatusCodes.Status409Conflict, "Conflict",
                ConflictDetail(result.ReasonCode), result.ReasonCode),
            _ => Problem(httpContext, StatusCodes.Status503ServiceUnavailable, "Service Unavailable",
                result.PolicyId is null
                    ? "The rule service is unavailable. Try again."
                    : "The rule was saved Off, but its first version could not be made current. Publish a version of it to finish.",
                result.ReasonCode ?? OntologyAdminAuthoringService.ReasonUnavailable, result.PolicyId),
        };

    private static string ConflictDetail(string? reasonCode) => reasonCode switch
    {
        OntologyAdminAuthoringService.ReasonDryRunRequired => "Run Test on this exact rule before saving it; any edit needs a new test run.",
        OntologyAdminAuthoringService.ReasonPolicyCodeTaken => "Another rule already uses this code.",
        OntologyAdminAuthoringService.ReasonPolicyRetired => "The rule is retired; a retired rule cannot be published.",
        OntologyAdminAuthoringService.ReasonRuleHeldSecure => OntologyAdminAuthoringService.RuleHeldSecureMessage,
        _ => "The rule changed while this was being saved. Nothing new is in force; reload and try again.",
    };

    private static IResult Problem(HttpContext httpContext, int status, string title, string detail, string? reasonCode, Guid? policyId = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["reasonCode"] = reasonCode,
            ["correlationId"] = httpContext.TraceIdentifier,
        };
        if (policyId is { } id)
        {
            extensions["policyId"] = id; // K6: the policy a failed save left Off without a current version
        }

        return Results.Problem(title: title, detail: detail, statusCode: status, extensions: extensions);
    }
}
