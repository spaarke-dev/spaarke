using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// FR-C4 (email-communication-intelligence-r2, task 025): the capture-vs-upload cross-path bridge. A mailbox-captured
/// email becomes a <c>sprk_communication</c>; a user "Save to Spaarke" of the SAME email's <c>.eml</c> archive lands as
/// a <c>sprk_document</c>. Keyed on the shared internet-message-id (the canonical identity task 021 establishes), the
/// two representations are LINKED — not duplicated — so the reconciliation/review surface (Pillar E) resolves them to
/// ONE email.
/// </summary>
/// <remarks>
/// <para>The link is a single-valued lookup ON THE DOCUMENT (<see cref="LinkedCommunicationAttribute"/>): the document
/// is the dependent representation; the communication stays the canonical reconciliation unit (ADR-045 — extend, not
/// fork; NOT a new <c>sprk_reconciliation</c> entity). N:1 (a document points at its one communication); Pillar E finds
/// a communication's archive via the reverse relationship. This is the message↔document bridge — distinct from task 021
/// (message↔message dedup) and task 024 (file↔file content dedup).</para>
/// <para>Direction-agnostic: BOTH arrival orders converge on the same write (set <see cref="LinkedCommunicationAttribute"/>
/// on the document). Capture-then-upload links from the office side (<c>OfficeDocumentPersistence</c>, the canonical
/// already exists); upload-then-capture links from the capture side (<c>IncomingCommunicationProcessor</c>, the archive
/// document already exists — found via <see cref="FindAndLinkArchiveDocumentsAsync"/>).</para>
/// <para>Idempotent: the link is only written when the document is not already linked to this communication (a
/// single-valued lookup cannot duplicate; re-processing / SB redelivery is a no-op). Best-effort / non-fatal (NFR-04):
/// every failure logs and degrades — the link is written via the generic seam (NOT the atomic document build), so
/// capture/upload NEVER fails on the gated <c>sprk_linkedcommunication</c> column being absent before its schema deploy
/// (contract-first — mirrors task 022 <see cref="DeliveryContextMerge"/>).</para>
/// </remarks>
public static class CrossPathLink
{
    private const string DocumentLogicalName = "sprk_document";
    private const string CommunicationLogicalName = "sprk_communication";

    /// <summary>The document → communication cross-path lookup (FR-C4). Gated schema, managed solution (ADR-027).</summary>
    public const string LinkedCommunicationAttribute = "sprk_linkedcommunication";

    /// <summary>Email Message-ID header (RFC 5322) stored on the archive document — the shared join key.</summary>
    public const string EmailMessageIdAttribute = "sprk_emailmessageid";

    /// <summary>Marks the main <c>.eml</c> archive document (not an attachment child).</summary>
    public const string IsEmailArchiveAttribute = "sprk_isemailarchive";

    private const string DocumentIdAttribute = "sprk_documentid";

    /// <summary>
    /// Links <paramref name="documentId"/> to <paramref name="communicationId"/> by setting the document's
    /// <see cref="LinkedCommunicationAttribute"/> lookup — but only when it is not ALREADY linked to that same
    /// communication (idempotent; a single-valued lookup cannot double-link, so re-processing is a no-op).
    /// Best-effort / non-fatal (NFR-04): any read/write failure logs and returns <c>false</c> without throwing.
    /// Returns <c>true</c> iff a link was written.
    /// </summary>
    public static async Task<bool> LinkDocumentToCommunicationAsync(
        IGenericEntityService dataverse,
        Guid documentId,
        Guid communicationId,
        ILogger logger,
        CancellationToken ct)
    {
        if (documentId == Guid.Empty || communicationId == Guid.Empty)
            return false;

        try
        {
            var row = await dataverse
                .RetrieveAsync(DocumentLogicalName, documentId, new[] { LinkedCommunicationAttribute }, ct)
                .ConfigureAwait(false);

            // Idempotent: already linked to THIS communication → no write.
            var current = row?.GetAttributeValue<EntityReference>(LinkedCommunicationAttribute);
            if (current is not null && current.Id == communicationId)
                return false;

            await dataverse.UpdateAsync(
                    DocumentLogicalName,
                    documentId,
                    new Dictionary<string, object>
                    {
                        [LinkedCommunicationAttribute] = new EntityReference(CommunicationLogicalName, communicationId)
                    },
                    ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "FR-C4 cross-path link failed (non-fatal) for document {DocumentId} → communication {CommunicationId}.",
                documentId, communicationId);
            return false;
        }
    }

    /// <summary>
    /// Capture-side entry (upload-then-capture): finds any pre-existing email-archive <c>sprk_document</c> whose
    /// <see cref="EmailMessageIdAttribute"/> matches <paramref name="internetMessageId"/> and links each to
    /// <paramref name="communicationId"/>. A no-op when no archive document was uploaded before capture (the common
    /// capture-then-upload order is handled from the office side instead). Best-effort / non-fatal (NFR-04): a query
    /// failure logs and returns <c>0</c>. Returns the number of documents newly linked.
    /// </summary>
    public static async Task<int> FindAndLinkArchiveDocumentsAsync(
        IGenericEntityService dataverse,
        string? internetMessageId,
        Guid communicationId,
        ILogger logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(internetMessageId) || communicationId == Guid.Empty)
            return 0;

        EntityCollection matches;
        try
        {
            var query = new QueryExpression(DocumentLogicalName)
            {
                ColumnSet = new ColumnSet(DocumentIdAttribute, LinkedCommunicationAttribute),
                TopCount = 20
            };
            query.Criteria.AddCondition(EmailMessageIdAttribute, ConditionOperator.Equal, internetMessageId);
            query.Criteria.AddCondition(IsEmailArchiveAttribute, ConditionOperator.Equal, true);

            matches = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "FR-C4 archive-document lookup failed (non-fatal) for message {MessageId}.", internetMessageId);
            return 0;
        }

        if (matches?.Entities is not { Count: > 0 } rows)
            return 0;

        var linked = 0;
        foreach (var row in rows)
        {
            // Idempotent guard inline (avoids a redundant re-read in LinkDocument…): skip if already linked here.
            var current = row.GetAttributeValue<EntityReference>(LinkedCommunicationAttribute);
            if (current is not null && current.Id == communicationId)
                continue;

            try
            {
                await dataverse.UpdateAsync(
                        DocumentLogicalName,
                        row.Id,
                        new Dictionary<string, object>
                        {
                            [LinkedCommunicationAttribute] =
                                new EntityReference(CommunicationLogicalName, communicationId)
                        },
                        ct)
                    .ConfigureAwait(false);
                linked++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "FR-C4 cross-path link failed (non-fatal) for archive document {DocumentId} → communication {CommunicationId}.",
                    row.Id, communicationId);
            }
        }

        return linked;
    }
}
