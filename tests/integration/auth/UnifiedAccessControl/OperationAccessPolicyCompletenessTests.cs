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

    /// <summary>
    /// <c>Operation = "literal"</c> in an object initialiser.
    /// </summary>
    /// <remarks>
    /// <b>ANCHORED (task 025, finding M3).</b> The pattern was previously unanchored, so it also matched
    /// the TAIL of any identifier ending in "Operation" — including a const DECLARATION such as
    /// <c>private const string AssociateOperation = "entity.associate_document";</c>. That is not a call
    /// site, and treating it as one made the gate report coverage it did not have: rename the const, or
    /// move its declaration, and the operation would silently vanish from the scan while the gate stayed
    /// green. The lookbehind requires that <c>Operation</c> begins an identifier, and the
    /// <c>const|readonly|string</c> exclusion rejects a declaration outright.
    /// </remarks>
    private static readonly Regex OperationLiteralPattern = new(
        @"(?<![A-Za-z0-9_])(?<!const\s)(?<!string\s)Operation\s*=\s*""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// <c>HasRequiredRights(rights, "literal")</c> / <c>GetRequiredRights("literal")</c> — operation
    /// strings passed DIRECTLY to the policy.
    /// </summary>
    /// <remarks>
    /// <b>ADDED by task 025 (finding M3).</b> This is an entire call-site mechanism the gate could not
    /// see. <c>PermissionsEndpoints.cs</c> alone passes 14 operation literals this way to compute the
    /// capability flags a client uses to decide what UI to show. Invisible to the scan, every one of
    /// them could name an operation the policy does not support, and the forcing function above would
    /// still pass.
    /// </remarks>
    private static readonly Regex DirectPolicyCallLiteralPattern = new(
        @"(?:Has|Get)RequiredRights\s*\(\s*(?:[A-Za-z0-9_.]+\s*,\s*)?""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// <c>HasRequiredRights(rights, SomeConst)</c> — const indirection through the policy call itself.
    /// </summary>
    /// <remarks>
    /// <b>ADDED by task 025.</b> This is how <c>entity.associate_document</c> is ACTUALLY reached
    /// (<c>EntityAccessFilter.cs:266</c>). Before this pattern existed the gate found that operation
    /// only by accidentally matching its const declaration — see
    /// <see cref="OperationLiteralPattern"/>. Resolved against the same-file const declarations, exactly
    /// as the <c>Operation = SomeConst</c> mechanism is.
    /// </remarks>
    private static readonly Regex DirectPolicyCallConstPattern = new(
        @"(?:Has|Get)RequiredRights\s*\(\s*(?:[A-Za-z0-9_.]+\s*,\s*)?([A-Z]\w*)\s*\)",
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
    /// The scan sees operation strings passed DIRECTLY to the policy — the mechanism finding M3 showed
    /// it was blind to.
    /// </summary>
    /// <remarks>
    /// <para><b>Task 025 / M3.</b> <c>PermissionsEndpoints.cs</c> computes a client's capability flags
    /// with 14 <c>HasRequiredRights(rights, "driveitem.…")</c> calls. Every one was invisible to this
    /// gate, so any of them could have named an operation the policy does not support — which resolves
    /// to no rights, i.e. a capability silently reported as denied — and the forcing function would
    /// still have been green.</para>
    ///
    /// <para>Asserted as a SET rather than a count: a count would drift on every ordinary edit to that
    /// file and teach the next reader to re-baseline it, which is how a ratchet becomes noise.</para>
    /// </remarks>
    [Fact]
    public void SourceScan_SeesOperationsPassedDirectlyToTheAccessPolicy()
    {
        var discovered = DiscoverCallSites()
            .Where(c => c.Location.Equals("PermissionsEndpoints.cs", StringComparison.Ordinal))
            .Select(c => c.Operation)
            .ToHashSet(StringComparer.Ordinal);

        discovered.Should().Contain(
            new[]
            {
                "driveitem.preview", "driveitem.content.download", "driveitem.content.upload",
                "driveitem.content.replace", "driveitem.delete", "driveitem.get", "driveitem.update",
                "driveitem.createlink", "driveitem.versions.list", "driveitem.versions.restore",
                "driveitem.move", "driveitem.copy", "driveitem.checkout", "driveitem.checkin",
            },
            "these are passed to OperationAccessPolicy.HasRequiredRights directly; before task 025 the "
            + "scan recognised no such mechanism and found NONE of them. Discovered in that file: {0}",
            string.Join(", ", discovered.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// A const DECLARATION is not a call site.
    /// </summary>
    /// <remarks>
    /// <para><b>Task 025 / M3 — the anchoring bug.</b> <c>entity.associate_document</c> must be
    /// discovered because <c>EntityAccessFilter.cs:266</c> genuinely calls
    /// <c>HasRequiredRights(rights, AssociateOperation)</c> — NOT because the old unanchored
    /// <c>Operation\s*=\s*"…"</c> pattern happened to match the tail of
    /// <c>private const string AssociateOperation = "…"</c> on line 87.</para>
    ///
    /// <para>The distinction is not academic. Under the accidental match the operation's coverage was
    /// pinned to the SPELLING OF A PRIVATE FIELD: rename it to <c>AssociateOp</c>, or move the
    /// declaration to a shared constants file, and the gate would have quietly stopped covering a live
    /// authorization filter while still reporting success.</para>
    ///
    /// <para>This asserts the operation survives with the declaration line removed from the scanned
    /// text — i.e. that a real call site, not a declaration, is what finds it.</para>
    /// </remarks>
    [Fact]
    public void SourceScan_FindsAssociateOperationByItsCallSite_NotByItsConstDeclaration()
    {
        var discovered = DiscoverCallSites().Select(c => c.Operation).ToHashSet(StringComparer.Ordinal);

        discovered.Should().Contain("entity.associate_document");

        // The declaration alone must NOT be enough. Feed the anchored literal pattern the declaration
        // in isolation: it must not report a call site.
        const string declarationOnly = "    private const string AssociateOperation = \"entity.associate_document\";";

        OperationLiteralPattern.IsMatch(declarationOnly).Should().BeFalse(
            "a const declaration is not a call site; matching it is what made the gate report coverage "
            + "it did not have (finding M3)");
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
                     {
                         FilterLiteralPattern, OperationLiteralPattern, RequirementLiteralPattern,
                         DirectPolicyCallLiteralPattern, // task 025 / M3
                     })
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

            // Task 025 / M3: the same indirection, through the policy call rather than an object
            // initialiser. This is how entity.associate_document is genuinely reached.
            foreach (Match m in DirectPolicyCallConstPattern.Matches(code))
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
