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
            var text = File.ReadAllText(file);

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
}
