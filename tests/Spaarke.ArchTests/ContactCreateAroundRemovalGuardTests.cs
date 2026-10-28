using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Slice C2b: no contact is created around a removal. The binder's ONE guarded create
/// (<c>ContactIdentityBinder.CreateContactGuardedAsync</c>) checks the inactive contacts for the new contact's email before
/// it calls the identity store's create, so the store's create (<c>CreateContactForOidAsync</c>) may be called from server
/// code ONLY inside that method. A new create path that calls the store directly would skip the check; this guard fails it.
/// </summary>
/// <remarks>
/// <para>Any mention of the member name in server code counts (a call, a method group, a <c>nameof</c>), except the member's
/// own declarations (the interface and the store implement it; a declaration's name is a token, not a use). A use is allowed
/// only when its nearest enclosing member is the method <c>CreateContactGuardedAsync</c> of the class
/// <c>ContactIdentityBinder</c> in <c>ContactIdentityBinder.cs</c>; a lambda or local function inside it is a unit of its own
/// and does not count as the method. Comments and XML documentation are trivia and are not scanned.</para>
/// <para>Known limit: the rule matches the member's name, not its behaviour; a reflection call by a computed string is not
/// seen. Slice C2c: the invite create does not use the store's create; it POSTs through the Web API client inside
/// <c>ContactIdentityBinder.CreateInviteContactGuardedAsync</c>, which applies the same rule immediately before the POST.</para>
/// <para>C2d: <see cref="EveryContactCreate_InServerCode_IsTheGuardedInviteCreate_OrTheStoreSeam"/> pins EVERY contact create
/// the ownership census sees (<see cref="ContactCreateOwnerPolicyGuardTests.ContactCreates(string, SyntaxNode, IReadOnlyCollection{string}?)"/>:
/// Web API creates, HTTP POSTs to a contacts path, user-client posts and SDK contact entities, by literal, interpolated or
/// prefixed path, constant or local) to two sites: that method, and the store's create seam, whose only caller is the store
/// create pinned above. One detector serves both rules, so a create shape the census learns is covered here too. Must-fire
/// and must-not-fire controls below. Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// </remarks>
public class ContactCreateAroundRemovalGuardTests
{
    internal const string BinderFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs";
    internal const string GuardClass = "ContactIdentityBinder";
    internal const string GuardMethod = "CreateContactGuardedAsync";
    internal const string StoreCreate = "CreateContactForOidAsync";

    /// <summary>One scan's answer: the uses outside the guarded method, and how many uses are inside it.</summary>
    internal sealed record ScanResult(IReadOnlyList<string> Violations, int GuardedUses);

    [Fact]
    public void TheStoreCreate_IsCalledOnlyInsideTheGuardedCreate()
    {
        var violations = new List<string>();
        var guardedUses = 0;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var path = SourceScan.Relative(file).Replace('\\', '/');
            var result = Scan(path, File.ReadAllText(file));
            violations.AddRange(result.Violations);
            guardedUses += result.GuardedUses;
        }

