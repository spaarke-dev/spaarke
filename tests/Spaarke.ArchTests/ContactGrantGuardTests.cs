using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 140 (#1063) — the contact-side Grant Access routes stay inside the owner's rules, and
/// the issuer column the code binds is the column the schema script creates.
/// </summary>
/// <remarks>
/// <para><b>What these pin, and why structurally.</b> The behavioural suite
/// (<c>tests/integration/auth/UnifiedAccessControl/ContactGrantAuthorizationTests.cs</c>) proves each refusal on a request
/// that reaches it. These guards cover the parts a request cannot reach — what the route files are able to DO at all:</para>
/// <list type="number">
/// <item>No onboarding, contact creation or membership write on the contact-side surface (owner decision G1 (a) /
/// escalation trigger 6): a contact never brings in a new person and never alters an organization membership, on either
/// sign-in plane. A future "invite a new colleague" shortcut would have to delete this guard to compile its way in.</item>
/// <item>The contact-typed issuer column: the navigation property the BFF binds, the column it selects and the target
/// entity are exactly what <c>scripts/Deploy-ExternalRecordAccessContactGrantor.ps1</c> creates. A mismatch is a 400 on
/// every grant read in production (the deploy-order hazard the script documents).</item>
/// <item>No UNCONDITIONAL grant-row write on the contact-side surface (session 27 round 42 item 1): a contact's revoke
/// deactivates through <c>ExternalGrantLifecycle.DeactivateIfUnchangedAsync</c> (If-Match), and its grant goes through the
/// grant core's contact-issuer mode, whose writes are conditional. The behavioural race tests prove the conditional writes;
/// this keeps a future contact route from adding an unconditional one beside them.</item>
/// </list>
/// <para>Crude by design (<see cref="SourceScan"/>): text, not syntax. Each rule was seeded with its violation and seen to
/// go red before this file was committed (task 140 notes).</para>
/// </remarks>
public class ContactGrantGuardTests
{
    private const string EndpointsFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ContactGrantEndpoints.cs";
    private const string FilterFile = "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ContactGrantorAuthorizationFilter.cs";
    private const string LifecycleFile = "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs";
    private const string SchemaScript = "scripts/Deploy-ExternalRecordAccessContactGrantor.ps1";

    private static string CodeOf(string relativePath) =>
        SourceScan.CodeText(File.ReadAllLines(Path.Combine(SourceScan.RepoRoot, relativePath)));

    private static string TextOf(string relativePath) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath));

    /// <summary>Calls and entity sets the contact-side surface must never reach.</summary>
    private static readonly (string Pattern, string Why)[] Forbidden =
    {
        (@"\bProvisionAsync\s*\(", "CIAM onboarding (InviteExternalUserEndpoint.ProvisionAsync)"),
        (@"\bCreateCiamUserAsync\s*\(", "creating a CIAM account"),
        (@"\bCreateContactForOidAsync\s*\(", "creating a contact"),
        (@"\bBindOidAsync\s*\(|\bBindInvitedContactAsync\s*\(", "binding a sign-in to a contact"),
        (@"sprk_contactorganization", "reading or writing an organization membership row directly"),
        (@"\bCreateAsync\s*\(\s*""contacts""", "creating a contact through the Dataverse client"),
    };

    [Theory(DisplayName = "Task 140 (owner G1 a): the contact-side surface never onboards, creates a contact or writes a membership")]
    [InlineData(EndpointsFile)]
    [InlineData(FilterFile)]
    public void TheContactSideSurfaceHasNoOnboardingOrMembershipPath(string relativePath)
    {
        var code = CodeOf(relativePath);

        var hits = Forbidden
            .Where(f => Regex.IsMatch(code, f.Pattern, RegexOptions.CultureInvariant))
            .Select(f => f.Why)
            .ToList();

        Assert.True(
            hits.Count == 0,
            $"{relativePath} reaches {string.Join("; ", hits)}. A contact grants only to EXISTING active colleagues of its own " +
            "organization (owner decisions Q2 and G1 (a), session 27): it never creates a person, provisions a sign-in, or " +
            "creates or alters a sprk_contactorganization membership — a membership silently widens every organization-wide " +
            "grant to that organization on every record.");
    }

    /// <summary>Unconditional grant-row writes — what the contact-side surface must never send itself.</summary>
    private static readonly (string Pattern, string Why)[] UnconditionalWrites =
    {
        (@"\.UpdateAsync\s*\(", "an unconditional PATCH (DataverseWebApiClient.UpdateAsync)"),
        (@"\bDeactivateAsync\s*\(", "an unconditional deactivation (ExternalGrantLifecycle.DeactivateAsync)"),
        (@"\bBulkUpdateAsync\s*\(", "an unconditional SDK bulk write"),
    };

    [Theory(DisplayName = "Task 140 (session 27 round 42 item 1): the contact-side surface writes a grant row only conditionally (If-Match)")]
    [InlineData(EndpointsFile)]
    [InlineData(FilterFile)]
    public void TheContactSideSurfaceWritesGrantRowsOnlyConditionally(string relativePath)
    {
        var code = CodeOf(relativePath);

        var hits = UnconditionalWrites
            .Where(f => Regex.IsMatch(code, f.Pattern, RegexOptions.CultureInvariant))
            .Select(f => f.Why)
            .ToList();

        Assert.True(
            hits.Count == 0,
            $"{relativePath} sends {string.Join("; ", hits)}. A contact may change only rows it issued, and it decides that from " +
            "a read; an internal user can take such a row over after that read (round 34 item 3). Every contact-side write is " +
            "therefore conditional on the version the read returned — ExternalGrantLifecycle.DeactivateIfUnchangedAsync, or " +
            "the grant core's contact-issuer mode (DataverseWebApiClient.UpdateIfMatchAsync) — so a take-over in between makes " +
            "the write fail (409 managed_elsewhere) instead of being overwritten.");
    }

    [Fact(DisplayName = "Task 140: the issuer column the BFF binds and selects is the one the schema script creates")]
    public void TheIssuerColumnAgreesWithTheSchemaScript()
    {
        var script = TextOf(SchemaScript);
        string ScriptValue(string variable)
        {
            var m = Regex.Match(script, $@"^\${variable}\s*=\s*'([^']+)'", RegexOptions.Multiline);
            Assert.True(m.Success, $"{SchemaScript} no longer declares ${variable}.");
            return m.Groups[1].Value;
        }

        var column = ScriptValue("Column");
        var navigation = ScriptValue("ExpectedNavigationProperty");
        var schemaName = ScriptValue("ColumnSchemaName");
        var target = ScriptValue("TargetEntity");

        var lifecycle = TextOf(LifecycleFile);
        var navConst = Regex.Match(lifecycle, @"GrantedByContactNavigationProperty\s*=\s*""([^""]+)""");
        Assert.True(navConst.Success, "ExternalGrantLifecycle.GrantedByContactNavigationProperty was not found.");

        Assert.Equal("sprk_grantedbycontact", column);
        Assert.Equal(navigation, navConst.Groups[1].Value);
        Assert.Equal(schemaName, navigation);
        Assert.Equal("contact", target);
        Assert.Contains($"_{column}_value", lifecycle, StringComparison.Ordinal);
        Assert.StartsWith("sprk_", column, StringComparison.Ordinal);

        // The LOGICAL name the SDK take-over write clears (set-record-share-expiry's bulk update, session 27 round 34
        // item 3) is the same column — a wrong name there is a transaction fault on every record with a contact-issued share.
        var logicalConst = Regex.Match(lifecycle, @"GrantedByContactAttribute\s*=\s*""([^""]+)""");
        Assert.True(logicalConst.Success, "ExternalGrantLifecycle.GrantedByContactAttribute was not found.");
        Assert.Equal(column, logicalConst.Groups[1].Value);
    }

    /// <summary>
    /// The schema script's <c>-Verify</c> (gate G-140-1) can pass: <c>sprk_externalrecordaccess</c> is in SpaarkeCore with
    /// <c>rootcomponentbehavior = 0</c> (live, read-only, 2026-10-04), so the new column and relationship get NO
    /// <c>solutioncomponents</c> row of their own and <c>AddSolutionComponent</c> on them is a no-op. Each of them must
    /// therefore carry its table's MetadataId into the shared helper's decision (<c>ViaTable</c>) — without it the column
    /// and relationship read MISSING forever (task 140 verifier r1 item 3, the batch-4 133/143 false negative).
    /// <c>SchemaScriptSolutionMembershipGuardTests</c> pins the helper itself; this pins this script's use of it.
    /// </summary>
    [Fact(DisplayName = "Task 140: the schema script decides the column's and relationship's solution membership through their table")]
    public void TheSchemaScriptCountsTheColumnAndRelationshipThroughTheirTable()
    {
        var script = TextOf(SchemaScript);

        Assert.Contains(". (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')", script, StringComparison.Ordinal);
        Assert.DoesNotContain("solutioncomponents?", script, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(new Regex(@"Test-DvInSolution\s+-Membership\s+\$membership\s+-ComponentId\s+\$c\.Id\s+-TableMetadataId\s+\$c\['TableId'\]"), script);

        var subcomponents = Regex.Matches(script, @"\$components\.Add\(@\{[^}]*Type\s*=\s*(2|10|14)\s*;[^}]*\}\)");
        Assert.Equal(3, subcomponents.Count); // the lookup (2), its relationship (10) and the provenance column (2, round 50 item 2)
        Assert.All(subcomponents, m => Assert.Contains("TableId = $tableId", m.Value, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Task 140 (session 27 round 50 item 2): the provenance column the BFF selects and writes is the one the schema script creates")]
    public void TheProvenanceColumnAgreesWithTheSchemaScript()
    {
        var script = TextOf(SchemaScript);
        string ScriptValue(string variable)
        {
            var m = Regex.Match(script, $@"^\${variable}\s*=\s*'([^']+)'", RegexOptions.Multiline);
            Assert.True(m.Success, $"{SchemaScript} no longer declares ${variable}.");
            return m.Groups[1].Value;
        }

        var column = ScriptValue("ProvenanceColumn");
        var schemaName = ScriptValue("ProvenanceColumnSchemaName");

        var lifecycle = TextOf(LifecycleFile);
        var constant = Regex.Match(lifecycle, @"GrantedByContactIdAttribute\s*=\s*""([^""]+)""");
        Assert.True(constant.Success, "ExternalGrantLifecycle.GrantedByContactIdAttribute was not found.");

        Assert.Equal("sprk_grantedbycontactid", column);
        Assert.Equal(column, constant.Groups[1].Value);
        Assert.Equal(column, schemaName.ToLowerInvariant()); // a custom column's logical name is its schema name, lower-cased
        Assert.StartsWith("sprk_", column, StringComparison.Ordinal);
        // Every grant-row read selects it (a plain column: its logical name IS its $select name) — the deploy-order gate.
        Assert.Matches(new Regex(@"RowSelect\s*=[^;]*\+\s*GrantedByContactIdAttribute\s*;", RegexOptions.Singleline), lifecycle);
    }

    /// <summary>The spellings a write of the contact-issuer LOOKUP takes in BFF source: the Web API bind key and the SDK attribute key.</summary>
    private static readonly Regex LookupWrite = new(
        @"(\{(ExternalGrantLifecycle\.)?GrantedByContactNavigationProperty\}@odata\.bind""|""sprk_GrantedByContact@odata\.bind""|\[(ExternalGrantLifecycle\.)?GrantedByContactAttribute|\[""sprk_grantedbycontact"")\]\s*=",
        RegexOptions.CultureInvariant);

    /// <summary>The spellings a write of its PROVENANCE takes.</summary>
    private static readonly Regex ProvenanceWrite = new(
        @"(\[(ExternalGrantLifecycle\.)?GrantedByContactIdAttribute|\[""sprk_grantedbycontactid"")\]\s*=",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Session 27 round 50 item 2: <c>sprk_grantedbycontactid</c> means "the issuer was deleted" only while EVERY write that
    /// sets or clears the issuer lookup sets or clears the provenance in the same write. The behavioural tests prove each
    /// writer that exists (the contact grant's create, the grant core's take-over, set-record-share-expiry's take-over); this
    /// keeps a NEW writer of the lookup from appearing without the provenance beside it.
    /// </summary>
    /// <remarks>
    /// <para><b>Exactly what this enforces</b> (text, per file, under <c>src/server</c>): a file that assigns the issuer lookup by
    /// one of its four key spellings (<see cref="LookupWrite"/>) assigns the provenance (<see cref="ProvenanceWrite"/>) at least
    /// as many times; and the files that assign the lookup are exactly the two known writers — so a regex that silently
    /// stopped matching, or a writer added elsewhere, fails here and is reviewed. It does NOT see a key held in a variable or
    /// built at run time, and it does not prove the two assignments reach the same request — the behavioural tests do.</para>
    /// </remarks>
    [Fact(DisplayName = "Task 140 (session 27 round 50 item 2): every writer of the issuer lookup also writes its provenance")]
    public void EveryWriterOfTheIssuerLookupAlsoWritesItsProvenance()
    {
        var writers = new List<string>();
        var unpaired = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(SourceScan.RepoRoot, "src", "server"), "*.cs", SearchOption.AllDirectories))
        {
            var code = SourceScan.CodeText(File.ReadAllLines(file));
            var lookupWrites = LookupWrite.Matches(code).Count;
            if (lookupWrites == 0)
                continue;

            var relative = SourceScan.Relative(file).Replace('\\', '/');
            writers.Add(relative);
            var provenanceWrites = ProvenanceWrite.Matches(code).Count;
            if (provenanceWrites < lookupWrites)
                unpaired.Add($"{relative}: {lookupWrites} write(s) of the issuer lookup, {provenanceWrites} of its provenance");
        }

        Assert.True(
            unpaired.Count == 0,
            "A write that sets or clears sprk_grantedbycontact must set or clear sprk_grantedbycontactid in the SAME write " +
            "(session 27 round 50 item 2) — otherwise a row whose lookup was cleared by a take-over still records a contact, and " +
            "the reconciliation job reads it as a DELETED issuer's grant and ends it:\n  " + string.Join("\n  ", unpaired));

        Assert.Equal(
            new[]
            {
                "src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs",
                "src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalGrantLifecycle.cs",
            },
            writers.OrderBy(w => w, StringComparer.Ordinal).ToArray());
    }
}
