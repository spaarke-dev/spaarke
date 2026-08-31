using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tenant-isolation invariant <b>I6</b> (spec.md FR-40 / design.md §4D I6, added
/// v3.5 2026-08-19 per the auth-v4 §5.4 proposal, adopted — Model 1 only):
/// <b>the app registration used for an OBO (On-Behalf-Of) exchange MUST be
/// derived from per-tenant request context; no default or fallback app
/// registration.</b>
///
/// <para>
/// <b>Severity</b>: HIGH. Under MI-as-FIC (FR-39), Model 1's shared BFF UAMI can
/// mint an assertion for ANY app registration that trusts it — the isolation
/// boundary that was resource-level (BFF reads customer X's secret from customer
/// X's Key Vault) becomes <b>code-level</b>: nothing but correct tenant routing
/// stops the process authenticating as the wrong customer's app-reg (§4D I6
/// rationale). Not CATASTROPHIC only because Model 1's app-reg is a single
/// shared object today, limiting blast radius relative to I2/I4/I5 — still
/// load-bearing and enforced with the same discipline.
/// </para>
///
/// <para>
/// <b>Scan shape</b>: content-scoped file scan (NOT directory-scoped like I5) —
/// every production <c>*.cs</c> under the BFF (<c>src/server/api/Sprk.Bff.Api</c>)
/// and the BFF-consumed shared libraries (<c>src/server/shared</c>) that contains
/// an OBO marker (<c>AcquireTokenOnBehalfOf</c> / <c>OnBehalfOfCredential</c> /
/// <c>UserAssertion</c> / <c>ITokenAcquisition</c>) is an "OBO file" and is
/// subject to the I6 predicates. Content-scoping (rather than a directory or
/// file allowlist) is deliberate: any FUTURE file that introduces an OBO
/// exchange anywhere in the BFF or its shared libs is automatically covered —
/// the test does not need a maintenance edit to see new OBO surface, and it
/// cannot silently lose coverage when files move.
/// </para>
///
/// <para>
/// <b>Predicates applied to each OBO file</b>:
/// <list type="number">
///   <item><b>No hardcoded tenant/app GUID literal</b> — a GUID-shaped value
///     inside a string literal in an OBO file is a hardcoded tenant id, app
///     (client) id, or audience: exactly the "default or fallback app
///     registration" I6 bans. Comment lines (<c>//</c> / <c>///</c> / block-star)
///     are excluded — a GUID in prose is documentation, not a credential
///     binding.</item>
///   <item><b><c>ConfidentialClientApplicationBuilder.Create(x)</c> argument must
///     be a derived expression</b> — <c>x</c> must NOT be empty / <c>null</c> /
///     <c>string.Empty</c> / a string literal, and must NOT contain a
///     null-coalescing fallback to a string literal (<c>expr ?? "…"</c>). The
///     compliant shape is a variable, options/member access, or request-context
///     value (e.g. <c>Create(apiAppId)</c> where <c>apiAppId</c> is
///     required-config-with-throw, <c>Create(config.OwningAppId)</c> per-request
///     BU config, <c>Create(_options.ClientId)</c> options-bound). A literal or
///     literal-fallback argument is a default app-reg baked into code.</item>
///   <item><b>No <c>Graph:TenantId</c> config read in an OBO file</b> —
///     <c>Graph:TenantId</c> is app-only Graph configuration (the service's own
///     tenant for app-only calls). Reading it inside an OBO code path means the
///     app-only tenant is being used as the OBO exchange target instead of the
///     caller's per-request tenant context. App-only (non-OBO) call sites
///     legitimately use configured tenant values — they live in files without
///     OBO markers and are governed by I5, not I6.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Exemption mechanism</b>: a line-level marker comment
/// <c>// I6-exempt: &lt;reason&gt;</c> on the offending line suppresses that
/// line's finding. This is the text-scan analog of attribute-based exemption
/// (preferred over a central allowlist): the exemption is co-located with the
/// code it exempts, carries a mandatory human-readable reason, is visible in
/// the PR diff that introduces it, and cannot drift the way a path list in this
/// test file would. A bare <c>I6-exempt:</c> with no reason does NOT suppress.
/// No production line currently carries the marker.
/// </para>
///
/// <para>
/// <b>Honest scope note (what I6 can assert statically TODAY)</b>: under Model 1
/// there is exactly ONE shared multitenant BFF app-reg (design.md §4.1 H3), so
/// "derived from per-tenant request context" currently reduces to "resolved
/// from required configuration / options / per-request BU config with no
/// literal default and no literal fallback" — which is what these predicates
/// enforce. The stronger form (an explicit, non-defaultable tenant-context
/// parameter on the app-reg RESOLVER seam) becomes statically assertable when
/// auth-v4's FR-39 pluggable credential path lands and introduces that seam;
/// this test's content-scoped discovery will see the new code automatically,
/// and the predicate set should be extended alongside that work.
/// </para>
///
/// <para>
/// <b>Compliant baseline references (the five current OBO files)</b>:
/// <c>Infrastructure/Graph/GraphClientFactory.cs</c> —
/// <c>Create(apiAppId)</c> with <c>configuration["API_APP_ID"] ?? throw</c>
/// (required config, throws — NOT a fallback);
/// <c>Services/SpeAdmin/SpeAdminTokenProvider.cs</c> —
/// <c>Create(config.OwningAppId)</c> per-request BU config (request-context
/// derivation, the strongest current shape);
/// <c>Api/Agent/AgentTokenService.cs</c> — <c>Create(_options.ClientId)</c>
/// options-bound;
/// <c>Services/Ai/Handlers/Dataverse/DataverseUserClient.cs</c> and
/// <c>Spaarke.Dataverse/DataverseAccessDataSource.cs</c> —
/// <c>Create(clientId)</c> from configuration with guard clauses.
/// </para>
/// </summary>
public class I6_ObAppRegDerivationTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    /// <summary>
    /// Roots under which OBO files are discovered. Both the BFF itself and the
    /// BFF-consumed shared server libraries (Spaarke.Core / Spaarke.Dataverse —
    /// <c>DataverseAccessDataSource</c> performs OBO exchanges on behalf of BFF
    /// requests) are in scope. Slash-normalized repo-relative paths.
    /// </summary>
    private static readonly string[] ScanRelDirs = new[]
    {
        "src/server/api/Sprk.Bff.Api",
        "src/server/shared",
    };

    /// <summary>
    /// A file containing any of these markers performs (or participates in) an
    /// OBO token exchange and is subject to the I6 predicates.
    /// <c>ITokenAcquisition</c> is included so a future Microsoft.Identity.Web
    /// migration (OBO via <c>GetAccessTokenForUserAsync</c>) is auto-covered.
    /// </summary>
    private static readonly Regex OboMarker = new(
        @"\bAcquireTokenOnBehalfOf\b|\bOnBehalfOfCredential\b|\bUserAssertion\b|\bITokenAcquisition\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Line-level exemption marker: <c>I6-exempt: &lt;non-empty reason&gt;</c>.
    /// Suppresses findings on the SAME line only.
    /// </summary>
    private static readonly Regex I6ExemptMarker = new(
        @"I6-exempt\s*:\s*\S",
        RegexOptions.Compiled);

    /// <summary>
    /// GUID shape (case-insensitive hex) — a hardcoded tenant id / app (client)
    /// id / audience GUID.
    /// </summary>
    private const string GuidPattern =
        @"[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}";

    /// <summary>
    /// A GUID inside a C# string literal (plain, verbatim, or interpolated) on a
    /// single line. Comment-line filtering happens in the scanner, not here.
    /// </summary>
    private static readonly Regex GuidInStringLiteral = new(
        @"[$@]{0,2}""[^""\r\n]*" + GuidPattern + @"[^""\r\n]*""",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes <c>ConfidentialClientApplicationBuilder.Create(</c> — the app-reg
    /// binding site of an MSAL confidential client. <c>\s*</c> spans newlines so
    /// the common fluent-chain line break between the type name and
    /// <c>.Create(</c> is matched.
    /// </summary>
    private static readonly Regex CcaBuilderCreate = new(
        @"\bConfidentialClientApplicationBuilder\s*\.\s*Create\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes a read of the app-only Graph tenant configuration key
    /// (<c>configuration["Graph:TenantId"]</c> in either <c>:</c> or <c>__</c>
    /// spelling) — forbidden inside OBO files (predicate 3).
    /// </summary>
    private static readonly Regex GraphTenantIdConfigRead = new(
        @"\[\s*""Graph(?::|__)TenantId""\s*\]",
        RegexOptions.Compiled);

    // -----------------------------------------------------------------------
    // Main invariant
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-40/§4D I6: every BFF OBO code path derives its app registration from config/request context — no hardcoded GUID, no literal or literal-fallback Create(...) argument, no Graph:TenantId read")]
    public void OboAppRegistrations_AreDerivedNotDefaulted()
    {
        var scanRoots = ScanRelDirs
            .Select(rel => (Rel: rel, Full: Path.Combine(RepoRoot, rel)))
            .ToList();

        foreach (var (rel, full) in scanRoots)
        {
            Assert.True(
                Directory.Exists(full),
                $"{rel} directory not found at '{full}'. The I6 ArchTest cannot run without it.");
        }

        var oboFiles = DiscoverOboFiles(scanRoots.Select(r => r.Full));
        var offenders = ScanForI6Offenders(oboFiles);

        Assert.True(
            offenders.Count == 0,
            "§4D I6 violation: BFF OBO code path(s) bind a default or fallback app registration " +
            "instead of deriving it from per-tenant request context / required configuration. Under " +
            "MI-as-FIC (FR-39) the shared BFF UAMI can mint an assertion for ANY app-reg that trusts " +
            "it — a hardcoded or fallback app-reg selection authenticates the process AS the wrong " +
            "customer (severity HIGH). Fix each site to resolve the app-reg from required config, " +
            "options, or per-request context; or (with reviewer sign-off) suppress a single line " +
            "with '// I6-exempt: <reason>'.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    [Fact(DisplayName = "FR-40 sanity: OBO-marker file discovery finds the BFF's OBO surface (scan scope is alive)")]
    public void OboFileDiscovery_FindsAtLeastOneOboFile()
    {
        var scanRoots = ScanRelDirs.Select(rel => Path.Combine(RepoRoot, rel));
        var oboFiles = DiscoverOboFiles(scanRoots);

        // The BFF's OBO surface (GraphClientFactory at minimum) has existed since Phase 4.
        // If this ever legitimately drops to zero (BFF fully migrated off MSAL OBO), update
        // the OboMarker regex to the new exchange API in the same PR — do NOT delete this
        // sanity check, it is what proves the main test is scanning something.
        Assert.True(
            oboFiles.Count > 0,
            "I6 scan-scope sanity failed: no file under " + string.Join(" / ", ScanRelDirs) +
            " matched the OBO markers (AcquireTokenOnBehalfOf / OnBehalfOfCredential / " +
            "UserAssertion / ITokenAcquisition). Either the BFF's OBO implementation moved to a " +
            "new API (update OboMarker so I6 keeps covering it) or the scan roots are wrong.");
    }

    // -----------------------------------------------------------------------
    // Negative controls — prove each predicate flags known-bad shapes and
    // passes the compliant shapes actually used in the codebase.
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-40 negative control: Create(...) predicate flags literal, empty, null, and literal-fallback app-reg arguments")]
    public void CreateArgPredicate_FlagsNonDerivedShapes()
    {
        Assert.True(IsNonDerivedAppRegExpression(""));
        Assert.True(IsNonDerivedAppRegExpression("null"));
        Assert.True(IsNonDerivedAppRegExpression("string.Empty"));
        Assert.True(IsNonDerivedAppRegExpression("\"\""));
        // Hardcoded app-reg literal (GUID or not) = a default app registration.
        Assert.True(IsNonDerivedAppRegExpression("\"a221a95e-6abc-4434-aecc-e48338a1b2f2\""));
        Assert.True(IsNonDerivedAppRegExpression("\"my-app-client-id\""));
        // Config read with a literal fallback = a fallback app registration.
        Assert.True(IsNonDerivedAppRegExpression("configuration[\"API_APP_ID\"] ?? \"a221a95e-6abc-4434-aecc-e48338a1b2f2\""));
        Assert.True(IsNonDerivedAppRegExpression("_options.ClientId ?? \"fallback-app\""));
    }

    [Fact(DisplayName = "FR-40 negative control: Create(...) predicate passes the derived shapes the five current OBO files use")]
    public void CreateArgPredicate_PassesDerivedShapes()
    {
        Assert.False(IsNonDerivedAppRegExpression("apiAppId"));                   // GraphClientFactory
        Assert.False(IsNonDerivedAppRegExpression("config.OwningAppId"));         // SpeAdminTokenProvider
        Assert.False(IsNonDerivedAppRegExpression("_options.ClientId"));          // AgentTokenService
        Assert.False(IsNonDerivedAppRegExpression("clientId"));                   // DataverseUserClient / DataverseAccessDataSource
        // Fallback to ANOTHER config key is still config-derived (no literal default).
        Assert.False(IsNonDerivedAppRegExpression("configuration[\"AzureAd:ClientId\"] ?? configuration[\"API_APP_ID\"]"));
        // Required-config-with-throw is NOT a fallback.
        Assert.False(IsNonDerivedAppRegExpression("configuration[\"API_APP_ID\"] ?? throw new InvalidOperationException(\"API_APP_ID not configured\")"));
    }

    [Fact(DisplayName = "FR-40 negative control: GUID-literal predicate flags quoted GUIDs and ignores GUIDs in comments")]
    public void GuidLiteralPredicate_FlagsStringsIgnoresComments()
    {
        Assert.Matches(GuidInStringLiteral, "var tid = \"a221a95e-6abc-4434-aecc-e48338a1b2f2\";");
        Assert.Matches(GuidInStringLiteral, "authority = $\"https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2\";");
        Assert.DoesNotMatch(GuidInStringLiteral, "authority = $\"https://login.microsoftonline.com/{tenantId}\";");
        Assert.DoesNotMatch(GuidInStringLiteral, "var x = Guid.NewGuid();");
        // Comment-line filtering is a scanner responsibility — proven end-to-end below.
        Assert.True(IsCommentLine("    // example tenant: a221a95e-6abc-4434-aecc-e48338a1b2f2"));
        Assert.True(IsCommentLine("    /// <summary>GUID a221a95e-6abc-4434-aecc-e48338a1b2f2</summary>"));
        Assert.True(IsCommentLine("     * block comment body"));
        Assert.False(IsCommentLine("    var url = \"https://login.microsoftonline.com/x\"; // trailing"));
    }

    [Fact(DisplayName = "FR-40 negative control: Graph:TenantId predicate flags both colon and double-underscore spellings")]
    public void GraphTenantIdPredicate_FlagsBothSpellings()
    {
        Assert.Matches(GraphTenantIdConfigRead, "var t = configuration[\"Graph:TenantId\"];");
        Assert.Matches(GraphTenantIdConfigRead, "var t = configuration[\"Graph__TenantId\"];");
        Assert.DoesNotMatch(GraphTenantIdConfigRead, "var t = configuration[\"Graph:ManagedIdentity:ClientId\"];");
        Assert.DoesNotMatch(GraphTenantIdConfigRead, "var t = configuration[\"TENANT_ID\"];");
    }

    /// <summary>
    /// Regression-seed / end-to-end negative control (mirrors
    /// <c>I1_NoHardcodedTenantTests.ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine</c>):
    /// authors a temporary <c>.cs</c> file under <see cref="Path.GetTempPath"/> that
    /// simulates a config-derived-with-literal-fallback OBO service — the exact
    /// hypothetical injection FR-40 exists to catch — runs the full scanner against
    /// JUST that file, and asserts all three predicates fire with correct file +
    /// line attribution. This proves the SCANNER (file read, comment filtering,
    /// balanced-arg extraction, line arithmetic, offender formatting) catches a real
    /// offender file end-to-end, not merely that the regexes match in memory.
    ///
    /// <para>
    /// The seed also proves the exemption mechanism both ways: an offending line
    /// carrying <c>// I6-exempt: reason</c> is suppressed; a bare
    /// <c>// I6-exempt:</c> (no reason) is NOT.
    /// </para>
    ///
    /// <para>
    /// Placed under <see cref="Path.GetTempPath"/> (unique GUID-suffixed dir,
    /// deleted in <c>finally</c>) so it is invisible to the main test's scan and
    /// cannot pollute the repo tree if the test process dies mid-run.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "FR-40 regression seed: temp .cs with a fallback app-reg OBO service is detected end-to-end (all three predicates + exemption honored both ways)")]
    public void ScanForI6Offenders_TempOboServiceWithFallbackAppReg_ReportsAllPredicates()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            "spaarke-i6-regression-seed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var seedPath = Path.Combine(tempDir, "SeedFallbackOboService.cs");

            // Line-numbered seed (1-based, matching scanner attribution):
            //   1: // Seed regression control - NOT production code. Contains UserAssertion marker.
            //   2: public class SeedFallbackOboService {
            //   3:     void Bad(IConfiguration c) {
            //   4:         var tid = c["Graph:TenantId"];
            //   5:         var cca = ConfidentialClientApplicationBuilder.Create(c["APP"] ?? "11111111-2222-3333-4444-555555555555").Build();
            //   6:         var exempted = "22222222-3333-4444-5555-666666666666"; // I6-exempt: seed proves suppression works
            //   7:         var notExempted = "33333333-4444-5555-6666-777777777777"; // I6-exempt:
            //   8:     }
            //   9: }
            File.WriteAllText(
                seedPath,
                "// Seed regression control - NOT production code. Contains UserAssertion marker.\n" +
                "public class SeedFallbackOboService {\n" +
                "    void Bad(IConfiguration c) {\n" +
                "        var tid = c[\"Graph:TenantId\"];\n" +
                "        var cca = ConfidentialClientApplicationBuilder.Create(c[\"APP\"] ?? \"11111111-2222-3333-4444-555555555555\").Build();\n" +
                "        var exempted = \"22222222-3333-4444-5555-666666666666\"; // I6-exempt: seed proves suppression works\n" +
                "        var notExempted = \"33333333-4444-5555-6666-777777777777\"; // I6-exempt:\n" +
                "    }\n" +
                "}\n");

            // Discovery must see the seed (the '// ... UserAssertion ...' comment on line 1
            // is a sufficient marker — discovery is content-based by design).
            var discovered = DiscoverOboFiles(new[] { tempDir });
            Assert.Single(discovered);

            var offenders = ScanForI6Offenders(discovered);

            // Expected findings:
            //   line 4 — Graph:TenantId read                      (predicate 3)
            //   line 5 — GUID literal in string                   (predicate 1)
            //   line 5 — Create(...) literal-fallback argument    (predicate 2)
            //   line 7 — GUID literal, bare exemption (no reason) (predicate 1)
            // Line 6 must be ABSENT (valid exemption suppresses).
            Assert.Equal(4, offenders.Count);
            Assert.Contains(offenders, o => o.Contains(":4 ") && o.Contains("Graph:TenantId"));
            Assert.Contains(offenders, o => o.Contains(":5 ") && o.Contains("hardcoded GUID"));
            Assert.Contains(offenders, o => o.Contains(":5 ") && o.Contains("Create("));
            Assert.Contains(offenders, o => o.Contains(":7 ") && o.Contains("hardcoded GUID"));
            Assert.DoesNotContain(offenders, o => o.Contains(":6 "));
            Assert.All(offenders, o => Assert.Contains("SeedFallbackOboService.cs", o));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Scanner
    // -----------------------------------------------------------------------

    /// <summary>
    /// Enumerates production <c>*.cs</c> files under the given roots (skipping
    /// <c>obj/</c> and <c>bin/</c>) and returns those containing an OBO marker.
    /// </summary>
    private static List<string> DiscoverOboFiles(IEnumerable<string> roots)
    {
        var files = new List<string>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                {
                    continue;
                }
                if (OboMarker.IsMatch(File.ReadAllText(file)))
                {
                    files.Add(file);
                }
            }
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Applies the three I6 predicates to each OBO file; returns offender strings
    /// in the shape <c>{relPath}:{line} — {predicate} …</c>. Extracted as a static
    /// helper (same convention as <c>I1.ScanForI1Offenders</c>) so the regression-seed
    /// test exercises the identical code path the main test uses.
    /// </summary>
    private static List<string> ScanForI6Offenders(IEnumerable<string> oboFiles)
    {
        var offenders = new List<string>();

        foreach (var file in oboFiles)
        {
            var rel = RelPath(file);
            var text = File.ReadAllText(file);
            var lines = text.Split('\n');

            // Predicate 1 — hardcoded GUID inside a string literal (line-based so
            // comment-line and exemption filtering are exact).
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsCommentLine(line)) continue;
                if (I6ExemptMarker.IsMatch(line)) continue;
                if (GuidInStringLiteral.IsMatch(line))
                {
                    offenders.Add(
                        $"{rel}:{i + 1} — hardcoded GUID in a string literal inside an OBO code path. " +
                        $"A literal tenant/app/audience GUID is a default app registration baked into " +
                        $"code — the OBO target must be derived from per-tenant request context or " +
                        $"required configuration. Reference: spec.md FR-40 / design.md §4D I6.");
                }
            }

            // Predicate 2 — ConfidentialClientApplicationBuilder.Create(x): x must be derived.
            foreach (Match m in CcaBuilderCreate.Matches(text))
            {
                var openParen = m.Index + m.Length - 1;
                var args = ExtractBalancedArgList(text, openParen);
                if (args is null) continue;

                var lineNumber = LineNumberFor(text, m.Index);
                if (LineAt(lines, lineNumber) is { } createLine && I6ExemptMarker.IsMatch(createLine)) continue;

                if (IsNonDerivedAppRegExpression(args))
                {
                    offenders.Add(
                        $"{rel}:{lineNumber} — ConfidentialClientApplicationBuilder.Create(...) argument " +
                        $"'{Truncate(args)}' is a literal, empty/null, or has a null-coalescing fallback " +
                        $"to a literal — a default/fallback app registration. Fix: pass an expression " +
                        $"derived from required config, options, or per-request context (e.g. " +
                        $"Create(config.OwningAppId)); required config may use '?? throw' but never " +
                        $"'?? \"literal\"'. Reference: spec.md FR-40 / design.md §4D I6.");
                }
            }

            // Predicate 3 — Graph:TenantId (app-only Graph tenant) read inside an OBO file.
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsCommentLine(line)) continue;
                if (I6ExemptMarker.IsMatch(line)) continue;
                if (GraphTenantIdConfigRead.IsMatch(line))
                {
                    offenders.Add(
                        $"{rel}:{i + 1} — reads the app-only Graph:TenantId configuration inside an OBO " +
                        $"code path. The app-only tenant is the SERVICE's tenant; the OBO exchange " +
                        $"target must come from the caller's per-request tenant context, never from " +
                        $"app-only configuration. Reference: spec.md FR-40 / design.md §4D I6.");
                }
            }
        }

        return offenders;
    }

    // -----------------------------------------------------------------------
    // Predicates / helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// True when the <c>Create(...)</c> argument expression is NOT derived from
    /// config/options/request context: empty, <c>null</c>, <c>string.Empty</c>,
    /// a pure string literal (plain/verbatim, or interpolated WITHOUT any
    /// <c>{...}</c> hole), or any expression containing a null-coalescing
    /// fallback to a string literal.
    /// </summary>
    private static bool IsNonDerivedAppRegExpression(string arg)
    {
        var s = arg.Trim();
        if (s.Length == 0) return true;
        if (s == "null") return true;
        if (s == "string.Empty" || s == "String.Empty") return true;

        // Null-coalescing fallback to a string literal ('?? "..."' in any literal
        // flavor). '?? throw' and '?? otherExpression' are NOT fallbacks to a default.
        if (Regex.IsMatch(s, @"\?\?\s*[$@]{0,2}""")) return true;

        // Pure literal argument. Interpolated strings containing a hole are treated
        // as derived (the hole is the derivation); interpolated strings without a
        // hole are literals.
        var isStringStart = Regex.IsMatch(s, @"^[$@]{0,2}""");
        if (isStringStart)
        {
            var interpolated = s.StartsWith('$') || s.StartsWith("@$", StringComparison.Ordinal);
            if (!interpolated) return true;
            if (!s.Contains('{')) return true;
        }

        return false;
    }

    /// <summary>
    /// True when the line is (the start of) a comment: <c>//</c> / <c>///</c>
    /// line comments or a block-comment body/openings (<c>*</c> / <c>/*</c>).
    /// Trailing comments on code lines return false — the code portion is scanned.
    /// </summary>
    private static bool IsCommentLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("//", StringComparison.Ordinal) ||
               t.StartsWith("*", StringComparison.Ordinal) ||
               t.StartsWith("/*", StringComparison.Ordinal);
    }

    private static string? LineAt(string[] lines, int oneBasedLine)
        => oneBasedLine >= 1 && oneBasedLine <= lines.Length ? lines[oneBasedLine - 1] : null;

    /// <summary>
    /// Extracts the balanced argument list following an opening paren (same
    /// helper convention as I5).
    /// </summary>
    private static string? ExtractBalancedArgList(string source, int openParenIndex)
    {
        if (openParenIndex < 0 || openParenIndex >= source.Length ||
            source[openParenIndex] != '(') return null;

        int depth = 1;
        for (int i = openParenIndex + 1; i < source.Length; i++)
        {
            var c = source[i];
            switch (c)
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0) return source[(openParenIndex + 1)..i];
                    break;
            }
        }
        return null;
    }

    private static int LineNumberFor(string source, int charIndex)
    {
        if (charIndex < 0 || charIndex > source.Length) return 1;
        int line = 1;
        for (int i = 0; i < charIndex; i++)
        {
            if (source[i] == '\n') line++;
        }
        return line;
    }

    private static string RelPath(string fullPath)
        => fullPath.Replace(RepoRoot + Path.DirectorySeparatorChar, string.Empty)
                   .Replace(Path.DirectorySeparatorChar, '/');

    private static string Truncate(string s, int max = 80)
    {
        var flat = s.Replace("\r", string.Empty).Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : $"{flat[..(max - 3)]}...";
    }

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
        return AppContext.BaseDirectory;
    }
}
