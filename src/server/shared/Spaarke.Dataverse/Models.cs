namespace Spaarke.Dataverse;

/// <summary>
/// Request model for creating a new document
/// </summary>
public class CreateDocumentRequest
{
    public required string Name { get; set; }
    public required string ContainerId { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// The ONE place that maps a caller-supplied association type onto an
/// <see cref="UpdateDocumentRequest"/>'s lookup field.
/// </summary>
/// <remarks>
/// <para>Created 2026-09-03 (unified-access-control-r2 item 7). It replaces <b>four</b> hand-written
/// copies of the same switch that had already drifted apart:</para>
/// <list type="bullet">
///   <item><c>UploadFinalizationWorker.ApplyAssociationLookup</c> — accepted <b>only friendly</b>
///     names ("matter"), warn-and-continue on a miss</item>
///   <item><c>OfficeDocumentPersistence</c> — accepted friendly <b>and</b> logical, warn-and-continue</item>
///   <item><c>EmailAttachmentProcessor</c> — accepted <b>only logical</b> names ("sprk_matter"),
///     warn-and-continue</item>
///   <item><c>RecordMatchEndpoints</c> — only logical, and the <b>only</b> one that failed closed</item>
/// </list>
/// <para>The drift was the defect: the same association token silently dropped in one path and
/// applied in another purely because of which spelling that copy happened to list. This map accepts
/// <b>both</b> spellings for every supported type, so a caller cannot lose an association by picking
/// the "wrong" form.</para>
///
/// <para><b>Supported types are exactly those with a real lookup column on <c>sprk_document</c></b>,
/// verified against live Dataverse metadata 2026-09-03: <c>sprk_matter</c>, <c>sprk_project</c>,
/// <c>sprk_invoice</c>, <c>sprk_workassignment</c>, <c>sprk_event</c>. Do not add a case here without
/// confirming the column exists — a case that sets a property no column backs produces a Dataverse
/// write error, and one that is missing produces a silently unassociated document.</para>
///
/// <para>⚠️ <b>Known gaps — deliberately NOT mapped, because the column does not exist:</b>
/// <c>account</c>, <c>contact</c> and <c>sprk_todo</c>. `account`/`contact` are nonetheless still
/// ACCEPTED by the Office save endpoint's own allow-list, so a save filed to one of them creates an
/// unassociated document today. That is a live behaviour gap needing an owner decision (add the
/// columns, or reject the type at the endpoint) — it is recorded rather than silently changed here,
/// because rejecting would alter a user-visible flow. <c>sprk_todo</c> was assumed mappable by the
/// Q4-widening note; it is not, for the same reason.</para>
/// </remarks>
public static class DocumentAssociationMap
{
    /// <summary>
    /// Apply <paramref name="recordId"/> to the lookup matching <paramref name="entityTypeOrAlias"/>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the association was applied; <see langword="false"/> when the type
    /// is not one this codebase can associate a document to. A <see langword="false"/> return is the
    /// caller's decision to make — a background worker logs and continues, a request handler should
    /// reject — but it must never be ignored, or the document is created unassociated.
    /// </returns>
    public static bool TryApply(UpdateDocumentRequest request, string? entityTypeOrAlias, Guid? recordId)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!recordId.HasValue || recordId.Value == Guid.Empty || string.IsNullOrWhiteSpace(entityTypeOrAlias))
            return false;

        switch (entityTypeOrAlias.Trim().ToLowerInvariant())
        {
            case "matter":
            case "sprk_matter":
                request.MatterLookup = recordId;
                return true;
            case "project":
            case "sprk_project":
                request.ProjectLookup = recordId;
                return true;
            case "invoice":
            case "sprk_invoice":
                request.InvoiceLookup = recordId;
                return true;
            case "workassignment":
            case "sprk_workassignment":
                request.WorkAssignmentLookup = recordId;
                return true;
            case "event":
            case "sprk_event":
                request.EventLookup = recordId;
                return true;
            // Added 2026-09-04 (unified-access-control-r2). Both columns EXIST and always did; the
            // 2026-09-03 metadata check missed them because it enumerated only the bare `sprk_{type}`
            // family and never looked at `sprk_related*`. See UpdateDocumentRequest.TodoLookup /
            // ContactLookup for the queries that prove it.
            case "todo":
            case "sprk_todo":
                request.TodoLookup = recordId;
                return true;
            case "contact":
                request.ContactLookup = recordId;
                return true;
            // ⚠️ `account` is deliberately ABSENT and must stay absent (owner decision, 2026-09-04).
            // `sprk_document` has NO account lookup in EITHER family, so a save filed to an account
            // could only ever land unassociated — the user believes it filed and it did not. The type
            // was removed from the Office endpoint's allow-list and from AssociationType rather than
            // being accepted-and-dropped. Spaarke's organization analogue is `sprk_organization`
            // (`sprk_relatedorganization` / `sprk_relatedvendororg` both exist on sprk_document); if
            // "file to an organization" is wanted, add THAT — do not re-add `account`.
            default:
                return false;
        }
    }
}

/// <summary>
/// One <c>sprk_document</c> record-link lookup. See <see cref="DocumentLinkFields.All"/>.
/// </summary>
/// <param name="LogicalName">
/// Always lowercase. Safe for SDK-based access — <c>ColumnSet</c>, <c>QueryExpression</c>, and the
/// <c>Microsoft.Xrm.Sdk.Entity</c> indexer are all logical-name-keyed, which is the ONLY access pattern
/// either current consumer uses (both go through the SDK-based <see cref="IGenericEntityService"/>).
/// </param>
/// <param name="SchemaName">
/// Case-SENSITIVE. Required ONLY for a Web API <c>@odata.bind</c> navigation property — NOT used by
/// either current consumer, carried here so a FUTURE Web-API-based consumer has a pinned, verified value
/// instead of deriving one by convention. See <see cref="DocumentLinkFields"/> remarks for why that
/// convention is unsafe.
/// </param>
/// <param name="TargetEntityLogicalName">The entity this lookup points at.</param>
public sealed record DocumentLinkField(string LogicalName, string SchemaName, string TargetEntityLogicalName);

