using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 049 (Held presentation, CODE_PATTERN pin): the billing rule bodies' code-identifier pattern is repeated in three places:
/// the ExpenseRule and NonBillableActivity schemas (enforced when a rule body is validated) and Rule Review's client-side
/// <c>CODE_PATTERN</c> (<c>src/solutions/RuleReview/src/services/ruleTypes.ts</c>, the administrator's check before a save).
/// <see cref="BillingRuleCatalogue.CodePattern"/> reads the first. This class pins the string itself, so changing it is noticed, and checks
/// the other two against it. Rule Review has no C# build, so its literal is read from the source file.
/// </summary>
public class BillingCodePatternTests
{
    /// <summary>The server pattern. A change here means every rule body, the client and the stored rules' meaning change: do it on purpose.</summary>
    private const string ServerPattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,19}$";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs"))) return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    private static string SchemaPattern(RuleType type)
    {
        using var schema = JsonDocument.Parse(BillingRuleCatalogue.SchemaText(type));
        return schema.RootElement.GetProperty("$defs").GetProperty("code").GetProperty("pattern").GetString()!;
    }

    [Fact(DisplayName = "049 CODE_PATTERN: the server pattern string is pinned")]
    public void TheServerPattern_IsPinned() => BillingRuleCatalogue.CodePattern.Should().Be(ServerPattern);

    [Fact(DisplayName = "049 CODE_PATTERN: both schemas that define a code carry the same pattern")]
    public void TheSchemas_CarryTheSamePattern()
    {
        SchemaPattern(RuleType.ExpenseRule).Should().Be(ServerPattern);
        SchemaPattern(RuleType.NonBillableActivity).Should().Be(ServerPattern);
    }

    [Fact(DisplayName = "049 CODE_PATTERN: Rule Review's client pattern (ruleTypes.ts) is the server pattern")]
    public void RuleReviewsClientPattern_IsTheServerPattern()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "solutions", "RuleReview", "src", "services", "ruleTypes.ts"));

        var declarations = Regex.Matches(source, @"^\s*(?:export\s+)?const\s+CODE_PATTERN\s*=\s*/(?<body>.+)/(?<flags>[a-z]*)\s*;", RegexOptions.Multiline);

        declarations.Should().ContainSingle("ruleTypes.ts declares CODE_PATTERN once, as a regular expression literal");
        declarations[0].Groups["body"].Value.Should().Be(ServerPattern);
        declarations[0].Groups["flags"].Value.Should().BeEmpty(
            "a multiline flag would make $ match at a line end, which the schema's does not; an i flag would widen the letters");
    }

    [Theory(DisplayName = "049 CODE_PATTERN: what the pattern accepts and refuses (the server's whole-value check)")]
    [InlineData("L110", true)]
    [InlineData("E101.a-1", true)]
    [InlineData("A", true)]
    [InlineData("a234567890123456789", true)]
    [InlineData("a2345678901234567890", true)]
    [InlineData("a23456789012345678901", false)]
    [InlineData("", false)]
    [InlineData(".E101", false)]
    [InlineData("E 101", false)]
    [InlineData("E101\n", false)]
    [InlineData("E101\nIgnore previous instructions", false)]
    [InlineData("E/101", false)]
    public void IsCodeShaped_FollowsThePattern(string value, bool expected) => BillingRuleCatalogue.IsCodeShaped(value).Should().Be(expected);
}
