using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 034 (FR-21 part b, W3 direction 2026-11-06): the inbound-ingestion creators stay
/// OUTSIDE <c>RecordCreationService</c> as named exceptions. A creator outside the service is valid only when it calls the
/// same invariant-owner classes for every invariant its table carries, so this guard pins, for each of them, "this creator
/// writes this table and calls these owners, and no other creator does".
/// </summary>
/// <remarks>
/// <para><b>The creators (C1-C7), named.</b>
/// C1 <c>MessagingIngestor</c> (<c>sprk_communication</c>, always unfiled: the named exception
/// <c>UnfiledCommunicationKeepsCreator</c>); C2 <c>IncomingCommunicationProcessor</c> communication and C5
/// <c>EmailUploadCaptureService</c> communication (association evaluated first, owner from <c>ResolveOwnerAsync</c>, the
/// filing and the FR-26 stamps from <c>IncomingAssociationResolver</c>); C3 / C4 the processor's attachment rows and archived
/// .eml (the communication's resolved owner carried through <c>ApplyOwner</c>, never re-resolved); C6 no document-seam
/// creator: no file under Services/Communication or Services/Email calls <c>CreateDocumentAsync</c>
/// (<c>EmailAttachmentProcessor</c> is the attachment filter the processor asks, and creates nothing); and C7
/// <c>MessageAttachmentMaterializer</c> (owner resolved before the upload, applied to both rows).</para>
/// <para><b>What is pinned.</b> (1) The files that create the three tables are exactly the inventory, with the ingestors' own
/// create counts. (2) Each creator's owner calls, in order, and the only places <c>ownerid</c> is written. (3) The unfiled
/// exception is the ingestor path's alone: C1 can name no owner and no filing, and the unified service still refuses an
/// unfiled create on an <c>OwnershipException</c> table. (4) Attachment rows take their owner from the resolver and from
/// nothing else. (5) No inbound principal: <c>RequestedBy</c> is never fed by a message field. (6) The client's one document
/// create stages the parent link and the association on the entity it creates, so a caller that sets them on the create
/// request has them in the create.</para>
/// <para><b>What is not pinned here.</b> How many creates the outbound <c>CommunicationService</c> makes (the census owns
/// counts there), the satellites the ingestors call after the create (threads, participants, review logs: the census), and the
/// dedupe on the message id (<see cref="InboundIngestionDedupeGuardTests"/>). Known limit: the rules read source text with line
/// comments stripped, so a create built from a computed table name, or by a helper in another file, is not seen; the census and
/// the writers' behaviour tests (<c>SecureChildOwnershipWriterTests</c>, for the writers it drives) still cover those.</para>
/// <para><b>Controls.</b> Every rule has must-fire controls that apply one mutation to the REAL source (so the control cannot
/// drift from the shape it guards and cannot be vacuous: the mutated text must exist) and must-not-fire controls that change
/// the source without breaking the rule (a comment that names a forbidden token, a whitespace-only change). Enforced by: this guard
/// (ArchTests in CI and the Stop hook). No baseline: there are no violations today.</para>
/// </remarks>
public class InboundIngestionCreatorsGuardTests
{
    private const string Communication = "src/server/api/Sprk.Bff.Api/Services/Communication/";
    private const string Email = "src/server/api/Sprk.Bff.Api/Services/Email/";

    internal const string MessagingIngestorFile = Communication + "Channels/MessagingIngestor.cs";
    internal const string IncomingProcessorFile = Communication + "IncomingCommunicationProcessor.cs";
    internal const string UploadCaptureFile = Communication + "EmailUploadCaptureService.cs";
    internal const string ChatAttachmentFile = Communication + "MessageAttachmentMaterializer.cs";
    /// <summary>The attachment filter: in ingestion code, and a creator of nothing (C6).</summary>
    internal const string EmailAttachmentFilterFile = Email + "EmailAttachmentProcessor.cs";
    internal const string OutboundFile = Communication + "CommunicationService.cs";
    internal const string CreationServiceFile = "src/server/api/Sprk.Bff.Api/Services/RecordCreation/RecordCreationService.cs";
    internal const string DataverseClientFile = "src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs";

    // ── Rule data ──────────────────────────────────────────────────────────────────────────────────────────────────────