/// <summary>
/// The ONE enumeration of every <c>sprk_document</c> record-link lookup — the READ-side counterpart to
/// <see cref="DocumentAssociationMap"/> immediately above, hoisted here so the two cannot drift apart
/// (unified-access-control-r2, 2026-09-05).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Two independent copies of this exact list lived in <c>Sprk.Bff.Api</c> —
/// <c>AttachmentDocumentAssociationRung.DocumentLinkFields</c> (surfaces a document's OWN record links as
/// association-candidate suggestions for an incoming communication) and
/// <c>ComposeService.DocumentAssociationLookupAttributes</c> (a PDF-sourced Compose create-on-save copies
/// these onto the new Word document so the two file alongside each other) — and the latter's own comment
/// conceded it was "the SAME closed set". Root <c>CLAUDE.md</c> §11 forbids a third copy; this is the one
/// home both now consume.
/// </para>
/// <para>
/// <b>Both prior copies were INCOMPLETE</b> — neither listed <c>sprk_relatedinvoice</c> or
/// <c>sprk_relatedworkassignment</c>, nor the further six columns enumerated below
/// (<c>sprk_relatedagreement</c>, <c>sprk_relatedcommunication</c>, <c>sprk_relatedcontact</c>,
/// <c>sprk_relatedorganization</c>, <c>sprk_relatedservicerequest</c>, <c>sprk_relatedtodo</c>,
/// <c>sprk_relatedvendororg</c>). A document linked ONLY through an omitted column was invisible to every
/// consumer of the old lists.
/// </para>
/// <para>
/// <b>Every entry below is verified against LIVE Dataverse metadata</b> (<c>spaarkedev1</c>, 2026-09-05:
/// <c>GET .../api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')/Attributes</c>, filtered to
/// <c>AttributeType eq 'Lookup'</c>) — not carried forward from any prior written list. Three separate
/// prior records in this repo were wrong about exactly these columns; see
/// <c>projects/unified-access-control-r2/notes/document-link-vocabulary-hoist.md</c> for the query and the
/// raw response.
/// </para>
/// <para>
/// 🔴 <b>THE <c>sprk_related*</c> SCHEMA NAMES ARE NOT UNIFORMLY CASED</b>, and neither are the 4
/// "primary" (non-<c>related</c>) columns'. <see cref="DocumentLinkField.SchemaName"/> is case-SENSITIVE
/// (required for a Web API <c>@odata.bind</c> navigation property) and is UNRELATED to
/// <see cref="DocumentLinkField.LogicalName"/> (always lowercase). <b>Never derive a schema name by
/// convention</b> — <c>$"sprk_Related{type}"</c> silently produces the WRONG value for 3 of the 12
/// <c>related</c> columns:
/// </para>
/// <list type="bullet">
///   <item><description>PascalCase (9): <c>sprk_RelatedAgreement</c>, <c>sprk_RelatedCommunication</c>,
///     <c>sprk_RelatedContact</c>, <c>sprk_RelatedInvoice</c>, <c>sprk_RelatedOrganization</c>,
///     <c>sprk_RelatedServiceRequest</c>, <c>sprk_RelatedToDo</c>, <c>sprk_RelatedWorkAssignment</c>,
///     <c>sprk_RelatedEvent</c>.</description></item>
///   <item><description>lowercase — the trap, since a convention-based builder gets these WRONG:
///     <c>sprk_relatedmatter</c>, <c>sprk_relatedproject</c>, <c>sprk_relatedvendororg</c>.</description></item>
///   <item><description>The 4 primary columns are PascalCase despite their plain lowercase logical
///     names: <c>sprk_Matter</c>, <c>sprk_Project</c>, <c>sprk_Invoice</c>,
///     <c>sprk_WorkAssignment</c>.</description></item>
/// </list>
/// <para>
/// <b>"Related" targets the SAME entity as its primary counterpart</b> — "a related matter is still a
/// matter" (061 UAT round-2, the type-agnostic design principle this list follows).
/// <c>sprk_relatedcontact</c> targets the OOB <c>contact</c> table; <c>sprk_relatedorganization</c> AND
/// <c>sprk_relatedvendororg</c> BOTH target <c>sprk_organization</c> — there is no separate "vendor org"
/// entity in Spaarke's model.
/// </para>
/// <para>
/// <b>Scope note.</b> Not every target entity here has a <c>sprk_communication</c> regarding field in the
/// BFF-layer <c>RegardingFieldMap</c> (<c>Sprk.Bff.Api.Services.Communication.Engine</c>) —
/// <c>sprk_agreement</c>, <c>sprk_communication</c>, and <c>sprk_todo</c> are absent from that map today.
/// This is harmless for <c>AttachmentDocumentAssociationRung</c> (it already soft-skips a link whose
/// target has no regarding field) but means those three targets are not yet surfaced as association
/// candidates by that rung even though they now appear here. <c>ComposeCreateOnSavePromoter</c>'s
/// link-inheritance copy does not go through <c>RegardingFieldMap</c> at all, so it is unaffected.
/// Widening <c>RegardingFieldMap</c> is a separate decision, out of scope for this hoist.
/// </para>
/// </remarks>
public static class DocumentLinkFields
{
    public static readonly IReadOnlyList<DocumentLinkField> All =
    [
        new("sprk_matter", "sprk_Matter", "sprk_matter"),
        new("sprk_relatedmatter", "sprk_relatedmatter", "sprk_matter"),
        new("sprk_project", "sprk_Project", "sprk_project"),
        new("sprk_relatedproject", "sprk_relatedproject", "sprk_project"),
        new("sprk_invoice", "sprk_Invoice", "sprk_invoice"),
        new("sprk_relatedinvoice", "sprk_RelatedInvoice", "sprk_invoice"),
        new("sprk_workassignment", "sprk_WorkAssignment", "sprk_workassignment"),
        new("sprk_relatedworkassignment", "sprk_RelatedWorkAssignment", "sprk_workassignment"),
        new("sprk_relatedagreement", "sprk_RelatedAgreement", "sprk_agreement"),
        new("sprk_relatedcommunication", "sprk_RelatedCommunication", "sprk_communication"),
        new("sprk_relatedcontact", "sprk_RelatedContact", "contact"),
        new("sprk_relatedevent", "sprk_RelatedEvent", "sprk_event"),
        new("sprk_relatedorganization", "sprk_RelatedOrganization", "sprk_organization"),
        new("sprk_relatedservicerequest", "sprk_RelatedServiceRequest", "sprk_servicerequest"),
        new("sprk_relatedtodo", "sprk_RelatedToDo", "sprk_todo"),
        new("sprk_relatedvendororg", "sprk_relatedvendororg", "sprk_organization"),
    ];

