using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 185 (design note §5.1 and §9.1 AC7 (iv)): the invite-code check compares digests in
/// constant time. <c>InviteCode.Matches</c> must return through <c>CryptographicOperations.FixedTimeEquals</c>, and the file
/// must contain no early-exit comparison (<c>SequenceEqual</c>, <c>SequenceCompareTo</c>, <c>StructuralComparisons</c>, or
/// an <c>Equals</c> call).
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> A non-constant-time compare passes every behavioural test: it gives the same answers,
/// only slower or faster depending on the first differing byte. The ladder (CLAUDE.md §16) puts the rule here, where the
/// edit that breaks it fails the build. Enforced by: this guard (ArchTests, CI and the Stop hook).</para>
/// <para>Crude by design (<see cref="SourceScan"/>): text with line comments stripped. Each rule has a must-fire control
/// and a must-not-fire control below.</para>
/// </remarks>
public class InviteCodeFixedTimeCompareGuardTests
{
    private const string InviteCodeFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/InviteCode.cs";

    private static readonly Regex MatchesMethod = new(
        @"\bpublic\s+static\s+bool\s+Matches\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FixedTimeReturn = new(
        @"\breturn\s+CryptographicOperations\.FixedTimeEquals\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (Regex Pattern, string Why)[] Forbidden =
    {
        (new Regex(@"\bSequenceEqual\s*\(", RegexOptions.CultureInvariant), "SequenceEqual (exits at the first differing byte)"),
        (new Regex(@"\bSequenceCompareTo\s*\(", RegexOptions.CultureInvariant), "SequenceCompareTo (exits at the first differing byte)"),
        (new Regex(@"\bStructuralComparisons\b", RegexOptions.CultureInvariant), "StructuralComparisons (not constant time)"),
        (new Regex(@"\.Equals\s*\(|\bstring\.Equals\b|\bString\.Equals\b", RegexOptions.CultureInvariant),
            "an Equals call (string or span equality is not constant time)"),
    };

    /// <summary>The rule over one file's code text: the reasons it is violated, empty when it holds.</summary>
    internal static IReadOnlyList<string> Violations(string code)
    {
        var violations = new List<string>();
        if (!MatchesMethod.IsMatch(code))
        {
            violations.Add("no `public static bool Matches(` method was found (the guard would be vacuous)");
        }

        if (!FixedTimeReturn.IsMatch(code))
        {
            violations.Add("Matches does not return through CryptographicOperations.FixedTimeEquals");
        }

        violations.AddRange(Forbidden.Where(f => f.Pattern.IsMatch(code)).Select(f => "uses " + f.Why));
        return violations;
    }

    [Fact(DisplayName = "Task 185: InviteCode.Matches compares digests with CryptographicOperations.FixedTimeEquals only")]
    public void InviteCodeMatches_UsesFixedTimeEquals_AndNoEarlyExitComparison()
    {
        var code = SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, InviteCodeFile)));

        var violations = Violations(code);

        Assert.True(violations.Count == 0,
            $"{InviteCodeFile}: {string.Join("; ", violations)}. The invite code is the only proof that a CIAM sign-in "
            + "came from this environment's own invite email (task 185). Compare the 32-byte digests with "
            + "CryptographicOperations.FixedTimeEquals (ADR-028 / auth.md, as for API keys), never with an early-exit compare.");
    }

    // ── Controls ──────────────────────────────────────────────────────────────────────────────────────

    private const string Sanctioned = """
        internal static class InviteCode
        {
            public static bool Matches(string? storedValue, string? presentedCode, Guid contactId)
            {
                if (!storedValue.StartsWith("v1.", StringComparison.Ordinal)) return false;
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
        }
        """;

    [Fact(DisplayName = "Task 185 guard, must-not-fire control: the sanctioned FixedTimeEquals shape passes")]
    public void Control_TheSanctionedShape_HasNoViolation()
        => Assert.Empty(Violations(Sanctioned));

    [Fact(DisplayName = "Task 185 guard, must-not-fire control: a comment mentioning SequenceEqual is ignored")]
    public void Control_ACommentMentioningAForbiddenCall_HasNoViolation()
        => Assert.Empty(Violations(SourceScan.CodeText(
            (Sanctioned + "\n// never use SequenceEqual( or .Equals( here\n").Split('\n'))));

    [Theory(DisplayName = "Task 185 guard, must-fire control: each early-exit comparison is caught")]
    [InlineData("return actual.SequenceEqual(expected);")]
    [InlineData("return actual.SequenceCompareTo(expected) == 0;")]
    [InlineData("return StructuralComparisons.StructuralEqualityComparer.Equals(a, b);")]
    [InlineData("return string.Equals(Convert.ToBase64String(actual), digestText);")]
    [InlineData("return Base64Url.EncodeToString(actual).Equals(digestText);")]
    public void Control_AnEarlyExitComparison_IsCaught(string statement)
    {
        var source = Sanctioned.Replace(
            "return CryptographicOperations.FixedTimeEquals(actual, expected);", statement, StringComparison.Ordinal);

        Assert.NotEmpty(Violations(source));
    }

    [Fact(DisplayName = "Task 185 guard, must-fire control: a Matches with no FixedTimeEquals return is caught")]
    public void Control_NoFixedTimeReturn_IsCaught()
    {
        var source = Sanctioned.Replace(
            "return CryptographicOperations.FixedTimeEquals(actual, expected);", "return actual == expected;",
            StringComparison.Ordinal);

        Assert.Contains(Violations(source), v => v.Contains("FixedTimeEquals", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 185 guard, must-fire control: a file with no Matches method is caught, never vacuously green")]
    public void Control_NoMatchesMethod_IsCaught()
        => Assert.Contains(Violations("internal static class InviteCode { }"), v => v.Contains("vacuous", StringComparison.Ordinal));
}
