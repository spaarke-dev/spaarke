using System.Text.RegularExpressions;
using FluentAssertions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Forcing function for spec FR-03: every operation string a live authorization filter passes to
/// <see cref="OperationAccessPolicy"/> MUST resolve there.
///
/// Why a source scan rather than a fixed list: <c>OperationAccessRule.EvaluateAsync:35-46</c> denies
/// any unregistered operation as <c>unknown_operation</c>. That is correct fail-closed design, but it
/// means a new call-site with an unregistered string produces a **silent unconditional 403** in
/// production — no compile error, no startup failure, no test failure. Findings A-3 and A-20 were
/// found by hand-enumerating call-sites; this test is what stops that from being necessary again.
/// A snapshot of today's strings would not do that, so this scans the source tree and asserts on
/// whatever it finds.
///
/// Three call-site mechanisms are covered, because the four findings used all three:
///   1. literal passed to an <c>Add*AuthorizationFilter("op")</c> / <c>Add*AccessFilter("op")</c>
///   2. literal assigned to <c>Operation = "op"</c> on an AuthorizationContext
///   3. <c>Operation = SomeConst</c> where the const is declared in the same file
///      (this is how <c>entity.associate_document</c> reaches the rule — a scan that missed
///      const-indirection would have silently dropped that finding)
///
/// Scope note: the AI filters (<c>AiAuthorizationFilter</c>, <c>AnalysisAuthorizationFilter</c>,
/// <c>VisualizationAuthorizationFilter</c>) route through <c>IAiAuthorizationService</c>, which checks
/// <c>AccessRights.Read</c> directly and never consults this policy — so they are correctly out of
/// scope. <c>DataverseAuthorizationFilter</c> likewise uses <c>IDataversePrivilegeChecker</c>.
/// </summary>
public class OperationAccessPolicyCompletenessTests
{
    /// <summary>The API tree scanned for call-sites, relative to the repo root.</summary>
    private const string ApiTreeRelativePath = "src/server/api/Sprk.Bff.Api";

    /// <summary>
    /// Lower bound on discovered call-sites. Guards against the scan silently finding nothing
    /// (moved directory, changed layout, test run from an unexpected working directory) and thereby
    /// passing vacuously — the failure mode that would quietly retire this forcing function.
    /// Deliberately well below the current count so ordinary refactors don't trip it.
    /// </summary>
    private const int MinimumExpectedCallSites = 20;

    private static readonly Regex FilterLiteralPattern = new(
        @"Add\w*(?:Authorization|Access)Filter\s*(?:<[^>()]*>)?\s*\(\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex OperationLiteralPattern = new(
        @"Operation\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex RequirementLiteralPattern = new(
        @"ResourceAccessRequirement\s*\(\s*""([^""]+)""",
        RegexOptions.Compiled);

    private static readonly Regex OperationConstReferencePattern = new(
        @"Operation\s*=\s*([A-Z]\w*)\s*,",
        RegexOptions.Compiled);