    /// <summary>Logical names only, in the same order as <see cref="All"/> — the shape a <c>ColumnSet</c>
    /// or an <see cref="IGenericEntityService.RetrieveAsync"/> <c>columns</c> argument needs.</summary>
    public static readonly IReadOnlyList<string> LogicalNames = All.Select(f => f.LogicalName).ToArray();
}

/// <summary>
/// Request model for updating an existing document
/// </summary>
public class UpdateDocumentRequest
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Basic Document Properties
    // ═══════════════════════════════════════════════════════════════════════════
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? GraphItemId { get; set; }
    public string? GraphDriveId { get; set; }

    /// <summary>SharePoint file URL (enables "Open in SharePoint" links). Maps to sprk_filepath.</summary>
    public string? FilePath { get; set; }

    /// <summary>File extension without dot, lowercase (e.g. "pdf", "docx", "xlsx"). Maps to sprk_filetype.</summary>
    public string? FileType { get; set; }

    public bool? HasFile { get; set; }
    public DocumentStatus? Status { get; set; }

    /// <summary>
    /// SPE <c>quickXorHash</c> content-identity fingerprint (FR-C3 Tier-1 exact-dedup key). Maps to the
    /// indexed <c>sprk_canonicalhash</c> column (task 023). Written on document create so a later byte-identical
    /// upload (any path) can be detected as a duplicate. Null when the hash was unavailable at persist time.
    /// </summary>
    public string? CanonicalHash { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // AI Analysis Fields
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>AI-generated summary of the document.</summary>
    public string? Summary { get; set; }

    /// <summary>AI-generated TL;DR bullet points (newline-separated).</summary>
    public string? TlDr { get; set; }

    /// <summary>AI-extracted keywords for search (comma-separated).</summary>
    public string? Keywords { get; set; }

    /// <summary>AI analysis status OptionSet values (Dataverse 100000000+ range):
    /// None=100000000, Pending=100000001, Completed=100000002, OptedOut=100000003,
    /// Failed=100000004, NotSupported=100000005, Skipped=100000006.</summary>
    public int? SummaryStatus { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Extracted Entities Fields
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>AI-extracted organization names (newline-separated).</summary>
    public string? ExtractOrganization { get; set; }

    /// <summary>AI-extracted person names (newline-separated).</summary>
    public string? ExtractPeople { get; set; }

    /// <summary>AI-extracted monetary amounts (newline-separated).</summary>
    public string? ExtractFees { get; set; }

    /// <summary>AI-extracted dates (newline-separated).</summary>
    public string? ExtractDates { get; set; }

    /// <summary>AI-extracted reference numbers (newline-separated).</summary>
    public string? ExtractReference { get; set; }

    /// <summary>AI-classified document type (raw text value).</summary>
    public string? ExtractDocumentType { get; set; }

    /// <summary>Document type choice field value (mapped from AI classification).</summary>
    public int? DocumentType { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Email Metadata Fields (for .eml and .msg files)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Email subject line. Maps to sprk_EmailSubject.</summary>
    public string? EmailSubject { get; set; }

    /// <summary>Email sender address(es). Maps to sprk_EmailFrom.</summary>
    public string? EmailFrom { get; set; }

    /// <summary>Email recipient address(es). Maps to sprk_EmailTo.</summary>
    public string? EmailTo { get; set; }

    /// <summary>Email sent date/time. Maps to sprk_EmailDate.</summary>
    public DateTime? EmailDate { get; set; }

    /// <summary>Email body content (truncated to 10K chars). Maps to sprk_EmailBody.</summary>
    public string? EmailBody { get; set; }

    /// <summary>Email CC recipients. Maps to sprk_EmailCc (if field exists).</summary>
    public string? EmailCc { get; set; }

    /// <summary>Email Message-ID header (RFC 5322). Maps to sprk_EmailMessageId.</summary>
    public string? EmailMessageId { get; set; }

    /// <summary>Email direction choice value: Received=100000000, Sent=100000001. Maps to sprk_EmailDirection.</summary>
    public int? EmailDirection { get; set; }

    /// <summary>Email tracking token. Maps to sprk_EmailTrackingToken.</summary>
    public string? EmailTrackingToken { get; set; }

    /// <summary>Email conversation index. Maps to sprk_EmailConversationIndex.</summary>
    public string? EmailConversationIndex { get; set; }

    /// <summary>Email activity lookup. Maps to sprk_Email@odata.bind.</summary>
    public Guid? EmailLookup { get; set; }

    /// <summary>Is this document an email archive (.eml). Maps to sprk_IsEmailArchive.</summary>
    public bool? IsEmailArchive { get; set; }

    /// <summary>Relationship type choice value: Email Attachment=100000000. Maps to sprk_RelationshipType.</summary>
    public int? RelationshipType { get; set; }

    /// <summary>JSON array of attachment metadata. Maps to sprk_Attachments.</summary>
    public string? Attachments { get; set; }

    /// <summary>
    /// DEPRECATED: This field does not exist in Dataverse schema.
    /// Use ParentDocumentLookup instead (sets sprk_ParentDocument lookup via @odata.bind).
    /// </summary>
    [Obsolete("Use ParentDocumentLookup instead - sprk_parentdocumentid does not exist in Dataverse")]
    public string? ParentDocumentId { get; set; }

    /// <summary>Parent document lookup. Maps to sprk_ParentDocument@odata.bind.</summary>
    public Guid? ParentDocumentLookup { get; set; }

    /// <summary>Parent file name (for attachments). Maps to sprk_ParentFileName.</summary>
    public string? ParentFileName { get; set; }

    /// <summary>Parent Graph item ID (for attachments). Maps to sprk_ParentGraphItemId.</summary>
    public string? ParentGraphItemId { get; set; }

    /// <summary>Parent email's internetMessageId (for attachments from emails). Maps to sprk_emailparentid.</summary>
    public string? EmailParentId { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Record Association Lookups (Phase 2 - Record Matching)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Matter lookup (sprk_matter). Maps to sprk_Matter@odata.bind.</summary>
    public Guid? MatterLookup { get; set; }

    /// <summary>Project lookup (sprk_project). Maps to sprk_Project@odata.bind.</summary>
    public Guid? ProjectLookup { get; set; }

    /// <summary>Invoice lookup (sprk_invoice). Maps to sprk_Invoice@odata.bind.</summary>
    public Guid? InvoiceLookup { get; set; }

    /// <summary>
    /// Work assignment lookup (<c>sprk_workassignment</c>). Added 2026-09-03 (unified-access-control-r2
    /// item 7 / Q4 widening) — the column already existed on <c>sprk_document</c>; only this request
    /// model and the association mappers were missing it, so a save filed to a work assignment was
    /// created UNASSOCIATED.
    /// </summary>
    public Guid? WorkAssignmentLookup { get; set; }

    /// <summary>
    /// Event lookup. Added 2026-09-03, same reason as <see cref="WorkAssignmentLookup"/> — but the
    /// column is <c>sprk_relatedevent</c>, NOT <c>sprk_event</c>, which does not exist on
    /// <c>sprk_document</c> at all (corrected 2026-09-04; the original write failed every event-filed
    /// save outright rather than dropping silently).
    /// </summary>
    public Guid? EventLookup { get; set; }

    /// <summary>
    /// To-do lookup (<c>sprk_relatedtodo</c>). Added 2026-09-04 (unified-access-control-r2).
    /// <para>
    /// ⚠️ This column ALWAYS existed. It was previously recorded across three places — the Q4 note,
    /// <c>EntityAccessFilter</c>, and the inbound email-r2 coordination doc — as proof that a document
    /// is <b>unmappable</b> to a to-do and that a SCHEMA change was required first. That was wrong in
    /// exactly one way: the check looked for a bare <c>sprk_todo</c> column and never looked at the
    /// <c>sprk_related*</c> family. <c>SELECT sprk_relatedtodo FROM sprk_document</c> succeeds.
    /// </para>
    /// </summary>
    public Guid? TodoLookup { get; set; }

    /// <summary>
    /// Contact lookup (<c>sprk_relatedcontact</c>). Added 2026-09-04 per the owner decision that closed
    /// the "account/contact saves are filed nowhere" gap: <c>contact</c> becomes real (the column
    /// exists), and <c>account</c> is REJECTED up front rather than accepted and silently dropped —
    /// <c>sprk_document</c> has no account lookup in either family.
    /// </summary>
    public Guid? ContactLookup { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Document Source Tracking
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Document source type choice value. Maps to sprk_SourceType.
    /// Values: UserUpload=659490000, EmailReceived=659490001, EmailSent=659490002,
    /// EmailArchive=659490003, EmailAttachment=659490004, Import=659490005, SystemGenerated=659490006.
    /// </summary>
    public int? SourceType { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Search Index Tracking Fields
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LEGACY (preserved for dual-write transition per R3 FR-3H3.2 / spec assumption line 366):
    /// Whether the document has been indexed for semantic search. Maps to sprk_searchindexed.
    /// Set to true at completion alongside <see cref="SearchIndexCompletedOn"/>.
    /// Slated for removal after consumer migration confirmed in prod (post-R3).
    /// </summary>
    public bool? SearchIndexed { get; set; }

    /// <summary>
    /// Name of the search index where document is stored. Maps to sprk_searchindexname.
    /// </summary>
    public string? SearchIndexName { get; set; }

    /// <summary>
    /// LEGACY (preserved for dual-write transition per R3 FR-3H3.2 / spec assumption line 366):
    /// Timestamp when document was last indexed. Maps to sprk_searchindexedon.
    /// Mirrors <see cref="SearchIndexCompletedOn"/> while dual-write is in force.
    /// </summary>
    public DateTime? SearchIndexedOn { get; set; }

    /// <summary>
    /// Timestamp when the document was ENQUEUED for indexing (Service Bus publish or direct invocation).
    /// Maps to sprk_searchindexqueuedon. Set to UtcNow at enqueue. R3 FR-3H3.2 / AC-H3.2.
    /// Distinct from <see cref="SearchIndexCompletedOn"/> — fixes the long-standing "indexed = true means enqueued, not completed" misleading-status pitfall.
    /// </summary>
    public DateTime? SearchIndexQueuedOn { get; set; }

    /// <summary>
    /// Timestamp when the document was CONFIRMED indexed by AI Search.
    /// Maps to sprk_searchindexcompletedon. Set to UtcNow at successful index callback. R3 FR-3H3.2 / AC-H3.2.
    /// During dual-write transition, set alongside the legacy <see cref="SearchIndexed"/> = true.
    /// </summary>
    public DateTime? SearchIndexCompletedOn { get; set; }
}

/// <summary>
/// Document entity model
/// </summary>
public class DocumentEntity
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? ContainerId { get; set; }
    public bool HasFile { get; set; }
    public string? FileName { get; set; }
    public long? FileSize { get; set; }
    public string? MimeType { get; set; }
    public string? GraphItemId { get; set; }
    public string? GraphDriveId { get; set; }
    public DocumentStatus Status { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }

    /// <summary>Created by user display name. Maps to _createdby_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>Last-modified by user display name. Maps to _modifiedby_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? ModifiedBy { get; set; }

    // Document Profile fields (populated by AI)
    /// <summary>TL;DR summary (1-2 sentences). Maps to sprk_filetldr.</summary>
    public string? Tldr { get; set; }

    /// <summary>Full summary (2-4 paragraphs). Maps to sprk_filesummary.</summary>
    public string? Summary { get; set; }

    /// <summary>Comma-separated keywords. Maps to sprk_keywords.</summary>
    public string? Keywords { get; set; }

    /// <summary>Document type classification (e.g., Contract, NDA, Invoice). Maps to sprk_documenttype.</summary>
    public string? DocumentType { get; set; }

    /// <summary>Extracted entities in JSON format (parties, dates, amounts). Maps to sprk_entities.</summary>
    public string? Entities { get; set; }

    // Email metadata fields (for .eml documents)
    /// <summary>Email subject. Maps to sprk_emailsubject.</summary>
    public string? EmailSubject { get; set; }

    /// <summary>Email sender. Maps to sprk_emailfrom.</summary>
    public string? EmailFrom { get; set; }

    /// <summary>Email recipients. Maps to sprk_emailto.</summary>
    public string? EmailTo { get; set; }

    /// <summary>Email CC recipients. Maps to sprk_emailcc.</summary>
    public string? EmailCc { get; set; }

    /// <summary>Email sent date. Maps to sprk_emaildate.</summary>
    public DateTime? EmailDate { get; set; }

    /// <summary>Email body text (truncated). Maps to sprk_emailbody.</summary>
    public string? EmailBody { get; set; }

    /// <summary>Is this document an email archive (.eml). Maps to sprk_isemailarchive.</summary>
    public bool? IsEmailArchive { get; set; }

    /// <summary>Parent document ID for attachments. Maps to _sprk_parentdocument_value.</summary>
    public string? ParentDocumentId { get; set; }

    /// <summary>Email conversation index for thread correlation. Maps to sprk_emailconversationindex.</summary>
    public string? EmailConversationIndex { get; set; }

    // Record association lookups (for relationship queries)
    /// <summary>Matter ID lookup. Maps to _sprk_matter_value.</summary>
    public string? MatterId { get; set; }

    /// <summary>Matter display name. Maps to _sprk_matter_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? MatterName { get; set; }

    /// <summary>Project ID lookup. Maps to _sprk_project_value.</summary>
    public string? ProjectId { get; set; }

    /// <summary>Project display name. Maps to _sprk_project_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Invoice ID lookup. Maps to _sprk_invoice_value.</summary>
    public string? InvoiceId { get; set; }

    /// <summary>Invoice display name. Maps to _sprk_invoice_value@OData.Community.Display.V1.FormattedValue.</summary>
    public string? InvoiceName { get; set; }

    // ═══════════════════════════════════════════════════════════════════════════
    // Search Index Tracking (multi-container-multi-index-r1)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// LEGACY (preserved for dual-write transition per R3 FR-3H3.2 / spec assumption line 366):
    /// Whether the document has been indexed for semantic search. Maps to sprk_searchindexed.
    /// Set to true by `RagEndpoints.IndexFile` after successful AI Search write.
    /// During R3 transition, this is set alongside <see cref="SearchIndexCompletedOn"/>.
    /// Slated for removal after consumer migration confirmed in prod (post-R3).
    /// </summary>
    public bool? SearchIndexed { get; set; }

    /// <summary>
    /// Name of the AI Search index where the document is stored. Maps to sprk_searchindexname.
    /// Required for multi-index aware consumers (e.g., `VisualizationService` / Find Similar)
    /// to bind the right SearchClient. Cascaded from the BU on wizard create.
    /// </summary>
    public string? SearchIndexName { get; set; }

    /// <summary>
    /// LEGACY (preserved for dual-write transition per R3 FR-3H3.2 / spec assumption line 366):
    /// Timestamp when document was last indexed. Maps to sprk_searchindexedon.
    /// Mirrors <see cref="SearchIndexCompletedOn"/> while dual-write is in force.
    /// </summary>
    public DateTime? SearchIndexedOn { get; set; }

    /// <summary>
    /// Timestamp when the document was ENQUEUED for indexing (Service Bus publish or direct invocation).
    /// Maps to sprk_searchindexqueuedon. R3 FR-3H3.2 / AC-H3.2.
    /// Distinct from <see cref="SearchIndexCompletedOn"/> — fixes the "indexed = true means enqueued, not completed" pitfall.
    /// </summary>
    public DateTime? SearchIndexQueuedOn { get; set; }

    /// <summary>
    /// Timestamp when the document was CONFIRMED indexed by AI Search.
    /// Maps to sprk_searchindexcompletedon. R3 FR-3H3.2 / AC-H3.2.
    /// During dual-write transition, set alongside the legacy <see cref="SearchIndexed"/> = true.
    /// </summary>
    public DateTime? SearchIndexCompletedOn { get; set; }
}

/// <summary>
/// Document status enumeration (matches Dataverse statuscode values)
/// </summary>
public enum DocumentStatus
{
    Draft = 1,
    Error = 2,
    Active = 421500001,
    Processing = 421500002
}

/// <summary>
/// Access level enumeration for documents
/// </summary>
public enum DocumentAccessLevel
{
    None = 0,
    Read = 1,
    Write = 2,
    FullControl = 3
}

/// <summary>
/// Metadata for a lookup navigation property on a child entity.
/// Used for discovering case-sensitive navigation properties for @odata.bind operations.
/// </summary>
public record LookupNavigationMetadata
{
    /// <summary>
    /// Logical name of the lookup attribute (e.g., "sprk_matter")
    /// </summary>
    public required string LogicalName { get; init; }

    /// <summary>
    /// Schema name of the lookup attribute (may differ in case, e.g., "sprk_Matter")
    /// </summary>
    public required string SchemaName { get; init; }

    /// <summary>
    /// Navigation property name for @odata.bind (CASE-SENSITIVE!)
    /// Example: "sprk_Matter" (capital M)
    /// This is ReferencingEntityNavigationPropertyName from metadata
    /// </summary>
    public required string NavigationPropertyName { get; init; }

    /// <summary>
    /// Target entity logical name (e.g., "sprk_matter")
    /// </summary>
    public required string TargetEntityLogicalName { get; init; }
}

/// <summary>
/// Analysis entity model (sprk_analysis)
/// </summary>
public class AnalysisEntity
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public Guid DocumentId { get; set; }
    public string? WorkingDocument { get; set; }
    // task 064 (ADR-040 Path A, spec §13.5 / FR-22): ChatHistory (sprk_chathistory) property
    // removed — the last reader (AnalysisDocumentLoader.GetOrReloadFromDataverseAsync) no longer
    // consumes it; Dataverse is anchor + outputs, Cosmos is the transcript store-of-record.
    public int StatusCode { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime ModifiedOn { get; set; }
}

/// <summary>
/// Target of an ADR-024 <c>regarding</c> association for a newly-created <c>sprk_analysis</c>
/// (spaarkeai-assistant-enhancements-r2 FR-D9 — "Set related record"). Carries the polymorphic
/// parent's logical name + id + (optional) picker-provided display name. When supplied to
/// <see cref="IAnalysisDataverseService.CreateAnalysisAsync"/>, the entity-specific regarding
/// lookup PLUS the ADR-024 denormalized resolver fields are written on the created analysis so it
/// surfaces on the parent's Analyses tab (subgrid relationship <c>sprk_analysis_RegardingMatter_*</c>).
///
/// Supported entity types: <c>sprk_matter</c>, <c>sprk_project</c> — the parents whose forms carry an
/// Analyses tab. A DOCUMENT association is NOT expressed here: an analysis anchors a document via its
/// own required <c>sprk_documentid</c> lookup (the <c>documentId</c> parameter), which is the
/// "regarding = document" path in FR-D9.
/// </summary>
public sealed record AnalysisRegardingTarget(string EntityLogicalName, Guid RecordId, string? RecordName);

/// <summary>
/// Analysis Action entity model (sprk_analysisaction)
/// </summary>
public class AnalysisActionEntity
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Analysis Output entity model (sprk_analysisoutput).
/// Stores individual output values from analysis execution.
/// </summary>
public class AnalysisOutputEntity
{
    /// <summary>Analysis Output ID (sprk_analysisoutputid)</summary>
    public Guid Id { get; set; }

    /// <summary>Output name for display</summary>
    public string? Name { get; set; }

    /// <summary>The actual output value/content</summary>
    public string? Value { get; set; }

    /// <summary>Parent analysis ID (lookup to sprk_analysis)</summary>
    public Guid AnalysisId { get; set; }

    /// <summary>Output type ID (lookup to sprk_aioutputtype)</summary>
    public Guid? OutputTypeId { get; set; }

    /// <summary>Sequence order for display</summary>
    public int? SortOrder { get; set; }

    /// <summary>Created date/time</summary>
    public DateTime CreatedOn { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════════════════
// Event Management Entities (Events and Workflow Automation R1)
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Event entity model (sprk_event)
/// </summary>
public class EventEntity
{
    /// <summary>Event ID (sprk_eventid)</summary>
    public Guid Id { get; set; }

    /// <summary>Event name (sprk_eventname) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>Event Type lookup ID (_sprk_eventtype_ref_value)</summary>
    public Guid? EventTypeId { get; set; }

    /// <summary>Event Type name (from expanded lookup)</summary>
    public string? EventTypeName { get; set; }

    /// <summary>State code: Active (0), Inactive (1)</summary>
    public int StateCode { get; set; }

    /// <summary>Status code: Draft (1), Planned (2), Open (3), OnHold (4), Completed (5), Cancelled (6), Deleted (7)</summary>
    public int StatusCode { get; set; }

    /// <summary>Base date (sprk_basedate)</summary>
    public DateTime? BaseDate { get; set; }

    /// <summary>Due date (sprk_duedate)</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Completed date (sprk_completeddate)</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Priority: Low (0), Normal (1), High (2), Urgent (3)</summary>
    public int? Priority { get; set; }

    /// <summary>Source: User (0), System (1), Workflow (2), External (3)</summary>
    public int? Source { get; set; }

    /// <summary>Remind at (sprk_remindat)</summary>
    public DateTime? RemindAt { get; set; }

    /// <summary>Related Event lookup ID (_sprk_relatedevent_value)</summary>
    public Guid? RelatedEventId { get; set; }

    /// <summary>Related Event Type: Reminder (0), Notification (1), Extension (2)</summary>
    public int? RelatedEventType { get; set; }

    /// <summary>Related Event Offset Type: HoursBefore (0), HoursAfter (1), DaysBefore (2), DaysAfter (3), Fixed (4)</summary>
    public int? RelatedEventOffsetType { get; set; }

    // Regarding lookup fields (entity-specific lookups)
    /// <summary>Regarding Account lookup (_sprk_regardingaccount_value)</summary>
    public Guid? RegardingAccountId { get; set; }
    /// <summary>Regarding Analysis lookup (_sprk_regardinganalysis_value)</summary>
    public Guid? RegardingAnalysisId { get; set; }
    /// <summary>Regarding Contact lookup (_sprk_regardingcontact_value)</summary>
    public Guid? RegardingContactId { get; set; }
    /// <summary>Regarding Invoice lookup (_sprk_regardinginvoice_value)</summary>
    public Guid? RegardingInvoiceId { get; set; }
    /// <summary>Regarding Matter lookup (_sprk_regardingmatter_value)</summary>
    public Guid? RegardingMatterId { get; set; }
    /// <summary>Regarding Project lookup (_sprk_regardingproject_value)</summary>
    public Guid? RegardingProjectId { get; set; }
    /// <summary>Regarding Budget lookup (_sprk_regardingbudget_value)</summary>
    public Guid? RegardingBudgetId { get; set; }
    /// <summary>Regarding Work Assignment lookup (_sprk_regardingworkassignment_value)</summary>
    public Guid? RegardingWorkAssignmentId { get; set; }

    // Denormalized regarding fields (for unified views)
    /// <summary>Regarding record ID as string (sprk_regardingrecordid)</summary>
    public string? RegardingRecordId { get; set; }
    /// <summary>Regarding record display name (sprk_regardingrecordname)</summary>
    public string? RegardingRecordName { get; set; }
    /// <summary>Regarding record type: Project (0), Matter (1), Invoice (2), Analysis (3), Account (4), Contact (5), WorkAssignment (6), Budget (7)</summary>
    public int? RegardingRecordType { get; set; }

    /// <summary>Created date/time</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Modified date/time</summary>
    public DateTime ModifiedOn { get; set; }
}

/// <summary>
/// Request model for creating a new Event
/// </summary>
public class CreateEventRequest
{
    /// <summary>Event name (required)</summary>
    public required string Name { get; set; }

    /// <summary>Description</summary>
    public string? Description { get; set; }

    /// <summary>Event Type ID</summary>
    public Guid? EventTypeId { get; set; }

    /// <summary>Base date</summary>
    public DateTime? BaseDate { get; set; }

    /// <summary>Due date</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Priority: Low (0), Normal (1), High (2), Urgent (3)</summary>
    public int? Priority { get; set; }

    /// <summary>Regarding record type</summary>
    public int? RegardingRecordType { get; set; }

    /// <summary>Regarding record ID</summary>
    public string? RegardingRecordId { get; set; }

    /// <summary>Regarding record name</summary>
    public string? RegardingRecordName { get; set; }
}

/// <summary>
/// Request model for updating an Event
/// </summary>
public class UpdateEventRequest
{
    /// <summary>Event name</summary>
    public string? Name { get; set; }

    /// <summary>Description</summary>
    public string? Description { get; set; }

    /// <summary>Event Type ID</summary>
    public Guid? EventTypeId { get; set; }

    /// <summary>Base date</summary>
    public DateTime? BaseDate { get; set; }

    /// <summary>Due date</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>Priority: Low (0), Normal (1), High (2), Urgent (3)</summary>
    public int? Priority { get; set; }

    /// <summary>Status code</summary>
    public int? StatusCode { get; set; }

    /// <summary>Regarding record type</summary>
    public int? RegardingRecordType { get; set; }

    /// <summary>Regarding record ID</summary>
    public string? RegardingRecordId { get; set; }

    /// <summary>Regarding record name</summary>
    public string? RegardingRecordName { get; set; }
}

/// <summary>
/// Event Type entity model (sprk_eventtype)
/// </summary>
public class EventTypeEntity
{
    /// <summary>Event Type ID (sprk_eventtypeid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_name) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Event code (sprk_eventcode)</summary>
    public string? EventCode { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>State code: Active (0), Inactive (1)</summary>
    public int StateCode { get; set; }

    /// <summary>Requires due date: No (0), Yes (1)</summary>
    public int? RequiresDueDate { get; set; }

    /// <summary>Requires base date: No (0), Yes (1)</summary>
    public int? RequiresBaseDate { get; set; }
}

/// <summary>
/// Event Log entity model (sprk_eventlog)
/// </summary>
public class EventLogEntity
{
    /// <summary>Event Log ID (sprk_eventlogid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_eventlogname) - Primary field</summary>
    public string? Name { get; set; }

    /// <summary>Event lookup ID (_sprk_event_value)</summary>
    public Guid EventId { get; set; }

    /// <summary>Action: Created (0), Updated (1), Completed (2), Cancelled (3), Deleted (4)</summary>
    public int Action { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>Created date/time</summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>Created by user ID</summary>
    public Guid? CreatedById { get; set; }

    /// <summary>Created by user name</summary>
    public string? CreatedByName { get; set; }
}

/// <summary>
/// Event Log action constants
/// </summary>
public static class EventLogAction
{
    public const int Created = 0;
    public const int Updated = 1;
    public const int Completed = 2;
    public const int Cancelled = 3;
    public const int Deleted = 4;

    public static string GetDisplayName(int action) => action switch
    {
        Created => "Created",
        Updated => "Updated",
        Completed => "Completed",
        Cancelled => "Cancelled",
        Deleted => "Deleted",
        _ => "Unknown"
    };
}

// ═══════════════════════════════════════════════════════════════════════════════════════
// Field Mapping Framework Entities (Events and Workflow Automation R1)
// ═══════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Field Mapping Profile entity model (sprk_fieldmappingprofile)
/// </summary>
public class FieldMappingProfileEntity
{
    /// <summary>Profile ID (sprk_fieldmappingprofileid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_name) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Source entity logical name (sprk_sourceentity)</summary>
    public required string SourceEntity { get; set; }

    /// <summary>Target entity logical name (sprk_targetentity)</summary>
    public required string TargetEntity { get; set; }

    /// <summary>Mapping direction: ParentToChild (0), ChildToParent (1), Bidirectional (2)</summary>
    public int MappingDirection { get; set; }

    /// <summary>Sync mode: OneTime (0), ManualRefresh (1)</summary>
    public int SyncMode { get; set; }

    /// <summary>Is active</summary>
    public bool IsActive { get; set; }

    /// <summary>Description (sprk_description)</summary>
    public string? Description { get; set; }

    /// <summary>Child mapping rules (populated via expand or separate query)</summary>
    public List<FieldMappingRuleEntity>? Rules { get; set; }
}

/// <summary>
/// Field Mapping Rule entity model (sprk_fieldmappingrule)
/// </summary>
public class FieldMappingRuleEntity
{
    /// <summary>Rule ID (sprk_fieldmappingruleid)</summary>
    public Guid Id { get; set; }

    /// <summary>Name (sprk_name) - Primary field</summary>
    public required string Name { get; set; }

    /// <summary>Mapping Profile lookup ID (_sprk_fieldmappingprofile_value)</summary>
    public Guid ProfileId { get; set; }

    /// <summary>Source field schema name (sprk_sourcefield)</summary>
    public required string SourceField { get; set; }

    /// <summary>Source field type: Text (0), Lookup (1), OptionSet (2), Number (3), DateTime (4), Boolean (5), Memo (6)</summary>
    public int SourceFieldType { get; set; }

    /// <summary>Target field schema name (sprk_targetfield)</summary>
    public required string TargetField { get; set; }

    /// <summary>Target field type</summary>
    public int TargetFieldType { get; set; }

    /// <summary>Mapping type: Copy (0), Default (1), Concat (2), Template (3) — sprk_mapping_type</summary>
    public int MappingType { get; set; }

    /// <summary>Compatibility mode: Strict (0), Resolve (1)</summary>
    public int CompatibilityMode { get; set; }

    /// <summary>Is required (fail if source is empty)</summary>
    public bool IsRequired { get; set; }

    /// <summary>Default value when source is empty (Default/Constant literal) — sprk_defaultvalue</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Concat/Template format string (nullable; unused for Copy/Default) — sprk_expression</summary>
    public string? Expression { get; set; }

    /// <summary>Is cascading source (triggers secondary mappings)</summary>
    public bool IsCascadingSource { get; set; }

    /// <summary>Execution order for dependent mappings</summary>
    public int ExecutionOrder { get; set; }

    /// <summary>Is active</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Field mapping direction constants
/// </summary>
public static class MappingDirection
{
    public const int ParentToChild = 0;
    public const int ChildToParent = 1;
    public const int Bidirectional = 2;
}

/// <summary>
/// Field mapping sync mode constants
/// </summary>
public static class SyncMode
{
    public const int OneTime = 0;
    public const int ManualRefresh = 1;
}

/// <summary>
/// Field type constants for mapping rules
/// </summary>
public static class FieldMappingFieldType
{
    public const int Text = 0;
    public const int Lookup = 1;
    public const int OptionSet = 2;
    public const int Number = 3;
    public const int DateTime = 4;
    public const int Boolean = 5;
    public const int Memo = 6;
}

/// <summary>
/// Regarding record type constants
/// </summary>
public static class RegardingRecordType
{
    public const int Project = 0;
    public const int Matter = 1;
    public const int Invoice = 2;
    public const int Analysis = 3;
    public const int Account = 4;
    public const int Contact = 5;
    public const int WorkAssignment = 6;
    public const int Budget = 7;

    /// <summary>
    /// Gets the entity logical name for a regarding record type
    /// </summary>
    public static string? GetEntityLogicalName(int recordType) => recordType switch
    {
        Project => "sprk_project",
        Matter => "sprk_matter",
        Invoice => "sprk_invoice",
        Analysis => "sprk_analysis",
        Account => "account",
        Contact => "contact",
        WorkAssignment => "sprk_workassignment",
        Budget => "sprk_budget",
        _ => null
    };

    /// <summary>
    /// Gets the regarding lookup field name for a regarding record type
    /// </summary>
    public static string? GetLookupFieldName(int recordType) => recordType switch
    {
        Project => "sprk_regardingproject",
        Matter => "sprk_regardingmatter",
        Invoice => "sprk_regardinginvoice",
        Analysis => "sprk_regardinganalysis",
        Account => "sprk_regardingaccount",
        Contact => "sprk_regardingcontact",
        WorkAssignment => "sprk_regardingworkassignment",
        Budget => "sprk_regardingbudget",
        _ => null
    };

    // ── String-keyed helpers (FR-D9 "Set related record" — sprk_analysis regarding write) ──
    // These map a target entity's LOGICAL NAME (as chosen in the client picker / AnalysisRegardingTarget)
    // to the ADR-024 fields the resolver writes on sprk_analysis. The canonical field-name maps live
    // here in the shared Spaarke.Dataverse library because the equivalent maps in the BFF-layer
    // IncomingAssociationResolver (Services/Communication) cannot be referenced from here (Spaarke.Dataverse
    // must not depend on Sprk.Bff.Api). Restricted to matter + project — the parents whose forms carry an
    // Analyses tab; a document association uses the analysis's own sprk_documentid anchor instead.

    /// <summary>Entity-specific regarding lookup attribute for a target entity logical name (matter/project).</summary>
    public static string? GetRegardingLookupFieldByEntity(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_regardingmatter",
        "sprk_project" => "sprk_regardingproject",
        _ => null,
    };

    /// <summary>Primary display-name attribute for a target entity (ADR-024 sprk_regardingrecordname source).</summary>
    public static string? GetPrimaryNameField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_mattername",
        "sprk_project" => "sprk_projectname",
        _ => null,
    };

    /// <summary>Reference-number attribute for a target entity (ADR-024 sprk_regardingrecordnumber source); null when none.</summary>
    public static string? GetReferenceNumberField(string entityLogicalName) => entityLogicalName switch
    {
        "sprk_matter" => "sprk_matternumber",
        "sprk_project" => "sprk_projectnumber",
        _ => null,
    };
}

/// <summary>
/// Lightweight record returned by KPI assessment queries.
/// Contains only the fields needed for scorecard calculations.
/// </summary>
public class KpiAssessmentRecord
{
    /// <summary>Assessment record ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Grade choice value from sprk_kpigradescore (e.g. 100000000=A+, 100000001=A, 100000003=B, 100000005=C, 100000008=F, 100000009=No Grade).</summary>
    public int Grade { get; set; }

    /// <summary>Record creation timestamp (used for ordering).</summary>
    public DateTime CreatedOn { get; set; }
}