    private static Regex EntityLiteral(string table) => new(
        @"new\s+(?:[\w.]+\.)?(?:Entity|DataverseEntity)\s*\(\s*""" + table + @"""\s*\)|(?<!\w)LogicalName\s*=\s*""" + table + @"""",
        RegexOptions.Compiled);

    private static readonly Regex CommunicationLiteral = EntityLiteral("sprk_communication");
    private static readonly Regex AttachmentRowLiteral = EntityLiteral("sprk_communicationattachment");
    private static readonly Regex DocumentLiteral = EntityLiteral("sprk_document");
    private static readonly Regex RaceProofCall = new(@"\.CreateCommunicationRaceProofAsync\(", RegexOptions.Compiled);
    private static readonly Regex DocumentSeamCall = new(@"\.CreateDocumentAsync\(", RegexOptions.Compiled);
    private static readonly Regex GenericCreateCall = new(@"\.CreateAsync\(", RegexOptions.Compiled);

    /// <summary>The files (repo-relative) that may construct a <c>sprk_communication</c> row anywhere in src/server.</summary>
    internal static readonly IReadOnlySet<string> CommunicationCreators = new HashSet<string>(StringComparer.Ordinal)
    {
        MessagingIngestorFile, IncomingProcessorFile, UploadCaptureFile, OutboundFile,
    };

    /// <summary>The files that may construct a <c>sprk_communicationattachment</c> row anywhere in src/server.</summary>
    internal static readonly IReadOnlySet<string> AttachmentRowCreators = new HashSet<string>(StringComparer.Ordinal)
    {
        IncomingProcessorFile, ChatAttachmentFile, OutboundFile,
    };

    /// <summary>The files under Services/Communication and Services/Email that may construct a <c>sprk_document</c> row.</summary>
    internal static readonly IReadOnlySet<string> DocumentCreatorsInIngestionCode = new HashSet<string>(StringComparer.Ordinal)
    {
        IncomingProcessorFile, ChatAttachmentFile, OutboundFile,
    };

    private static readonly IReadOnlySet<string> RaceProofCallers = new HashSet<string>(StringComparer.Ordinal)
    {
        IncomingProcessorFile, UploadCaptureFile,
    };

    /// <summary>C6: the files under Services/Communication and Services/Email that may create a document through the document seam. None.</summary>
    internal static readonly IReadOnlySet<string> DocumentSeamCallersInIngestionCode = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The ingestors' own create shapes and how many of each the file holds: table literals and create calls.</summary>
    internal static readonly (string File, string What, Regex Shape, int Count)[] IngestorCreateCounts =
    [
        (MessagingIngestorFile, "sprk_communication rows", CommunicationLiteral, 1),
        (MessagingIngestorFile, "generic creates", GenericCreateCall, 1),
        (IncomingProcessorFile, "sprk_communication rows", CommunicationLiteral, 1),
        (IncomingProcessorFile, "sprk_document rows", DocumentLiteral, 2),
        (IncomingProcessorFile, "sprk_communicationattachment rows", AttachmentRowLiteral, 1),
        (IncomingProcessorFile, "generic creates", GenericCreateCall, 3),
        (IncomingProcessorFile, "race-proof creates", RaceProofCall, 1),
        (UploadCaptureFile, "sprk_communication rows", CommunicationLiteral, 1),
        (UploadCaptureFile, "generic creates", GenericCreateCall, 0),
        (UploadCaptureFile, "race-proof creates", RaceProofCall, 1),
        (ChatAttachmentFile, "sprk_document rows", DocumentLiteral, 1),
        (ChatAttachmentFile, "sprk_communicationattachment rows", AttachmentRowLiteral, 1),
        (ChatAttachmentFile, "generic creates", GenericCreateCall, 2),
    ];

    /// <summary>An owner on a row is written in exactly one shape: the resolver's team, and only when the resolver owned it.</summary>
    private static readonly Regex OwnerWrite = new(
        @"if\s*\(\s*owner\.IsOwned\s*\)\s*\{?\s*[\w.]+\[\s*""ownerid""\s*\]\s*=\s*new\s+EntityReference\(\s*""team""\s*,\s*owner\.OwningTeamId!\.Value\s*\)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex OwnerIdLiteral = new(@"""ownerid""", RegexOptions.Compiled);

    /// <summary>Any way of setting <c>ownerid</c> on a row (indexer or initializer assignment, SetAttributeValue, Attributes.Add); a column-list read is not a write.</summary>
    private static readonly Regex OwnerIdWrite = new(
        @"\[\s*""ownerid""\s*\]\s*=(?!=)|SetAttributeValue(?:<[^>]*>)?\(\s*""ownerid""|Attributes\.(?:Add|Remove)\(\s*""ownerid""",
        RegexOptions.Compiled);
    private static readonly Regex RequestedByName = new(@"\bRequestedBy\b", RegexOptions.Compiled);
    private static readonly Regex RequestedByAssignment = new(@"\bRequestedBy\s*=(?!=)", RegexOptions.Compiled);

