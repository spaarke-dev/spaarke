using System.Text.RegularExpressions;
using Sprk.Bff.Api.Infrastructure.Auth;
using Xunit;

namespace Spaarke.ArchTests.TenantIsolation;

/// <summary>
/// The Microsoft Graph application roles a customer stamp identity — and the L2 control-plane Worker identity — hold
/// are exactly the evidence-backed sets in <c>projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md</c>
/// (task 261 / G31, owner 2026-10-09: "ensure this is accurately scoped").
/// </summary>
/// <remarks>
/// <para><b>Why.</b> Every Model 1 stamp lives in Spaarke's own tenant: a tenant-wide role on a stamp identity
/// (<c>Directory.ReadWrite.All</c>, <c>Sites.ReadWrite.All</c>, …) reaches Spaarke's directory and SharePoint. H10 grants
/// the catalog and removes every other Graph role, so the catalog itself is the boundary — a role added to it is granted
/// to every stamp at the next run.</para>
///
/// <para><b>The rules.</b></para>
/// <list type="number">
///   <item><c>GraphAppRoles.All</c> (BFF), <c>L2GraphAppRolesRegistry</c> (its L2 mirror) and the note's
///   <c>t261-stamp-set</c> block list the same (value, app-role id) pairs, and the note's <c>entra</c> / <c>exchange</c>
///   column agrees with <c>IGraphAppRolesRegistry.ExchangeScopedValues</c>.</item>
///   <item><c>ControlPlaneGraphAppRoles.cs</c> — parsed with the same three flat shapes
///   <c>scripts/Grant-GraphAppRoles.ps1</c> parses — equals the note's <c>t261-control-plane-set</c> block.</item>
///   <item>Every role in both sets has an evidence row in the note: a table row naming it with a
///   <c>learn.microsoft.com</c> citation. <b>Adding a role = a note row (call site + Learn URL) + the catalog edit.</b></item>
///   <item>The demo self-service registration feature (the BFF's only app-only <c>/users</c> + <c>/groups</c> writer)
///   is registered and mapped only behind <c>RegistrationModule.IsDemoProvisioningEnabled</c>, and no stamp deployment
///   channel writes <c>DemoProvisioning:*</c> — which is what keeps directory roles out of the stamp set.</item>
/// </list>
/// <para>Negative controls prove the parsers fire on a mismatch.</para>
/// </remarks>
public class StampGraphAppRoleEvidenceTests
{
    private static readonly string NotePath = Path.Combine(SourceScan.RepoRoot,
        "projects", "customer-provisioning-orchestration-r1", "notes", "t261-stamp-graph-least-privilege.md");

    private static readonly string L2Handlers = Path.Combine(SourceScan.RepoRoot,
        "src", "server", "services", "Sprk.Provisioning.ControlPlane.Core", "Handlers");

    private static readonly string BffRoot = Path.Combine(SourceScan.RepoRoot, "src", "server", "api", "Sprk.Bff.Api");

