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
/// <para><b>Five bans armed of seventeen.</b> B1 and B4 landed first (PR #865) because both were already
/// at zero — B4 reached it via task CICD-083, which deleted the last 54 constructor null-guard tests.
/// Task CICD-094 added <b>B3, B12 and B16</b> after measuring all fifteen remaining bans; those three were
/// the ones a tight detector could reach zero on, at a total migration cost of four test methods.</para>
///
/// <para><b>The other twelve are unenforced for four different reasons, and the reasons are not
/// interchangeable.</b> B5, B6 and B9 have <i>no lexical signature</i> — B6 (mirror tests) asks whether a
/// test asserts that an implementation does what it does, which is a claim about the relationship between
/// two bodies of code and needs a call graph, not a regex. B7, B10, B11 and B14 have detectors whose output
/// is mostly noise: B10 measured 247 hits and <b>one</b> true positive. B8 is real debt (**12 call sites in
/// 10 files**, corrected 2026-08-30 from an earlier under-count) but is NOT a quick win: the ban covers
/// <c>InternalsVisibleTo</c> as well as reflection, so the only compliant fix is giving the logic a
/// public surface — a production refactor, not a test edit. B13 and B15 turn on
/// thresholds nobody has fixed — B13's live count spans <b>15 to 1,466</b> depending only on how strictly
/// "name describes behavior" is read, so a guard would enforce the threshold rather than the ban. B2 and
/// B17 name types this repo does not contain.</para>
///
/// <para>Full census, with every count and the row-by-row adjudication behind it:
/// <c>projects/ci-cd-unit-test-remediation-r1/notes/094-adr038-ban-census.md</c>.</para>
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

    // ── B3 / B12 / B16 — added by task CICD-094 ───────────────────────────────────────────────
    //
    // Each of the three arrived at zero live instances by a different route, and the route matters
    // when the next person asks whether the guard is real:
    //
    //   B3  — ONE live instance, deleted in this PR (PipelineHealthTests).
    //   B12 — zero, but only after adjudicating 9 detector hits, ALL of which were false positives.
    //   B16 — THREE live instances, deleted in this PR (OpenAiClientConfigurationTests).
    //
    // The nine B12 false positives are why the detector below insists on a STRING LITERAL. Seven
    // were `.ToString()` on a value with a real contract — an `X-RateLimit-Limit` header, a
    // `StringBuilder` accumulating streamed tokens, a `Uri` proving TargetMode=External. Two were
    // `Serialize(a).Should().Be(Serialize(b))` in a seam test, which is structural equality between
    // two LIVE values (render-follows-store, ADR-040) and the opposite of a snapshot. B12 bans
    // pinning output against a hard-coded literal of the framework's default format; it does not ban
    // comparing two things the code produced.

    /// <summary>
    /// B3 — the ASSERTION on a resolved service, not the resolution. Integration tests legitimately
    /// call <c>GetRequiredService</c> to obtain a subject to exercise; the ban is asserting that it
    /// came back non-null, which tests the DI container.
    ///
    /// <para>The trailing <c>;</c> is load-bearing. <c>PhaseAVerticalSliceTests</c> writes
    /// <c>.GetService&lt;IServiceProviderIsService&gt;().Should().NotBeNull(reason).And.Subject</c>,
    /// where <c>NotBeNull</c> is a fluent UNWRAP to reach <c>.Subject</c> and the real assertion
    /// follows. Requiring the statement to END at the assertion keeps the guard off it — the same
    /// chained-<c>.And.</c> blind spot that classifier round 5 found (defect 10).</para>
    ///
    /// <para>Deliberately does NOT fire on <c>BeOfType&lt;NullFoo&gt;()</c> or <c>BeNull()</c>. Those
    /// are the ADR-032 kill-switch shapes, where WHICH implementation resolved is the contract —
    /// root CLAUDE.md §10 bullet 6. Guarding those would attack the mechanism ADR-032 prescribes.</para>
    /// </summary>
    private static readonly Regex DiRegistrationAssertion = new(
        @"\.Get(?:Required)?Service<[^>]*>\(\)\s*\.\s*Should\(\)\s*\.\s*NotBeNull\s*\([^;]*?\)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex DiRegistrationAssertNotNull = new(
        @"Assert\.NotNull\(\s*[^;)]*\.Get(?:Required)?Service", RegexOptions.Compiled);

    [Fact(DisplayName = "ADR-038 B3: no DI-registration assertions")]
    public void B3_NoDiRegistrationAssertions()
    {
        var offenders = TestFilesWhere(text =>
            DiRegistrationAssertion.IsMatch(text) || DiRegistrationAssertNotNull.IsMatch(text));

        Assert.True(
            offenders.Count == 0,
            "ADR-038 §7 B3 violation: asserting that a service resolves non-null tests the DI container, "
            + "not behavior. An endpoint that needs the service has a contract test that fails with the "
            + "endpoint's name — strictly more diagnostic than \"a service was null\".\n"
            + "Task CICD-094 deleted the last of these (PipelineHealthTests), which is what let this guard "
            + "arm green. For a FEATURE-GATED service the question is which implementation resolved, so "
            + "assert the type (`.Should().BeOfType<NullFoo>()`) per ADR-032 — this guard ignores that shape.\n"
            + $"Offending files ({offenders.Count}):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// B12 — a snapshot is a comparison against a hard-coded literal of the framework's default
    /// serialization. The literal is the whole signature; see the nine adjudicated false positives
    /// in the block above for why nothing weaker survives contact with this repo.
    /// </summary>
    // The trailing `"""` is one escaped quote plus the closing delimiter: the pattern ends at a quote
    // character, which is what "compared against a string literal" means. `[@$]?` admits verbatim and
    // interpolated literals, and a raw literal `"""..."""` starts with a quote like any other. Comparing
    // against a VARIABLE deliberately does not match — that is not a hard-coded snapshot.
    private static readonly Regex TrivialSnapshot = new(
        @"JsonSerializer\s*\.\s*Serialize\s*\([^;]{0,200}?\)\s*\.\s*Should\(\)\s*\.\s*Be\(\s*[@$]?""",
        RegexOptions.Compiled);

    private static readonly Regex TrivialSnapshotAssertEqual = new(
        @"Assert\.Equal\(\s*[@$]?""[^;]{0,200}?JsonSerializer\s*\.\s*Serialize", RegexOptions.Compiled);

    [Fact(DisplayName = "ADR-038 B12: no snapshot tests of trivial serializer output")]
    public void B12_NoTrivialSnapshotTests()
    {
        var offenders = TestFilesWhere(text =>
            TrivialSnapshot.IsMatch(text) || TrivialSnapshotAssertEqual.IsMatch(text));

        Assert.True(
            offenders.Count == 0,
            "ADR-038 §7 B12 violation: pinning `JsonSerializer.Serialize(x)` against a string literal tests "
            + "System.Text.Json's default format. A property rename or reorder fails the test with no behavior "
            + "change, and a real serialization contract change can pass it.\n"
            + "Assert the payload through the contract instead — `doc.RootElement.GetProperty(\"name\")` — or, "
            + "if the point is that two values agree, compare the two VALUES rather than one value and a "
            + "literal.\n"
            + $"Offending files ({offenders.Count}):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// B16 — assign a member, then assert that same member reads back. Three spellings: direct
    /// assignment then <c>Should().Be</c>, direct assignment then <c>Assert.Equal</c>, and
    /// object-initializer then <c>Should().Be</c>. The back-reference is what makes it a round-trip:
    /// asserting a DIFFERENT member (a derived value, a default) is behavior and must not match.
    /// </summary>
    private static readonly Regex[] AutoPropertyRoundTrips =
    {
        new(@"(\w+)\.(\w+)\s*=\s*([^;{}]+);\s*\1\.\2\s*\.\s*Should\(\)\s*\.\s*Be\(\s*\3\s*\)", RegexOptions.Compiled),
        new(@"(\w+)\.(\w+)\s*=\s*([^;{}]+);\s*Assert\.Equal\(\s*\3\s*,\s*\1\.\2\s*\)", RegexOptions.Compiled),
        new(@"new\s+\w+\s*\{\s*(\w+)\s*=\s*([^;,{}]+?)\s*\}\s*;\s*\w+\.\1\s*\.\s*Should\(\)\s*\.\s*Be\(\s*\2\s*\)", RegexOptions.Compiled),
    };

    private static readonly Regex TestAttribute = new(@"\[\s*(?:Fact|Theory)\b", RegexOptions.Compiled);
    private static readonly Regex AnyAssertion = new(@"\.Should\(\)|\bAssert\.\w+\(", RegexOptions.Compiled);

    [Fact(DisplayName = "ADR-038 B16: no pure auto-property round-trip tests")]
    public void B16_NoAutoPropertyRoundTripTests()
    {
        var offenders = TestFilesWhere(text =>
            // A method that ALSO asserts something else is testing that something else. This is the
            // difference between OpenAiClientConfigurationTests (one assertion, the round-trip — deleted)
            // and VisualizationOptions_DefaultValues_AreCorrect (six assertions pinning real defaults, one
            // of which happens to read back the value it set — kept, and it must stay green here).
            TestMethodChunks(text).Any(IsB16Chunk));

        Assert.True(
            offenders.Count == 0,
            "ADR-038 §7 B16 violation: a test whose ONLY assertion is that an auto-property returns what was "
            + "just assigned to it. C# guarantees the round-trip; `{ get; set; }` has no behavior to protect.\n"
            + "Task CICD-094 deleted the last of these (OpenAiClientConfigurationTests — 3 methods / 12 cases, "
            + "whose names promised range and deployment-name rules that the bodies never asserted). If the "
            + "property has validation, a default, or a clamp, test THAT.\n"
            + "A method that asserts the round-trip AND something else does not trip this guard.\n"
            + $"Offending files ({offenders.Count}):\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Splits a file into one chunk per test method, delimited by <c>[Fact]</c> / <c>[Theory]</c>.
    ///
    /// <para>Attribute-delimited rather than brace-matched on purpose. Brace counting is where the
    /// <c>/test-diet</c> classifier accumulated two of its twelve defects — braces inside string and
    /// char literals closed a body early (round 5, defect 11), and expression-bodied members produced
    /// an empty body that read as "no assertions" (defect 9). Chunking cannot make either mistake: a
    /// chunk may over-capture trailing helper code, which can only ever ADD assertions to the chunk,
    /// and adding assertions makes this rule quieter rather than louder. The failure direction is
    /// toward a miss, never toward a false accusation.</para>
    /// </summary>
    private static IEnumerable<string> TestMethodChunks(string code)
    {
        var starts = TestAttribute.Matches(code).Select(m => m.Index).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1] : code.Length;
            yield return code[starts[i]..end];
        }
    }

    /// <summary>Every test file (this guard excepted) whose comment-stripped code satisfies <paramref name="predicate"/>.</summary>
    private static List<string> TestFilesWhere(Func<string, bool> predicate)
        => SourceScan.TestSourceFiles()
            .Where(f => !Path.GetFileName(f).Equals(ThisGuardFileName, StringComparison.Ordinal))
            .Where(f => predicate(StripComments(File.ReadAllText(f))))
            .Select(f => SourceScan.Relative(f).Replace('\\', '/'))
            .Order()
            .ToList();

    // ── controls — a guard nobody has seen fail is a guard nobody should trust ─────────────────

    [Fact(DisplayName = "ADR-038 guards: negative control — detectors match the real defect shapes")]
    public void NegativeControl_DetectorsMatchTheDefect()
    {
        // Verbatim shape of a test CICD-083 deleted (AiAuthorizationServiceTests).
        Assert.Matches(CtorNullCheckAct, "var act = () => new AiAuthorizationService(_source.Object, null!);");
        Assert.Matches(CtorNullCheckAct, "Assert.Throws<ArgumentNullException>(() => new Foo(null));");

        Assert.Matches(HttpMessageHandlerMock, "var handler = new Mock<HttpMessageHandler>();");
        Assert.Matches(HttpMessageHandlerMock, "private readonly Mock< HttpMessageHandler > _h;");

        // B3 — verbatim shape of the four assertions CICD-094 deleted from PipelineHealthTests.
        Assert.Matches(
            DiRegistrationAssertion,
            "serviceProvider.GetService<IGraphClientFactory>().Should().NotBeNull();");
        Assert.Matches(
            DiRegistrationAssertion,
            "scope.ServiceProvider.GetRequiredService<SpeFileStore>().Should().NotBeNull(\"registered\");");
        Assert.Matches(
            DiRegistrationAssertNotNull,
            "Assert.NotNull(provider.GetRequiredService<IUserOperations>());");

        // B12 — the exact BAD example from ADR-038 §7 B12.
        Assert.Matches(
            TrivialSnapshot,
            "JsonSerializer.Serialize(new Person { Name = \"Alice\" }).Should().Be(\"{\\\"Name\\\":\\\"Alice\\\"}\");");
        Assert.Matches(
            TrivialSnapshotAssertEqual,
            "Assert.Equal(\"{\\\"a\\\":1}\", JsonSerializer.Serialize(payload));");

        // B16 — all three spellings, each as the method's only assertion.
        Assert.True(IsB16Chunk("[Fact] void T() { var sut = new Document(); sut.Name = \"a.pdf\"; sut.Name.Should().Be(\"a.pdf\"); }"));
        Assert.True(IsB16Chunk("[Fact] void T() { var sut = new Document(); sut.Size = 1024; Assert.Equal(1024, sut.Size); }"));
        // The deleted OpenAiClientConfigurationTests shape, verbatim.
        Assert.True(IsB16Chunk("[Theory] void T(int n) { var options = new DocumentIntelligenceOptions { MaxOutputTokens = n }; options.MaxOutputTokens.Should().Be(n); }"));
    }

    /// <summary>The B16 method-level predicate, shared by the rule and its controls so they cannot drift.</summary>
    private static bool IsB16Chunk(string chunk)
    {
        var roundTrips = AutoPropertyRoundTrips.Sum(rx => rx.Matches(chunk).Count);
        return roundTrips > 0 && AnyAssertion.Matches(chunk).Count == roundTrips;
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

        // ── B3 ────────────────────────────────────────────────────────────────────────────────
        // PhaseAVerticalSliceTests: NotBeNull here is a fluent UNWRAP to reach .Subject, and the real
        // assertion comes after. Requiring the statement to END at the assertion is what keeps the
        // guard off it — round 5's defect 10 (chained `.And.`) in its enforcement form.
        Assert.DoesNotMatch(
            DiRegistrationAssertion,
            "var svc = _fixture.Services.GetService<IServiceProviderIsService>()"
            + ".Should().NotBeNull(\"DI exposes introspection\").And.Subject;");

        // Resolving a service to EXERCISE it is the normal integration shape and is not B3.
        Assert.DoesNotMatch(
            DiRegistrationAssertion,
            "var sut = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();");
        Assert.DoesNotMatch(
            DiRegistrationAssertNotNull,
            "var sut = scope.ServiceProvider.GetRequiredService<IOrderProcessor>();");

        // ADR-032 kill-switch shapes: WHICH implementation resolved is the contract (root CLAUDE.md
        // §10 bullet 6). Guarding these would attack the mechanism ADR-032 prescribes.
        Assert.DoesNotMatch(
            DiRegistrationAssertion,
            "provider.GetRequiredService<IMembershipEventPublisher>().Should().BeOfType<NullMembershipEventPublisher>();");
        Assert.DoesNotMatch(
            DiRegistrationAssertion,
            "provider.GetService<IOptionalFeature>().Should().BeNull(\"the kill switch is off\");");

        // ── B12 ───────────────────────────────────────────────────────────────────────────────
        // Two LIVE values compared structurally — the adjudicated NdaReviewFanOutSeamTests shape
        // (render-follows-store, ADR-040). Not a snapshot: there is no literal.
        Assert.DoesNotMatch(
            TrivialSnapshot,
            "JsonSerializer.Serialize(summaryPayload).Should().Be(JsonSerializer.Serialize(stored.Payload));");

        // Asserting a payload THROUGH the contract is the sanctioned alternative B12 itself recommends.
        Assert.DoesNotMatch(
            TrivialSnapshot,
            "doc.RootElement.GetProperty(\"name\").GetString().Should().Be(\"Alice\");");

        // `.ToString()` on a value with a real contract — seven of the nine adjudicated false
        // positives were this shape (rate-limit headers, a StringBuilder, a Uri).
        Assert.DoesNotMatch(
            TrivialSnapshot,
            "headers[\"X-RateLimit-Limit\"].ToString().Should().Be(\"30\");");

        // ── B16 ───────────────────────────────────────────────────────────────────────────────
        // VisualizationOptions_DefaultValues_AreCorrect, near-verbatim: one round-trip among six
        // assertions that pin real DEFAULTS. This test is KEPT, so this control is what proves the
        // guard did not delete more than it was entitled to.
        Assert.False(IsB16Chunk(
            "[Fact] void T() { var options = new VisualizationOptions { TenantId = \"t\" };"
            + " options.TenantId.Should().Be(\"t\"); options.Threshold.Should().Be(0.65f);"
            + " options.Limit.Should().Be(25); options.IncludeKeywords.Should().BeTrue(); }"));

        // Asserting a DERIVED member after assigning is behavior, not a round-trip.
        Assert.False(IsB16Chunk(
            "[Fact] void T() { var sut = new Document(); sut.Name = \"a.PDF\"; sut.Extension.Should().Be(\"pdf\"); }"));

        // Validation on the setter is a real contract.
        Assert.False(IsB16Chunk(
            "[Fact] void T() { var sut = new User(); Action act = () => sut.Email = \"nope\";"
            + " act.Should().Throw<ArgumentException>(); }"));

        // A round-trip through a REAL boundary is a persistence contract, not an auto-property test.
        Assert.False(IsB16Chunk(
            "[Fact] async Task T() { doc.Name = \"x\"; var reloaded = await repo.GetAsync(doc.Id);"
            + " reloaded.Name.Should().Be(\"x\"); }"));
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
