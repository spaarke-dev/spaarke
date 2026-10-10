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
/// construction is an update or a keyed upsert target, unless its id is fresh or the row is handed to a create),
/// <c>new Entity { LogicalName = "t" }</c> and <c>e.LogicalName = "t"</c> after <c>new Entity()</c> (b2-r1; unless given
/// an existing id that is not handed to a create), a create-by-upsert of a fresh id, and a Web API POST to a literal
/// entity set. b2-r2: a row given a caller-chosen id and handed to <c>Create</c> / <c>Create…Async</c> /
/// <c>new CreateRequest { Target = … }</c> is a create (Dataverse creates with a supplied id). The shapes it does not see are listed under "KNOWN LIMITS" in
/// the maintenance procedure. Writers whose
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
    //   4. KNOWN LIMITS (b2-r1, verifier b2 items 5 and 6) — shapes neither census sees. In src/server today (Grep,
    //      2026-10-02) none of them creates a listed child table or writes an owner, except SpendSnapshotService's keyed
    //      UpsertRequest, which is listed in UnscannedWriters (the other KeyAttributeCollection uses are keyed READS or a
    //      non-child junction). A writer that needs one must list itself in UnscannedWriters (which must call the
    //      resolver) or extend the scanner with a negative control, never rely on the gap:
    //        - creates: a table name that is neither a literal nor a const (a variable / a computed entity set), a keyed
    //          construction (`new Entity(t, keyName, keyValue)` / a KeyAttributeCollection), a target-typed construction
    //          (`Entity row = new("t")`), an UpsertRequest / CreateRequest / ExecuteMultiple built elsewhere, a POST to a
    //          computed URL, string-embedded JSON;
    //        - creates with a CALLER-CHOSEN id (b2-r2, verifier b2-r1 item 5 seed 4): a row given a non-fresh id is
    //          counted only when the same member hands it to `Create(…)` / `Create…Async(…)` / `new CreateRequest
    //          { Target = … }`. Such a row UPSERTED (`UpsertAsync`, `UpsertRequest`), or handed to a create in another
    //          member or file, is not counted — and Dataverse creates on an upsert of an absent id, so a deterministic or
    //          idempotency id there evades the count (a `Guid.NewGuid()` / `Generate…Id(…)` id IS counted, see IsFreshId);
    //        - owner writes: `new KeyValuePair<string, object>("ownerid", …)`, `AddRange`, a DTO property serialized as
    //          `[JsonPropertyName("ownerid@odata.bind")]`, an owner key held in a non-const field or built at runtime
    //          (an interpolation WITH holes, `$"owner{x}"`, or a concatenation), an AssignRequest. A hole-free
    //          interpolated or verbatim key — `$"ownerid"`, `@"ownerid"`, `$@"ownerid@odata.bind"` — IS seen (b2-r2,
    //          verifier b2-r1 item 5 seed 1);
    //        - the value check follows a builder's arguments only within its own FILE (calls from another file are not
    //          seen), and its taint is name-based: a variable assigned from ANY call to a member that reaches the resolver
    //          counts as a resolution, and a later reassignment of that name from something else (`team =
    //          request.MatterId;`) does not clear it (verifier b2-r1 item 5 seed 3); an owner value is accepted when it
    //          MENTIONS one resolved name, so a conditional or coalesce whose other branch is not a resolution (`c ? other
    //          : team`, `other ?? team`) passes (seed 2 — b2-r2 refuses a value that parses a GUID from text,
    //          `Guid.TryParse` included, but not every such branch).
    //      Behaviour tests (SecureChildOwnership*Tests, InvoiceReviewWritePathTests, TodoGenerationAssignedToTests) pin
    //      each EXISTING writer's owner; these limits bite only on new code.
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
        new CensusEntry("InquiryReplyTodoCreator.cs", "sprk_todo", 1, Disposition.Routed,
            "Ontology task 071 (D-111): the \"Record the outcome of the budget inquiry\" To Do raised when a reply arrives: ForChild "
            + "over the matter it is filed against (secure-if-any); the person it is for is Assigned To (task 152's rule); a "
            + "refusal creates nothing and is logged (email capture never fails on it)."),
        new CensusEntry("DocumentCheckoutService.cs", "sprk_fileversion", 1, Disposition.Routed,
            "A file version is content of its document (ContentOf); a refusal is a 409 and nothing is written."),
        new CensusEntry("OfficeService.cs", "sprk_todo", 1, Disposition.Routed,
            "Office to-do: ForChild over the record regarding, its core-ancestor stamps and the document / email "
            + "carriers (secure-if-any, task 146); unfiled → the acting user's team."),
        new CensusEntry("TodoGenerationService.cs", "sprk_todo", 1, Disposition.Routed,
            "Generated to-dos are owned from their source event (ownershipSource); a refusal counts the rule failed."),

        // ── Task 146 b2 (verifier AC10): create-by-upsert sites the scanner now sees (a PATCH of a fresh id) ─────────
        new CensusEntry("InvoiceReviewService.cs", "sprk_invoice", 1, Disposition.Routed,
            "Invoice confirm (G5): a fresh-id PATCH whose field map BuildInvoiceCreateFields binds the matter's team "
            + "(task 130, ResolveOwningTeamAsync record-first from the matter); an unresolved team refuses before any write."),
        new CensusEntry("SignalEvaluationService.cs", "sprk_spendsignal", 1, Disposition.Routed,
            "Spend signals: a deterministic-id PATCH (idempotent upsert) whose field map binds the matter's team, "
            + "resolved by the caller of UpsertSignalAsync; a refusal skips the evaluation."),

        // ── Waived ─────────────────────────────────────────────────────────────────────────────────────────────
        new CensusEntry("MessagingIngestor.cs", "sprk_communication", 1, Disposition.Waived,
            "An inbound chat message names no parent at create, so it keeps its creator: task 146 escalation E1, ACCEPTED "
            + "by owner round 10 item 8 (2026-10-03) — unfiled communications (inbound, chat, outbound naming no record) "
            + "keep their creator as owner; filed ones are routed secure-if-any. The per-user master thread keys on the "
            + "message's owner and Direct-thread privacy rests on per-participant shares. It is filed only by joining a "
            + "record thread, which re-derives its owner (ThreadResolver JOIN → ReparentAsync).",
            WaiverKind.Permanent),
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
        // WorkAssignmentEndpoints.cs's sprk_workassignment create is gone: task 166 deleted the caller-less route (S-76,
        // owner round 10 item 1), so its Pending waiver went with it (batch-4 integration).
    };

    /// <summary>
    /// Writers whose create or re-file the scanner cannot see. Each must still call the resolver.
    /// </summary>
    private sealed record UnscannedWriter(string FileName, string Shape, string Reason);

    private static readonly IReadOnlyList<UnscannedWriter> UnscannedWriters = new[]
    {
        new UnscannedWriter("InvoiceReviewService.cs", "document re-file under the new invoice",
            "Linking the document is a reparent (task 146); the invoice's own create-by-upsert is a census site (b2)."),
        new UnscannedWriter("SpendSnapshotService.cs", "keyed UpsertRequest (sprk_spendsnapshot)",
            "Owned by the parent matter/project's team; a refusal skips the snapshot."),
        new UnscannedWriter("ExternalProjectDataEndpoints.cs", "Web API POST to a computed URL (document, event, to-do)",
            "The external routes resolve the root's owner before the create; ExternalDataService refuses an empty team."),
        new UnscannedWriter("EventEndpoints.cs", "seam caller (CreateEventAsync, CreateEventLogAsync)",
            "Owner resolved from the regarding record. The re-file route (PUT /{id}) was deleted by task 159 (owner round 10 item 1)."),
        new UnscannedWriter("AnalysisEndpoints.cs", "seam caller (CreateAnalysisAsync)",
            "Create and promote resolve the owner from the document and regarding record; a refusal is a 409 (the fork route was deleted by task 162)."),
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
        // (RecordMatchEndpoints.cs — the associate-record re-file — left with its routes: task 164, owner round 10 item 1.)
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
            + "read back); any other table moved under a SECURE record is refused. Since task 147 r1 through the shared "
            + "OwnedChildWrite.RefileAsync."),

        // ── task 147 r1 (owner round 28 item 1): the browser's child writers, through the BFF ─────────────────────────
        new UnscannedWriter("ChildRecordEndpoints.cs", "app-only create (G5) + run-as-user re-file PATCH of a child table",
            "POST /api/v1/child-records/{table}: OwnedChildWrite.CreateAsync (as-the-caller checks, the resolver's owner, the "
            + "app-only create). PATCH /api/v1/child-records/{table}/{id}, /api/v1/events/{id}/filing and "
            + "/api/communications/{id}/filing: OwnedChildWrite.RefileAsync (ReparentAsync); a PATCH that files nothing is the "
            + "caller's own update."),
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
        // b2 merge (task 152 extracted the payload builder): the refusal lives in the builder, so no payload without an
        // owner is ever built; CreateEventAsync's POST uses only that builder.
        new OwnerSeam("DataverseWebApiService.cs", "BuildCreateEventPayload", Refusal),
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

        /// <summary>
        /// Task 148 r1: puts a row back on the owner it had when the SAME pass began (read by that pass before its own
        /// write), as a compensation for a later step of the pass that failed — never an owner decided another way. Asserted:
        /// the member's owner value is its start-owner parameter, and the file's only caller of it passes the snapshot.
        /// </summary>
        Restore,
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
        new OwnerWriteEntry("DataverseWebApiService.cs", "BuildCreateEventPayload", 1, OwnerWriteKind.Seam,
            "CreateEventRequest.OwningTeamId — throws without it (the builder CreateEventAsync POSTs; task 152 extracted it)."),
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
        new OwnerWriteEntry("EmailUploadCaptureService.cs", "CaptureWithOutcomeAsync", 1, OwnerWriteKind.Routed, // word-add-in-r1 task 121: the body moved (CaptureAsync wraps it)
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
        new OwnerWriteEntry("InquiryReplyTodoCreator.cs", "CreateIfNeededAsync", 1, OwnerWriteKind.Routed,
            "The budget-inquiry outcome To Do (ontology 071) - ForChild over its matter, the resolver's team; nothing is created on a refusal."),
        new OwnerWriteEntry("OwnedChildWrite.cs", "CreateAsync", 1, OwnerWriteKind.Routed,
            "The chat tools' owned create (owner S1 / G5) — the resolver's team, after the as-the-caller checks."),
        new OwnerWriteEntry("SecureChildReconciler.cs", "AssignAsync", 1, OwnerWriteKind.Routed,
            "Task 148: an EXISTING child of a root moved into or out of isolation (provisioning, unsecure, the sweep) — the " +
            "team IRecordOwnershipResolver answered for the row's own parents, its own update, read back."),
        new OwnerWriteEntry("SecureChildReconciler.cs", "RestoreStartOwnerAsync", 1, OwnerWriteKind.Restore,
            "Task 148 r1: a row the same pass moved is put back on the owner the pass read before its write — a child taken " +
            "out of isolation whose mirrored shares could not all be removed (back on the Secure team, so no later pass reads " +
            "it as never isolated), or a row the pass pulled into isolation whose support then left (back on its ordinary " +
            "owner); read back."),
        new OwnerWriteEntry("RecordCreationService.cs", "CreateMatterAsync", 1, OwnerWriteKind.Root,
            "Office quick-create of a MATTER (a root) — owned by the resolver's team for the acting user (task 080)."),
        new OwnerWriteEntry("RecordCreationService.cs", "CreateProjectAsync", 1, OwnerWriteKind.Root,
            "Office quick-create of a PROJECT (a root) — owned by the resolver's team for the acting user (task 080)."),
        new OwnerWriteEntry("RecordCreationService.cs", "IsolateProjectCreate", 1, OwnerWriteKind.Root,
            "Task 158 r1 (owner round 31 item 2): a PROJECT (a root) filed under a secure record is created INTO isolation, " +
            "owned by the named Secure Record Owners team provisioning's topology names (SecureRootInheritance.PlanCreateAsync)."),
        new OwnerWriteEntry("OwnedChildWrite.cs", "CreateIntoIsolationAsync", 1, OwnerWriteKind.Root,
            "Task 158 r1 (owner round 31 item 2): a chat-created work assignment / project (a root) filed under a secure record " +
            "is created INTO isolation, owned by the named team provisioning's topology names (SecureRootInheritance.PlanCreateAsync)."),

        new OwnerWriteEntry("ProvisionProjectEndpoint.cs", "MoveOwnerAsync", 1, OwnerWriteKind.Root,
            "Secure provisioning assigns the ROOT to the named Secure team (task 144), and its compensation moves the ROOT " +
            "back to the owner read before the call (task 133 renamed AssignOwnerToSecureTeamAsync to serve both)."),
        // One entry for both callers of this primitive (batch 4 integration: 133's compensation and 148's unsecure were each
        // censused on their own branch; the 148 merge joined them). The row is one a ROOT's own Assign cascades to
        // (sharepointdocumentlocation / sharepointdocument — never a sprk_* child).
        new OwnerWriteEntry("AssignCascadeChildOwners.cs", "RestoreOneAsync", 1, OwnerWriteKind.Root,
            "A row a ROOT's Assign cascades to (document location / document). Provisioning's compensation (task 133 c1, owner " +
            "round 10 item 4): after a VERIFIED move of the ROOT back, put back on the owner it had before the call, read from " +
            "the snapshot taken before any write — the root move's own side effect, undone. Unsecure (task 148, owner round 13 " +
            "item 1): placed on the owner SecureChildReconciler resolved for a child of that root through " +
            "IRecordOwnershipResolver. Read back in both."),
        new OwnerWriteEntry("UnsecureProjectEndpoint.cs", "UnsecureRecordAsync", 1, OwnerWriteKind.Root,
            "Un-securing hands the ROOT back to a user (task 144 / F3). Task 158: the record's own steps moved from the " +
            "handler into UnsecureRecordAsync, which also unsecures each alsoUnsecure related record an F3 holder names."),
        // WorkAssignmentEndpoints.CreateWorkAssignmentAsync: deleted with its route by task 166 (S-76; batch-4 integration).

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
        new OwnerWriteEntry("PlaybookService.cs", "BuildCreatePayload", 1, OwnerWriteKind.PerUser,
            "sprk_analysisplaybook — a playbook definition owned by the person who created it, so task 164's OwnerOnly "
            + "(caller systemuserid == _ownerid_value) admits its creator; never a business record's child (dev gate D-G6-2)."),

        new OwnerWriteEntry("CommunicationEnrichmentService.cs", "AssignOwningTeamAsync", 1, OwnerWriteKind.UnfiledOnly,
            "FR-E7 category routing — a communication FILED under a record is the resolver's (r2): routing applies only to "
            + "a row filed under nothing (IsFiledOrUnreadableAsync first)."),
    };

    /// <summary>The gate an <see cref="OwnerWriteKind.UnfiledOnly"/> member must call before its owner write.</summary>
    private const string UnfiledGate = "IsFiledOrUnreadableAsync";

    /// <summary>
    /// The owner key as a literal: <c>"ownerid"</c> or <c>"ownerid@odata.bind"</c>, plain, verbatim (<c>@"…"</c>) or
    /// interpolated with no holes (<c>$"…"</c>, <c>$@"…"</c>, <c>@$"…"</c>) — b2-r2, verifier b2-r1 item 5 seed 1: the
    /// prefixed forms are the same key and were unseen. An interpolation WITH holes is a key built at runtime (KNOWN LIMITS).
    /// </summary>
    private const string OwnerKeyLiteral = @"(?:\$@|@\$|\$|@)?""ownerid(?:@odata\.bind)?""";

    /// <summary>A const whose value is the owner key — <c>"ownerid"</c> or <c>"ownerid@odata.bind"</c> (any
    /// <see cref="OwnerKeyLiteral"/> spelling).</summary>
    private static readonly Regex OwnerKeyConst = new(
        @"const\s+string\s+(?<name>\w+)\s*=\s*" + OwnerKeyLiteral + @"\s*;", RegexOptions.Compiled);

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

        var globalKeys = GlobalOwnerKeys(files);
        foreach (var entry in OwnerWrites.Where(e => e.Kind == OwnerWriteKind.UnfiledOnly))
        {
            var body = CodeOf(files, entry.FileName) is { } code ? MethodBody(code, entry.Member) : null;
            if (body is null || !UnfiledOnlyGateComesFirst(body, globalKeys))
                problems.Add($"{entry.FileName}.{entry.Member}: UnfiledOnly, but it does not call {UnfiledGate} before its owner write");
        }

        Assert.True(problems.Count == 0, "Owner writes that no longer keep their kind:\n" + string.Join("\n", problems));
    }

    /// <summary>True when <paramref name="memberBody"/> calls the filing gate before its first owner write — of ANY shape
    /// the owner-write census sees (b2-r1: it used to look at <c>["ownerid"] =</c> only).</summary>
    private static bool UnfiledOnlyGateComesFirst(string memberBody, IReadOnlySet<string>? globalKeys = null)
    {
        var gate = Regex.Match(memberBody, @"\b" + UnfiledGate + @"\s*\(");
        var writes = OwnerWritesIn(memberBody, globalKeys ?? new HashSet<string>()).Select(w => w.Index).ToList();
        return gate.Success && writes.Count > 0 && gate.Index < writes.Min();
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

        // b2-r1: an owner write of another shape BEFORE the gate is found too.
        Assert.False(UnfiledOnlyGateComesFirst(
            "{ row.SetAttributeValue(\"ownerid\", new EntityReference(\"team\", t)); if (await " + UnfiledGate
            + "(id, ct)) return; fields[\"ownerid\"] = new EntityReference(\"team\", t); }"));
    }

    /// <summary>(file, member) → the 1-based lines of each owner write in it.</summary>
    private static Dictionary<(string File, string Member), List<int>> ScanOwnerWrites(IReadOnlyDictionary<string, string> files)
    {
        var globalKeys = GlobalOwnerKeys(files);
        var sites = new Dictionary<(string, string), List<int>>();
        foreach (var (fileName, code) in files)
        {
            var members = MemberDeclaration.Matches(code).ToList();
            foreach (var (index, _) in OwnerWritesIn(code, globalKeys))
            {
                var key = (fileName, MemberNameAt(code, members, index));
                if (!sites.TryGetValue(key, out var list))
                    sites[key] = list = new List<int>();
                list.Add(SourceScan.LineOf(code, index));
            }
        }

        return sites;
    }

    /// <summary>Every const, anywhere in the server, whose value is an owner key.</summary>
    private static HashSet<string> GlobalOwnerKeys(IReadOnlyDictionary<string, string> files) =>
        files.Values
            .SelectMany(code => OwnerKeyConst.Matches(code).Select(m => m.Groups["name"].Value))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The owner writes in <paramref name="code"/>: the index of each and the index its VALUE starts at. Four shapes
    /// (task 146 b2 and b2-r1, verifier AC10 and b2 item 6 — the last three were proven blind by seeding):
    /// <list type="bullet">
    /// <item><c>x["ownerid"] = v</c> / <c>x["ownerid@odata.bind"] = v</c> (or through a const holding either key);</item>
    /// <item><c>x.Add("ownerid", v)</c> / <c>x.Attributes.Add(…)</c> / <c>TryAdd</c>;</item>
    /// <item><c>x.SetAttributeValue("ownerid", v)</c> (b2-r1);</item>
    /// <item>a collection-initializer element <c>{ "ownerid", v }</c> — only when the element CLOSES after its value, so a
    /// list of column names (<c>{ "ownerid", "owninguser", … }</c>) is not an owner write.</item>
    /// </list>
    /// </summary>
    private static IEnumerable<(int Index, int ValueStart)> OwnerWritesIn(string code, IReadOnlySet<string> globalKeys)
    {
        var keys = string.Join("|", OwnerKeyConst.Matches(code).Select(m => Regex.Escape(m.Groups["name"].Value))
            .Concat(globalKeys.Select(Regex.Escape))
            .Distinct()
            .Prepend(OwnerKeyLiteral));

        foreach (Match m in new Regex(@"\[\s*(?:" + keys + @")\s*\]\s*=(?![=>])").Matches(code))
            yield return (m.Index, m.Index + m.Length);

        foreach (Match m in new Regex(@"\.(?:Add|TryAdd|SetAttributeValue)\s*\(\s*(?:" + keys + @")\s*,").Matches(code))
            yield return (m.Index, m.Index + m.Length);

        foreach (Match m in new Regex(@"\{\s*(?:" + keys + @")\s*,").Matches(code))
        {
            // A dictionary element is an INNER brace — preceded by its initializer's '{' or a ',' — that closes after its
            // value. A set initializer's own brace (`new() { "ownerid", "owninguser" }`) follows ')' or a type name.
            var before = code[..m.Index].TrimEnd();
            var (_, terminator) = ValueEnd(code, m.Index + m.Length);
            if (terminator == '}' && before.Length > 0 && before[^1] is '{' or ',')
                yield return (m.Index, m.Index + m.Length);
        }
    }

    /// <summary>The index of the bracket that closes the one opened at <paramref name="open"/> (commas ignored).</summary>
    private static int MatchingClose(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] is '(' or '[' or '{')
                depth++;
            else if (code[i] is ')' or ']' or '}' && --depth == 0)
                return i;
        }

        return code.Length;
    }

    /// <summary>
    /// Where a value that starts at <paramref name="start"/> ends: the first <c>;</c> or <c>,</c> at nesting depth 0, or
    /// the bracket that closes its enclosing construct — and which of those it was.
    /// </summary>
    private static (int End, char Terminator) ValueEnd(string code, int start)
    {
        var depth = 0;
        for (var i = start; i < code.Length; i++)
        {
            var c = code[i];
            if (c is '(' or '[' or '{')
            {
                depth++;
            }
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0)
                    return (i, c);
                depth--;
            }
            else if ((c is ';' or ',') && depth == 0)
            {
                return (i, c);
            }
        }

        return (code.Length, '\0');
    }

    /// <summary>The name of the member declared last before <paramref name="index"/> — "(file)" when none.</summary>
    private static string MemberNameAt(string code, IReadOnlyList<Match> members, int index)
    {
        var declaration = members.LastOrDefault(d => d.Index <= index);
        return declaration is null
            ? "(file)"
            : Regex.Match(code[declaration.Index..], @"\b(?<name>\w+)\s*(?:<[^>()]*>)?\s*\(").Groups["name"].Value;
    }

    // =============================================================================================
    // PER-MEMBER VALUE CHECK (task 146 b2, verifier GAP "census check is per file, not per member")
    // ---------------------------------------------------------------------------------------------
    // EveryOwnerWriteKeepsItsKind only asks whether the FILE calls the resolver, so a Routed member could write any team
    // while a sibling member made the call — proven by seeding a hard-coded team into ThreadResolver's default-thread
    // owner write. This asks it of the VALUE: every owner write in a Routed member must take its value from a resolution
    // made in that member (a resolver call, or a call to a member of the file that reaches one), from a
    // RecordOwnerResolution handed to it, or — for a builder — from a parameter that EVERY same-file call passes a
    // resolution in (the argument itself, in the caller; b2-r1). The b2 builder rule ("every caller reaches the resolver")
    // passed any parameter of a member that called the resolver itself, because its callers reached it through that very
    // member — the verifier proved it in ThreadResolver.FindOrCreateDefaultThreadAsync (verifier b2 item 5).
    // =============================================================================================

    [Fact(DisplayName = "Task 146 b2: every Routed owner write takes its value from a resolution in the member that writes it")]
    public void EveryRoutedOwnerWriteTakesItsValueFromAResolution()
    {
        var files = ServerFiles();
        var globalKeys = GlobalOwnerKeys(files);
        var problems = OwnerWrites
            .Where(e => e.Kind == OwnerWriteKind.Routed)
            .SelectMany(e => CodeOf(files, e.FileName) is { } code
                ? RoutedValueProblems(code, e.FileName, e.Member, globalKeys)
                : new[] { $"{e.FileName}: not found" })
            .ToList();

        Assert.True(
            problems.Count == 0,
            "Routed owner writes whose value is not a resolution made in (or handed to) the member that writes it (task 146 "
            + "b2). A child's owner is IRecordOwnershipResolver's answer — write THAT answer, not a team found another way:\n"
            + string.Join("\n", problems));
    }

    // Task 148 r1: a Restore owner write is a compensation — it may only write back the owner its pass read before its own
    // write. Asserted on the VALUE (it comes from the member's single start-owner parameter, never a literal) and on every
    // same-file CALL (the argument is that pass's snapshot: a `start…` name or a `.Previous` the pass recorded).
    [Fact(DisplayName = "Task 148 r1: a Restore owner write writes back only the start owner its pass recorded")]
    public void EveryRestoreOwnerWriteWritesBackOnlyTheStartOwnerItsPassRecorded()
    {
        var files = ServerFiles();
        var globalKeys = GlobalOwnerKeys(files);
        var problems = new List<string>();

        foreach (var entry in OwnerWrites.Where(e => e.Kind == OwnerWriteKind.Restore))
        {
            if (CodeOf(files, entry.FileName) is not { } code || MethodBody(code, entry.Member) is not { } body)
            {
                problems.Add($"{entry.FileName}.{entry.Member}: not found");
                continue;
            }

            var parameters = ParametersOf(body, entry.Member).Select(p => p.Name).ToList();
            var startParameters = parameters.Where(p => p.StartsWith("start", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
            if (startParameters.Count != 1)
            {
                problems.Add($"{entry.FileName}.{entry.Member}: Restore needs exactly one start-owner parameter (named start…)");
                continue;
            }

            var fromStart = ResolvedNamesIn(body, entry.Member, new HashSet<string>(StringComparer.Ordinal), startParameters);
            foreach (var (_, valueStart) in OwnerWritesIn(body, globalKeys))
            {
                var (end, _) = ValueEnd(body, valueStart);
                var value = body[valueStart..end];
                var identifiers = Regex.Matches(value, @"\b[A-Za-z_]\w*\b").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
                if (HardCodedId.IsMatch(value) || !identifiers.Overlaps(fromStart))
                    problems.Add($"{entry.FileName}.{entry.Member}: owner value `{value.Trim()}` is not its start-owner parameter");
            }

            var members = MemberDeclaration.Matches(code).ToList();
            var declarationParens = members
                .Where(d => MemberNameAt(code, members, d.Index) == entry.Member)
                .Select(d => code.IndexOf('(', d.Index))
                .ToHashSet();
            var calls = Regex.Matches(code, @"\b" + Regex.Escape(entry.Member) + @"\s*\(")
                .Where(m => !declarationParens.Contains(m.Index + m.Length - 1))
                .ToList();
            if (calls.Count == 0)
                problems.Add($"{entry.FileName}.{entry.Member}: never called");

            foreach (var site in calls)
            {
                var open = site.Index + site.Length - 1;
                var arguments = ArgumentsByParameter(SplitTopLevel(code[(open + 1)..MatchingClose(code, open)]), parameters);
                var snapshot = arguments.TryGetValue(startParameters.Single(), out var argument)
                               && !HardCodedId.IsMatch(argument)
                               && Regex.IsMatch(argument, @"\bstart\w*\b|\.Previous\b");
                if (!snapshot)
                    problems.Add($"{entry.FileName}.{entry.Member}: a call passes `{argument?.Trim()}`, not the pass's start-owner snapshot");
            }
        }

        Assert.True(
            problems.Count == 0,
            "Restore owner writes that could write something other than the owner their pass read first (task 148 r1):\n"
            + string.Join("\n", problems));
    }

    /// <summary>The value problems of one Routed member's owner writes (see the section comment).</summary>
    private static IEnumerable<string> RoutedValueProblems(
        string code, string fileName, string member, IReadOnlySet<string> globalKeys)
    {
        var body = MethodBody(code, member);
        if (body is null)
        {
            yield return $"{fileName}.{member}: member not found";
            yield break;
        }

        // b2-r1 (verifier item 5): the member under check never counts as reaching the resolver for its OWN value — a
        // caller that reaches the resolver only THROUGH this member resolved nothing it could hand in.
        var reaching = MembersReachingTheResolver(code, exclude: member);
        var tainted = ResolvedNamesIn(body, member, reaching, ParametersFedByResolution(code, member));

        var writes = OwnerWritesIn(body, globalKeys).ToList();
        if (writes.Count == 0)
            yield return $"{fileName}.{member}: censused as Routed but writes no owner";

        foreach (var (index, valueStart) in writes)
        {
            var (end, _) = ValueEnd(body, valueStart);
            var value = body[valueStart..end];
            if (HardCodedId.IsMatch(value))
            {
                yield return $"{fileName}.{member}: hard-coded owner `{value.Trim()}`";
                continue;
            }

            var identifiers = Regex.Matches(value, @"\b[A-Za-z_]\w*\b").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            if (!identifiers.Overlaps(tainted) && !ResolverOrReachingCall(reaching).IsMatch(value))
                yield return $"{fileName}.{member}: owner value `{value.Trim()}` is not a resolution made in this member";
        }
    }

    /// <summary>A GUID literal or a GUID parsed from text — never a resolver's answer. b2-r2 (verifier b2-r1 item 5 seed 2):
    /// <c>Guid.TryParse</c> / <c>ParseExact</c> too, so a conditional that parses its owner from text is refused even when
    /// its other branch is the resolution.</summary>
    private static readonly Regex HardCodedId = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\bGuid\.(?:Try)?Parse(?:Exact)?\s*\(|\bnew\s+Guid\s*\(",
        RegexOptions.Compiled);

    private static Regex ResolverOrReachingCall(IReadOnlySet<string> reaching) => new(
        @"\b(?:ResolveOwnerAsync|ResolveOwningTeamAsync|ReparentAsync|AssignToThreadReconcilingOwnerAsync|ResolveDocumentOwnerTeamAsync"
        + string.Concat(reaching.Select(r => "|" + Regex.Escape(r))) + @")\s*(?:<[^>()]*>)?\s*\(");

    /// <summary>
    /// The members of <paramref name="code"/> that reach the resolver: those whose body calls it, and — to a fixpoint —
    /// those whose body calls one of them (<c>ThreadResolver.ResolveRecordThreadOwnerAsync</c>,
    /// <c>ExternalProjectDataEndpoints.ResolveChildOwnerAsync</c>). <paramref name="exclude"/> is never counted as
    /// reaching, so neither is a member that reaches the resolver only through it.
    /// </summary>
    private static HashSet<string> MembersReachingTheResolver(string code, string? exclude = null)
    {
        var names = MemberDeclaration.Matches(code)
            .Select(m => Regex.Match(code[m.Index..], @"\b(?<name>\w+)\s*(?:<[^>()]*>)?\s*\(").Groups["name"].Value)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var bodies = names.ToDictionary(n => n, n => MethodBody(code, n) ?? string.Empty, StringComparer.Ordinal);

        var reaching = new HashSet<string>(StringComparer.Ordinal);
        bool grew;
        do
        {
            grew = false;
            var call = ResolverOrReachingCall(reaching);
            foreach (var name in names.Where(n => !reaching.Contains(n) && n != exclude))
            {
                // A member's own declaration ("Name(") is not a call of itself.
                var body = bodies[name];
                var firstParen = body.IndexOf('(');
                if (firstParen >= 0 && call.IsMatch(body[(firstParen + 1)..]))
                {
                    reaching.Add(name);
                    grew = true;
                }
            }
        }
        while (grew);

        return reaching;
    }

    /// <summary>
    /// The parameters of <paramref name="member"/> that are fed a resolution at EVERY call of it in this file (and it is
    /// called at least once) — so a builder of the owned row may write them. Decided per ARGUMENT (b2-r1, verifier item 5):
    /// the argument bound to the parameter (by position, or by name) must itself be a resolution in the CALLER — a
    /// resolving call, or a name the caller's own taint holds — and never a GUID literal. The caller's taint is computed
    /// without this member in the reaching set and without a builder clause of its own (a builder fed by a builder fails
    /// closed). The b2 rule only asked whether every caller REACHED the resolver; a caller that reached it only through
    /// the member itself always did, so any parameter passed — proven by the verifier with
    /// <c>anchor!.RecordId</c> and <c>ParseKey(keyId)</c> in <c>ThreadResolver.FindOrCreateDefaultThreadAsync</c>.
    /// Calls from OTHER files are not followed: a builder whose owner rests on its parameters must be private to the
    /// file in practice (the two today: <c>InvoiceReviewService.BuildInvoiceCreateFields</c>,
    /// <c>SignalEvaluationService.UpsertSignalAsync</c>).
    /// </summary>
    private static IReadOnlySet<string> ParametersFedByResolution(string code, string member)
    {
        var none = new HashSet<string>(StringComparer.Ordinal);
        var body = MethodBody(code, member);
        if (body is null)
            return none;

        var parameters = ParametersOf(body, member).Select(p => p.Name).ToList();
        if (parameters.Count == 0)
            return none;

        var members = MemberDeclaration.Matches(code).ToList();

        // The '(' that opens each declaration of the member — a match ending there is the declaration, not a call.
        var declarationParens = members
            .Where(d => MemberNameAt(code, members, d.Index) == member)
            .Select(d => code.IndexOf('(', d.Index))
            .ToHashSet();
        var calls = Regex.Matches(code, @"\b" + Regex.Escape(member) + @"\s*(?:<[^>()]*>)?\s*\(")
            .Where(m => !declarationParens.Contains(m.Index + m.Length - 1))
            .ToList();
        if (calls.Count == 0)
            return none;

        var reaching = MembersReachingTheResolver(code, exclude: member);
        var call = ResolverOrReachingCall(reaching);
        var callerTaint = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var fed = new HashSet<string>(parameters, StringComparer.Ordinal);

        foreach (var site in calls)
        {
            var caller = MemberNameAt(code, members, site.Index);
            if (!callerTaint.TryGetValue(caller, out var resolved))
            {
                callerTaint[caller] = resolved = MethodBody(code, caller) is { } callerBody
                    ? ResolvedNamesIn(callerBody, caller, reaching, none)
                    : new HashSet<string>(StringComparer.Ordinal);
            }

            var open = site.Index + site.Length - 1;
            var arguments = ArgumentsByParameter(SplitTopLevel(code[(open + 1)..MatchingClose(code, open)]), parameters);
            foreach (var parameter in parameters.Where(fed.Contains).ToList())
            {
                var isResolution = arguments.TryGetValue(parameter, out var argument)
                                   && !HardCodedId.IsMatch(argument)
                                   && (call.IsMatch(argument)
                                       || Regex.Matches(argument, @"\b[A-Za-z_]\w*\b").Any(m => resolved.Contains(m.Value)));
                if (!isResolution)
                    fed.Remove(parameter);
            }
        }

        return fed;
    }

    /// <summary>The arguments of one call keyed by the parameter each binds to: <c>name: value</c> by name, the rest by
    /// position. A parameter left to its default is absent.</summary>
    private static Dictionary<string, string> ArgumentsByParameter(IEnumerable<string> arguments, IReadOnlyList<string> parameters)
    {
        var bound = new Dictionary<string, string>(StringComparer.Ordinal);
        var position = 0;
        foreach (var raw in arguments.Select(a => a.Trim()).Where(a => a.Length > 0))
        {
            var named = Regex.Match(raw, @"^(?<n>[A-Za-z_]\w*)\s*:(?!:)\s*(?<v>[\s\S]*)$");
            if (named.Success && parameters.Contains(named.Groups["n"].Value))
                bound[named.Groups["n"].Value] = named.Groups["v"].Value;
            else if (position < parameters.Count)
                bound[parameters[position]] = raw;
            position++;
        }

        return bound;
    }

    /// <summary>The parameters of <paramref name="member"/>, in order (name and declaration text). The list opens right
    /// after the member's NAME (a tuple return type has a '(' of its own before it).</summary>
    private static IReadOnlyList<(string Name, string Text)> ParametersOf(string body, string member)
    {
        var name0 = Regex.Match(body, @"\b" + Regex.Escape(member) + @"\s*(?:<[^>()]*>)?\s*\(");
        if (!name0.Success)
            return Array.Empty<(string, string)>();

        var open = name0.Index + name0.Length - 1;
        return SplitTopLevel(body[(open + 1)..MatchingClose(body, open)])
            .Select(p => (Name: Regex.Match(p, @"(?<name>\w+)\s*(?:=[^,]*)?\s*$").Groups["name"].Value, Text: p))
            .Where(p => p.Name.Length > 0)
            .ToList();
    }

    /// <summary>
    /// The names in <paramref name="body"/> that hold a resolution (a crude taint): parameters typed
    /// <c>RecordOwnerResolution</c>; the parameters in <paramref name="fedParameters"/> (fed a resolution at every call,
    /// <see cref="ParametersFedByResolution"/>); any variable assigned from a resolver call, a call to a reaching member,
    /// or an expression over a resolved name; any pattern variable bound from a resolved name or a resolving call
    /// (<c>recordOwner is { } ownerTeamId</c>, <c>await ResolveSnapshotOwnerAsync(…) is not { } owningTeamId</c>) — to a
    /// fixpoint.
    /// </summary>
    private static HashSet<string> ResolvedNamesIn(
        string body, string member, IReadOnlySet<string> reaching, IReadOnlySet<string> fedParameters)
    {
        var resolved = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (name, text) in ParametersOf(body, member))
        {
            if (fedParameters.Contains(name) || text.Contains("RecordOwnerResolution", StringComparison.Ordinal))
                resolved.Add(name);
        }

        var call = ResolverOrReachingCall(reaching);
        var assignments = Regex.Matches(body, @"(?<lhs>(?:\bvar\s+)?(?:\([^()=]*\)|[A-Za-z_][\w\.]*))\s*=(?![=>])(?<rhs>[^;]*)");
        bool grew;
        do
        {
            grew = false;
            foreach (Match a in assignments)
            {
                var rhs = a.Groups["rhs"].Value;
                var taintedRhs = call.IsMatch(rhs)
                                 || Regex.Matches(rhs, @"\b[A-Za-z_]\w*\b").Any(m => resolved.Contains(m.Value));
                if (!taintedRhs)
                    continue;

                foreach (Match n in Regex.Matches(a.Groups["lhs"].Value.Replace("var", " ", StringComparison.Ordinal), @"\b[A-Za-z_]\w*\b"))
                {
                    var name = n.Value.Split('.').Last();
                    if (resolved.Add(name))
                        grew = true;
                }
            }

            // Pattern bindings: the expression the pattern tests — back to the nearest condition or statement boundary —
            // must be a resolving call or name a resolved value.
            foreach (Match b in Regex.Matches(body, @"\bis\s+(?:not\s+)?\{[^}]*\}\s+(?<v>\w+)"))
            {
                var lead = body[Math.Max(0, b.Index - 300)..b.Index];
                var cut = new[] { "if (", "if(", "&&", "||", ";", "{", "}", "return " }
                    .Select(t => lead.LastIndexOf(t, StringComparison.Ordinal))
                    .Max();
                var tested = cut >= 0 ? lead[cut..] : lead;
                var fromResolution = call.IsMatch(tested)
                                     || Regex.Matches(tested, @"\b[A-Za-z_]\w*\b").Any(m => resolved.Contains(m.Value));
                if (fromResolution && resolved.Add(b.Groups["v"].Value))
                    grew = true;
            }
        }
        while (grew);

        return resolved;
    }

    /// <summary>Splits a parameter or argument list on its top-level commas.</summary>
    private static IEnumerable<string> SplitTopLevel(string list)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < list.Length; i++)
        {
            var c = list[i];
            if (c is '(' or '[' or '{' or '<')
                depth++;
            else if (c is ')' or ']' or '}' or '>')
                depth--;
            else if (c == ',' && depth == 0)
            {
                yield return list[start..i];
                start = i + 1;
            }
        }

        if (start < list.Length)
            yield return list[start..];
    }

    [Fact(DisplayName = "Task 146 b2: negative control — the per-member value check flags a hard-coded or sibling-resolved owner")]
    public void RoutedValueCheck_NegativeControl()
    {
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private static readonly Guid OtherTeam = Guid.NewGuid();",
            "    private async Task<Guid?> ResolveRowOwnerAsync(Guid id, CancellationToken ct)",
            "    {",
            "        var owner = await _ownership.ResolveOwnerAsync(Context(id), ct);",
            "        return owner.OwningTeamId;",
            "    }",
            "    private async Task ResolvedHereAsync(Entity row, CancellationToken ct)",
            "    {",
            "        var recordOwner = await ResolveRowOwnerAsync(row.Id, ct);",
            "        if (recordOwner is { } teamId)",
            "            row[\"ownerid\"] = new EntityReference(\"team\", teamId);",
            "    }",
            "    private async Task SiblingResolvesAsync(Entity row, CancellationToken ct)",
            "    {",
            "        var unused = await ResolveRowOwnerAsync(row.Id, ct);",
            "        row[\"ownerid\"] = new EntityReference(\"team\", OtherTeam);",
            "    }",
            "    private void HardCoded(Entity row, RecordOwnerResolution owner)",
            "    {",
            "        row.Attributes.Add(\"ownerid\", new EntityReference(\"team\", Guid.Parse(\"0f0f0f0f-0000-4000-8000-000000000001\")));",
            "    }",
            "    private void FromResolution(Entity row, RecordOwnerResolution owner)",
            "    {",
            "        row[\"ownerid\"] = new EntityReference(\"team\", owner.OwningTeamId!.Value);",
            "    }",
            "    private static Dictionary<string, object> BuildFields(Guid teamId) => new() { { \"ownerid\", new EntityReference(\"team\", teamId) } };",
            "    private static Dictionary<string, object> BuildUnfedFields(Guid teamId) => new() { { \"ownerid\", new EntityReference(\"team\", teamId) } };",
            "    private async Task FeedsAsync(Guid id, CancellationToken ct)",
            "    {",
            "        var team = await _ownership.ResolveOwningTeamAsync(Context(id), ct);",
            "        var fields = BuildFields(team!.Value);",
            "    }",
            "    private void FeedsBadly() { var fields = BuildUnfedFields(OtherTeam); }",
            "}",
        });
        var keys = new HashSet<string>();

        Assert.Empty(RoutedValueProblems(code, "Seeded.cs", "ResolvedHereAsync", keys));
        Assert.Single(RoutedValueProblems(code, "Seeded.cs", "SiblingResolvesAsync", keys)); // the verifier's seed shape
        Assert.Single(RoutedValueProblems(code, "Seeded.cs", "HardCoded", keys));
        Assert.Empty(RoutedValueProblems(code, "Seeded.cs", "FromResolution", keys));
        Assert.Empty(RoutedValueProblems(code, "Seeded.cs", "BuildFields", keys));
        Assert.Single(RoutedValueProblems(code, "Seeded.cs", "BuildUnfedFields", keys)); // fed by a non-resolving caller
    }

    [Fact(DisplayName = "Task 146 b2-r1: negative control — a parameter-sourced owner fails, even in a member that resolves itself")]
    public void RoutedValueCheck_NegativeControl_ParameterSourcedOwner()
    {
        // The verifier's b2 item 5, seeded: a member that calls the resolver ITSELF writes the owner from a parameter. Its
        // only caller reaches the resolver through that very member, so the b2 rule ("every caller reaches the resolver")
        // tainted every parameter and passed both writes.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private static readonly Guid OtherTeam = Guid.NewGuid();",
            "    private async Task<Guid?> ResolveRowOwnerAsync(Anchor? anchor, CancellationToken ct)",
            "    {",
            "        var owner = await _ownership.ResolveOwnerAsync(Context(anchor), ct);",
            "        return owner.OwningTeamId;",
            "    }",
            "    private async Task<Guid> FindOrCreateFromAnchorAsync(string keyId, Anchor? anchor, CancellationToken ct)",
            "    {",
            "        var recordOwner = await ResolveRowOwnerAsync(anchor, ct);",
            "        var thread = new Entity(\"sprk_communicationthread\");",
            "        if (recordOwner is { } ownerTeamId)",
            "            thread[\"ownerid\"] = new EntityReference(\"team\", anchor!.RecordId);",
            "        return await _svc.CreateAsync(thread, ct);",
            "    }",
            "    private async Task<Guid> FindOrCreateFromKeyAsync(string keyId, Anchor? anchor, CancellationToken ct)",
            "    {",
            "        var recordOwner = await ResolveRowOwnerAsync(anchor, ct);",
            "        var thread = new Entity(\"sprk_communicationthread\");",
            "        if (recordOwner is { } ownerTeamId)",
            "            thread[\"ownerid\"] = new EntityReference(\"team\", ParseKey(keyId));",
            "        return await _svc.CreateAsync(thread, ct);",
            "    }",
            "    private async Task<Guid> FindOrCreateOwnedAsync(string keyId, Anchor? anchor, CancellationToken ct)",
            "    {",
            "        var recordOwner = await ResolveRowOwnerAsync(anchor, ct);",
            "        var thread = new Entity(\"sprk_communicationthread\");",
            "        if (recordOwner is { } ownerTeamId)",
            "            thread[\"ownerid\"] = new EntityReference(\"team\", ownerTeamId);",
            "        return await _svc.CreateAsync(thread, ct);",
            "    }",
            "    private async Task LadderAsync(Request request, CancellationToken ct)",
            "    {",
            "        var anchor = await ReadAnchorAsync(request.Id, ct);",
            "        var a = await FindOrCreateFromAnchorAsync(anchor.RecordId, anchor, ct);",
            "        var k = await FindOrCreateFromKeyAsync(anchor.RecordId, anchor, ct);",
            "        var o = await FindOrCreateOwnedAsync(anchor.RecordId, anchor, ct);",
            "    }",
            // A caller that DOES resolve — and hands the builder something else.
            "    private static Dictionary<string, object> BuildHandedFields(string name, Guid teamId) => new() { { \"ownerid\", new EntityReference(\"team\", teamId) } };",
            "    private async Task ResolvesButHandsAnotherTeamAsync(Guid id, CancellationToken ct)",
            "    {",
            "        var team = await _ownership.ResolveOwningTeamAsync(Context(id), ct);",
            "        var fields = BuildHandedFields(\"x\", OtherTeam);",
            "    }",
            // The same builder shape, fed its resolution by NAME at every call — accepted.
            "    private static Dictionary<string, object> BuildNamedFields(string name, Guid teamId) => new() { { \"ownerid\", new EntityReference(\"team\", teamId) } };",
            "    private async Task FeedsByNameAsync(Guid id, CancellationToken ct)",
            "    {",
            "        var team = await _ownership.ResolveOwningTeamAsync(Context(id), ct);",
            "        var fields = BuildNamedFields(teamId: team!.Value, name: \"x\");",
            "    }",
            "}",
        });
        var keys = new HashSet<string>();

        Assert.Single(RoutedValueProblems(code, "Seeded.cs", "FindOrCreateFromAnchorAsync", keys)); // anchor!.RecordId
        Assert.Single(RoutedValueProblems(code, "Seeded.cs", "FindOrCreateFromKeyAsync", keys));    // ParseKey(keyId)
        Assert.Empty(RoutedValueProblems(code, "Seeded.cs", "FindOrCreateOwnedAsync", keys));       // its own resolution
        Assert.Single(RoutedValueProblems(code, "Seeded.cs", "BuildHandedFields", keys));           // a resolving caller, another value
        Assert.Empty(RoutedValueProblems(code, "Seeded.cs", "BuildNamedFields", keys));             // fed by name
    }

    [Fact(DisplayName = "Task 146 b2-r2: negative control — a prefixed owner key is checked; an owner parsed from text in a branch fails")]
    public void RoutedValueCheck_NegativeControl_PrefixedKeyAndParsedBranch()
    {
        // The verifier's b2-r1 item 5 seeds 1 and 2: `x[$"ownerid"] =` / `x[@"ownerid"] =` were not owner writes to the
        // census, and `c && Guid.TryParse(key, out var k) ? k : team` passed because it MENTIONS the resolved name.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private async Task<Guid> PrefixedKeyAsync(Guid id, CancellationToken ct)",
            "    {",
            "        var ownerTeamId = await _ownership.ResolveOwningTeamAsync(Context(id), ct);",
            "        var thread = new Entity(\"sprk_communicationthread\");",
            "        thread[@\"ownerid\"] = new EntityReference(\"team\", ownerTeamId!.Value);",
            "        return await _svc.CreateAsync(thread, ct);",
            "    }",
            "    private async Task<Guid> InterpolatedKeyOtherTeamAsync(Guid id, CancellationToken ct)",
            "    {",
            "        var ownerTeamId = await _ownership.ResolveOwningTeamAsync(Context(id), ct);",
            "        var thread = new Entity(\"sprk_communicationthread\");",
            "        thread[$\"ownerid\"] = new EntityReference(\"team\", Guid.NewGuid());",
            "        return await _svc.CreateAsync(thread, ct);",
            "    }",
            "    private async Task<Guid> ParsedBranchAsync(string keyId, Guid id, CancellationToken ct)",
            "    {",
            "        var ownerTeamId = await _ownership.ResolveOwningTeamAsync(Context(id), ct) ?? Guid.Empty;",
            "        var thread = new Entity(\"sprk_communicationthread\");",
            "        thread[\"ownerid\"] = new EntityReference(\"team\", ownerTeamId != Guid.Empty && Guid.TryParse(keyId, out var k) ? k : ownerTeamId);",
            "        return await _svc.CreateAsync(thread, ct);",
            "    }",
            "}",
        });
        var keys = new HashSet<string>();

        // Seen AND accepted: an unseen write would report "censused as Routed but writes no owner".
        Assert.Empty(RoutedValueProblems(code, "Seeded.cs", "PrefixedKeyAsync", keys));
        Assert.Contains("is not a resolution", Assert.Single(RoutedValueProblems(code, "Seeded.cs", "InterpolatedKeyOtherTeamAsync", keys)));
        Assert.Contains("hard-coded owner", Assert.Single(RoutedValueProblems(code, "Seeded.cs", "ParsedBranchAsync", keys)));
    }

    /// <summary>
    /// Task 146 b2: a <c>RecordOwnerResolution</c> carrying a team can be made only by the resolver. A writer that built
    /// one itself (<c>RecordOwnerResolution.Owned(team)</c>) would pass every Routed check while choosing its own team.
    /// </summary>
    [Fact(DisplayName = "Task 146 b2: no server code outside the resolver makes an Owned resolution")]
    public void NoServerCodeForgesAnOwnedResolution()
    {
        var forged = ServerFiles()
            .Where(f => f.Key != "RecordOwnershipResolver.cs")
            .SelectMany(f => ForgedResolution.Matches(f.Value).Select(m => $"{f.Key}:{SourceScan.LineOf(f.Value, m.Index)}"))
            .ToList();

        Assert.True(forged.Count == 0,
            "An owned RecordOwnerResolution may be made only by RecordOwnershipResolver (task 146 b2): " + string.Join(", ", forged));
        Assert.True(ForgedResolution.IsMatch("var r = RecordOwnerResolution.Owned(team);"), "negative control");
        Assert.True(ForgedResolution.IsMatch("var r = resolution with { OwningTeamId = other };"), "negative control");
        Assert.False(ForgedResolution.IsMatch("return RecordOwnerResolution.Refused(code, reason);"), "positive control");
    }

    private static readonly Regex ForgedResolution = new(
        @"\bRecordOwnerResolution\s*\.\s*Owned\s*\(|\bnew\s+(?:[\w\.]+\.)?RecordOwnerResolution\s*\(|\bwith\s*\{[^}]*\bOwningTeamId\s*=",
        RegexOptions.Compiled);

    /// <summary>An external payload builder that refuses an empty team (<c>RequireOwner(owningTeamId, …)</c>) AND writes
    /// <c>[OwnerBindKey] = $"/teams({owningTeamId})"</c> — the very team it was handed, onto the payload it returns.</summary>
    private static Regex RefusesAndBindsTheOwner => new(
        @"(?s)^(?=.*\bRequireOwner\s*\(\s*owningTeamId\b)(?=.*\[\s*OwnerBindKey\s*\]\s*=\s*\$""/teams\(\{owningTeamId\}\)"")");


    /// <summary>A resolver call — the ONE owner (task 146 constraint: no writer computes a team itself). r2: the chat tools'
    /// one owned-create path (<c>OwnedChildWrite.CreateAsync</c>, itself an UnscannedWriter that calls the resolver).</summary>
    private static readonly Regex ResolverCall = new(
        @"\b(ResolveOwnerAsync|ResolveOwningTeamAsync|ReparentAsync|AssignToThreadReconcilingOwnerAsync|ResolveDocumentOwnerTeamAsync|OwnedChildWrite\.CreateAsync|OwnedChildWrite\.RefileAsync)\s*\(",
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
        ["InquiryDispositionService.cs"] = "Ontology task 071 (D-111): the caller's own PATCHes of scalar columns only (the inquiry's "
                                           + "disposition and state, the reply's association status, the outcome To Do's state): no lookup "
                                           + "is written, so nothing is created or re-filed.",
        ["WorkProductRecordPersister.cs"] = "PATCHes ONE registry-declared text column (the work-product envelope JSON) on "
                                            + "the session's host record — never a lookup, so it files nothing anywhere.",
        ["DecisionActionExecutors.cs"] = "Ontology platform R1 task 044: the caller PATCHes ONE budget amount column (sprk_totalbudget, D-55) and, on a work "
                                         + "assignment, sprk_responseduedate / sprk_respondedon / sprk_responseoutcome after their Write — plain columns, never a "
                                         + "lookup or an owner, so nothing is re-filed. The budget revision is created by the writer, owned through "
                                         + "IRecordOwnershipResolver (ApplyTo); the To Do and event writes go through the child-records cores.",
        ["EventDueAssigneeWrite.cs"] = "Ontology platform R1 task 044 (#29): PATCHes an event's sprk_duedate and/or its assignee CONTACT "
                                       + "(sprk_assignedto, statuscode Reassigned, reassigned-by) as the caller, after their Write. It writes no "
                                       + "regarding/filing lookup and no owner, so the row is filed under the same records and owned by the same "
                                       + "team before and after; re-filing stays on PATCH /events/{id}/filing.",
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

    /// <summary>
    /// A POST or PATCH through the run-as-user client: the handlers' field <c>_dataverse</c>, or — task 147 r1 — the
    /// <c>user</c> parameter the shared re-file core (<c>OwnedChildWrite.RefileAsync</c>) and the browser child-record
    /// routes take it as. The census had seen only the field name, so a re-file core taking the client as a parameter would
    /// have been invisible to it.
    /// </summary>
    private static readonly Regex RunAsUserWrite = new(@"\b(?:_dataverse|user)\s*\.\s*(?:PostAsync|PatchAsync)\s*\(", RegexOptions.Compiled);

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
            // Task 147 r1: the client held as a PARAMETER named user (the shared re-file core's shape).
            ["NewParameterRefile.cs"] = SourceScan.CodeText(new[]
            {
                "internal static class NewParameterRefile",
                "{",
                "    static Task A(IDataverseUserClient user) => user.PatchAsync($\"{set}({id:D})\", body, ct);",
                "}",
            }),
        };

        Assert.Equal(new[] { "NewCreateHandler.cs", "NewRefileHandler.cs", "NewParameterRefile.cs" }, UnclassifiedRunAsUserWrites(files));
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

    [Fact(DisplayName = "Task 146 b2: negative control — a create-by-upsert or a fresh-id construction is a create; an update is not")]
    public void Detector_NegativeControl_UpsertAndFreshIdCreates()
    {
        // The verifier's AC10 seed (a): an owner-less create-by-upsert of sprk_todo was invisible to the census.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private const string TodoEntity = \"sprk_todo\";",
            "    private async Task A() { await _f.UpdateRecordFieldsAsync(\"sprk_todo\", Guid.NewGuid(), fields, ct); }",
            "    private async Task B() { var id = Guid.NewGuid(); await _f.UpdateRecordFieldsAsync(TodoEntity, id, fields, ct); }",
            "    private async Task C() { var key = GenerateDeterministicId(m, t); await _f.UpdateRecordFieldsAsync(TodoEntity, key, fields, ct); }",
            "    private async Task D() { var row = new Entity(\"sprk_todo\", Guid.NewGuid()); await _s.UpsertAsync(row, ct); }",
            "    private async Task E(Guid existing) { await _f.UpdateRecordFieldsAsync(\"sprk_todo\", existing, fields, ct); }",
            "    private void F(Guid existing) { var update = new Entity(\"sprk_todo\", existing); }",
            "}",
        });

        var sites = ScanSites(new Dictionary<string, string> { ["Seeded.cs"] = code });

        Assert.Equal(4, sites[("Seeded.cs", "sprk_todo")].Count); // A, B, C, D — never the updates E and F
    }

    [Fact(DisplayName = "Task 146 b2-r1: negative control — a create naming its table through LogicalName is a create; an update is not")]
    public void Detector_NegativeControl_LogicalNameCreates()
    {
        // The verifier's b2 item 6 seed: `new Entity { LogicalName = "sprk_todo" }` + CreateAsync — an owner-less app-only
        // create of a listed table — was invisible to the census.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private const string TodoEntity = \"sprk_todo\";",
            "    private async Task A() { var row = new Entity { LogicalName = \"sprk_todo\" }; await _s.CreateAsync(row, ct); }",
            "    private async Task B() { await _s.CreateAsync(new Entity() { LogicalName = TodoEntity, [\"sprk_name\"] = \"x\" }, ct); }",
            "    private async Task C() { var row = new Entity(); row.LogicalName = \"sprk_todo\"; await _s.CreateAsync(row, ct); }",
            "    private async Task D(Guid existing) { await _s.UpdateAsync(new Entity { LogicalName = \"sprk_todo\", Id = existing }, ct); }",
            "    private async Task E() { var row = new Entity { LogicalName = \"sprk_todo\", Id = Guid.NewGuid() }; await _s.UpsertAsync(row, ct); }",
            "    private void F() { var request = new RetrieveEntityRequest { LogicalName = \"sprk_todo\" }; }",
            "    private async Task G(RecordOwnerResolution owner) { var row = new Entity { LogicalName = \"sprk_todo\", [\"sprk_regarding\"] = new EntityReference { LogicalName = \"sprk_matter\", Id = m } }; owner.ApplyTo(row); await _s.CreateAsync(row, ct); }",
            "    private void H(Guid existing) { var row = new Entity(); row.LogicalName = \"sprk_todo\"; row.Id = existing; }",
            "    private async Task I(Guid team) { var row = new Entity(); row.LogicalName = \"sprk_todo\"; row.SetAttributeValue(\"ownerid\", new EntityReference(\"team\", team)); await _s.CreateAsync(row, ct); }",
            "}",
        });
        var files = new Dictionary<string, string> { ["Seeded.cs"] = code };

        var sites = ScanSites(files);

        Assert.Equal(new[] { 4, 5, 6, 8, 10, 12 }, sites[("Seeded.cs", "sprk_todo")].OrderBy(l => l)); // A B C E G I
        Assert.False(sites.ContainsKey(("Seeded.cs", "sprk_matter")));                                 // the nested reference

        // As Routed sites: A, B, C and E write no owner on their row; G (ApplyTo) and I (SetAttributeValue) do.
        var problems = SiteOwnerProblems(code, "Seeded.cs", "sprk_todo", ScanSiteIndexes(files)[("Seeded.cs", "sprk_todo")]).ToList();
        Assert.Equal(4, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains("Seeded.cs:10", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("Seeded.cs:12", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 146 b2-r2: negative control — a row given a caller-chosen id and handed to a create is a create; an update is not")]
    public void Detector_NegativeControl_CallerChosenIdCreates()
    {
        // The verifier's b2-r1 item 5 seed 4: `new Entity(EntityTodo, seededId)` / `new Entity { LogicalName = EntityTodo,
        // Id = seededId }` passed to CreateAsync were read as updates — but Dataverse creates with a supplied id.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private const string TodoEntity = \"sprk_todo\";",
            "    private async Task A(Guid seeded) { var row = new Entity(TodoEntity, seeded); row[\"sprk_name\"] = \"x\"; await _s.CreateAsync(row, ct); }",
            "    private async Task B(Guid seeded) { await _s.CreateAsync(new Entity { LogicalName = TodoEntity, Id = seeded }, ct); }",
            "    private async Task C(Guid seeded) { var row = new Entity(); row.LogicalName = \"sprk_todo\"; row.Id = seeded; await _s.CreateAsync(row, ct); }",
            "    private async Task D(Guid seeded) { await _svc.ExecuteAsync(new CreateRequest { Target = new Entity(\"sprk_todo\", seeded) }, ct); }",
            "    private void E(Guid seeded) { var row = new Entity(\"sprk_todo\", seeded); _svc.Create(row); }",
            "    private async Task F(Guid existing) { var row = new Entity(\"sprk_todo\", existing); await _s.UpdateAsync(row, ct); }",
            "    private async Task G(Guid existing) { var row = new Entity { LogicalName = \"sprk_todo\", Id = existing }; await _s.UpsertAsync(row, ct); }",
            "    private async Task H(Guid existing) { var update = new Entity(\"sprk_todo\", existing); await _s.UpdateAsync(update, ct); var other = new Entity(\"sprk_memo\"); await _s.CreateAsync(other, ct); }",
            "    private async Task I(Guid seeded, RecordOwnerResolution owner) { var row = new Entity(TodoEntity, seeded); owner.ApplyTo(row); await _s.CreateAsync(row, ct); }",
            "    private async Task J(Request request) { var row = new Entity(TodoEntity, request!.RecordId); await _s.CreateAsync(row, ct); }",
            "}",
        });
        var files = new Dictionary<string, string> { ["Seeded.cs"] = code };

        var sites = ScanSites(files);

        // A-E, I and J (a member-access id); never the update F, the upsert G (KNOWN LIMITS) or H's update beside another
        // row's create.
        Assert.Equal(new[] { 4, 5, 6, 7, 8, 12, 13 }, sites[("Seeded.cs", "sprk_todo")].OrderBy(l => l));
        Assert.Equal(new[] { 11 }, sites[("Seeded.cs", "sprk_memo")]);

        // As Routed sites: A-E and J write no owner on their row; I (ApplyTo) does.
        var problems = SiteOwnerProblems(code, "Seeded.cs", "sprk_todo", ScanSiteIndexes(files)[("Seeded.cs", "sprk_todo")]).ToList();
        Assert.Equal(6, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains("Seeded.cs:12", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 146 b2: negative control — Add(...) and initializer owner writes are found; a list of column names is not")]
    public void OwnerWriteDetector_NegativeControl_AddAndInitializerForms()
    {
        // The verifier's AC10 seeds (b) and (c), plus the shapes that are NOT owner writes.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private void AttributesAdd(Entity row, Guid t) { row.Attributes.Add(\"ownerid\", new EntityReference(\"team\", t)); }",
            "    private void DictionaryAdd(Dictionary<string, object> f, Guid t) { f.Add(\"ownerid@odata.bind\", $\"/teams({t})\"); }",
            "    private Dictionary<string, object> Initializer(Guid t) => new() { { \"ownerid\", new EntityReference(\"team\", t) } };",
            "    private static readonly HashSet<string> ServerColumns = new() { \"ownerid\", \"owninguser\", \"createdby\" };",
            "    private void ColumnList(List<string> c) { c.Add(\"ownerid\"); }",
            // b2-r1 (verifier b2 item 6): the SDK's SetAttributeValue — an owner write; one on another column is not.
            "    private void SetOwner(Entity row, Guid t) { row.SetAttributeValue(\"ownerid\", new EntityReference(\"team\", t)); }",
            "    private void SetPaging(XElement root, string c) { root.SetAttributeValue(\"paging-cookie\", c); }",
            "}",
        });

        var sites = ScanOwnerWrites(new Dictionary<string, string> { ["Seeded.cs"] = code });

        Assert.Equal(4, sites.Count);
        Assert.Contains(("Seeded.cs", "AttributesAdd"), sites.Keys);
        Assert.Contains(("Seeded.cs", "DictionaryAdd"), sites.Keys);
        Assert.Contains(("Seeded.cs", "Initializer"), sites.Keys);
        Assert.Contains(("Seeded.cs", "SetOwner"), sites.Keys);
    }

    [Fact(DisplayName = "Task 146 b2-r2: negative control — an interpolated or verbatim owner key is an owner write; another column is not")]
    public void OwnerWriteDetector_NegativeControl_PrefixedKeyLiterals()
    {
        // The verifier's b2-r1 item 5 seed 1: `entity[$"ownerid"] = …` and `entity[@"ownerid"] = …` passed every census test.
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private const string VerbatimKey = @\"ownerid\";",
            "    private void Interpolated(Entity row, Guid t) { row[$\"ownerid\"] = new EntityReference(\"team\", t); }",
            "    private void Verbatim(Entity row, Guid t) { row[@\"ownerid\"] = new EntityReference(\"team\", t); }",
            "    private void VerbatimInterpolatedBind(Dictionary<string, object> f, Guid t) { f.Add($@\"ownerid@odata.bind\", $\"/teams({t})\"); }",
            "    private void ThroughVerbatimConst(Entity row, Guid t) { row.SetAttributeValue(VerbatimKey, new EntityReference(\"team\", t)); }",
            "    private void OtherColumns(Entity row, string v) { row[$\"owneridname\"] = v; row[@\"sprk_name\"] = v; }",
            "}",
        });

        var sites = ScanOwnerWrites(new Dictionary<string, string> { ["Seeded.cs"] = code });

        Assert.Equal(4, sites.Count);
        Assert.Contains(("Seeded.cs", "Interpolated"), sites.Keys);
        Assert.Contains(("Seeded.cs", "Verbatim"), sites.Keys);
        Assert.Contains(("Seeded.cs", "VerbatimInterpolatedBind"), sites.Keys);
        Assert.Contains(("Seeded.cs", "ThroughVerbatimConst"), sites.Keys);
    }

    [Fact(DisplayName = "Task 146 b2: negative control — the per-site check needs an owner on an upsert's field map")]
    public void PerSiteDetector_NegativeControl_Upserts()
    {
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private async Task OwnedInlineAsync(Guid team)",
            "    {",
            "        var fields = new Dictionary<string, object?> { [\"sprk_name\"] = \"x\", [\"ownerid@odata.bind\"] = $\"/teams({team})\" };",
            "        await _f.UpdateRecordFieldsAsync(\"sprk_todo\", Guid.NewGuid(), fields, ct);",
            "    }",
            "    private async Task OwnedByBuilderAsync(Guid team)",
            "    {",
            "        var fields = BuildFields(team);",
            "        await _f.UpdateRecordFieldsAsync(\"sprk_todo\", Guid.NewGuid(), fields, ct);",
            "    }",
            "    private static Dictionary<string, object?> BuildFields(Guid team)",
            "    {",
            "        var f = new Dictionary<string, object?>();",
            "        f[\"ownerid@odata.bind\"] = $\"/teams({team})\";",
            "        return f;",
            "    }",
            "    private async Task OwnerlessAsync()",
            "    {",
            "        var fields = new Dictionary<string, object?> { [\"sprk_name\"] = \"x\" };",
            "        await _f.UpdateRecordFieldsAsync(\"sprk_todo\", Guid.NewGuid(), fields, ct);",
            "    }",
            "}",
        });
        var files = new Dictionary<string, string> { ["Seeded.cs"] = code };

        var sites = ScanSiteIndexes(files)[("Seeded.cs", "sprk_todo")];
        var problems = SiteOwnerProblems(code, "Seeded.cs", "sprk_todo", sites).ToList();

        Assert.Equal(3, sites.Count);
        Assert.Contains(problems, p => p.Contains("Seeded.cs:22", StringComparison.Ordinal));
        Assert.Single(problems);
    }

    /// <summary>
    /// Task 146 b2 (AC14 in code): every table whose rows the server can hand the Secure team is in the ONE codified
    /// Secure Record Owner role set (<c>config/secure-record-owner-role.json</c>) — otherwise Dataverse refuses the owner
    /// ("Read Privilege Check For Owner failed"). The tables are the resolver's child set
    /// (<c>RecordOwnershipResolver.IsReparentableChild</c>: the chat tools and every re-file) and every Routed or Seam create
    /// in the census (content rows owned like their secure parent).
    /// </summary>
    [Fact(DisplayName = "Task 146 b2: the codified Secure Record Owner role set covers every table the server can hand the Secure team")]
    public void CodifiedSecureOwnerRoleSet_CoversEveryTableTheServerCanHandTheSecureTeam()
    {
        var configPath = Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json");
        using var config = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
        var codified = config.RootElement.GetProperty("tables").EnumerateArray()
            .Select(t => t.GetProperty("logicalName").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var required = Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.OwnershipParentEntities
            .Where(Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver.IsReparentableChild)
            .Concat(Census.Where(e => e.Disposition is Disposition.Routed or Disposition.Seam).Select(e => e.Table))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = required.Where(t => !codified.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.True(
            missing.Count == 0,
            "Tables the server can make the Secure Record team own, missing from config/secure-record-owner-role.json — "
            + "add each through the file's howToExtend procedure (refusal first), then the live role per task 145's "
            + "procedure: " + string.Join(", ", missing));
    }

    // =============================================================================================
    // THE PERSON WHO ASKED (task 146 c1-r1, owner round 13 item 9, 2026-10-03, BINDING)
    // ---------------------------------------------------------------------------------------------
    // "Children the BFF creates as the application record the person who asked: sprk_createdbyperson is added to the
    // child tables ... and stamped by every app-create writer, so F3's 'or the creator' branch works for them."
    // (1) Every child table a census create writes carries the column (RecordCreatorPerson.StampedChildTables — the same
    //     list the schema script creates, ChildRecordCreatorPersonSchemaAgreementTests), and the set stays inside the
    //     codified Secure Record Owner role set (the child tables this task governs).
    // (2) Every app-create writer that acts for a person names that person in the member that creates (or decides the
    //     owner of) the row. The writers that act for NOBODY are listed apart, each with its reason, so the boundary is
    //     reviewed, not assumed. Behaviour tests pin the stamp for every family a harness can drive
    //     (SecureChildOwnership*Tests, OfficeRecordOwnershipTests, EventEndpointsMembershipPublishingTests); this list
    //     also pins the members no harness drives (the upload worker, the checkout file version, the shared seams).
    // =============================================================================================

    /// <summary>One app-create writer that acts for a person: the member that must name them.</summary>
    private sealed record PersonBearingWriter(string File, string Member, string Person);

    private static readonly IReadOnlyList<PersonBearingWriter> PersonBearingWriters = new[]
    {
        new PersonBearingWriter("OwnedChildWrite.cs", "CreateAsync", "the chat caller (WhoAmI) — dataverse.create_record, email.draft"),
        new PersonBearingWriter("DataverseDocumentsEndpoints.cs", "CreateDocumentAsync", "POST /api/v1/documents caller (oid)"),
        new PersonBearingWriter("EventEndpoints.cs", "OwnershipContextFor", "POST /api/v1/events caller (oid)"),
        new PersonBearingWriter("EventEndpoints.cs", "CreateEventInDataverseAsync", "the event and its creation log row"),
        new PersonBearingWriter("EventEndpoints.cs", "ResolveEventLogOwnerAsync", "the caller whose change a log row records"),
        new PersonBearingWriter("AnalysisEndpoints.cs", "CreateAnalysis", "POST /api/ai/analysis/create caller"),
        // (ForkAnalysis left with POST /api/ai/analysis/fork, deleted by task 162 under owner round 10 item 1.)
        new PersonBearingWriter("AnalysisEndpoints.cs", "PromoteSession", "promote caller"),
        new PersonBearingWriter("AnalysisResultPersistence.cs", "StoreDocumentProfileOutputsAsync", "the user who ran the profile"),
        new PersonBearingWriter("AnalysisResultPersistence.cs", "PersistReviewMemoAsync", "the user who generated the memo"),
        new PersonBearingWriter("AnalysisOrchestrationService.cs", "ExecutePlaybookAsync", "hands the HTTP caller to the profile store"),
        new PersonBearingWriter("ReviewMemoEndpoints.cs", "GenerateReviewMemo", "hands the HTTP caller to the memo store"),
        new PersonBearingWriter("ComposeCreateOnSavePromoter.cs", "PromoteIfEphemeralAsync", "the saving user (oid)"),
        new PersonBearingWriter("DocumentCheckoutService.cs", "CreateFileVersionAsync", "the user checking the document out"),
        new PersonBearingWriter("CommunicationService.cs", "ResolveOutboundOwnerAsync", "the sender of an outbound communication"),
        new PersonBearingWriter("CommunicationService.cs", "ResolveContentOwnerAsync", "the sender / archiver of its .eml and attachment rows"),
        new PersonBearingWriter("CommunicationService.cs", "SendAsync", "shared-mailbox sender (HTTP caller)"),
        new PersonBearingWriter("CommunicationService.cs", "SendAsUserAsync", "user-mode sender (oid)"),
        new PersonBearingWriter("CommunicationService.cs", "SendMessageAsync", "message sender (HTTP caller)"),
        new PersonBearingWriter("CommunicationService.cs", "ArchiveExistingAsync", "the caller asking for an archive"),
        new PersonBearingWriter("CommunicationEndpoints.cs", "ArchiveCommunicationAsync", "hands the HTTP caller to the archive"),
        new PersonBearingWriter("EmailUploadCaptureService.cs", "CaptureWithOutcomeAsync", "the Office user who saved the email"), // task 121: the body moved
        new PersonBearingWriter("MessageAttachmentMaterializer.cs", "MaterializeAsync", "the message's sender, when the caller knows them"),
        new PersonBearingWriter("ThreadResolver.cs", "CreateRecordThreadAsync", "the caller creating a record thread"),
        new PersonBearingWriter("CommunicationProposalApplyService.cs", "ResolveAuditRowOwnerAsync", "the confirming user"),
        new PersonBearingWriter("CommunicationCreateTaskApplyService.cs", "ResolveAuditRowOwnerAsync", "the confirming user"),
        new PersonBearingWriter("CommunicationCreateTaskApplyService.cs", "ApplyAsync", "the confirming user, on the task it creates"),
        new PersonBearingWriter("TaskActionCore.cs", "CreateAsync", "the person who asked for the task (RequestedBySystemUserId)"),
        new PersonBearingWriter("ActionSeam.cs", "CreateTaskAsync", "hands RequestedBySystemUserId to the task core"),
        new PersonBearingWriter("OfficeService.cs", "SaveAsync", "the Office user saving a document (carried to the worker)"),
        new PersonBearingWriter("OfficeService.cs", "QuickCreateAsync", "the Office user quick-creating an invoice"),
        new PersonBearingWriter("OfficeService.cs", "ResolveTodoOwnerTeamAsync", "the Office user creating a To Do"),
        new PersonBearingWriter("OfficeDocumentPersistence.cs", "CreateDocumentWithSpePointersAsync", "the saving user, on the document"),
        new PersonBearingWriter("OfficeJobQueue.cs", "QueueUploadFinalizationAsync", "carries the saving user to the worker"),
        new PersonBearingWriter("UploadFinalizationWorker.cs", "CreateDocumentRecordAsync", "the carried saving user"),
        new PersonBearingWriter("UploadFinalizationWorker.cs", "CreateEmailArtifactAsync", "the carried saving user"),
        new PersonBearingWriter("UploadFinalizationWorker.cs", "CreateAttachmentArtifactAsync", "the carried saving user"),
        new PersonBearingWriter("UploadFinalizationWorker.cs", "ProcessSingleAttachmentAsync", "the carried saving user"),
        new PersonBearingWriter("InvoiceReviewService.cs", "CreateInvoiceRecordAsync", "the reviewer confirming the invoice"),
        new PersonBearingWriter("FinanceEndpoints.cs", "ConfirmInvoiceReview", "hands the HTTP caller to the invoice create"),
        new PersonBearingWriter("UpdateRecordActionCore.cs", "UpdateAsync", "the impersonated user, for F3 (owner round 13 item 8)"),
        new PersonBearingWriter("DataverseServiceClientImpl.cs", "CreateDocumentAsync", "seam: writes CreatedByPersonId"),
        new PersonBearingWriter("DataverseServiceClientImpl.cs", "CreateAnalysisAsync", "seam: writes createdByPersonId"),
        new PersonBearingWriter("DataverseServiceClientImpl.cs", "CreateAnalysisOutputAsync", "seam: writes CreatedByPersonId"),
        new PersonBearingWriter("DataverseServiceClientImpl.cs", "CreateEmailArtifactAsync", "seam: maps CreatedByPersonId"),
        new PersonBearingWriter("DataverseServiceClientImpl.cs", "CreateAttachmentArtifactAsync", "seam: maps CreatedByPersonId"),
        new PersonBearingWriter("DataverseWebApiService.cs", "BuildCreateEventPayload", "seam: binds CreatedByPersonId"),
        new PersonBearingWriter("DataverseWebApiService.cs", "CreateEventLogAsync", "seam: binds createdByPersonId"),
    };

    /// <summary>
    /// The census writers that act for NOBODY, and so record no person — reviewed, not assumed (each reason names why no
    /// person asked). A writer moved from here to <see cref="PersonBearingWriters"/> must start naming its person.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PersonLessWriters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["IncomingCommunicationProcessor.cs"] = "inbound mail: nobody in the organisation asked for it (the sender is outside it)",
        ["EmailAttachmentProcessor.cs"] = "inbound mail attachments: as above",
        ["MessagingIngestor.cs"] = "inbound channel message (E1, keeps the application as creator): no BFF caller",
        ["CommunicationParticipantIndexer.cs"] = "derived index rows of a communication: system work",
        ["CommunicationEnrichmentService.cs"] = "background enrichment and its review logs: system work",
        ["TodoGenerationService.cs"] = "scheduled to-do generation: system work",
        ["SignalEvaluationService.cs"] = "scheduled spend signals: system work",
        ["SpendSnapshotService.cs"] = "scheduled spend snapshots: system work",
        ["DataverseObservationMirror.cs"] = "insight observation mirror: system work",
        ["AppOnlyAnalysisService.cs"] = "background document profile: system work",
        ["ExternalDataService.cs"] = "external portal: the person is a CONTACT, and sprk_createdbyperson names a systemuser",
        ["DirectThreadAccessService.cs"] = "Direct thread (E2): owned by the caller themselves, never filed under a record",
        ["DataverseUpdateHandler.cs"] = "playbook output mapping re-files (no create); acts for no person, so F3 refuses a move out",
    };

    private static readonly Regex PersonToken = new(
        @"\b(?:RequestedBy|requestedBy|RequestedBySystemUserId|RecordRequester|CreatedByPerson|CreatedByPersonId|createdByPersonId|createdByPerson|StampCreatorOn|RecordCreatorPerson|RecordCreatorPersonColumn|SecureExitCaller)\b",
        RegexOptions.Compiled);

    [Fact(DisplayName = "Task 146 c1-r1: every child table a census create writes carries the creator-person column, inside the codified set")]
    public void EveryChildTableTheServerCreatesCarriesTheCreatorPersonColumn()
    {
        var stamped = Sprk.Bff.Api.Services.Dataverse.RecordCreatorPerson.StampedChildTables;
        var created = Census.Where(e => e.Disposition is Disposition.Routed or Disposition.Seam).Select(e => e.Table)
            .Append("sprk_spendsnapshot") // an unscanned keyed UpsertRequest (SpendSnapshotService) — see UnscannedWriters
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = created.Where(t => !stamped.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            "Child tables the server creates app-only, without the creator-person column in RecordCreatorPerson."
            + "StampedChildTables (and scripts/Set-ChildRecordCreatorPersonSchema.ps1): " + string.Join(", ", missing));

        using var config = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(SourceScan.RepoRoot, "config", "secure-record-owner-role.json")));
        var codifiedChildren = config.RootElement.GetProperty("tables").EnumerateArray()
            .Where(t => t.GetProperty("kind").GetString() == "child")
            .Select(t => t.GetProperty("logicalName").GetString()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outside = stamped.Where(t => !codifiedChildren.Contains(t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.True(outside.Count == 0,
            "Stamped child tables outside the codified Secure Record Owner child set (the tables task 146 governs): "
            + string.Join(", ", outside));
    }

    [Fact(DisplayName = "Task 146 c1-r1: every app-create writer that acts for a person names that person")]
    public void EveryAppCreateWriterThatActsForAPersonNamesThem()
    {
        var files = ServerFiles();
        var problems = PersonCoverageProblems(files, PersonBearingWriters).ToList();
        foreach (var (file, why) in PersonLessWriters)
        {
            if (CodeOf(files, file) is null)
                problems.Add($"{file}: listed as person-less ({why}) but no such file exists — remove the stale entry");
        }

        Assert.True(problems.Count == 0,
            "App-create writers that act for a person must record them (owner round 13 item 9; task note §17):\n  "
            + string.Join("\n  ", problems));
    }

    private static IEnumerable<string> PersonCoverageProblems(
        IReadOnlyDictionary<string, string> files, IEnumerable<PersonBearingWriter> writers)
    {
        foreach (var writer in writers)
        {
            if (CodeOf(files, writer.File) is not { } code)
            {
                yield return $"{writer.File}: listed ({writer.Person}) but no such file exists";
                continue;
            }

            var regions = MemberRegions(code, writer.Member).ToList();
            if (regions.Count == 0)
            {
                yield return $"{writer.File}.{writer.Member}: listed ({writer.Person}) but no such member exists";
                continue;
            }

            if (!regions.Any(PersonToken.IsMatch))
                yield return $"{writer.File}.{writer.Member}: creates (or decides the owner of) a row for {writer.Person} without naming them";
        }
    }

    /// <summary>The text of every member named <paramref name="member"/> — from its declaration to the next one.</summary>
    private static IEnumerable<string> MemberRegions(string code, string member)
    {
        var members = MemberDeclaration.Matches(code).ToList();
        for (var i = 0; i < members.Count; i++)
        {
            if (MemberNameAt(code, members, members[i].Index) != member)
                continue;
            var end = i + 1 < members.Count ? members[i + 1].Index : code.Length;
            yield return code[members[i].Index..end];
        }
    }

    [Fact(DisplayName = "Task 146 c1-r1: negative control — the person check flags a listed writer that names nobody")]
    public void PersonCoverage_NegativeControl()
    {
        var code = SourceScan.CodeText(new[]
        {
            "internal sealed class Seeded",
            "{",
            "    private async Task<Guid> NamesThePersonAsync(RecordOwnershipContext context)",
            "    {",
            "        var owner = await _ownership.ResolveOwnerAsync(context with { RequestedBy = RecordRequester.Of(me) }, ct);",
            "        owner.ApplyTo(row);",
            "    }",
            "    private async Task<Guid> NamesNobodyAsync(RecordOwnershipContext context)",
            "    {",
            "        var owner = await _ownership.ResolveOwnerAsync(context, ct); // RequestedBy is only in a comment",
            "        owner.ApplyTo(row);",
            "    }",
            "}",
        });
        var files = new Dictionary<string, string> { ["Seeded.cs"] = code };

        var problems = PersonCoverageProblems(files, new[]
        {
            new PersonBearingWriter("Seeded.cs", "NamesThePersonAsync", "a"),
            new PersonBearingWriter("Seeded.cs", "NamesNobodyAsync", "b"),
            new PersonBearingWriter("Seeded.cs", "Missing", "c"),
        }).ToList();

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("NamesNobodyAsync", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Missing", StringComparison.Ordinal));
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

        // `row["ownerid"]` / `fields["ownerid@odata.bind"]` — or either through a const whose value is the key.
        var ownerKey = string.Join("|", OwnerKeyConst.Matches(code)
            .Select(m => Regex.Escape(m.Groups["name"].Value))
            .Distinct()
            .Prepend(OwnerKeyLiteral));

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

            // b2-r1: `e.LogicalName = "t";` — the row is `e`, named by the site itself.
            var namedRow = Regex.Match(construction, @"^(?<v>[A-Za-z_]\w*)\s*\.\s*LogicalName\s*=");
            if (namedRow.Success)
                assigned = namedRow;

            var isPost = !namedRow.Success
                         && (!construction.StartsWith("new", StringComparison.Ordinal)
                             || construction.StartsWith("new HttpRequestMessage", StringComparison.Ordinal));

            bool written;
            if (Regex.IsMatch(construction, ownerKey))
            {
                written = true; // the construction's own initializer sets the owner
            }
            else if (construction.StartsWith("UpdateRecordFieldsAsync", StringComparison.Ordinal))
            {
                // Task 146 b2: a create-by-upsert — its row is the field map (third argument), which must carry the owner:
                // in its own initializer, by a write onto it, or by the same-file builder it is assigned from.
                var open = construction.IndexOf('(');
                var arguments = SplitTopLevel(construction[(open + 1)..]).Select(a => a.Trim()).ToList();
                var fields = arguments.Count > 2 ? arguments[2] : string.Empty;
                written = Regex.IsMatch(fields, @"^[A-Za-z_]\w*$")
                    && (WritesOwnerOn(region, fields, ownerKey, ownerWritingHelpers)
                        || InitializerOrBuilderOwns(code, region, fields, ownerKey, RegionAt));
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

    /// <summary>
    /// True when the field map <paramref name="v"/> owns its row: its initializer in <paramref name="region"/> (up to the
    /// end of the statement) names an owner key, or it is assigned from a call to a same-file member that writes one.
    /// </summary>
    private static bool InitializerOrBuilderOwns(
        string code, string region, string v, string ownerKey, Func<int, string> regionAt)
    {
        var assignment = Regex.Match(region, $@"\b{Regex.Escape(v)}\s*=\s*(?<rhs>[^;]*;)");
        if (!assignment.Success)
            return false;

        var rhs = assignment.Groups["rhs"].Value;
        if (Regex.IsMatch(rhs, ownerKey))
            return true;

        var builder = Regex.Match(rhs, @"^\s*(?:await\s+)?(?<name>[A-Za-z_]\w*)\s*\(");
        if (!builder.Success)
            return false;

        var declaration = Regex.Match(code,
            @"^[ \t]*(?:public|internal|private|protected)\b[^;=\n]*?\b" + Regex.Escape(builder.Groups["name"].Value) + @"\s*\(",
            RegexOptions.Multiline);
        return declaration.Success && Regex.IsMatch(regionAt(declaration.Index), $@"\[\s*(?:{ownerKey})\s*\]\s*=");
    }

    /// <summary>True when <paramref name="region"/> writes an owner onto the row variable <paramref name="v"/>.</summary>
    private static bool WritesOwnerOn(string region, string v, string ownerKey, IReadOnlySet<string>? helpers)
    {
        var name = Regex.Escape(v);
        if (Regex.IsMatch(region,
                $@"\b{name}\s*\[\s*(?:{ownerKey})\s*\]\s*=|\b{name}(?:\.Attributes)?\.(?:Add|TryAdd|SetAttributeValue)\s*\(\s*(?:{ownerKey})\s*,"
                + $@"|\.ApplyTo\s*\(\s*{name}\s*\)|\bApply\w*Owner\w*\s*\(\s*{name}\b"))
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

            // Task 146 b2 (verifier AC10): creates the literal shapes above cannot see, proven blind by seeding —
            // a create-by-upsert (a PATCH of a FRESH id — Guid.NewGuid(), or a variable assigned one or a generated id in
            // the same member) and a two-argument construction with a fresh id.
            var members = MemberDeclaration.Matches(code).Select(d => d.Index).ToList();
            foreach (Match m in CreateByUpsert.Matches(code))
            {
                if (IsFreshId(code, members, m.Index, m.Groups["rid"].Value))
                    Add(Resolve(m), m.Index);
            }

            // b2-r2 (verifier b2-r1 item 5 seed 4): a construction with a CALLER-CHOSEN id handed to a create is a create.
            foreach (Match m in FreshEntityCreate.Matches(code))
            {
                if (IsFreshId(code, members, m.Index, m.Groups["rid"].Value) || HandedToACreate(code, members, m.Index))
                    Add(Resolve(m), m.Index);
            }

            // Task 146 b2-r1 (verifier b2 item 6): a construction that names its table through LogicalName — in its
            // object initializer (`new Entity { LogicalName = "t" }`), or assigned in the same member after a
            // parameterless construction (`var e = new Entity(); e.LogicalName = "t";`). A create, exactly like the
            // one-argument form, unless the row is given an EXISTING id (`Id = existing` / `e.Id = existing`).
            foreach (Match m in InitializerEntityCreate.Matches(code))
            {
                var open = m.Index + m.Length - 1;
                var initializer = TopLevelOfInitializer(code[open..(MatchingClose(code, open) + 1)]);
                var named = InitializerLogicalName.Match(initializer);
                if (!named.Success)
                    continue;

                var id = InitializerId.Match(initializer);
                if (!id.Success || IsFreshId(code, members, m.Index, id.Groups["rid"].Value)
                    || HandedToACreate(code, members, m.Index))
                    Add(Resolve(named), m.Index);
            }

            foreach (Match m in LogicalNameAssignment.Matches(code))
            {
                var v = Regex.Escape(m.Groups["v"].Value);
                var start = members.LastOrDefault(i => i <= m.Index);
                var end = members.FirstOrDefault(i => i > m.Index);
                var member = code[start..(end > m.Index ? end : code.Length)];
                var constructed = Regex.IsMatch(member,
                    $@"\b{v}\s*=\s*new\s+(?:Microsoft\.Xrm\.Sdk\.)?(?:DataverseEntity|Entity)\s*(?:\(\s*\))?\s*[;{{]"
                    + $@"|\b(?:DataverseEntity|Entity)\s+{v}\s*=\s*new\s*\(\s*\)");
                if (!constructed)
                    continue;

                var id = Regex.Match(member, $@"\b{v}\s*\.\s*Id\s*=\s*(?<rid>Guid\.NewGuid\(\)|[A-Za-z_][\w\.]*)");
                if (!id.Success || IsFreshId(code, members, m.Index, id.Groups["rid"].Value)
                    || HandedToACreate(code, members, m.Index, row: m.Groups["v"].Value))
                    Add(Resolve(m), m.Index);
            }
        }

        return sites;
    }

    /// <summary>An entity construction with an object initializer and no constructor argument — the match ends at the
    /// initializer's opening brace.</summary>
    private static readonly Regex InitializerEntityCreate = new(
        @"new\s+(?:Microsoft\.Xrm\.Sdk\.)?(?:DataverseEntity|Entity)\s*(?:\(\s*\))?\s*\{",
        RegexOptions.Compiled);

    /// <summary>The table an entity initializer names (top level only — not a nested EntityReference's).</summary>
    private static readonly Regex InitializerLogicalName = new(
        @"\bLogicalName\s*=\s*(?:""(?<lit>[a-z_]+)""|(?<id>[A-Za-z_][\w\.]*))",
        RegexOptions.Compiled);

    /// <summary>The id an entity initializer gives its row (top level only).</summary>
    private static readonly Regex InitializerId = new(
        @"\bId\s*=\s*(?<rid>Guid\.NewGuid\(\)|[A-Za-z_][\w\.]*)",
        RegexOptions.Compiled);

    /// <summary><c>e.LogicalName = "t";</c> — a create when <c>e</c> was constructed without a table in the same member.</summary>
    private static readonly Regex LogicalNameAssignment = new(
        @"\b(?<v>[A-Za-z_]\w*)\s*\.\s*LogicalName\s*=\s*(?:""(?<lit>[a-z_]+)""|(?<id>[A-Za-z_][\w\.]*))\s*;",
        RegexOptions.Compiled);

    /// <summary>An initializer's text with every NESTED brace's content blanked, so a nested
    /// <c>new EntityReference { LogicalName = …, Id = … }</c> is never read as the row's own table or id.</summary>
    private static string TopLevelOfInitializer(string braced)
    {
        var chars = braced.ToCharArray();
        var depth = 0;
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '{')
            {
                depth++;
                if (depth > 1)
                    chars[i] = ' ';
            }
            else if (chars[i] == '}')
            {
                if (depth > 1)
                    chars[i] = ' ';
                depth--;
            }
            else if (depth > 1)
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }

    /// <summary>A field-map PATCH of a record id — a CREATE when the id is fresh (Dataverse upserts).</summary>
    private static readonly Regex CreateByUpsert = new(
        @"UpdateRecordFieldsAsync\s*\(\s*(?:""(?<lit>[a-z_]+)""|(?<id>[A-Za-z_][\w\.]*))\s*,\s*(?<rid>Guid\.NewGuid\(\)|[A-Za-z_]\w*)",
        RegexOptions.Compiled);

    /// <summary>A two-argument construction — a create (or upsert target) when the id is fresh, or when the row is handed
    /// to a create. b2-r2: the id may be a member access (<c>request.RecordId</c>, <c>anchor!.Id</c>), so such a row
    /// handed to a create is seen too.</summary>
    private static readonly Regex FreshEntityCreate = new(
        @"new\s+(?:Microsoft\.Xrm\.Sdk\.)?(?:DataverseEntity|Entity)\s*\(\s*(?:""(?<lit>[a-z_]+)""|(?<id>[A-Za-z_][\w\.]*))\s*,\s*(?<rid>Guid\.NewGuid\(\)|[A-Za-z_][\w\.!]*)\s*\)",
        RegexOptions.Compiled);

    /// <summary>True when <paramref name="id"/> is <c>Guid.NewGuid()</c>, or a variable the same member assigns from
    /// <c>Guid.NewGuid()</c> or a generated id (a deterministic upsert key, as SignalEvaluationService's).</summary>
    private static bool IsFreshId(string code, IReadOnlyList<int> members, int index, string id)
    {
        if (id.StartsWith("Guid.NewGuid", StringComparison.Ordinal))
            return true;

        var start = members.LastOrDefault(i => i <= index);
        var region = code[start..index];
        return Regex.IsMatch(region,
            @"\b" + Regex.Escape(id) + @"\s*=\s*(?:Guid\.NewGuid\s*\(\s*\)|[\w\.]*Generate\w*Id\s*\()");
    }

    /// <summary>A create call left open just before a construction: <c>CreateAsync(</c> / <c>Create(</c> /
    /// <c>CreateReviewLogAsync(</c>, or <c>new CreateRequest { … Target =</c>.</summary>
    private static readonly Regex CreateCallOpen = new(
        @"(?:\b(?:Create|Create\w*Async)\s*\(|\bnew\s+CreateRequest\b[^;]*?\bTarget\s*=)\s*$",
        RegexOptions.Compiled);

    /// <summary>
    /// b2-r2 (verifier b2-r1 item 5 seed 4): true when the row constructed at <paramref name="index"/> — or the row variable
    /// <paramref name="row"/> — is handed to a CREATE in the same member: inline (<c>CreateAsync(new Entity(T, id), ct)</c>,
    /// <c>new CreateRequest { Target = new Entity(T, id) }</c>) or through the variable it is assigned to. Dataverse creates
    /// with a supplied id, so a caller-chosen id does not make such a row an update. An UPSERT of a caller-chosen id is not
    /// seen (KNOWN LIMITS).
    /// </summary>
    private static bool HandedToACreate(string code, IReadOnlyList<int> members, int index, string? row = null)
    {
        var start = members.LastOrDefault(i => i <= index);
        var end = members.FirstOrDefault(i => i > index);
        var member = code[start..(end > index ? end : code.Length)];
        if (row is null)
        {
            var lead = code[start..index];
            if (CreateCallOpen.IsMatch(lead))
                return true;

            var assigned = Regex.Match(lead, @"\b(?<v>[A-Za-z_]\w*)\s*=\s*$");
            if (!assigned.Success)
                return false;
            row = assigned.Groups["v"].Value;
        }

        var v = Regex.Escape(row);
        return Regex.IsMatch(member,
            $@"\b(?:Create|Create\w*Async)\s*\(\s*{v}\s*[,)]|\bnew\s+CreateRequest\b[^;]*?\bTarget\s*=\s*{v}\b");
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
