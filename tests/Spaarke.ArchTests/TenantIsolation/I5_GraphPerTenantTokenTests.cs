using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// Tenant-isolation invariant <b>I5</b> (spec.md FR-32 / design.md §4D I5):
/// Graph token acquisition is per-tenant scoped. Delegated calls use OBO with
/// the caller's <c>tid</c> claim; app-only calls use <c>.default</c> scope
/// with the target tenant's <c>tid</c> explicitly named (NOT the default
/// tenant of the MI credential).
///
/// <para>
/// <b>Severity</b>: CATASTROPHIC. A token acquired against the wrong tenant
/// returns Graph resources (SPE files, mail, group membership) from the
/// wrong tenant (§4D I5 rationale).
/// </para>
///
/// <para>
/// <b>Scan shape</b>: file-level scan of every credential-construction site
/// and MSAL authority binding under the BFF's credential-boundary directories:
/// <list type="bullet">
///   <item><c>src/server/api/Sprk.Bff.Api/Infrastructure/Graph/**/*.cs</c> —
///     the original scope (task 064). Every Graph-outbound credential lives
///     here.</item>
///   <item><c>src/server/api/Sprk.Bff.Api/Infrastructure/Auth/**/*.cs</c> —
///     added 2026-08-17 by customer-provisioning-orchestration-r1 Wave 4
///     Batch 4D drift-1 in response to the task 065 audit report §7.2
///     finding: <c>ManagedIdentityCredentialFactory.cs</c> constructed a
///     <c>DefaultAzureCredential</c> without a <c>TenantId</c> assignment
///     (parallel to the fixed <c>GraphClientFactory:132</c> gap) and was
///     invisible to the original scan. The BFF's central credential factory
///     is the highest-blast-radius credential surface — it feeds the
///     DI-singleton <c>TokenCredential</c> used by every Dataverse / Cosmos
///     / OpenAI / Content Safety consumer — so scanning it under the same
///     I5 rules closes the visibility gap.</item>
/// </list>
/// For each construction site the ArchTest asserts one of the following
/// compliant shapes:
/// <list type="number">
///   <item><c>new ClientSecretCredential(tenantId, ...)</c> — first positional
///     argument is a non-empty tenant expression (NOT an empty string, NOT
///     the null literal, NOT the string literal <c>"common"</c> /
///     <c>"organizations"</c> / <c>"consumers"</c>).</item>
///   <item><c>new DefaultAzureCredential(options)</c> where the same file
///     contains an explicit <c>TenantId = ...</c> assignment on a
///     <c>DefaultAzureCredentialOptions</c> object (i.e., the options bag
///     scopes the credential to a specific tenant instead of letting the MI
///     credential fall back to its host-assigned default tenant).</item>
///   <item><c>ConfidentialClientApplicationBuilder.Create(...).WithAuthority(x)</c>
///     where <c>x</c> is NOT the shape
///     <c>"https://login.microsoftonline.com/common"</c> /
///     <c>"...organizations"</c> / <c>"...consumers"</c> — i.e., a specific
///     tenant path (either an interpolated <c>{tenantId}</c> or a stored
///     variable) is used.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Compliant baseline references</b>: <c>GraphClientFactory.cs</c> uses
/// <c>new ClientSecretCredential(_tenantId, ...)</c> and
/// <c>.WithAuthority($"https://login.microsoftonline.com/{tenantId}")</c>
/// for the legacy and OBO paths respectively;
/// <c>CiamGraphClientFactory.cs</c> constructs
/// <c>_authority = $"{instance}/{tenantId}"</c> before passing it to
/// <c>.WithAuthority(_authority)</c>;
/// <c>SpeAdminGraphService.cs</c> uses
/// <c>new ClientSecretCredential(config.TenantId, ...)</c>.
/// </para>
/// </summary>
public class I5_GraphPerTenantTokenTests
{
    private static readonly string RepoRoot = ResolveRepoRoot();

    /// <summary>
    /// Directories scanned for credential-construction sites. Each entry MUST be a
    /// slash-normalized repo-relative path. Adding a directory here broadens the I5 invariant;
    /// see the class-level XML doc for the rationale behind each entry.
    /// </summary>
    private static readonly string[] ScanRelDirs = new[]
    {
        "src/server/api/Sprk.Bff.Api/Infrastructure/Graph",
        // Added 2026-08-17 (customer-provisioning-orchestration-r1 Wave 4 Batch 4D drift-1
        // follow-up to task 065): the BFF's central Dataverse/Cosmos/OpenAI credential factory
        // lives under Infrastructure/Auth and is the highest-blast-radius credential surface
        // outside Infrastructure/Graph. Task 065 audit §7.2 found ManagedIdentityCredentialFactory
        // had the same missing-TenantId gap as GraphClientFactory but was invisible to this
        // ArchTest's original scope.
        "src/server/api/Sprk.Bff.Api/Infrastructure/Auth",
    };

