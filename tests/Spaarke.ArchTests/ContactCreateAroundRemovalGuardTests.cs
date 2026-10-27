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
/// seen. The invite create (<c>ExternalInviteService</c>) does not use the store's create: it goes through
/// <c>ContactIdentityBinder.ResolveInviteContactAsync</c>, which applies the same rule
/// (<c>ContactBindingDecision.DecideCreateAroundRemoval</c>) before it answers "create".</para>
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

    private static bool IsInsideGuard(string relativePath, SyntaxNode use)
    {
        if (relativePath != BinderFile)
            return false;

        // The nearest unit of code: a lambda or local function is its own unit, never the method that contains it.
        var unit = use.Ancestors().FirstOrDefault(a =>
            a is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or MemberDeclarationSyntax);
        return unit is MethodDeclarationSyntax method
               && method.Identifier.ValueText == GuardMethod
               && method.Parent is ClassDeclarationSyntax type
               && type.Identifier.ValueText == GuardClass;
    }
}
