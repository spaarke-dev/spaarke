using System.IO;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Core.Auth;
using Spaarke.Core.Utilities;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// File access endpoints for SharePoint Embedded files.
/// Implements Microsoft's recommended patterns for SPE file access (Nov 2025).
///
/// 🔴 IDENTITY (unified-access-control-r2 task 171, owner round 69 — broker-only). Every route here authorizes the
/// caller on the <c>sprk_document</c> row (<see cref="DocumentAuthorizationFilter"/>), verifies the row's SPE pointer
/// (<c>RecordContainerResolver.EnsureDocumentPointerContainerAsync</c>), and then calls Graph APP-ONLY. Until
/// 2026-10-06 the preview / content / office / open-links / view-url / share-link reads ran as the user (OBO), which
/// SharePoint Embedded answers only for a caller holding a container ROLE — so they failed for everyone on a
/// per-record secure container (no members by design) and for any user not hand-added to a business-unit container.
/// The Office EDIT itself still runs as the user in Office; for a document in a SECURE container the /office and
/// /open-links routes grant a just-in-time writer role to a caller with Write on the secure record
/// (<c>OfficeEditAccessService</c>); a business-unit container's internal users are standing writers
/// (<c>SpeContainerMembershipSyncJob</c>).
///
/// References:
/// - Preview: https://learn.microsoft.com/en-us/graph/api/driveitem-preview
/// - Content: https://learn.microsoft.com/en-us/graph/api/driveitem-get-content
/// - Office: https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/app-concepts/office-experiences
/// </summary>
public static class FileAccessEndpoints
{
    /// <summary>Share-link refusal: the document belongs to a SECURE record (owner round 72 item 2).</summary>
    internal const string ShareLinkSecureRecordCode = "sdap.access.deny.share_link_secure_record";

    /// <summary>Share-link refusal: the document belongs to a RESTRICTED record (owner round 72 item 2).</summary>
    internal const string ShareLinkRestrictedRecordCode = "sdap.access.deny.share_link_restricted_record";

    /// <summary>Share-link refusal: the document row could not be read, so its record's protection is unknown.</summary>
    internal const string ShareLinkProtectionUnverifiableCode = "sdap.access.deny.share_link_protection_unverifiable";

    private const string ShareLinkSecureDetail =
        "Sharing links cannot be created for documents of a secure record: a link reaches people outside the record's "
        + "access list. Share the record with the person instead.";

    private const string ShareLinkRestrictedDetail =
        "Sharing links cannot be created for documents of a restricted record. Share the record with the person instead.";

    /// <summary>The record types that carry sprk_issecure / sprk_accesspermission (the share-link refusal reads them).</summary>
    private static readonly HashSet<string> ShareLinkRootTypes = new(StringComparer.Ordinal)
    {
        "sprk_project", "sprk_matter", "sprk_workassignment",
    };

    /// <summary>
    /// Why a sharing link may NOT be minted for <paramref name="documentId"/> — its record is SECURE or RESTRICTED (owner
    /// round 72 item 2; task 171 adversarial findings 10 and V2) — or <see langword="null"/> for a standard document.
    /// </summary>
    /// <remarks>
    /// <para><b>Which records.</b> Every record the document is filed to, resolved to its project / matter / work
    /// assignment: a ROOT link directly; any other link (communication — every email archive and attachment carries only
    /// <c>sprk_relatedcommunication</c> — event, invoice, to-do, agreement, …) through
    /// <see cref="CoreAncestorResolver.ResolveStampsAsync"/>, the one-hop ancestor rule the access model already uses (a
    /// communication inherits its parent's access permission). Party links (contact, organization) carry no protection.</para>
    /// <para><b>Which flags.</b> <see cref="ExternalParticipationService.GetRootRecordFlagsAsync"/> for every such root (an
    /// id it cannot read comes back secure AND restricted), plus <c>RecordContainerResolver.DeriveDocumentContainersAsync</c>
    /// <c>.IsSecure</c>.</para>
    /// <para><b>Fail closed.</b> An unreadable row, an ancestor read that ERRORS, or — when the document has a child link —
    /// a derivation that cannot be decided answers <see cref="ShareLinkProtectionUnverifiableCode"/>. A document whose
    /// links are all roots (or none) keeps the earlier behaviour: an undecided derivation adds no refusal there, because
    /// the root flags already decided every record it is filed to.</para>
    /// </remarks>
    internal static async Task<(string ReasonCode, string Detail)?> ShareLinkProtectionRefusalAsync(
        Guid documentId,
        Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
        IGenericEntityService entityService,
        Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService participations,
        Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver ancestors,
        ILogger logger,
        CancellationToken ct)
    {
        var row = await entityService
            .RetrieveAsync("sprk_document", documentId, DocumentLinkFields.LogicalNames.ToArray(), ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return Unverifiable("the document row could not be read");
        }

        var roots = new HashSet<(string Entity, Guid Id)>();
        var hasChildLink = false;
        foreach (var link in DocumentLinkFields.All)
        {
            if (row.GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(link.LogicalName) is not { Id: var linkedId }
                || linkedId == Guid.Empty)
            {
                continue;
            }

            if (ShareLinkRootTypes.Contains(link.TargetEntityLogicalName))
            {
                roots.Add((link.TargetEntityLogicalName, linkedId));
                continue;
            }

            var resolved = await ancestors.ResolveStampsAsync(link.TargetEntityLogicalName, linkedId, ct).ConfigureAwait(false);
            switch (resolved.Status)
            {
                case Sprk.Bff.Api.Services.Dataverse.CoreAncestorStatus.Error:
                    return Unverifiable($"the record behind {link.LogicalName} could not be resolved ({resolved.Error})");
                case Sprk.Bff.Api.Services.Dataverse.CoreAncestorStatus.Unclassified:
                    // A party (contact, organization) or a non-content type: no protection flag applies to it.
                    continue;
                default:
                    // A CHILD (Derived / NoAncestor) is what makes an undecided derivation unverifiable; a core target that
                    // is not a flag-bearing root (a service request) carries no protection flag of its own.
                    hasChildLink |= resolved.Status is Sprk.Bff.Api.Services.Dataverse.CoreAncestorStatus.Derived
                        or Sprk.Bff.Api.Services.Dataverse.CoreAncestorStatus.NoAncestor;
                    foreach (var stamp in resolved.Stamps)
                    {
                        if (ShareLinkRootTypes.Contains(stamp.EntityType) && stamp.RecordId != Guid.Empty)
                        {
                            roots.Add((stamp.EntityType, stamp.RecordId));
                        }
                    }

                    break;
            }
        }

        foreach (var group in roots.GroupBy(r => r.Entity, StringComparer.Ordinal))
        {
            var ids = group.Select(r => r.Id).ToArray();
            var flags = await participations.GetRootRecordFlagsAsync(group.Key, ids, ct).ConfigureAwait(false);
            foreach (var id in ids)
            {
                // Every id asked about is in the map; an unreadable one is secure AND restricted (fail closed).
                var f = flags.TryGetValue(id, out var known) ? known : Sprk.Bff.Api.Infrastructure.ExternalAccess.RootRecordFlags.Unreadable;
                if (f.IsSecure || f.IsRestricted)
                {
                    logger.LogInformation(
                        "CreateShareLink REFUSED | DocumentId: {DocumentId} | {Entity} {RecordId} is {What}.",
                        documentId, group.Key, id,
                        f.IsUnreadable ? "unreadable (fail closed)" : f.IsSecure ? "secure" : "restricted");
                    return f.IsSecure && !f.IsRestricted
                        ? (ShareLinkSecureRecordCode, ShareLinkSecureDetail)
                        : (ShareLinkRestrictedRecordCode, ShareLinkRestrictedDetail);
                }
            }
        }

        var derivation = await containerResolver.DeriveDocumentContainersAsync(documentId, ct).ConfigureAwait(false);
        if (derivation.Decided && derivation.IsSecure)
        {
            logger.LogInformation(
                "CreateShareLink REFUSED | DocumentId: {DocumentId} | the document belongs to a secure record ({Reason}).",
                documentId, derivation.Reason);
            return (ShareLinkSecureRecordCode, ShareLinkSecureDetail);
        }

        if (!derivation.Decided && hasChildLink)
        {
            return Unverifiable($"the document's container could not be derived ({derivation.Reason})");
        }

        return null;

        (string ReasonCode, string Detail) Unverifiable(string why)
        {
            logger.LogWarning("CreateShareLink REFUSED | DocumentId: {DocumentId} | protection unverifiable: {Why}.", documentId, why);
            return (ShareLinkProtectionUnverifiableCode,
                "Whether this document's record allows a sharing link could not be determined, so none was created.");
        }
    }