    private static readonly Regex ConstDeclarationPattern = new(
        @"const\s+string\s+(\w+)\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    // ─────────────────────────────────────────────────────────────────────────────
    // The forcing function
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryAuthorizationFilterOperationString_ResolvesInOperationAccessPolicy()
    {
        // Arrange
        var callSites = DiscoverCallSites();

        callSites.Should().NotBeEmpty("the source scan must find call-sites — see MinimumExpectedCallSites");
        callSites.Count.Should().BeGreaterThanOrEqualTo(MinimumExpectedCallSites,
            "the scan found only {0} call-sites, which suggests it is no longer reaching the API tree " +
            "at '{1}'. A scan that finds nothing passes vacuously and silently retires this gate — fix " +
            "the scan rather than lowering the bound.", callSites.Count, ApiTreeRelativePath);

        // Act — which discovered operations does the policy not know?
        var unregistered = callSites
            .Where(c => !OperationAccessPolicy.IsOperationSupported(c.Operation))
            .ToList();

        // Assert
        unregistered.Should().BeEmpty(
            "every operation string reaching OperationAccessRule must resolve in OperationAccessPolicy, " +
            "or that call-site returns 403 for EVERY caller regardless of rights (findings A-3/A-20). " +
            "Unregistered: {0}",
            string.Join(" · ", unregistered.Select(u => $"\"{u.Operation}\" at {u.Location}")));
    }

    /// <summary>
    /// Guards the SCAN itself, not the policy. The forcing function above only means something if the
    /// scan actually reaches every call-site mechanism in use; if a regex silently stopped matching,
    /// the scan would find fewer operations and still pass. This asserts the scan discovers each of
    /// the four A-20 strings, which between them exercise all three mechanisms:
    ///   "read"                      → literal in Add*AuthorizationFilter("…")
    ///   "finance.read"/"…confirm"   → literal in Add*AuthorizationFilter("…")
    ///   "entity.associate_document" → CONST-INDIRECTION (Operation = AssociateOperation)
    /// The const case is the fragile one and the reason this test exists.
    /// </summary>
    [Theory]
    [InlineData("read")]
    [InlineData("finance.read")]
    [InlineData("finance.confirm")]
    [InlineData("entity.associate_document")]
    public void SourceScan_DiscoversKnownCallSiteOperation(string operation)
    {
        var discovered = DiscoverCallSites().Select(c => c.Operation).ToHashSet(StringComparer.Ordinal);

        discovered.Should().Contain(operation,
            "the scan must reach this call-site or the forcing function above silently stops covering " +
            "it. Discovered {0} distinct operations: {1}",
            discovered.Count, string.Join(", ", discovered.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Closed-list guard for the four strings findings A-3 / A-20 identified. The scan above is the
    /// general forcing function; this pins the specific regression so it cannot recur even if the scan
    /// is later narrowed or a call-site moves to a mechanism the scan does not recognise.
    /// </summary>
    [Theory]
    [InlineData("read", AccessRights.Read)]
    [InlineData("finance.read", AccessRights.Read)]
    [InlineData("finance.confirm", AccessRights.Write)]
    [InlineData("entity.associate_document", AccessRights.AppendTo)]
    public void RegressionA3A20_Operation_ResolvesWithLeastPrivilegeRights(
        string operation, AccessRights expected)
    {
        OperationAccessPolicy.IsOperationSupported(operation).Should().BeTrue(
            "\"{0}\" is passed by a live authorization filter; if it does not resolve, that filter " +
            "denies every caller (findings A-3/A-20)", operation);

        OperationAccessPolicy.GetRequiredRights(operation).Should().Be(expected,
            "task 003 chose {0} for \"{1}\" on least-privilege grounds against the resource the filter " +
            "actually authorizes — see the inline rationale in OperationAccessPolicy.cs", expected, operation);
    }

    /// <summary>
    /// The four operations must NOT have been registered by widening rights beyond what the resource
    /// needs. Pins least-privilege explicitly so a later "just make it work" edit to Write|Create|Delete
    /// fails rather than silently over-granting.
    /// </summary>
    [Theory]
    [InlineData("read")]
    [InlineData("finance.read")]
    [InlineData("finance.confirm")]
    [InlineData("entity.associate_document")]
    public void RegressionA3A20_Operation_DoesNotRequireDeleteOrShare(string operation)
    {
        var rights = OperationAccessPolicy.GetRequiredRights(operation);

        rights.Should().NotHaveFlag(AccessRights.Delete,
            "none of these operations deletes the authorized resource");
        rights.Should().NotHaveFlag(AccessRights.Share,
            "none of these operations shares the authorized resource");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — fail-closed behaviour this task must NOT weaken (ADR-003 / NFR-01)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsOperationSupported_ForUnregisteredOperation_StillReturnsFalse()
    {
        // Registering the four keys must not have turned the policy permissive.
        OperationAccessPolicy.IsOperationSupported("definitely.not.a.real.operation").Should().BeFalse();
        OperationAccessPolicy.IsOperationSupported("finance.reject").Should().BeFalse(
            "\"finance.reject\" appears only in a FinanceAuthorizationFilter doc-comment example; the " +
            "reject route actually uses \"finance.confirm\". Registering unused strings is not the fix");
    }

    [Fact]
    public void GetRequiredRights_ForUnregisteredOperation_Throws()
    {
        var act = () => OperationAccessPolicy.GetRequiredRights("definitely.not.a.real.operation");

        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Scan implementation
    // ─────────────────────────────────────────────────────────────────────────────

    private readonly record struct CallSite(string Operation, string Location);

    private static List<CallSite> DiscoverCallSites()
    {
        var apiRoot = Path.Combine(ResolveRepoRoot(), ApiTreeRelativePath);

        Directory.Exists(apiRoot).Should().BeTrue(
            "the API source tree must be reachable from the test assembly for this gate to mean " +
            "anything; looked for '{0}'", apiRoot);

        var results = new List<CallSite>();

        foreach (var file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Skip build output — obj/ contains generated copies that would double-count.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var codeLines = File.ReadAllLines(file).Where(IsCodeLine).ToArray();
            var code = string.Join('\n', codeLines);
            var name = Path.GetFileName(file);

            foreach (var pattern in new[]
                     { FilterLiteralPattern, OperationLiteralPattern, RequirementLiteralPattern })
            {
                foreach (Match m in pattern.Matches(code))
                {
                    results.Add(new CallSite(m.Groups[1].Value, name));
                }
            }

            // Const-indirection: Operation = SomeConst, with the const declared in this same file.
            // A Lookup, not a Dictionary: one file may declare the same const name in two nested
            // types (e.g. "selectFields"), and a Dictionary throws on the duplicate. Resolving to
            // ALL candidate literals is the safe direction — over-collecting surfaces as a visible
            // failure to investigate, whereas dropping one silently loses coverage.
            var constsInFile = ConstDeclarationPattern.Matches(code)
                .ToLookup(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);

            foreach (Match m in OperationConstReferencePattern.Matches(code))
            {
                foreach (var literal in constsInFile[m.Groups[1].Value])
                {
                    results.Add(new CallSite(literal, name));
                }
            }
        }

        return results.DistinctBy(r => $"{r.Operation}|{r.Location}").ToList();
    }

    /// <summary>
    /// Excludes comment lines so documentation EXAMPLES are not mistaken for call-sites — e.g.
    /// FinanceAuthorizationFilter.cs's <c>&lt;param&gt;</c> comment naming "finance.reject", which no
    /// route uses. Line-prefix matching (rather than stripping at the first <c>//</c>) avoids
    /// mangling URLs inside real string literals.
    /// </summary>
    private static bool IsCodeLine(string line)
    {
        var t = line.TrimStart();
        return !(t.StartsWith("//", StringComparison.Ordinal)
                 || t.StartsWith("*", StringComparison.Ordinal)
                 || t.StartsWith("/*", StringComparison.Ordinal));
    }

    /// <summary>
    /// Walks up from the test assembly looking for the repo root (a directory holding both
    /// <c>src</c> and <c>tests</c>). Unlike the ArchTests helper this does NOT fall back to
    /// <c>AppContext.BaseDirectory</c> — a wrong root would make the scan find zero files and pass
    /// vacuously, so it throws instead.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root (a directory containing both 'src' and 'tests') by walking " +
            $"up from '{AppContext.BaseDirectory}'. This gate scans source, so it fails loudly rather " +
            $"than silently scanning nothing.");
    }
}
