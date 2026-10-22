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

    private static readonly Regex Mention = new(
        @"\bNoneForForeignEnvironment\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    // ── The type's shape: there is no other way to say "no owner" ────────────────────────────────────

    [Fact(DisplayName = "ISS-005: ContactCreateOwner has no constructor outside itself and exactly the two factory methods")]
    public void ContactCreateOwner_CanOnlyBeMadeByItsTwoFactoryMethods()
    {
        var constructors = typeof(ContactCreateOwner).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.All(constructors, c => Assert.True(c.IsPrivate, $"constructor {c} must be private"));

        var factories = typeof(ContactCreateOwner)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => typeof(ContactCreateOwner).IsAssignableFrom(m.ReturnType) && !m.IsPrivate)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(ContactCreateOwner.FromCustomerBusinessUnit), nameof(ContactCreateOwner.NoneForForeignEnvironment) },
            factories);
    }

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
                public static ContactCreateOwner NoneForForeignEnvironment(string reason) => new ForeignEnvironment(reason);
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
                public static ContactCreateOwner NoneForForeignEnvironment(string reason) => new ForeignEnvironment(reason);
                public static ContactCreateOwner Default() => NoneForForeignEnvironment("default");
            }
            """));

    [Fact(DisplayName = "ISS-005 guard, must-fire: a binder file with no factory class is caught, never vacuously green")]
    public void Control_NoFactoryClass_IsCaught()
        => Assert.Contains("vacuous", Assert.Single(Violations(FactoryFile, "public sealed class ContactIdentityBinder { }")));
}