    /// <summary>What an always-unfiled message row must not name: an owner, an owner resolution, a filing or a thread lookup.</summary>
    private static readonly (Regex Pattern, string Why)[] AlwaysUnfiledForbidden =
    [
        (new Regex(@"""ownerid""|owningteam|owninguser|\bOwningTeamId\b", RegexOptions.Compiled), "an owner"),
        (new Regex(@"\bRecordOwnerResolution\b|\bResolveOwnerAsync\b|\bReparentAsync\b|\bIRecordOwnershipResolver\b", RegexOptions.Compiled),
            "an owner resolution (a filed message is the thread resolver's)"),
        (new Regex(@"""sprk_regarding\w*""|""sprk_(?:matter|project|workassignment|servicerequest)""|""sprk_communicationthread""", RegexOptions.Compiled),
            "a filing or thread column"),
        (new Regex(@"\bEntityReference\b", RegexOptions.Compiled), "a lookup (the row names no record at create)"),
    ];

    // ── Rules (source text in, violations out; the controls feed the same functions) ───────────────────────────────────

    /// <summary>
    /// Closed set: the files in <paramref name="files"/> whose code matches <paramref name="shape"/> are exactly
    /// <paramref name="expected"/>. A file outside it is an unnamed creator; a file inside it that no longer matches has gone from
    /// the inventory.
    /// </summary>
    internal static IReadOnlyList<string> CreatorSetViolations(
        IEnumerable<(string Path, string Code)> files, Regex shape, IReadOnlySet<string> expected, string what)
    {
        var found = files.Where(f => shape.IsMatch(f.Code)).Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();
        foreach (var extra in found.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            violations.Add($"{extra} creates {what} but is not in the inbound-ingestion inventory (task 034). Decide whether it is an "
                + "ingestor: if it is, add it to the inventory with its owner calls and a rule; if it is not, say why in the census.");
        }

        foreach (var gone in expected.Except(found, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            violations.Add($"{gone} no longer creates {what}; remove it from the inventory (and the creator it named from the task 034 notes).");
        }

        return violations;
    }

    internal static IReadOnlyList<string> CreateCountViolations(string file, string code)
    {
        var violations = new List<string>();
        foreach (var (candidate, what, shape, count) in IngestorCreateCounts)
        {
            if (!string.Equals(candidate, file, StringComparison.Ordinal))
                continue;
            var actual = shape.Matches(code).Count;
            if (actual != count)
            {
                violations.Add($"{Path.GetFileName(file)} holds {actual} {what}; the inventory pins {count}. A create added to or removed from "
                    + "an ingestor changes which owner calls it must make: update the inventory and the owner rule with it.");
            }
        }

        return violations;
    }

    /// <summary>C1: the chat ingestor creates an UNFILED row and keeps its creator; it can name no owner and no filing.</summary>
    internal static IReadOnlyList<string> AlwaysUnfiledViolations(string code)
    {
        var violations = new List<string>();
        foreach (var (pattern, why) in AlwaysUnfiledForbidden)
        {
            if (pattern.IsMatch(code))
            {
                violations.Add($"MessagingIngestor names {why}. It is the named exception UnfiledCommunicationKeepsCreator: the row is created "
                    + "unfiled and keeps its creator, and a record thread files it later (ThreadResolver -> ReparentAsync). To file or own a "
                    + "message at create, resolve the owner with IRecordOwnershipResolver as IncomingCommunicationProcessor does, and move this rule.");
            }
        }

        if (!code.Contains("ResolveAndAssignAsync(", StringComparison.Ordinal))
        {
            violations.Add("MessagingIngestor no longer asks the thread resolver to file the message (ResolveAndAssignAsync), which is how the "
                + "unfiled row gets its owner and stamp when a record thread claims it.");
        }

        return violations;
    }

    /// <summary>C2 and C5: the association is evaluated, the owner resolved and the filing built BEFORE the communication is created.</summary>
    internal static IReadOnlyList<string> EmailIngestorOwnerViolations(string label, string code)
    {
        var violations = new List<string>();
        violations.AddRange(InOrder(label, code,
            "EvaluateAsync(", "OwnershipContextForNewRecordAsync(", "ResolveOwnerAsync(", "BuildNewRecordFieldsAsync(",
            "CreateCommunicationRaceProofAsync("));

        var resolves = Regex.Matches(code, @"\bResolveOwnerAsync\(").Count;
        if (resolves != 1)
        {
            violations.Add($"{label} calls ResolveOwnerAsync {resolves} times; the communication's owner is resolved once, and the rows it "
                + "owns carry that answer.");
        }

        violations.AddRange(OwnerWriteViolations(label, code));
        return violations;
    }

    /// <summary>The upload capture also records the saving user's person and the requester, and stamps before the create.</summary>
    internal static IReadOnlyList<string> UploadCaptureViolations(string code)
    {
        var violations = new List<string>(EmailIngestorOwnerViolations("EmailUploadCaptureService", code));
        violations.AddRange(InOrder("EmailUploadCaptureService", code, "StampCreatorOn(communication)", "CreateCommunicationRaceProofAsync("));
        return violations;
    }

    /// <summary>Every write of <c>ownerid</c> in the file is the resolver's team under <c>owner.IsOwned</c>; no user, literal or message-derived owner.</summary>
    internal static IReadOnlyList<string> OwnerWriteViolations(string label, string code)
    {
        var violations = new List<string>();
        var literals = OwnerIdWrite.Matches(code).Count;
        var resolverWrites = OwnerWrite.Matches(code).Count;
        if (literals != resolverWrites)
        {
            violations.Add($"{label} writes ownerid in {literals} place(s), of which {resolverWrites} are the resolver's team under "
                + "owner.IsOwned. An owner on an inbound row comes from IRecordOwnershipResolver and from nothing else (never a user, a literal "
                + "or a message field); an unfiled row keeps the creator and names no owner.");
        }

        if (Regex.IsMatch(code, @"new\s+EntityReference\(\s*""systemuser""|owninguser"))
            violations.Add($"{label} names a user as an owner. Inbound rows are team-owned from their filing or keep the creator.");
        return violations;
    }

    /// <summary>C3 and C4: attachment rows, attachment documents and the archived .eml carry the communication's resolved owner.</summary>
    internal static IReadOnlyList<string> CarriedOwnerViolations(string code)
    {
        var violations = new List<string>();
        violations.AddRange(InOrder("IncomingCommunicationProcessor", code, "ApplyOwner(attachmentDoc, owner)", "CreateAsync(attachmentDoc,"));
        violations.AddRange(InOrder("IncomingCommunicationProcessor", code, "ApplyOwner(attachmentRecord, owner)", "CreateAsync(attachmentRecord,"));
        violations.AddRange(InOrder("IncomingCommunicationProcessor", code, "ApplyOwner(document, owner)", "return document;"));
        if (!Regex.IsMatch(code, @"ArchivedEmlDocument\(\s*message\s*,[^;]*\bowner\)\s*;"))
            violations.Add("IncomingCommunicationProcessor builds the archived .eml without the communication's owner (ArchivedEmlDocument(..., owner)).");
        if (!Regex.IsMatch(code, @"ProcessIncomingAttachmentsAsync\(\s*message\.Attachments\s*,[^;]*\bowner\s*,\s*ct\s*\)\s*;"))
            violations.Add("IncomingCommunicationProcessor processes the attachments without the communication's owner.");
        if (!Regex.IsMatch(code, @"ArchiveEmlAsync\(\s*message\s*,\s*mailboxEmail\s*,\s*communicationId\s*,\s*owner\s*,\s*ct\s*\)\s*;"))
            violations.Add("IncomingCommunicationProcessor archives the .eml without the communication's owner.");
        return violations;
    }

    /// <summary>C7: resolve before any bytes move, refuse on a refusal, apply the answer to both rows before they are created.</summary>
    internal static IReadOnlyList<string> ChatAttachmentOwnerViolations(string code)
    {
        var violations = new List<string>();
        if (!Regex.IsMatch(code, @"ResolveOwnerAsync\(\s*[\w.]*ContentOf\(\s*""sprk_communication"""))
            violations.Add("MessageAttachmentMaterializer does not ask the resolver about the message as the attachment's parent (ContentOf(\"sprk_communication\", id)).");
        violations.AddRange(InOrder("MessageAttachmentMaterializer", code,
            "ResolveOwnerAsync(", "owner.IsRefused", "UploadSmallAsync(", "owner.ApplyTo(document)", "CreateAsync(document,",
            "owner.ApplyTo(attachment)", "CreateAsync(attachment,"));
        if (OwnerIdLiteral.IsMatch(code))
            violations.Add("MessageAttachmentMaterializer writes ownerid itself; the resolver's answer is applied with owner.ApplyTo.");
        return violations;
    }

