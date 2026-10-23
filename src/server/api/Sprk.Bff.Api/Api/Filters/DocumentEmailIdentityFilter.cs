using Microsoft.AspNetCore.Mvc;
using Spaarke.Core.Auth;
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
/// copy the caller may not read (no right, or hidden by the external document narrowing) is answered EXACTLY like no
/// match — the same status and body, no reason code, id or record — because since task 132 the match also covers
/// captured and Spaarke-sent mail, and the keys are whatever the caller sends: any other answer for a match the caller
/// cannot read would tell them the thing exists. The handler, the only place metadata enters a response, never runs on
/// a refusal. Only the NEWEST copy is authorized — see
/// <see cref="DocumentUrlIdentityResolution.ResolveByEmailMessageIdAsync"/> for why an older readable copy is not
/// searched.</para>
/// <para><b>Known limits.</b> (a) Only a 403 DECISION is folded into "not saved". A 401/400/500/503 passes through, so
/// an outage the filter chain reports as a 500 "Authorization Error" (the authorization call itself throwing) or a 503
/// never reads as "not saved" (task 120), and a caller cannot cause one. <c>AuthorizationService</c> catches the faults
/// of its own access source and fails closed with a 403 whose reason code is
/// <see cref="AuthorizationResult.SystemFailureReasonCode"/>; that refusal is not a decision, so it is answered as the
/// route's 503 <c>identity_resolution_unavailable</c> (the answer Dataverse failing gives) and never as "not saved". A
/// caller cannot cause an outage, so the per-key signal during one is accepted (as with the 500 pass-through). The one
/// fault that still folds into "not saved" is a fault of the external-narrowing call: it is refused with the same
/// reason code as "hidden", so it cannot be told from a hidden match (by design — no code distinguishes them). The 503
/// assumes a fault is an outage: if the access evaluation of one particular matched record throws every time (broken
/// lineage or data on that row), its Message-ID answers 503 on every probe while no match answers "not saved". That
/// per-record signal needs a pre-existing broken row and reveals only that such a row exists; it is accepted (UAC-r2).
/// (b) The match path does extra reads (the authorization and the narrowing), so the response TIME
/// differs slightly from a no-match answer; the body and status do not.</para>
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
            return NotSaved();

        // The same item key the URL route uses, so both handlers read the filter-resolved identity one way.
        httpContext.Items[DocumentUrlIdentityFilter.ResolutionItemKey] = identity;
        routeValues["documentId"] = identity.DocumentId.ToString("D");

        var result = await next(context);

        // A match the caller may not read is answered exactly like no match. DocumentAuthorizationFilter refuses with 403
        // for both "no right" and "hidden by the external narrowing" (it returns before the handler, so no metadata was
        // produced); passing either 403 through would let a caller tell "saved somewhere I cannot read" from "not saved".
        // Only a refusal that IS a decision is folded in. 401/400/500/503 are not (see the remarks on the known limit).
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status403Forbidden })
        {
            // AuthorizationService answers a FAULT of its access source with a fail-closed 403 carrying this reason code.
            // That is an outage, not a decision: it is answered as the route's own 503 "could not be determined" (the
            // answer Dataverse failing gives), never as "not saved" (task 120).
            if (IsSystemFailure(result))
                throw DocumentUrlIdentityResolution.EmailLookupUnavailable();

            return NotSaved();
        }

        return result;
    }

    private static bool IsSystemFailure(object? result)
    {
        // ProblemHttpResult implements both value-result interfaces; read whichever the result offers.
        var problem = result switch
        {
            IValueHttpResult<ProblemDetails> typed => typed.Value,
            IValueHttpResult { Value: ProblemDetails untyped } => untyped,
            _ => null,
        };
        if (problem is null)
            return false;

        if (!problem.Extensions.TryGetValue("reasonCode", out var value))
            return false;

        var reasonCode = value switch
        {
            string s => s,
            System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e => e.GetString(),
            _ => null,
        };
        return string.Equals(reasonCode, AuthorizationResult.SystemFailureReasonCode, StringComparison.Ordinal);
    }

    // One value for both the no-match branch and the refused-match branch, so the two cannot drift apart.
    private static IResult NotSaved()
        => TypedResults.Ok(DocumentIdentityResponse.NoIdentity(DocumentUrlIdentityResolution.ReasonNotSaved));
}
