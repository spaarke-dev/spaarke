using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 146 (GitHub #1034) — the census of server CREATES of child records. Every place the
/// server builds a new row of a table that is a child (or content) of a project, matter or work assignment is listed
/// here with its count and its disposition, and the list is asserted against the source. An unlisted create of a
/// child table fails the build, so the next writer cannot quietly create a secure record's child in an ordinary
/// business unit — the defect C10 half 2 closed (write-path invariant I-2).
/// </summary>
/// <remarks>
/// <para><b>Dispositions.</b> <c>Routed</c>: the file decides the row's owner through <c>IRecordOwnershipResolver</c>
/// and writes it — asserted by <see cref="EveryRoutedEntryRoutesThroughTheResolver"/>. <c>Seam</c>: the create sits
/// in a shared persistence method that REFUSES a row without a resolved owner team, so every caller is forced to
/// resolve one — asserted by <see cref="EverySeamRefusesAnOwnerlessCreate"/>. <c>Waived</c>: the row is deliberately
/// not re-owned, with a reason and a kind — <c>Pending</c> (an open owner decision, named) or <c>Permanent</c>.</para>
/// <para><b>What the scanner sees, and what it does not.</b> It counts the create shapes that name a table literally
/// or through a <c>const</c>: <c>new Entity("t")</c> / <c>new DataverseEntity(T)</c> (single argument — a two-argument
/// construction is an update or a keyed upsert target), and a Web API POST to a literal entity set. Writers whose
/// create it cannot see — a create-by-upsert PATCH, a keyed <c>UpsertRequest</c>, a POST to a computed URL, a
/// caller of a seam, a REPARENT (an update that files a child under a new parent) — are listed in
/// <see cref="UnscannedWriters"/>, and each must still call the resolver
/// (<see cref="EveryUnscannedWriterRoutesThroughTheResolver"/>). Crude by design, like every
/// <see cref="SourceScan"/> rule: <see cref="Detector_NegativeControl"/> proves it fires and that it ignores the
/// shapes that are not creates.</para>
/// <para><b>No DI resolution anywhere in this file</b> (ADR-038 ban B3).</para>
/// </remarks>
public class RecordOwnerAssignmentCensusTests
{
    /// <summary>
    /// The child and content tables, from LIVE metadata (spaarkedev1, 2026-10-01: the OneToMany relationships of
    /// sprk_project, sprk_matter and sprk_workassignment, plus the content rows that hang off a communication, an
    /// analysis, a document, an event, an invoice and a budget). All UserOwned.
    /// </summary>
    private static readonly string[] ChildTables =
    {
        "sprk_agreement", "sprk_analysis", "sprk_billingevent", "sprk_budget", "sprk_communication",
        "sprk_communicationthread", "sprk_document", "sprk_event", "sprk_invoice", "sprk_kpiassessment", "sprk_memo",
        "sprk_reportcard", "sprk_spendsignal", "sprk_spendsnapshot", "sprk_todo", "sprk_workassignment",
        "sprk_servicerequest", "sprk_analysisoutput", "sprk_communicationattachment", "sprk_communicationparticipant",
        "sprk_emailreviewlog", "sprk_communicationchannelref", "sprk_fileversion", "sprk_emailartifact",
        "sprk_attachmentartifact", "sprk_eventlog", "sprk_invoicelineitem", "sprk_budgetbucket", "sprk_processingjob",
    };

    private enum Disposition
    {
        Routed,
        Seam,
        Waived,
    }

    private enum WaiverKind
    {
        None,
        Pending,
        Permanent,
    }

    /// <param name="OwnerFile">For <see cref="Disposition.Routed"/>: the file that resolves the owner when it is not
    /// the file that builds the row (a mapper whose caller owns the write).</param>
    private sealed record CensusEntry(
        string FileName,
        string Table,
        int Sites,
        Disposition Disposition,
        string Reason,
        WaiverKind Waiver = WaiverKind.None,
        string? OwnerFile = null);

    // =============================================================================================
    // THE CENSUS
    // ---------------------------------------------------------------------------------------------
    // MAINTENANCE PROCEDURE — read before changing a number here.
    //   1. A failure is NOT a prompt to bump a count. It means a create of a secure record's child appeared, moved or
    //      vanished. A new create must decide its owner through IRecordOwnershipResolver (every parent lookup it
    //      writes, secure-if-any) and refuse when none resolves, in its own error contract — then be listed Routed.
    //   2. Counts are per (file, table), so removing one site and adding another elsewhere cannot pass unnoticed.
    //   3. Never add a Waived entry to make a build green. A row that carries a secure record's content or names its
    //      participants is a child and is routed, not waived (task 146 constraint).
    // =============================================================================================
    private static readonly IReadOnlyList<CensusEntry> Census = new[]
    {
        // ── Seams: shared persistence methods that refuse a row with no resolved owner team ─────────────────────
        new CensusEntry("DataverseServiceClientImpl.cs", "sprk_document", 1, Disposition.Seam,
            "CreateDocumentAsync — throws when CreateDocumentRequest.OwningTeamId is absent; every caller resolves the "
            + "team (Office save, upload finalization, email attachments, POST /api/v1/documents)."),
        new CensusEntry("DataverseServiceClientImpl.cs", "sprk_analysis", 1, Disposition.Seam,
            "CreateAnalysisAsync — throws without an owning team; AnalysisEndpoints, AnalysisResultPersistence and "
            + "AppOnlyAnalysisService resolve it from the document / regarding record."),
        new CensusEntry("DataverseServiceClientImpl.cs", "sprk_analysisoutput", 1, Disposition.Seam,
            "CreateAnalysisOutputAsync — throws without AnalysisOutputEntity.OwningTeamId; the output is content of its "
            + "analysis (AI output about a possibly secure document) and is owned like it."),
        new CensusEntry("DataverseServiceClientImpl.cs", "sprk_emailartifact", 1, Disposition.Seam,
            "CreateEmailArtifactAsync — refuses without the OwningTeamId request property; UploadFinalizationWorker "
            + "passes its document's team (task 146)."),
        new CensusEntry("DataverseServiceClientImpl.cs", "sprk_attachmentartifact", 1, Disposition.Seam,
            "CreateAttachmentArtifactAsync — refuses without the OwningTeamId request property; UploadFinalizationWorker "
            + "passes its document's team (task 146)."),
        new CensusEntry("DataverseWebApiService.cs", "sprk_event", 1, Disposition.Seam,
            "CreateEventAsync — throws when CreateEventRequest.OwningTeamId is absent; EventEndpoints resolves it from "
            + "the regarding record (or the caller for an unfiled event)."),

        // ── Routed: the file resolves the owner and writes it ───────────────────────────────────────────────────
        new CensusEntry("DataverseWebApiService.cs", "sprk_eventlog", 1, Disposition.Routed,
            "CreateEventLogAsync takes the owner positionally; EventEndpoints resolves it as content of the event "
            + "(RecordOwnershipContext.ContentOf) for every log row it writes.",
            OwnerFile: "EventEndpoints.cs"),
        new CensusEntry("ObservationMirrorMapper.cs", "sprk_analysis", 1, Disposition.Routed,
            "The mapper builds the mirrored analysis row; DataverseObservationMirror resolves its owner from the row's "
            + "parent lookups (ForChild) and skips the mirror on a refusal.",
            OwnerFile: "DataverseObservationMirror.cs"),
        new CensusEntry("CommunicationService.cs", "sprk_communication", 3, Disposition.Routed,
            "Outbound email, message and shared sends: ForChild over every parent lookup on the row (secure-if-any); an "
            + "unfiled send keeps its creator (E1); a refusal throws before the create."),
        new CensusEntry("CommunicationService.cs", "sprk_document", 3, Disposition.Routed,
            "Archived .eml and attachment documents — content of the communication (ContentOf), resolved before the SPE "
            + "upload so a refusal leaves neither bytes nor a row."),
        new CensusEntry("CommunicationService.cs", "sprk_communicationattachment", 1, Disposition.Routed,
            "Attachment rows — content of the communication (ContentOf); a refusal writes none."),
        new CensusEntry("EmailUploadCaptureService.cs", "sprk_communication", 1, Disposition.Routed,
            "Upload capture evaluates the association BEFORE the create and owns the row from the decision's records "
            + "(secure-if-any); a refusal skips the capture."),
        new CensusEntry("IncomingCommunicationProcessor.cs", "sprk_communication", 1, Disposition.Routed,
            "Inbound email: association evaluated before the create; a refusal throws RecordOwnerUnresolvedException "
            + "and the job HOLDS the email (retry, dead-letter, administrator alert — owner amendment R3)."),
        new CensusEntry("IncomingCommunicationProcessor.cs", "sprk_document", 2, Disposition.Routed,
            "Inbound .eml archive and attachment documents — owned like the email (the owner resolved before its create)."),
        new CensusEntry("IncomingCommunicationProcessor.cs", "sprk_communicationattachment", 1, Disposition.Routed,
            "Inbound attachment rows — owned like the email (the owner resolved before its create)."),
        new CensusEntry("MessageAttachmentMaterializer.cs", "sprk_document", 1, Disposition.Routed,
            "Chat attachment document — content of the message (ContentOf), resolved before the SPE upload; a refusal is "
            + "a 409 rejection."),
        new CensusEntry("MessageAttachmentMaterializer.cs", "sprk_communicationattachment", 1, Disposition.Routed,
            "Chat attachment link row — content of the message, same owner as its document."),
        new CensusEntry("CommunicationParticipantIndexer.cs", "sprk_communicationparticipant", 1, Disposition.Routed,
            "Participant index rows name the people on a message — content of it (ContentOf); a refusal writes none "
            + "(best-effort index, self-heals next pass)."),
        new CensusEntry("CommunicationEnrichmentService.cs", "sprk_emailreviewlog", 6, Disposition.Routed,
            "AI review-log rows carry the message's suggestions — content of the communication (ContentOf); a refusal "
            + "throws inside the best-effort enrichment step."),
        new CensusEntry("CommunicationProposalApplyService.cs", "sprk_emailreviewlog", 3, Disposition.Routed,
            "Apply / dismiss / undo audit rows — owner resolved BEFORE the target write; a refusal is a 409."),
        new CensusEntry("CommunicationCreateTaskApplyService.cs", "sprk_emailreviewlog", 3, Disposition.Routed,
            "Create-task / ad-hoc / undo audit rows — owner resolved BEFORE the task is created; a refusal is a 409."),
        new CensusEntry("ThreadResolver.cs", "sprk_communicationthread", 3, Disposition.Routed,
            "Record threads (owner S6): a thread anchored to an ownership parent is owned by that record's team; a "
            + "Direct thread and the per-user master keep their creator (E2). A message joining a record thread is "
            + "re-filed under its record (ReparentAsync)."),
        new CensusEntry("ComposeCreateOnSavePromoter.cs", "sprk_document", 1, Disposition.Routed,
            "Compose promote: owned from the inherited filing links (secure-if-any) or the caller; the SPE alternate "
            + "key is NOT relaxed; a refusal is a 409."),
        new CensusEntry("TaskActionCore.cs", "sprk_event", 1, Disposition.Routed,
            "AI create-task: ForChild over the stamped parents; the caller-supplied owner no longer owns a filed task "
            + "(B2: the assignee belongs in Assigned To, task 152)."),
        new CensusEntry("DocumentCheckoutService.cs", "sprk_fileversion", 1, Disposition.Routed,
            "A file version is content of its document (ContentOf); a refusal is a 409 and nothing is written."),
        new CensusEntry("OfficeService.cs", "sprk_todo", 1, Disposition.Routed,
            "Office to-do: ForChild over the record regarding, its core-ancestor stamps and the document / email "
            + "carriers (secure-if-any, task 146); unfiled → the acting user's team."),
        new CensusEntry("TodoGenerationService.cs", "sprk_todo", 1, Disposition.Routed,
            "Generated to-dos are owned from their source event (ownershipSource); a refusal counts the rule failed."),

        // ── Waived ─────────────────────────────────────────────────────────────────────────────────────────────
        new CensusEntry("MessagingIngestor.cs", "sprk_communication", 1, Disposition.Waived,
            "An inbound chat message names no parent at create, so it keeps its creator (task 146 escalation E1: unfiled "
            + "communications stay creator-owned — the per-user master thread keys on the message's owner and Direct-"
            + "thread privacy rests on per-participant shares). It is filed only by joining a record thread, which "
            + "re-derives its owner (ThreadResolver JOIN → ReparentAsync).",
            WaiverKind.Pending),
        new CensusEntry("DirectThreadAccessService.cs", "sprk_communicationthread", 1, Disposition.Waived,
            "A Direct (two-party) thread has no regarding record and is private by per-participant shares — per-user by "
            + "design (task 146 constraint 'per-user artifacts'; escalation E2).",
            WaiverKind.Permanent),
        new CensusEntry("MessagingThreadKeyStrategy.cs", "sprk_communicationchannelref", 1, Disposition.Waived,
            "The ACS thread-key mapping row: a transport key (ACS thread id → thread), no record content or "
            + "participants; read only by the BFF app-only.",
            WaiverKind.Permanent),
        new CensusEntry("DataverseServiceClientImpl.cs", "sprk_processingjob", 1, Disposition.Waived,
            "An Office job-tracking row (status and progress), authorized by its sprk_initiatedby creator lookup and "
            + "read app-only; not a child of any root in live metadata.",
            WaiverKind.Permanent),
        new CensusEntry("WorkAssignmentEndpoints.cs", "sprk_workassignment", 1, Disposition.Waived,
            "A ROOT, not a child: its ownership is provisioning's (task 144 / owner S6, never a bare re-own here). It "
            + "cannot file under a matter today — the endpoint writes sprk_matterid, which sprk_workassignment does not "
            + "have (live metadata 2026-10-02: the matter lookup is sprk_regardingmatter), so every MatterId-carrying "
            + "create already fails. Inline secure provisioning at creation (S6 b) is recorded as task 146 handoff.",
            WaiverKind.Pending),
    };

    /// <summary>
    /// Writers whose create or re-file the scanner cannot see. Each must still call the resolver.
    /// </summary>
    private sealed record UnscannedWriter(string FileName, string Shape, string Reason);

    private static readonly IReadOnlyList<UnscannedWriter> UnscannedWriters = new[]
    {
        new UnscannedWriter("InvoiceReviewService.cs", "create-by-upsert PATCH (sprk_invoice) + document re-file",
            "Invoice owned by the matter's team (task 130); linking the document is a reparent (task 146)."),
        new UnscannedWriter("SignalEvaluationService.cs", "create-by-upsert PATCH (sprk_spendsignal)",
            "Owned by the matter's team; a refusal skips the evaluation."),
        new UnscannedWriter("SpendSnapshotService.cs", "keyed UpsertRequest (sprk_spendsnapshot)",
            "Owned by the parent matter/project's team; a refusal skips the snapshot."),
        new UnscannedWriter("ExternalProjectDataEndpoints.cs", "Web API POST to a computed URL (document, event, to-do)",
            "The external routes resolve the root's owner before the create; ExternalDataService refuses an empty team."),
        new UnscannedWriter("EventEndpoints.cs", "seam caller (CreateEventAsync, CreateEventLogAsync) + event re-file",
            "Owner resolved from the regarding record; a regarding change is a reparent."),
        new UnscannedWriter("AnalysisEndpoints.cs", "seam caller (CreateAnalysisAsync)",
            "Create / fork / promote resolve the owner from the document and regarding record; a refusal is a 409."),
        new UnscannedWriter("AnalysisResultPersistence.cs", "seam caller (CreateAnalysisAsync, CreateAnalysisOutputAsync)",
            "Outputs are owned like their analysis; a refusal skips the analysis and its outputs."),
        new UnscannedWriter("AppOnlyAnalysisService.cs", "seam caller (CreateAnalysisAsync, CreateAnalysisOutputAsync)",
            "App-only profile analyses owned from the document; a refusal skips (best-effort shape)."),
        new UnscannedWriter("DataverseObservationMirror.cs", "owner of ObservationMirrorMapper's row",
            "ForChild over the mirrored row; a refusal skips the mirror."),
        new UnscannedWriter("EmailAttachmentProcessor.cs", "seam caller (CreateDocumentAsync)",
            "Inbound attachment documents owned from the parent document and its association."),
        new UnscannedWriter("UploadFinalizationWorker.cs", "seam caller (CreateDocumentAsync, artifact creates)",
            "Office upload documents and their artifacts carry the resolved / carried team."),
        new UnscannedWriter("DataverseDocumentsEndpoints.cs", "seam caller (POST) + PUT re-file",
            "An update that files the document under a record is a reparent."),
        new UnscannedWriter("RecordMatchEndpoints.cs", "document re-file (associate)",
            "Associating a document with a record is a reparent."),
        new UnscannedWriter("DataverseUpdateHandler.cs", "generic update re-file (EntityReference values)",
            "An EntityReference onto a parent of a child table is a reparent."),
        new UnscannedWriter("UpdateRecordActionCore.cs", "generic update re-file (lookups)",
            "A lookup onto a parent of a child table is a reparent."),
        new UnscannedWriter("IncomingAssociationResolver.cs", "communication re-file (regarding writes)",
            "Filing a communication under a record re-derives its owner before the regarding is written."),
        new UnscannedWriter("CommunicationService.cs", "explicit respond-into-thread stamp",
            "Joining a record thread files the message under the thread's record (shared ThreadResolver step)."),

        // ── task 146 r2: the chat tools that write AS THE USER (verifier items 2 and 6) ──────────────────────────────
        new UnscannedWriter("DataverseCreateRecordHandler.cs", "run-as-user POST to a computed entity set (any table)",
            "Owner S1 (1) / G5: a CHILD row filed under a record is checked as the caller, then created by the APPLICATION "
            + "owned by the resolver's team (OwnedChildWrite); any other table filed under a SECURE record is refused."),
        new UnscannedWriter("EmailDraftToolHandler.cs", "run-as-user POST of a sprk_communication draft",
            "Owner S1 (1) / G5: a draft filed under a record is created by the APPLICATION owned by the resolver's team, the "
            + "drafter recorded as sprk_sentby; an unfiled draft is still created as the user (E1)."),
        new UnscannedWriter("OwnedChildWrite.cs", "app-only create-by-upsert of a filed child (S1 / G5)",
            "The one owned-create path of the chat tools: as-the-caller checks, the resolver's owner, the app-only create."),
        new UnscannedWriter("DataverseUpdateRecordHandler.cs", "run-as-user PATCH of any table (lookups allowed)",
            "A lookup change on a CHILD table is a re-file (ReparentAsync: the caller's PATCH, then the owner assigned and "
            + "read back); any other table moved under a SECURE record is refused."),
    };

    /// <summary>Seams that must refuse an owner-less create, by the method that builds the row.</summary>
    private sealed record OwnerSeam(string FileName, string Method, Regex Guard);

    private static readonly IReadOnlyList<OwnerSeam> Seams = new[]
    {
        new OwnerSeam("DataverseServiceClientImpl.cs", "CreateDocumentAsync", Refusal),
        new OwnerSeam("DataverseServiceClientImpl.cs", "CreateAnalysisAsync", Refusal),
        new OwnerSeam("DataverseServiceClientImpl.cs", "CreateAnalysisOutputAsync", Refusal),
        new OwnerSeam("DataverseServiceClientImpl.cs", "CreateEmailArtifactAsync", Calls("RequireArtifactOwner")),
        new OwnerSeam("DataverseServiceClientImpl.cs", "CreateAttachmentArtifactAsync", Calls("RequireArtifactOwner")),
        new OwnerSeam("DataverseServiceClientImpl.cs", "RequireArtifactOwner", Refusal),
        new OwnerSeam("DataverseWebApiService.cs", "CreateEventAsync", Refusal),
        // r2 (verifier items 3 and 15): a builder must REFUSE an empty team AND BIND the team it was handed — dropping the
        // bind used to pass every test, leaving the external create owned by the BFF application user.
        new OwnerSeam("ExternalDataService.cs", "BuildDocumentCreatePayload", RefusesAndBindsTheOwner),
        new OwnerSeam("ExternalDataService.cs", "BuildEventCreatePayload", RefusesAndBindsTheOwner),
        new OwnerSeam("ExternalDataService.cs", "BuildTodoCreatePayload", RefusesAndBindsTheOwner),
        new OwnerSeam("ExternalDataService.cs", "RequireOwner", Refusal),
    };

    /// <summary>The refusal every seam throws: "... refusing to create ... app-owned ..." (a stray throw elsewhere in
    /// the method does not satisfy it).</summary>
    private static Regex Refusal => new(@"throw new InvalidOperationException\([\s\S]{0,400}?refusing to create");

    private static Regex Calls(string helper) => new(@"\b" + Regex.Escape(helper) + @"\s*\(");

    // =============================================================================================
    // THE OWNER-WRITE CENSUS (task 146 r2, verifier items 1 and 15)
    // ---------------------------------------------------------------------------------------------
    // Every member of the server that WRITES an owner — `x["ownerid"] =`, `["ownerid@odata.bind"] =`, or the same through
    // a const holding either key — listed with its count and kind. The create census above sees creates; it could not see
    // an owner OVERRIDE on an existing row: CommunicationEnrichmentService.AssignOwningTeamAsync (FR-E7 category routing)
    // set ownerid to a team looked up by NAME in any business unit, re-owning a secure record's email out of isolation,
    // inside a file the census already listed as Routed. A new owner write anywhere now fails until it is classified.
    // MAINTENANCE: a failure is not a prompt to bump a count. A child's owner comes from IRecordOwnershipResolver.
    // =============================================================================================

    private enum OwnerWriteKind
    {
        /// <summary>The resolver itself (ApplyTo, the reparent's assignment).</summary>
        Resolver,

        /// <summary>A shared create that writes the team its caller resolved and refuses an empty one (<see cref="Seams"/>).</summary>
        Seam,

        /// <summary>Writes a team the resolver answered — asserted: the file calls the resolver.</summary>
        Routed,

        /// <summary>A ROOT's own ownership — provisioning's (task 144 / owner S6), not a child's.</summary>
        Root,

        /// <summary>A per-user artifact owned by its user by design (task 146 constraint "per-user artifacts").</summary>
        PerUser,

        /// <summary>An owner set only on a row filed under NOTHING — asserted: the member checks the filing first.</summary>
        UnfiledOnly,
    }

    private sealed record OwnerWriteEntry(string FileName, string Member, int Writes, OwnerWriteKind Kind, string Reason);

    private static readonly IReadOnlyList<OwnerWriteEntry> OwnerWrites = new[]
    {
        new OwnerWriteEntry("RecordOwnershipResolver.cs", "ApplyTo", 1, OwnerWriteKind.Resolver,
            "Writes the resolved team onto a row about to be created; refuses to apply a refusal."),
        new OwnerWriteEntry("RecordOwnershipResolver.cs", "ReparentAsync", 1, OwnerWriteKind.Resolver,
            "The re-file's separate owner assignment, read back."),

        new OwnerWriteEntry("DataverseServiceClientImpl.cs", "CreateDocumentAsync", 1, OwnerWriteKind.Seam,
            "CreateDocumentRequest.OwningTeamId — throws without it."),
        new OwnerWriteEntry("DataverseServiceClientImpl.cs", "CreateAnalysisAsync", 1, OwnerWriteKind.Seam,
            "The caller's resolved team — throws without it."),
        new OwnerWriteEntry("DataverseServiceClientImpl.cs", "CreateAnalysisOutputAsync", 1, OwnerWriteKind.Seam,
            "AnalysisOutputEntity.OwningTeamId — throws without it."),
        new OwnerWriteEntry("DataverseServiceClientImpl.cs", "CreateEmailArtifactAsync", 1, OwnerWriteKind.Seam,
            "The OwningTeamId request property — RequireArtifactOwner refuses without it."),
        new OwnerWriteEntry("DataverseServiceClientImpl.cs", "CreateAttachmentArtifactAsync", 1, OwnerWriteKind.Seam,
            "The OwningTeamId request property — RequireArtifactOwner refuses without it."),
        new OwnerWriteEntry("DataverseWebApiService.cs", "CreateEventAsync", 1, OwnerWriteKind.Seam,
            "CreateEventRequest.OwningTeamId — throws without it."),
        new OwnerWriteEntry("DataverseWebApiService.cs", "CreateEventLogAsync", 1, OwnerWriteKind.Seam,
            "The owner EventEndpoints resolved as content of the event; null only when the resolver answered Unchanged."),
        new OwnerWriteEntry("ExternalDataService.cs", "BuildDocumentCreatePayload", 1, OwnerWriteKind.Seam,
            "The team the external route resolved from the project — RequireOwner refuses an empty one."),
        new OwnerWriteEntry("ExternalDataService.cs", "BuildEventCreatePayload", 1, OwnerWriteKind.Seam,
            "The team the external route resolved from the project — RequireOwner refuses an empty one."),
        new OwnerWriteEntry("ExternalDataService.cs", "BuildTodoCreatePayload", 1, OwnerWriteKind.Seam,
            "The team the external route resolved from the root — RequireOwner refuses an empty one."),

        new OwnerWriteEntry("DocumentCheckoutService.cs", "CreateFileVersionAsync", 1, OwnerWriteKind.Routed,
            "A file version is content of its document (ContentOf)."),
        new OwnerWriteEntry("TodoGenerationService.cs", "CreateTodoAsync", 1, OwnerWriteKind.Routed,
            "Generated to-dos owned from their source event."),
        new OwnerWriteEntry("SpendSnapshotService.cs", "GenerateAsync", 1, OwnerWriteKind.Routed,
            "Matter spend snapshots — owned by the matter's team."),
        new OwnerWriteEntry("SpendSnapshotService.cs", "GenerateForProjectAsync", 1, OwnerWriteKind.Routed,
            "Project spend snapshots — owned by the project's team."),
        new OwnerWriteEntry("SignalEvaluationService.cs", "UpsertSignalAsync", 1, OwnerWriteKind.Routed,
            "Owned by the matter's team."),
        new OwnerWriteEntry("DataverseObservationMirror.cs", "MirrorAsync", 1, OwnerWriteKind.Routed,
            "ForChild over the mirrored analysis row."),
        new OwnerWriteEntry("InvoiceReviewService.cs", "BuildInvoiceCreateFields", 1, OwnerWriteKind.Routed,
            "The invoice owned by the matter's team (task 130 / G5), resolved by the caller of the builder."),
        new OwnerWriteEntry("ComposeCreateOnSavePromoter.cs", "PromoteIfEphemeralAsync", 1, OwnerWriteKind.Routed,
            "Owned from the inherited filing links or the caller."),
        new OwnerWriteEntry("OfficeService.cs", "QuickCreateAsync", 1, OwnerWriteKind.Routed,
            "Office invoice quick-create — the resolver's team (task 080)."),
        new OwnerWriteEntry("OfficeService.cs", "CreateTodoAsync", 1, OwnerWriteKind.Routed,
            "Office to-do — secure-if-any over regarding, stamps and carriers."),
        new OwnerWriteEntry("EmailUploadCaptureService.cs", "CaptureAsync", 1, OwnerWriteKind.Routed,
            "Upload capture — the team the evaluated filing resolved."),
        new OwnerWriteEntry("IncomingCommunicationProcessor.cs", "CreateCommunicationRecordAsync", 1, OwnerWriteKind.Routed,
            "Inbound email — the team the evaluated filing resolved."),
        new OwnerWriteEntry("IncomingCommunicationProcessor.cs", "ApplyOwner", 1, OwnerWriteKind.Routed,
            "Inbound .eml / attachment rows — owned like the email."),
        new OwnerWriteEntry("ThreadResolver.cs", "CreateRecordThreadAsync", 1, OwnerWriteKind.Routed,
            "A thread created for a message — its anchor record's team (S6); a Direct thread keeps its creator (E2)."),
        new OwnerWriteEntry("ThreadResolver.cs", "CreateThreadAsync", 1, OwnerWriteKind.Routed,
            "A thread created on request — the record's team for an ownership parent (409 on refusal), else the caller."),
        new OwnerWriteEntry("ThreadResolver.cs", "FindOrCreateDefaultThreadAsync", 1, OwnerWriteKind.Routed,
            "A record's default thread — its record's team; the per-user master thread keeps its user (E2)."),
        new OwnerWriteEntry("TaskActionCore.cs", "CreateAsync", 1, OwnerWriteKind.Routed,
            "AI create-task — ForChild over the stamped parents."),
        new OwnerWriteEntry("OwnedChildWrite.cs", "CreateAsync", 1, OwnerWriteKind.Routed,
            "The chat tools' owned create (owner S1 / G5) — the resolver's team, after the as-the-caller checks."),
        new OwnerWriteEntry("RecordCreationService.cs", "CreateMatterAsync", 1, OwnerWriteKind.Root,
            "Office quick-create of a MATTER (a root) — owned by the resolver's team for the acting user (task 080)."),
        new OwnerWriteEntry("RecordCreationService.cs", "CreateProjectAsync", 1, OwnerWriteKind.Root,
            "Office quick-create of a PROJECT (a root) — owned by the resolver's team for the acting user (task 080)."),

        new OwnerWriteEntry("ProvisionProjectEndpoint.cs", "AssignOwnerToSecureTeamAsync", 1, OwnerWriteKind.Root,
            "Secure provisioning assigns the ROOT to the named Secure team (task 144)."),
        new OwnerWriteEntry("UnsecureProjectEndpoint.cs", "UnsecureProjectAsync", 1, OwnerWriteKind.Root,
            "Un-securing hands the ROOT back to a user (task 144 / F3)."),
        new OwnerWriteEntry("WorkAssignmentEndpoints.cs", "CreateWorkAssignmentAsync", 1, OwnerWriteKind.Root,
            "A work assignment (a ROOT) created owned by its assignee; S6 b (inline secure provisioning) is escalated."),

        new OwnerWriteEntry("OutboxService.cs", "WriteAsync", 1, OwnerWriteKind.PerUser,
            "sprk_notificationoutbox — one row per recipient user."),
        new OwnerWriteEntry("WorkspaceLayoutService.cs", "CreateLayoutAsync", 1, OwnerWriteKind.PerUser,
            "sprk_workspacelayout — a user's own layout."),
        new OwnerWriteEntry("NotificationService.cs", "CreateNotificationAsync", 1, OwnerWriteKind.PerUser,
            "appnotification — owned by its recipient so only they see it."),
        new OwnerWriteEntry("NotificationActionCore.cs", "BuildNotificationEntity", 1, OwnerWriteKind.PerUser,
            "appnotification from a playbook — owned by its recipient."),
        new OwnerWriteEntry("DirectThreadAccessService.cs", "FindOrCreateDirectThreadAsync", 1, OwnerWriteKind.PerUser,
            "A Direct (two-party) thread — per-participant by design (E2)."),

        new OwnerWriteEntry("CommunicationEnrichmentService.cs", "AssignOwningTeamAsync", 1, OwnerWriteKind.UnfiledOnly,
            "FR-E7 category routing — a communication FILED under a record is the resolver's (r2): routing applies only to "
            + "a row filed under nothing (IsFiledOrUnreadableAsync first)."),
    };

    /// <summary>The gate an <see cref="OwnerWriteKind.UnfiledOnly"/> member must call before its owner write.</summary>
    private const string UnfiledGate = "IsFiledOrUnreadableAsync";

    /// <summary>A const whose value is the owner key — <c>"ownerid"</c> or <c>"ownerid@odata.bind"</c>.</summary>
    private static readonly Regex OwnerKeyConst = new(
        @"const\s+string\s+(?<name>\w+)\s*=\s*""ownerid(?:@odata\.bind)?""\s*;", RegexOptions.Compiled);

    [Fact(DisplayName = "Task 146 r2: every owner write in the server is censused, by member, with its kind")]
    public void EveryOwnerWriteIsCensused()
    {
        var actual = ScanOwnerWrites(ServerFiles());
        var expected = OwnerWrites.ToDictionary(e => (e.FileName, e.Member), e => e.Writes);
        var problems = new List<string>();

        foreach (var ((file, member), lines) in actual.OrderBy(kv => kv.Key.File, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue((file, member), out var count))
                problems.Add($"UNLISTED owner write in {file}.{member}: lines {string.Join(", ", lines)}");
            else if (count != lines.Count)
                problems.Add($"COUNT CHANGED for {file}.{member}: census says {count}, source has {lines.Count} (lines {string.Join(", ", lines)})");
        }

        foreach (var key in expected.Keys.Where(k => !actual.ContainsKey(k)))
            problems.Add($"CENSUSED BUT ABSENT: {key.FileName}.{key.Member}. Remove the entry if the owner write was deleted.");

        Assert.True(
            problems.Count == 0,
            "The owner-write census does not match the source (unified-access-control-r2 task 146 r2). A child's owner is "
            + "IRecordOwnershipResolver's answer — no writer computes a team itself. Route the new write through the resolver "
            + "(or classify it with its reason), then list it:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 146 r2: Routed owner writes sit in files that call the resolver; UnfiledOnly writes check the filing first")]
    public void EveryOwnerWriteKeepsItsKind()
    {
        var files = ServerFiles();
        var problems = new List<string>();

        foreach (var entry in OwnerWrites.Where(e => e.Kind == OwnerWriteKind.Routed))
        {
            if (CodeOf(files, entry.FileName) is not { } code || !ResolverCall.IsMatch(code))
                problems.Add($"{entry.FileName}.{entry.Member}: Routed, but the file makes no IRecordOwnershipResolver call");
        }

        foreach (var entry in OwnerWrites.Where(e => e.Kind == OwnerWriteKind.UnfiledOnly))
        {
            var body = CodeOf(files, entry.FileName) is { } code ? MethodBody(code, entry.Member) : null;
            if (body is null || !UnfiledOnlyGateComesFirst(body))
                problems.Add($"{entry.FileName}.{entry.Member}: UnfiledOnly, but it does not call {UnfiledGate} before its owner write");
        }

        Assert.True(problems.Count == 0, "Owner writes that no longer keep their kind:\n" + string.Join("\n", problems));
    }

    /// <summary>True when <paramref name="memberBody"/> calls the filing gate before its first owner write.</summary>
    private static bool UnfiledOnlyGateComesFirst(string memberBody)
    {
        var gate = Regex.Match(memberBody, @"\b" + UnfiledGate + @"\s*\(");
        var write = Regex.Match(memberBody, @"\[\s*""ownerid(?:@odata\.bind)?""\s*\]\s*=(?!=)");
        return gate.Success && write.Success && gate.Index < write.Index;
    }

    [Fact(DisplayName = "Task 146 r2: negative control — an unlisted owner override is found, by member; a lost filing gate is found")]
    public void OwnerWriteDetector_NegativeControl()
    {
        // The verifier's finding, seeded: a category-routing owner override inside a file the create census lists.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private const string OwnerKey = \"ownerid@odata.bind\";",
            "    private async Task AssignCategoryTeamAsync(Dictionary<string, object> fields, Guid teamId)",
            "    {",
            "        fields[\"ownerid\"] = new EntityReference(\"team\", teamId);",
            "    }",
            "    private void Bind(Dictionary<string, object?> body, Guid team) { body[OwnerKey] = $\"/teams({team})\"; }",
            "    private void Read(Entity e) { var o = e.GetAttributeValue<EntityReference>(\"ownerid\"); var x = e[\"ownerid\"] == null; }",
            "}",
        });

        var sites = ScanOwnerWrites(new Dictionary<string, string> { ["Seeded.cs"] = code });

        Assert.Equal(2, sites.Count);
        Assert.Single(sites[("Seeded.cs", "AssignCategoryTeamAsync")]);
        Assert.Single(sites[("Seeded.cs", "Bind")]);

        Assert.False(UnfiledOnlyGateComesFirst(
            "{ var teamId = await ResolveTeamIdByNameAsync(n, ct); fields[\"ownerid\"] = new EntityReference(\"team\", teamId); }"));
        Assert.True(UnfiledOnlyGateComesFirst(
            "{ if (await " + UnfiledGate + "(id, ct)) return; fields[\"ownerid\"] = new EntityReference(\"team\", t); }"));
    }

    /// <summary>(file, member) → the 1-based lines of each owner write in it.</summary>
    private static Dictionary<(string File, string Member), List<int>> ScanOwnerWrites(IReadOnlyDictionary<string, string> files)
    {
        var globalKeys = files.Values
            .SelectMany(code => OwnerKeyConst.Matches(code).Select(m => m.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);

        var sites = new Dictionary<(string, string), List<int>>();
        foreach (var (fileName, code) in files)
        {
            var keys = OwnerKeyConst.Matches(code).Select(m => Regex.Escape(m.Groups["name"].Value))
                .Concat(globalKeys.Select(Regex.Escape))
                .Distinct()
                .Prepend(@"""ownerid(?:@odata\.bind)?""");
            var write = new Regex(@"\[\s*(?:" + string.Join("|", keys) + @")\s*\]\s*=(?!=)");

            var members = MemberDeclaration.Matches(code).ToList();
            foreach (Match m in write.Matches(code))
            {
                var declaration = members.LastOrDefault(d => d.Index <= m.Index);
                var name = declaration is null
                    ? "(file)"
                    : Regex.Match(code[declaration.Index..], @"\b(?<name>\w+)\s*(?:<[^>()]*>)?\s*\(").Groups["name"].Value;
                var key = (fileName, name);
                if (!sites.TryGetValue(key, out var list))
                    sites[key] = list = new List<int>();
                list.Add(SourceScan.LineOf(code, m.Index));
            }
        }

        return sites;
    }

    /// <summary>An external payload builder that refuses an empty team (<c>RequireOwner(owningTeamId, …)</c>) AND writes
    /// <c>[OwnerBindKey] = $"/teams({owningTeamId})"</c> — the very team it was handed, onto the payload it returns.</summary>
    private static Regex RefusesAndBindsTheOwner => new(
        @"(?s)^(?=.*\bRequireOwner\s*\(\s*owningTeamId\b)(?=.*\[\s*OwnerBindKey\s*\]\s*=\s*\$""/teams\(\{owningTeamId\}\)"")");


    /// <summary>A resolver call — the ONE owner (task 146 constraint: no writer computes a team itself). r2: the chat tools'
    /// one owned-create path (<c>OwnedChildWrite.CreateAsync</c>, itself an UnscannedWriter that calls the resolver).</summary>
    private static readonly Regex ResolverCall = new(
        @"\b(ResolveOwnerAsync|ResolveOwningTeamAsync|ReparentAsync|AssignToThreadReconcilingOwnerAsync|ResolveDocumentOwnerTeamAsync|OwnedChildWrite\.CreateAsync)\s*\(",
        RegexOptions.Compiled);

    /// <summary>An owner write onto a row about to be created.</summary>
    private static readonly Regex OwnerWrite = new(
        @"""ownerid""|ownerid@odata\.bind|\.ApplyTo\s*\(|ApplyContentOwner\s*\(|ApplyOwner\s*\(|OwningTeamId\s*=|ArtifactOwningTeamProperty|OwnerBindKey",
        RegexOptions.Compiled);

    private static readonly Regex EntityCreate = new(
        @"new\s+(?:Microsoft\.Xrm\.Sdk\.)?(?:DataverseEntity|Entity)\s*\(\s*(?:""(?<lit>[a-z_]+)""|(?<id>[A-Za-z_][\w\.]*))\s*\)",
        RegexOptions.Compiled);

    private static readonly Regex WebApiPost = new(
        @"(?:SendPostAsJsonAsync|PostAsJsonAsync)\s*\(\s*(?:""(?<lit>[a-z_]+)""|(?<id>[A-Za-z_][\w\.]*))",
        RegexOptions.Compiled);

    private static readonly Regex HttpPost = new(
        @"new\s+HttpRequestMessage\s*\(\s*HttpMethod\.Post\s*,\s*""(?<lit>[a-z_]+)""",
        RegexOptions.Compiled);

    private static readonly Regex StringConst = new(
        @"const\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[a-z_]+)""\s*;",
        RegexOptions.Compiled);

    // =============================================================================================

    [Fact(DisplayName = "Task 146: every server create of a child-record table is in the census, with the expected count")]
    public void EveryChildCreateSiteIsCensused()
    {
        var actual = ScanSites(ServerFiles());
        var expected = Census.ToDictionary(e => (e.FileName, e.Table), e => e.Sites);
        var problems = new List<string>();

        foreach (var ((fileName, table), sites) in actual.OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue((fileName, table), out var count))
            {
                problems.Add($"UNLISTED create of {table} in {fileName}:\n" + string.Join("\n", sites.Select(s => $"    {s}")));
            }
            else if (sites.Count != count)
            {
                problems.Add($"COUNT CHANGED for {table} in {fileName}: census says {count}, source has {sites.Count}:\n"
                             + string.Join("\n", sites.Select(s => $"    {s}")));
            }
        }

        foreach (var key in expected.Keys.Where(k => !actual.ContainsKey(k)))
        {
            problems.Add($"CENSUSED BUT ABSENT: {key.Item2} in {key.Item1}. Remove the entry if the create was deleted.");
        }

        Assert.True(
            problems.Count == 0,
            "The child-record create census does not match the source (unified-access-control-r2 task 146).\n\n"
            + "A failure is NOT a prompt to bump a count: a create of a secure record's child appeared, moved or "
            + "vanished. Route its owner through IRecordOwnershipResolver (every parent lookup it writes, "
            + "secure-if-any; refuse in the writer's own error contract), then list it — see the MAINTENANCE "
            + "PROCEDURE above the census.\n\n"
            + string.Join("\n\n", problems));
    }

    [Fact(DisplayName = "Task 146: every Routed entry decides its owner through the resolver and writes it")]
    public void EveryRoutedEntryRoutesThroughTheResolver()
    {
        var files = ServerFiles();
        var problems = new List<string>();
        foreach (var entry in Census.Where(e => e.Disposition == Disposition.Routed))
        {
            var ownerFile = entry.OwnerFile ?? entry.FileName;
            var code = CodeOf(files, ownerFile);
            if (code is null)
            {
                problems.Add($"{entry.Table} in {entry.FileName}: owner file {ownerFile} not found");
                continue;
            }

            if (!ResolverCall.IsMatch(code))
                problems.Add($"{entry.Table} in {entry.FileName}: {ownerFile} makes no IRecordOwnershipResolver call");

            if (!OwnerWrite.IsMatch(CodeOf(files, entry.FileName) ?? string.Empty) && !OwnerWrite.IsMatch(code))
                problems.Add($"{entry.Table} in {entry.FileName}: no owner write (ownerid / ApplyTo / OwningTeamId)");
        }

        Assert.True(problems.Count == 0, "Routed census entries that no longer route their owner:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Task 146 r1 (verifier item 6): the file-level check above passes when a file holds ANY resolver call and ANY owner
    /// write — so removing one site's owner write in a multi-site file went unnoticed (IncomingCommunicationProcessor's
    /// attachment document, CommunicationService's shared-mailbox send). This asserts it PER SITE: the row a Routed create
    /// builds must itself receive an owner write in the member that builds it (see <see cref="SiteOwnerProblems"/>).
    /// </summary>
    [Fact(DisplayName = "Task 146 r1: every Routed create SITE writes its own row's owner, in the member that builds it")]
    public void EveryRoutedCreateSiteWritesItsOwnRowsOwner()
    {
        var files = ServerFiles();
        var routed = Census
            .Where(e => e.Disposition == Disposition.Routed && e.OwnerFile is null)
            .Select(e => (e.FileName, e.Table))
            .ToHashSet();

        var problems = ScanSiteIndexes(files)
            .Where(kv => routed.Contains(kv.Key))
            .SelectMany(kv => SiteOwnerProblems(files[kv.Key.File], kv.Key.File, kv.Key.Table, kv.Value))
            .ToList();

        Assert.True(
            problems.Count == 0,
            "Routed create sites whose row receives no owner write in the member that builds it (task 146 r1). Write "
            + "the resolved owner onto THAT row — resolution.ApplyTo(row), row[\"ownerid\"] = …, or the file's "
            + "Apply…Owner(row, …) helper — before it is created:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// Run-as-user writes that neither create a row nor change what a row is filed under (so not census entries), each
    /// with its reason. With <see cref="Census"/> and <see cref="UnscannedWriters"/> (which must call the resolver) this
    /// classifies every run-as-user POST and PATCH in the BFF.
    /// </summary>
    /// <remarks>r2: the two run-as-user create tools are no longer escalated — they route their owner (owner S1 / G5) and
    /// are <see cref="UnscannedWriters"/>; so is the update tool, whose re-file the r1 census never saw (verifier item 2).</remarks>
    private static readonly IReadOnlyDictionary<string, string> RunAsUserWritesThatFileNothing = new Dictionary<string, string>
    {
        ["DataverseSearchDataHandler.cs"] = "POSTs to the Dataverse search action (searchquery) — a READ; writes no row.",
        ["WorkProductRecordPersister.cs"] = "PATCHes ONE registry-declared text column (the work-product envelope JSON) on "
                                            + "the session's host record — never a lookup, so it files nothing anywhere.",
    };

    [Fact(DisplayName = "Task 146 r2: every run-as-user POST and PATCH in the BFF is classified — routed, or files nothing")]
    public void EveryRunAsUserWriteIsClassified()
    {
        var files = ServerFiles();
        var unclassified = UnclassifiedRunAsUserWrites(files);

        Assert.True(
            unclassified.Count == 0,
            "Run-as-user (IDataverseUserClient) POSTs / PATCHes that the census does not classify (task 146 r2). A run-as-"
            + "user CREATE of a child record leaves it owned by the caller in an ordinary business unit, and a run-as-user "
            + "PATCH of a lookup re-files a child with no owner re-derivation: route the owner through IRecordOwnershipResolver "
            + "and list the file in UnscannedWriters; a write that files nothing goes in RunAsUserWritesThatFileNothing with "
            + "its reason:\n  " + string.Join("\n  ", unclassified));

        var stale = RunAsUserWritesThatFileNothing.Keys
            .Where(name => CodeOf(files, name) is not { } code || !RunAsUserWrite.IsMatch(code))
            .Select(name => $"{name}: no run-as-user write any more — delete the entry")
            .ToList();
        Assert.True(stale.Count == 0, "Stale run-as-user classification entries:\n" + string.Join("\n", stale));
    }

    /// <summary>A POST or PATCH through the run-as-user client field (<c>_dataverse</c> of type <c>IDataverseUserClient</c>).</summary>
    private static readonly Regex RunAsUserWrite = new(@"\b_dataverse\s*\.\s*(?:PostAsync|PatchAsync)\s*\(", RegexOptions.Compiled);

    /// <summary>Files that POST or PATCH through the run-as-user client and are classified nowhere.</summary>
    private static List<string> UnclassifiedRunAsUserWrites(IReadOnlyDictionary<string, string> files)
    {
        var classified = RunAsUserWritesThatFileNothing.Keys
            .Concat(Census.Select(e => e.FileName))
            .Concat(UnscannedWriters.Select(w => w.FileName))
            .ToHashSet(StringComparer.Ordinal);

        return files
            .Where(f => f.Value.Contains("IDataverseUserClient", StringComparison.Ordinal) && RunAsUserWrite.IsMatch(f.Value))
            .Select(f => f.Key)
            .Where(name => !classified.Contains(name))
            .ToList();
    }

    [Fact(DisplayName = "Task 146 r2: negative control — an unlisted run-as-user POST or PATCH fails the classification")]
    public void RunAsUserWriteDetector_NegativeControl()
    {
        var files = new Dictionary<string, string>
        {
            ["NewCreateHandler.cs"] = SourceScan.CodeText(new[]
            {
                "internal sealed class NewCreateHandler(IDataverseUserClient _dataverse)",
                "{",
                "    Task A() => _dataverse.PostAsync($\"/api/data/v9.2/{set}\", body, ct);",
                "}",
            }),
            ["NewRefileHandler.cs"] = SourceScan.CodeText(new[]
            {
                "internal sealed class NewRefileHandler(IDataverseUserClient _dataverse)",
                "{",
                "    Task A() => _dataverse.PatchAsync($\"{set}({id:D})\", body, ct);",
                "}",
            }),
            ["DataverseSearchDataHandler.cs"] = "IDataverseUserClient _dataverse; _dataverse.PostAsync(x);",
        };

        Assert.Equal(new[] { "NewCreateHandler.cs", "NewRefileHandler.cs" }, UnclassifiedRunAsUserWrites(files));
    }

    [Fact(DisplayName = "Task 146: every seam refuses a create with no resolved owner team")]
    public void EverySeamRefusesAnOwnerlessCreate()
    {
        var files = ServerFiles();
        var problems = new List<string>();
        foreach (var seam in Seams)
        {
            var code = CodeOf(files, seam.FileName);
            var body = code is null ? null : MethodBody(code, seam.Method);
            if (body is null)
                problems.Add($"{seam.FileName}.{seam.Method}: method not found");
            else if (!seam.Guard.IsMatch(body))
                problems.Add($"{seam.FileName}.{seam.Method}: no owner refusal (expected /{seam.Guard}/)");
        }

        Assert.True(problems.Count == 0, "Owner seams that no longer refuse an owner-less create:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 146: every writer the scanner cannot see still calls the resolver")]
    public void EveryUnscannedWriterRoutesThroughTheResolver()
    {
        var files = ServerFiles();
        var problems = UnscannedWriters
            .Where(w => CodeOf(files, w.FileName) is not { } code || !ResolverCall.IsMatch(code))
            .Select(w => $"{w.FileName} ({w.Shape}): no IRecordOwnershipResolver call")
            .ToList();

        Assert.True(problems.Count == 0, "Listed writers that no longer route their owner:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 146: every census entry is explained; every waiver names its kind")]
    public void EveryEntryIsExplained()
    {
        var problems = Census
            .Where(e => string.IsNullOrWhiteSpace(e.Reason) || e.Reason.Trim().Length < 60
                        || (e.Disposition == Disposition.Waived) != (e.Waiver != WaiverKind.None)
                        || !ChildTables.Contains(e.Table))
            .Select(e => $"{e.Table} in {e.FileName}")
            .ToList();

        Assert.True(
            problems.Count == 0,
            "Every entry needs a substantive reason, a known child table, and — exactly when waived — a Pending or "
            + "Permanent kind: " + string.Join(", ", problems));
    }

    // =============================================================================================
    // Negative controls
    // =============================================================================================

    [Fact(DisplayName = "Task 146: negative control — the detector finds both create forms and ignores non-creates")]
    public void Detector_NegativeControl()
    {
        var seeded = new Dictionary<string, string>
        {
            // Comment-stripped exactly as ServerFiles() strips the real source.
            ["Seeded.cs"] = SourceScan.CodeText(new[]
            {
                "internal sealed class Seeded",
                "{",
                "    private const string MemoEntity = \"sprk_memo\";",
                "    void A() { var a = new Entity(\"sprk_todo\"); }",
                "    void B() { var b = new DataverseEntity(MemoEntity); }",
                "    void C() { _ = SendPostAsJsonAsync(\"sprk_events\", payload, ct); }",
                "    void D() { var update = new Entity(\"sprk_todo\", id); }",
                "    // var commented = new Entity(\"sprk_document\");",
                "    void E() { var notAChild = new Entity(\"appnotification\"); }",
                "}",
            }),
        };

        var sites = ScanSites(seeded);

        Assert.Equal(3, sites.Count);
        Assert.True(sites.ContainsKey(("Seeded.cs", "sprk_todo")));
        Assert.True(sites.ContainsKey(("Seeded.cs", "sprk_memo")));
        Assert.True(sites.ContainsKey(("Seeded.cs", "sprk_event")));
        Assert.Single(sites[("Seeded.cs", "sprk_todo")]); // the two-argument update is not a create
    }

    [Fact(DisplayName = "Task 146 r1: negative control — the per-site check flags the one site in a multi-site member that lost its owner")]
    public void PerSiteDetector_NegativeControl()
    {
        // The shape of the plants the file-level check missed: two creates in one member, ONE owner write.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private async Task ArchiveAsync(RecordOwnerResolution owner)",
            "    {",
            "        var attachmentDoc = new DataverseEntity(\"sprk_document\");",
            "        ApplyOwner(attachmentDoc, owner);",
            "        await _svc.CreateAsync(attachmentDoc, ct);",
            "        var emlDoc = new DataverseEntity(\"sprk_document\");",
            "        await _svc.CreateAsync(emlDoc, ct);",
            "    }",
            "    private async Task InlineAsync(RecordOwnerResolution owner)",
            "    {",
            "        await _svc.CreateAsync(new Entity(\"sprk_document\") { [\"ownerid\"] = team }, ct);",
            "        await _svc.CreateAsync(new Entity(\"sprk_document\") { [\"sprk_name\"] = \"x\" }, ct);",
            "    }",
            "    private async Task ResolvedAsync(RecordOwnerResolution owner)",
            "    {",
            "        var row = new Entity(\"sprk_document\");",
            "        owner.ApplyTo(row);",
            "        await _svc.CreateAsync(row, ct);",
            "    }",
            "}",
        });
        var files = new Dictionary<string, string> { ["Seeded.cs"] = code };

        var sites = ScanSiteIndexes(files)[("Seeded.cs", "sprk_document")];
        var problems = SiteOwnerProblems(code, "Seeded.cs", "sprk_document", sites).ToList();

        Assert.Equal(5, sites.Count);
        Assert.Equal(2, problems.Count); // the emlDoc (its member has an owner write — for the OTHER row) and the bare inline create
        Assert.Contains(problems, p => p.Contains("Seeded.cs:8", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Seeded.cs:14", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 146: negative control — the scan reaches every server assembly")]
    public void Scan_ReachesEveryServerAssembly()
    {
        var scanned = SourceScan.ServerSourceFiles().Select(SourceScan.Relative).ToList();
        Assert.Contains(scanned, f => f.Contains("Spaarke.Dataverse", StringComparison.Ordinal));
        Assert.Contains(scanned, f => f.Contains("Sprk.Bff.Api", StringComparison.Ordinal));
        Assert.Contains(scanned, f => f.Contains("Spaarke.Core", StringComparison.Ordinal));
    }

    // =============================================================================================

    /// <summary>File name → comment-stripped code, for every server source file.</summary>
    private static Dictionary<string, string> ServerFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in SourceScan.ServerSourceFiles())
        {
            var name = Path.GetFileName(path);
            var code = SourceScan.CodeText(File.ReadAllLines(path));
            // Same-named files in different folders are concatenated: the census keys on file NAME (as the
            // credential census does), and a duplicate name must not hide one file's sites behind the other's.
            files[name] = files.TryGetValue(name, out var existing) ? existing + "\n" + code : code;
        }

        return files;
    }

    private static string? CodeOf(IReadOnlyDictionary<string, string> files, string fileName)
        => files.TryGetValue(fileName, out var code) ? code : null;

    /// <summary>(file, table) → the 1-based lines of each create site.</summary>
    private static Dictionary<(string File, string Table), List<int>> ScanSites(IReadOnlyDictionary<string, string> files)
        => ScanSiteIndexes(files).ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Select(index => SourceScan.LineOf(files[kv.Key.File], index)).ToList());

    /// <summary>A member declaration line: an access modifier, then a parameter list (methods, constructors, local
    /// members with a modifier). Field initializers (<c>= new(...)</c>) are excluded by the no-<c>=</c> rule.</summary>
    private static readonly Regex MemberDeclaration = new(
        @"^[ \t]*(?:public|internal|private|protected)\b[^;=\n]*?\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// The owner problems of one routed file's create sites (task 146 r1): each site's row must receive an owner write in
    /// the MEMBER that builds it — the text from the member declaration before the site to the next one. When the row is
    /// assigned to a variable, the write must name THAT variable (<c>v["ownerid"] =</c>, <c>.ApplyTo(v)</c>,
    /// <c>Apply…Owner(v, …)</c>) or sit in the construction's own initializer; an inline construction falls back to any
    /// owner write in the member. Crude by design, like every <see cref="SourceScan"/> rule — proven to bite by
    /// <see cref="PerSiteDetector_NegativeControl"/>.
    /// </summary>
    private static IEnumerable<string> SiteOwnerProblems(string code, string fileName, string table, IEnumerable<int> siteIndexes)
    {
        var members = MemberDeclaration.Matches(code).Select(m => m.Index).ToList();

        string RegionAt(int index)
        {
            var start = members.LastOrDefault(i => i <= index);
            var end = members.FirstOrDefault(i => i > index);
            return code[start..(end > index ? end : code.Length)];
        }

        // `row["ownerid"]` — or `row[FieldOwnerId]` through a const whose value is "ownerid".
        var ownerKey = string.Join("|", StringConst.Matches(code)
            .Where(m => m.Groups["value"].Value == "ownerid")
            .Select(m => Regex.Escape(m.Groups["name"].Value))
            .Prepend(@"""ownerid"""));

        // Members of this file that write an owner onto their FIRST parameter — a create helper the row is handed to
        // (CommunicationEnrichmentService.CreateReviewLogAsync(entity, …) resolves and applies the owner itself).
        var ownerWritingHelpers = MemberDeclaration.Matches(code)
            .Select(m => (Match: m, Signature: Regex.Match(code[m.Index..], @"\b(?<name>\w+)\s*\(\s*(?:[\w\.<>\?]+\s+)(?<param>\w+)\s*[,)]")))
            .Where(x => x.Signature.Success
                        && WritesOwnerOn(RegionAt(x.Match.Index), x.Signature.Groups["param"].Value, ownerKey, helpers: null))
            .Select(x => x.Signature.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var index in siteIndexes)
        {
            var region = RegionAt(index);
            var statementEnd = code.IndexOf(';', index);
            var construction = code[index..(statementEnd < 0 ? code.Length : statementEnd)];
            var lead = code[Math.Max(0, index - 120)..index];
            var assigned = Regex.Match(lead, @"\b(?<v>[A-Za-z_]\w*)\s*=\s*$");
            var isPost = !construction.StartsWith("new", StringComparison.Ordinal)
                         || construction.StartsWith("new HttpRequestMessage", StringComparison.Ordinal);

            bool written;
            if (Regex.IsMatch(construction, ownerKey))
            {
                written = true; // the construction's own initializer sets the owner
            }
            else if (isPost)
            {
                // A Web API POST: the row is a JSON payload built in the member — its owner bind must be there.
                written = OwnerWrite.IsMatch(region);
            }
            else if (assigned.Success)
            {
                var v = assigned.Groups["v"].Value;
                written = WritesOwnerOn(region, v, ownerKey, ownerWritingHelpers)
                          || BuilderCallersWriteOwner(code, region, v, ownerKey, ownerWritingHelpers, RegionAt);
            }
            else
            {
                // An inline construction cannot be named afterwards, so its owner is either in its own initializer
                // (handled above) or it is handed straight to an owner-writing call.
                written = Regex.IsMatch(lead, @"(?:\bApply\w*Owner\w*|\.ApplyTo)\s*\(\s*$");
            }

            if (!written)
                yield return $"    {fileName}:{SourceScan.LineOf(code, index)} — {table} created with no owner write on its row";
        }
    }

    /// <summary>True when <paramref name="region"/> writes an owner onto the row variable <paramref name="v"/>.</summary>
    private static bool WritesOwnerOn(string region, string v, string ownerKey, IReadOnlySet<string>? helpers)
    {
        var name = Regex.Escape(v);
        if (Regex.IsMatch(region,
                $@"\b{name}\s*\[\s*(?:{ownerKey})\s*\]\s*=|\.ApplyTo\s*\(\s*{name}\s*\)|\bApply\w*Owner\w*\s*\(\s*{name}\b"))
        {
            return true;
        }

        return helpers is { Count: > 0 }
               && Regex.IsMatch(region, $@"\b(?:{string.Join("|", helpers.Select(Regex.Escape))})\s*\(\s*{name}\b");
    }

    /// <summary>
    /// A BUILDER member — one that returns the row it constructs (<c>return v;</c>) without owning it — is acceptable
    /// only when EVERY caller of it in the file writes the owner onto what it receives
    /// (<c>var row = BuildRow(…); owner.ApplyTo(row);</c>). A builder nobody calls fails.
    /// </summary>
    private static bool BuilderCallersWriteOwner(
        string code, string region, string v, string ownerKey, IReadOnlySet<string> helpers, Func<int, string> regionAt)
    {
        if (!Regex.IsMatch(region, $@"\breturn\s+{Regex.Escape(v)}\s*;"))
            return false;

        var builder = Regex.Match(region, @"^[ \t]*(?:public|internal|private|protected)\b[^;=\n]*?\b(?<name>\w+)\s*\(",
            RegexOptions.Multiline);
        if (!builder.Success)
            return false;

        var calls = Regex.Matches(code,
            $@"\b(?<w>[A-Za-z_]\w*)\s*=\s*(?:await\s+)?{Regex.Escape(builder.Groups["name"].Value)}\s*\(");
        return calls.Count > 0 && calls.All(c => WritesOwnerOn(regionAt(c.Index), c.Groups["w"].Value, ownerKey, helpers));
    }

    /// <summary>(file, table) → the character index of each create site.</summary>
    private static Dictionary<(string File, string Table), List<int>> ScanSiteIndexes(IReadOnlyDictionary<string, string> files)
    {
        // Constants are resolved within the file first, then across the server (a qualified ComposeService.X).
        var globalConsts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var code in files.Values)
        {
            foreach (Match m in StringConst.Matches(code))
                globalConsts.TryAdd(m.Groups["name"].Value, m.Groups["value"].Value);
        }

        var sites = new Dictionary<(string, string), List<int>>();
        foreach (var (fileName, code) in files)
        {
            var localConsts = StringConst.Matches(code)
                .GroupBy(m => m.Groups["name"].Value)
                .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value, StringComparer.Ordinal);

            string? Resolve(Match m)
            {
                if (m.Groups["lit"].Success)
                    return m.Groups["lit"].Value;
                var name = m.Groups["id"].Value.Split('.').Last();
                return localConsts.TryGetValue(name, out var v) ? v : globalConsts.GetValueOrDefault(name);
            }

            void Add(string? table, int index)
            {
                if (table is null || !ChildTables.Contains(table))
                    return;
                var key = (fileName, table);
                if (!sites.TryGetValue(key, out var list))
                    sites[key] = list = new List<int>();
                list.Add(index);
            }

            foreach (Match m in EntityCreate.Matches(code))
                Add(Resolve(m), m.Index);

            foreach (Match m in WebApiPost.Matches(code))
                Add(TableOfEntitySet(Resolve(m)), m.Index);

            foreach (Match m in HttpPost.Matches(code))
                Add(TableOfEntitySet(Resolve(m)), m.Index);
        }

        return sites;
    }

    private static string? TableOfEntitySet(string? entitySet)
    {
        if (string.IsNullOrEmpty(entitySet))
            return null;
        if (entitySet == "sprk_analyses")
            return "sprk_analysis";
        return entitySet.EndsWith('s') && ChildTables.Contains(entitySet[..^1]) ? entitySet[..^1] : null;
    }

    /// <summary>The text of the first method named <paramref name="method"/> (declaration to its closing brace).</summary>
    private static string? MethodBody(string code, string method)
    {
        var decl = new Regex(@"(?:public|internal|private|protected)[^;{=]*?\b" + Regex.Escape(method) + @"\s*\(");
        var m = decl.Match(code);
        if (!m.Success)
            return null;

        var open = code.IndexOf('{', m.Index);
        if (open < 0)
            return null;

        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
                depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[m.Index..(i + 1)];
        }

        return null;
    }
}
