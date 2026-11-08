using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 030b (W3 direction, owner 2026-11-06, reviewed by UAC-r2): the Office document
/// save stays OUTSIDE <c>RecordCreationService</c> as a named exception, so these guards pin what the service would
/// otherwise guarantee for it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>G1</b> the association is written in the CREATE of every Office <c>sprk_document</c>, never in an update;</item>
/// <item><b>G2</b> <c>DataverseServiceClientImpl.CreateDocumentAsync</c> stages that association on the create entity;</item>
/// <item><b>G3</b> the save calls the owner class (<c>ResolveOwnerAsync</c> over <c>OfficeDocumentPersistence.DocumentOwnershipOf</c>)
/// and every Office create carries its owner team;</item>
/// <item><b>G4</b> only the server writes the narrowing and pointer columns: <c>CreateDocumentRequest</c>'s server-only members
/// are <c>[JsonIgnore]</c>, and the Office save body names no pointer, narrowing, owner or new-document id;</item>
/// <item><b>G5</b> a given document id is created, never upserted: the create never upserts, only the Office save sets a create's
/// id, and that id is minted by the server.</item>
/// <item><b>G6</b> every Office create carries its SPE pointer, and an attachment child carries its parent link, in the create;
/// <c>CreateDocumentAsync</c> stages both.</item>
/// </list>
/// <para><b>Why source scans.</b> Every Office test mocks <c>IDocumentDataverseService</c>, and the real create runs only
/// against a live org, so moving the association back into the update, or upserting a given id, would pass every behaviour
/// test that does not look for it. The behaviour is pinned by <c>OfficeDocumentCreateAssociationTests</c> and
/// <c>DocumentAssociationCreateStagingTests</c>; these guards fail the build when the shape that behaviour depends on is undone.
/// Enforced by: ArchTests (CI and the Stop hook), root CLAUDE.md §16.</para>
/// <para><b>Stated limits.</b> The scans see the shapes their controls pin (code text, line comments stripped): an assignment
/// through a variable whose declaration is not in the file, a reflection write, or a property set outside the two Office
/// creator files is not seen by G1 and G3. The I-1 part of "the save calls the stamp class" is a column agreement, not a call:
/// a document has no stamp column, and <c>CoreAncestorResolver</c> reads its root from the association column the create
/// writes (pinned by <c>DocumentAssociationCreateStagingTests</c>).</para>
/// </remarks>
public class OfficeDocumentSaveGuardTests
{
    private const string PersistenceFile = "src/server/api/Sprk.Bff.Api/Services/Office/OfficeDocumentPersistence.cs";
    private const string WorkerFile = "src/server/api/Sprk.Bff.Api/Workers/Office/UploadFinalizationWorker.cs";
    private const string OfficeServiceFile = "src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs";
    private const string DataverseClientFile = "src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs";

    /// <summary>The Office files that create <c>sprk_document</c> rows (the save and the finalization worker).</summary>
    private static readonly string[] OfficeCreatorFiles = [PersistenceFile, WorkerFile];

