using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// The resolution half of <c>POST /api/documents/resolve-email-identity</c> (spaarkeai-word-add-in-r1 task 120, UAT
/// round 12 O6). Runs BEFORE <see cref="DocumentAuthorizationFilter"/> on that route and turns the open email's keys
/// into the <c>documentId</c> of its saved <c>.eml</c> document — the email counterpart of
/// <see cref="DocumentUrlIdentityFilter"/>, with the same shape and the same reason for being a filter.
/// </summary>
/// <remarks>
/// <para><b>Why a filter.</b> ADR-008: authorization is a filter, never an inline handler check. The id does not exist
/// until Dataverse has been asked, so it is resolved here and written into the route values under <c>documentId</c> —
/// the key <see cref="DocumentAuthorizationFilter"/> reads — and the unchanged authorization filter, registered next,
/// decides <c>read</c> on it exactly as on <c>/{documentId}/identity</c>. One decision path for every document route.</para>
/// <para><b>What a caller can learn.</b> No saved copy → 200 <c>{ resolved: false, reason: "not_saved" }</c>. A saved
/// copy the caller may not read → 403 with no id, name or record (the handler, the only place metadata enters a
/// response, never runs). The 403 tells a caller who already holds the email in their mailbox only that it is saved
/// somewhere in Spaarke. Only the NEWEST copy is authorized — see
/// <see cref="DocumentUrlIdentityResolution.ResolveByEmailMessageIdAsync"/> for why an older readable copy is not
/// searched.</para>
/// </remarks>
public sealed class DocumentEmailIdentityFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // DocumentAuthorizationFilter reads `id` before `documentId`. This route binds neither; if it were ever mapped
        // under a template that did, authorization would silently check THAT id instead of the one resolved here.
        var routeValues = httpContext.Request.RouteValues;
        if (routeValues.ContainsKey("id") || routeValues.ContainsKey("documentId"))
        {
            throw new InvalidOperationException(
                "resolve-email-identity must not be mapped under a route that binds 'id' or 'documentId'.");
        }

        var request = context.Arguments.OfType<ResolveEmailIdentityRequest>().FirstOrDefault();

        // 400 ProblemDetails for a missing or over-long key (SdapProblemException → global handler); never echoed.
        var keys = DocumentUrlIdentityResolution.ParseEmailKeys(request?.InternetMessageId, request?.ExchangeItemId);

        var services = httpContext.RequestServices;
        var identity = await DocumentUrlIdentityResolution.ResolveByEmailMessageIdAsync(
            keys,
            services.GetRequiredService<IGenericEntityService>(),
            services.GetRequiredService<ILogger<DocumentEmailIdentityFilter>>(),
            httpContext.RequestAborted);

        if (identity is null)
            return TypedResults.Ok(DocumentIdentityResponse.NoIdentity(DocumentUrlIdentityResolution.ReasonNotSaved));

        // The same item key the URL route uses, so both handlers read the filter-resolved identity one way.
        httpContext.Items[DocumentUrlIdentityFilter.ResolutionItemKey] = identity;
        routeValues["documentId"] = identity.DocumentId.ToString("D");

        return await next(context);
    }
}
