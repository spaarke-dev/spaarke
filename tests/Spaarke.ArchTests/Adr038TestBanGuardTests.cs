using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Issue #864 — structural fitness function for the <see href="../../docs/adr/ADR-038-testing-strategy.md">ADR-038 §7</see>
/// build-vs-maintain bans. Owner-directed 2026-08-28: <i>"we do need to ensure ADR-038 is enforced to the
/// extent it is effective at managing the test suite."</i>
/// </summary>
/// <remarks>
/// <para><b>Why this guard exists.</b> The 17 bans (B1–B17) were documented in the ADR, in
/// <c>.claude/constraints/testing.md</c> and in the <c>/test-diet</c> classifier — and <b>nothing failed a
/// build</b>. Every enforcement point was a human or an agent that chose to look, and <c>/test-diet</c>
/// runs only at project close, only over that project's own deltas.</para>
///
/// <para><b>Two bans, both hard, both arming green.</b> B4 and B1 were chosen because their live count is
/// zero, so the guard costs nothing to adopt and prevents each category from ever returning. B4 reached
/// zero via task CICD-083, which deleted the last 54 constructor null-guard tests. B1 was already at zero.
/// The other 15 bans need either judgement (B6 mirror tests cannot be pattern-detected at all) or a
/// migration first (B13 has ~1,124 live instances), and are deliberately out of scope here.</para>
///
/// <para><b>Correction to issue #864, which motivated this file.</b> The issue reports "24 files use the
/// explicitly-banned <c>Mock&lt;HttpMessageHandler&gt;</c>". That number came from a plain grep, and a
/// plain grep matches the token inside <i>comments</i> — including the many files whose header reads
/// <c>"Banned-pattern clean: no Mock&lt;HttpMessageHandler&gt;"</c>. Scanning code with comments stripped,
/// the true count across the whole <c>tests/</c> tree is <b>0</b>; all 78 grep hits were prose. That is
/// the same false positive the <c>/test-diet</c> classifier made in spot-check round 3 (PR #855), where a
/// file documenting its ADR-038 compliance was classified as violating it. Hence
/// <see cref="StripComments"/> here, and hence a hard rule rather than the frozen inventory #864
/// recommended — there is nothing to freeze.</para>
///
/// <para><b>No allowlist. Deliberately.</b> Same reasoning as <see cref="ServiceBusClientGuardTests"/>: an
/// allowlist with zero entries is a census waiting to regrow, and a stale allowlist is room for a
/// regression to hide. If a legitimate need appears, the conversation should happen on the PR, not be
/// pre-authorised by a file.</para>
///
/// <para><b>Not a count ceiling.</b> Learned from ADR-010 this week: that ratchet went 153 → 155 while
/// <b>seven</b> interfaces were added and five removed — the net number hid five additions from review.
/// These rules name the offending file; net-zero churn cannot slip past them.</para>
///
/// <para><b>Armed in the same PR that adds it.</b> Non-negotiable, per #864 and the #839 post-mortem:
/// <c>CredentialGuardTests</c> shipped red while CI reported green for six days because nobody added it to
/// the Tier 1 filter. An unarmed guard is a file that <i>looks</i> like enforcement, which is worse than
/// no guard. The Tier 1 filter entries in <c>.github/workflows/ci-tier1-blocking.yml</c> land with this
/// file.</para>
///
/// <para><b>Known adjacent gap, deliberately NOT guarded here.</b> 13 files subclass
/// <c>HttpMessageHandler</c> / <c>DelegatingHandler</c> directly. That is the same coupling B1 is aimed at,
/// under a different spelling — but it is also the conventional way to exercise an outbound HTTP boundary
/// in a seam test, and ADR-038 B1 names the mock, not the subclass. Guarding it would fail 13 files today
/// on a rule the ADR does not actually state. Reported to #864 for a decision instead of quietly widened.</para>
///
/// <para><b>Crude by design</b>, like the rest of <see cref="SourceScan"/>: this strips comments and
/// pattern-matches; it does not compile. Adequate because each rule is paired with a negative control
/// proving the detector fires on the real defect, and a positive control proving it does not fire on the
/// sanctioned shape.</para>
///
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions", this file is MAINTAIN-class: it is the
/// mechanism, not scaffolding.</para>
/// </remarks>
public class Adr038TestBanGuardTests
{
    /// <summary>B1 — the literal banned token, as ADR-038 §7 states it.</summary>
    private static readonly Regex HttpMessageHandlerMock =
        new(@"Mock<\s*HttpMessageHandler\s*>", RegexOptions.Compiled);

