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
/// <para><b>Task 143.</b> Both <c>not_saved</c> branches also carry <c>savedAttachmentNames</c> when the email has standalone
/// attachment documents (saved without the email) that the caller may read — the same lookup and checks in both branches, and
/// never the children of a refused <c>.eml</c>, so the two answers still cannot be told apart. Without any, the answer is
/// unchanged.</para>
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
            return await NotSavedAsync(keys, matchedEmlId: null, httpContext);

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

            return await NotSavedAsync(keys, identity.DocumentId, httpContext);
        }

        return result;
    }

    /// <summary>
    /// The <c>not_saved</c> answer, for both the no-match and the refused-match branch, built by ONE call so the two cannot
    /// drift apart. Task 143: it carries <c>savedAttachmentNames</c> — the email's standalone attachment documents the caller
    /// may read (<see cref="ReadSavedAttachmentNamesAsync"/>, never the children of a refused <c>.eml</c>) — only when there is
    /// at least one, so the answer without any is byte for byte today's, and a read or check that did not decide omits it.
    /// </summary>
    private static async Task<IResult> NotSavedAsync(
        DocumentUrlIdentityResolution.EmailKeys keys, Guid? matchedEmlId, HttpContext httpContext)
    {
        var names = await ReadSavedAttachmentNamesAsync(
            savedEmlId: null, excludedDocumentId: matchedEmlId, keys, httpContext,
            httpContext.RequestServices.GetRequiredService<ILogger<DocumentEmailIdentityFilter>>(), httpContext.RequestAborted);
        var answer = DocumentIdentityResponse.NoIdentity(DocumentUrlIdentityResolution.ReasonNotSaved);
        return TypedResults.Ok(names is { Count: > 0 } ? answer with { SavedAttachmentNames = names } : answer);
    }

    /// <summary>
    /// Task 141, widened by task 143: the names (<see cref="Sprk.Bff.Api.Services.Office.EmailAttachmentAddPlanner.SavedNamesOf"/>,
    /// the planner's one name rule) of the email's saved attachments the CALLER may read — the children of the saved
    /// <c>.eml</c> <paramref name="savedEmlId"/> (when the caller may read it), and the email's standalone attachment documents
    /// (<see cref="DocumentUrlIdentityResolution.FindStandaloneEmailAttachmentsAsync"/>) — or <see langword="null"/> (the field is
    /// omitted) when they cannot be read.
    /// </summary>
    /// <param name="savedEmlId">The saved <c>.eml</c> the caller may read, whose children are listed; <see langword="null"/> on
    /// <c>not_saved</c>.</param>
    /// <param name="excludedDocumentId">A document never listed: the matched <c>.eml</c> on a refused match (an archive is never a
    /// standalone attachment; this only keeps that true whatever the lookup answers).</param>
    /// <remarks>
    /// ADR-008 Amendment A1: these documents are found by this lookup (the request names only the email's keys), so each one is
    /// checked here, as the caller, with the check the route's filter makes on the <c>.eml</c>: <c>read</c> through
    /// <see cref="InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync"/> (the root grants, then the external document
    /// narrowing). Task 141 review: one call over all of them first, which allows only when every one is allowed; only when it
    /// denies is each checked on its own. A document the caller may not read is left out. Only the lookups run before the check;
    /// no content is read at all. Fail closed: a read that throws, or a check (of all, or of one) that answers the system-failure
    /// deny (<see cref="AuthorizationResult.SystemFailureReasonCode"/> — the authorization service answers a fault that way and
    /// does not throw), omits the whole field, so the pane never claims an attachment is unsaved on a guess and keeps today's
    /// behaviour.
    /// </remarks>
    internal static async Task<IReadOnlyList<string>?> ReadSavedAttachmentNamesAsync(
        Guid? savedEmlId,
        Guid? excludedDocumentId,
        DocumentUrlIdentityResolution.EmailKeys keys,
        HttpContext context,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var userId = Sprk.Bff.Api.Infrastructure.Authentication.CallerResolution.ResolveObjectId(context.User);
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            var services = context.RequestServices;
            var found = new List<DocumentEntity>();
            if (savedEmlId is { } emlId)
            {
                found.AddRange(await services.GetRequiredService<IDocumentDataverseService>().GetDocumentsByParentAsync(emlId, ct));
            }

            found.AddRange(await DocumentUrlIdentityResolution.FindStandaloneEmailAttachmentsAsync(
                keys.InternetMessageId, services.GetRequiredService<IGenericEntityService>(), ct));

            var candidates = found
                .Select(document => (Document: document, Id: Guid.TryParse(document.Id, out var id) ? id : Guid.Empty))
                .Where(c => c.Id != Guid.Empty && c.Id != savedEmlId && c.Id != excludedDocumentId)
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .ToList();
            if (candidates.Count == 0)
            {
                return Array.Empty<string>();
            }

            var authorizationService = services.GetRequiredService<IAuthorizationService>();
            var token = Sprk.Bff.Api.Infrastructure.Auth.TokenHelper.ExtractBearerTokenOrNull(context);

            // Task 141 review (F6): ONE check over every document first. AuthorizeDocumentsAsync allows a list only when it allows
            // every id in it (the root grants per id, then one narrowing call over all), so an allow names them all. Only a deny is
            // checked again one document at a time, to leave out exactly the ones the caller may not read.
            var all = await InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync(
                context, authorizationService, userId, candidates.Select(c => c.Id).ToList(), "read", token,
                "email-identity:saved-attachments", logger, ct);
            if (all.IsAllowed)
            {
                return NamesOf(candidates.Select(c => c.Document));
            }

            if (IsSystemFailure(all))
            {
                logger.LogWarning(
                    "Email identity: the access check of the saved attachments of the email faulted; the field is omitted.");
                return null;
            }

            var readable = new List<DocumentEntity>();
            foreach (var (document, documentId) in candidates)
            {
                var decision = await InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync(
                    context, authorizationService, userId, new[] { documentId }, "read", token,
                    "email-identity:saved-attachments", logger, ct);
                if (decision.IsAllowed)
                {
                    readable.Add(document);
                }
                else if (IsSystemFailure(decision))
                {
                    // The check did not decide: the service answers a fault as a deny with this reason code, never a throw.
                    // Dropping the document would make the pane show it as unsaved on a guess, so say nothing.
                    logger.LogWarning(
                        "Email identity: the access check of saved attachment {AttachmentDocumentId} faulted; the field is omitted.",
                        documentId);
                    return null;
                }
            }

            return NamesOf(readable);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // The message id is not logged: it identifies a message in the caller's mailbox.
            logger.LogWarning(ex,
                "Email identity: the saved attachments of the email (saved .eml {DocumentId}) could not be read; the field is omitted.",
                savedEmlId);
            return null;
        }

        static IReadOnlyList<string> NamesOf(IEnumerable<DocumentEntity> readableDocuments) =>
            readableDocuments
                .SelectMany(Sprk.Bff.Api.Services.Office.EmailAttachmentAddPlanner.SavedNamesOf)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        static bool IsSystemFailure(AuthorizationResult decision) =>
            string.Equals(decision.ReasonCode, AuthorizationResult.SystemFailureReasonCode, StringComparison.Ordinal);
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

}
