using System.Reflection;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ISS-005 (#1673, TENANT-ISOLATION; UAC review) — "this binder creates contacts with no owner" is a decision that only
/// <c>ContactIdentityBinderFactory</c> (a binder over ANOTHER Dataverse environment) may make. Every other binder, the
/// DI-registered one over this BFF's own environment included, is built with
/// <c>ContactCreateOwner.FromCustomerBusinessUnit</c> and refuses a create it cannot own. So
/// <c>ContactCreateOwner.NoneForForeignEnvironment</c> may be mentioned in server code only inside that factory class (and
/// declared in <c>ContactCreateOwner.cs</c>); a use anywhere else is how an own-environment binder would silently create
/// app-owned contacts in root again.
/// </summary>
/// <remarks>
/// <para><b>What counts as a mention.</b> The name itself, in code: a call, a method group, a <c>nameof</c>. Comments (line,
/// doc) are not code and are skipped. The factory class is located by name in <c>ContactIdentityBinder.cs</c> and its body by
/// brace matching, so a use in the binder class, or anywhere else in that file, still fails. Test code is out of scope: tests
/// that do not exercise ownership build their binder with a named test-kit policy.</para>
/// <para>Known limit: the rule matches the member's name, not its behaviour. Reflection by a computed string is not seen;
/// the constructor of <c>ContactCreateOwner</c> is private and its factory methods are pinned below, so a new way to say "no
/// owner" has to appear in the one file this guard reads.</para>
/// <para>Crude by design (<see cref="SourceScan"/>), like every rule built on it: each rule has a must-fire and a
/// must-not-fire control below. Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// </remarks>
public class ContactCreateOwnerPolicyGuardTests
{
    private const string FactoryFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactIdentityBinder.cs";
    private const string FactoryClass = "ContactIdentityBinderFactory";
    private const string DeclaringFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ContactCreateOwner.cs";

    // UAC review F3: also the bare variant name. ContactCreateOwner once had a nested ForeignEnvironment class with an internal
    // constructor, which `new ContactCreateOwner.ForeignEnvironment("x")` could use without naming the factory method.
    private static readonly Regex Mention = new(
        @"\b(?:NoneFor)?ForeignEnvironment\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The member's declaration; group 1 is the name, so a mention can be tied to it by position.</summary>
    private static readonly Regex Declaration = new(
        @"\bstatic\s+ContactCreateOwner\s+(NoneForForeignEnvironment)\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The mentions in one file's code text that the rule does not allow: "line N" for each.</summary>
    internal static IReadOnlyList<string> Violations(string relativePath, string code)
    {
        var allowed = new List<(int Start, int End)>();
        if (relativePath == FactoryFile)
        {
            var span = ClassBody(code, FactoryClass);
            if (span is null)
            {
                return new[] { $"class {FactoryClass} was not found (the guard would be vacuous)" };
            }

            allowed.Add(span.Value);
        }

        var declarations = relativePath == DeclaringFile
            ? Declaration.Matches(code).Select(d => d.Groups[1].Index).ToHashSet()
            : new HashSet<int>();
        var violations = new List<string>();
        foreach (Match m in Mention.Matches(code))
        {
            var inFactory = allowed.Any(a => m.Index >= a.Start && m.Index < a.End);
            var isDeclaration = declarations.Contains(m.Index);
            if (!inFactory && !isDeclaration)
            {
                violations.Add($"line {SourceScan.LineOf(code, m.Index)}");
            }
        }

        return violations;
    }

    /// <summary>[start, end) of the body of the first class with this name: from its opening brace to the matching one.</summary>
    private static (int Start, int End)? ClassBody(string code, string className)
    {
        var declaration = Regex.Match(code, @"\bclass\s+" + Regex.Escape(className) + @"\b");
        if (!declaration.Success)
        {
            return null;
        }

        var open = code.IndexOf('{', declaration.Index);
        if (open < 0)
        {
            return null;
        }

        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{')
            {
                depth++;
            }
            else if (code[i] == '}' && --depth == 0)
            {
                return (open, i + 1);
            }
        }

        return null;
    }

    [Fact(DisplayName = "ISS-005: NoneForForeignEnvironment is mentioned in server code only in ContactIdentityBinderFactory")]
    public void NoneForForeignEnvironment_IsUsedOnlyByTheFactory()
    {
        var offenders = new List<string>();
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var path = SourceScan.Relative(file).Replace('\\', '/');
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            if (!Mention.IsMatch(code))
            {
                continue;
            }

            offenders.AddRange(Violations(path, code).Select(v => $"{path}: {v}"));
        }

        Assert.True(
            offenders.Count == 0,
            "ContactCreateOwner.NoneForForeignEnvironment means 'this binder creates ownerless contacts'. It is for a binder over "
            + "ANOTHER Dataverse environment and so only ContactIdentityBinderFactory may use it (ISS-005, #1673; the gap it leaves "
            + "is tracked in #2270). A binder over this BFF's own environment takes ContactCreateOwner.FromCustomerBusinessUnit. "
            + "Offending mentions:\n" + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "ISS-005 guard, non-vacuous: the factory does use it, and the declaration exists")]
    public void TheGuard_SeesTheFactorysUse_AndTheDeclaration()
    {
        var factoryCode = SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, FactoryFile)));
        var body = ClassBody(factoryCode, FactoryClass);
        Assert.NotNull(body);
        Assert.True(
            Mention.Matches(factoryCode).Any(m => m.Index >= body!.Value.Start && m.Index < body.Value.End),
            "ContactIdentityBinderFactory no longer uses NoneForForeignEnvironment: update this guard with the change.");

        var declaringCode = SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, DeclaringFile)));
        Assert.Matches(Declaration, declaringCode);
    }

    // ── No ownerless CreateAsync("contacts") anywhere in the BFF (ISS-005 invite create, #1673 review F1) ──────────────

    private const string BffRoot = "src/server/api/Sprk.Bff.Api/";

    /// <summary>A Dataverse Web API create into the contact entity set: the literal, or a constant named like it.</summary>
    private static readonly Regex ContactCreate = new(
        @"\.CreateAsync\s*\(\s*(?:""contacts""|\w*Contacts?EntitySet\b)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The start of a member declaration (an access modifier at the head of a line, up to its opening parenthesis).</summary>
    private static readonly Regex MemberStart = new(
        @"\n[ \t]*(?:public|private|internal|protected)\b[^\n;=]*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex OwnerBind = new(
        @"ownerid@odata\.bind|\bOwnerBindKey\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The contact creates in one BFF file's code whose enclosing member (from its declaration to the call) never sets
    /// <c>ownerid@odata.bind</c>: "line N" for each. A contact created through the Web API without an owner is owned by the
    /// calling application user in the root business unit (ISS-005). Known limit: a payload built in ANOTHER member and passed
    /// in is flagged too (build it where it is created), and a create that is not <c>CreateAsync("contacts"</c> or a constant
    /// named <c>*ContactEntitySet</c> (the identity store's own create, which binds the owner through
    /// <c>ContactIdentityStore.OwnerBindKey</c>) is not seen.
    /// </summary>
    internal static IReadOnlyList<string> OwnerlessContactCreates(string relativePath, string code)
    {
        var violations = new List<string>();
        if (!relativePath.StartsWith(BffRoot, StringComparison.Ordinal))
        {
            return violations;
        }

        var memberStarts = MemberStart.Matches(code).Select(m => m.Index).ToList();
        foreach (Match create in ContactCreate.Matches(code))
        {
            var start = memberStarts.Where(i => i < create.Index).DefaultIfEmpty(0).Max();
            if (!OwnerBind.IsMatch(code.Substring(start, create.Index - start)))
            {
                violations.Add($"line {SourceScan.LineOf(code, create.Index)}");
            }
        }

        return violations;
    }

    [Fact(DisplayName = "ISS-005: no CreateAsync(\"contacts\") in Sprk.Bff.Api is ownerless")]
    public void EveryContactCreate_InTheBff_SetsAnOwner()
    {
        var offenders = new List<string>();
        var seen = 0;
        foreach (var file in SourceScan.ServerSourceFiles())
        {
            var path = SourceScan.Relative(file).Replace('\\', '/');
            if (!path.StartsWith(BffRoot, StringComparison.Ordinal))
            {
                continue;
            }

            var code = SourceScan.CodeText(File.ReadAllLines(file));
            seen += ContactCreate.Matches(code).Count;
            offenders.AddRange(OwnerlessContactCreates(path, code).Select(v => $"{path}: {v}"));
        }

        Assert.True(seen > 0, "the scan saw no CreateAsync(\"contacts\") at all (the invite create): the guard is vacuous");
        Assert.True(
            offenders.Count == 0,
            "A contact created through the Web API with no ownerid@odata.bind is owned by the BFF's application user in the root "
            + "business unit (ISS-005, #1673). Resolve the owner team (ContactIdentityBinder.ResolveCreateOwnerAsync) and bind it. "
            + "Offending creates:\n" + string.Join("\n", offenders));
    }

    private const string InviteFile = BffRoot + "Api/ExternalAccess/ExternalInviteService.cs";

    [Fact(DisplayName = "ISS-005 guard, must-fire: an ownerless contact create is caught")]
    public void Control_AnOwnerlessContactCreate_IsCaught()
        => Assert.Single(OwnerlessContactCreates(InviteFile, """
            private static async Task<Guid> CreateContactAsync(DataverseWebApiClient client, string email)
            {
                var payload = new Dictionary<string, object?> { ["emailaddress1"] = email };
                return await client.CreateAsync("contacts", payload, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a constant-named contact set is caught, and a bind in ANOTHER member does not excuse it")]
    public void Control_AConstantNamedSet_AndABindElsewhere_AreCaught()
        => Assert.Single(OwnerlessContactCreates(InviteFile, """
            private static object Other() => new Dictionary<string, object?> { ["ownerid@odata.bind"] = "/teams(x)" };

            private static async Task<Guid> CreateContactAsync(DataverseWebApiClient client, string email)
            {
                var payload = new Dictionary<string, object?> { ["emailaddress1"] = email };
                return await client.CreateAsync(ContactEntitySet, payload, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: a contact create that binds the owner passes")]
    public void Control_AnOwnedContactCreate_HasNoViolation()
        => Assert.Empty(OwnerlessContactCreates(InviteFile, """
            private static async Task<Guid> CreateContactAsync(DataverseWebApiClient client, string email, Guid team)
            {
                var payload = new Dictionary<string, object?>
                {
                    ["emailaddress1"] = email,
                    ["ownerid@odata.bind"] = $"/teams({team:D})",
                };
                return await client.CreateAsync(ContactEntitySet, payload, ct);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: creates in other entity sets, and files outside the BFF, pass")]
    public void Control_OtherEntitySets_AndOtherProjects_HaveNoViolation()
    {
        Assert.Empty(OwnerlessContactCreates(InviteFile, "return await client.CreateAsync(\"accounts\", payload, ct);"));
        Assert.Empty(OwnerlessContactCreates("src/server/shared/Spaarke.Dataverse/X.cs", "return await client.CreateAsync(\"contacts\", payload, ct);"));
    }

    // ── The type's shape: there is no other way to say "no owner" ────────────────────────────────────

    /// <summary>
    /// The constructors of <paramref name="type"/> and of every type nested in it, recursively, that are not private: the ways
    /// to make a policy object other than the two factory methods. Reflection, so it sees a nested class a name scan would not.
    /// </summary>
    internal static IReadOnlyList<string> NonPrivateConstructors(Type type)
    {
        var offenders = new List<string>();
        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!constructor.IsPrivate)
            {
                offenders.Add($"{type.FullName}: {constructor}");
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            offenders.AddRange(NonPrivateConstructors(nested));
        }

        return offenders;
    }

    [Fact(DisplayName = "ISS-005: ContactCreateOwner and every type nested in it have only private constructors; exactly the two factory methods")]
    public void ContactCreateOwner_CanOnlyBeMadeByItsTwoFactoryMethods()
    {
        Assert.Empty(NonPrivateConstructors(typeof(ContactCreateOwner)));
        Assert.True(typeof(ContactCreateOwner).IsSealed, "a subclass would be a third way to make one");

        var factories = typeof(ContactCreateOwner)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => typeof(ContactCreateOwner).IsAssignableFrom(m.ReturnType) && !m.IsPrivate)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(ContactCreateOwner.FromCustomerBusinessUnit), nameof(ContactCreateOwner.NoneForForeignEnvironment) },
            factories);
    }

    // Fixtures for the reflection check's controls.
    private sealed class FixtureLeaky
    {
        private FixtureLeaky()
        {
        }

        internal sealed class Variant
        {
            internal Variant()
            {
            }
        }
    }

    private sealed class FixtureSealed
    {
        private FixtureSealed()
        {
        }

        private sealed class Variant
        {
            private Variant()
            {
            }
        }
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a nested type with an internal constructor is caught")]
    public void Control_ANestedTypeWithAnInternalConstructor_IsCaught()
        => Assert.Single(NonPrivateConstructors(typeof(FixtureLeaky)));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: private constructors, nested ones included, pass")]
    public void Control_PrivateConstructors_AreNotFlagged()
        => Assert.Empty(NonPrivateConstructors(typeof(FixtureSealed)));

    [Theory(DisplayName = "ISS-005 guard, must-fire: the bare nested-variant name is caught outside the factory")]
    [InlineData("var owner = new ContactCreateOwner.ForeignEnvironment(\"x\");")]
    [InlineData("Func<string, object> f = r => new ForeignEnvironment(r);")]
    public void Control_TheBareVariantName_IsCaught(string statement)
        => Assert.NotEmpty(Violations(OtherFile, statement));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: IsForeignEnvironment (a different identifier) passes")]
    public void Control_AnIdentifierContainingTheName_IsNotFlagged()
        => Assert.Empty(Violations(OtherFile, "if (owner.IsForeignEnvironment) { }"));

    [Fact(DisplayName = "ISS-005: the ContactIdentityBinder constructor requires a non-null ContactCreateOwner")]
    public void TheBinderConstructor_TakesTheOwnerPolicy_AsARequiredNonNullableParameter()
    {
        var constructor = Assert.Single(typeof(ContactIdentityBinder).GetConstructors());
        var parameter = Assert.Single(constructor.GetParameters(), p => p.ParameterType == typeof(ContactCreateOwner));

        Assert.False(parameter.IsOptional, "the owner policy must have no default: a forgotten argument must not compile");
        Assert.False(parameter.HasDefaultValue);
        Assert.Equal(NullabilityState.NotNull, new NullabilityInfoContext().Create(parameter).WriteState);
    }

    // ── Controls: the rule over synthetic code ───────────────────────────────────────────────────────

    private const string OtherFile = "src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs";

    private const string BinderFileShape = """
        public sealed class ContactIdentityBinder
        {
            public ContactIdentityBinder(ContactCreateOwner createOwner) { }
        }

        public class ContactIdentityBinderFactory
        {
            public ContactIdentityBinder CreateBinder(string url)
                => new(ContactCreateOwner.NoneForForeignEnvironment($"foreign {url}; see #2270"));
        }
        """;

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the factory class's own use, in the binder file, passes")]
    public void Control_TheFactorysUse_HasNoViolation()
        => Assert.Empty(Violations(FactoryFile, BinderFileShape));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the declaration in ContactCreateOwner.cs passes")]
    public void Control_TheDeclaration_HasNoViolation()
        => Assert.Empty(Violations(DeclaringFile, """
            public abstract class ContactCreateOwner
            {
                public static ContactCreateOwner NoneForForeignEnvironment(string reason) => new(null, null, reason);
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-not-fire: the other policy, and comments naming the member, pass anywhere")]
    public void Control_TheOtherPolicy_AndComments_HaveNoViolation()
    {
        var code = SourceScan.CodeText(new[]
        {
            "var owner = ContactCreateOwner.FromCustomerBusinessUnit(options, configuration);",
            "// ContactCreateOwner.NoneForForeignEnvironment(\"x\") is for the factory only",
            "/// <see cref=\"ContactCreateOwner.NoneForForeignEnvironment\"/>",
        });

        Assert.Empty(Violations(OtherFile, code));
    }

    [Theory(DisplayName = "ISS-005 guard, must-fire: a use outside the factory is caught")]
    [InlineData(OtherFile, "services.AddSingleton(sp => new ContactIdentityBinder(a, b, c, d, ContactCreateOwner.NoneForForeignEnvironment(\"x\")));")]
    [InlineData(OtherFile, "Func<string, ContactCreateOwner> none = ContactCreateOwner.NoneForForeignEnvironment;")]
    [InlineData(OtherFile, "var name = nameof(ContactCreateOwner.NoneForForeignEnvironment);")]
    [InlineData("src/server/api/Sprk.Bff.Api/Services/Registration/RegistrationDataverseService.cs",
        "return new ContactIdentityBinder(s, o, t, l, ContactCreateOwner.NoneForForeignEnvironment(\"own\"));")]
    public void Control_AUseOutsideTheFactory_IsCaught(string path, string statement)
        => Assert.NotEmpty(Violations(path, statement));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a use in the binder class, in the factory's own file, is caught")]
    public void Control_AUseInTheBinderClass_InTheFactorysFile_IsCaught()
    {
        var code = BinderFileShape.Replace(
            "public ContactIdentityBinder(ContactCreateOwner createOwner) { }",
            "public ContactIdentityBinder() : this(ContactCreateOwner.NoneForForeignEnvironment(\"lazy\")) { }",
            StringComparison.Ordinal);

        Assert.Single(Violations(FactoryFile, code));
    }

    [Fact(DisplayName = "ISS-005 guard, must-fire: a use in the declaring file other than the declaration is caught")]
    public void Control_AnotherUseInTheDeclaringFile_IsCaught()
        => Assert.Single(Violations(DeclaringFile, """
            public abstract class ContactCreateOwner
            {
                public static ContactCreateOwner NoneForForeignEnvironment(string reason) => new(null, null, reason);
                public static ContactCreateOwner Default() => NoneForForeignEnvironment("default");
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a binder file with no factory class is caught, never vacuously green")]
    public void Control_NoFactoryClass_IsCaught()
        => Assert.Contains("vacuous", Assert.Single(Violations(FactoryFile, "public sealed class ContactIdentityBinder { }")));
}
