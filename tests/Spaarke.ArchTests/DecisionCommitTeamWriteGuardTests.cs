using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-010 part D1 (#3184), uac-r2 Q3 (binding): the child-records route keeps the business-unit and team CONFIGURATION answers
/// for one request (<c>RecordOwnerLookupCache.ForRequest</c>: the Secure Record business unit, the Secure Record owner team,
/// each unit's default owner team). That is sound only because nothing the decision commit runs creates,
/// changes or deletes a <c>team</c> or <c>businessunit</c> row, or moves a user to another unit. This guard fails any such write
/// anywhere in the BFF except the known writers listed in <see cref="KnownWriters"/>, each off the commit path.
/// </summary>
/// <remarks>
/// <para><b>Scope.</b> Every <c>.cs</c> file under <c>src/server/api/Sprk.Bff.Api</c> (no build output), parsed with Roslyn. The
/// whole tree, not the commit path's files: the cache is never suspended (unlike the access window), and the commit reaches the
/// communication, event and reconciler services through steps that scan would leave out (review F3).</para>
/// <para><b>What fails.</b></para>
/// <list type="bullet">
/// <item>A call whose name starts with <c>Create</c>, <c>Update</c>, <c>Delete</c>, <c>Upsert</c>, <c>Post</c>, <c>Patch</c> or
/// <c>Put</c> with a direct argument that names <c>team</c>, <c>teams</c>, <c>businessunit</c> or <c>businessunits</c>: a string,
/// an interpolated string, a <c>const</c> or <c>static readonly</c> string of the same file, or one of another class
/// (<c>SecureRecordOwnerTeam.TeamEntity</c>), as a table, an entity set or a Web API path with that segment
/// (<c>teams(…)</c>, <c>/api/data/v9.2/businessunits(…)</c>).</item>
/// <item>A <c>Delete…</c> call whose arguments hold <c>new EntityReference("team" | "businessunit", …)</c> (or its
/// <c>LogicalName =</c> initializer).</item>
/// <item><c>new Entity("team" | "businessunit", …)</c>, a target-typed <c>Entity t = new("team")</c>, or <c>new Entity { LogicalName = "team" }</c> anywhere (such a row is built
/// only to write it), and a <c>CreateRequest</c>, <c>UpdateRequest</c>, <c>UpsertRequest</c> or <c>DeleteRequest</c> whose
/// <c>Target</c> is either table.</item>
/// <item>A <c>new OrganizationRequest("Create" | "Update" | "Upsert" | "Delete")</c> (or <c>RequestName =</c>) in a method that sets
/// a <c>"Target"</c> parameter to a team or business unit.</item>
/// <item>A <c>new HttpRequestMessage(HttpMethod.Post | Patch | Put | Delete, path)</c>, or ANY call (whatever its name) with such an
/// <c>HttpMethod</c> (or <c>new HttpMethod("PATCH" | "MERGE")</c>) and a path argument, where the path addresses <c>teams(…)</c> or
/// <c>businessunits(…)</c>.</item>
/// <item><c>SetBusinessSystemUser</c> in an identifier or a string.</item>
/// <item>A <c>systemuser</c> business-unit write: the text <c>businessunitid@odata.bind</c>, an assignment to
/// <c>x["businessunitid"]</c>, a dictionary initializer <c>["businessunitid"] = …</c> or <c>{ "businessunitid", … }</c>.</item>
/// </list>
/// <para><b>What does not.</b> Reads (<c>RetrieveMultipleAsync</c> over a <c>team</c> query, <c>GetAsync("teams(…)")</c>, a GET
/// request), an owner assignment of a CHILD row (<c>["ownerid"] = new EntityReference("team", t)</c>,
/// <c>"ownerid@odata.bind": "/teams(…)"</c>), the <c>businessunitid</c> column read or filtered on, and comments or XML docs
/// (trivia). The controls below pin both sides.</para>
/// <para><b>Known writers.</b> <see cref="KnownWriters"/> lists the files that write a team or business unit today, each with
/// why it is not on the decision commit's path. A listed file that no longer writes fails (a stale entry), so the list only
/// shrinks by an edit. A new writer reachable from the commit is not added here: it breaks the per-request cache. That no
/// commit-path folder (Signals, RecordCreation, Access, Communication) names a known writer in code is checked too
/// (<see cref="CommitPathFolders_NeverNameAKnownWriter"/>).</para>
/// <para><b>Limits.</b> A name match: a write through an <c>Entity</c> variable built elsewhere and passed by name
/// (<c>UpdateAsync(entity)</c>), through a table name computed at run time, or through a constant built from other constants, is
/// not seen. Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class DecisionCommitTeamWriteGuardTests
{
    private static readonly string BffRoot = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    /// <summary>
    /// The files that write a team, a business unit or a user's unit today, each off the decision commit's path (the commit runs
    /// <c>DecisionCommitService</c>, its executors, <c>RecordCreationService</c>, the owner resolver, the share synchronizer, the
    /// reconciler's child update, the communication and event steps; none of these calls into a file below).
    /// </summary>
    private static readonly Dictionary<string, string> KnownWriters = new(StringComparer.Ordinal)
    {
        ["src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs"] =
            "demo and customer registration (RegistrationEndpoints, DemoProvisioningService, DemoExpirationJob): creates a systemuser "
            + "bound to its business unit and adds or removes team members; no decision-commit step calls it",
        ["src/server/api/Sprk.Bff.Api/Services/ExternalAccess/FlaggedUserPlacementJob.cs"] =
            "an IScheduledJob that moves a flagged external user into the External Collaborators unit; it runs on its schedule, "
            + "never inside a request",
    };

    private static readonly HashSet<string> Tables = new(StringComparer.OrdinalIgnoreCase) { "team", "teams", "businessunit", "businessunits" };

    private static readonly HashSet<string> EntityTables = new(StringComparer.OrdinalIgnoreCase) { "team", "businessunit" };

    private static readonly string[] WriteVerbs = ["Create", "Update", "Delete", "Upsert", "Post", "Patch", "Put"];

    private static readonly HashSet<string> WriteRequests = new(StringComparer.Ordinal) { "CreateRequest", "UpdateRequest", "UpsertRequest", "DeleteRequest" };

    private static readonly HashSet<string> WriteMessages = new(StringComparer.OrdinalIgnoreCase) { "Create", "Update", "Upsert", "Delete" };

    private static readonly HashSet<string> WriteHttpMethods = new(StringComparer.OrdinalIgnoreCase) { "Post", "Patch", "Put", "Delete", "Merge" };

    private const string BusinessUnitColumn = "businessunitid";

    internal static IEnumerable<string> BffSourceFiles() =>
        Directory.EnumerateFiles(BffRoot, "*.cs", SearchOption.AllDirectories).Where(f =>
            !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact(DisplayName = "ISS-010 D1 Q3: nothing in the BFF writes a team, a business unit or a user's unit, except the known writers")]
    public void Bff_WritesNoTeamOrBusinessUnit_OutsideTheKnownWriters()
    {
        var files = BffSourceFiles().Select(f => (Path: SourceScan.Relative(f).Replace('\\', '/'), Source: File.ReadAllText(f))).ToList();
        Assert.Contains(files, f => f.Path.EndsWith("Services/Signals/DecisionCommitService.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Path.EndsWith("Services/Access/SecureChildReconciler.cs", StringComparison.Ordinal));
        Assert.Contains(files, f => f.Path.Contains("/Services/Communication/", StringComparison.Ordinal));

        var qualified = QualifiedConstants(files.Select(f => f.Source));
        var problems = new List<string>();
        var writers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, source) in files)
        {
            var found = Scan(path, source, qualified);
            if (found.Count == 0)
                continue;
            writers.Add(path);
            if (!KnownWriters.ContainsKey(path))
                problems.AddRange(found);
        }

        foreach (var stale in KnownWriters.Keys.Where(k => !writers.Contains(k)))
            problems.Add($"{stale}: listed as a known team or business-unit writer, but writes none. Remove its entry (stale).");

        Assert.True(problems.Count == 0,
            "The child-records route keeps business-unit and team answers for one request (ISS-010 D1), so no code the decision "
            + "commit can reach may write a team or business unit row or a user's business unit (uac-r2 Q3). A new writer off the "
            + "commit path is listed in KnownWriters with its reason; one on it breaks the cache:\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "ISS-010 D1 Q3 guard (must fire): each team, business-unit and user-unit write shape")]
    public void Scan_MustFire()
    {
        string[] shapes =
        [
            "await _dataverse.CreateAsync(new Entity(\"team\") { [\"name\"] = \"x\" }, ct);",
            "var e = new Entity(\"businessunit\", id);",
            "var e = new Entity { LogicalName = \"team\", Id = id };",
            "var e = new Microsoft.Xrm.Sdk.Entity() { LogicalName = \"businessunit\" };",
            "await _dataverse.UpdateAsync(\"team\", id, fields, ct);",
            "await _dataverse.UpdateAsync(\"businessunit\", id, null!, ct);",
            "await _dataverse.UpsertAsync(\"teams\", fields, ct);",
            "await _dataverse.DeleteAsync(\"businessunit\", id, ct);",
            "await _org.DeleteAsync(new EntityReference(\"team\", id), ct);",
            "await _org.DeleteAsync(new EntityReference { LogicalName = \"team\", Id = id }, ct);",
            "await _webApi.PostAsync(\"teams\", body, ct);",
            "await _webApi.PatchAsync($\"teams({id})\", body, ct);",
            "await _webApi.DeleteAsync($\"/businessunits({id:D})\", ct);",
            "await _webApi.PutAsync($\"teams({id})/name\", body, ct);",
            "await _webApi.PatchAsync($\"{_base}/api/data/v9.2/teams({id})\", body, ct);",
            "await _org.ExecuteAsync(new DeleteRequest { Target = new EntityReference(\"team\", id) }, ct);",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new Entity(\"businessunit\", id) }, ct);",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new Entity { LogicalName = \"team\", Id = id } }, ct);",
            "await _org.ExecuteAsync(new OrganizationRequest(\"Update\") { [\"Target\"] = new EntityReference(\"team\", id) }, ct);",
            "var r = new OrganizationRequest(\"Delete\"); r[\"Target\"] = new EntityReference(\"businessunit\", id); await _org.ExecuteAsync(r, ct);",
            "var r = new OrganizationRequest { RequestName = \"Delete\" }; r.Parameters[\"Target\"] = new EntityReference(\"team\", id);",
            "var r = new OrganizationRequest(\"Upsert\") { Parameters = { { \"Target\", new EntityReference(\"team\", id) } } };",
            "using var m = new HttpRequestMessage(HttpMethod.Patch, $\"teams({id})\");",
            "using var m = new HttpRequestMessage(HttpMethod.Delete, $\"{_base}/businessunits({id})\");",
            "using var m = new HttpRequestMessage(new HttpMethod(\"PATCH\"), $\"teams({id})\");",
            "await _client.SendAsync(HttpMethod.Post, \"teams\", body, ct);",
            // review round 2: any call name with a write HttpMethod, MERGE, and target-typed new(…).
            "await WriteAsync(HttpMethod.Patch, $\"teams({id})\", body, (\"If-Match\", \"*\"), ct);",
            "await _store.RunAsync(HttpMethod.Put, $\"{_base}/businessunits({id})/name\", body, ct);",
            "await _store.RunAsync(new HttpMethod(\"MERGE\"), $\"businessunits({id})\", body, ct);",
            "Entity t = new(\"team\");",
            "Microsoft.Xrm.Sdk.Entity? t = new(\"businessunit\") { Id = id };",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new(\"team\", id) }, ct);",
            "await _org.ExecuteAsync(new DeleteRequest { Target = new(\"businessunit\", id) }, ct);",
            "OrganizationRequest r = new(\"Delete\") { [\"Target\"] = new EntityReference(\"team\", id) };",
            "await _web.ExecuteAsync(HttpMethod.Delete, $\"businessunits({id})\", ct);",
            "await _org.ExecuteAsync(new SetBusinessSystemUserRequest { BusinessId = bu, UserId = u }, ct);",
            "var name = \"SetBusinessSystemUser\";",
            "await _dataverse.UpdateAsync(\"systemuser\", u, new Dictionary<string, object> { [\"businessunitid\"] = new EntityReference(\"businessunit\", b) }, ct);",
            "var fields = new Dictionary<string, object> { { \"businessunitid\", unit } };",
            "row[\"businessunitid\"] = new EntityReference(\"businessunit\", b);",
            "var body = new Dictionary<string, object> { [\"businessunitid@odata.bind\"] = $\"/businessunits({b})\" };",
            "await _webApi.PatchAsync($\"systemusers({u})\", \"{\\\"businessunitid@odata.bind\\\":\\\"/businessunits(1)\\\"}\", ct);",
        ];
        foreach (var shape in shapes)
            Assert.True(Scan("fixture.cs", Method(shape)).Count > 0, $"not seen: {shape}");

        // A same-file constant or static readonly string that holds the table name counts as the name.
        Assert.NotEmpty(Scan("fixture.cs", "class S { private const string TeamEntity = \"team\"; async Task M() { await _d.UpdateAsync(TeamEntity, id, f, ct); } }"));
        Assert.NotEmpty(Scan("fixture.cs", "class S { private const string Units = \"businessunits\"; async Task M() { await _w.PostAsync(Units, b, ct); } }"));
        Assert.NotEmpty(Scan("fixture.cs", "class S { private static readonly string Units = \"teams\"; async Task M() { await _w.PostAsync(Units, b, ct); } }"));
        Assert.NotEmpty(Scan("fixture.cs", "class S { private const string T = \"team\"; void M() { var e = new Entity(T); } }"));

        // review F3: a constant of ANOTHER class, resolved across the scanned files.
        var other = "static class SecureRecordOwnerTeam { internal const string TeamEntity = \"team\"; internal const string TeamEntitySet = \"teams\"; }";
        var qualified = QualifiedConstants([other]);
        Assert.NotEmpty(Scan("fixture.cs", Method("await _d.UpdateAsync(SecureRecordOwnerTeam.TeamEntity, id, f, ct);"), qualified));
        Assert.NotEmpty(Scan("fixture.cs", Method("await _w.PatchAsync($\"{SecureRecordOwnerTeam.TeamEntitySet}({id})\", body, ct);"), qualified));
        Assert.NotEmpty(Scan("fixture.cs", Method("await _w.DeleteAsync(Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.TeamEntitySet, ct);"), qualified));
        Assert.NotEmpty(Scan("fixture.cs", Method("var e = new Entity(SecureRecordOwnerTeam.TeamEntity, id);"), qualified));
        Assert.Empty(Scan("fixture.cs", Method("await _d.UpdateAsync(SecureRecordOwnerTeam.TeamEntity, id, f, ct);")));
    }

    [Fact(DisplayName = "ISS-010 D1 Q3 guard (must not fire): reads, a child's owner assignment, the column read, comments")]
    public void Scan_MustNotFire()
    {
        var qualified = QualifiedConstants(["static class SecureRecordOwnerTeam { internal const string TeamEntitySet = \"teams\"; internal const string Todo = \"sprk_todo\"; }"]);
        string[] shapes =
        [
            "var teams = await _dataverse.RetrieveMultipleAsync(new QueryExpression(\"team\") { ColumnSet = new ColumnSet(\"teamid\") }, ct);",
            "var q = new QueryExpression(\"businessunit\"); q.Criteria.AddCondition(\"businessunitid\", ConditionOperator.Equal, id);",
            "var r = await _webApi.GetAsync($\"teams({id})?$select=name\", ct);",
            "var r = await _webApi.GetAsync($\"{SecureRecordOwnerTeam.TeamEntitySet}({id})\", ct);",
            "await _dataverse.UpdateAsync(SecureRecordOwnerTeam.Todo, id, f, ct);",
            "await _dataverse.UpdateAsync(\"sprk_todo\", id, new Dictionary<string, object> { [\"ownerid\"] = new EntityReference(\"team\", t) }, ct);",
            "await _app.UpdateRecordFieldsAsync(\"sprk_todo\", id, new Dictionary<string, object?> { [\"ownerid@odata.bind\"] = $\"/teams({t})\" }, ct);",
            "await _org.CreateAsync(new Entity(\"sprk_decisionrecord\") { [\"ownerid\"] = new EntityReference(\"team\", t) }, ct);",
            "await _org.CreateAsync(new Entity { LogicalName = \"sprk_todo\", [\"ownerid\"] = new EntityReference(\"team\", t) }, ct);",
            "await _org.ExecuteAsync(new AssignRequest { Target = new EntityReference(\"sprk_todo\", id), Assignee = new EntityReference(\"team\", t) }, ct);",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new Entity(\"sprk_todo\", id) { [\"ownerid\"] = new EntityReference(\"team\", t) } }, ct);",
            "await _org.ExecuteAsync(new RetrieveRequest { Target = new EntityReference(\"team\", t) }, ct);",
            "await _org.ExecuteAsync(new OrganizationRequest(\"Retrieve\") { [\"Target\"] = new EntityReference(\"team\", t) }, ct);",
            "await _org.ExecuteAsync(new OrganizationRequest(\"Update\") { [\"Target\"] = new Entity(\"sprk_todo\", id) { [\"ownerid\"] = new EntityReference(\"team\", t) } }, ct);",
            "await _org.ExecuteAsync(new OrganizationRequest(\"RetrievePrincipalAccess\") { [\"Target\"] = new EntityReference(\"sprk_todo\", id), [\"Principal\"] = new EntityReference(\"team\", t) }, ct);",
            "using var m = new HttpRequestMessage(HttpMethod.Get, $\"teams({id})\");",
            "using var m = new HttpRequestMessage(HttpMethod.Patch, $\"sprk_todos({id})\");",
            "await _client.SendAsync(HttpMethod.Get, $\"businessunits({id})\", ct);",
            "await _client.SendAsync(HttpMethod.Post, \"sprk_todos\", body, ct);",
            // review round 2: ContactIdentityStore.WriteHomeLinkAsync PATCHes a systemuser's home-link columns, never its unit.
            "await WriteAsync(HttpMethod.Patch, $\"systemusers({u:D})\", HomeLinkPayload(t, b), (\"If-Match\", \"v\"), ct);",
            "await WriteAsync(HttpMethod.Get, $\"teams({id})\", ct);",
            "Entity t = new(\"sprk_todo\") { [\"ownerid\"] = new EntityReference(\"team\", u) };",
            "EntityReference owner = new(\"team\", t);",
            "await _org.ExecuteAsync(new RetrieveRequest { Target = new(\"team\", id) }, ct);",
            "await _org.ExecuteAsync(new UpdateRequest { Target = new(\"sprk_todo\", id) }, ct);",
            "var bu = row.GetAttributeValue<EntityReference>(\"businessunitid\")?.Id;",
            "var columns = new ColumnSet(\"businessunitid\", \"sprk_isexternal\");",
            "await _dataverse.DeleteAsync(\"sprk_todo\", id, ct);",
            "await _org.DeleteAsync(new EntityReference(\"sprk_todo\", id), ct);",
            "var path = $\"teams({id})\"; var answer = await _webApi.GetAsync(path, ct);",
            "await _webApi.PatchAsync($\"sprk_todos({id})\", \"{\\\"ownerid@odata.bind\\\":\\\"/teams(1)\\\"}\", ct);",
        ];
        foreach (var shape in shapes)
        {
            var found = Scan("fixture.cs", Method(shape), qualified);
            Assert.True(found.Count == 0, $"seen, but is no team or unit write: {shape}: {string.Join("; ", found)}");
        }

        const string comments = """
            /// <summary>Never <c>UpdateAsync("team", id)</c>; never <c>SetBusinessSystemUser</c>; no businessunitid@odata.bind.</summary>
            class S
            {
                // await _dataverse.UpdateAsync("team", id, f, ct); new Entity("businessunit")
                /* row["businessunitid"] = x; DeleteAsync($"teams({id})") */
                void M() { }
            }
            """;
        Assert.Empty(Scan("fixture.cs", comments));
    }

    [Fact(DisplayName = "ISS-010 D1 Q3 guard control: every known writer is a writer and is never a commit-path entry point")]
    public void KnownWriters_AreRealFiles_WithAReason()
    {
        foreach (var (path, reason) in KnownWriters)
        {
            Assert.True(File.Exists(Path.Combine(SourceScan.RepoRoot, path.Replace('/', Path.DirectorySeparatorChar))), $"{path}: no such file");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{path}: give the reason it is off the decision commit's path");
            Assert.DoesNotContain("/Services/Signals/", path, StringComparison.Ordinal);
            Assert.DoesNotContain("/Services/RecordCreation/", path, StringComparison.Ordinal);
        }
    }

    /// <summary>The folders the decision commit runs in (its executors, the create service, the owner and share code, the
    /// communication steps). None of them may name a known writer in code, so "off the commit path" is checked, not prose.</summary>
    private static readonly string[] CommitPathFolders =
        ["Services/Signals/", "Services/RecordCreation/", "Services/Access/", "Services/Communication/"];

    [Fact(DisplayName = "ISS-010 D1 Q3 review round 2 (F4): no commit-path folder names a known writer in code")]
    public void CommitPathFolders_NeverNameAKnownWriter()
    {
        var writers = KnownWriterTypeNames();
        Assert.Contains("RegistrationDataverseService", writers);
        Assert.Contains("FlaggedUserPlacementJob", writers);

        var files = BffSourceFiles().Select(f => (Path: SourceScan.Relative(f).Replace('\\', '/'), File: f))
            .Where(f => CommitPathFolders.Any(folder => f.Path.Contains("/" + folder, StringComparison.Ordinal)))
            .ToList();
        foreach (var folder in CommitPathFolders)
            Assert.Contains(files, f => f.Path.Contains("/" + folder, StringComparison.Ordinal)); // the scan has its signal

        var problems = files
            .SelectMany(f => WriterReferences(File.ReadAllText(f.File), writers).Select(r => $"{f.Path}: names {r}"))
            .ToList();
        Assert.True(problems.Count == 0,
            "A known team or business-unit writer is allowlisted because the decision commit never reaches it (ISS-010 D1). A "
            + "commit-path file that names one breaks that, and breaks the per-request cache:\n  " + string.Join("\n  ", problems));
    }

    [Fact(DisplayName = "ISS-010 D1 Q3 review round 2 (F4) control: a writer named in code is seen; in a comment, a doc or a string it is not")]
    public void WriterReferences_Controls()
    {
        string[] writers = ["RegistrationDataverseService", "FlaggedUserPlacementJob"];
        Assert.NotEmpty(WriterReferences("class C { private readonly IRegistrationDataverseService _r; }", writers));
        Assert.NotEmpty(WriterReferences("class C { void M(IServiceProvider s) => s.GetRequiredService<RegistrationDataverseService>(); }", writers));
        Assert.NotEmpty(WriterReferences("class C { void M() { var j = new Sprk.Bff.Api.Services.ExternalAccess.FlaggedUserPlacementJob(null!); } }", writers));
        Assert.NotEmpty(WriterReferences("class C { const int Mode = FlaggedUserPlacementJob.AdministrativeAccessMode; }", writers));

        Assert.Empty(WriterReferences("""
            /// <summary>The same value as <c>FlaggedUserPlacementJob.AdministrativeAccessMode</c>; see
            /// <see cref="RegistrationDataverseService"/>.</summary>
            class C
            {
                // RegistrationDataverseService adds team members; FlaggedUserPlacementJob moves users.
                /* new FlaggedUserPlacementJob() */
                string Why => "not RegistrationDataverseService";
                void RegistrationDataverseServiceLike() { }
            }
            """, writers));
    }

    /// <summary>The known writers' type names: each allowlisted file's name (and its <c>I…</c> interface).</summary>
    private static IReadOnlyList<string> KnownWriterTypeNames() =>
        KnownWriters.Keys.Select(Path.GetFileNameWithoutExtension).Select(n => n!).ToList();

    /// <summary>The identifiers in <paramref name="source"/>'s code (Roslyn tokens: comments, XML docs and strings are not
    /// identifiers) that name one of <paramref name="writers"/> or its interface.</summary>
    private static IReadOnlyList<string> WriterReferences(string source, IReadOnlyList<string> writers) =>
        CSharpSyntaxTree.ParseText(source).GetRoot().DescendantTokens()
            .Where(t => t.IsKind(SyntaxKind.IdentifierToken))
            .Select(t => t.ValueText)
            .Where(id => writers.Any(w => id == w || id == "I" + w))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Every <c>const</c> or <c>static readonly</c> string field with a literal value, by <c>Type.Field</c>, across
    /// <paramref name="sources"/> (so <c>SecureRecordOwnerTeam.TeamEntity</c> resolves in another file). A name two types
    /// share with different values is dropped (ambiguous).</summary>
    internal static IReadOnlyDictionary<string, string> QualifiedConstants(IEnumerable<string> sources)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var root = CSharpSyntaxTree.ParseText(source).GetRoot();
            foreach (var (declarator, value) in LiteralFields(root))
            {
                if (declarator.FirstAncestorOrSelf<BaseTypeDeclarationSyntax>() is not { } type)
                    continue;
                var key = $"{type.Identifier.ValueText}.{declarator.Identifier.ValueText}";
                all[key] = all.TryGetValue(key, out var known) && known != value ? null : value;
            }
        }

        return all.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);
    }

    /// <summary>The problems in <paramref name="source"/> (Roslyn: comments and XML docs are trivia, never seen).
    /// <paramref name="qualified"/> resolves another class's constants (<see cref="QualifiedConstants"/>).</summary>
    internal static IReadOnlyList<string> Scan(string file, string source, IReadOnlyDictionary<string, string>? qualified = null)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var constants = new Constants(
            LiteralFields(root).GroupBy(f => f.Declarator.Identifier.ValueText, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal),
            qualified ?? new Dictionary<string, string>(StringComparer.Ordinal));
        var problems = new List<string>();
        string At(SyntaxNode node) => $"{file}:{node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}";

        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = NameOf(call);
            if (name is null)
                continue;

            if (WriteVerbs.Any(v => name.StartsWith(v, StringComparison.Ordinal)))
            {
                foreach (var argument in call.ArgumentList.Arguments)
                {
                    if (TextOf(argument.Expression, constants) is { } text && NamesTeamOrUnit(text))
                        problems.Add($"{At(call)} {name}(…) names \"{text}\"");
                }

                if (name.StartsWith("Delete", StringComparison.Ordinal) && EntityReferenceToTeamOrUnit(call.ArgumentList, constants) is { } target)
                    problems.Add($"{At(call)} {name}(…) deletes a {target}");
            }

            // review F3 (rounds 1 and 2): ANY call with a write HttpMethod and a team or unit path, whatever its name
            // (SendAsync, ExecuteAsync, a store's own WriteAsync(HttpMethod.Patch, "teams(…)", …)).
            if (call.ArgumentList.Arguments.Any(a => IsWriteHttpMethod(a.Expression, constants))
                && call.ArgumentList.Arguments.Select(a => TextOf(a.Expression, constants)).FirstOrDefault(t => t is not null && NamesTeamOrUnit(t)) is { } path)
                problems.Add($"{At(call)} {name}(…) writes \"{path}\"");
        }

        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var type = TypeNameOf(creation);
            if (type == "Entity" && CreatedTable(creation, constants) is { } table && EntityTables.Contains(table))
                problems.Add($"{At(creation)} new Entity(\"{table}\") (a {table} row is built only to write it)");

            if (type is not null && WriteRequests.Contains(type) && RequestTargetTable(creation, constants) is { } named)
                problems.Add($"{At(creation)} new {type} whose Target is a {named}");

            // review F3: new HttpRequestMessage(HttpMethod.Patch, "teams(…)").
            if (type == "HttpRequestMessage" && creation.ArgumentList?.Arguments is { Count: >= 2 } message
                && IsWriteHttpMethod(message[0].Expression, constants)
                && TextOf(message[1].Expression, constants) is { } uri && NamesTeamOrUnit(uri))
                problems.Add($"{At(creation)} new HttpRequestMessage(…) writes \"{uri}\"");
        }

        // review F3: new OrganizationRequest("Update" | …) in a method that sets "Target" to a team or business unit.
        foreach (var body in root.DescendantNodes().Where(n => n is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax
                     or AnonymousFunctionExpressionSyntax or GlobalStatementSyntax or AccessorDeclarationSyntax))
        {
            if (body.Ancestors().Any(a => a is BaseMethodDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax
                    or AccessorDeclarationSyntax))
                continue; // the outermost body decides
            var writes = body.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()
                .Where(c => TypeNameOf(c) == "OrganizationRequest" && WriteMessages.Contains(MessageNameOf(c, constants) ?? ""))
                .ToList();
            if (writes.Count == 0)
                continue;
            foreach (var target in TargetParameters(body, constants))
                problems.Add($"{At(writes[0])} new OrganizationRequest(\"{MessageNameOf(writes[0], constants)}\") whose Target is a {target}");
        }

        foreach (var token in root.DescendantTokens())
        {
            var text = token.Kind() switch
            {
                SyntaxKind.IdentifierToken => token.ValueText,
                SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.SingleLineRawStringLiteralToken
                    or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8StringLiteralToken => token.Text,
                _ => null,
            };
            if (text is null)
                continue;
            if (text.Contains("SetBusinessSystemUser", StringComparison.Ordinal))
                problems.Add($"{At(token.Parent!)} names SetBusinessSystemUser");
            if (text.Contains(BusinessUnitColumn + "@odata.bind", StringComparison.OrdinalIgnoreCase))
                problems.Add($"{At(token.Parent!)} binds a user's businessunitid");
        }

        // x["businessunitid"] = …, and the initializers ["businessunitid"] = … and { "businessunitid", … }.
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (IndexOf(assignment.Left) is { } index && string.Equals(TextOf(index, constants), BusinessUnitColumn, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{At(assignment)} writes businessunitid");
        }

        foreach (var pair in root.DescendantNodes().OfType<InitializerExpressionSyntax>().Where(i => i.IsKind(SyntaxKind.ComplexElementInitializerExpression)))
        {
            if (pair.Expressions.FirstOrDefault() is { } key && string.Equals(TextOf(key, constants), BusinessUnitColumn, StringComparison.OrdinalIgnoreCase))
                problems.Add($"{At(pair)} writes businessunitid");
        }

        return problems;
    }

    /// <summary>The same file's constants by simple name, and every scanned file's by <c>Type.Field</c>.</summary>
    private sealed record Constants(IReadOnlyDictionary<string, string> Local, IReadOnlyDictionary<string, string> Qualified);

    private static IEnumerable<(VariableDeclaratorSyntax Declarator, string Value)> LiteralFields(SyntaxNode root) =>
        root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Parent?.Parent is FieldDeclarationSyntax field
                        && (field.Modifiers.Any(SyntaxKind.ConstKeyword)
                            || (field.Modifiers.Any(SyntaxKind.StaticKeyword) && field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)))
                        && v.Initializer?.Value is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression })
            .Select(v => (v, ((LiteralExpressionSyntax)v.Initializer!.Value).Token.ValueText));

    /// <summary>A table or entity-set name, or a Web API path with a segment that addresses one of the two tables.</summary>
    private static bool NamesTeamOrUnit(string text)
    {
        var path = text.Trim();
        if (path.Contains('"', StringComparison.Ordinal) || path.Contains(' ', StringComparison.Ordinal))
            return false; // a JSON body or a sentence, not a table or a path (a body's businessunitid bind is its own rule)
        var query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
            path = path[..query];
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var open = segment.IndexOf('(', StringComparison.Ordinal);
            if (Tables.Contains(open > 0 ? segment[..open] : segment))
                return true;
        }

        return false;
    }

    /// <summary><c>HttpMethod.Post | Patch | Put | Delete</c> or <c>new HttpMethod("PATCH")</c>.</summary>
    private static bool IsWriteHttpMethod(ExpressionSyntax expression, Constants constants) => expression switch
    {
        MemberAccessExpressionSyntax { Expression: var owner, Name.Identifier.ValueText: var method }
            when LastIdentifier(owner) == "HttpMethod" => WriteHttpMethods.Contains(method),
        BaseObjectCreationExpressionSyntax creation when TypeNameOf(creation) == "HttpMethod"
            && creation.ArgumentList?.Arguments.FirstOrDefault() is { } a && TextOf(a.Expression, constants) is { } verb
            => WriteHttpMethods.Contains(verb),
        _ => false,
    };

    /// <summary>The table a <c>new Entity(…)</c> or <c>new EntityReference(…)</c> names: its first argument, or its
    /// <c>LogicalName =</c> initializer.</summary>
    private static string? CreatedTable(BaseObjectCreationExpressionSyntax creation, Constants constants) =>
        creation.ArgumentList?.Arguments.FirstOrDefault() is { } first
            ? TextOf(first.Expression, constants)
            : InitializerValue(creation, "LogicalName", constants);

    private static string? InitializerValue(BaseObjectCreationExpressionSyntax creation, string property, Constants constants) =>
        creation.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left is IdentifierNameSyntax id && id.Identifier.ValueText == property)
            .Select(a => TextOf(a.Right, constants))
            .FirstOrDefault(t => t is not null);

    /// <summary>An <c>OrganizationRequest</c>'s message: its first argument or its <c>RequestName =</c> initializer.</summary>
    private static string? MessageNameOf(BaseObjectCreationExpressionSyntax request, Constants constants) =>
        request.ArgumentList?.Arguments.FirstOrDefault() is { } first
            ? TextOf(first.Expression, constants)
            : InitializerValue(request, "RequestName", constants);

    /// <summary>The team or business-unit tables a <c>"Target"</c> parameter is set to in <paramref name="body"/>:
    /// <c>x["Target"] = …</c>, <c>x.Parameters["Target"] = …</c>, <c>{ ["Target"] = … }</c> and <c>{ { "Target", … } }</c>.</summary>
    private static IEnumerable<string> TargetParameters(SyntaxNode body, Constants constants)
    {
        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (IndexOf(assignment.Left) is { } index && TextOf(index, constants) == "Target"
                && TeamOrUnitCreation(assignment.Right, constants) is { } table)
                yield return table;
        }

        foreach (var pair in body.DescendantNodes().OfType<InitializerExpressionSyntax>().Where(i => i.IsKind(SyntaxKind.ComplexElementInitializerExpression)))
        {
            if (pair.Expressions is { Count: 2 } kv && TextOf(kv[0], constants) == "Target" && TeamOrUnitCreation(kv[1], constants) is { } table)
                yield return table;
        }
    }

    private static string? TeamOrUnitCreation(ExpressionSyntax expression, Constants constants) =>
        expression is BaseObjectCreationExpressionSyntax c && TypeNameOf(c) is "Entity" or "EntityReference"
                                                           && CreatedTable(c, constants) is { } t && EntityTables.Contains(t)
            ? t
            : null;

    private static ExpressionSyntax? IndexOf(ExpressionSyntax left) => left switch
    {
        ElementAccessExpressionSyntax e => e.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        ImplicitElementAccessSyntax i => i.ArgumentList.Arguments.FirstOrDefault()?.Expression,
        _ => null,
    };

    /// <summary>The table a write request's <c>Target = new Entity(…)</c> or <c>new EntityReference(…)</c> names, when it is a team or
    /// business unit (a child row's owner team set inside the target is not the target).</summary>
    private static string? RequestTargetTable(BaseObjectCreationExpressionSyntax request, Constants constants) =>
        request.Initializer?.Expressions.OfType<AssignmentExpressionSyntax>()
            .Where(a => a.Left is IdentifierNameSyntax { Identifier.ValueText: "Target" })
            .Select(a => TeamOrUnitCreation(a.Right, constants))
            .FirstOrDefault(t => t is not null);

    /// <summary>The table a <c>new EntityReference("team" | "businessunit", …)</c> among <paramref name="arguments"/> names, if any.</summary>
    private static string? EntityReferenceToTeamOrUnit(ArgumentListSyntax arguments, Constants constants) =>
        arguments.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>()
            .Where(c => TypeNameOf(c) == "EntityReference")
            .Select(c => CreatedTable(c, constants))
            .FirstOrDefault(t => t is not null && EntityTables.Contains(t));

    /// <summary>The text of a string literal, the literal parts of an interpolated string (a constant hole is resolved), a
    /// same-file constant, or another class's constant (<c>Type.Field</c>, the last two names of a qualified access).</summary>
    private static string? TextOf(ExpressionSyntax expression, Constants constants) => expression switch
    {
        LiteralExpressionSyntax { RawKind: (int)SyntaxKind.StringLiteralExpression } literal => literal.Token.ValueText,
        InterpolatedStringExpressionSyntax interpolated => string.Concat(interpolated.Contents.Select(c => c switch
        {
            InterpolatedStringTextSyntax t => t.TextToken.ValueText,
            InterpolationSyntax hole when TextOf(hole.Expression, constants) is { } value => value,
            _ => "{}",
        })),
        IdentifierNameSyntax id when constants.Local.TryGetValue(id.Identifier.ValueText, out var value) => value,
        MemberAccessExpressionSyntax { Expression: var owner, Name: IdentifierNameSyntax member }
            when LastIdentifier(owner) is { } type && constants.Qualified.TryGetValue($"{type}.{member.Identifier.ValueText}", out var value) => value,
        _ => null,
    };

    private static string? LastIdentifier(ExpressionSyntax expression) => expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
        _ => null,
    };

    private static string? TypeNameOf(BaseObjectCreationExpressionSyntax creation) => creation switch
    {
        ObjectCreationExpressionSyntax { Type: var type } => SimpleTypeName(type),
        ImplicitObjectCreationExpressionSyntax target => TargetTypeOf(target), // review round 2: new("team")
        _ => null,
    };

    private static string? SimpleTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        QualifiedNameSyntax q => q.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax a => a.Name.Identifier.ValueText,
        NullableTypeSyntax n => SimpleTypeName(n.ElementType),
        _ => null,
    };

    /// <summary>The type a target-typed <c>new(…)</c> builds, when the syntax names it: a declaration's type
    /// (<c>Entity t = new("team")</c>, a field or property initializer), or a write request's <c>Target = new(…)</c>
    /// (<c>Entity</c> for Create, Update and Upsert, <c>EntityReference</c> for Delete). Otherwise unknown.</summary>
    private static string? TargetTypeOf(ImplicitObjectCreationExpressionSyntax creation) => creation.Parent switch
    {
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } =>
            SimpleTypeName(declaration.Type),
        EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax property } => SimpleTypeName(property.Type),
        AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "Target" }, Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax request } }
            when TypeNameOf(request) is { } requestType && WriteRequests.Contains(requestType) =>
            requestType == "DeleteRequest" ? "EntityReference" : "Entity",
        _ => null,
    };

    private static string? NameOf(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
        IdentifierNameSyntax n => n.Identifier.ValueText,
        GenericNameSyntax g => g.Identifier.ValueText,
        _ => null,
    };

    private static string Method(string statement) => $$"""
        class S
        {
            async Task M(Guid id, Guid t, Guid u, Guid b, Guid bu, object body, object fields, Entity row, CancellationToken ct)
            {
                {{statement}}
            }
        }
        """;
}