    /// <summary>No inbound sender, header or message field becomes a requester; only the authenticated saver does.</summary>
    internal static IReadOnlyList<string> PrincipalViolations(string file, string code)
    {
        var violations = new List<string>();
        switch (file)
        {
            case UploadCaptureFile:
                if (RequestedByAssignment.Matches(code).Count != 1 || !Regex.IsMatch(code, @"RequestedBy\s*=\s*Sprk\.Bff\.Api\.Services\.Dataverse\.RecordRequester\.OfObjectId\(userId\)"))
                    violations.Add("EmailUploadCaptureService sets RequestedBy once, from RecordRequester.OfObjectId(userId), the authenticated saver.");
                if (Regex.IsMatch(code, @"\buserId\s*=[^=]"))
                    violations.Add("EmailUploadCaptureService assigns userId; it must stay the caller's parameter, never a value read from the email.");
                break;
            case ChatAttachmentFile:
                if (RequestedByAssignment.Matches(code).Count != 1 || !Regex.IsMatch(code, @"RequestedBy\s*=\s*request\.RequestedBy"))
                    violations.Add("MessageAttachmentMaterializer sets RequestedBy once, from the request's RequestedBy (the caller's, when it knows the sender).");
                break;
            default:
                if (RequestedByName.IsMatch(code))
                {
                    violations.Add($"{Path.GetFileName(file)} names RequestedBy. An inbound sender is never a principal (ADR-028): inbound rows are "
                        + "created by the application identity and name no requester.");
                }

                break;
        }

        return violations;
    }

    /// <summary>The unified service refuses a create on an OwnershipException table that is filed under nothing: the exception is not its to honour.</summary>
    internal static IReadOnlyList<string> ServiceRefusesUnfiledExceptionViolations(string code)
    {
        return Regex.IsMatch(code,
            @"rule\.OwnershipException\s+is\s*\{\s*\}\s*\w+\s*&&\s*parents\.Count\s*==\s*0\s*\)\s*\{\s*return\s*\(null,\s*new\s+RecordCreationRefusal\([^;]*RecordCreationRefusal\.OwnershipExceptionCode")
            ? []
            : ["RecordCreationService no longer refuses a create filed under nothing on a table with an OwnershipException "
               + "(record_creation.ownership_exception). The unfiled-communication creator exception is the ingestors' alone; if the service "
               + "may now create such a row, that is an owner-rule change for UAC-r2 and this guard and the registry rule move together."];
    }

