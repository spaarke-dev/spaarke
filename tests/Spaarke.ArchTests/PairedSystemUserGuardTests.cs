using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 193 G1 (owner decision (iii), 2026-10-28) — the pairing column
/// <c>sprk_externalrecordaccess.sprk_pairedsystemuser</c> the BFF binds is the one the schema script creates, it stays out of
/// every grant read until the script has run, it is field-secured, and only the grant core writes it.
/// </summary>
/// <remarks>
/// <para><b>What these pin, and why structurally.</b> The behavioural suites
/// (<c>ContactGrantSharesInternalUserTests</c>, <c>ExternalAccessReconciliationTests</c> R5) prove the pairing is written,
/// adopted, read and reconciled. These guards cover what a request cannot reach:</para>
/// <list type="number">
/// <item>The names: the navigation property, logical name, value property and target agree with
/// <c>scripts/Deploy-ExternalRecordAccessPairedSystemUser.ps1</c>. A mismatch is a 400 on every pairing read in production.</item>
/// <item>The deploy order: the column is NOT in <c>ExternalGrantLifecycle.RowSelect</c> — a <c>$select</c> naming a column the
/// environment lacks 400s every grant read, so the BFF reads it only in its own one-column read (fail closed: no pairing).</item>
/// <item>The script decides its column's and relationship's solution membership through their table (the task 140 lesson).</item>
/// <item>The column is field-secured in the solution source: a caller's own write is refused by field security, and the app-only
/// create path refuses any field-secured column in a caller payload (<c>CallerWriteAuthorization</c> step 3), so a caller can
/// never name a pairing of its own.</item>
/// <item>The writers: only <c>GrantExternalAccessEndpoint.cs</c> (the grant core) SETS the pairing bind — a second writer
/// would have to change this allow-list and be reviewed. Task 193 G4a: only <c>ExternalGrantRevocationService.cs</c> CLEARS
/// it (assigns <c>null</c>), on a paired grant whose end is confirmed, rows and file access alike; an inactive row still
/// paired is one whose file step is not confirmed yet, which the next end for that user retries. A clear names no user, so
/// it needs none of the grant core's checks.</item>
/// </list>
/// <para>Crude by design (<see cref="SourceScan"/>): text, not syntax. Each rule was seeded with its violation and seen to go
/// red before this file was committed (task 193 notes).</para>
/// <para>Enforced by: this file.</para>
/// </remarks>
public class PairedSystemUserGuardTests
{
    private const string LifecycleFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs";
    private const string SchemaScript = "scripts/Deploy-ExternalRecordAccessPairedSystemUser.ps1";
    private const string EntityXml = "src/dataverse/solutions/SpaarkeMaster/Entities/sprk_ExternalRecordAccess/Entity.xml";
    private const string ProfilesXml = "src/dataverse/solutions/SpaarkeMaster/Other/FieldSecurityProfiles.xml";

