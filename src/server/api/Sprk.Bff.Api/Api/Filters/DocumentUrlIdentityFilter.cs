using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// The resolution half of <c>POST /api/documents/resolve-identity</c> (spaarkeai-word-add-in-r1 task 012, FR-01).
/// Runs BEFORE <see cref="DocumentAuthorizationFilter"/> on that route and turns the caller's document URL into the
/// <c>documentId</c> the authorization filter needs.
/// </summary>
/// <remarks>
/// <para><b>Why a filter.</b> Every other route in the <c>/api/documents</c> group names its document in the route,
/// so <see cref="DocumentAuthorizationFilter"/> can read the id before the handler runs. This route receives a URL;
/// the id does not exist until Graph and Dataverse have been asked. ADR-008 still binds — authorization is a filter,
/// never an inline check in a handler — so the id is resolved here and written into the route values under
/// <c>documentId</c>, the key <see cref="DocumentAuthorizationFilter"/> already reads. The unchanged authorization
/// filter, registered next, then decides <c>read</c> on it exactly as it does for <c>/{documentId}/open-links</c>:
/// one decision path for every document route, and no second authorization implementation to drift.</para>
/// <para><b>What a caller can learn.</b> Graph resolution runs as the caller (OBO), so it only ever resolves files
/// the caller can already reach. A caller who can reach the file but lacks Read on its <c>sprk_document</c> gets 403.
/// Container access is coarser than per-document rights (see the header of <c>FileAccessEndpoints</c>), so that
/// caller exists. They learn only that a Spaarke record tracks a file they can already open; the acceptance
/// criteria require the 403, and it carries no id, name or record. Document metadata enters a response only in the
/// handler, which runs only after the authorization filter has allowed the caller.</para>
/// <para>A no-identity answer short-circuits here with 200, so the authorization filter never sees a request that
/// has nothing to authorize.</para>
/// </remarks>
public sealed class DocumentUrlIdentityFilter : IEndpointFilter
{
    /// <summary><see cref="HttpContext.Items"/> key the handler reads the filter-resolved identity from.</summary>
    public const string ResolutionItemKey = "Sprk.DocumentUrlIdentity.Resolution";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // DocumentAuthorizationFilter reads `id` before `documentId`. This route binds neither; if it were ever mapped
        // under a template that did, authorization would silently check THAT id instead of the one resolved here.
        var routeValues = httpContext.Request.RouteValues;
        if (routeValues.ContainsKey("id") || routeValues.ContainsKey("documentId"))
        {
            throw new InvalidOperationException(
                "resolve-identity must not be mapped under a route that binds 'id' or 'documentId'.");
        }

        var request = context.Arguments.OfType<ResolveDocumentIdentityRequest>().FirstOrDefault();

        // 400 ProblemDetails for an empty, over-long or non-absolute URL (SdapProblemException → global handler).
        var documentUrl = DocumentUrlIdentityResolution.ParseDocumentUrl(request?.DocumentUrl);

        var services = httpContext.RequestServices;
        var result = await DocumentUrlIdentityResolution.ResolveAsync(
            documentUrl,
            httpContext,
            services.GetRequiredService<SpeFileStore>(),
            services.GetRequiredService<IGenericEntityService>(),
            services.GetRequiredService<ILogger<DocumentUrlIdentityFilter>>(),
            httpContext.RequestAborted);

        if (result.Identity is null)
            return TypedResults.Ok(DocumentIdentityResponse.NoIdentity(result.NoIdentityReason!));

        // The point at which the document id first exists. "D" format is bare lowercase — the ADR-044 canonical form.
        httpContext.Items[ResolutionItemKey] = result.Identity;
        routeValues["documentId"] = result.Identity.DocumentId.ToString("D");

        return await next(context);
    }
}
