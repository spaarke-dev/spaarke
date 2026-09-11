using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// FR-01 (spaarkeai-word-add-in-r1 task 012): which <c>sprk_document</c>, if any, does an open document's URL
/// denote? <c>Office.context.document.url</c> → Graph <c>/shares/u!{base64url}/driveItem</c> (as the caller) →
/// <c>driveId</c> + <c>itemId</c> → <c>sprk_document</c> via the <c>sprk_graphitemid_uk</c> alternate key.
/// </summary>
/// <remarks>
/// <para><b>Three answers, never two.</b> Resolved; not a Spaarke document (a definitive "no" the pane treats as a
/// new document); or INDETERMINATE, which surfaces as 503. Collapsing the third into the second is the failure this
/// type exists to prevent: a Spaarke document misreported as new is saved as new, and the user gets a second
/// <c>sprk_document</c> row instead of a version — the UAT complaint FR-01 was written for. So a Dataverse fault is
/// classified by error code rather than treated as not-found, a broken alternate key is reported rather than worked
/// around, and a Graph refusal of a file the caller has open is reported rather than read as "not ours".</para>
/// <para><b>Lookup semantics copied from <c>ComposeRecordResolution.TryFindDocumentByGraphItemIdAsync</c> — except
/// its #781 self-heal.</b> That member is internal to a collaborator built inside <c>ComposeService</c>, and the
/// Office path takes no code dependency on Compose (see <c>OfficeDocumentPersistence</c>'s header). The
/// alternate-key retrieve on the raw item id is copied. The #781 column-query fallback is NOT: it picks one row out of
/// a duplicated set, which is the "tolerant secondary lookup" NFR-07 and this task's escalation trigger forbid. When
/// the key cannot answer, this route says so (503) rather than guessing; Compose's save path heals such rows on
/// touch.</para>
/// <para>Public: it has its own contract and is tested through it (ADR-038 B8). Stateless, with its collaborators
/// passed in: no DI registration (ADR-010).</para>
/// </remarks>
public static class DocumentUrlIdentityResolution
{
    /// <summary>Well above any real SharePoint URL (whose path is capped at 400 characters); bounds the input.</summary>
    public const int MaxDocumentUrlLength = 4096;

    /// <summary>A file on this machine. The pane treats the document as new.</summary>
    public const string ReasonNotCloudDocument = "not_cloud_document";

    /// <summary>Graph found nothing at this URL. The pane treats the document as new.</summary>
    public const string ReasonNotResolvable = "not_resolvable";

    /// <summary>The file exists but no <c>sprk_document</c> tracks it. The pane treats the document as new.</summary>
    public const string ReasonNotSpaarkeDocument = "not_spaarke_document";

    /// <summary>
    /// A <c>sprk_document</c> holds this drive-item id but records a DIFFERENT drive. Not resolved — and NOT a new
    /// document either: a save-as-new would collide with <c>sprk_graphitemid_uk</c>. The pane must not offer it.
    /// </summary>
    public const string ReasonIdentityConflict = "identity_conflict";

    // Declared locally, as OfficeDocumentPersistence does, so this path carries no dependency on Compose internals.
    private const string DocumentLogicalName = "sprk_document";
    private const string GraphItemIdAttribute = "sprk_graphitemid";
    private const string GraphDriveIdAttribute = "sprk_graphdriveid";
    private const string DocumentNameAttribute = "sprk_documentname";
    private const string FileNameAttribute = "sprk_filename";

    /// <summary>
    /// The DIRECT association slots, in the regarding priority of the polymorphic-resolver pattern (matter &gt;
    /// project &gt; invoice &gt; work assignment). The Office save path writes only this family; the
    /// <c>sprk_related*</c> family belongs to task 026's two-slot record card.
    /// </summary>
    private static readonly string[] RelatedRecordAttributes =
        { "sprk_matter", "sprk_project", "sprk_invoice", "sprk_workassignment" };