    public static IEndpointRouteBuilder MapFileAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var docs = app.MapGroup("/api/documents").RequireAuthorization();

        // Register endpoints using method groups (fixes CS1593 compilation error)
        // The URL-MINTING reads (task-022 inventory): preview-url, preview, office, open-links,
        // view-url. Each returns a url that OUTLIVES the request, and none carried a per-document
        // filter.
        //
        // Task 171: these five reached SPE as the user (OBO) until 2026-10-06, so SPE's container check stood
        // behind the gate. They now read APP-ONLY, so this per-document filter plus the pointer check in each
        // handler IS the whole boundary — exactly as for /content and /download. The gate NARROWS behaviour
        // deliberately: SPE permission is container-scoped and coarser than per-document Dataverse rights, so a
        // caller with container access but no Read on the sprk_document row gets 403 (spec FR-01).
        //
        // Operation is `read`, not a new mint-specific key. A separate key would carry the SAME
        // required right and therefore change no decision — CLAUDE.md §11 asks for a concrete
        // behaviour that fails without the new surface, and there is none. What IS different about
        // these routes is url LIFETIME, not authorization: revoking a caller's access later does not
        // invalidate a url already minted. That is a real gap, deliberately NOT solved by an
        // operation key because no key can. It belongs with task 012's link-lifecycle work.
        docs.MapGet("/{documentId}/preview-url", GetPreviewUrl)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentPreviewUrl")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/preview", GetPreview)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentPreview")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // A-1 applies here identically (unified-access-control-r2 task 002, spec FR-01). GetContent
        // streams the document's bytes (`TypedResults.Stream`) from the same app-only SPE path as
        // /download, and had the same missing gate. Closing /download alone would have left the attack
        // scenario fully intact behind a different URL — the finding is the missing per-document
        // authorization, not the route name.
        docs.MapGet("/{documentId}/content", GetContent)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentContent")
            .WithTags("File Access")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/office", GetOffice)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentOfficeViewer")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        docs.MapGet("/{documentId}/open-links", GetOpenLinks)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentOpenLinks")
            .WithTags("File Access")
            .Produces<OpenLinksResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status500InternalServerError);

        // Recipient-openable SPE sharing link for a document (email-communication-solution-r5 R2 item
        // 12). Minted APP-ONLY since task 171, after the "share" gate below and the pointer check. Used by the
        // email composer's "Link" attachments so an emailed link opens the actual file (incl. for external recipients).
        //
        // Per-document authorization (unified-access-control-r2 task 072, spec FR-01). This was the ONE
        // route on this group with no filter — and the one that mints a credential. Its authority was the
        // caller's container-scoped OBO access, which is coarser than per-document Dataverse rights: the
        // exact coarseness the id-keyed routes in this file exist to close (see the header comment).
        //
        // "share", not "read" like the eight siblings: those return content to a caller the platform can
        // still identify and revoke. This one mints a URL that OUTLIVES revocation. Rationale + the
        // precedent it follows (driveitem.createlink / share_document, both already Share) are recorded on
        // the ["share"] entry in OperationAccessPolicy.
        docs.MapPost("/{documentId}/share-link", CreateShareLink)
            .AddDocumentAuthorizationFilter("share")
            .WithName("CreateDocumentShareLink")
            .WithTags("File Access")
            .WithDescription("Create a recipient-openable SPE sharing link (Graph createLink) for a document. "
                           + "Requires Share on the document. Organization-scoped by default; set "
                           + "allowExternalRecipients=true for an anonymous link. Always expires.")
            .Produces<ShareLinkResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status502BadGateway);

        docs.MapGet("/{documentId}/view-url", GetViewUrl)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentViewUrl")
            .WithTags("File Access")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // Per-document authorization (unified-access-control-r2 task 002, spec FR-01, finding A-1).
        //
        // This route had NO per-document filter: the group's RequireAuthorization() asked only "are you
        // anyone?", and the handler then streamed app-only from SPE — so any authenticated caller could
        // download any document by GUID. That is R1's January-2026 attack scenario.
        //
        // The app-only SPE stream is NOT the defect and is deliberately unchanged: files written by the
        // managed identity can only be read back by it (auth constraints, Pattern 4 — Writer-Identity
        // Matching). What was missing is the Dataverse-level answer to "may THIS caller have this
        // document?", which the filter now supplies before any SPE call is made.
        //
        // Operation "read" matches the two routes that already do this correctly — the sibling
        // DataverseDocumentsEndpoints.cs `GET /api/v1/documents/{id}/download` and the eml-render route
        // below. Both download routes must reach the SAME decision for the same caller on the same
        // document; task 001 pinned their disagreement as the finding.
        docs.MapGet("/{documentId}/download", GetDownload)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentDownload")
            .WithTags("File Access")
            .WithDescription("Download document file. The caller is authorized against the document " +
                "first; the SPE stream itself is app-only because background-written files are only " +
                "readable by the identity that wrote them.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // GET /api/documents/{documentId}/eml-render (email-communication-solution-r5 task 010 / FR-07 / NFR-03).
        // Renders an archived .eml as sanitized, safe-to-display HTML for the reading pane. Unlike the sibling
        // routes above (which rely on the group's RequireAuthorization() + downstream Graph/Dataverse access),
        // this route ADDS a per-document DocumentAuthorizationFilter("read") because it is on the untrusted-
        // email-HTML path and MUST fail closed — an unauthorized/inaccessible document returns 403/404 with NO
        // HTML body (the filter/resolution rejects BEFORE any HTML is produced). ADR-008 (endpoint-filter authz).
        docs.MapGet("/{documentId}/eml-render", GetEmlRender)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentEmlRender")
            .WithTags("File Access")
            .WithDescription("Render an archived .eml as sanitized, safe HTML for the reading pane. " +
                "Server-side sanitization is the authoritative XSS boundary (NFR-03); fails closed on no-access.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status500InternalServerError);

        // POST /api/documents/resolve-identity — FR-01 (spaarkeai-word-add-in-r1 task 012). Turns the open document's
        // URL (Office.context.document.url) into the sprk_document it came from, or into a clean "not a Spaarke
        // document": 200 with resolved=false, never a 404 for that case.
        //
        // The first route in this group that PRODUCES a documentId instead of consuming one, so its authorization is
        // two filters and their ORDER is the design (filters run in registration order):
        //   1. DocumentUrlIdentityFilter — URL → drive item (Graph /shares, AS THE CALLER) → sprk_document
        //      (sprk_graphitemid_uk). No identity: it returns 200 {resolved:false} itself. Otherwise it writes the
        //      resolved id into the route values as `documentId` — the moment the id first exists.
        //   2. DocumentAuthorizationFilter("read") — the same filter and operation as open-links — authorizes that id,
        //      answering 403 with no metadata when the caller may not read the record.
        // The handler runs only after (2) allows, and it is the only place document metadata enters a response.
        //
        // No rate-limit policy, matching the nine sibling routes in this group: the pane calls this once per open,
        // the same cadence as open-links.
        docs.MapPost("/resolve-identity", ResolveIdentity)
            .AddEndpointFilter<DocumentUrlIdentityFilter>()
            .AddDocumentAuthorizationFilter("read")
            .WithName("ResolveDocumentIdentity")
            .WithTags("File Access")
            .WithDescription("Resolve an open Office document's URL to the Spaarke document it came from. " +
                "resolved=false (200) means the file is not a Spaarke document; 503 means it could not be determined.")
            .Produces<DocumentIdentityResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // GET /api/documents/{documentId}/identity — spaarkeai-word-add-in-r1 task 112 (UAT round 11 item 4). The same
        // DocumentIdentityResponse as resolve-identity, for a document the pane already knows by its ID (its stamp —
        // e.g. after Quick Save) rather than by its URL. Only the id source differs; the authorization filter, its
        // operation, the DTO and the related-record slot logic are the ones resolve-identity uses.
        //
        // The id is in the route, so DocumentAuthorizationFilter("read") decides on it BEFORE the handler runs, exactly
        // as on /open-links: a caller who may not read the document gets 403 with no metadata. Dataverse grants no
        // rights on a row that does not exist, so an UNKNOWN id is also 403 — deliberately indistinguishable from "not
        // yours", so this route is not an existence oracle. 404 document_not_found is reachable only when the row is
        // gone after the filter allowed (deleted between the two reads); 503 when Dataverse cannot answer.
        docs.MapGet("/{documentId}/identity", GetDocumentIdentity)
            .AddDocumentAuthorizationFilter("read")
            .WithName("GetDocumentIdentity")
            .WithTags("File Access")
            .WithDescription("The identity of a Spaarke document by its id: names and the record it is filed to " +
                "(same shape as resolve-identity). 403 when the caller may not read it — including an unknown id.")
            .Produces<DocumentIdentityResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        // POST /api/documents/resolve-email-identity — spaarkeai-word-add-in-r1 task 120 (UAT round 12 O6). The same
        // DocumentIdentityResponse as resolve-identity, for the email open in Outlook: is it already saved to Spaarke,
        // and to which record? The keys travel in the body (they identify a message in the caller's mailbox).
        //
        // Same two-filter shape as resolve-identity, and the order is the design:
        //   1. DocumentEmailIdentityFilter — keys → the NEWEST saved .eml sprk_document (sprk_isemailarchive = true,
        //      sprk_emailmessageid = either key; app-only read). None: it returns 200 {resolved:false,
        //      reason:"not_saved"} itself. Otherwise it writes that id into the route values as `documentId`.
        //   2. DocumentAuthorizationFilter("read") — 403 with no metadata when the caller may not read THAT row. An older
        //      copy the caller could read is not searched (no oracle; one authorization per request).
        // No rate-limit policy, matching the sibling routes: the pane calls this once per open and after a save.
        docs.MapPost("/resolve-email-identity", ResolveEmailIdentity)
            .AddEndpointFilter<DocumentEmailIdentityFilter>()
            .AddDocumentAuthorizationFilter("read")
            .WithName("ResolveEmailDocumentIdentity")
            .WithTags("File Access")
            .WithDescription("Find the saved Spaarke document (.eml) of an Outlook email by its message id. " +
                "resolved=false with reason not_saved (200) means it is not saved; 403 means the newest saved copy " +
                "is not readable by the caller; 503 means it could not be determined.")
            .Produces<DocumentIdentityResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status503ServiceUnavailable);

        return app;

        // Static local functions (method groups)

        /// <summary>
        /// POST /api/documents/resolve-identity (task 012; task 026 / FR-09 extended the related-record shape).
        /// Reached only after <see cref="DocumentUrlIdentityFilter"/> resolved an identity AND
        /// <see cref="DocumentAuthorizationFilter"/> allowed <c>read</c> on it. Both are filter-enforced (ADR-008),
        /// so the handler performs no access check of its own — including for the related-record display fields
        /// this method now also resolves: the access model treats record access as equivalent to document access
        /// (see <c>DocumentUrlIdentityResolution.ResolveRelatedRecordDisplayAsync</c> and
        /// <c>notes/026-slot-scope-decision.md</c> §6), so a SECOND authorization check on the related record
        /// itself would be redundant, not defense-in-depth. <paramref name="request"/> is bound here so the filter
        /// can read it from the invocation arguments.
        /// </summary>
        static async Task<IResult> ResolveIdentity(
            ResolveDocumentIdentityRequest? request,
            IGenericEntityService dataverse,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            if (context.Items[DocumentUrlIdentityFilter.ResolutionItemKey]
                is not Sprk.Bff.Api.Services.Documents.DocumentUrlIdentityResolution.Resolution identity)
            {
                // Reachable only if this route's filters were removed or reordered.
                throw new InvalidOperationException(
                    "resolve-identity reached its handler without a filter-resolved identity.");
            }

            return TypedResults.Ok(await BuildIdentityResponseAsync(identity, dataverse, logger, ct));
        }

        /// <summary>
        /// POST /api/documents/resolve-email-identity (task 120). Reached only after
        /// <see cref="DocumentEmailIdentityFilter"/> found the email's saved <c>.eml</c> AND
        /// <see cref="DocumentAuthorizationFilter"/> allowed <c>read</c> on it; like <c>ResolveIdentity</c> it performs
        /// no access check of its own (ADR-008). <paramref name="request"/> is bound so the filter can read it.
        /// </summary>
        static async Task<IResult> ResolveEmailIdentity(
            ResolveEmailIdentityRequest? request,
            IGenericEntityService dataverse,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            if (context.Items[DocumentUrlIdentityFilter.ResolutionItemKey]
                is not Sprk.Bff.Api.Services.Documents.DocumentUrlIdentityResolution.Resolution identity)
            {
                // Reachable only if this route's filters were removed or reordered.
                throw new InvalidOperationException(
                    "resolve-email-identity reached its handler without a filter-resolved identity.");
            }

            return TypedResults.Ok(await BuildIdentityResponseAsync(identity, dataverse, logger, ct));
        }

        /// <summary>
        /// GET /api/documents/{documentId}/identity (task 112). Reached only after
        /// <see cref="DocumentAuthorizationFilter"/> allowed <c>read</c> on the route's <c>documentId</c>; like
        /// <c>ResolveIdentity</c> it performs no access check of its own (ADR-008), and the related-record display
        /// fields follow the same record-access ⇔ document-access rule (<c>notes/026-slot-scope-decision.md</c> §6).
        /// </summary>
        static async Task<IResult> GetDocumentIdentity(
            string documentId,
            IGenericEntityService dataverse,
            ILogger<Program> logger,
            CancellationToken ct)
        {
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400);
            }

            var identity = await Sprk.Bff.Api.Services.Documents.DocumentUrlIdentityResolution
                .ResolveByIdAsync(docGuid, dataverse, logger, ct);
            if (identity is null)
            {
                // Reachable only when the filter allowed the id and the row is gone by the time it is read.
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404);
            }

            return TypedResults.Ok(await BuildIdentityResponseAsync(identity, dataverse, logger, ct));
        }

        /// <summary>
        /// The resolved-identity response both identity routes return — extracted from <c>ResolveIdentity</c> by task
        /// 112 so the URL and id routes share one body (names, related record, display name and number). Call it only
        /// after the route's <see cref="DocumentAuthorizationFilter"/> has allowed the caller: this is where document
        /// metadata enters a response.
        /// </summary>
        static async Task<DocumentIdentityResponse> BuildIdentityResponseAsync(
            Sprk.Bff.Api.Services.Documents.DocumentUrlIdentityResolution.Resolution identity,
            IGenericEntityService dataverse,
            ILogger logger,
            CancellationToken ct)
        {
            var related = identity.RelatedRecord;
            RelatedRecordIdentity? relatedRecordResponse = null;
            if (related is not null)
            {
                var display = await Sprk.Bff.Api.Services.Documents.DocumentUrlIdentityResolution
                    .ResolveRelatedRecordDisplayAsync(related, dataverse, logger, ct);
                relatedRecordResponse = new RelatedRecordIdentity(
                    related.LogicalName, related.Id.ToString("D"), related.Name, display.DisplayName, display.Number);
            }

            return new DocumentIdentityResponse(
                Resolved: true,
                DocumentId: identity.DocumentId.ToString("D"),
                DocumentName: identity.DocumentName,
                FileName: identity.FileName,
                RelatedRecord: relatedRecordResponse,
                Reason: null);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/preview-url
        /// Returns an ephemeral preview URL, minted app-only after the per-document gate (task 171).
        /// Includes checkout status for PCF control to show lock indicators
        /// </summary>
        static async Task<IResult> GetPreviewUrl(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            DocumentCheckoutService checkoutService,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetPreviewUrl called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4-5. Call Graph API (via the SpeFileStore app-only facade) to get preview URL.
            // Request chromeless preview (no SharePoint header/toolbar). Per CICD-088b,
            // the Graph SDK request/response types stay inside Infrastructure.Graph.
            var rawPreviewUrl = await speFileStore.GetEmbedPreviewUrlAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                additionalData: new Dictionary<string, object>
                {
                    { "chromeless", true },  // Hide SharePoint preview header
                    { "viewer", "onedrive" }  // Use OneDrive viewer
                },
                ct: ct);

            if (string.IsNullOrEmpty(rawPreviewUrl))
            {
                throw new SdapProblemException(
                    "preview_not_available",
                    "Preview Not Available",
                    $"Graph API did not return a preview URL for document {documentId}",
                    500
                );
            }

            logger.LogInformation("Preview URL retrieved successfully | TraceId: {TraceId}",
                context.TraceIdentifier);

            // 6. Modify preview URL to hide SharePoint banner/header
            // Use Microsoft-documented 'nb=true' parameter (no banner)
            // Reference: https://learn.microsoft.com/en-us/sharepoint/dev/
            var previewUrl = rawPreviewUrl;
            {
                var separator = previewUrl.Contains('?') ? '&' : '?';
                // nb=true hides the top banner/header in SharePoint embed.aspx
                previewUrl = $"{previewUrl}{separator}nb=true";
                logger.LogInformation("Modified preview URL with nb=true (no banner) | TraceId: {TraceId}",
                    context.TraceIdentifier);
            }

            // 7. Extract file extension from filename
            string? fileExtension = null;
            if (!string.IsNullOrEmpty(document.FileName))
            {
                var lastDot = document.FileName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < document.FileName.Length - 1)
                {
                    fileExtension = document.FileName.Substring(lastDot + 1);
                }
            }

            // 8. Get checkout status for the document
            CheckoutStatusInfo? checkoutStatus = null;
            try
            {
                checkoutStatus = await checkoutService.GetCheckoutStatusAsync(docGuid, context.User, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail - checkout status is non-critical
                logger.LogWarning(ex, "Failed to get checkout status for document {DocumentId}", documentId);
            }

            // 9. Return PCF-compatible response (flat structure for SpeFileViewer/SpeDocumentViewer)
            return TypedResults.Ok(new
            {
                previewUrl = previewUrl,  // Modified URL with chromeless parameters
                documentInfo = new
                {
                    name = document.FileName ?? document.Name ?? "Unknown",
                    fileExtension = fileExtension,
                    size = document.FileSize,
                    lastModified = document.ModifiedOn.ToString("o") // ISO 8601 format
                },
                checkoutStatus = checkoutStatus != null ? new
                {
                    isCheckedOut = checkoutStatus.IsCheckedOut,
                    checkedOutBy = checkoutStatus.CheckedOutBy != null ? new
                    {
                        id = checkoutStatus.CheckedOutBy.Id,
                        name = checkoutStatus.CheckedOutBy.Name,
                        email = checkoutStatus.CheckedOutBy.Email
                    } : null,
                    checkedOutAt = checkoutStatus.CheckedOutAt?.ToString("o"),
                    isCurrentUser = checkoutStatus.IsCurrentUser
                } : null,
                correlationId = context.TraceIdentifier
            });
        }

        /// <summary>
        /// GET /api/documents/{documentId}/preview
        /// Redirects to an embeddable preview URL (iframe scenarios), minted app-only after the gate (task 171)
        /// </summary>
        static async Task<IResult> GetPreview(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetPreview called | DocumentId: {DocumentId}", documentId);

            // 1. Validate document ID
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4. Get preview URL app-only (via SpeFileStore facade per CICD-088b)
            var previewUrl = await speFileStore.GetEmbedPreviewUrlAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                additionalData: null,
                ct: ct);

            if (string.IsNullOrEmpty(previewUrl))
            {
                throw new SdapProblemException(
                    "preview_not_available",
                    "Preview Not Available",
                    $"Graph API did not return a preview URL for document {documentId}",
                    500
                );
            }

            // 5. Redirect to preview page
            logger.LogInformation("Redirecting to preview URL for document {DocumentId}", documentId);
            return TypedResults.Redirect(previewUrl);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/content
        /// Returns the file content stream, read app-only after the gate (task 171)
        /// </summary>
        static async Task<IResult> GetContent(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetContent called | DocumentId: {DocumentId}", documentId);

            // 1. Validate document ID
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4. Download file content app-only (via SpeFileStore facade per CICD-088b)
            var contentStream = await speFileStore.DownloadFileAsync(
                document.GraphDriveId!, document.GraphItemId!, ct);

            if (contentStream == null)
            {
                throw new SdapProblemException(
                    "content_not_found",
                    "File Content Not Found",
                    $"Graph API returned null content stream for document {documentId}",
                    500
                );
            }

            // 5. Return file stream with proper content type
            var contentType = document.MimeType ?? "application/octet-stream";
            var fileName = document.FileName ?? $"{documentId}.bin";

            logger.LogInformation("Returning file content | FileName: {FileName} | ContentType: {ContentType}",
                fileName, contentType);

            return TypedResults.Stream(contentStream, contentType, fileName);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/office
        /// Returns the Office web URL (read app-only after the gate, task 171). For a document in a SECURE container,
        /// a caller with Write on the secure record is granted a just-in-time writer role first (owner round 70).
        /// </summary>
        static async Task<IResult> GetOffice(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            Sprk.Bff.Api.Services.Documents.OfficeEditAccessService editAccess,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetOffice called | DocumentId: {DocumentId}", documentId);

            // 1. Validate document ID
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4. Get Office web app URL app-only (via SpeFileStore facade per CICD-088b). The URL is a POINTER: Office
            //    enforces the user's own role when they open it.
            var driveItem = await speFileStore.GetDriveItemAsync(
                document.GraphDriveId!, document.GraphItemId!,
                selectFields: new[] { "id", "name", "webUrl" }, ct: ct);

            if (string.IsNullOrEmpty(driveItem?.WebUrl))
            {
                throw new SdapProblemException(
                    "office_url_not_available",
                    "Office URL Not Available",
                    $"Graph API did not return a webUrl for document {documentId}",
                    500
                );
            }

            logger.LogInformation("Office URL retrieved | DocumentId: {DocumentId}", documentId);

            // 5. Edit access (owner round 70): a SECURE container has no standing members, so a caller with Write on
            //    the secure record gets a just-in-time writer role before the URL is useful to them.
            var edit = await editAccess.PrepareAsync(docGuid, document.GraphDriveId!, context, ct);

            // 6. Return structured JSON response (not redirect)
            // Office Online enforces the user's own role when they open the URL.
            return TypedResults.Ok(new
            {
                officeUrl = driveItem.WebUrl,
                permissions = new
                {
                    canEdit = edit.CanEdit,
                    canView = true,
                    role = edit.Role
                },
                correlationId = context.TraceIdentifier
            });
        }

        /// <summary>
        /// GET /api/documents/{documentId}/open-links
        /// Returns desktop protocol URL (ms-word:, ms-excel:, ms-powerpoint:) and web URL
        /// for opening documents in native Office applications.
        /// </summary>
        static async Task<IResult> GetOpenLinks(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            Sprk.Bff.Api.Services.Documents.OfficeEditAccessService editAccess,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetOpenLinks called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4-5. Get DriveItem metadata app-only (SpeFileStore facade per CICD-088b). webUrl / webDavUrl are POINTERS.
            var driveItem = await speFileStore.GetDriveItemAsync(
                document.GraphDriveId!, document.GraphItemId!,
                selectFields: new[] { "id", "name", "webUrl", "webDavUrl", "file", "parentReference" }, ct: ct);

            if (driveItem == null)
            {
                throw new SdapProblemException(
                    "item_not_found",
                    "Drive Item Not Found",
                    $"Graph API did not return drive item for document {documentId}",
                    404
                );
            }

            if (string.IsNullOrEmpty(driveItem.WebUrl))
            {
                throw new SdapProblemException(
                    "web_url_not_available",
                    "Web URL Not Available",
                    $"Graph API did not return a webUrl for document {documentId}",
                    500
                );
            }

            // 6. Extract MIME type from file facet
            var mimeType = driveItem.MimeType ?? document.MimeType ?? "application/octet-stream";
            var fileName = string.IsNullOrEmpty(driveItem.Name) ? (document.FileName ?? "Unknown") : driveItem.Name;

            // 7. Construct direct file URL for desktop protocol
            // The webUrl returns Doc.aspx (Office Online URL) which doesn't work well with ms-word: protocol
            // We need to construct a direct file URL from the parent path + filename
            string? directFileUrl = null;

            // Prefer webDavUrl if available (direct file URL)
            if (!string.IsNullOrEmpty(driveItem.WebDavUrl))
            {
                directFileUrl = driveItem.WebDavUrl;
            }
            // Otherwise construct from parent path
            else if (driveItem.ParentReferencePath != null && !string.IsNullOrEmpty(fileName))
            {
                // ParentReference.Path format: /drives/{driveId}/root:/folder/path
                // Extract the path after "root:" and construct URL
                var pathParts = driveItem.ParentReferencePath.Split("root:");
                if (pathParts.Length > 1)
                {
                    var folderPath = pathParts[1].TrimStart('/');
                    // Get base SharePoint URL from webUrl (before /_layouts/)
                    var webUrlParts = driveItem.WebUrl!.Split("/_layouts/");
                    if (webUrlParts.Length > 0)
                    {
                        var baseUrl = webUrlParts[0];
                        directFileUrl = $"{baseUrl}/{folderPath}/{Uri.EscapeDataString(fileName)}";
                    }
                }
            }

            // Fall back to webUrl if we couldn't construct a direct URL
            var urlForDesktop = directFileUrl ?? driveItem.WebUrl;

            logger.LogInformation(
                "OpenLinks URL selection | HasWebDavUrl: {HasWebDavUrl} | UsingDirectFileUrl: {UsingDirect}",
                !string.IsNullOrEmpty(driveItem.WebDavUrl), directFileUrl is not null);

            // 8. Generate desktop protocol URL using DesktopUrlBuilder
            var desktopUrl = DesktopUrlBuilder.FromMime(urlForDesktop, mimeType);

            logger.LogInformation(
                "OpenLinks generated | FileName: {FileName} | MimeType: {MimeType} | HasDesktopUrl: {HasDesktopUrl} | TraceId: {TraceId}",
                fileName, mimeType, desktopUrl != null, context.TraceIdentifier);

            // 9. Edit access (owner round 70): just-in-time writer role on a SECURE container for a caller with Write on
            //    the secure record. A caller without Write gets the same pointers and no grant — SharePoint refuses
            //    them, and the in-app preview (broker) is their view path.
            await editAccess.PrepareAsync(docGuid, document.GraphDriveId!, context, ct, describeSharedContainer: false);

            // 10. Return response
            return TypedResults.Ok(new OpenLinksResponse(
                DesktopUrl: desktopUrl,
                WebUrl: driveItem.WebUrl,
                MimeType: mimeType,
                FileName: fileName
            ));
        }

        /// <summary>
        /// POST /api/documents/{documentId}/share-link
        /// Creates a recipient-openable SPE sharing link (Graph createLink) for the document, so an
        /// emailed "Link" opens the actual file — including, on request, for external recipients
        /// (email-communication-solution-r5 R2 item 12). APP-ONLY since task 171: per-document authorization is the
        /// <c>"share"</c> filter on the route, plus the pointer check; the BFF identity performs the createLink.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>unified-access-control-r2 task 072.</b> Three things changed and they are independent:
        /// the route gained a per-document <c>share</c> gate (it had none); the link gained a bounded
        /// expiry (it was <c>expiration: null</c> — permanent); and <c>anonymous</c> stopped being the
        /// silent default (it is now an explicit request, capped harder, and logged).
        /// </para>
        /// <para>
        /// <b>Why expiry is the load-bearing control.</b> A minted SPE URL is not revocable through
        /// Dataverse — removing the caller's rights afterwards does not invalidate it. This file's header
        /// already records that as unsolved in general. Task 072 does not solve it in general; it removes
        /// the worst instance (permanent + anonymous + ungated) and bounds the rest.
        /// </para>
        /// </remarks>
        static async Task<IResult> CreateShareLink(
            string documentId,
            ShareLinkRequest? request,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            IGenericEntityService entityService,
            Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalParticipationService participations,
            Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver ancestors,
            IOptionsMonitor<ShareLinkOptions> shareLinkOptions,
            TimeProvider timeProvider,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("CreateShareLink called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id", "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format", 400);
            }

            var document = await dataverseService.GetDocumentAsync(documentId, ct);
            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found", "Document Not Found",
                    $"Document with ID '{documentId}' does not exist", 404);
            }

            // Reuses the same SPE-pointer validation as open-links (404/409 on missing/malformed pointers).
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // Owner round 72 item 2 (task 171, adversarial finding 10): a sharing link reaches people OUTSIDE Dataverse's
            // decision, so it is never minted for a document of a SECURE or RESTRICTED record. Standard documents keep it.
            if (await ShareLinkProtectionRefusalAsync(docGuid, containerResolver, entityService, participations, ancestors, logger, ct)
                is { } protection)
            {
                return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.Forbidden(protection.ReasonCode, protection.Detail, context.TraceIdentifier);
            }

            var (scope, expiresAt) = ResolveShareLinkPolicy(
                request, shareLinkOptions.CurrentValue, timeProvider, documentId, logger, context);
            var wantsExternalReach = scope == AnonymousScope;

            if (wantsExternalReach)
            {
                // Warning, not Information: this is the one branch that publishes a handle reachable by
                // parties with no Spaarke identity. The caller's oid is what makes it attributable.
                logger.LogWarning(
                    "CreateShareLink minting an ANONYMOUS link | DocumentId: {DocumentId} | "
                    + "Caller: {CallerObjectId} | ExpiresAt: {ExpiresAt} | TraceId: {TraceId}",
                    documentId,
                    // The comment above is the whole point: "the caller's oid is what makes it
                    // attributable". Until 2026-08-27 this read `FindFirst("oid") ?? NameIdentifier`,
                    // which under inbound claim mapping resolved `sub` — pairwise per user+app and
                    // joinable to nothing. The audit line for minting an ANONYMOUS link was therefore
                    // recording a pseudonym, defeating exactly the attributability task 072 added it for.
                    CallerResolution.ResolveObjectId(context.User),
                    expiresAt,
                    context.TraceIdentifier);
            }

            try
            {
                // Requires the tenant SPE/SharePoint external-sharing policy to allow "Anyone" links when
                // scope is anonymous; if disabled Graph throws → mapped to 502 below and the caller
                // (composer) falls back to the prior link (best-effort, never blocks the send).
                var url = await speFileStore.CreateSharingLinkAsync(
                    document.GraphDriveId!, document.GraphItemId!,
                    linkType: "view", scope: scope, expiration: expiresAt, ct: ct);

                if (string.IsNullOrWhiteSpace(url))
                {
                    throw new SdapProblemException(
                        "share_link_unavailable", "Share Link Unavailable",
                        $"Graph returned no sharing link for document {documentId}", 502);
                }

                logger.LogInformation(
                    "CreateShareLink succeeded | DocumentId: {DocumentId} | Scope: {Scope} | "
                    + "ExpiresAt: {ExpiresAt} | TraceId: {TraceId}",
                    documentId, scope, expiresAt, context.TraceIdentifier);
                return TypedResults.Ok(new ShareLinkResponse(url, expiresAt, scope));
            }
            // ADR-007: endpoints must NOT reference Microsoft.Graph SDK types directly (the Graph
            // request/response + error types stay isolated in Infrastructure.Graph). We still want the
            // clean 502 mapping for a Graph OData error (most commonly: tenant policy forbids anonymous
            // links), so match it by type NAME via an exception filter — no `Microsoft.Graph.*` type
            // reference in this endpoint. Non-Graph exceptions propagate unchanged (behavior preserved).
            catch (Exception ex) when (ex.GetType().FullName?.Contains("ODataError", StringComparison.Ordinal) == true)
            {
                logger.LogWarning(ex,
                    "CreateShareLink Graph error | DocumentId: {DocumentId} | TraceId: {TraceId}",
                    documentId, context.TraceIdentifier);
                throw new SdapProblemException(
                    "share_link_failed", "Share Link Failed",
                    $"Could not create a sharing link (Graph): {ex.Message}", 502);
            }
        }

        /// <summary>
        /// GET /api/documents/{documentId}/view-url
        /// Returns embeddable view URL using driveItem webUrl (not cached Preview action).
        /// Use this for real-time file viewing without the 30-60s Preview cache delay.
        /// Includes checkout status for PCF control to show lock indicators.
        /// </summary>
        static async Task<IResult> GetViewUrl(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            DocumentCheckoutService checkoutService,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetViewUrl called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // uac-r2 task 171: the read below runs AS THE APPLICATION, so the row's pointer must name a container this
            // document may use (409 document_storage_unverified otherwise, before any Graph read) — the check OBO used to
            // get for free from SPE's own container ACL.
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4-5. Get driveItem metadata for file info app-only (via SpeFileStore facade per CICD-088b)
            var driveItem = await speFileStore.GetDriveItemAsync(
                document.GraphDriveId!, document.GraphItemId!,
                selectFields: new[] { "id", "name", "webUrl", "size", "lastModifiedDateTime" }, ct: ct);

            if (driveItem == null)
            {
                throw new SdapProblemException(
                    "view_url_not_available",
                    "View URL Not Available",
                    $"Graph API did not return drive item for document {documentId}",
                    500
                );
            }

            // 6. Use Preview action to get embeddable URL (works for SPE files)
            // The Preview action returns a properly authenticated URL that works in iframes
            // Note: Preview URLs are cached for 30-60 seconds by SharePoint, but this is
            // the only reliable way to get an embeddable URL for SPE containers
            var previewUrlRaw = await speFileStore.GetEmbedPreviewUrlAsync(
                document.GraphDriveId!, document.GraphItemId!,
                additionalData: new Dictionary<string, object>
                {
                    { "chromeless", true },
                    { "viewer", "onedrive" }
                },
                ct: ct);

            string viewUrl;
            if (!string.IsNullOrEmpty(previewUrlRaw))
            {
                // Use the preview URL with nb=true (no banner)
                viewUrl = previewUrlRaw;
                var separator = viewUrl.Contains('?') ? '&' : '?';
                viewUrl = $"{viewUrl}{separator}nb=true";
                logger.LogInformation("Using Preview action URL for embedding");
            }
            else
            {
                // Fall back to webUrl if Preview fails
                viewUrl = driveItem.WebUrl ?? "";
                logger.LogWarning("Preview action failed, falling back to webUrl");
            }

            // Task 171: never log the URL — an app-only preview URL acts as the BFF identity for whoever holds it.
            logger.LogInformation("View URL constructed | FileName: {FileName}", driveItem.Name);

            // 6. Extract file extension from filename
            string? fileExtension = null;
            var fileName = driveItem.Name ?? document.FileName ?? "Unknown";
            if (!string.IsNullOrEmpty(fileName))
            {
                var lastDot = fileName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < fileName.Length - 1)
                {
                    fileExtension = fileName.Substring(lastDot + 1);
                }
            }

            // 7. Get checkout status for the document
            CheckoutStatusInfo? checkoutStatus = null;
            try
            {
                checkoutStatus = await checkoutService.GetCheckoutStatusAsync(docGuid, context.User, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail - checkout status is non-critical
                logger.LogWarning(ex, "Failed to get checkout status for document {DocumentId}", documentId);
            }

            // 8. Return PCF-compatible response (matches preview-url format for easy switching)
            return TypedResults.Ok(new
            {
                previewUrl = viewUrl,  // Named previewUrl for PCF compatibility
                documentInfo = new
                {
                    name = fileName,
                    fileExtension = fileExtension,
                    size = driveItem.Size ?? document.FileSize,
                    lastModified = (driveItem.LastModifiedDateTime ?? document.ModifiedOn).ToString("o")
                },
                checkoutStatus = checkoutStatus != null ? new
                {
                    isCheckedOut = checkoutStatus.IsCheckedOut,
                    checkedOutBy = checkoutStatus.CheckedOutBy != null ? new
                    {
                        id = checkoutStatus.CheckedOutBy.Id,
                        name = checkoutStatus.CheckedOutBy.Name,
                        email = checkoutStatus.CheckedOutBy.Email
                    } : null,
                    checkedOutAt = checkoutStatus.CheckedOutAt?.ToString("o"),
                    isCurrentUser = checkoutStatus.IsCurrentUser
                } : null,
                correlationId = context.TraceIdentifier
            });
        }

        /// <summary>
        /// GET /api/documents/{documentId}/download
        /// Downloads file content using app-only authentication via SpeFileStore.
        /// This is necessary for files uploaded by email-to-document automation,
        /// where users don't have direct SPE container permissions.
        /// </summary>
        static async Task<IResult> GetDownload(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            // uac-r2 task 166 r1 (round 21 item 1b): the pointer's container is verified before the app-only download.
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetDownload called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out var docGuid))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            logger.LogInformation("SPE pointers validated | DriveId: {DriveId} | ItemId: {ItemId}",
                document.GraphDriveId, document.GraphItemId);

            // 3b. uac-r2 task 166 r1 (owner round 21 item 1b): followed AS THE APPLICATION, so the pointer must name a
            //     container this document may use (409 document_storage_unverified otherwise, before any read).
            await containerResolver.EnsureDocumentPointerContainerAsync(docGuid, document.GraphDriveId, document.GraphItemId, ct);

            // 4. Download file stream from SPE using app-only auth
            var fileStream = await speFileStore.DownloadFileAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                ct);

            if (fileStream == null)
            {
                throw new SdapProblemException(
                    "file_not_found",
                    "File Not Found",
                    $"File content not found in storage for document {documentId}",
                    404
                );
            }

            // 5. Determine content type and filename
            var contentType = document.MimeType ?? "application/octet-stream";
            var fileName = document.FileName ?? $"{documentId}.bin";

            logger.LogInformation(
                "Streaming download | DocumentId: {DocumentId} | FileName: {FileName} | ContentType: {ContentType} | TraceId: {TraceId}",
                documentId, fileName, contentType, context.TraceIdentifier);

            // 6. Return streaming file response with proper headers
            return TypedResults.Stream(
                fileStream,
                contentType: contentType,
                fileDownloadName: fileName,
                enableRangeProcessing: true);
        }

        /// <summary>
        /// GET /api/documents/{documentId}/eml-render
        /// Renders an archived .eml as sanitized, safe-to-display HTML for the reading pane (FR-07 / NFR-03).
        /// Reuses the GetDownload resolution shape (resolve document -> validate SPE pointers ->
        /// SpeFileStore.DownloadFileAsync), then parses with MimeKit (HTML-preserving), rewrites inline cid:
        /// images to data: URIs, and SANITIZES server-side (authoritative XSS boundary). Fails closed:
        /// unauthorized documents are rejected by DocumentAuthorizationFilter (403) and missing documents/files
        /// resolve to 404 — in neither case is any HTML body returned.
        /// </summary>
        static async Task<IResult> GetEmlRender(
            string documentId,
            IDocumentDataverseService dataverseService,
            SpeFileStore speFileStore,
            // uac-r2 task 166 r1 (round 21 item 1b): the pointer's container is verified before the app-only download.
            Sprk.Bff.Api.Infrastructure.Dataverse.RecordContainerResolver containerResolver,
            EmlToHtmlRenderer emlRenderer,
            ILogger<Program> logger,
            HttpContext context,
            CancellationToken ct)
        {
            logger.LogInformation("GetEmlRender called | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            // 1. Validate document ID format
            if (!Guid.TryParse(documentId, out _))
            {
                throw new SdapProblemException(
                    "invalid_id",
                    "Invalid Document ID",
                    $"Document ID '{documentId}' is not a valid GUID format",
                    400
                );
            }

            // 2. Get document entity from Dataverse (includes SPE pointers)
            var document = await dataverseService.GetDocumentAsync(documentId, ct);

            if (document == null)
            {
                throw new SdapProblemException(
                    "document_not_found",
                    "Document Not Found",
                    $"Document with ID '{documentId}' does not exist",
                    404
                );
            }

            // 3. Validate SPE pointers (driveId, itemId)
            ValidateSpePointers(document.GraphDriveId, document.GraphItemId, documentId, document.HasFile);

            // 3b. uac-r2 task 166 r1 (owner round 21 item 1b): the pointer must name a container this document may use.
            await containerResolver.EnsureDocumentPointerContainerAsync(
                Guid.Parse(documentId), document.GraphDriveId, document.GraphItemId, ct);

            // 4. Download the .eml stream from SPE via the EXISTING facade (app-only, per ADR-007) — no new
            //    download method, no GraphServiceClient injection.
            var fileStream = await speFileStore.DownloadFileAsync(
                document.GraphDriveId!,
                document.GraphItemId!,
                ct);

            // 5. Parse + cid:->data: rewrite + sanitize + shape the immutable-cacheable HTML response.
            return await BuildEmlRenderResponseAsync(fileStream, emlRenderer, documentId, ct);
        }
    }

    /// <summary>
    /// Parses the downloaded .eml stream into sanitized HTML and wraps it in a long-lived, immutable-cacheable
    /// response. A null stream (no .eml in SPE) throws a 404 SdapProblemException (the client degrades to
    /// sprk_body) — so NO HTML body is ever returned for a missing archive. Extracted (and internal) so the
    /// HTTP-shaping behavior (404-on-missing, 200 + immutable cache header + sanitized body) is unit-testable
    /// without mocking SpeFileStore/Graph (ADR-038).
    /// </summary>
    internal static async Task<IResult> BuildEmlRenderResponseAsync(
        Stream? emlStream,
        EmlToHtmlRenderer renderer,
        string documentId,
        CancellationToken ct)
    {
        if (emlStream == null)
        {
            throw new SdapProblemException(
                "file_not_found",
                "File Not Found",
                $"File content not found in storage for document {documentId}",
                404
            );
        }

        string sanitizedHtml;
        await using (emlStream)
        {
            sanitizedHtml = await renderer.RenderSanitizedHtmlAsync(emlStream, ct);
        }

        return new SanitizedEmlHtmlResult(sanitizedHtml);
    }

    /// <summary>
    /// IResult that writes sanitized email HTML with a long-lived immutable cache header. The archived .eml is
    /// content-immutable, so repeat opens hit the HTTP cache (spec NFR-01) — no bespoke server-side render cache.
    /// </summary>
    internal sealed class SanitizedEmlHtmlResult : IResult
    {
        // public: browser + shared caches may store the immutable per-document render (task 010 spec header).
        // Long-lived + immutable because the archived .eml never changes (spec NFR-01).
        internal static readonly string CacheControlValue =
            $"public, max-age={EmlToHtmlRenderer.ImmutableMaxAgeSeconds}, immutable";

        private readonly string _html;

        public SanitizedEmlHtmlResult(string html) => _html = html;

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            httpContext.Response.Headers.CacheControl = CacheControlValue;
            await httpContext.Response.WriteAsync(_html, System.Text.Encoding.UTF8, httpContext.RequestAborted);
        }
    }

    /// <summary>
    /// Validates SPE pointer format before calling Graph API.
    /// Throws SdapProblemException for invalid/missing pointers.
    /// DriveId/ItemId presence is the source of truth for whether a file exists in SPE.
    /// sprk_hasfile is a Dataverse-side flag that can be stale (upload completed but flag
    /// never flipped) — use it only to distinguish "never uploaded" (HasFile=false) from
    /// "partial/failed upload" (HasFile=true) when DriveId/ItemId is missing.
    /// </summary>
    /// <summary>Graph link scopes used by the share-link route.</summary>
    private const string AnonymousScope = "anonymous";
    private const string OrganizationScope = "organization";

    /// <summary>
    /// Decides the sharing scope and expiry for one share-link request (unified-access-control-r2
    /// task 072). Extracted from the handler because it is the one piece of genuine POLICY on that
    /// route — it changes for product/compliance reasons, while the rest of the handler changes for SPE
    /// plumbing reasons. Separate reasons-to-change, per docs/standards/COMPONENT-COMPLEXITY.md.
    /// </summary>
    /// <returns>The Graph link scope, and the instant the link stops working.</returns>
    /// <exception cref="SdapProblemException">
    /// 403 when external reach is requested but disabled for this environment.
    /// </exception>
    private static (string Scope, DateTimeOffset ExpiresAt) ResolveShareLinkPolicy(
        ShareLinkRequest? request,
        ShareLinkOptions options,
        TimeProvider timeProvider,
        string documentId,
        ILogger logger,
        HttpContext context)
    {
        var wantsExternalReach = request?.AllowExternalRecipients == true;

        // Refuse rather than downgrade. A silent downgrade to organization scope yields a link that
        // looks fine to the sender and is dead on arrival for the external recipient — the failure
        // mode hardest to diagnose from a support ticket.
        if (wantsExternalReach && !options.AnonymousLinksEnabled)
        {
            logger.LogWarning(
                "CreateShareLink refused an anonymous link: Documents:ShareLinks:AnonymousLinksEnabled "
                + "is false | DocumentId: {DocumentId} | TraceId: {TraceId}",
                documentId, context.TraceIdentifier);

            throw new SdapProblemException(
                "anonymous_links_disabled", "Anonymous Links Disabled",
                "This environment does not permit anyone-with-the-link sharing. Remove "
                + "allowExternalRecipients to mint an organization-scoped link instead.", 403);
        }

        // Scope is organization unless the caller explicitly asked for external reach. The dangerous
        // option must be requested, never inherited from omission.
        var lifetimeDays = wantsExternalReach
            ? options.AnonymousMaxLifetimeDays
            : options.MaxLifetimeDays;

        return (
            wantsExternalReach ? AnonymousScope : OrganizationScope,
            timeProvider.GetUtcNow().AddDays(lifetimeDays));
    }

    private static void ValidateSpePointers(string? driveId, string? itemId, string documentId, bool hasFile)
    {
        // Validate driveId exists
        if (string.IsNullOrWhiteSpace(driveId))
        {
            if (!hasFile)
            {
                throw new SdapProblemException(
                    "no_file_attached",
                    "No File Attached",
                    $"Document {documentId} has no file attached yet (sprk_hasfile=false and sprk_graphdriveid is empty). Upload a file before accessing it.",
                    409
                );
            }

            throw new SdapProblemException(
                "mapping_missing_drive",
                "SPE Drive ID Missing",
                $"Document {documentId} is marked as having a file (sprk_hasfile=true) but the Graph Drive ID is empty. " +
                $"The upload may still be in progress or did not complete successfully.",
                409
            );
        }

        // Validate driveId format (SharePoint Embedded drives always start with "b!")
        if (!driveId.StartsWith("b!", StringComparison.Ordinal))
        {
            throw new SdapProblemException(
                "invalid_drive_id",
                "Invalid SPE Drive ID Format",
                $"Drive ID '{driveId}' does not start with 'b!' (expected SharePoint Embedded container format)",
                400
            );
        }

        // Validate itemId exists (same hasFile distinction as for DriveId)
        if (string.IsNullOrWhiteSpace(itemId))
        {
            if (!hasFile)
            {
                throw new SdapProblemException(
                    "no_file_attached",
                    "No File Attached",
                    $"Document {documentId} has no file attached yet (sprk_hasfile=false and sprk_graphitemid is empty). Upload a file before accessing it.",
                    409
                );
            }

            throw new SdapProblemException(
                "mapping_missing_item",
                "SPE Item ID Missing",
                $"Document {documentId} is marked as having a file (sprk_hasfile=true) but the Graph Item ID is empty. " +
                $"The upload may still be in progress or did not complete successfully.",
                409
            );
        }

        // Validate itemId length (SharePoint item IDs are typically 20+ characters)
        if (itemId.Length < 20)
        {
            throw new SdapProblemException(
                "invalid_item_id",
                "Invalid SPE Item ID Format",
                $"Item ID '{itemId}' is too short (expected at least 20 characters)",
                400
            );
        }
    }
}