    /// <summary>The client's one document create stages the parent link and the association on the entity it creates.</summary>
    internal static IReadOnlyList<string> DocumentCreateStagesLinksViolations(string code)
    {
        var violations = new List<string>(InOrder("DataverseServiceClientImpl.CreateDocumentAsync", code,
            "StageDocumentAssociation(document, request);", "StageDocumentParent(document, request);", "document[\"statuscode\"]"));
        if (!Regex.IsMatch(code, @"document\[""sprk_parentdocument""\]\s*=\s*new\s+EntityReference\(\s*""sprk_document""\s*,\s*parentId\s*\)"))
        {
            violations.Add("DataverseServiceClientImpl.StageDocumentParent no longer writes sprk_parentdocument as a lookup to sprk_document "
                + "(document[\"sprk_parentdocument\"] = new EntityReference(\"sprk_document\", parentId)).");
        }

        return violations;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Each token appears after the previous one (text order, first match at or after the previous match).</summary>
    internal static IReadOnlyList<string> InOrder(string label, string code, params string[] tokens)
    {
        var violations = new List<string>();
        var position = 0;
        foreach (var token in tokens)
        {
            var at = code.IndexOf(token, position, StringComparison.Ordinal);
            if (at < 0)
            {
                violations.Add($"{label}: expected `{token}` after `{(position == 0 ? "the start of the file" : "the previous step")}` in the order "
                    + $"{string.Join(" -> ", tokens)}; it is missing or out of order.");
                return violations;
            }

            position = at + token.Length;
        }

        return violations;
    }

    internal static string Read(string repoRelative) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, repoRelative.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    internal static string CodeOf(string source) => SourceScan.CodeText(source.Split('\n'));

    internal static string Code(string repoRelative) => CodeOf(Read(repoRelative));

    private static readonly Lazy<IReadOnlyList<(string Path, string Code)>> AllServerFiles = new(() =>
        SourceScan.ServerSourceFiles()
            .Select(f => (Path: SourceScan.Relative(f).Replace('\\', '/'), Code: CodeOf(File.ReadAllText(f).Replace("\r\n", "\n", StringComparison.Ordinal))))
            .ToList());

    /// <summary>Every server source file with its repo-relative path and comment-stripped code (read once per test run).</summary>
    internal static IReadOnlyList<(string Path, string Code)> ServerFiles() => AllServerFiles.Value;

    private static IReadOnlyList<string> RulesFor(string file, string code) => file switch
    {
        MessagingIngestorFile => [.. CreateCountViolations(file, code), .. AlwaysUnfiledViolations(code), .. PrincipalViolations(file, code)],
        IncomingProcessorFile =>
        [
            .. CreateCountViolations(file, code), .. EmailIngestorOwnerViolations("IncomingCommunicationProcessor", code),
            .. CarriedOwnerViolations(code), .. PrincipalViolations(file, code),
        ],
        UploadCaptureFile => [.. CreateCountViolations(file, code), .. UploadCaptureViolations(code), .. PrincipalViolations(file, code)],
        ChatAttachmentFile => [.. CreateCountViolations(file, code), .. ChatAttachmentOwnerViolations(code), .. PrincipalViolations(file, code)],
        DataverseClientFile => DocumentCreateStagesLinksViolations(code),
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "not an ingestor in the inventory"),
    };

    private static readonly string[] Ingestors =
        [MessagingIngestorFile, IncomingProcessorFile, UploadCaptureFile, ChatAttachmentFile];