    private static readonly string[] LookupColumns =
        new[] { "sprk_documentid", GraphDriveIdAttribute, DocumentNameAttribute, FileNameAttribute }
            .Concat(RelatedRecordAttributes)
            .ToArray();

    /// <summary>A resolved identity. Carries metadata the caller must not emit until authorization has allowed it.</summary>
    public sealed record Resolution(Guid DocumentId, string? DocumentName, string? FileName, EntityReference? RelatedRecord);

    /// <summary>Exactly one of the two is set: an identity, or the reason there is none.</summary>
    public sealed record Result(Resolution? Identity, string? NoIdentityReason)
    {
        public static Result None(string reason) => new(null, reason);
    }

    /// <summary>
    /// Validates the caller's URL. Empty, over-long or not an absolute URI → 400 ProblemDetails. A local path as a
    /// <c>file:</c> URI passes — it is a desktop file, answered as <see cref="ReasonNotCloudDocument"/>, not a
    /// malformed request.
    /// </summary>
    public static Uri ParseDocumentUrl(string? documentUrl)
    {
        if (string.IsNullOrWhiteSpace(documentUrl))
        {
            throw new SdapProblemException(
                "document_url_required", "Document URL Required",
                "documentUrl is required.", 400);
        }

        var trimmed = documentUrl.Trim();
        if (trimmed.Length > MaxDocumentUrlLength)
        {
            throw new SdapProblemException(
                "document_url_too_long", "Document URL Too Long",
                $"documentUrl exceeds {MaxDocumentUrlLength} characters.", 400);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new SdapProblemException(
                "document_url_malformed", "Malformed Document URL",
                "documentUrl is not an absolute URL.", 400);
        }

        return uri;
    }

    public static async Task<Result> ResolveAsync(
        Uri documentUrl,
        HttpContext httpContext,
        SpeFileStore speFileStore,
        IGenericEntityService dataverse,
        ILogger logger,
        CancellationToken ct)
    {
        // A file:// (or any non-web) URL is a document that lives on this machine. Graph has nothing to say.
        if (documentUrl.Scheme != Uri.UriSchemeHttps && documentUrl.Scheme != Uri.UriSchemeHttp)
            return Result.None(ReasonNotCloudDocument);

        var shared = await speFileStore.ResolveSharedItemAsUserAsync(httpContext, documentUrl, ct);
        switch (shared.Outcome)
        {
            case SpeSharedItemOutcome.NotFound:
                return Result.None(ReasonNotResolvable);

            case SpeSharedItemOutcome.AccessDenied:
                // The caller has this file open in Word, yet Graph refuses it to them through this app. That almost
                // always means the environment's SPE container-type registration or consent is incomplete — and
                // answering "not a Spaarke document" would then make the pane save EVERY Spaarke document as new.
                logger.LogWarning(
                    "Document identity: Graph refused /shares for host {Host} to this caller. If users can open the " +
                    "file in Word, check the BFF app's SharePoint Embedded container-type registration and consent.",
                    documentUrl.Host);
                throw new SdapProblemException(
                    "identity_resolution_access_denied", "Document Identity Unavailable",
                    "Spaarke could not look this document up with your access. If you can open it in Word, the " +
                    "environment's SharePoint Embedded registration may be incomplete.", 503);

            case SpeSharedItemOutcome.Unavailable:
                throw Unavailable("Microsoft Graph could not be reached to identify this document. Try again.");
        }

        var row = await FindDocumentByGraphItemIdAsync(shared.ItemId!, dataverse, logger, ct);
        if (row is null)
            return Result.None(ReasonNotSpaarkeDocument);

        // SPIKE-1 link 3: sprk_graphitemid_uk keys on the item id ALONE (DocumentVersionEndpoints.cs:59-61), so the
        // drive is corroborated here. A recorded drive that differs means the row names some other file.
        var recordedDriveId = row.GetAttributeValue<string>(GraphDriveIdAttribute);
        if (!string.IsNullOrEmpty(recordedDriveId)
            && !string.Equals(recordedDriveId, shared.DriveId, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Document identity: drive-item {ItemId} matched sprk_document {DocumentId} by item id, but the row " +
                "records drive {RecordedDriveId} and Graph resolved drive {ResolvedDriveId}. Treated as NOT resolved.",
                shared.ItemId, row.Id, recordedDriveId, shared.DriveId);
            return Result.None(ReasonIdentityConflict);
        }

        if (string.IsNullOrEmpty(recordedDriveId))
        {
            // Every current writer stamps the drive; a row without one predates that. The item id is the key the
            // whole platform binds on (NFR-07), so the row still resolves — but the drive went unchecked.
            logger.LogWarning(
                "Document identity: sprk_document {DocumentId} has no sprk_graphdriveid; drive {ResolvedDriveId} " +
                "could not be corroborated. Resolving on the item id alone.",
                row.Id, shared.DriveId);
        }

        return new Result(
            new Resolution(
                row.Id,
                row.GetAttributeValue<string>(DocumentNameAttribute),
                row.GetAttributeValue<string>(FileNameAttribute),
                FirstRelatedRecord(row)),
            null);
    }

