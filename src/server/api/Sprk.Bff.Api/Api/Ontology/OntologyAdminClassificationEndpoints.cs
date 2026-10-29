using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Ontology;

namespace Sprk.Bff.Api.Api.Ontology;

/// <summary>
/// The Ontology admin Classification routes, <c>/api/v1/ontology/admin/classification</c> (ontology platform R1 task 103; D-22, FR-59,
/// D-49): the triage categories with their guidance, On/Off, the rules that read each, 30-day volume and recall against the floor, and
/// the three writes (create a category, edit it, turn it On/Off).
/// </summary>
/// <remarks>
/// <para><b>Every route runs as the caller (D-22).</b> Each handler passes the caller-identity <see cref="IDataverseUserClient"/> to
/// <see cref="OntologyAdminClassificationService"/>, which makes every read and write through it, so Dataverse enforces the Spaarke
/// Ontology Administrator role on each call and the audit names the human. A Console User is refused by Dataverse on every write (403
/// <c>ontology.admin.classification.not_permitted</c>). Nothing here reads the capability probe's answer: it only decides whether the
/// Console shows the tab (<c>OntologyAdminCallerBoundaryGuardTests</c>, rules A and B).</para>
/// <para><b>Authorization (NFR-10, D-29).</b> <c>RequireAuthorization</c> on the group (no token: 401). A caller the BFF cannot name to
/// Dataverse (no user context, OBO not configured or refused, Dataverse 401) is #1312's single 403
/// (<c>sdap.access.deny.caller_unresolved</c>). The category id in the path is a row of an organisation-owned table the caller reads as
/// themselves: a row the caller cannot read is the uniform 404. Census: <c>RouteLevelGate</c> with one HandlerDecision per route.</para>
/// <para><b>Why its own group.</b> The URL prefix is the admin group's, but the group is declared here (not received from
/// <c>OntologyAdminEndpoints</c>) so the census binds these routes in this file; a group passed between files must be a pinned aggregator
/// in the uac-r2-owned ledger.</para>
/// <para><b>Placement and justification (CLAUDE.md sections 10 and 11; .claude/constraints/bff-extensions.md).</b> <i>In the BFF:</i> the
/// reads and writes need Dataverse as the caller through the OBO client the BFF owns, and the rename check must find the policy versions
/// that read a category, which no browser read of <c>sprk_policyversion</c> can do under the caller's access. <i>Existing:</i> nothing
/// overlaps; the categories are authored in Dataverse directly (task 004) and the only readers are the classifier
/// (<c>LookupChoicesResolver</c>, app-identity, read-only). <i>Extension:</i> there is no admin surface for categories to extend; the
/// capability route (task 104) is a probe and the Rules routes (tasks 100/101) own policies. <i>Cost of doing nothing:</i> criterion 8
/// (a new category with guidance changes classifier behaviour with no deployment) can only be exercised by editing rows outside the
/// Console, and nothing stops a rename that silently changes a rule's decoding enum. It adds no DI registration, package or background
/// work.</para>
/// </remarks>
public static class OntologyAdminClassificationEndpoints
{
    internal const string NotPermittedReasonCode = "ontology.admin.classification.not_permitted";
    internal const string InvalidReasonCode = "ontology.admin.classification.invalid";
    internal const string NameTakenReasonCode = "ontology.admin.classification.name_taken";
    internal const string RenameBlockedReasonCode = "ontology.admin.classification.rename_blocked";
    internal const string RenameUnverifiableReasonCode = "ontology.admin.classification.rename_unverifiable";
    internal const string RenameRoutedReasonCode = "ontology.admin.classification.rename_routed";
    internal const string StaleReasonCode = "ontology.admin.classification.stale";
    internal const string UnavailableReasonCode = "ontology.admin.classification.unavailable";

    private const string Prefix = "/api/v1/ontology/admin/classification";

