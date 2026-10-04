using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// The task 150 standing assertion on <c>sprk_issecure</c>'s field-level security
/// (<see cref="SecureFlagFieldSecurityAssertion"/>) — the same two layers as <c>SecureBuRoleDepthAssertionTests</c>:
/// perturbations that prove every FAIL direction with no tenant, and an opt-in live census that is the manual gate.
/// </summary>
/// <remarks>
/// <para><b>Why the perturbations matter more than the live run.</b> No pipeline in this repo can reach Dataverse, so
/// the live census runs only by hand. An assertion nobody has watched fail is not verified — each fail-open topology
/// below is fed to the evaluator and must be reported.</para>
/// </remarks>
[Trait("Category", "Security")]
public class SecureFlagFieldSecurityAssertionTests
{
    private readonly ITestOutputHelper _output;

    public SecureFlagFieldSecurityAssertionTests(ITestOutputHelper output) => _output = output;

    /// <summary>Comma-separated BFF application (client) ids whose application users must be the writers.</summary>
    public const string BffApplicationIdsVariable = "SPAARKE_BFF_APPLICATION_IDS";

    private static readonly Guid ReaderId = Guid.Parse("f150f150-0000-0000-0000-0000000000a1");
    private static readonly Guid WriterId = Guid.Parse("f150f150-0000-0000-0000-0000000000a2");
    private static readonly Guid SysAdminProfileId = Guid.Parse("f150f150-0000-0000-0000-0000000000a3");
    private static readonly CensusDefaultTeam RootTeam = new(Guid.Parse("f150f150-0000-0000-0000-0000000000b1"), "Spaarke", "Spaarke");
    private static readonly CensusDefaultTeam ChildTeam = new(Guid.Parse("f150f150-0000-0000-0000-0000000000b2"), "Customer BU", "Customer BU");
    private static readonly CensusDefaultTeam SecureBuTeam = new(Guid.Parse("f150f150-0000-0000-0000-0000000000b3"), "Secure Record", "Secure Record");
    private static readonly CensusPrincipal Bff = new(Guid.Parse("f150f150-0000-0000-0000-0000000000c1"), "# mi-bff-api-dev", true);

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 1 — PERTURBATION
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>The configuration task 150 step 6 produces.</summary>
    private static SecureFlagFieldSecurityCensus Passing(
        IReadOnlySet<string>? secured = null,
        IReadOnlyList<CensusFieldSecurityProfile>? profiles = null,
        IReadOnlyList<CensusFieldPermission>? permissions = null,
        IReadOnlyList<CensusDefaultTeam>? defaultTeams = null,
        IReadOnlyList<CensusPrincipal>? bffUsers = null,
        IReadOnlyList<string>? unresolved = null,
        IReadOnlyList<CensusConfiguredWriter>? configuredWriters = null)
    {
        var teams = defaultTeams ?? new[] { RootTeam, ChildTeam, SecureBuTeam };
        return new SecureFlagFieldSecurityCensus(
            secured ?? SecureFlagFieldSecurityAssertion.Tables.ToHashSet(),
            profiles ?? new[]
            {
                new CensusFieldSecurityProfile(ReaderId, SecureFlagFieldSecurityAssertion.ReaderProfileName,
                    Array.Empty<CensusPrincipal>(), teams.Select(t => new CensusPrincipal(t.Id, t.Name)).ToArray()),
                new CensusFieldSecurityProfile(WriterId, SecureFlagFieldSecurityAssertion.WriterProfileName,
                    new[] { Bff }, Array.Empty<CensusPrincipal>()),
                new CensusFieldSecurityProfile(SysAdminProfileId, SecureFlagFieldSecurityAssertion.SystemAdministratorProfileName,
                    Array.Empty<CensusPrincipal>(), Array.Empty<CensusPrincipal>())
            },
            permissions ?? SecureFlagFieldSecurityAssertion.Tables.SelectMany(t => new[]
            {
                new CensusFieldPermission(t, ReaderId, 4, 0, 0),
                new CensusFieldPermission(t, WriterId, 4, 4, 4),
                new CensusFieldPermission(t, SysAdminProfileId, 4, 4, 4)
            }).ToArray(),
            teams,
            bffUsers ?? new[] { Bff },
            unresolved ?? Array.Empty<string>(),
            new[] { "user 'Ralph Schroeder'", "TEAM 'Spaarke Demo'" },
            configuredWriters ?? Array.Empty<CensusConfiguredWriter>());
    }