    // ── rule 1 ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "T261: GraphAppRoles.All = L2 mirror = the note's documented stamp set")]
    public void StampCatalog_EqualsTheNote_AndTheL2Mirror()
    {
        var note = ReadBlock(File.ReadAllText(NotePath), "t261-stamp-set");
        var bff = GraphAppRoles.All.Select(r => $"{r.Value}|{r.AppRoleId}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var l2 = ParseL2Mirror(File.ReadAllText(Path.Combine(L2Handlers, "DataverseAppUserGraphParity", "L2GraphAppRolesRegistry.cs")));
        var noted = note.Select(r => $"{r[0]}|{r[1]}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(bff.SetEquals(noted), Diff("GraphAppRoles.All", bff, "the note's t261-stamp-set", noted));
        Assert.True(l2.SetEquals(noted), Diff("L2GraphAppRolesRegistry", l2, "the note's t261-stamp-set", noted));

        var exchangeScoped = ParseExchangeScoped(File.ReadAllText(Path.Combine(L2Handlers, "DataverseAppUserGraphParity", "IGraphAppRolesRegistry.cs")));
        foreach (var row in note)
        {
            var expected = exchangeScoped.Contains(row[0]) ? "exchange" : "entra";
            Assert.True(string.Equals(row.ElementAtOrDefault(2), expected, StringComparison.OrdinalIgnoreCase),
                $"{row[0]}: the note says '{row.ElementAtOrDefault(2)}', IGraphAppRolesRegistry.ExchangeScopedValues says '{expected}'.");
        }
    }

    // ── rule 2 ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "T261: ControlPlaneGraphAppRoles.cs (as the grant script parses it) = the note's control-plane set")]
    public void ControlPlaneCatalog_EqualsTheNote()
    {
        var note = ReadBlock(File.ReadAllText(NotePath), "t261-control-plane-set")
            .Select(r => $"{r[0]}|{r[1]}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var parsed = ParseLikeTheGrantScript(File.ReadAllText(Path.Combine(L2Handlers, "ControlPlaneGraphAppRoles.cs")));

        Assert.True(parsed.SetEquals(note), Diff("ControlPlaneGraphAppRoles.cs", parsed, "the note's t261-control-plane-set", note));
    }

    [Fact(DisplayName = "T261: the platform BFF catalog equals the note's documented platform set")]
    public void PlatformCatalog_EqualsTheNote()
    {
        var note = ReadBlock(File.ReadAllText(NotePath), "t261-platform-set")
            .Select(r => $"{r[0]}|{r[1]}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var platform = PlatformBffGraphAppRoles.All.Select(r => $"{r.Value}|{r.AppRoleId}").ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(platform.SetEquals(note), Diff("PlatformBffGraphAppRoles.All", platform, "the note's t261-platform-set", note));
        // the platform set is never the stamp set's superset by accident: every stamp role is also a platform role
        Assert.True(GraphAppRoles.All.All(r => platform.Contains($"{r.Value}|{r.AppRoleId}")),
            "The platform catalog must include every stamp role (the platform BFF runs the same SPE and mail code).");
    }

    // ── rule 3 ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "T261: every documented role has an evidence row (kept tables) with a call site and a Microsoft Learn URL in the same row")]
    public void EveryRole_HasAnEvidenceRow_InTheKeptTables()
    {
        var text = File.ReadAllText(NotePath);
        // Rows count only in the inventory (§3) and the control-plane table (§4), never in §7.1 (dropped roles) or prose.
        var start = text.IndexOf("\n## 3.", StringComparison.Ordinal);
        var end = text.IndexOf("\n## 5.", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "The note's '## 3.' … '## 5.' evidence sections were not found.");
        var section = text[start..end];

        var roles = ReadBlock(text, "t261-stamp-set")
            .Concat(ReadBlock(text, "t261-control-plane-set"))
            .Concat(ReadBlock(text, "t261-platform-set"))
            .Select(r => r[0]).Distinct();

        var missing = roles.Where(role => !HasEvidenceRow(section, role)).ToList();

        Assert.True(missing.Count == 0,
            "Role(s) with no evidence row in §3/§4 of " + NotePath + " (a table row naming the role, a `File.cs:line` call site " +
            $"and a learn.microsoft.com URL): {string.Join(", ", missing)}. Add the call site and the Learn citation first.");
    }

    internal static bool HasEvidenceRow(string section, string role)
        => section.Split('\n')
            .Where(l => l.TrimStart().StartsWith('|'))
            .Any(row => Regex.IsMatch(row, $@"(?<![\w.]){Regex.Escape(role)}(?![\w.])")
                        && Regex.IsMatch(row, @"\w\.cs:\d+")
                        && row.Contains("learn.microsoft.com", StringComparison.Ordinal));

    // ── rule 4 ─────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "T261: demo registration (directory writes) is registered and mapped only behind IsDemoProvisioningEnabled")]
    public void DemoRegistration_IsGated()
    {
        var module = Strip(File.ReadAllText(Path.Combine(BffRoot, "Infrastructure", "DI", "RegistrationModule.cs")));
        var gate = module.IndexOf("if (IsDemoProvisioningEnabled(configuration))", StringComparison.Ordinal);
        Assert.True(gate > 0, "RegistrationModule no longer gates the demo registration feature on IsDemoProvisioningEnabled.");
        var gatedBlock = module[gate..];
        foreach (var registration in new[] { "AddSingleton<GraphUserService>", "AddSingleton<DemoProvisioningService>", "AddHostedService<DemoExpirationService>" })
        {
            Assert.True(Regex.Matches(module, Regex.Escape(registration)).Count == 1 && gatedBlock.Contains(registration, StringComparison.Ordinal),
                $"{registration} must appear once, inside the IsDemoProvisioningEnabled block (task 261).");
        }

        var mapping = Strip(File.ReadAllText(Path.Combine(BffRoot, "Infrastructure", "DI", "EndpointMappingExtensions.cs")));
        Assert.Matches(new Regex(@"if \(RegistrationModule\.IsDemoProvisioningEnabled\(app\.Configuration\)\)\s*\{\s*app\.MapRegistrationEndpoints\(\);"), mapping);
        Assert.Single(Regex.Matches(mapping, @"MapRegistrationEndpoints\("));
    }

    [Fact(DisplayName = "T261: no stamp deployment channel writes DemoProvisioning settings")]
    public void NoStampChannel_WritesDemoProvisioning()
    {
        var channels = new[]
            {
                Path.Combine(SourceScan.RepoRoot, "scripts", "canonical-secret-catalog", "manifest.yaml"),
            }
            .Concat(Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "infrastructure", "bicep"), "*.bicep*", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src", "server", "services"), "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                            && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));

        var writers = channels.Where(f => Regex.IsMatch(File.ReadAllText(f), @"DemoProvisioning(__|:)")).ToList();

        Assert.True(writers.Count == 0,
            "A stamp deployment channel writes DemoProvisioning settings, which turns on the demo registration feature " +
            "(directory writes) on customer stamps: " + string.Join(", ", writers.Select(w => Path.GetRelativePath(SourceScan.RepoRoot, w))));
    }

    // ── negative controls ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "T261 control: the note block parser reads rows and the grant-script parser reads the three shapes")]
    public void Control_ParsersFire()
    {
        var block = ReadBlock("x\n```t261-stamp-set\nA.B | 11111111-1111-1111-1111-111111111111 | entra\n\n```\n", "t261-stamp-set");
        Assert.Single(block);
        Assert.Equal("entra", block[0][2]);

        var parsed = ParseLikeTheGrantScript(
            "public const string GraphResourceAppId = \"00000003-0000-0000-c000-000000000000\";\n" +
            "public const string A = \"A.Read\";\nprivate const string IdA = \"22222222-2222-2222-2222-222222222222\";\n" +
            "new GraphAppRole(A, \"Display\", IdA, \"x\", \"y\"),");
        Assert.Equal(new[] { "A.Read|22222222-2222-2222-2222-222222222222" }, parsed);

        var mirror = ParseL2Mirror("new GraphAppRoleEntry(\"B.Read\", \"33333333-3333-3333-3333-333333333333\"),");
        Assert.Equal(new[] { "B.Read|33333333-3333-3333-3333-333333333333" }, mirror);

        const string sec = "| Role | calls |\n| `X.Read` | `Foo.cs:12` [u](https://learn.microsoft.com/x) |\n| `Y.Read` | no call site [u](https://learn.microsoft.com/y) |\n| `Z.Read` | `Bar.cs:3` no url |\n";
        Assert.True(HasEvidenceRow(sec, "X.Read"));
        Assert.False(HasEvidenceRow(sec, "Y.Read"), "no call site");
        Assert.False(HasEvidenceRow(sec, "Z.Read"), "no Learn URL");
        Assert.False(HasEvidenceRow(sec, "X.Read.All"), "a longer role name is not a match");
        Assert.Throws<InvalidOperationException>(() => ReadBlock("no block here", "t261-stamp-set"));
        Assert.Throws<InvalidOperationException>(() => ParseLikeTheGrantScript("new GraphAppRole(Missing, \"D\", IdMissing,"));
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────

    /// <summary>The rows of a fenced block whose info string is <paramref name="name"/>; cells split on '|'.</summary>
    private static List<string[]> ReadBlock(string text, string name)
    {
        var match = Regex.Match(text, $@"```{Regex.Escape(name)}\r?\n(?<body>.*?)```", RegexOptions.Singleline);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Block ```{name} not found in the task 261 note.");
        }
        var rows = match.Groups["body"].Value.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l => l.Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
        if (rows.Count == 0)
        {
            throw new InvalidOperationException($"Block ```{name} is empty.");
        }
        return rows;
    }

    /// <summary>The same three shapes <c>scripts/Grant-GraphAppRoles.ps1</c> parses: value consts, Id consts, rows.</summary>
    private static HashSet<string> ParseLikeTheGrantScript(string source)
    {
        var values = Regex.Matches(source, @"public\s+const\s+string\s+(\w+)\s*=\s*""([^""]+)""\s*;")
            .Where(m => m.Groups[1].Value != "GraphResourceAppId")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var ids = Regex.Matches(source, @"private\s+const\s+string\s+(Id\w+)\s*=\s*""([0-9a-fA-F-]{36})""\s*;")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match row in Regex.Matches(source, @"new\s+GraphAppRole\s*\(\s*(\w+)\s*,\s*""([^""]+)""\s*,\s*(\w+)"))
        {
            if (!values.TryGetValue(row.Groups[1].Value, out var value) || !ids.TryGetValue(row.Groups[3].Value, out var id))
            {
                throw new InvalidOperationException($"Parser desync: row references unknown constant '{row.Groups[1].Value}' / '{row.Groups[3].Value}'.");
            }
            result.Add($"{value}|{id}");
        }
        if (result.Count == 0)
        {
            throw new InvalidOperationException("Parser desync: zero GraphAppRole rows.");
        }
        return result;
    }

    private static HashSet<string> ParseL2Mirror(string source)
        => Regex.Matches(source, @"new\s+GraphAppRoleEntry\s*\(\s*""([^""]+)""\s*,\s*""([0-9a-fA-F-]{36})""\s*\)")
            .Select(m => $"{m.Groups[1].Value}|{m.Groups[2].Value}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> ParseExchangeScoped(string source)
    {
        var block = Regex.Match(source, @"ExchangeScopedValues\s*=\s*new\s+HashSet<string>\([^)]*\)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        if (!block.Success)
        {
            throw new InvalidOperationException("IGraphAppRolesRegistry.ExchangeScopedValues initializer not found.");
        }
        return Regex.Matches(Strip(block.Groups["body"].Value), @"""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static string Strip(string source)
        => string.Join('\n', source.Split('\n').Select(l =>
        {
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static string Diff(string leftName, IReadOnlySet<string> left, string rightName, IReadOnlySet<string> right)
        => $"{leftName} and {rightName} differ. Only in {leftName}: [{string.Join(", ", left.Except(right))}]. " +
           $"Only in {rightName}: [{string.Join(", ", right.Except(left))}]. A role is added only with an evidence row " +
           "(call site + Microsoft Learn least-privileged citation) in projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md.";
}