    public static void MapOntologyAdminClassificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Prefix)
            .WithTags("Ontology admin")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapGet("/categories", ListCategoriesAsync)
            .WithName("ListOntologyAdminTriageCategories")
            .WithSummary("The triage categories with guidance, On/Off, the rules that read each, 30-day volume and recall against the floor")
            .WithDescription("Read as the caller. 'readBy' lists every policy version (current, draft or earlier, any policy the caller can read) "
                + "whose rule body names the category; 'volume30Days' counts the communications the caller can read; recall is the stored "
                + "measurement judged against the 80 percent / 50 item floor. No classifier confidence is returned: none exists. "
                + "Unresolved caller -> 403; 503 when the categories cannot be read.")
            .Produces<ClassificationList>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/categories", CreateCategoryAsync)
            .WithName("CreateOntologyAdminTriageCategory")
            .WithSummary("Create a triage category (always saved Off)")
            .WithDescription("Writes as the caller. The body has no 'enabled' member: a new category saves Off and is turned On with the "
                + "On/Off route. 400 on a bad name or guidance, 409 when the name is taken, 403 when Dataverse refuses the caller.")
            .Produces<ClassificationRow>(201)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPatch("/categories/{categoryId:guid}", UpdateCategoryAsync)
            .WithName("UpdateOntologyAdminTriageCategory")
            .WithSummary("Edit a triage category's name and/or guidance")
            .WithDescription("Writes as the caller, conditional on the row version. A rename is refused (409) while any policy version reads "
                + "the category (by id or by name), naming the rule; 409 rename_unverifiable unless the caller's read of the policies and of their versions is "
                + "unconditional and covers every business unit (a role copy at Global depth, or at Deep on the root business unit); 409 rename_routed while the routing "
                + "configuration is keyed by the name; if the rules cannot be read the rename is refused (503). 409 also for a taken name or "
                + "a row that changed since the form loaded it.")
            .Produces<ClassificationRow>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/categories/{categoryId:guid}/enabled", SetCategoryEnabledAsync)
            .WithName("SetOntologyAdminTriageCategoryEnabled")
            .WithSummary("Turn a triage category On or Off")
            .WithDescription("Writes as the caller, conditional on the row version. On: the classifier may choose the category on the next "
                + "enrichment (409 name_taken when another active category has the same name). Off: it is no longer offered.")
            .Produces<ClassificationRow>(200)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    internal static async Task<IResult> ListCategoriesAsync(
        HttpContext httpContext, IDataverseUserClient user, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var result = await OntologyAdminClassificationService.ListAsync(user, LoggerFor(loggerFactory), ct).ConfigureAwait(false);
        return ToResult(httpContext, result, list => Results.Ok(list));
    }

    internal static async Task<IResult> CreateCategoryAsync(
        HttpContext httpContext,
        CreateClassificationCategoryRequest? request,
        IDataverseUserClient user,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var result = await OntologyAdminClassificationService.CreateAsync(user, request, LoggerFor(loggerFactory), ct).ConfigureAwait(false);
        return ToResult(httpContext, result, row => Results.Created($"{Prefix}/categories/{row.Id:D}", row));
    }

    internal static async Task<IResult> UpdateCategoryAsync(
        Guid categoryId,
        HttpContext httpContext,
        UpdateClassificationCategoryRequest? request,
        IDataverseUserClient user,
        IOptionsMonitor<CategoryRoutingOptions> routing,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var result = await OntologyAdminClassificationService.UpdateAsync(user, categoryId, request, routing, LoggerFor(loggerFactory), ct).ConfigureAwait(false);
        return ToResult(httpContext, result, row => Results.Ok(row));
    }

    internal static async Task<IResult> SetCategoryEnabledAsync(
        Guid categoryId,
        HttpContext httpContext,
        SetClassificationCategoryEnabledRequest? request,
        IDataverseUserClient user,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var result = await OntologyAdminClassificationService.SetEnabledAsync(user, categoryId, request, LoggerFor(loggerFactory), ct).ConfigureAwait(false);
        return ToResult(httpContext, result, row => Results.Ok(row));
    }

    private static ILogger LoggerFor(ILoggerFactory loggerFactory) => loggerFactory.CreateLogger(typeof(OntologyAdminClassificationEndpoints));

    private static IResult ToResult<T>(HttpContext httpContext, ClassificationOperationResult<T> result, Func<T, IResult> ok)
    {
        switch (result.Outcome)
        {
            case ClassificationOutcome.Ok:
            case ClassificationOutcome.Created:
                return ok(result.Value!);

            case ClassificationOutcome.CallerUnresolved:
                return Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", "The caller could not be resolved to a Dataverse user.",
                    DecisionPlanEndpoints.CallerUnresolvedReasonCode);

            case ClassificationOutcome.Forbidden:
                return Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden",
                    "You do not have permission to change triage categories. This needs the Spaarke Ontology Administrator role.",
                    NotPermittedReasonCode);

            case ClassificationOutcome.NotFound:
                return ProblemDetailsHelper.UniformRecordNotFound(httpContext);

            case ClassificationOutcome.Invalid:
                return Results.ValidationProblem(
                    result.Errors!.ToDictionary(e => e.Key, e => e.Value),
                    extensions: new Dictionary<string, object?>
                    {
                        ["reasonCode"] = InvalidReasonCode,
                        ["correlationId"] = httpContext.TraceIdentifier,
                    });

            case ClassificationOutcome.NameTaken:
                return Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", result.Detail ?? "The name is taken.", NameTakenReasonCode);

            case ClassificationOutcome.RenameBlocked:
                return Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", result.Detail ?? "A rule reads this category.",
                    RenameBlockedReasonCode, new Dictionary<string, object?> { ["blockedBy"] = result.Blockers });

            case ClassificationOutcome.RenameUnverifiable:
                return Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", result.Detail ?? "The rename cannot be checked from your business unit.",
                    RenameUnverifiableReasonCode);

            case ClassificationOutcome.RenameRouted:
                return Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", result.Detail ?? "The routing configuration names this category.",
                    RenameRoutedReasonCode);

            case ClassificationOutcome.Stale:
                return Problem(httpContext, StatusCodes.Status409Conflict, "Conflict", result.Detail ?? "The category changed.", StaleReasonCode);

            default:
                return Problem(httpContext, StatusCodes.Status503ServiceUnavailable, "Service Unavailable",
                    result.Detail ?? "The triage categories could not be read or saved. Try again.", UnavailableReasonCode);
        }
    }

    private static IResult Problem(
        HttpContext httpContext, int status, string title, string detail, string reasonCode, Dictionary<string, object?>? more = null)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["reasonCode"] = reasonCode,
            ["correlationId"] = httpContext.TraceIdentifier,
        };
        if (more is not null)
        {
            foreach (var (key, value) in more)
            {
                extensions[key] = value;
            }
        }

        return Results.Problem(title: title, detail: detail, statusCode: status, extensions: extensions);
    }
}