        Assert.True(guardedUses == 1,
            $"Expected exactly one store create inside {GuardClass}.{GuardMethod}, found {guardedUses}: the guard is vacuous "
            + "or the guarded method was renamed or moved.");
        Assert.True(violations.Count == 0,
            $"No contact is created around a removal: the store's {StoreCreate} may be called only inside "
            + $"{GuardClass}.{GuardMethod}, which checks the inactive contacts for the email first. Route the create through "
            + "that method. Offending uses:\n" + string.Join("\n", violations));
    }

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a call in another binder method",
        "class ContactIdentityBinder { async Task Other() { await _store.CreateContactForOidAsync(o, p, d, ct); } }")]
    [InlineData("a call split across lines",
        "class ContactIdentityBinder { async Task Other() { await _store\n    .CreateContactForOidAsync(\n o, p, d, ct); } }")]
    [InlineData("a method group in another method",
        "class ContactIdentityBinder { void Other() { Func<Guid, Task> f = _store.CreateContactForOidAsync; } }")]
    [InlineData("a nameof in another method",
        "class ContactIdentityBinder { string Other() => nameof(IContactIdentityStore.CreateContactForOidAsync); }")]
    [InlineData("a lambda inside the guarded method",
        "class ContactIdentityBinder { async Task CreateContactGuardedAsync() { Func<Task> f = () => _store.CreateContactForOidAsync(o, p, d, ct); } }")]
    [InlineData("a local function inside the guarded method",
        "class ContactIdentityBinder { async Task CreateContactGuardedAsync() { Task L() => _store.CreateContactForOidAsync(o, p, d, ct); } }")]
    [InlineData("a guard-named method of another class",
        "class Other { async Task CreateContactGuardedAsync() { await _store.CreateContactForOidAsync(o, p, d, ct); } }")]
    [InlineData("a field initializer",
        "class ContactIdentityBinder { private readonly object _x = Store.CreateContactForOidAsync(o, p, d, ct); }")]
    public void TheRule_Fires(string because, string code)
        => Assert.True(Scan(BinderFile, code).Violations.Count == 1, because);

    [Fact]
    public void TheRule_Fires_OnTheGuardNamedMethodInAnotherFile()
        => Assert.Single(Scan("src/server/api/Sprk.Bff.Api/Services/Other.cs",
            "class ContactIdentityBinder { async Task CreateContactGuardedAsync() { await _store.CreateContactForOidAsync(o, p, d, ct); } }")
            .Violations);

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheRule_DoesNotFire_OnTheCallInsideTheGuardedMethod()
    {
        var result = Scan(BinderFile,
            "class ContactIdentityBinder { private async Task<GuardedCreate> CreateContactGuardedAsync(Guid oid) {\n"
            + "  var write = await _store.CreateContactForOidAsync(oid, plane, details, ct).ConfigureAwait(false);\n"
            + "  return new GuardedCreate(null, write); } }");

        Assert.Empty(result.Violations);
        Assert.Equal(1, result.GuardedUses);
    }

    [Theory]
    [InlineData("the interface declaration",
        "interface IContactIdentityStore { Task<StoreWriteResult> CreateContactForOidAsync(Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct); }")]
    [InlineData("the implementation declaration",
        "class DataverseContactIdentityStore { public async Task<StoreWriteResult> CreateContactForOidAsync(Guid oid) { return null; } }")]
    [InlineData("a documentation reference",
        "class ContactIdentityBinder { /// <see cref=\"IContactIdentityStore.CreateContactForOidAsync\"/>\n void Other() { } }")]
    [InlineData("a line comment",
        "class ContactIdentityBinder { void Other() { // _store.CreateContactForOidAsync(o, p, d, ct) was the old call\n } }")]
    [InlineData("another member with a longer name",
        "class ContactIdentityBinder { async Task Other() { await _store.CreateContactForOidAsyncLegacy(o, p, d, ct); } }")]
    public void TheRule_DoesNotFire(string because, string code)
        => Assert.True(Scan(BinderFile, code).Violations.Count == 0, because);

    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════
    // C2d F1: every contact create in server code is a guarded one (ONE detector: the ownership census)
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The binder's guarded invite create: one of the two places a contact may be created.</summary>
    internal const string InviteGuardMethod = "CreateInviteContactGuardedAsync";

    /// <summary>The identity store's create seam: the other place, reached only through <see cref="GuardMethod"/>.</summary>
    internal const string StoreFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityStore.cs";
    internal const string StoreClass = "DataverseContactIdentityStore";
    internal const string StoreSeam = "BuildCreateRequest";

    /// <summary>One file's contact creates: those at neither allowed site, and how many sit at each allowed site.</summary>
    internal sealed record CreateSiteResult(IReadOnlyList<string> Violations, int InviteCreates, int StoreSeamCreates);

    /// <summary>
    /// The contact creates in one server file, judged by site. The creates are the ownership census's
    /// (<see cref="ContactCreateOwnerPolicyGuardTests.ContactCreates(string, SyntaxNode, IReadOnlyCollection{string}?)"/>):
    /// <c>CreateAsync</c> on a contact table or set, <c>HttpMethod.Post</c> next to a contacts path, <c>Post*Async</c> to a
    /// contacts path and <c>new Entity("contact")</c>, with the path read from literals, interpolated, prefixed and concatenated
    /// strings, constants and locals. Whether the create binds an owner does not matter here. A create is allowed only (i) in
    /// the member <see cref="InviteGuardMethod"/> of <see cref="GuardClass"/> in <see cref="BinderFile"/>, or (ii) in the member
    /// <see cref="StoreSeam"/> of <see cref="StoreClass"/> in <see cref="StoreFile"/>; a lambda or local function inside either
    /// is a unit of its own and is not allowed. Known limit: what the census does not see (its own doc comment lists it, and
    /// the runtime-table chat create) is not seen here either.
    /// </summary>
    internal static CreateSiteResult ScanContactCreateSites(
        string relativePath, SyntaxNode root, IReadOnlyCollection<string> constantNames)
    {
        var violations = new List<string>();
        int invite = 0, store = 0;
        foreach (var hit in ContactCreateOwnerPolicyGuardTests.ContactCreates(relativePath, root, constantNames))
        {
            if (IsSite(relativePath, hit, BinderFile, GuardClass, InviteGuardMethod))
            {
                invite++;
            }
            else if (IsSite(relativePath, hit, StoreFile, StoreClass, StoreSeam))
            {
                store++;
            }
            else
            {
                violations.Add($"{relativePath}:{hit.Line}: {hit.Shape} in {hit.TypeName}.{hit.MemberName ?? "?"} ({hit.Unit})");
            }
        }

        return new CreateSiteResult(violations, invite, store);
    }

    internal static CreateSiteResult ScanContactCreateSites(string relativePath, string code, IReadOnlyCollection<string>? constantNames = null)
    {
        var root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        return ScanContactCreateSites(relativePath, root,
            constantNames ?? ContactCreateOwnerPolicyGuardTests.ContactConstantNames(root));
    }

    private static bool IsSite(string relativePath, ContactCreateOwnerPolicyGuardTests.ContactCreateHit hit, string file,
        string typeName, string memberName)
        => relativePath == file
           && hit.Unit == ContactCreateOwnerPolicyGuardTests.MemberKind
           && string.Equals(hit.TypeName, typeName, StringComparison.Ordinal)
           && string.Equals(hit.MemberName, memberName, StringComparison.Ordinal);

    /// <summary>Every mention of <see cref="StoreSeam"/> outside its own declaration: "path:line in Type.Member" for each.</summary>
    internal static IReadOnlyList<string> StoreSeamCallers(string relativePath, SyntaxNode root)
        => root.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(n => n.Identifier.ValueText == StoreSeam)
            .Select(n =>
            {
                var unit = n.Ancestors().FirstOrDefault(a =>
                    a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax);
                var member = unit is MethodDeclarationSyntax m ? m.Identifier.ValueText : unit?.Kind().ToString() ?? "?";
                var type = n.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "?";
                return $"{relativePath}:{n.GetLocation().GetLineSpan().StartLinePosition.Line + 1} in {type}.{member}";
            })
            .ToList();

    [Fact]
    public void EveryContactCreate_InServerCode_IsTheGuardedInviteCreate_OrTheStoreSeam()
    {
        var files = SourceScan.ServerSourceFiles()
            .Select(f => (Path: SourceScan.Relative(f).Replace('\\', '/'),
                Root: CSharpSyntaxTree.ParseText(File.ReadAllText(f), new CSharpParseOptions(LanguageVersion.Latest)).GetRoot()))
            .ToList();
        var names = files.SelectMany(f => ContactCreateOwnerPolicyGuardTests.ContactConstantNames(f.Root)).Distinct().ToList();

        var violations = new List<string>();
        int invite = 0, store = 0;
        foreach (var (path, root) in files)
        {
            var result = ScanContactCreateSites(path, root, names);
            violations.AddRange(result.Violations);
            invite += result.InviteCreates;
            store += result.StoreSeamCreates;
        }

        Assert.True(invite == 1 && store == 1,
            $"Expected exactly one contact create in {GuardClass}.{InviteGuardMethod} and one in {StoreClass}.{StoreSeam}, found "
            + $"{invite} and {store}: the guard is vacuous or a site was renamed or moved.");
        Assert.True(violations.Count == 0,
            "No contact is created around a removal: a contact create may be made only in "
            + $"{GuardClass}.{InviteGuardMethod} (it checks the inactive contacts for the email first) or in the store's "
            + $"{StoreClass}.{StoreSeam}, reached only through {GuardClass}.{GuardMethod}. Offending creates:\n"
            + string.Join("\n", violations));
    }

    [Fact]
    public void TheStoreSeam_IsCalledOnlyByTheStoreCreate_WhichOnlyTheGuardedCreateCalls()
    {
        var callers = SourceScan.ServerSourceFiles()
            .SelectMany(f => StoreSeamCallers(SourceScan.Relative(f).Replace('\\', '/'),
                CSharpSyntaxTree.ParseText(File.ReadAllText(f), new CSharpParseOptions(LanguageVersion.Latest)).GetRoot()))
            .ToList();

        // The store create's own callers are pinned by TheStoreCreate_IsCalledOnlyInsideTheGuardedCreate.
        Assert.Equal(new[] { $"{StoreClass}.{StoreCreate}" }, callers.Select(c => c[(c.IndexOf(" in ", StringComparison.Ordinal) + 4)..]));
        Assert.All(callers, c => Assert.StartsWith(StoreFile + ":", c, StringComparison.Ordinal));
    }

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────

    private const string OtherFile = "src/server/api/Sprk.Bff.Api/Services/Other.cs";
    private const string InviteServiceFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalInviteService.cs";

    [Theory]
    [InlineData("an interpolated HttpMethod.Post to contacts in another file", OtherFile,
        "class Other { async Task M() { await _http.SendAsync(new HttpRequestMessage(HttpMethod.Post, $\"{_baseUrl}/api/data/v9.2/contacts\"), ct); } }")]
    [InlineData("an interpolated PostAsync to contacts in another file", OtherFile,
        "class Other { async Task M() { await _client.PostAsync($\"{_root}contacts\", body, ct); } }")]
    [InlineData("a prefixed path held in a local", OtherFile,
        "class Other { async Task M() { var path = ApiPrefix + \"contacts\"; await _client.PostAsJsonAsync(path, body, ct); } }")]
    [InlineData("an SDK Entity(\"contact\") create outside the binder, even with an owner", OtherFile,
        "class Other { async Task M() { var e = new Entity(\"contact\") { [\"ownerid\"] = owner }; await _svc.CreateAsync(e, ct); } }")]
    [InlineData("a Web API create on contacts, even with an owner", OtherFile,
        "class Other { async Task M() { var p = new Dictionary<string, object?> { [\"ownerid@odata.bind\"] = t }; await _d.CreateAsync(\"contacts\", p, ct); } }")]
    [InlineData("a create in the invite service outside the guarded method", InviteServiceFile,
        "class ExternalInviteService { private const string ContactEntitySet = \"contacts\";\n"
        + " static async Task<Guid> CreateContactAsync() => await dataverseClient.CreateAsync(ContactEntitySet, payload, ct); }")]
    [InlineData("a create in another binder method", BinderFile,
        "class ContactIdentityBinder { async Task Other() { await _d.CreateAsync(\"contacts\", p, ct); } }")]
    [InlineData("a lambda inside the guarded invite create", BinderFile,
        "class ContactIdentityBinder { async Task CreateInviteContactGuardedAsync() { Func<Task> f = () => _d.CreateAsync(\"contacts\", p, ct); } }")]
    [InlineData("the guard-named method of another class", BinderFile,
        "class Other { async Task CreateInviteContactGuardedAsync() { await _d.CreateAsync(\"contacts\", p, ct); } }")]
    [InlineData("a local function named like the guarded method", BinderFile,
        "class ContactIdentityBinder { async Task Other() { async Task CreateInviteContactGuardedAsync() { await _d.CreateAsync(\"contacts\", p, ct); } } }")]
    [InlineData("the guard-named method in another file", OtherFile,
        "class ContactIdentityBinder { async Task CreateInviteContactGuardedAsync() { await _d.CreateAsync(\"contacts\", p, ct); } }")]
    [InlineData("a second create request in the store, outside the seam", StoreFile,
        "class DataverseContactIdentityStore { static (HttpMethod, string, object) BuildOther() => (HttpMethod.Post, \"contacts\", null); }")]
    public void TheCreateSiteRule_Fires(string because, string path, string code)
        => Assert.True(ScanContactCreateSites(path, code).Violations.Count == 1, because);

    [Fact]
    public void TheCreateSiteRule_Fires_OnAConstantDeclaredInAnotherFile()
    {
        var names = ContactCreateOwnerPolicyGuardTests.ContactConstantNames(
            "static class Names { internal const string ContactSet = \"contacts\"; }");
        Assert.Single(ScanContactCreateSites(OtherFile,
            "class Other { async Task M() { await _d.CreateAsync(Names.ContactSet, p, ct); } }", names).Violations);
    }

    [Fact]
    public void TheStoreSeamCallers_Fire_OnASecondCaller()
    {
        var root = CSharpSyntaxTree.ParseText(
            "class DataverseContactIdentityStore { void Other() { var r = BuildCreateRequest(o, p, d); } }").GetRoot();
        Assert.Equal(new[] { $"{StoreFile}:1 in {StoreClass}.Other" }, StoreSeamCallers(StoreFile, root));
    }

    // ── Must not fire: the two allowed sites ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCreateSiteRule_DoesNotFire_OnTheGuardedInviteCreate()
    {
        const string code = "class ContactIdentityBinder { private const string InviteContactEntitySet = \"contacts\";\n"
            + " internal async Task<(object, Guid)> CreateInviteContactGuardedAsync() {\n"
            + "  var payload = new Dictionary<string, object?> { [\"ownerid@odata.bind\"] = t };\n"
            + "  var created = await dataverseClient.CreateAsync(InviteContactEntitySet, payload, ct).ConfigureAwait(false);\n"
            + "  return (null, created); } }";

        var result = ScanContactCreateSites(BinderFile, code);

        Assert.Empty(result.Violations);
        Assert.Equal(1, result.InviteCreates);
    }

    [Fact]
    public void TheCreateSiteRule_DoesNotFire_OnTheStoreSeam()
    {
        const string code = """
            public sealed class DataverseContactIdentityStore
            {
                public static (HttpMethod Method, string Path, Dictionary<string, object?> Payload) BuildCreateRequest(
                    Guid oid, IdentityPlaneMarker plane, NewContactDetails details)
                    => (HttpMethod.Post, "contacts", CreatePayload(oid, plane, details));
            }
            """;

        var result = ScanContactCreateSites(StoreFile, code);

        Assert.Empty(result.Violations);
        Assert.Equal(1, result.StoreSeamCreates);
    }

    [Theory]
    [InlineData("a read of contacts", "class Other { async Task R() { await _d.RetrieveAsync<Row>(\"contacts\", id, \"fullname\", ct); } }")]
    [InlineData("a create on another entity set", "class Other { async Task M() { await _d.CreateAsync(\"sprk_externalrecordaccesses\", p, ct); } }")]
    [InlineData("a PATCH of a contact", "class Other { async Task M() { await _d.UpdateAsync(\"contacts\", id, p, ct); } }")]
    [InlineData("a comment", "class Other { void M() { // _d.CreateAsync(\"contacts\", p, ct)\n } }")]
    public void TheCreateSiteRule_DoesNotFire(string because, string code)
        => Assert.True(ScanContactCreateSites(OtherFile, code).Violations.Count == 0, because);

    // ── The scan ─────────────────────────────────────────────────────────────────────────────────────────────

    internal static ScanResult Scan(string relativePath, string code)
    {
        var root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        var violations = new List<string>();
        var guarded = 0;
        foreach (var use in root.DescendantNodes().OfType<IdentifierNameSyntax>()
                     .Where(n => n.Identifier.ValueText == StoreCreate))
        {
            if (IsInsideGuard(relativePath, use))
            {
                guarded++;
                continue;
            }

            var line = use.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            violations.Add($"{relativePath}:{line}: {use.Parent?.ToString().Trim()}");
        }

        return new ScanResult(violations, guarded);
    }

    private static bool IsInsideGuard(string relativePath, SyntaxNode use) => IsInsideMethod(relativePath, use, GuardMethod);

    private static bool IsInsideMethod(string relativePath, SyntaxNode use, string guardMethod)
    {
        if (relativePath != BinderFile)
            return false;

        // The nearest unit of code: a lambda or local function is its own unit, never the method that contains it.
        var unit = use.Ancestors().FirstOrDefault(a =>
            a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax);
        return unit is MethodDeclarationSyntax method
               && method.Identifier.ValueText == guardMethod
               && method.Parent is ClassDeclarationSyntax type
               && type.Identifier.ValueText == GuardClass;
    }
}
