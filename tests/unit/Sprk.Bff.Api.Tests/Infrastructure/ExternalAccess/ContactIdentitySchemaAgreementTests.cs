using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 141, owner round 4 item 4 (B2) — the schema script
/// (<c>scripts/Set-ContactIdentityBindingSchema.ps1</c>) and the BFF agree on WHICH column carries the alternate key
/// and WHICH is field-secured, and the script never asks Dataverse for both on one column.
/// </summary>
/// <remarks>
/// <para><b>Why a source guard.</b> The original design put the alternate key AND field-level security on
/// <c>contact.sprk_externalobjectid</c>. Dataverse refuses an alternate key on a field-secured column, every
/// behavioural test stayed green (the in-memory store modelled the combination), and the conflict surfaced only in
/// review. The owner's answer moved the key to the unsecured mirror <c>sprk_externalobjectidkey</c>; this pins it:
/// the BFF creates by the key the script defines, the script secures the column every reader resolves by, the two
/// never coincide, and the script's choice options cover every value the BFF writes.</para>
/// <para>The script is read as text (no PowerShell host, no network). The analyser is itself tested on seeded
/// snippets, so a regression in the guard reddens too.</para>
/// </remarks>
[Trait("Category", "Security")]
public class ContactIdentitySchemaAgreementTests
{
    [Fact]
    public void TheScript_KeysTheMirror_SecuresTheBinding_AndNeverBothOnOneColumn()
    {
        var schema = Analyse(File.ReadAllText(ScriptPath()));

        schema.KeyAttributes.Should().Equal(new[] { ContactBindingDecision.KeyMirrorColumn },
            "the BFF creates by contacts(sprk_externalobjectidkey='…'); a key on any other column breaks every create");
        schema.SecuredContactAttributes.Should().Contain(ContactBindingDecision.ExternalObjectIdColumn,
            "the binding decides whose grants a caller inherits; only the BFF may write it");
        schema.SecuredContactAttributes.Should().NotContain(ContactBindingDecision.KeyMirrorColumn,
            "Dataverse cannot key a field-secured column");
        schema.Violations.Should().BeEmpty();
    }

    [Fact]
    public void TheScriptsChoices_CoverEveryValueTheBffWrites()
    {
        var schema = Analyse(File.ReadAllText(ScriptPath()));

        schema.ReasonOptions.Should().BeEquivalentTo(Enum.GetValues<IdentityCollisionReason>().Select(v => (int)v),
            "a reason the choice lacks makes the flag write fail, and the operator never sees the collision");
        schema.PlaneOptions.Should().BeEquivalentTo(Enum.GetValues<IdentityPlaneMarker>().Select(v => (int)v));
    }

    // ── The guard bites: the analyser on seeded scripts ───────────────────────────────────────────────

    private const string Seed = """
        $BindingColumn = 'sprk_externalobjectid'
        $MirrorColumn = 'sprk_externalobjectidkey'
        $PlaneOptions = [ordered]@{ 100000000 = 'External'; 100000001 = 'Workforce' }
        $ReasonOptions = [ordered]@{
            100000000 = 'Bound to a different oid'
        }
        $SecuredFields = @(
            @{ Entity = 'contact'; Attribute = $BindingColumn },
            @{ Entity = 'systemuser'; Attribute = 'sprk_primarycontact' }
        )
        $KeyAttributes = @($MirrorColumn)
        """;

    [Fact]
    public void TheAnalyser_PassesTheB2Shape()
        => Analyse(Seed).Violations.Should().BeEmpty();

    [Theory]
    [InlineData("$KeyAttributes = @($MirrorColumn)", "$KeyAttributes = @($BindingColumn)")]
    [InlineData("$KeyAttributes = @($MirrorColumn)", "$KeyAttributes = @('sprk_externalobjectid')")]
    [InlineData("@{ Entity = 'systemuser'; Attribute = 'sprk_primarycontact' }", "@{ Entity = 'contact'; Attribute = $MirrorColumn }")]
    public void TheAnalyser_FlagsAKeyOnAFieldSecuredColumn(string from, string to)
        => Analyse(Seed.Replace(from, to)).Violations.Should().NotBeEmpty();

    // ── Analyser ─────────────────────────────────────────────────────────────────────────────────────

    private sealed record SchemaShape(
        IReadOnlyList<string> KeyAttributes,
        IReadOnlyList<string> SecuredContactAttributes,
        IReadOnlyList<int> ReasonOptions,
        IReadOnlyList<int> PlaneOptions,
        IReadOnlyList<string> Violations);

    private static SchemaShape Analyse(string script)
    {
        var variables = Regex.Matches(script, @"^\s*\$(?<name>\w+)\s*=\s*'(?<value>[^']*)'\s*(#.*)?$", RegexOptions.Multiline)
            .GroupBy(m => m.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Groups["value"].Value, StringComparer.OrdinalIgnoreCase);

        string Resolve(string token)
        {
            token = token.Trim();
            if (token.StartsWith('\'') && token.EndsWith('\'')) return token.Trim('\'');
            if (token.StartsWith('$') && variables.TryGetValue(token[1..], out var value)) return value;
            return $"<unresolved {token}>";
        }

        var keyLine = Regex.Match(script, @"^\s*\$KeyAttributes\s*=\s*@\((?<items>[^)]*)\)", RegexOptions.Multiline);
        var keys = keyLine.Success
            ? keyLine.Groups["items"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Resolve).ToList()
            : new List<string>();

        var secured = Regex.Matches(script, @"@\{\s*Entity\s*=\s*'contact'\s*;\s*Attribute\s*=\s*(?<attr>[^}]+?)\s*\}")
            .Select(m => Resolve(m.Groups["attr"].Value))
            .ToList();

        static List<int> Options(string script, string name)
        {
            var block = Regex.Match(script, $@"\${name}\s*=\s*\[ordered\]@\{{(?<body>.*?)\}}", RegexOptions.Singleline);
            return block.Success
                ? Regex.Matches(block.Groups["body"].Value, @"(?<value>1\d{8})\s*=").Select(m => int.Parse(m.Groups["value"].Value)).ToList()
                : new List<int>();
        }

        var violations = new List<string>();
        if (keys.Count == 0) violations.Add("no $KeyAttributes found");
        violations.AddRange(keys.Where(k => k.StartsWith("<unresolved", StringComparison.Ordinal)).Select(k => $"key column {k}"));
        violations.AddRange(keys.Intersect(secured, StringComparer.OrdinalIgnoreCase)
            .Select(k => $"contact.{k} is both an alternate-key column and field-secured"));

        return new SchemaShape(keys, secured, Options(script, "ReasonOptions"), Options(script, "PlaneOptions"), violations);
    }

    private static string ScriptPath() => Path.Combine(RepoRoot(), "scripts", "Set-ContactIdentityBindingSchema.ps1");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            // `.git` is a FILE in a git worktree, which is how this repo is normally developed.
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
