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
/// <c>ContactIdentityBinder.CreateInviteContactGuardedAsync</c>, which applies the same rule immediately before the POST.
/// <see cref="AContactEntitySetCreate_IsMadeOnlyInsideTheGuardedInviteCreate"/> pins every contact-entity-set create
/// (<c>CreateAsync</c> / <c>PostAsync</c> on <c>"contacts"</c>) to that method.</para>
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
    // Slice C2c: every contact-entity-set create in server code is a guarded one
    // ═════════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The binder's guarded invite POST, the one place a contact may be created through the Web API client.</summary>
    internal const string InviteGuardMethod = "CreateInviteContactGuardedAsync";

    /// <summary>The Web API client members that create a row.</summary>
    private static readonly HashSet<string> CreateMembers = new(StringComparer.Ordinal) { "CreateAsync", "PostAsync" };

    private const string ContactEntitySetLiteral = "contacts";

    [Fact]
    public void AContactEntitySetCreate_IsMadeOnlyInsideTheGuardedInviteCreate()
    {
        var files = SourceScan.ServerSourceFiles()
            .Select(f => (Path: SourceScan.Relative(f).Replace('\\', '/'), Code: File.ReadAllText(f)))
            .ToList();
        var contactSetNames = ContactSetConstantNames(files.Select(f => f.Code));

        var violations = new List<string>();
        var guardedUses = 0;
        foreach (var (path, code) in files)
        {
            var result = ScanEntitySetCreates(path, code, contactSetNames);
            violations.AddRange(result.Violations);
            guardedUses += result.GuardedUses;
        }

        Assert.True(guardedUses == 1,
            $"Expected exactly one contact-entity-set create inside {GuardClass}.{InviteGuardMethod}, found {guardedUses}: the "
            + "guard is vacuous or the guarded method was renamed or moved.");
        Assert.True(violations.Count == 0,
            "No contact is created around a removal: a contact-entity-set create (CreateAsync / PostAsync on \"contacts\") may "
            + $"be made only inside {GuardClass}.{InviteGuardMethod}, which checks the inactive contacts for the email first "
            + $"(or through the store's {StoreCreate}, guarded by {GuardMethod}). Offending creates:\n"
            + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("a direct \"contacts\" create in another file", "src/server/api/Sprk.Bff.Api/Services/Other.cs",
        "class Other { async Task Make() { await _dataverse.CreateAsync(\"contacts\", payload, ct); } }")]
    [InlineData("a create in the invite service outside the guarded method",
        "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalInviteService.cs",
        "class ExternalInviteService { private const string ContactEntitySet = \"contacts\";\n"
        + " static async Task<Guid> CreateContactAsync() => await dataverseClient.CreateAsync(ContactEntitySet, payload, ct); }")]
    [InlineData("a PostAsync to \"contacts\"", "src/server/api/Sprk.Bff.Api/Services/Other.cs",
        "class Other { async Task Make() { await _client.PostAsync(\"contacts\", body, ct); } }")]
    [InlineData("a lambda inside the guarded method", ContactCreateAroundRemovalGuardTests.BinderFile,
        "class ContactIdentityBinder { async Task CreateInviteContactGuardedAsync() { Func<Task> f = () => _d.CreateAsync(\"contacts\", p, ct); } }")]
    [InlineData("the guard-named method of another class", ContactCreateAroundRemovalGuardTests.BinderFile,
        "class Other { async Task CreateInviteContactGuardedAsync() { await _d.CreateAsync(\"contacts\", p, ct); } }")]
    public void TheEntitySetRule_Fires(string because, string path, string code)
        => Assert.True(ScanEntitySetCreates(path, code, ContactSetConstantNames(new[] { code })).Violations.Count == 1, because);

    [Fact]
    public void TheEntitySetRule_Fires_OnAConstantDeclaredInAnotherFile()
        => Assert.Single(ScanEntitySetCreates("src/server/api/Sprk.Bff.Api/Services/Other.cs",
            "class Other { async Task Make() { await _d.CreateAsync(Names.ContactSet, p, ct); } }",
            ContactSetConstantNames(new[] { "static class Names { internal const string ContactSet = \"contacts\"; }" })).Violations);

    [Fact]
    public void TheEntitySetRule_DoesNotFire_InsideTheGuardedInviteCreate()
    {
        const string code = "class ContactIdentityBinder { private const string InviteContactEntitySet = \"contacts\";\n"
            + " internal async Task<(object, Guid)> CreateInviteContactGuardedAsync() {\n"
            + "  var created = await dataverseClient.CreateAsync(InviteContactEntitySet, payload, ct).ConfigureAwait(false);\n"
            + "  return (null, created); } }";

        var result = ScanEntitySetCreates(BinderFile, code, ContactSetConstantNames(new[] { code }));

        Assert.Empty(result.Violations);
        Assert.Equal(1, result.GuardedUses);
    }

    [Theory]
    [InlineData("a read of \"contacts\"", "class Other { async Task R() { await _d.RetrieveAsync<Row>(\"contacts\", id, \"fullname\", ct); } }")]
    [InlineData("a create on another entity set", "class Other { async Task M() { await _d.CreateAsync(\"sprk_externalrecordaccesses\", p, ct); } }")]
    [InlineData("a create on a constant naming another set",
        "class Other { private const string GrantSet = \"sprk_externalrecordaccesses\"; async Task M() { await _d.CreateAsync(GrantSet, p, ct); } }")]
    [InlineData("a comment", "class Other { void M() { // _d.CreateAsync(\"contacts\", p, ct)\n } }")]
    public void TheEntitySetRule_DoesNotFire(string because, string code)
        => Assert.True(ScanEntitySetCreates("src/server/api/Sprk.Bff.Api/Services/Other.cs", code,
            ContactSetConstantNames(new[] { code })).Violations.Count == 0, because);

    /// <summary>The names of every <c>const string</c> whose value is <c>"contacts"</c> in <paramref name="sources"/>.</summary>
    internal static IReadOnlySet<string> ContactSetConstantNames(IEnumerable<string> sources)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
            foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.Parent?.Parent is FieldDeclarationSyntax field
                    && field.Modifiers.Any(SyntaxKind.ConstKeyword)
                    && declarator.Initializer?.Value is LiteralExpressionSyntax { Token.ValueText: ContactEntitySetLiteral })
                {
                    names.Add(declarator.Identifier.ValueText);
                }
            }
        }

        return names;
    }

    /// <summary>
    /// Every invocation of <c>CreateAsync</c> / <c>PostAsync</c> whose first argument is the literal <c>"contacts"</c> or a
    /// constant (by name, from <paramref name="contactSetNames"/>) holding it. Allowed only inside the binder's guarded invite
    /// create. Known limit: an entity set computed at run time is not seen.
    /// </summary>
    internal static ScanResult ScanEntitySetCreates(string relativePath, string code, IReadOnlySet<string> contactSetNames)
    {
        var root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        var violations = new List<string>();
        var guarded = 0;
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                GenericNameSyntax generic => generic.Identifier.ValueText,
                _ => null,
            };
            if (name is null || !CreateMembers.Contains(name)
                || invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is not { } first
                || !NamesContactSet(first, contactSetNames))
            {
                continue;
            }

            if (IsInsideMethod(relativePath, invocation, InviteGuardMethod))
            {
                guarded++;
                continue;
            }

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            violations.Add($"{relativePath}:{line}: {invocation.ToString().Trim()}");
        }

        return new ScanResult(violations, guarded);
    }

    private static bool NamesContactSet(ExpressionSyntax expression, IReadOnlySet<string> contactSetNames) => expression switch
    {
        LiteralExpressionSyntax literal => literal.Token.ValueText == ContactEntitySetLiteral,
        IdentifierNameSyntax identifier => contactSetNames.Contains(identifier.Identifier.ValueText),
        MemberAccessExpressionSyntax member => contactSetNames.Contains(member.Name.Identifier.ValueText),
        _ => false,
    };

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