    private static string TextOf(string relativePath) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath));

    private static string ScriptValue(string script, string variable)
    {
        var m = Regex.Match(script, $@"^\${variable}\s*=\s*'([^']+)'", RegexOptions.Multiline);
        Assert.True(m.Success, $"{SchemaScript} no longer declares ${variable}.");
        return m.Groups[1].Value;
    }

    private static string LifecycleConstant(string lifecycle, string name)
    {
        var m = Regex.Match(lifecycle, $@"\b{name}\s*=\s*""([^""]+)""");
        Assert.True(m.Success, $"ExternalGrantLifecycle.{name} was not found.");
        return m.Groups[1].Value;
    }

    [Fact(DisplayName = "Task 193 G1: the pairing column the BFF binds, selects and scans is the one the schema script creates")]
    public void ThePairingColumnAgreesWithTheSchemaScript()
    {
        var script = TextOf(SchemaScript);
        var column = ScriptValue(script, "Column");
        var navigation = ScriptValue(script, "ExpectedNavigationProperty");
        var schemaName = ScriptValue(script, "ColumnSchemaName");
        var target = ScriptValue(script, "TargetEntity");

        var lifecycle = TextOf(LifecycleFile);

        Assert.Equal("sprk_pairedsystemuser", column);
        Assert.Equal("systemuser", target);
        Assert.Equal(schemaName, navigation);
        Assert.Equal(column, schemaName.ToLowerInvariant());
        Assert.Equal(navigation, LifecycleConstant(lifecycle, "PairedSystemUserNavigationProperty"));
        Assert.Equal(column, LifecycleConstant(lifecycle, "PairedSystemUserAttribute"));
        Assert.Equal($"_{column}_value", LifecycleConstant(lifecycle, "PairedSystemUserValueProperty"));
    }

    [Fact(DisplayName = "Task 193 G1 (deploy order): the pairing column is in no shared grant-row $select")]
    public void ThePairingColumnIsNotInTheSharedGrantRowSelect()
    {
        var lifecycle = TextOf(LifecycleFile);
        var rowSelect = Regex.Match(lifecycle, @"private const string RowSelect\s*=(?<expr>[^;]*);", RegexOptions.Singleline);
        Assert.True(rowSelect.Success, "ExternalGrantLifecycle.RowSelect was not found.");

        Assert.DoesNotContain("pairedsystemuser", rowSelect.Groups["expr"].Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PairedSystemUser", rowSelect.Groups["expr"].Value, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Task 193 G1: the schema script decides the column's and relationship's solution membership through their table")]
    public void TheSchemaScriptCountsTheColumnAndRelationshipThroughTheirTable()
    {
        var script = TextOf(SchemaScript);

        Assert.Contains(". (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("solutioncomponents?", script, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"Test-DvInSolution\s+-Membership\s+\$membership\s+-ComponentId\s+\$c\.Id\s+-TableMetadataId\s+\$c\['TableId'\]"), script);

        var subcomponents = Regex.Matches(script, @"\$components\.Add\(@\{[^}]*Type\s*=\s*(2|10|14)\s*;[^}]*\}\)");
        Assert.Equal(2, subcomponents.Count); // the lookup (2) and its relationship (10)
        Assert.All(subcomponents, m => Assert.Contains("TableId = $tableId", m.Value, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 193 G1: the pairing column is field-secured, readable and writable only through the BFF-managed profiles")]
    public void ThePairingColumnIsFieldSecuredInTheSolutionSource()
    {
        var entity = XDocument.Parse(TextOf(EntityXml));
        var attribute = entity.Descendants("attribute")
            .SingleOrDefault(a => string.Equals((string?)a.Element("LogicalName"), "sprk_pairedsystemuser", StringComparison.Ordinal));
        Assert.True(attribute is not null, $"{EntityXml} declares no sprk_pairedsystemuser attribute.");
        Assert.Equal("1", (string?)attribute!.Element("IsSecured"));

        var profiles = XDocument.Parse(TextOf(ProfilesXml));
        var permissions = profiles.Descendants("FieldPermission")
            .Where(p => string.Equals((string?)p.Element("AttributeName"), "sprk_pairedsystemuser", StringComparison.Ordinal)
                        && string.Equals((string?)p.Element("EntityName"), "sprk_externalrecordaccess", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, permissions.Count); // BFF-Managed Field Readers (read) and Writers (read/create/update)
        Assert.All(permissions, p => Assert.Equal("4", (string?)p.Element("CanRead")));
        Assert.Single(permissions, p => (string?)p.Element("CanUpdate") == "4" && (string?)p.Element("CanCreate") == "4");
    }

    /// <summary>The spellings a write of the pairing takes in BFF source: the Web API bind key and the SDK attribute key.</summary>
    private static readonly Regex PairingWrite = new(
        @"(\{(ExternalGrantLifecycle\.)?PairedSystemUserNavigationProperty\}@odata\.bind""|""sprk_PairedSystemUser@odata\.bind""|\[(ExternalGrantLifecycle\.|ExternalAccessReconciliationJob\.)?PairedSystemUserAttribute|\[""sprk_pairedsystemuser"")\]\s*=",
        RegexOptions.CultureInvariant);

    /// <summary>A write of the pairing that assigns <c>null</c>: a clear (task 193 G4a).</summary>
    private static readonly Regex PairingClear = new(PairingWrite + @"\s*null\b", RegexOptions.CultureInvariant);

    [Fact(DisplayName = "Task 193 G1: only the grant core writes the pairing column")]
    public void OnlyTheGrantCoreWritesThePairing()
    {
        var setters = new List<string>();
        var clearers = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src", "server"), "*.cs", SearchOption.AllDirectories))
        {
            var (sets, clears) = Classify(SourceScan.CodeText(File.ReadAllLines(file)));
            var relative = SourceScan.Relative(file).Replace('\\', '/');
            if (sets)
                setters.Add(relative);
            if (clears)
                clearers.Add(relative);
        }

        Assert.Equal(
            new[] { "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs" },
            setters.OrderBy(w => w, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalGrantRevocationService.cs" },
            clearers.OrderBy(w => w, StringComparer.Ordinal).ToArray());
    }

    /// <summary>Must fire and must not fire: a set is a setter even beside a clear; a clear alone is only a clearer.</summary>
    [Fact(DisplayName = "Task 193 G4a: a pairing set and a pairing clear are told apart")]
    public void ASetAndAClearAreToldApart()
    {
        const string bind = "[\"sprk_PairedSystemUser@odata.bind\"]";
        Assert.Equal((true, false), Classify($"p{bind} = $\"/systemusers({{id}})\";"));
        Assert.Equal((false, true), Classify($"p{bind} = null;"));
        Assert.Equal((true, true), Classify($"a{bind} = null; b{bind} = x;"));
        Assert.Equal((false, false), Classify("var name = \"sprk_PairedSystemUser@odata.bind\";"));
    }

    private static (bool Sets, bool Clears) Classify(string code) =>
        (PairingWrite.Matches(code).Count > PairingClear.Matches(code).Count, PairingClear.IsMatch(code));
}
