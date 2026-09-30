using Spaarke.Core.Auth;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding <see cref="OfficeVersionSaveAuthorizationFilter"/> to the Office save route.
/// </summary>
public static class OfficeVersionSaveAuthorizationFilterExtensions
{
    /// <summary>
    /// Requires <c>write</c> on the existing <c>sprk_document</c> that an FR-11 version save targets
    /// (<c>SaveRequest.Document.ExistingDocumentId</c>). Every other save passes through untouched.
    /// </summary>
    public static TBuilder AddOfficeVersionSaveAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var authService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new OfficeVersionSaveAuthorizationFilter(authService);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// ADR-008 gate for the FR-11 version save (spaarkeai-word-add-in-r1 task 023): a save that writes a new
/// version of an EXISTING document must hold <c>write</c> on that document's row.
/// </summary>
/// <remarks>
/// <para><b>Why a filter of its own, and why it decides nothing itself.</b> The authorization decision
/// already exists — <see cref="DocumentAuthorizationFilter"/> with operation <c>"write"</c>, the same filter
/// and operation as <c>PUT /api/v1/documents/{id}</c> and the checkout family
/// (<c>OperationAccessPolicy["write"] = AccessRights.Write</c>). What it cannot do is find the id: it reads
/// route values, and this route carries the id in its JSON body. So this filter only LOCATES the target and
/// binds it where that filter looks (<c>RouteValues["documentId"]</c>), then delegates — the same composition
/// task 012's <c>DocumentUrlIdentityFilter</c> uses. Extending <see cref="DocumentAuthorizationFilter"/> to
/// read bodies would change a filter shared by twenty routes; extending <see cref="EntityAccessFilter"/> would
/// give a filter that answers "may you file TO this record" a second, unrelated reason to change.</para>
/// <para><b>Scope.</b> Acts only when <see cref="OfficeService.IsVersionSave"/> is true — the SAME predicate
/// <c>OfficeService.SaveAsync</c> branches on, so the gate and the write can never disagree about what a
/// version save is. Email and Attachment saves never reach the delegate, whatever their body carries.</para>
/// <para><b>An unknown id is answered 403, like a denied one.</b> The access data source fails closed to
/// <c>AccessRights.None</c> for a row that does not exist, so "no such document" and "not yours" are the same
/// answer here — deliberately, as in <see cref="EntityAccessFilter"/>: separating them would be a
/// record-enumeration oracle.</para>
/// </remarks>
public sealed class OfficeVersionSaveAuthorizationFilter : IEndpointFilter
{
    /// <summary>The operation the version save is authorized as.</summary>
    public const string Operation = "write";

    private const string DocumentIdRouteKey = "documentId";

    private readonly AuthorizationService _authorizationService;

    public OfficeVersionSaveAuthorizationFilter(AuthorizationService authorizationService)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.Arguments.OfType<SaveRequest>().FirstOrDefault();
        if (request is null || !OfficeService.IsVersionSave(request))
        {
            return await next(context);
        }

        var routeValues = context.HttpContext.Request.RouteValues;

        // DocumentAuthorizationFilter reads "id" BEFORE "documentId". A route that already bound either would
        // have the WRONG document authorized, so refuse rather than guess (the task 012 guard).
        if (routeValues.ContainsKey("id") || routeValues.ContainsKey(DocumentIdRouteKey))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Authorization Misconfigured",
                detail: "The version-save authorization filter cannot run on a route that binds a document id.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        // ADR-044: bare-lowercase ("D" formatting of a typed Guid). This value becomes an OData key predicate
        // inside the access data source (sprk_documents({id})), so the canonical form is load-bearing here.
        routeValues[DocumentIdRouteKey] = request.Document!.ExistingDocumentId!.Value.ToString("D");

        return await new DocumentAuthorizationFilter(_authorizationService, Operation).InvokeAsync(context, next);
    }
}