    /// <summary>The first populated direct association slot, in <see cref="RelatedRecordAttributes"/> order.</summary>
    private static EntityReference? FirstRelatedRecord(Entity row)
        => RelatedRecordAttributes
            .Select(row.GetAttributeValue<EntityReference>)
            .FirstOrDefault(r => r is not null && r.Id != Guid.Empty);

    /// <summary>
    /// <c>sprk_document</c> by SPE drive-item id through <c>sprk_graphitemid_uk</c>, or <c>null</c> when no row
    /// carries it. Throws 503 when Dataverse cannot answer — including when the alternate key itself is unhealthy.
    /// </summary>
    private static async Task<Entity?> FindDocumentByGraphItemIdAsync(
        string graphItemId,
        IGenericEntityService dataverse,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // The RAW item id. sprk_graphitemid is an opaque SPE drive-item id — a STRING, not a GUID — so the match
            // is exact-string and ADR-044 GUID canonicalization does NOT apply (ComposeCreateOnSavePromoter.cs:336-339).
            return await dataverse.RetrieveByAlternateKeyAsync(
                DocumentLogicalName,
                new KeyAttributeCollection { { GraphItemIdAttribute, graphItemId } },
                LookupColumns,
                ct);
        }
        catch (InvalidOperationException ex) when (IsAlternateKeyNotFound(ex))
        {
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // DataverseServiceClientImpl wraps a cancellation too; the caller's cancellation is not an outage.
            ct.ThrowIfCancellationRequested();

            // Deliberately no column-query fallback (see the type's remarks): a duplicated or not-Active key is
            // "cannot determine", never a guessed row.
            logger.LogWarning(ex,
                "Document identity: Dataverse could not answer for drive-item {ItemId}; reporting unavailable, NOT " +
                "absent. If this persists, check sprk_graphitemid_uk (scripts/Verify-ComposeIdentityKey.ps1).",
                graphItemId);
            throw Unavailable("Dataverse could not be reached to identify this document. Try again.");
        }
    }

    /// <summary>
    /// Whether an alternate-key retrieve failed because the row does not exist. <c>DataverseServiceClientImpl</c>
    /// wraps EVERY failure — absent row, broken key, outage — in one <see cref="InvalidOperationException"/>, so the
    /// cause is read off the inner chain: the ObjectDoesNotExist fault code (locale-independent, see
    /// <see cref="RecordContainerResolver.IsRecordNotFound"/>), or the message that client throws itself when the
    /// retrieve returns no entity. That message is our own literal, not a localized Dataverse string.
    /// </summary>
    private static bool IsAlternateKeyNotFound(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(current))
                return true;

            if (current.Message.Contains("not found with provided alternate key values", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static SdapProblemException Unavailable(string detail)
        => new("identity_resolution_unavailable", "Document Identity Unavailable", detail, 503);
}
