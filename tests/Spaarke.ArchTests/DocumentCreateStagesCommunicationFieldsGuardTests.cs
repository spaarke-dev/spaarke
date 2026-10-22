using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// spaarkeai-word-add-in-r1 task 130 (#2126): <c>DataverseServiceClientImpl.CreateDocumentAsync</c> must call
/// <c>StageDocumentCommunicationFields(document, request)</c>, the call that puts <c>sprk_relatedcommunication</c> and
/// <c>sprk_accesspermission</c> on the created <c>sprk_document</c>; and every Office save-path create applies
/// <c>OfficeDocumentPersistence.ApplyEmailCommunicationOutcome</c> to its request first (write-path registry I-19).
/// </summary>
/// <remarks>
/// <para><b>Why a source scan.</b> Every Office test mocks <c>IDataverseService.CreateDocumentAsync</c>, and the real
/// method creates through a <c>ServiceClient</c> that only connects to a live org (no seam to hand it an in-memory
/// service), so deleting the one staging line breaks no test: the stager's own unit test calls it directly. Without the
/// call every email-derived document is created with neither the link nor Restricted, the exact open-by-default state task
/// 130 removes — and nothing fails. Enforced by: this guard (ArchTests, CI and the Stop hook), per CLAUDE.md §16.</para>
/// <para><b>What it checks.</b> The body of <c>CreateDocumentAsync</c> (code text: comments stripped, so a commented-out call
/// does not count) contains a call to <c>StageDocumentCommunicationFields</c> with the <c>request</c> parameter.
/// <b>Stated limit:</b> it proves the call is present, not that it runs on every path or that the stager maps correctly —
/// <c>DocumentCommunicationFieldsStagingTests</c> pins the mapping.</para>
/// </remarks>
public class DocumentCreateStagesCommunicationFieldsGuardTests
{
    private const string File_ = "src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs";
    private const string Method = "CreateDocumentAsync";