    /// <summary>
    /// Recognizes <c>new ClientSecretCredential(</c> — captures the position
    /// of the opening paren so the argument list can be balanced-extracted.
    /// </summary>
    private static readonly Regex NewClientSecretCredential = new(
        @"\bnew\s+ClientSecretCredential\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes <c>new DefaultAzureCredential(</c>. Requires
    /// <c>DefaultAzureCredentialOptions.TenantId</c> to be assigned in the
    /// same file for compliance.
    /// </summary>
    private static readonly Regex NewDefaultAzureCredential = new(
        @"\bnew\s+DefaultAzureCredential\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes <c>new ManagedIdentityCredential(</c> — treated same as
    /// DefaultAzureCredential: requires an explicit tenant option OR the file
    /// must scope the following ForApp/ForUser flow to a specific tenant.
    /// </summary>
    private static readonly Regex NewManagedIdentityCredential = new(
        @"\bnew\s+ManagedIdentityCredential\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes MSAL <c>.WithAuthority(</c>. The argument must NOT bind to
    /// the multi-tenant common/organizations/consumers authorities.
    /// </summary>
    private static readonly Regex WithAuthority = new(
        @"\.WithAuthority\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Recognizes an assignment of <c>TenantId = ...</c> on a
    /// <c>DefaultAzureCredentialOptions</c> (or other credential options
    /// bag). Presence anywhere in the file is treated as evidence the file
    /// scopes its DefaultAzureCredential(s) to a specific tenant.
    /// </summary>
    private static readonly Regex OptionsTenantIdAssignment = new(
        @"\.TenantId\s*=|\bTenantId\s*=\s*[\w""$]",
        RegexOptions.Compiled);

    /// <summary>
    /// Forbidden multi-tenant authority path fragments — any authority string
    /// ending in these values scopes the credential to the multi-tenant
    /// endpoint (NOT a specific customer tenant).
    /// </summary>
    private static readonly Regex MultiTenantAuthority = new(
        @"/(common|organizations|consumers)\b(?!/)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact(DisplayName = "FR-32/§4D I5: every BFF credential construction under Infrastructure/{Graph,Auth}/** is per-tenant scoped (ClientSecretCredential, DefaultAzureCredential, WithAuthority)")]
    public void GraphCredentials_ArePerTenantScoped()
    {
        var scanRoots = ScanRelDirs
            .Select(rel => (Rel: rel, Full: Path.Combine(RepoRoot, rel)))
            .ToList();

        foreach (var (rel, full) in scanRoots)
        {
            Assert.True(
                Directory.Exists(full),
                $"{rel} directory not found at '{full}'. The I5 ArchTest cannot run without it.");
        }

        var offenders = new List<string>();
        var scannedFiles = scanRoots
            .SelectMany(root => EnumerateProductionCsFiles(root.Full))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var file in scannedFiles)
        {
            var rel = RelPath(file);

            // Comments are MASKED before scanning — see MaskComments. Without this the regexes
            // match prose, and this invariant reported a false CATASTROPHIC violation against a
            // doc comment whose entire purpose was to warn against the pattern it was flagged for.
            var text = MaskComments(File.ReadAllText(file));

            // 1. ClientSecretCredential — first positional arg must be a non-trivial tenant expression.
            foreach (Match m in NewClientSecretCredential.Matches(text))
            {
                var openParen = m.Index + m.Length - 1;
                var args = ExtractBalancedArgList(text, openParen);
                if (args is null) continue;

                var firstArg = FirstPositionalArgOrEmpty(args);
                if (IsEmptyOrNullOrMultiTenantLiteral(firstArg))
                {
                    var lineNumber = LineNumberFor(text, m.Index);
                    offenders.Add(
                        $"{rel}:{lineNumber} — new ClientSecretCredential(...) first arg '{Truncate(firstArg)}' " +
                        $"is empty, null, or a multi-tenant authority literal (common/organizations/consumers). " +
                        $"Fix: pass an explicit tenantId variable or configured value as the first argument. " +
                        $"Reference: spec.md FR-32 / design.md §4D I5.");
                }
            }

            // 2. DefaultAzureCredential / ManagedIdentityCredential — the file must also
            //    contain an explicit .TenantId = ... assignment on an options bag.
            var defaultAzureCredHits = NewDefaultAzureCredential.Matches(text).Cast<Match>().ToList();
            var managedIdCredHits = NewManagedIdentityCredential.Matches(text).Cast<Match>().ToList();
            var credConstructions = defaultAzureCredHits.Concat(managedIdCredHits);

            if (credConstructions.Any())
            {
                var hasOptionsTenantAssignment = OptionsTenantIdAssignment.IsMatch(text);
                if (!hasOptionsTenantAssignment)
                {
                    foreach (var m in credConstructions)
                    {
                        var lineNumber = LineNumberFor(text, m.Index);
                        var credName = defaultAzureCredHits.Contains(m) ? "DefaultAzureCredential" : "ManagedIdentityCredential";
                        offenders.Add(
                            $"{rel}:{lineNumber} — new {credName}(...) constructed with no " +
                            $"'TenantId = ...' assignment on its options bag anywhere in the file. Without an " +
                            $"explicit TenantId the credential resolves to the MI host's default tenant, which " +
                            $"is CATASTROPHIC in a multi-tenant / customer-provisioning scenario. " +
                            $"Fix: set `credentialOptions.TenantId = tenantId` (or the appropriate option-bag " +
                            $"field) before constructing the credential. " +
                            $"Reference: spec.md FR-32 / design.md §4D I5.");
                    }
                }
            }

            // 3. WithAuthority — argument must NOT bind to common/organizations/consumers.
            foreach (Match m in WithAuthority.Matches(text))
            {
                var openParen = m.Index + m.Length - 1;
                var args = ExtractBalancedArgList(text, openParen);
                if (args is null) continue;

                if (MultiTenantAuthority.IsMatch(args))
                {
                    var lineNumber = LineNumberFor(text, m.Index);
                    offenders.Add(
                        $"{rel}:{lineNumber} — .WithAuthority(...) argument uses a multi-tenant authority " +
                        $"path (/common /organizations /consumers). This creates a token intent that spans " +
                        $"tenants — CATASTROPHIC per I5. Fix: bind the authority to " +
                        $"'https://login.microsoftonline.com/{{tenantId}}' with an interpolated tenant. " +
                        $"Reference: spec.md FR-32 / design.md §4D I5.");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "§4D I5 violation: Graph credential construction site(s) do not explicitly scope to a specific " +
            "tenant. In a multi-tenant / customer-provisioning scenario, an implicitly-tenanted credential " +
            "acquires tokens for the wrong tenant (CATASTROPHIC — file / mail / group membership from the " +
            "wrong tenant). Bind every credential explicitly.\n" +
            $"Offenders:\n{string.Join("\n", offenders.OrderBy(x => x, StringComparer.Ordinal))}");
    }

    // -----------------------------------------------------------------------
    // Negative controls
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "FR-32 negative control: ClientSecretCredential predicate flags empty and multi-tenant first args")]
    public void ClientSecretCredentialFirstArg_FlagsBadShapes()
    {
        Assert.True(IsEmptyOrNullOrMultiTenantLiteral("\"\""));
        Assert.True(IsEmptyOrNullOrMultiTenantLiteral("null"));
        Assert.True(IsEmptyOrNullOrMultiTenantLiteral("\"common\""));
        Assert.True(IsEmptyOrNullOrMultiTenantLiteral("\"organizations\""));
        Assert.True(IsEmptyOrNullOrMultiTenantLiteral("\"consumers\""));
    }

    [Fact(DisplayName = "FR-32 negative control: ClientSecretCredential predicate passes on legitimate tenant expressions")]
    public void ClientSecretCredentialFirstArg_PassesRealShapes()
    {
        Assert.False(IsEmptyOrNullOrMultiTenantLiteral("_tenantId"));
        Assert.False(IsEmptyOrNullOrMultiTenantLiteral("tenantId"));
        Assert.False(IsEmptyOrNullOrMultiTenantLiteral("config.TenantId"));
        Assert.False(IsEmptyOrNullOrMultiTenantLiteral("\"a221a95e-6abc-4434-aecc-e48338a1b2f2\""));
    }

    [Fact(DisplayName = "FR-32 negative control: WithAuthority predicate flags multi-tenant paths")]
    public void WithAuthorityPredicate_FlagsMultiTenantPaths()
    {
        Assert.Matches(MultiTenantAuthority, "\"https://login.microsoftonline.com/common\"");
        Assert.Matches(MultiTenantAuthority, "\"https://login.microsoftonline.com/organizations\"");
        Assert.Matches(MultiTenantAuthority, "\"https://login.microsoftonline.com/consumers\"");
    }

    [Fact(DisplayName = "FR-32 negative control: WithAuthority predicate passes on tenant-specific paths")]
    public void WithAuthorityPredicate_PassesSpecificTenantPaths()
    {
        Assert.DoesNotMatch(MultiTenantAuthority, "$\"https://login.microsoftonline.com/{tenantId}\"");
        Assert.DoesNotMatch(MultiTenantAuthority, "$\"{instance}/{tenantId}\"");
        Assert.DoesNotMatch(MultiTenantAuthority, "_authority");
        Assert.DoesNotMatch(MultiTenantAuthority, "\"https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2\"");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extract the first positional argument from a comma-separated argument
    /// list (surface-level split — does not descend into nested calls; this
    /// is sufficient for the credential-construction shapes we scan).
    /// </summary>
    private static string FirstPositionalArgOrEmpty(string args)
    {
        int depth = 0;
        for (int i = 0; i < args.Length; i++)
        {
            var c = args[i];
            if (c == '(' || c == '[' || c == '<' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '>' || c == '}') depth--;
            else if (c == ',' && depth == 0) return args[..i].Trim();
        }
        return args.Trim();
    }

    private static bool IsEmptyOrNullOrMultiTenantLiteral(string firstArg)
    {
        var s = firstArg.Trim();
        if (s.Length == 0) return true;
        if (s == "null") return true;
        if (s == "string.Empty" || s == "String.Empty") return true;
        if (s == "\"\"") return true;
        // Multi-tenant authority literals.
        if (s.Equals("\"common\"", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("\"organizations\"", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("\"consumers\"", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

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

    /// <summary>
    /// Blanks out comments so the credential regexes match CODE, not prose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> On 2026-08-26 this invariant failed CI with a CATASTROPHIC-severity
    /// offender: <c>ServiceBusClientFactory.cs:52 — new DefaultAzureCredential(...) constructed
    /// with no 'TenantId = ...' assignment</c>. That file constructs no credential at all. Line 52
    /// is an XML doc comment reading <i>"MembershipJunctionUpdaterHost.cs:120 constructs
    /// `new DefaultAzureCredential()` inline, and that is a deviation not to propagate"</i> — a
    /// warning AGAINST the very pattern it was reported for.
    /// </para>
    /// <para>
    /// The scan read raw file text, so any prose quoting a banned construct became a violation.
    /// That is worse than a nuisance: a merge-blocking invariant that cries wolf gets routed
    /// around, and the next real offender arrives in a job everyone has learned to ignore.
    /// </para>
    /// <para>
    /// <b>This narrows the detector, it does not weaken it.</b> A comment cannot construct a
    /// credential, so nothing real is lost. It is in fact slightly STRICTER: a
    /// <c>TenantId = ...</c> assignment that appears only inside a comment no longer satisfies the
    /// options-bag check, because a commented-out assignment does not scope anything.
    /// </para>
    /// <para>
    /// Offsets are preserved — every masked character is replaced 1:1 with a space and newlines are
    /// kept — so <see cref="LineNumberFor"/> still reports the true line of a real offender.
    /// </para>
    /// </remarks>
    internal static string MaskComments(string source)
    {
        var buffer = source.ToCharArray();
        var i = 0;
        var n = source.Length;

        static bool IsVerbatimStart(string s, int idx) =>
            s[idx] == '@' && idx + 1 < s.Length && s[idx + 1] == '"';

        while (i < n)
        {
            var c = source[i];

            // ── Raw string literal (C# 11): """ … """ ──
            if (c == '"' && i + 2 < n && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var end = source.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                i = end < 0 ? n : end + 3;
                continue;
            }

            // ── Verbatim string: @"…" where "" is an escaped quote ──
            if (IsVerbatimStart(source, i))
            {
                i += 2;
                while (i < n)
                {
                    if (source[i] == '"')
                    {
                        if (i + 1 < n && source[i + 1] == '"') { i += 2; continue; }
                        i++; break;
                    }
                    i++;
                }
                continue;
            }

            // ── Regular string: "…" with backslash escapes ──
            if (c == '"')
            {
                i++;
                while (i < n)
                {
                    if (source[i] == '\\') { i += 2; continue; }
                    if (source[i] == '"') { i++; break; }
                    if (source[i] == '\n') break;   // unterminated — bail rather than run away
                    i++;
                }
                continue;
            }

            // ── Char literal: 'c' / '\'' ──
            if (c == '\'')
            {
                i++;
                while (i < n)
                {
                    if (source[i] == '\\') { i += 2; continue; }
                    if (source[i] == '\'') { i++; break; }
                    if (source[i] == '\n') break;
                    i++;
                }
                continue;
            }

            // ── Line comment: // and /// ──
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n') { buffer[i] = ' '; i++; }
                continue;
            }

            // ── Block comment: /* … */ ──
            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                buffer[i] = ' ';
                buffer[i + 1] = ' ';
                i += 2;
                while (i < n)
                {
                    if (source[i] == '*' && i + 1 < n && source[i + 1] == '/')
                    {
                        buffer[i] = ' ';
                        buffer[i + 1] = ' ';
                        i += 2;
                        break;
                    }
                    // Newlines survive so line numbers stay correct.
                    if (source[i] != '\n' && source[i] != '\r') buffer[i] = ' ';
                    i++;
                }
                continue;
            }

            i++;
        }

        return new string(buffer);
    }

    private static IEnumerable<string> EnumerateProductionCsFiles(string root)
        => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

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

    private static string Truncate(string s, int max = 60)
        => s.Length <= max ? s : $"{s[..(max - 3)]}...";

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

    // -------------------------------------------------------------------------
    // Controls for MaskComments.
    //
    // tests/CLAUDE.md, "Authoring rules for this path": every rule carries a NEGATIVE control
    // proving the detector fires on a seeded violation, and a POSITIVE control proving it does
    // NOT fire on the sanctioned shape. Both matter here -- "mask everything" would satisfy the
    // false-positive control while silently disabling a CATASTROPHIC invariant.
    // -------------------------------------------------------------------------

    /// <summary>
    /// POSITIVE CONTROL -- the scan must NOT see a credential named only in prose. This is the
    /// exact shape that produced the false offender against ServiceBusClientFactory.cs:52.
    /// </summary>
    [Fact(DisplayName = "MaskComments: a credential named only in a doc comment is not scannable")]
    public void MaskComments_CredentialNamedOnlyInADocComment_IsNotScannable()
    {
        var source =
            "/// Never write new DefaultAzureCredential() inline here -- use the DI singleton." + Environment.NewLine +
            "public static class Factory { }";

        Assert.DoesNotContain("DefaultAzureCredential", MaskComments(source));
    }

    /// <summary>
    /// NEGATIVE CONTROL -- a real construction must survive masking and stay visible to the scan.
    /// </summary>
    [Fact(DisplayName = "MaskComments: a real credential construction remains scannable")]
    public void MaskComments_RealCredentialConstruction_RemainsScannable()
    {
        var source =
            "// a comment mentioning DefaultAzureCredential" + Environment.NewLine +
            "var cred = new DefaultAzureCredential(options);";

        var masked = MaskComments(source);

        Assert.Contains("new DefaultAzureCredential(options)", masked);

        var firstLine = masked.Split(NEWLINE_CHAR)[0];
        Assert.DoesNotContain("DefaultAzureCredential", firstLine);
    }

    /// <summary>
    /// A <c>//</c> inside a string literal is not a comment. Masking it would corrupt the very
    /// authority literals the WithAuthority rule inspects.
    /// </summary>
    [Fact(DisplayName = "MaskComments: '//' inside a string literal is preserved")]
    public void MaskComments_DoubleSlashInsideStringLiteral_IsPreserved()
    {
        var source = "var uri = QUOTEhttps://login.microsoftonline.com/commonQUOTE; var x = 1;"
            .Replace("QUOTE", DOUBLE_QUOTE);

        Assert.Contains("https://login.microsoftonline.com/common", MaskComments(source));
    }

    /// <summary>
    /// Masking preserves offsets and line count, so a reported line number still points at the
    /// real offender rather than drifting.
    /// </summary>
    [Fact(DisplayName = "MaskComments: preserves length and line count")]
    public void MaskComments_PreservesLengthAndLineCount()
    {
        var source =
            "/* block" + Environment.NewLine +
            "   comment */" + Environment.NewLine +
            "var x = 1;";

        var masked = MaskComments(source);

        Assert.Equal(source.Length, masked.Length);
        Assert.Equal(
            source.Count(c => c == NEWLINE_CHAR),
            masked.Count(c => c == NEWLINE_CHAR));
    }

    private const char NEWLINE_CHAR = '\n';
    private const string DOUBLE_QUOTE = "\"";
}