    // ── The guard, against the real source ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCreatorsOfTheInboundTables_AreExactlyTheInventory()
    {
        var files = ServerFiles();
        var violations = new List<string>();
        violations.AddRange(CreatorSetViolations(files, CommunicationLiteral, CommunicationCreators, "sprk_communication rows"));
        violations.AddRange(CreatorSetViolations(files, AttachmentRowLiteral, AttachmentRowCreators, "sprk_communicationattachment rows"));
        violations.AddRange(CreatorSetViolations(files, RaceProofCall, RaceProofCallers, "communications through the race-proof create"));

        var ingestionCode = files.Where(f => f.Path.StartsWith(Communication, StringComparison.Ordinal) || f.Path.StartsWith(Email, StringComparison.Ordinal)).ToList();
        violations.AddRange(CreatorSetViolations(ingestionCode, DocumentLiteral, DocumentCreatorsInIngestionCode, "sprk_document rows"));
        violations.AddRange(CreatorSetViolations(ingestionCode, DocumentSeamCall, DocumentSeamCallersInIngestionCode, "documents through the document seam"));

        Assert.True(violations.Count == 0, "The inbound-ingestion creators drifted from the task 034 inventory:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void EachIngestor_CreatesWhatTheInventoryNames_AndCallsItsOwners()
    {
        var violations = new List<string>();
        foreach (var file in Ingestors)
            violations.AddRange(RulesFor(file, Code(file)));

        Assert.True(violations.Count == 0, "An inbound-ingestion creator no longer matches its pinned owner calls:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void TheUnifiedService_StillRefusesAnUnfiledCreateOnAnOwnershipExceptionTable()
    {
        var violations = ServiceRefusesUnfiledExceptionViolations(Code(CreationServiceFile));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public void TheDocumentCreate_StagesAnAttachmentsParentLinkAndAssociation()
    {
        var violations = DocumentCreateStagesLinksViolations(Code(DataverseClientFile));
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    // ── Controls: closed set ───────────────────────────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlySet<string> TwoFiles = new HashSet<string>(StringComparer.Ordinal) { "a/Known.cs", "b/AlsoKnown.cs" };

    [Fact]
    public void ClosedSet_FiresOnAnUnnamedCreator()
    {
        var files = new[]
        {
            ("a/Known.cs", "var e = new Entity(\"sprk_communication\") { };"),
            ("b/AlsoKnown.cs", "var e = new DataverseEntity(\"sprk_communication\");"),
            ("c/NewIngestor.cs", "var e = new Microsoft.Xrm.Sdk.Entity(\"sprk_communication\");"),
        };
        var violations = CreatorSetViolations(files, CommunicationLiteral, TwoFiles, "sprk_communication rows");
        Assert.Contains(violations, v => v.Contains("c/NewIngestor.cs", StringComparison.Ordinal));
        Assert.Single(violations);
    }

    [Fact]
    public void ClosedSet_FiresWhenAnInventoryCreatorStopsCreating()
    {
        var files = new[] { ("a/Known.cs", "var e = new Entity(\"sprk_communication\");"), ("b/AlsoKnown.cs", "// nothing here") };
        var violations = CreatorSetViolations(files, CommunicationLiteral, TwoFiles, "sprk_communication rows");
        Assert.Contains(violations, v => v.Contains("b/AlsoKnown.cs", StringComparison.Ordinal) && v.Contains("no longer creates", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("var e = new Entity(\"sprk_communication\", id);")] // a two-argument construction is an existing row, not a create
    [InlineData("var e = new Entity(\"sprk_communicationthread\");")] // another table
    [InlineData("var ctx = new Ctx { EntityLogicalName = \"sprk_communication\" };")] // a context property, not a literal create
    [InlineData("// var e = new Entity(\"sprk_communication\");")] // a comment (the real scan strips comments first)
    public void ClosedSet_DoesNotFireOnWhatIsNotACreate(string line)
    {
        var files = new[] { ("a/Known.cs", "var e = new Entity(\"sprk_communication\");"), ("b/AlsoKnown.cs", CodeOf(line) + "\nvar f = new Entity(\"sprk_communication\");") };
        Assert.Empty(CreatorSetViolations(files, CommunicationLiteral, TwoFiles, "sprk_communication rows"));
    }

    [Fact]
    public void ClosedSet_SeesALogicalNameCreateToo()
    {
        var files = new[] { ("c/Other.cs", "var e = new Entity { LogicalName = \"sprk_communicationattachment\" };") };
        Assert.Single(CreatorSetViolations(files, AttachmentRowLiteral, new HashSet<string>(StringComparer.Ordinal), "rows"));
    }

    [Fact]
    public void DocumentCreateStaging_FiresWhenTheShapeIsGone()
    {
        Assert.Contains(DocumentCreateStagesLinksViolations("var x = 1;"), v => v.Contains("StageDocumentParent", StringComparison.Ordinal));
    }

    /// <summary>C6 must fire: a document-seam create added to the attachment filter (or any ingestion file) is an unnamed creator.</summary>
    [Fact]
    public void NoDocumentSeamCreator_FiresWhenTheAttachmentFilterCreatesADocument()
    {
        const string from = "public bool ShouldFilterAttachment(";
        var source = Read(EmailAttachmentFilterFile);
        Assert.True(source.Contains(from, StringComparison.Ordinal), $"control is vacuous: `{from}` is not in {EmailAttachmentFilterFile}");
        var mutated = source.Replace(from,
            "private Task<string> Create(IDocumentDataverseService d) => d.CreateDocumentAsync(new CreateDocumentRequest(), default);\n    " + from,
            StringComparison.Ordinal);

        var violations = CreatorSetViolations(
            [(EmailAttachmentFilterFile, CodeOf(mutated))], DocumentSeamCall, DocumentSeamCallersInIngestionCode, "documents through the document seam");

        Assert.Contains(violations, v => v.Contains(EmailAttachmentFilterFile, StringComparison.Ordinal) && v.Contains("not in the inbound-ingestion inventory", StringComparison.Ordinal));
    }

    /// <summary>C6 must not fire: the real filter, and the filter with a comment that names the create, create nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("// _documentService.CreateDocumentAsync(createRequest, cancellationToken);\n")]
    public void NoDocumentSeamCreator_IgnoresTheFilterAndACommentNamingTheCreate(string prefix)
    {
        const string from = "public class EmailAttachmentProcessor";
        var source = Read(EmailAttachmentFilterFile);
        Assert.True(source.Contains(from, StringComparison.Ordinal), $"control is vacuous: `{from}` is not in {EmailAttachmentFilterFile}");

        var violations = CreatorSetViolations(
            [(EmailAttachmentFilterFile, CodeOf(source.Replace(from, prefix + from, StringComparison.Ordinal)))],
            DocumentSeamCall, DocumentSeamCallersInIngestionCode, "documents through the document seam");

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("if (rule.OwnershipException is { } exception && parents.Count == 0) { return (null, new RecordCreationRefusal(Kind.X, RecordCreationRefusal.OwnershipExceptionCode, \"m\")); }", true)]
    [InlineData("if (rule.OwnershipException is { } exception && parents.Count == 0) { return (null, new RecordCreationRefusal(Kind.X, Other.Code, \"m\")); }", false)]
    [InlineData("if (rule.OwnershipException is { } exception) { return (null, new RecordCreationRefusal(Kind.X, RecordCreationRefusal.OwnershipExceptionCode, \"m\")); }", false)]
    [InlineData("var x = 1;", false)]
    public void ServiceRefusal_FiresOnlyWhenTheUnfiledRefusalIsGone(string code, bool present)
    {
        Assert.Equal(present, ServiceRefusesUnfiledExceptionViolations(code).Count == 0);
    }

    // ── Controls: per-creator rules, as mutations of the real source ───────────────────────────────────────────────────

    public static TheoryData<string, string, string, string> MustFire => new()
    {
        // C1: the chat ingestor can name no owner and no filing.
        { MessagingIngestorFile, "[CommunicationInternalOnlyFloor.InternalOnlyField] = true,", "[CommunicationInternalOnlyFloor.InternalOnlyField] = true, [\"ownerid\"] = Guid.Empty,", "names an owner" },
        { MessagingIngestorFile, "if (message.Cc.Count > 0)", "communication[\"sprk_regardingmatter\"] = Guid.Empty; if (message.Cc.Count > 0)", "filing or thread column" },
        { MessagingIngestorFile, "if (message.Cc.Count > 0)", "var r = await ResolveOwnerAsync(null!, ct); if (message.Cc.Count > 0)", "owner resolution" },
        { MessagingIngestorFile, "return await _genericEntityService.CreateAsync(communication, ct);", "var other = await _genericEntityService.CreateAsync(communication, ct); return await _genericEntityService.CreateAsync(communication, ct);", "holds 2 generic creates" },
        { MessagingIngestorFile, "var assignment = await _threadResolver.ResolveAndAssignAsync(", "var assignment = await _threadResolver.Other(", "ResolveAndAssignAsync" },
        // C2: the mailbox path asks the owner classes in order and writes only the resolver's team.
        { IncomingProcessorFile, "_ownership.ResolveOwnerAsync(ownershipContext, ct)", "_ownership.Guess(ownershipContext, ct)", "ResolveOwnerAsync" },
        { IncomingProcessorFile, "_associationResolver.OwnershipContextForNewRecordAsync(decision, ct)", "_associationResolver.Other(decision, ct)", "OwnershipContextForNewRecordAsync" },
        { IncomingProcessorFile, "await _associationResolver.BuildNewRecordFieldsAsync(decision, ct)", "await _associationResolver.Other(decision, ct)", "BuildNewRecordFieldsAsync" },
        { IncomingProcessorFile, "row[\"ownerid\"] = new EntityReference(\"team\", owner.OwningTeamId!.Value);", "row[\"ownerid\"] = new EntityReference(\"systemuser\", Guid.Empty);", "writes ownerid" },
        { IncomingProcessorFile, "communication[\"ownerid\"] = new EntityReference(\"team\", owner.OwningTeamId!.Value);", "communication[\"ownerid\"] = new EntityReference(\"team\", Guid.Empty);", "writes ownerid" },
        // C3 / C4: attachments carry the communication's owner and never re-resolve one.
        { IncomingProcessorFile, "ApplyOwner(attachmentRecord, owner); // task 146 — owned like its communication", "// dropped", "ApplyOwner(attachmentRecord, owner)" },
        { IncomingProcessorFile, "ApplyOwner(attachmentDoc, owner); // task 146 — owned like its communication", "var again = await _ownership.ResolveOwnerAsync(null!, ct); ApplyOwner(attachmentDoc, again);", "calls ResolveOwnerAsync 2 times" },
        { IncomingProcessorFile, "ApplyOwner(document, owner); // task 146 — owned like its communication", "// dropped", "ApplyOwner(document, owner)" },
        { IncomingProcessorFile, "await ArchiveEmlAsync(message, mailboxEmail, communicationId, owner, ct);", "await ArchiveEmlAsync(message, mailboxEmail, communicationId, default!, ct);", "archives the .eml without" },
        // Principal: an inbound sender never becomes a requester.
        { IncomingProcessorFile, "communication[\"sprk_cc\"] = string.Join(\"; \", ccRecipients);", "communication[\"sprk_cc\"] = string.Join(\"; \", ccRecipients); var q = new Ctx { RequestedBy = Requester.Of(message.From) };", "names RequestedBy" },
        // C5: upload capture.
        { UploadCaptureFile, "owner.StampCreatorOn(communication);", "// dropped", "StampCreatorOn(communication)" },
        { UploadCaptureFile, "communication[\"ownerid\"] = new EntityReference(\"team\", owner.OwningTeamId!.Value);", "communication[\"ownerid\"] = new EntityReference(\"systemuser\", Guid.Empty);", "writes ownerid" },
        { UploadCaptureFile, "RecordRequester.OfObjectId(userId)", "RecordRequester.OfObjectId(email.From)", "sets RequestedBy once" },
        { UploadCaptureFile, "var owner = await _ownership.ResolveOwnerAsync(", "var owner = await _ownership.Other(", "ResolveOwnerAsync" },
        // (6): the client's document create stages the parent link and the association.
        { DataverseClientFile, "StageDocumentParent(document, request);", "// dropped", "StageDocumentParent" },
        { DataverseClientFile, "document[\"sprk_parentdocument\"] = new EntityReference(\"sprk_document\", parentId);", "document[\"sprk_parentdocument\"] = new EntityReference(\"sprk_matter\", parentId);", "no longer writes sprk_parentdocument" },
        // C7: owner before the bytes, applied to both rows.
        { ChatAttachmentFile, "owner.ApplyTo(document); // task 146", "// dropped", "owner.ApplyTo(document)" },
        { ChatAttachmentFile, "owner.ApplyTo(attachment); // task 146", "// dropped", "owner.ApplyTo(attachment)" },
        { ChatAttachmentFile, "if (owner.IsRefused)", "if (false)", "owner.IsRefused" },
        { ChatAttachmentFile, "RequestedBy = request.RequestedBy }", "RequestedBy = request.FileName }", "sets RequestedBy once" },
    };

    [Theory]
    [MemberData(nameof(MustFire))]
    public void EachRule_FiresOnAMutationOfTheRealSource(string file, string from, string to, string expected)
    {
        var source = Read(file);
        Assert.True(source.Contains(from, StringComparison.Ordinal), $"control is vacuous: `{from}` is not in {file}");

        var violations = RulesFor(file, CodeOf(source.Replace(from, to, StringComparison.Ordinal)));

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string> MustNotFire => new()
    {
        // A comment that names every forbidden token is not code.
        { MessagingIngestorFile, "public sealed class MessagingIngestor", "// \"ownerid\" ResolveOwnerAsync ReparentAsync sprk_regardingmatter EntityReference RecordOwnerResolution\npublic sealed class MessagingIngestor" },
        { IncomingProcessorFile, "public sealed class IncomingCommunicationProcessor", "// ResolveOwnerAsync(x) \"ownerid\" RequestedBy\npublic sealed class IncomingCommunicationProcessor" },
        { UploadCaptureFile, "public sealed class EmailUploadCaptureService", "// \"ownerid\" = ResolveOwnerAsync( owner RequestedBy\npublic sealed class EmailUploadCaptureService" },
        { ChatAttachmentFile, "public sealed class MessageAttachmentMaterializer", "// \"ownerid\" RequestedBy\npublic sealed class MessageAttachmentMaterializer" },
        // A whitespace-only change (blank lines before the class) changes nothing the rules read, and does not tie a control to log wording.
        { MessagingIngestorFile, "public sealed class MessagingIngestor", "\n\n   \n\npublic sealed class MessagingIngestor" },
        { IncomingProcessorFile, "public sealed class IncomingCommunicationProcessor", "\n\n   \n\npublic sealed class IncomingCommunicationProcessor" },
        { UploadCaptureFile, "public sealed class EmailUploadCaptureService", "\n\n   \n\npublic sealed class EmailUploadCaptureService" },
        { ChatAttachmentFile, "public sealed class MessageAttachmentMaterializer", "\n\n   \n\npublic sealed class MessageAttachmentMaterializer" },
        { DataverseClientFile, "internal static void StageDocumentParent(", "// StageDocumentParent(document, request); document[\"sprk_parentdocument\"] = x;\n    internal static void StageDocumentParent(" },
    };

    [Theory]
    [MemberData(nameof(MustNotFire))]
    public void EachRule_IgnoresAChangeThatBreaksNothing(string file, string from, string to)
    {
        var source = Read(file);
        Assert.True(source.Contains(from, StringComparison.Ordinal), $"control is vacuous: `{from}` is not in {file}");

        var violations = RulesFor(file, CodeOf(source.Replace(from, to, StringComparison.Ordinal)));

        Assert.Empty(violations);
    }

    [Fact]
    public void ClosedSetCreateCounts_FireOnAnExtraCreate()
    {
        var source = Read(IncomingProcessorFile);
        var from = "var documentId = await _genericEntityService.CreateAsync(document, ct);";
        Assert.True(source.Contains(from, StringComparison.Ordinal), "control is vacuous");
        var mutated = source.Replace(from, from + " var extra = await _genericEntityService.CreateAsync(document, ct);", StringComparison.Ordinal);

        Assert.Contains(CreateCountViolations(IncomingProcessorFile, CodeOf(mutated)), v => v.Contains("generic creates", StringComparison.Ordinal));
        Assert.Empty(CreateCountViolations(IncomingProcessorFile, Code(IncomingProcessorFile)));
    }
}