    private static IReadOnlyList<CensusFieldPermission> PermissionsWithout(Func<CensusFieldPermission, bool> drop)
        => Passing().Permissions.Where(p => !drop(p)).ToArray();

    [Fact]
    public void Evaluate_TheConfigurationStep6Produces_Passes_AndListsTheSystemAdministratorHolders()
    {
        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing());

        outcome.Passed.Should().BeTrue(outcome.Message);
        outcome.Message.Should().Contain("Ralph Schroeder").And.Contain("Spaarke Demo",
            "owner decision F4: the residual System Administrator writers are accepted and LISTED, never hidden");
    }

    [Fact]
    public void Evaluate_WhenTheBffApplicationUserIsNotInTheWriterProfile_Fails()
    {
        // The acceptance criterion's "census lacking the BFF read" — the standing-grant precedent's exact miss.
        var profiles = Passing().Profiles
            .Select(p => p.Id == WriterId ? p with { Users = Array.Empty<CensusPrincipal>() } : p).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(profiles: profiles));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("BFF READ").And.Contain(Bff.Name);
    }

    [Fact]
    public void Evaluate_WhenAnExtraProfileCanWriteTheFlag_Fails()
    {
        // The acceptance criterion's "listing an extra writer".
        var rogue = Guid.NewGuid();
        var profiles = Passing().Profiles.Append(new CensusFieldSecurityProfile(
            rogue, "Matter Editors", Array.Empty<CensusPrincipal>(), Array.Empty<CensusPrincipal>())).ToArray();
        var permissions = Passing().Permissions.Append(new CensusFieldPermission("sprk_matter", rogue, 4, 0, 4)).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(profiles: profiles, permissions: permissions));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("EXTRA WRITER").And.Contain("Matter Editors").And.Contain("sprk_matter");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluate_WhenTheWriterProfileHasAMemberBesidesTheBff_Fails(bool isTeam)
    {
        var extra = new CensusPrincipal(Guid.NewGuid(), isTeam ? "Legal Ops" : "Chelsea Friez");
        var profiles = Passing().Profiles.Select(p => p.Id != WriterId
            ? p
            : isTeam ? p with { Teams = new[] { extra } } : p with { Users = new[] { Bff, extra } }).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(profiles: profiles));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("EXTRA WRITER").And.Contain(extra.Name);
    }

    [Fact]
    public void Evaluate_WhenABusinessUnitsDefaultTeamIsNotOnTheReaderProfile_Fails()
    {
        // A business unit created after the lock: its users read the flag EMPTY.
        var profiles = Passing().Profiles.Select(p => p.Id == ReaderId
            ? p with { Teams = new[] { new CensusPrincipal(RootTeam.Id, RootTeam.Name), new CensusPrincipal(SecureBuTeam.Id, SecureBuTeam.Name) } }
            : p).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(profiles: profiles));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("MASKED READERS").And.Contain(ChildTeam.BusinessUnitName);
    }

    [Theory]
    [InlineData("sprk_project")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_workassignment")]
    [InlineData("sprk_invoice")]   // owner round 10 item 11: the invoice's copy is locked like the roots'
    public void Evaluate_WhenOneTableIsNotSecured_Fails(string table)
    {
        var secured = SecureFlagFieldSecurityAssertion.Tables.Where(t => t != table).ToHashSet();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(secured: secured));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("NOT LOCKED").And.Contain(table);
    }

    [Fact]
    public void Evaluate_WhenTheReaderProfileHasNoPermissionOnATable_Fails()
    {
        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(
            permissions: PermissionsWithout(p => p.ProfileId == ReaderId && p.Table == "sprk_workassignment")));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("MISSING PERMISSION").And.Contain("sprk_workassignment");
    }

    [Fact]
    public void Evaluate_WhenTheWriterProfileCannotUpdate_Fails()
    {
        var permissions = Passing().Permissions
            .Select(p => p.ProfileId == WriterId && p.Table == "sprk_project" ? p with { CanUpdate = 0 } : p).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(permissions: permissions));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("WRONG PERMISSION").And.Contain("sprk_project");
    }

    [Fact]
    public void Evaluate_WhenTheReaderProfileCanWrite_Fails()
    {
        var permissions = Passing().Permissions
            .Select(p => p.ProfileId == ReaderId && p.Table == "sprk_matter" ? p with { CanUpdate = 4 } : p).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(permissions: permissions));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain(SecureFlagFieldSecurityAssertion.ReaderProfileName).And.Contain("sprk_matter");
    }

    [Fact]
    public void Evaluate_WithNoBffApplicationIdSupplied_IsNotAPass()
    {
        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(bffUsers: Array.Empty<CensusPrincipal>()));

        outcome.Passed.Should().BeFalse("the BFF's own Read is the invariant the standing-grant precedent missed");
        outcome.Message.Should().Contain("BFF IDENTITY");
    }

    [Fact]
    public void Evaluate_WhenABffApplicationIdHasNoApplicationUser_Fails()
    {
        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(unresolved: new[] { "1e40baad-e065-4aea-a8d4-4b7ab273458c" }));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("1e40baad");
    }

    [Fact]
    public void Evaluate_WithNoDefaultTeamsRead_IsAVacuousCensus_NotAPass()
    {
        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(defaultTeams: Array.Empty<CensusDefaultTeam>()));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("VACUOUS");
    }

    [Fact]
    public void Evaluate_WhenTheReaderProfileIsMissing_Fails()
    {
        var profiles = Passing().Profiles.Where(p => p.Id != ReaderId).ToArray();

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(profiles: profiles));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("READER PROFILE");
    }

    /// <summary>
    /// Clause 5 (task 150 r2, verifier F5): a maker-authored configuration row that writes the flag — a Field Mapping
    /// Framework Copy rule onto a child record, an AI topic-registry target, an email update field — is a writer outside
    /// the endpoints, and once the column is locked it fails every write it drives.
    /// </summary>
    [Theory]
    [InlineData("sprk_fieldmappingrule")]
    [InlineData("sprk_aitopicregistry")]
    [InlineData("sprk_emailupdatefield")]
    public void Evaluate_WhenAConfigurationRowTargetsTheFlag_Fails(string table)
    {
        var row = new CensusConfiguredWriter(table, Guid.Parse("f150f150-0000-0000-0000-0000000000d1"));

        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(Passing(configuredWriters: new[] { row }));

        outcome.Passed.Should().BeFalse();
        outcome.Message.Should().Contain("CONFIGURED WRITER").And.Contain(table).And.Contain(row.RowId.ToString());
    }

    [Fact]
    public void ConfiguredWriterChannels_CoverTheThreeMakerAuthoredWriteTargets()
    {
        SecureFlagFieldSecurityAssertion.ConfiguredWriterChannels.Select(c => c.Table).Should().BeEquivalentTo(
            new[] { "sprk_fieldmappingrule", "sprk_aitopicregistry", "sprk_emailupdatefield" },
            "the live census queries exactly these; dropping one silently stops checking it");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Layer 2 — LIVE (opt-in): the manual gate of task 150 step 6
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reads the live field-security census and asserts every clause. Gated on the NFR-05 URL variable (or the canary's)
    /// AND <see cref="BffApplicationIdsVariable"/>; <c>SPAARKE_NFR05_REQUIRED=true</c> makes an unconfigured run a
    /// failure. A query error is a FAILURE, never a skip.
    /// </summary>
    [Fact]
    [Trait("Category", "LiveDataverseFieldSecurity")]
    public async Task SecureFlagFieldSecurity_InTheTargetEnvironment_ReadsForEveryoneAndWritesOnlyForTheBff()
    {
        var required = string.Equals(
            Environment.GetEnvironmentVariable(SecureBuRoleDepthAssertionTests.RequiredVariable), "true",
            StringComparison.OrdinalIgnoreCase);
        var configured = Environment.GetEnvironmentVariable(SecureBuRoleDepthAssertionTests.EnvironmentUrlVariable)
                         ?? Environment.GetEnvironmentVariable(SecureBuRoleDepthAssertionTests.FallbackEnvironmentUrlVariable);

        if (configured is null && !required)
        {
            _output.WriteLine("sprk_issecure field-security assertion NOT RUN: no environment URL is set. This is NOT a pass.");
            return;
        }

        var serviceUrl = SecureBuRoleDepthAssertionTests.ResolveEnvironmentUrl(configured, required);
        var appIds = (Environment.GetEnvironmentVariable(BffApplicationIdsVariable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var census = await ReadCensusAsync(serviceUrl, appIds);
        var outcome = SecureFlagFieldSecurityAssertion.Evaluate(census);

        _output.WriteLine($"sprk_issecure field-security census for {serviceUrl}: {census.SecuredTables.Count} of " +
                          $"{SecureFlagFieldSecurityAssertion.Tables.Count} table(s) secured, {census.Permissions.Count} field " +
                          $"permission(s), {census.DefaultTeams.Count} default team(s), {census.BffApplicationUsers.Count} BFF " +
                          "application user(s).");
        _output.WriteLine(outcome.Message);
        Console.WriteLine(outcome.Message);

        outcome.Passed.Should().BeTrue(outcome.Message);
    }

    private static async Task<SecureFlagFieldSecurityCensus> ReadCensusAsync(string serviceUrl, IReadOnlyList<string> appIds)
    {
        var token = await new DefaultAzureCredential().GetTokenAsync(
            new TokenRequestContext(new[] { $"{serviceUrl}/.default" }), CancellationToken.None);

        using var http = new HttpClient { BaseAddress = new Uri($"{serviceUrl}/api/data/v9.2/") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var secured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in SecureFlagFieldSecurityAssertion.Tables)
        {
            var attribute = await GetAsync(http,
                $"EntityDefinitions(LogicalName='{table}')/Attributes(LogicalName='{SecureFlagFieldSecurityAssertion.Column}')?$select=IsSecured");
            if (attribute.TryGetProperty("IsSecured", out var isSecured) && isSecured.ValueKind == JsonValueKind.True)
                secured.Add(table);
        }

        var profiles = (await GetAllAsync(http,
                "fieldsecurityprofiles?$select=name,fieldsecurityprofileid"
                + "&$expand=teamprofiles_association($select=teamid,name),systemuserprofiles_association($select=systemuserid,fullname,applicationid)"))
            .Select(row => new CensusFieldSecurityProfile(
                Guid.Parse(row.GetProperty("fieldsecurityprofileid").GetString()!),
                row.GetProperty("name").GetString() ?? string.Empty,
                row.GetProperty("systemuserprofiles_association").EnumerateArray().Select(u => new CensusPrincipal(
                    Guid.Parse(u.GetProperty("systemuserid").GetString()!),
                    u.GetProperty("fullname").GetString() ?? string.Empty,
                    u.TryGetProperty("applicationid", out var app) && app.ValueKind == JsonValueKind.String)).ToArray(),
                row.GetProperty("teamprofiles_association").EnumerateArray().Select(t => new CensusPrincipal(
                    Guid.Parse(t.GetProperty("teamid").GetString()!), t.GetProperty("name").GetString() ?? string.Empty)).ToArray()))
            .ToArray();

        var permissions = (await GetAllAsync(http,
                $"fieldpermissions?$select=entityname,canread,cancreate,canupdate,_fieldsecurityprofileid_value"
                + $"&$filter=attributelogicalname eq '{SecureFlagFieldSecurityAssertion.Column}'"))
            .Select(row => new CensusFieldPermission(
                row.GetProperty("entityname").GetString() ?? string.Empty,
                Guid.Parse(row.GetProperty("_fieldsecurityprofileid_value").GetString()!),
                row.GetProperty("canread").GetInt32(), row.GetProperty("cancreate").GetInt32(),
                row.GetProperty("canupdate").GetInt32()))
            .ToArray();

        var defaultTeams = (await GetAllAsync(http, "teams?$select=teamid,name&$filter=isdefault eq true&$expand=businessunitid($select=name)"))
            .Select(row => new CensusDefaultTeam(
                Guid.Parse(row.GetProperty("teamid").GetString()!),
                row.GetProperty("name").GetString() ?? string.Empty,
                row.GetProperty("businessunitid").GetProperty("name").GetString() ?? string.Empty))
            .ToArray();

        var bffUsers = new List<CensusPrincipal>();
        var unresolved = new List<string>();
        foreach (var appId in appIds)
        {
            var users = await GetAllAsync(http, $"systemusers?$select=systemuserid,fullname&$filter=applicationid eq {Guid.Parse(appId)}");
            if (users.Count == 0) { unresolved.Add(appId); continue; }
            bffUsers.Add(new CensusPrincipal(
                Guid.Parse(users[0].GetProperty("systemuserid").GetString()!), users[0].GetProperty("fullname").GetString() ?? appId, true));
        }

        var holders = new List<string>();
        foreach (var role in await GetAllAsync(http, "roles?$select=roleid&$filter=name eq 'System Administrator'"))
        {
            var roleId = role.GetProperty("roleid").GetString();
            holders.AddRange((await GetAllAsync(http, $"roles({roleId})/systemuserroles_association?$select=fullname,isdisabled"))
                .Where(u => u.GetProperty("isdisabled").ValueKind != JsonValueKind.True)
                .Select(u => $"user '{u.GetProperty("fullname").GetString()}'"));
            holders.AddRange((await GetAllAsync(http, $"roles({roleId})/teamroles_association?$select=name"))
                .Select(t => $"TEAM '{t.GetProperty("name").GetString()}'"));
        }

        // Clause 5: configuration rows whose target column is the flag. The entity set is resolved, never guessed.
        var configuredWriters = new List<CensusConfiguredWriter>();
        foreach (var (table, targetColumn) in SecureFlagFieldSecurityAssertion.ConfiguredWriterChannels)
        {
            var entitySet = (await GetAsync(http, $"EntityDefinitions(LogicalName='{table}')?$select=EntitySetName"))
                .GetProperty("EntitySetName").GetString();
            var rows = await GetAllAsync(http,
                $"{entitySet}?$select={table}id&$filter={targetColumn} eq '{SecureFlagFieldSecurityAssertion.Column}'");
            configuredWriters.AddRange(rows.Select(r => new CensusConfiguredWriter(table, Guid.Parse(r.GetProperty($"{table}id").GetString()!))));
        }

        return new SecureFlagFieldSecurityCensus(
            secured, profiles, permissions, defaultTeams, bffUsers, unresolved, holders.Distinct().ToArray(),
            configuredWriters);
    }

    private static async Task<JsonElement> GetAsync(HttpClient http, string query)
    {
        using var response = await http.GetAsync(query);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"The field-security census query '{query}' failed ({(int)response.StatusCode}). A query failure is a FAILURE, never a skip.\n{body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<IReadOnlyList<JsonElement>> GetAllAsync(HttpClient http, string query)
    {
        var rows = new List<JsonElement>();
        string? next = query;
        while (next is not null)
        {
            var page = await GetAsync(http, next);
            rows.AddRange(page.GetProperty("value").EnumerateArray().Select(e => e.Clone()));
            next = page.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        }

        return rows;
    }
}

/// <summary>
/// Task 150: the lock script, task 133's profile script and the standing assertion agree on the column, the tables and
/// the two profile names — so the lock reuses task 133's profiles rather than forking them, and the assertion checks what
/// the scripts configure. Read as text (no PowerShell host, no network); the parser is exercised on seeded drifts.
/// </summary>
[Trait("Category", "Security")]
public class SecureFlagFieldSecurityScriptAgreementTests
{
    [Fact]
    public void TheLockScript_SecuresTheColumnTheBffReads_OnTheThreeRoots_WithTaskOneThirtyThreesProfiles()
    {
        var lockScript = Analyse(File.ReadAllText(ScriptPath("Set-SecureFlagFieldSecurity.ps1")));
        var profileScript = Analyse(File.ReadAllText(ScriptPath("Set-RecordCreatorPersonSchema.ps1")));

        Agrees(lockScript).Should().BeTrue(
            $"the lock script must secure {SecureFlagFieldSecurityAssertion.Column} on the three roots with the assertion's profiles");
        lockScript.Column.Should().Be(SecurableEntityRegistry.SecureFlagAttribute, "the column the BFF reads and refuses on");
        lockScript.Reader.Should().Be(profileScript.Reader, "task 150 REUSES task 133's reader profile — never a second one");
        lockScript.Writer.Should().Be(profileScript.Writer, "task 150 REUSES task 133's writer profile — never a second one");
        lockScript.Tables.Should().BeEquivalentTo(profileScript.Tables.Append("sprk_invoice"),
            "both lock the three roots; the invoice carries only sprk_issecure, locked too (owner round 10 item 11)");
    }

    /// <summary>
    /// Owner round 10 item 11: the invoice's flag is locked BECAUSE it is no longer a security input — the BFF's registry
    /// leaves it out of the securable set. The two halves move together: a lock without the exclusion would leave an
    /// invoice flagged true refusing every upload with nobody able to clear it; the exclusion without the lock would leave
    /// a user-settable flag that looks meaningful.
    /// </summary>
    [Fact]
    public void TheInvoiceIsLocked_ExactlyBecauseItsFlagIsNotASecurityInput()
    {
        SecureFlagFieldSecurityAssertion.Tables.Should().Contain("sprk_invoice");
        SecurableEntityRegistry.FlagIsNotASecurityInput.Should().BeEquivalentTo(
            SecureFlagFieldSecurityAssertion.Tables.Except(new[] { "sprk_project", "sprk_matter", "sprk_workassignment" }),
            "every locked table that is not a secure root is one whose flag the registry ignores, and vice versa");
    }

    [Fact]
    public void TheBackfillScript_TargetsTheSameColumnAndTables()
    {
        var backfill = Analyse(File.ReadAllText(ScriptPath("Repair-SecureFlagNulls.ps1")));

        backfill.Column.Should().Be(SecureFlagFieldSecurityAssertion.Column);
        backfill.Tables.Should().BeEquivalentTo(SecureFlagFieldSecurityAssertion.Tables);
    }

    /// <summary>
    /// The backfill's <c>-Apply</c> after-check must count the ROWS, not the one List <c>Get-DvAll</c> emits. The function
    /// ends <c>, $rows</c> (the List as ONE pipeline object), so <c>@(Get-DvAll …).Count</c> is always 1: every
    /// <c>-Apply</c> then printed "1 row(s) still hold NULL" per table and exited 1 after a full repair — seen live at G-0
    /// in dev, 2026-10-03, fixed in task 150 c1-r2. A non-zero exit after a correct repair breaks the guide's "each must
    /// exit 0" deploy order, and an operator who learns to ignore it would miss a real residual NULL (which makes the
    /// task 150 BFF refuse uploads). Read as text (no PowerShell host, no network); the detector is exercised on seeds.
    /// </summary>
    [Fact]
    public void TheBackfillScript_CountsTheRowsGetDvAllReturns_NotTheSingleListItEmits()
    {
        var script = File.ReadAllText(ScriptPath("Repair-SecureFlagNulls.ps1"));

        Regex.IsMatch(script, @"^\s*,\s*\$rows\s*$", RegexOptions.Multiline).Should().BeTrue(
            "this guard's premise: Get-DvAll emits its List as one object (if that changes, revisit the guard)");
        ArrayWrapsGetDvAll(script).Should().BeFalse(
            "@(Get-DvAll …) is a one-element array whose .Count is always 1 — count (Get-DvAll …).Count instead");
    }

    [Theory]
    [InlineData("$after = if ($Apply) { @(Get-DvAll \"$set`?`$select=$idColumn\").Count } else { $ids.Count }", true)]
    [InlineData("$after = @( Get-DvAll \"x\" )", true)]
    [InlineData("$after = if ($Apply) { (Get-DvAll \"$set`?`$select=$idColumn\").Count } else { $ids.Count }", false)]
    [InlineData("$nulls = Get-DvAll \"x\"\n$ids = @($nulls | ForEach-Object { $_.$idColumn })", false)]
    public void TheArrayWrapDetector_SeesTheOneElementCountShape(string line, bool expected) =>
        ArrayWrapsGetDvAll(line).Should().Be(expected);

    private static bool ArrayWrapsGetDvAll(string script) => Regex.IsMatch(script, @"@\(\s*Get-DvAll\b");

    private const string Seed = """
        $Column = 'sprk_issecure'
        $Tables = @('sprk_project', 'sprk_matter', 'sprk_workassignment', 'sprk_invoice')
        $ReaderProfileName = 'Spaarke BFF-Managed Field Readers'
        $WriterProfileName = 'Spaarke BFF-Managed Field Writers'
        $Secured = $true
        """;

    [Theory]
    [InlineData("$Secured = $true", "$Secured = $false")]
    [InlineData("'sprk_matter', ", "")]
    [InlineData(", 'sprk_invoice'", "")]
    [InlineData("$Column = 'sprk_issecure'", "$Column = 'sprk_accesspermission'")]
    [InlineData("Field Readers'", "Field Readers 2'")]
    [InlineData("Field Writers'", "Flag Writers'")]
    public void TheParser_SeesEachDrift(string from, string to)
    {
        Agrees(Analyse(Seed)).Should().BeTrue("the faithful seed agrees");
        Agrees(Analyse(Seed.Replace(from, to))).Should().BeFalse("each seeded drift must fail the agreement test");
    }

    private sealed record ScriptShape(string? Column, IReadOnlyList<string> Tables, string? Reader, string? Writer, bool Secured);

    private static bool Agrees(ScriptShape s) =>
        s.Column == SecureFlagFieldSecurityAssertion.Column
        && s.Tables.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(SecureFlagFieldSecurityAssertion.Tables)
        && s.Reader == SecureFlagFieldSecurityAssertion.ReaderProfileName
        && s.Writer == SecureFlagFieldSecurityAssertion.WriterProfileName
        && s.Secured;

    private static ScriptShape Analyse(string script)
    {
        static string? Single(string text, string pattern) =>
            Regex.Match(text, pattern, RegexOptions.Multiline) is { Success: true } m ? m.Groups[1].Value : null;

        var tablesLine = Single(script, @"^\s*\$Tables\s*=\s*@\(([^)]*)\)");
        var tables = tablesLine is null
            ? Array.Empty<string>()
            : Regex.Matches(tablesLine, @"'([^']+)'").Select(m => m.Groups[1].Value).ToArray();

        return new ScriptShape(
            Single(script, @"^\s*\$Column\s*=\s*'([^']+)'"),
            tables,
            Single(script, @"^\s*\$ReaderProfileName\s*=\s*'([^']+)'"),
            Single(script, @"^\s*\$WriterProfileName\s*=\s*'([^']+)'"),
            string.Equals(Single(script, @"^\s*\$Secured\s*=\s*\$(\w+)"), "true", StringComparison.OrdinalIgnoreCase));
    }

    private static string ScriptPath(string name) => Path.Combine(RepoRoot(), "scripts", name);

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
