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
        new OwnerSeam("ExternalDataService.cs", "BuildDocumentCreatePayload", Calls("RequireOwner")),
        new OwnerSeam("ExternalDataService.cs", "BuildEventCreatePayload", Calls("RequireOwner")),
        new OwnerSeam("ExternalDataService.cs", "BuildTodoCreatePayload", Calls("RequireOwner")),
        new OwnerSeam("ExternalDataService.cs", "RequireOwner", Refusal),
    };

    /// <summary>The refusal every seam throws: "... refusing to create ... app-owned ..." (a stray throw elsewhere in
    /// the method does not satisfy it).</summary>
    private static Regex Refusal => new(@"throw new InvalidOperationException\([\s\S]{0,400}?refusing to create");

    private static Regex Calls(string helper) => new(@"\b" + Regex.Escape(helper) + @"\s*\(");


    /// <summary>A resolver call — the ONE owner (task 146 constraint: no writer computes a team itself).</summary>
    private static readonly Regex ResolverCall = new(
        @"\b(ResolveOwnerAsync|ResolveOwningTeamAsync|ReparentAsync|AssignToThreadReconcilingOwnerAsync|ResolveDocumentOwnerTeamAsync)\s*\(",
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
                list.Add(SourceScan.LineOf(code, index));
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