    private static string Read(string file)
    {
        var path = Path.Combine(SourceScan.RepoRoot, file.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path), $"{file} not found; update this guard if the file moved.");
        return File.ReadAllText(path);
    }

    private static string Code(string source) => SourceScan.CodeText(source.Split('\n'));

    // ── Shared detectors ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The request type <paramref name="variable"/> holds at <paramref name="index"/>: from its last
    /// <c>variable = new (ns.)Type</c>, or its last declaration <c>(ns.)Type variable</c> (a parameter or a typed local),
    /// whichever is nearer. <see langword="null"/> when neither is in view.
    /// </summary>
    internal static string? TypeOf(string code, string variable, int index)
    {
        var before = code[..index];
        var assigned = Regex.Matches(before,
            @"\b" + Regex.Escape(variable) + @"\s*=\s*new\s+(?:[\w.]+\.)?(?<type>\w+)\b", RegexOptions.CultureInvariant);
        var declared = Regex.Matches(before,
            @"(?:[\w.]+\.)?(?<type>\w+DocumentRequest)\??\s+" + Regex.Escape(variable) + @"\b", RegexOptions.CultureInvariant);

        Match? last = null;
        if (assigned.Count > 0) last = assigned[^1];
        if (declared.Count > 0 && (last is null || declared[^1].Index > last.Index)) last = declared[^1];
        return last?.Groups["type"].Value;
    }

    /// <summary>The [start, end) span of the body of the first method named <paramref name="method"/>, or null.</summary>
    internal static (int Start, int End)? MethodSpan(string code, string method)
    {
        var declaration = Regex.Match(code, @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{", RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return null;

        var depth = 0;
        for (var i = declaration.Index + declaration.Length - 1; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return (declaration.Index, i + 1);
        }

        return null;
    }

    /// <summary>The text of the balanced <c>{ … }</c> starting at <paramref name="open"/> (which must be a <c>{</c>).</summary>
    private static string Block(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0)
                return code[open..(i + 1)];
        }

        return code[open..];
    }

    // ── G1: the association is in the create ──────────────────────────────────────────────────────────────────────

    private static readonly Regex AssociationApply = new(
        @"\b(?:DocumentAssociationMap\s*\.\s*TryApply|ApplyAssociationLookup)\s*\(\s*(?<target>\w+)\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AssociationLookupWrite = new(
        @"\b(?:Matter|Project|Invoice|WorkAssignment|Event|Todo|Contact)Lookup\s*=(?![=>])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Members of the Office creator files that apply an association to a request that is never written, each with the
    /// reason. Code review checks the reason; a member that writes may not be listed.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NonWritingAppliers = new Dictionary<string, string>
    {
        ["DocumentOwnershipOf"] = "builds the owner QUESTION from an UpdateDocumentRequest it never sends (RecordReparent.ParentChangesOf)",
    };

    /// <summary>G1 over one file's source: the violations, and how many association applies were found.</summary>
    internal static (IReadOnlyList<string> Violations, int ApplySites) AssociationScan(string source, string file)
    {
        var code = Code(source);
        var excluded = NonWritingAppliers.Keys
            .Select(m => MethodSpan(code, m))
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToList();
        bool IsExcluded(int index) => excluded.Any(s => index >= s.Start && index < s.End);

        var violations = new List<string>();
        var sites = 0;
        foreach (Match apply in AssociationApply.Matches(code))
        {
            if (IsExcluded(apply.Index))
                continue;

            sites++;
            var target = apply.Groups["target"].Value;
            var type = TypeOf(code, target, apply.Index);
            if (type != "CreateDocumentRequest")
            {
                violations.Add($"{file}:{SourceScan.LineOf(code, apply.Index)}: the association is applied to '{target}' "
                               + $"({type ?? "type not in view"}), not to a CreateDocumentRequest");
            }
        }

        foreach (Match write in AssociationLookupWrite.Matches(code))
        {
            if (!IsExcluded(write.Index))
                violations.Add($"{file}:{SourceScan.LineOf(code, write.Index)}: an association lookup is set on a request: {write.Value.Trim()}");
        }

        return (violations, sites);
    }

    /// <summary>Today: the save's create, the worker's fallback create (through ApplyAssociationLookup) and the helper's own
    /// TryApply. A drop means a site moved out of view.</summary>
    private const int ExpectedAssociationApplySites = 4;

    [Fact(DisplayName = "Task 030b G1: every Office sprk_document is filed in its CREATE, never by an update")]
    public void G1_TheAssociationIsAppliedOnlyToTheCreate()
    {
        var violations = new List<string>();
        var sites = 0;
        foreach (var file in OfficeCreatorFiles)
        {
            var (fileViolations, fileSites) = AssociationScan(Read(file), file);
            violations.AddRange(fileViolations);
            sites += fileSites;
        }

        Assert.True(violations.Count == 0,
            "An Office document's association must be part of its create (task 030b, FR-18): a document filed by a follow-up "
            + "update exists, however briefly, filed nowhere, and stays so when that update fails. Apply it with "
            + "DocumentAssociationMap.TryApply(createRequest, …) before CreateDocumentAsync:\n  " + string.Join("\n  ", violations));
        Assert.True(sites >= ExpectedAssociationApplySites,
            $"Found {sites} association applies in the Office creator files, expected at least {ExpectedAssociationApplySites}: "
            + "the scan lost a site. Update the guard.");
    }

    [Fact(DisplayName = "Task 030b G1 control: an association on an update, or a lookup set on a request, fires")]
    public void G1_Control_MustFire()
    {
        const string source = """
            var updateRequest = new UpdateDocumentRequest { GraphItemId = itemId };
            DocumentAssociationMap.TryApply(updateRequest, type, id);
            var other = new Spaarke.Dataverse.UpdateDocumentRequest();
            ApplyAssociationLookup(other, type, id);
            updateRequest.MatterLookup = id;
            var third = new UpdateDocumentRequest { ProjectLookup = id };
            ApplyAssociationLookup(unknown, type, id);
            """;

        var (violations, sites) = AssociationScan(source, "A.cs");

        Assert.Equal(3, sites);
        Assert.Equal(5, violations.Count);
    }

    [Fact(DisplayName = "Task 030b G1 control: an association on a create, a typed parameter, a comment or the owner question does not fire")]
    public void G1_Control_MustNotFire()
    {
        const string source = """
            var createRequest = new CreateDocumentRequest { Name = "x", OwningTeamId = team };
            if (!DocumentAssociationMap.TryApply(createRequest, type, id)) { }
            // DocumentAssociationMap.TryApply(updateRequest, type, id);   (a comment)
            private void ApplyAssociationLookup(Spaarke.Dataverse.CreateDocumentRequest request, string? type, Guid? id)
            {
                if (!DocumentAssociationMap.TryApply(request, type, id)) { }
            }
            internal static RecordOwnershipContext DocumentOwnershipOf(string? type, Guid? id)
            {
                var association = new UpdateDocumentRequest();
                DocumentAssociationMap.TryApply(association, type, id);
                return null;
            }
            """;

        var (violations, sites) = AssociationScan(source, "B.cs");

        Assert.Empty(violations);
        Assert.Equal(2, sites);
    }

    // ── G2: the create stages the association ─────────────────────────────────────────────────────────────────────

    private static readonly Regex StageAssociationCall = new(
        @"\bStageDocumentAssociation\s*\(\s*\w+\s*,\s*request\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool StagesAssociation(string source)
    {
        var code = Code(source);
        return MethodSpan(code, "CreateDocumentAsync") is { } span && StageAssociationCall.IsMatch(code[span.Start..span.End]);
    }

    [Fact(DisplayName = "Task 030b G2: DataverseServiceClientImpl.CreateDocumentAsync stages the association in the create")]
    public void G2_CreateDocumentAsync_StagesTheAssociation() =>
        Assert.True(StagesAssociation(Read(DataverseClientFile)),
            $"{DataverseClientFile}: CreateDocumentAsync no longer calls StageDocumentAssociation(document, request). Every Office "
            + "document would be created filed nowhere, and no Office test notices because they all mock the create (task 030b).");

    [Fact(DisplayName = "Task 030b G2 control: no call, a commented call or a call in another method fires; the call passes")]
    public void G2_Controls()
    {
        Assert.False(StagesAssociation("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                // StageDocumentAssociation(document, request);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """));
        Assert.False(StagesAssociation("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                return (await _serviceClient.CreateAsync(new Entity("sprk_document"), ct)).ToString();
            }
            public void Other(Entity document, CreateDocumentRequest request) { StageDocumentAssociation(document, request); }
            """));
        Assert.True(StagesAssociation("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                StageDocumentAssociation(document, request);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """));
    }

    // ── G3: the save calls the owner class ────────────────────────────────────────────────────────────────────────

    private static readonly Regex OwnerQuestion = new(
        @"\bResolveOwnerAsync\s*\(\s*(?:[\w.]+\.)?OfficeDocumentPersistence\s*\.\s*DocumentOwnershipOf\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DocumentCreateCall = new(
        @"\b_documentService\s*\.\s*CreateDocumentAsync\s*\(\s*(?<request>\w+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static int OwnerQuestions(string source) => OwnerQuestion.Matches(Code(source)).Count;

    /// <summary>Every <c>_documentService.CreateDocumentAsync(x, …)</c> and whether x's initializer sets <c>OwningTeamId</c>.</summary>
    internal static IReadOnlyList<(string Request, bool CarriesOwner)> CreateOwnerSites(string source)
    {
        var code = Code(source);
        var sites = new List<(string, bool)>();
        foreach (Match call in DocumentCreateCall.Matches(code))
        {
            var request = call.Groups["request"].Value;
            var construction = Regex.Matches(code[..call.Index],
                @"\b" + Regex.Escape(request) + @"\s*=\s*new\s+(?:[\w.]+\.)?CreateDocumentRequest\s*(?<open>\{)", RegexOptions.CultureInvariant);
            var carries = construction.Count > 0
                && Regex.IsMatch(Block(code, construction[^1].Groups["open"].Index), @"\bOwningTeamId\s*=(?![=>])");
            sites.Add((request, carries));
        }

        return sites;
    }

    [Fact(DisplayName = "Task 030b G3: the save asks the owner class over the document's association, and every create carries its owner")]
    public void G3_TheSaveCallsTheOwnerClass()
    {
        Assert.True(OwnerQuestions(Read(OfficeServiceFile)) >= 2,
            $"{OfficeServiceFile}: the save must ask ResolveOwnerAsync(OfficeDocumentPersistence.DocumentOwnershipOf(…)) for the "
            + "document and for an editable copy (I-6, record-first over the association the create writes; task 030b).");
        Assert.True(OwnerQuestions(Read(WorkerFile)) >= 1,
            $"{WorkerFile}: a payload with no carried team must ask ResolveOwnerAsync(OfficeDocumentPersistence.DocumentOwnershipOf(…)).");

        var total = 0;
        foreach (var file in OfficeCreatorFiles)
        {
            foreach (var (request, carries) in CreateOwnerSites(Read(file)))
            {
                total++;
                Assert.True(carries,
                    $"{file}: CreateDocumentAsync({request}, …) is built without OwningTeamId. An Office document is owned by the "
                    + "team the owner class decided (I-6), never by the application in the root business unit (task 030b).");
            }
        }

        Assert.True(total >= 3, $"Found {total} Office create sites, expected at least 3: the scan lost a site. Update the guard.");
    }

    [Fact(DisplayName = "Task 030b G3 control: a create without OwningTeamId and an owner question not built by DocumentOwnershipOf fire")]
    public void G3_Controls()
    {
        Assert.Equal([("createRequest", false)], CreateOwnerSites("""
            var createRequest = new CreateDocumentRequest { Name = "x", CreatedByPersonId = person };
            createRequest.OwningTeamId = team;   // set after the initializer: not the shape the guard accepts
            var id = await _documentService.CreateDocumentAsync(createRequest, ct);
            """));
        Assert.Equal([("createRequest", true)], CreateOwnerSites("""
            var createRequest = new Spaarke.Dataverse.CreateDocumentRequest
            {
                Name = "x",
                OwningTeamId = owningTeamId,
            };
            var id = await _documentService.CreateDocumentAsync(createRequest, ct);
            """));
        Assert.Equal(0, OwnerQuestions("var o = await _ownershipResolver.ResolveOwnerAsync(RecordOwnershipContext.ForRow(t, l), ct);"));
        Assert.Equal(1, OwnerQuestions("""
            var o = await _ownershipResolver.ResolveOwnerAsync(
                OfficeDocumentPersistence.DocumentOwnershipOf(type, id, comm, oid) with { RequestedBy = r }, ct);
            """));
        Assert.Equal(1, OwnerQuestions(
            "await ownershipResolver.ResolveOwnerAsync(Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence.DocumentOwnershipOf(a, b, c, d), ct);"));
    }

    // ── G4: only the server writes the narrowing and pointer columns ──────────────────────────────────────────────

    /// <summary>The members of <c>CreateDocumentRequest</c> a request body may set; every other member is server-only.</summary>
    private static readonly string[] BindableCreateMembers = ["Name", "ContainerId", "Description"];

    /// <summary>Member names the Office save body may never carry: the pointer, the narrowing, the owner and a new row's id.</summary>
    private static readonly HashSet<string> ServerOnlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GraphDriveId", "GraphItemId", "DriveId", "ItemId", "FilePath", "GraphItemIdBound", "FilePointer",
        "ParentDocumentLookup",
        "AccessPermission", "AccessInheritance", "AccessInheritanceRecord", "RestrictedBySubmission", "RelatedCommunicationId",
        "OwningTeamId", "OwnerId", "OwningBusinessUnit", "CreatedByPersonId",
        "Id", "DocumentId", "PreAssignedDocumentId",
    };

    /// <summary>The public instance properties of <paramref name="type"/> a JSON body can bind (no <c>[JsonIgnore]</c>).</summary>
    internal static IReadOnlyList<string> BindableMembers(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is { IsPublic: true } && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every bindable member path under <paramref name="root"/> (recursing into non-framework types) with a server-only name.</summary>
    internal static IReadOnlyList<string> ServerOnlyMembersIn(Type root)
    {
        var found = new List<string>();
        var seen = new HashSet<Type>();

        void Walk(Type type, string path)
        {
            if (!seen.Add(type))
                return;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                    continue;

                var memberPath = $"{path}.{property.Name}";
                if (ServerOnlyNames.Contains(property.Name))
                    found.Add(memberPath);

                foreach (var next in Unwrap(property.PropertyType))
                    Walk(next, memberPath);
            }
        }

        Walk(root, root.Name);
        return found;
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Unwrap))
                yield return argument;
            yield break;
        }

        if (type.IsArray)
        {
            foreach (var element in Unwrap(type.GetElementType()!))
                yield return element;
            yield break;
        }

        var ns = type.Namespace ?? string.Empty;
        if (type.IsClass && !ns.StartsWith("System", StringComparison.Ordinal) && !ns.StartsWith("Microsoft", StringComparison.Ordinal))
            yield return type;
    }

    [Fact(DisplayName = "Task 030b G4: only Name, ContainerId and Description of CreateDocumentRequest bind from a body")]
    public void G4_CreateDocumentRequest_ServerOnlyMembersAreJsonIgnored() =>
        Assert.Equal(
            BindableCreateMembers.OrderBy(n => n, StringComparer.Ordinal).ToList(),
            BindableMembers(typeof(Spaarke.Dataverse.CreateDocumentRequest)));

    [Fact(DisplayName = "Task 030b G4: the Office save body carries no pointer, narrowing, owner or new-document id")]
    public void G4_TheOfficeSaveBody_NamesNoServerOnlyColumn()
    {
        var found = ServerOnlyMembersIn(typeof(Sprk.Bff.Api.Models.Office.SaveRequest));

        Assert.True(found.Count == 0,
            "The Office save body (POST /api/office/save) must not carry a member the server writes itself: the SPE pointer, "
            + "the narrowing (Access Permission, its record, the submission marker, the communication link), the owner, or the "
            + "id of the row it creates (task 030b). ExistingDocumentId is the version save's target and is checked as Write. "
            + "Found:\n  " + string.Join("\n  ", found));
    }

    private sealed class LeakyRequest
    {
        public string? Name { get; set; }
        public Guid? OwningTeamId { get; set; }
        [JsonIgnore] public Guid? Id { get; set; }
    }

    private sealed class LeakyBody
    {
        public string? FileName { get; init; }
        public LeakyInner? Document { get; init; }
        public List<LeakyInner>? Parts { get; init; }
    }

    private sealed class LeakyInner
    {
        public string? GraphItemId { get; init; }
        public int? AccessPermission { get; init; }
    }

    private sealed class CleanBody
    {
        public string? FileName { get; init; }
        public Guid? ExistingDocumentId { get; init; }
        public string? ExchangeItemId { get; init; }
        [JsonIgnore] public Guid? Id { get; init; }
    }

    [Fact(DisplayName = "Task 030b G4 controls: a server-only member without [JsonIgnore] fires, nested or in a list; a clean body does not")]
    public void G4_Controls()
    {
        Assert.Equal(["Name", "OwningTeamId"], BindableMembers(typeof(LeakyRequest)));
        Assert.Equal(
            ["LeakyBody.Document.GraphItemId", "LeakyBody.Document.AccessPermission"],
            ServerOnlyMembersIn(typeof(LeakyBody)));
        Assert.Empty(ServerOnlyMembersIn(typeof(CleanBody)));
    }

    // ── G5: a given id is created, never upserted ─────────────────────────────────────────────────────────────────

    private static readonly Regex CreateCall = new(@"\.\s*CreateAsync\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Upsert = new(@"\bUpsert", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True when <c>CreateDocumentAsync</c>'s body creates and never upserts.</summary>
    internal static bool CreatesNeverUpserts(string source)
    {
        var code = Code(source);
        if (MethodSpan(code, "CreateDocumentAsync") is not { } span)
            return false;
        var body = code[span.Start..span.End];
        return CreateCall.IsMatch(body) && !Upsert.IsMatch(body);
    }

    /// <summary>The one file allowed to give a create its primary key (the id stamped into the bytes, FR-02).</summary>
    private const string IdSetterFile = PersistenceFile;

    /// <summary>Sites in <paramref name="source"/> that set a <c>CreateDocumentRequest</c>'s <c>Id</c>.</summary>
    internal static IReadOnlyList<int> CreateIdWrites(string source)
    {
        var code = Code(source);
        var sites = new List<int>();

        foreach (Match construction in Regex.Matches(code,
                     @"\bnew\s+(?:[\w.]+\.)?CreateDocumentRequest\s*(?<open>\{)", RegexOptions.CultureInvariant))
        {
            var block = Block(code, construction.Groups["open"].Index);
            foreach (Match id in Regex.Matches(block, @"(?<![\w.])Id\s*=(?![=>])", RegexOptions.CultureInvariant))
                sites.Add(SourceScan.LineOf(code, construction.Groups["open"].Index + id.Index));
        }

        foreach (Match member in Regex.Matches(code, @"\b(?<var>\w+)\s*\.\s*Id\s*=(?![=>])", RegexOptions.CultureInvariant))
        {
            if (TypeOf(code, member.Groups["var"].Value, member.Index) == "CreateDocumentRequest")
                sites.Add(SourceScan.LineOf(code, member.Index));
        }

        return sites;
    }

    /// <summary>Every right-hand side assigned to <c>preAssignedDocumentId</c> in <paramref name="source"/>.</summary>
    internal static IReadOnlyList<string> PreAssignedIdSources(string source) =>
        Regex.Matches(Code(source), @"\bpreAssignedDocumentId\s*=(?![=>])\s*(?<rhs>[^;]+);", RegexOptions.CultureInvariant)
            .Select(m => Regex.Replace(m.Groups["rhs"].Value, @"\s+", string.Empty))
            .ToList();

    [Fact(DisplayName = "Task 030b G5: a document create with a given id is a Create, never an Upsert")]
    public void G5_TheCreateNeverUpserts() =>
        Assert.True(CreatesNeverUpserts(Read(DataverseClientFile)),
            $"{DataverseClientFile}: CreateDocumentAsync must create with CreateAsync and never upsert. A given id that already "
            + "exists is then refused by Dataverse (duplicate key), never written over (task 030b).");

    [Fact(DisplayName = "Task 030b G5: only the Office save gives a create its id, and that id is minted by the server")]
    public void G5_OnlyTheOfficeSaveSetsAGivenId_AndTheServerMintsIt()
    {
        var setters = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var relative = Path.GetRelativePath(SourceScan.RepoRoot, file).Replace('\\', '/');
            var source = File.ReadAllText(file);
            if (!source.Contains("CreateDocumentRequest", StringComparison.Ordinal))
                continue;
            setters.AddRange(CreateIdWrites(source).Select(line => $"{relative}:{line}"));
        }

        Assert.True(setters.Count > 0 && setters.All(s => s.StartsWith(IdSetterFile + ":", StringComparison.Ordinal)),
            $"CreateDocumentRequest.Id is set only in {IdSetterFile} (the id the save stamped into the bytes, FR-02). Found:\n  "
            + string.Join("\n  ", setters));

        var sources = PreAssignedIdSources(Read(OfficeServiceFile));
        Assert.Contains("Guid.NewGuid()", sources);
        Assert.True(sources.All(s => s is "Guid.NewGuid()" or "null"),
            $"{OfficeServiceFile}: preAssignedDocumentId is minted by the server (Guid.NewGuid()) or null, never taken from the "
            + "request (task 030b: a client-supplied id is not accepted on the create). Found: " + string.Join(", ", sources));
    }

    [Fact(DisplayName = "Task 030b G5 controls: an upsert, an id set elsewhere and a client id fire; a create and a minted id do not")]
    public void G5_Controls()
    {
        Assert.False(CreatesNeverUpserts("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document") { Id = request.Id ?? Guid.Empty };
                var (id, _) = await UpsertAsync(document, ct);
                return id.ToString();
            }
            """));
        Assert.False(CreatesNeverUpserts("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                await _serviceClient.ExecuteAsync(new UpsertRequest { Target = document }, ct);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """));
        Assert.True(CreatesNeverUpserts("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                if (request.Id is { } id) { document.Id = id; }
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """));

        Assert.Equal(2, CreateIdWrites("""
            var create = new CreateDocumentRequest { Name = "x", Id = request.Document.DocumentId, OwningTeamId = t };
            var later = new Spaarke.Dataverse.CreateDocumentRequest { Name = "y" };
            later.Id = Guid.NewGuid();
            """).Count);
        Assert.Empty(CreateIdWrites("""
            var create = new CreateDocumentRequest { Name = "x", ContainerId = c, OwningTeamId = t, CreatedByPersonId = p };
            var document = new Entity("sprk_document");
            document.Id = suppliedId;
            var update = new UpdateDocumentRequest { GraphItemId = i };
            """));

        Assert.Equal(["null", "request.Document.DocumentId"], PreAssignedIdSources("""
            Guid? preAssignedDocumentId = null;
            preAssignedDocumentId = request.Document.DocumentId;
            """));
        Assert.Equal(["null", "Guid.NewGuid()"], PreAssignedIdSources("""
            Guid? preAssignedDocumentId = null;
            preAssignedDocumentId = Guid.NewGuid();
            if (preAssignedDocumentId == null) { }
            """));
    }

    // ── G6: the SPE pointer, and an attachment child's parent link, are in the create ───────────────────────────────

    /// <summary>
    /// Writes of the pointer and the parent link. <c>GraphDriveId</c> / <c>GraphItemId</c> exist only on the update request;
    /// <c>FilePointer</c> only on the create; <c>ParentDocumentLookup</c> on both (task 034's create member and the update's), so
    /// it is judged by the request it is set on. <c>FilePath</c> is not listed: the version save (<c>RecordNewVersionAsync</c>)
    /// refreshes the URL of an existing row, which is not a create.
    /// </summary>
    private static readonly Regex PointerOrParentWrite = new(
        @"(?:\b(?<var>\w+)\s*\.\s*)?\b(?<member>GraphDriveId|GraphItemId|ParentDocumentLookup|FilePointer)\s*=(?![=>])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CreateInitializer = new(
        @"\bnew\s+(?:[\w.]+\.)?CreateDocumentRequest\s*(?<open>\{)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OtherInitializer = new(
        @"\bnew\s+(?:[\w.]+\.)?(?!CreateDocumentRequest\b)\w+\s*(?:\([^)]*\))?\s*(?<open>\{)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>G6 over one file's source: the violations, and how many creates carry a pointer and a parent link.</summary>
    internal static (IReadOnlyList<string> Violations, int PointerSites, int ParentSites) PointerAndParentScan(string source, string file)
    {
        var code = Code(source);

        List<(int Start, int End, bool Create)> Spans(Regex initializer, bool create) =>
            initializer.Matches(code)
                .Select(m => (m.Groups["open"].Index, m.Groups["open"].Index + Block(code, m.Groups["open"].Index).Length, create))
                .ToList();
        var blocks = Spans(CreateInitializer, true).Concat(Spans(OtherInitializer, false)).ToList();

        // A member write is typed by its variable; an initializer write by the innermost initializer it sits in.
        bool OnCreate(Match write) => write.Groups["var"].Success
            ? TypeOf(code, write.Groups["var"].Value, write.Index) == "CreateDocumentRequest"
            : blocks.Where(b => write.Index > b.Start && write.Index < b.End).OrderByDescending(b => b.Start).FirstOrDefault().Create;

        var violations = new List<string>();
        int pointers = 0, parents = 0;
        foreach (Match write in PointerOrParentWrite.Matches(code))
        {
            var onCreate = OnCreate(write);
            switch (write.Groups["member"].Value)
            {
                case "FilePointer" when onCreate:
                    pointers++;
                    break;
                case "ParentDocumentLookup" when onCreate:
                    parents++;
                    break;
                case "FilePointer":
                    break; // not a document create's member: not counted
                default:
                    violations.Add($"{file}:{SourceScan.LineOf(code, write.Index)}: written outside the create: {write.Value.Trim()}");
                    break;
            }
        }

        return (violations, pointers, parents);
    }

    [Fact(DisplayName = "Task 030b G6: every Office create carries its SPE pointer, and an attachment child its parent link, in the create")]
    public void G6_ThePointerAndTheParentLinkAreInTheCreate()
    {
        var violations = new List<string>();
        int pointers = 0, parents = 0;
        foreach (var file in OfficeCreatorFiles)
        {
            var (fileViolations, filePointers, fileParents) = PointerAndParentScan(Read(file), file);
            violations.AddRange(fileViolations);
            pointers += filePointers;
            parents += fileParents;
        }

        Assert.True(violations.Count == 0,
            "An Office document's SPE pointer, and an attachment child's parent link, are part of its create (task 030b): set "
            + "CreateDocumentRequest.FilePointer / ParentDocumentLookup, not the UpdateDocumentRequest members:\n  " + string.Join("\n  ", violations));
        Assert.True(pointers >= 3,
            $"Found {pointers} Office creates carrying FilePointer, expected at least 3 (the save, the fallback, each child).");
        Assert.True(parents >= 1,
            $"Found {parents} Office creates carrying ParentDocumentLookup, expected at least 1: an attachment child carries its parent link in the create.");
    }

    [Fact(DisplayName = "Task 030b G6 control: a pointer or parent link on an update fires; the create's members and the version URL do not")]
    public void G6_Controls()
    {
        var (fired, firedPointers, firedParents) = PointerAndParentScan("""
            var updateRequest = new UpdateDocumentRequest
            {
                GraphDriveId = driveId,
                GraphItemId = fileHandle.Id,
                ParentDocumentLookup = parentDocumentId,
            };
            updateRequest.ParentDocumentLookup = parentDocumentId;
            var other = new OtherRequest { FilePointer = p };
            """, "A.cs");
        Assert.Equal(4, fired.Count);
        Assert.Equal(0, firedPointers);
        Assert.Equal(0, firedParents);

        var (clean, pointers, parents) = PointerAndParentScan("""
            var createRequest = new CreateDocumentRequest
            {
                Name = attachment.FileName,
                Description = kind switch { 1 => "a", _ => null },
                ParentDocumentLookup = parentDocumentId,
                FilePointer = new DocumentFilePointer(driveId, fileHandle.Id, fileHandle.WebUrl),
            };
            var fallback = new Spaarke.Dataverse.CreateDocumentRequest { Name = "x" };
            fallback.FilePointer = new Spaarke.Dataverse.DocumentFilePointer(driveId, itemId, webUrl);
            var update = new UpdateDocumentRequest { FileSize = fileSize, ParentGraphItemId = parentGraphItemId };
            update.FilePath = webUrl;   // the version save refreshes an existing row's URL
            if (request.ParentDocumentLookup == null) { }
            // updateRequest.GraphItemId = itemId;   (a comment)
            """, "B.cs");
        Assert.Empty(clean);
        Assert.Equal(2, pointers);
        Assert.Equal(1, parents);
    }

    private static readonly Regex StageFilePointerCall = new(
        @"\bStageDocumentFilePointer\s*\(\s*\w+\s*,\s*request\s*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static bool StagesFilePointer(string source)
    {
        var code = Code(source);
        return MethodSpan(code, "CreateDocumentAsync") is { } span && StageFilePointerCall.IsMatch(code[span.Start..span.End]);
    }

    /// <remarks>The parent link's stager (<c>StageDocumentParent</c>) is task 034's and is pinned by
    /// <c>InboundIngestionCreatorsGuardTests</c>; this guard pins the pointer's.</remarks>
    [Fact(DisplayName = "Task 030b G6: DataverseServiceClientImpl.CreateDocumentAsync stages the SPE pointer in the create")]
    public void G6_CreateDocumentAsync_StagesThePointer() =>
        Assert.True(StagesFilePointer(Read(DataverseClientFile)),
            $"{DataverseClientFile}: CreateDocumentAsync no longer calls StageDocumentFilePointer(document, request). Every Office "
            + "document would be created without its SPE pointer, and no Office test notices because they all mock the create (task 030b).");

    [Fact(DisplayName = "Task 030b G6 control: a commented call or a call in another method fires; the call passes")]
    public void G6_StageControls()
    {
        Assert.False(StagesFilePointer("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                // StageDocumentFilePointer(document, request);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            public void Other(Entity document, CreateDocumentRequest request) { StageDocumentFilePointer(document, request); }
            """));
        Assert.True(StagesFilePointer("""
            public async Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default)
            {
                var document = new Entity("sprk_document");
                StageDocumentFilePointer(document, request);
                return (await _serviceClient.CreateAsync(document, ct)).ToString();
            }
            """));
    }
}