    private static readonly Regex StagerCall = new(
        @"\bStageDocumentCommunicationFields\s*\(\s*\w+\s*,\s*request\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The code text of the body of the first method named <paramref name="method"/> in <paramref name="source"/>,
    /// or null when there is none.</summary>
    internal static string? MethodBody(string source, string method)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var declaration = Regex.Match(code, @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{", RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return null;

        var depth = 0;
        for (var i = declaration.Index + declaration.Length - 1; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[declaration.Index..(i + 1)];
        }

        return null;
    }

    /// <summary>True when <paramref name="source"/>'s <c>CreateDocumentAsync</c> body calls the stager.</summary>
    internal static bool StagesCommunicationFields(string source)
        => MethodBody(source, Method) is { } body && StagerCall.IsMatch(body);

    // ── The Office create sites (write-path registry I-19) ────────────────────────────────────────────

    /// <summary>The Office save-path files that create <c>sprk_document</c> rows from an email (I-19's owners' callers).</summary>
    private static readonly string[] OfficeCreateFiles =
    [
        "src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs",
        "src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs",
    ];

    /// <summary>Today's create sites across <see cref="OfficeCreateFiles"/>: the save's create, the worker's fallback
    /// create and its attachment-child create. A drop means the scan lost a site (moved or renamed): update the guard.</summary>
    private const int ExpectedOfficeCreateSites = 3;

    private static readonly Regex DocumentCreateCall = new(
        @"\b_documentService\s*\.\s*CreateDocumentAsync\s*\(\s*(?<request>\w+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Every <c>_documentService.CreateDocumentAsync(x, …)</c> in <paramref name="source"/> (code text, comments stripped)
    /// with whether <c>ApplyEmailCommunicationOutcome(x, …)</c> runs between <c>x</c>'s <c>new CreateDocumentRequest</c>
    /// and the create. A create whose request is not built in view counts as a violation.
    /// </summary>
    internal static IReadOnlyList<(string Request, bool AppliesOutcome)> OfficeCreateSites(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        var sites = new List<(string, bool)>();
        foreach (Match call in DocumentCreateCall.Matches(code))
        {
            var request = call.Groups["request"].Value;
            var declarations = Regex.Matches(
                code[..call.Index], @"\b" + Regex.Escape(request) + @"\s*=\s*new\s+(?:[\w.]+\.)?CreateDocumentRequest\b",
                RegexOptions.CultureInvariant);
            var applies = declarations.Count > 0
                && Regex.IsMatch(
                    code[declarations[^1].Index..call.Index],
                    @"\bApplyEmailCommunicationOutcome\s*\(\s*" + Regex.Escape(request) + @"\s*,",
                    RegexOptions.CultureInvariant);
            sites.Add((request, applies));
        }

        return sites;
    }

    [Fact(DisplayName = "Task 130 (I-19): every Office sprk_document create applies the email communication outcome")]
    public void EveryOfficeDocumentCreate_AppliesTheEmailCommunicationOutcome()
    {
        var total = 0;
        foreach (var file in OfficeCreateFiles)
        {
            var path = Path.Combine(SourceScan.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(System.IO.File.Exists(path), $"{file} not found; update this guard if the file moved.");

            var sites = OfficeCreateSites(System.IO.File.ReadAllText(path));
            total += sites.Count;
            foreach (var (request, applies) in sites)
            {
                Assert.True(
                    applies,
                    $"{file}: CreateDocumentAsync({request}, …) runs without OfficeDocumentPersistence.ApplyEmailCommunicationOutcome"
                    + $"({request}, …) after the request is built. An email-derived document would be created with neither "
                    + "sprk_relatedcommunication nor Restricted (write-path registry I-19, task 130, #2126).");
            }
        }

        Assert.True(
            total >= ExpectedOfficeCreateSites,
            $"Found {total} Office sprk_document create sites, expected at least {ExpectedOfficeCreateSites}: a site moved out of "
            + "view of this guard. Add its file to OfficeCreateFiles.");
    }

    [Fact(DisplayName = "Task 130: DataverseServiceClientImpl.CreateDocumentAsync stages the communication link and access permission")]
    public void CreateDocumentAsync_CallsStageDocumentCommunicationFields()
    {
        var path = Path.Combine(SourceScan.RepoRoot, File_.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(System.IO.File.Exists(path), $"{File_} not found; update this guard if the file moved.");

        Assert.True(
            StagesCommunicationFields(System.IO.File.ReadAllText(path)),
            $"{File_}: {Method} no longer calls StageDocumentCommunicationFields(document, request). Without it an "
            + "email-derived sprk_document is created with neither sprk_relatedcommunication nor Restricted "
            + "sprk_accesspermission (task 130, #2126), and no Office test notices because they all mock the create.");
    }

    // ── The own-narrowing record has one setter (#2177 UAC pass 1, K2) ─────────────────────────────────

    /// <summary>The one member that may set <c>CreateDocumentRequest.AccessInheritanceRecord</c>.</summary>
    private const string OutcomeFile = "src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs";
    private const string OutcomeMethod = "ApplyEmailCommunicationOutcome";

    private static readonly Regex RecordAssignment = new(
        @"\bAccessInheritanceRecord\s*(?:\?\?|[-+*/%&|^]|<<|>>)?=(?![=>])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The <c>AccessInheritanceRecord</c> assignments in <paramref name="source"/> (code text) that are NOT inside
    /// <see cref="OutcomeMethod"/>, when <paramref name="isOutcomeFile"/>; every assignment otherwise.
    /// </summary>
    internal static int RecordAssignmentsOutsideTheOutcome(string source, bool isOutcomeFile)
    {
        var total = RecordAssignment.Matches(SourceScan.CodeText(source.Split('\n'))).Count;
        var inOutcome = isOutcomeFile && MethodBody(source, OutcomeMethod) is { } body ? RecordAssignment.Matches(body).Count : 0;
        return total - inOutcome;
    }

    [Fact(DisplayName = "Task 130 (I-19): only ApplyEmailCommunicationOutcome sets a document's own-narrowing record")]
    public void AccessInheritanceRecord_IsSetOnlyByTheEmailCommunicationOutcome()
    {
        // The stager writes a document's own Access Permission only with this record (task 189), so the one setter of the
        // record is the one place a create can carry an own narrowing. A second setter would be a new document writer that
        // 189's writer guard cannot see: it lists the stager, not the stager's callers.
        var problems = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            var outside = RecordAssignmentsOutsideTheOutcome(System.IO.File.ReadAllText(file), relative == OutcomeFile);
            if (outside > 0)
                problems.Add($"{relative}: {outside} assignment(s) of AccessInheritanceRecord outside {OutcomeMethod}");
        }

        var outcomePath = Path.Combine(SourceScan.RepoRoot, OutcomeFile.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(
            MethodBody(System.IO.File.ReadAllText(outcomePath), OutcomeMethod) is { } body && RecordAssignment.Matches(body).Count >= 2,
            $"{OutcomeFile} {OutcomeMethod} should set AccessInheritanceRecord (linked and Restricted): the scan lost its signal.");
        Assert.True(problems.Count == 0,
            "CreateDocumentRequest.AccessInheritanceRecord is set only by OfficeDocumentPersistence.ApplyEmailCommunicationOutcome " +
            "(write-path registry I-19, #2177). Decide a new own narrowing there, or add it to task 189's writer guard:\n" +
            string.Join("\n", problems));
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Task 130 guard control: an own-narrowing record set outside the outcome is counted; reads are not")]
    public void Control_RecordAssignmentOutsideTheOutcome()
    {
        const string source = """
            internal static bool ApplyEmailCommunicationOutcome(CreateDocumentRequest create, Guid? id)
            {
                create.AccessInheritanceRecord = null;
                create.AccessInheritanceRecord = AccessInheritance.OwnNarrowing(Restricted).Serialize();
                return true;
            }

            void Elsewhere(CreateDocumentRequest create)
            {
                // create.AccessInheritanceRecord = "x";   (commented out: does not count)
                var record = create.AccessInheritanceRecord;
                if (create.AccessInheritanceRecord == null) { }
                if (create.AccessInheritanceRecord != null) { }
                var request = new CreateDocumentRequest { AccessInheritanceRecord = "x" };
                create.AccessInheritanceRecord ??= "{}";
            }
            """;

        Assert.Equal(2, RecordAssignmentsOutsideTheOutcome(source, isOutcomeFile: true));
        Assert.Equal(4, RecordAssignmentsOutsideTheOutcome(source, isOutcomeFile: false));
    }

    [Fact(DisplayName = "Task 130 guard control: a CreateDocumentAsync body without the stager call fails the scan")]
    public void Control_BodyWithoutTheCall_IsAViolation()
    {
        const string source = """
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                document["sprk_documentname"] = request.Name;
                // StageDocumentCommunicationFields(document, request);   (commented out: does not count)
                document["statuscode"] = new OptionSetValue(1);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }

            internal static void StageDocumentCommunicationFields(Entity document, CreateDocumentRequest request) { }
            """;

        Assert.False(StagesCommunicationFields(source));
    }

    [Fact(DisplayName = "Task 130 guard control: a stager call in another method does not satisfy CreateDocumentAsync")]
    public void Control_CallInAnotherMethod_IsAViolation()
    {
        const string source = """
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }

            public void Other(Entity document, CreateDocumentRequest request)
            {
                StageDocumentCommunicationFields(document, request);
            }
            """;

        Assert.False(StagesCommunicationFields(source));
    }

    [Fact(DisplayName = "Task 130 guard control: a CreateDocumentAsync body that calls the stager passes")]
    public void Control_BodyWithTheCall_IsNotAViolation()
    {
        const string source = """
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                if (request.Id is { } id) { document.Id = id; }
                StageDocumentCommunicationFields(document, request);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """;

        Assert.True(StagesCommunicationFields(source));
    }

    [Fact(DisplayName = "Task 130 guard control: an Office create without the outcome call is a violation")]
    public void Control_OfficeCreateWithoutTheOutcome_IsAViolation()
    {
        const string source = """
            var createRequest = new Spaarke.Dataverse.CreateDocumentRequest { Name = "x" };
            // OfficeDocumentPersistence.ApplyEmailCommunicationOutcome(createRequest, id);   (commented out: does not count)
            var id = await _documentService.CreateDocumentAsync(createRequest, ct);
            """;

        Assert.Equal([("createRequest", false)], OfficeCreateSites(source));
    }

    [Fact(DisplayName = "Task 130 guard control: an outcome call for an earlier request does not cover a later one")]
    public void Control_OutcomeForAnEarlierRequest_IsAViolation()
    {
        const string source = """
            var createRequest = new CreateDocumentRequest { Name = "first" };
            OfficeDocumentPersistence.ApplyEmailCommunicationOutcome(createRequest, id);
            await _documentService.CreateDocumentAsync(createRequest, ct);
            createRequest = new CreateDocumentRequest { Name = "second" };
            await _documentService.CreateDocumentAsync(createRequest, ct);
            """;

        Assert.Equal([("createRequest", true), ("createRequest", false)], OfficeCreateSites(source));
    }

    [Fact(DisplayName = "Task 130 guard control: an Office create that applies the outcome passes")]
    public void Control_OfficeCreateWithTheOutcome_IsNotAViolation()
    {
        const string source = """
            var createRequest = new CreateDocumentRequest { Name = "x" };
            var restricted = IsEmailDerived(type) && OfficeDocumentPersistence.ApplyEmailCommunicationOutcome(createRequest, id);
            var documentId = await _documentService.CreateDocumentAsync(createRequest, cancellationToken);
            """;

        Assert.Equal([("createRequest", true)], OfficeCreateSites(source));
    }
}