    /// <summary>
    /// B4 — the ACT must be a construction: <c>=&gt; new Foo(..., null!)</c> paired with an
    /// ArgumentNullException expectation.
    ///
    /// <para>Deliberately NOT a bare "ArgumentNullException appears" match. Six rounds of classifier
    /// spot-checks (PRs #855, #862) established that building a DTO with a null field and then calling a
    /// METHOD is a behavioral test, not a constructor guard test. The <c>null</c> match is
    /// case-SENSITIVE so it cannot fire on <c>Null*</c> type names — case-insensitivity is precisely how
    /// the classifier once swept in the entire ADR-032 Null-Object family.</para>
    /// </summary>
    private static readonly Regex CtorNullCheckAct =
        new(@"=>\s*new\s+\w+\s*\([^)]*\bnull\b", RegexOptions.Compiled);

    [Fact(DisplayName = "ADR-038 B4: no constructor null-check tests")]
    public void B4_NoConstructorNullGuardTests()
    {
        var offenders = new List<string>();

        foreach (var file in SourceScan.TestSourceFiles())
        {
            if (Path.GetFileName(file).Equals(ThisGuardFileName, StringComparison.Ordinal))
            {
                continue;   // holds the control fixtures — see ThisGuardFileName
            }

            var text = StripComments(File.ReadAllText(file));

            foreach (Match m in CtorNullCheckAct.Matches(text))
            {
                // The ArgumentNullException expectation must belong to THIS act, so look in a window
                // around the match rather than anywhere in the file.
                var start = Math.Max(0, m.Index - 300);
                var window = text.Substring(start, Math.Min(600, text.Length - start));
                if (window.Contains("ArgumentNullException", StringComparison.Ordinal))
                {
                    offenders.Add(SourceScan.Relative(file).Replace('\\', '/'));
                    break;
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "ADR-038 §7 B4 violation: constructor null-guard tests are banned. `ArgumentNullException.ThrowIfNull` "
            + "in the production constructor is the accepted replacement and is already this codebase's "
            + "convention — these tests assert that a guard clause exists, not that any behavior is correct.\n"
            + "Task CICD-083 removed the last 54 of these, which is what let this guard arm green. Fix or delete "
            + "the test; do not add an allowlist.\n"
            + $"Offending files ({offenders.Distinct().Count()}):\n  "
            + string.Join("\n  ", offenders.Distinct().Order()));
    }

    /// <summary>
    /// This file is the one place the banned tokens must appear in CODE — the controls below hold them
    /// as string literals to prove the detectors fire. Scoping it out is not an allowlist: it is the
    /// same self-exclusion <see cref="ServiceBusClientGuardTests"/> applies to the factory it governs.
    /// Caught on first run — the guard's very first red was itself.
    /// </summary>
    private const string ThisGuardFileName = "Adr038TestBanGuardTests.cs";

    [Fact(DisplayName = "ADR-038 B1: no test mocks HttpMessageHandler")]
    public void B1_NoHttpMessageHandlerMocks()
    {
        var offenders = SourceScan.TestSourceFiles()
            .Where(f => !Path.GetFileName(f).Equals(ThisGuardFileName, StringComparison.Ordinal))
            .Where(f => HttpMessageHandlerMock.IsMatch(StripComments(File.ReadAllText(f))))
            .Select(f => SourceScan.Relative(f).Replace('\\', '/'))
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "ADR-038 §7 B1 violation: `Mock<HttpMessageHandler>` couples the test to the wire format rather "
            + "than to behavior. Use a typed client fake, or exercise the boundary in an "
            + "`tests/integration/seam/**` test.\n"
            + "The whole tests/ tree was at zero when this guard was armed (2026-08-28), so this is a NEW "
            + "regression, not inherited debt.\n"
            + $"Offending files ({offenders.Count}):\n  " + string.Join("\n  ", offenders));
    }

    // ── controls — a guard nobody has seen fail is a guard nobody should trust ─────────────────

    [Fact(DisplayName = "ADR-038 guards: negative control — detectors match the real defect shapes")]
    public void NegativeControl_DetectorsMatchTheDefect()
    {
        // Verbatim shape of a test CICD-083 deleted (AiAuthorizationServiceTests).
        Assert.Matches(CtorNullCheckAct, "var act = () => new AiAuthorizationService(_source.Object, null!);");
        Assert.Matches(CtorNullCheckAct, "Assert.Throws<ArgumentNullException>(() => new Foo(null));");

        Assert.Matches(HttpMessageHandlerMock, "var handler = new Mock<HttpMessageHandler>();");
        Assert.Matches(HttpMessageHandlerMock, "private readonly Mock< HttpMessageHandler > _h;");
    }

    [Fact(DisplayName = "ADR-038 guards: positive control — detectors ignore the sanctioned shapes")]
    public void PositiveControl_DetectorsIgnoreSanctionedShapes()
    {
        // Building a DTO with a null field and then calling a METHOD is behavioral, not B4.
        // This exact shape (EffortScoringServiceTests) was a false positive in classifier round 3.
        Assert.DoesNotMatch(
            CtorNullCheckAct,
            "var input = new EffortScoreInput(null!, false);\nAction act = () => _sut.CalculateEffortScore(input);");

        // Case sensitivity: a Null-Object TYPE NAME must not read as the `null` keyword. PowerShell's
        // case-insensitive `-match` made exactly this mistake and swept in the whole ADR-032 family.
        Assert.DoesNotMatch(
            CtorNullCheckAct,
            "var sut = () => new NullMembershipEventPublisher(Mock.Of<ILogger>());");

        // A comment mentioning a banned token is not a violation. This is not hypothetical: all 78 grep
        // hits for Mock<HttpMessageHandler> in this repo are comments, most of them asserting compliance.
        Assert.DoesNotMatch(
            HttpMessageHandlerMock,
            StripComments("// ADR-038 compliance: NO Mock<HttpMessageHandler> here.\nvar x = 1;"));
        Assert.DoesNotMatch(
            HttpMessageHandlerMock,
            StripComments("/* Banned-pattern clean: no Mock<HttpMessageHandler>. */\nvar y = 2;"));
    }

    [Fact(DisplayName = "ADR-038 guards: the scanner actually sees the test tree")]
    public void Control_ScannerSeesTheTestTree()
    {
        // A rule that scans nothing passes vacuously. #839's lesson was a guard that looked armed and
        // was not; this is the cheap check that the file set is non-empty and includes this very file.
        var files = SourceScan.TestSourceFiles().ToList();

        Assert.True(files.Count > 100, $"Scanner found only {files.Count} test files — the scan root is wrong.");
        Assert.Contains(files, f => f.EndsWith("Adr038TestBanGuardTests.cs", StringComparison.Ordinal));
    }

    /// <summary>Strips block and line comments. A ban token inside prose is not a violation.</summary>
    private static string StripComments(string text)
    {
        var noBlock = Regex.Replace(text, @"/\*[\s\S]*?\*/", " ");
        return Regex.Replace(noBlock, @"(?m)//.*$", string.Empty);
    }
}
