using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 020 (spec FR-15, FR-03, FR-02, FR-09) — <c>POST /api/v1/child-records/{table}</c>
/// creates a Matter and a Project, and takes the optional mapping source (query parameters <c>mappingSourceEntity</c> and
/// <c>mappingSourceId</c>). Driven through the REAL handler (<see cref="ChildRecordEndpoints.CreateAsync"/>) over the REAL
/// <see cref="RecordCreationService"/> of the task 014 harness (the real resolver, probe, engine, caller checks and owner
/// resolver; doubled only at the module boundaries).
/// </summary>
/// <remarks>
/// <para>What is pinned: the owner of a Matter and a Project created through the route (FR-09, the team
/// <c>ResolveOwnerAsync</c> names: record-first, else the caller's business-unit team); fill-only mapping (FR-02: the
/// caller's value wins, an untouched mapped column is filled); the source inferred for a Matter or Project and asked for no
/// other table unless named (an absent source is exactly the old route); a named source the caller cannot read, a missing
/// one, and a malformed one — no mapped value, one warning, never a refusal or an echo of what was named.</para>
/// <para>Not pinned here (task 041 changes it): any search-index column. The secure Project's isolation and the uniform
/// refusal on a secure parent are <c>SecureRootInheritanceWriterTests</c>' (ChildRecordSecureProjectCreateSeamTests.cs).</para>
/// <para>A partial of <see cref="RecordCreationServiceMappingSeamTests"/> (its harness), as the route suites' partials are.</para>
/// </remarks>
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid GeneralMatter = Guid.Parse("a0200000-0000-4000-8000-000000000001");
    private const string UsersOwnWords = "Typed by the user";

    // ── The route ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private Task<IResult> RouteCreate(
        string table, Dictionary<string, object?> body, string? sourceEntity = null, string? sourceId = null)
    {
        // Step 12's number read-back of an autonumber table reads the new row: answer it with a number (not a source read).
        _onCreate = (created, id, _) =>
        {
            if (RecordTableRules.Find(created)?.AutonumberColumn is { } number)
                _sources[(created, id)] = new Entity(created, id) { [number] = "N-000001" };
        };

        var http = new DefaultHttpContext { TraceIdentifier = FixedCorrelation };
        http.Request.Headers.Authorization = $"Bearer {CallerToken}";
        return ChildRecordEndpoints.CreateAsync(
            table, JsonSerializer.SerializeToElement(body), _user, Service(), http, NullLogger<Program>.Instance,
            CancellationToken.None, sourceEntity, sourceId, _probe);
    }

    private static int? StatusOf(IResult result) => ((IStatusCodeHttpResult)result).StatusCode;

    private static JsonElement BodyOf(IResult result) => JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value);

    private static string? DetailOf(IResult result) => (result as ProblemHttpResult)?.ProblemDetails.Detail;

    private static string? ReasonOf(IResult result) =>
        result is ProblemHttpResult problem && problem.ProblemDetails.Extensions.TryGetValue("reasonCode", out var code)
            ? code?.ToString()
            : null;

    private static IReadOnlyList<string> WarningsOf(IResult result) =>
        BodyOf(result).TryGetProperty("warnings", out var warnings)
            ? warnings.EnumerateArray().Select(w => w.GetString()!).ToList()
            : [];

    /// <summary>The source project's columns the project → matter profile copies, and the matter's columns that take them.</summary>
    private void ProjectToMatterProfile()
    {
        _sources[("sprk_project", SourceProject)]["sprk_projectcode"] = "PRJ-7";
        _user.Columns["sprk_matter"] = new()
        {
            ["sprk_description"] = ("Memo", true),
            ["sprk_mattercode"] = ("String", true),
        };
        Profile("sprk_project", "sprk_matter",
            Rule("sprk_projectdescription", "sprk_description", Memo),
            Rule("sprk_projectcode", "sprk_mattercode", Text));
    }

    private void MatterToProjectProfile() => Profile("sprk_matter", "sprk_project",
        Rule("sprk_matterdescription", "sprk_description", Memo));

    /// <summary>A matter in ANOTHER business unit than the caller's (General), so record-first and the caller's unit differ.</summary>
    private void TheGeneralMatter()
    {
        _world.WithRecord("sprk_matter", GeneralMatter, Directory.GeneralBu, isSecure: false, owningTeam: Directory.GeneralTeam);
        _stamps.Row("sprk_matter", GeneralMatter);
        _shareWorld.OrdinaryRoot("sprk_matter", GeneralMatter);
        _readable.Add(GeneralMatter);
        _sources[("sprk_matter", GeneralMatter)] = _sources[("sprk_matter", OrdinaryMatter)];
        // The ADR-024 catalog's matter row: the caller's read (mapping source), the plan's and the stamp's app-only reads.
        _scripted.RecordTypes[RecordTypeCatalogSetup.MatterTypeId] = "sprk_matter";
        _stamps.RecordType(RecordTypeCatalogSetup.MatterTypeId, "sprk_matter");
        _sourceReader.AnswersRecordTypeCatalog(); // the service's own app-only reader (step 7 reads the pair's type there)
    }

    private static Dictionary<string, object?> AProjectFiledUnder(Guid matter) => new()
    {
        ["sprk_projectname"] = "Phase 1",
        [SecureRootInheritance.PairIdColumn] = matter.ToString("D"),
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({RecordTypeCatalogSetup.MatterTypeId:D})",
    };

    // ── FR-09: the owner of a Matter and a Project created through the route ─────────────────────────────────────────

    [Fact]
    public async Task RouteCreate_AnUnfiledMatter_IsOwnedByTheCallersBusinessUnitTeam_AndAsksForNoMappingItCannotInfer()
    {
        var result = await RouteCreate("sprk_matter", new() { ["sprk_mattername"] = "Acme v. Widget" });

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var (table, id, fields) = _creates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_matter");
        Owner(fields).Should().Be(Directory.ChildTeam, "FR-09: an unfiled root is owned by the caller's business-unit team");
        fields.Keys.Should().NotContain(k => k.StartsWith("ownerid", StringComparison.Ordinal) && k != "ownerid@odata.bind");
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind", "createdby is the application; the caller is recorded");
        BodyOf(result).GetProperty("id").GetGuid().Should().Be(id);
        WarningsOf(result).Should().BeEmpty();
        _journal.Should().NotContain("profile", "an unfiled matter names no source: inference finds none, a silent no-op");
    }

    [Fact]
    public async Task RouteCreate_AProjectFiledUnderAMatterOfAnotherUnit_IsOwnedRecordFirst_ByTheMattersTeam()
    {
        TheGeneralMatter();

        var result = await RouteCreate("sprk_project", AProjectFiledUnder(GeneralMatter));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        Owner(fields).Should().Be(Directory.GeneralTeam,
            "FR-09 record-first: the filed-under matter's business-unit team, not the caller's (Child) unit");
        Owner(fields).Should().NotBe(Directory.ChildTeam);
    }

    // ── FR-02 fill-only / FR-03 source, through the route ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteCreate_AMatterWithANamedSource_KeepsTheUsersValue_AndFillsTheColumnTheUserNeverTouched()
    {
        ProjectToMatterProfile();

        var result = await RouteCreate("sprk_matter", new()
        {
            ["sprk_mattername"] = "Acme v. Widget",
            ["sprk_description"] = UsersOwnWords,
        }, "sprk_project", SourceProject.ToString("D"));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be(UsersOwnWords, "FR-02: a caller-supplied value survives create");
        StringOf(fields, "sprk_mattercode").Should().Be("PRJ-7", "FR-02: an unsupplied mapped column is filled at create");
        _probe.Asked.Should().ContainSingle().Which.Should().Be((CallerToken, "sprk_projects", SourceProject),
            "FR-04: the named source is probed for Read AS THE CALLER, with their own token, before it is read");
        Owner(fields).Should().Be(Directory.ChildTeam, "a mapping source is not a filing parent: the matter is still unfiled");
        WarningsOf(result).Should().BeEmpty();
    }

    [Fact]
    public async Task RouteCreate_AProjectWithNoSource_InfersItFromTheMatterItIsFiledUnder_FillOnly()
    {
        TheGeneralMatter();
        MatterToProjectProfile();

        var result = await RouteCreate("sprk_project", AProjectFiledUnder(GeneralMatter));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "FR-03: inferred from the regarding pair");
        _probe.Asked.Should().ContainSingle().Which.Id.Should().Be(GeneralMatter);
        WarningsOf(result).Should().BeEmpty();
    }

    [Fact]
    public async Task RouteCreate_AProjectWithNoSource_KeepsTheUsersValueOverTheInferredOne()
    {
        TheGeneralMatter();
        MatterToProjectProfile();
        var body = AProjectFiledUnder(GeneralMatter);
        body["sprk_description"] = UsersOwnWords;

        var result = await RouteCreate("sprk_project", body);

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be(UsersOwnWords);
    }

    // ── FR-04: a named source the caller cannot read — no mapping, one warning, never a refusal or an oracle ─────────

    [Fact]
    public async Task RouteCreate_ANamedSourceTheCallerCannotRead_MapsNothing_AndAnswersExactlyAsAMissingSource()
    {
        MatterToProjectProfile();
        _sources[("sprk_matter", DeniedMatter)] = _sources[("sprk_matter", OrdinaryMatter)]; // it exists; the caller may not read it

        var denied = await RouteCreate("sprk_project", new() { ["sprk_projectname"] = "Phase 1" },
            "sprk_matter", DeniedMatter.ToString("D"));
        var deniedCreate = _creates.Should().ContainSingle().Subject.Fields;
        _creates.Clear();
        var missing = await RouteCreate("sprk_project", new() { ["sprk_projectname"] = "Phase 1" },
            "sprk_matter", MissingMatter.ToString("D"));

        StatusOf(denied).Should().Be(StatusCodes.Status201Created, "a source the caller cannot read never refuses the create");
        StatusOf(missing).Should().Be(StatusCodes.Status201Created);
        deniedCreate.Should().NotContainKey("sprk_description", "no mapped value from a source the caller cannot read");
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
        WarningsOf(denied).Should().ContainSingle().And.Equal(WarningsOf(missing),
            "denied and missing are ONE outcome: the answer says nothing about whether the record exists");
        WarningsOf(denied).Single().Should().NotContain(DeniedMatter.ToString("D")).And.NotContain("sprk_matter");
        _sourceReads.Should().NotContain(r => r.Id == DeniedMatter, "Read is probed as the caller BEFORE any read");
    }

    // ── A malformed or half-named source — undetermined: no mapping, one constant warning, never a 400 ───────────────

    public static TheoryData<string?, string?> MalformedSources() => new()
    {
        { "sprk_matter", null },
        { null, "a0140000-0000-4000-8000-000000000001" },
        { "sprk_matter", "not-a-guid" },
        { null, "not-a-guid" },
        { "Bad Name!", "a0140000-0000-4000-8000-000000000001" },
        { "sprk_matter", "00000000-0000-0000-0000-000000000000" },
    };

    [Theory]
    [MemberData(nameof(MalformedSources))]
    public async Task RouteCreate_AHalfNamedOrMalformedSource_IsUndetermined_MapsNothing_NeverInfers_AndEchoesNothing(
        string? sourceEntity, string? sourceId)
    {
        MatterToEventProfile(); // the event is filed under the matter: inferring would map — it must not
        var body = new Dictionary<string, object?>
        {
            ["sprk_eventname"] = "Hearing",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        };

        var result = await RouteCreate("sprk_event", body, sourceEntity, sourceId);

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description", "a named source that cannot be told is never replaced by an inferred one");
        var warning = WarningsOf(result).Should().ContainSingle().Subject;
        warning.Should().Be(MappingSourceResolver.SourceUndeterminedWarning);
        foreach (var named in new[] { sourceEntity, sourceId }.Where(v => !string.IsNullOrWhiteSpace(v)))
            warning.Should().NotContain(named!, "the warning never echoes what the caller named");
        _journal.Should().NotContain("profile").And.NotContain("probe");
    }

    // ── An absent source: every pre-existing table's create is exactly the old route (Mapping null) ──────────────────

    [Fact]
    public async Task RouteCreate_AnExistingTableWithNoSource_AsksForNoMapping_EvenWithAProfileForItsParent()
    {
        MatterToEventProfile();

        var result = await RouteCreate("sprk_event", new()
        {
            ["sprk_eventname"] = "Hearing",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        });

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description",
            "no source named on a table that does not infer one: nothing is mapped, as before task 020");
        _journal.Should().NotContain("profile").And.NotContain("probe");
        WarningsOf(result).Should().BeEmpty();
    }

    /// <summary>Must-fire control of the test above: the same create, naming the matter, is mapped.</summary>
    [Fact]
    public async Task RouteCreate_AnExistingTableWithANamedSource_IsMapped()
    {
        MatterToEventProfile();

        var result = await RouteCreate("sprk_event", new()
        {
            ["sprk_eventname"] = "Hearing",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
        }, "sprk_matter", OrdinaryMatter.ToString("D"));

        StatusOf(result).Should().Be(StatusCodes.Status201Created, DetailOf(result));
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_description").Should().Be("From the matter");
    }

    [Fact]
    public void MappingSourceOf_EveryCreateTableThatDoesNotInfer_AsksForNoMappingWhenNoSourceIsNamed()
    {
        var unchanged = ChildRecordEndpoints.CreateTables.Except(ChildRecordEndpoints.InferMappingSourceTables).ToList();

        unchanged.Should().Contain(["sprk_todo", "sprk_event", "sprk_workassignment"]);
        unchanged.Should().OnlyContain(t => ChildRecordEndpoints.MappingSourceOf(t, null, null) == null,
            "Mapping stays null for every pre-existing table when no source is named");
        unchanged.Should().OnlyContain(t => ChildRecordEndpoints.MappingSourceOf(t, "  ", "") == null);
        ChildRecordEndpoints.InferMappingSourceTables.Should().BeEquivalentTo(["sprk_matter", "sprk_project"]);
        ChildRecordEndpoints.InferMappingSourceTables.Should().OnlyContain(t => ChildRecordEndpoints.MappingSourceOf(t, null, null) != null);
    }

    // ── A caller-supplied secure flag on a root: the server-only column, unchanged ───────────────────────────────────

    /// <summary>
    /// A caller-supplied <c>sprk_issecure</c> on a root, as the route answers it today (not changed by task 020). The flag is
    /// field-secured live (only the BFF writes it: Make Secure, provisioning), which the scripted caller models: the caller's
    /// own column check refuses it, 403 <see cref="ChildRecordEndpoints.DeniedCode"/>, and nothing is created. (Without field
    /// security a project's flag is still refused by the secure-root gate; a matter's is not — task 020 notes, finding F-1.)
    /// </summary>
    [Theory]
    [InlineData("sprk_matter", "sprk_mattername")]
    [InlineData("sprk_project", "sprk_projectname")]
    public async Task RouteCreate_ACallerSuppliedSecureFlagOnARoot_IsRefused403_AndNothingIsCreated(string table, string nameColumn)
    {
        _scripted.SecuredColumns.Add(SecurableEntityRegistry.SecureFlagAttribute);

        var result = await RouteCreate(table, new()
        {
            [nameColumn] = "Secret",
            [SecurableEntityRegistry.SecureFlagAttribute] = true,
        });

        StatusOf(result).Should().Be(StatusCodes.Status403Forbidden, DetailOf(result));
        ReasonOf(result).Should().Be(ChildRecordEndpoints.DeniedCode);
        _creates.Should().BeEmpty("the secure flag is the server's: Make Secure / provisioning, never a create payload");
    }
}
