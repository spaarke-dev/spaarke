using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using UnifiedCreateRequest = Sprk.Bff.Api.Services.RecordCreation.RecordCreationRequest;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 015 (spec FR-09, owner decision Q9: record-first everywhere) — the SAME project
/// filed under the SAME matter gets the SAME owner from the unified create whatever surface's input it is given: the Office
/// quick-create's (its field mapping files the project under the matter it is created from), the browser route's Web API
/// body, the chat create's mapped item and a decision follow-on's core-derived pair. Then the owner order (secure ancestor →
/// the named Secure Record Owners team; else the filed-under record's business-unit team; else the acting user's), the
/// refusals (an unresolved owner is the typed refusal with NO create), and the registry exception (an unfiled
/// communication is never team-owned).
/// </summary>
/// <remarks>
/// <para>Task 018 routed the live Office quick-create through the unified create (task 018c deleted the Office creation
/// core), so its owner is record-first too; the live Office route's own owner tests are in
/// <c>SecureRootCreateRouteTests.OfficeSecureOwner</c> and the Office contract suites.</para>
/// The REAL <see cref="RecordCreationService"/>, the REAL
/// <see cref="RecordOwnershipResolver"/> over <see cref="Directory"/>, the REAL <see cref="SecureRootFilingGate"/> reading the
/// same directory, and the REAL caller checks over the caller's scripted Dataverse. Only the Dataverse module boundaries are
/// doubled (<see cref="CreateWorld"/>).
/// </remarks>
[Trait("status", "new")]
public sealed class CrossSurfaceOwnerTests
{
    private readonly CreateWorld _world = new();

    // =====================================================================================
    // One project, one matter, four surfaces, one owner
    // =====================================================================================

    [Fact]
    public async Task TheSameProjectUnderTheSameMatter_GetsTheMattersBusinessUnitTeam_ThroughEverySurfacesInput()
    {
        var owners = new Dictionary<string, Guid>
        {
            ["office"] = await _world.UnifiedOwnerAsync(CreateWorld.OfficeStyleProject(CreateWorld.OrdinaryMatter)),
            ["browser route"] = await _world.UnifiedOwnerAsync(_world.BrowserRouteProject(CreateWorld.OrdinaryMatter)),
            ["chat"] = await _world.UnifiedOwnerAsync(await _world.ChatProjectAsync(CreateWorld.OrdinaryMatter)),
            ["decision follow-on"] = await _world.UnifiedOwnerAsync(_world.DecisionFollowOnProject(CreateWorld.OrdinaryMatter)),
        };

        owners.Values.Should().AllBeEquivalentTo(Directory.ChildTeam,
            "every surface's project filed under a matter is owned by the matter's business-unit team — never by the " +
            "caller's (the caller sits in the general unit): {0}", string.Join(", ", owners.Select(o => $"{o.Key}={o.Value:D}")));
        _world.Creates.Should().HaveCount(4, "one create per surface's input");
    }

    // TheLiveOfficeQuickCreate_KeepsTheActingUsersTeam_ForAProjectUnderAMatter_UntilTask018 is gone (task 018c): task 018b
    // changed that owner on purpose (record-first through the unified create), pinned over the live route by
    // SecureRootCreateRouteTests.OfficeQuickCreate_UnderAnOrdinaryMatter_IsOwnedByTheMattersUnitTeam_NotSecure_AndIndexedByThatUnit.

    // =====================================================================================
    // The owner order
    // =====================================================================================

    [Fact]
    public async Task OwnerOrder_ASecureAncestor_GivesTheNamedSecureRecordOwnersTeam()
    {
        var outcome = await _world.CreateAsync(await _world.MappedAsync("sprk_event",
            new { sprk_eventname = "Hearing", sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.SecureMatter) }));

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.OwningTeamId.Should().Be(Directory.SecureNamedTeam, "secure-if-any: the named team, never the Secure BU's default");
        CreateWorld.Owner(_world.Creates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.SecureNamedTeam);
    }

    [Fact]
    public async Task OwnerOrder_AnOrdinaryFiledUnderRecord_GivesItsBusinessUnitTeam_NotTheCallers()
    {
        var outcome = await _world.CreateAsync(await _world.MappedAsync("sprk_event",
            new { sprk_eventname = "Hearing", sprk_regardingmatter = CreateWorld.Lookup("sprk_matter", CreateWorld.OrdinaryMatter) }));

        outcome.OwningTeamId.Should().Be(Directory.ChildTeam);
        CreateWorld.Owner(_world.Creates.Should().ContainSingle().Subject.Fields).Should().Be(Directory.ChildTeam);
    }

    [Fact]
    public async Task OwnerOrder_ARecordFiledUnderNothing_GivesTheActingUsersBusinessUnitTeam_OnEverySurface()
    {
        var unified = await _world.UnifiedOwnerAsync(await _world.MappedAsync("sprk_project", new { sprk_projectname = "Phase 2" }));

        unified.Should().Be(Directory.GeneralTeam, "the caller sits in the general business unit");
        // The Office half (task 018c): the Office quick-create IS this unified create now; its unfiled project's owner over
        // the live route is pinned by OfficeQuickCreateProjectContractTests.Post_Project_Returns201_OwnedByCallersTeam_….
    }

    // =====================================================================================
    // Unresolved → the typed refusal, nothing created
    // =====================================================================================

    [Fact]
    public async Task AFiledUnderRecordWhoseBusinessUnitHasNoDefaultTeam_IsRefused_AndNothingIsCreated()
    {
        var unified = await _world.CreateAsync(await _world.ChatProjectAsync(CreateWorld.TeamlessMatter));

        unified.Succeeded.Should().BeFalse();
        unified.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        unified.Refusal.Code.Should().Be(RecordOwnerRefusal.NoDefaultOwnerTeam);
        var problem = RecordCreationProblemMap.ToProblemDetails(unified.Refusal, "sprk_project", unified.CorrelationId);
        problem.Extensions["errorCode"].Should().Be(RecordOwnerRefusal.NoDefaultOwnerTeam, "ADR-019: a stable code");

        _world.Creates.Should().BeEmpty("an unresolved owner never falls back to a user- or app-owned row");
    }

    [Fact]
    public async Task AnActingUserWithNoBusinessUnit_AndAnUnfiledRecord_IsRefused_OnEverySurface_AndNothingIsCreated()
    {
        _world.CallerIsUnknownToTheDirectory();

        var unified = await _world.CreateAsync(await _world.MappedAsync("sprk_project", new { sprk_projectname = "Phase 2" }));

        unified.Succeeded.Should().BeFalse();
        unified.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.OwnerUnresolved);
        unified.Refusal.Code.Should().Be(RecordOwnerRefusal.ActingUserUnresolved);

        _world.Creates.Should().BeEmpty();
        // The Office half (task 018c): the same refusal reaches the Office route as OFFICE_022 with nothing written —
        // OfficeQuickCreateProjectContractTests.Post_Project_WhenTheCallersTeamCannotBeResolved_Returns403_AndCreatesNothingAppOwned
        // and OfficeQuickCreateContractTests.Post_Matter_WhenTheCallersTeamCannotBeResolved_Returns403_AndCreatesNothingAppOwned.
    }

    [Fact]
    public async Task AStep7CatalogReadFault_IsTheOwnerUnavailableRefusal_503_AndNothingIsWritten()
    {
        // UAC-r2's review of 015 (K1), task 018a: step 7 reads the pair's record type to file a project under the matter it
        // names (PairParentAsync). That read faulting is an owner that could not be decided — never "filed under nothing",
        // never the generic check fault.
        _world.ServiceCatalogReadFault = new InvalidOperationException("catalog read faulted");

        var outcome = await _world.CreateAsync(_world.BrowserRouteProject(CreateWorld.OrdinaryMatter));

        _world.ServiceCatalogReadsFaulted.Should().BeGreaterThan(0, "the fault was reached");
        outcome.Succeeded.Should().BeFalse();
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Unavailable);
        outcome.Refusal.Code.Should().Be(RecordCreationRefusal.OwnerUnavailableCode,
            "a step-7 fault is the owner's, refused by step 7 itself (NFR-01)");
        outcome.Steps.Should().Contain(s => s.Step == RecordCreationStep.SecureRootPlan && s.State == RecordCreationStepState.Ran,
            "the plan's own catalog read still answered: the fault is step 7's");
        outcome.Steps.Last().Step.Should().Be(RecordCreationStep.Owner, "refused at step 7");
        RecordCreationProblemMap.ToProblemDetails(outcome.Refusal, "sprk_project", outcome.CorrelationId).Status
            .Should().Be(StatusCodes.Status503ServiceUnavailable);
        _world.Creates.Should().BeEmpty("nothing is written when the owner could not be decided");
    }

    [Fact]
    public async Task APairWhoseRecordTypeNamesNoTable_IsUndetermined_NeverFiledUnderNothing()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        var (parent, undetermined) = await RecordCreationService.PairParentAsync(
            entities.Object, "sprk_project", CreateWorld.OrdinaryMatter.ToString("D"), Guid.NewGuid(), CancellationToken.None);

        parent.Should().BeNull();
        undetermined.Should().NotBeNull("an unreadable record type is a refusal, never \"filed under nothing\"");
    }

    [Theory]
    [InlineData(false, "an ordinary document in the child unit")]
    [InlineData(true, "a document owned in the Secure BU")]
    public async Task AProjectWhosePairNamesADocument_IsFiledUnderNothing_AsThePlanReadsIt(bool secureDocument, string because)
    {
        var document = secureDocument ? CreateWorld.SecureDocument : CreateWorld.OrdinaryDocument;

        var (parent, undetermined) = await RecordCreationService.PairParentAsync(
            _world.EntitiesForTest(), "sprk_project", document.ToString("D"), CreateWorld.DocumentType, CancellationToken.None);
        parent.Should().BeNull("only a matter or project is a parent the pair names (SecureRootInheritance.IsParent)");
        undetermined.Should().BeNull();

        var outcome = await _world.CreateAsync(_world.BrowserRouteProject(document, CreateWorld.DocumentType));

        outcome.Succeeded.Should().BeTrue($"{because}: the plan filed it under nothing, so the owner must agree, " +
            $"never \"Try again\" ({outcome.Refusal?.Code}: {outcome.Refusal?.Detail})");
        outcome.OwningTeamId.Should().Be(Directory.GeneralTeam, "the acting user's team, never the document's unit");
    }

    [Fact]
    public async Task AMalformedPair_OnATableWhosePairIsNotAParent_IsNotAnOwnerRefusal()
    {
        var unified = CreateWorld.Mapped("sprk_event", new Dictionary<string, object?>
        {
            ["sprk_eventname"] = "Hearing",
            ["sprk_regardingrecordid"] = 42, // not text: OwnedChildWrite.PairOf reads it as an invalid id
        });

        // As the system for nobody, so no caller check refuses the pair first and step 7 is reached.
        var outcome = await _world.CreateAsync(unified, system: true);

        outcome.Steps.Should().Contain(s => s.Step == RecordCreationStep.Owner, "step 7 is reached");
        outcome.Refusal!.Code.Should().NotBe(RecordOwnerRefusal.ParentUndetermined,
            "an event's pair files nothing on its own (PairNamesAParentOnItsOwn), so step 7 never asks about it");
        outcome.Refusal.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource,
            "the answer before task 015: an unfiled row made for nobody has no owner source");
    }

    // =====================================================================================
    // The registry's ownership exception
    // =====================================================================================

    [Fact]
    public async Task AnUnfiledCommunication_TheRegistrysOwnershipException_IsNeverTeamOwned_AndNothingIsCreated()
    {
        var outcome = await _world.CreateAsync(await _world.MappedAsync("sprk_communication", new { sprk_subject = "Hello" }));

        outcome.Succeeded.Should().BeFalse();
        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.OwnershipExceptionCode,
            "UnfiledCommunicationKeepsCreator: an application create cannot keep a creator, and never makes it a team's row");
        RecordCreationProblemMap.ToProblemDetails(outcome.Refusal, "sprk_communication", outcome.CorrelationId).Status
            .Should().Be(StatusCodes.Status409Conflict);
        _world.Creates.Should().BeEmpty();
        _world.OwnerQuestions.Should().Be(0, "the exception is decided before any owner is resolved");
    }
}

/// <summary>
/// The world task 015's create tests run in: one directory (business units, teams, the caller, a matter in each kind of
/// unit, the ADR-024 record-type catalog) read by the REAL resolver, the REAL secure-root gate and the unified create;
/// the caller's scripted Dataverse; and the app-only boundary — the unified create's PATCH — recorded, never applied.
/// </summary>
internal sealed class CreateWorld
{
    public static readonly Guid OrdinaryMatter = Guid.Parse("a0150000-0000-4000-8000-000000000001");
    public static readonly Guid SecureMatter = Guid.Parse("a0150000-0000-4000-8000-000000000002");
    public static readonly Guid TeamlessMatter = Guid.Parse("a0150000-0000-4000-8000-000000000003");
    public static readonly Guid TeamlessBu = Guid.Parse("a0150000-0000-4000-8000-000000000004");
    public static readonly Guid MatterType = Guid.Parse("a0150000-0000-4000-8000-000000000005");
    public static readonly Guid DocumentType = Guid.Parse("a0150000-0000-4000-8000-000000000006");
    public static readonly Guid OrdinaryDocument = Guid.Parse("a0150000-0000-4000-8000-000000000007");
    public static readonly Guid SecureDocument = Guid.Parse("a0150000-0000-4000-8000-000000000008");

    private readonly SecureChildOwnershipAiToolTests.ScriptedUserClient _caller = new(Directory.CallerUserId);
    private Directory _directory;
    private int _ownerQuestions;

    public CreateWorld()
    {
        _directory = NewDirectory(withCaller: true);
        _caller.RecordTypes[MatterType] = "sprk_matter";
        _caller.RecordTypes[DocumentType] = "sprk_document";
    }

    /// <summary>The caller's scripted Dataverse (its privileges, its AppendTo denials).</summary>
    public SecureChildOwnershipAiToolTests.ScriptedUserClient Caller => _caller;

    /// <summary>Every app-only create the unified service asked for.</summary>
    public List<(string Table, Guid Id, Dictionary<string, object?> Fields)> Creates { get; } = [];

    /// <summary>Owner decisions asked of the resolver.</summary>
    public int OwnerQuestions => _ownerQuestions;

    /// <summary>Per business unit: its search-index defaults (the name and the catalog row), as the unit row holds them.</summary>
    public Dictionary<Guid, (string? Name, Guid? Index)> SearchIndexDefaults { get; } = [];

    /// <summary>When set, every business-unit read faults with it.</summary>
    public Exception? BusinessUnitReadFault { get; set; }

    /// <summary>Every business-unit row read (step 8).</summary>
    public List<Guid> BusinessUnitReads { get; } = [];

    /// <summary>
    /// When set, every record-type catalog query the unified create's OWN reader sends (step 7's pair read,
    /// <see cref="RecordCreationService.PairParentAsync"/>) faults with it; the secure-root gate's reader (step 6's plan)
    /// still answers.
    /// </summary>
    public Exception? ServiceCatalogReadFault { get; set; }

    /// <summary>Catalog queries the unified create's own reader faulted on.</summary>
    public int ServiceCatalogReadsFaulted { get; private set; }

    private static Directory NewDirectory(bool withCaller)
    {
        var directory = withCaller ? Directory.Standard() : StandardWithoutTheCaller();
        return directory
            .WithBusinessUnit(TeamlessBu, "No Default Team")
            .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
            .WithSecureRoot("sprk_matter", SecureMatter)
            .WithRecord("sprk_matter", TeamlessMatter, TeamlessBu, isSecure: false)
            .WithRecord("sprk_recordtype_ref", MatterType, owningBusinessUnit: null,
                extra: new Dictionary<string, object> { ["sprk_recordlogicalname"] = "sprk_matter" })
            .WithRecord("sprk_recordtype_ref", DocumentType, owningBusinessUnit: null,
                extra: new Dictionary<string, object> { ["sprk_recordlogicalname"] = "sprk_document" })
            .WithRecord("sprk_document", OrdinaryDocument, Directory.ChildBu, owningTeam: Directory.ChildTeam)
            .WithRecord("sprk_document", SecureDocument, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam);
    }

    /// <summary>The standard units and teams, but no user: the acting user has no business unit to own anything by.</summary>
    private static Directory StandardWithoutTheCaller()
    {
        var directory = new Directory()
            .WithBusinessUnit(Directory.GeneralBu, "Spaarke")
            .WithBusinessUnit(Directory.ChildBu, "Spaarke Business Unit 1")
            .WithBusinessUnit(Directory.SecureBu, Directory.SecureBuName)
            .WithTeam(Directory.GeneralTeam, Directory.GeneralBu, isDefault: true, teamType: 0, "Spaarke")
            .WithTeam(Directory.ChildTeam, Directory.ChildBu, isDefault: true, teamType: 0, "Spaarke Business Unit 1")
            .WithTeam(Directory.SecureNamedTeam, Directory.SecureBu, isDefault: false, teamType: 0, Directory.SecureOwnerTeamName);
        return directory;
    }

    public void CallerIsUnknownToTheDirectory() => _directory = NewDirectory(withCaller: false);

    public static object Lookup(string table, Guid id) => new { relatedTable = table, recordId = id };

    public static Guid Owner(IReadOnlyDictionary<string, object?> fields) =>
        fields.TryGetValue("ownerid@odata.bind", out var bind) && bind is string path
        && Guid.TryParse(path["/teams(".Length..^1], out var team)
            ? team
            : Guid.Empty;

    // ── The four surfaces' inputs ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The browser route: the Create Project wizard's Web API body, the pair typed by its record-type bind.</summary>
    public UnifiedCreateRequest BrowserRouteProject(Guid matter, Guid? recordType = null) => WebApi("sprk_project", new Dictionary<string, object?>
    {
        ["sprk_projectname"] = "Phase 2",
        ["sprk_regardingrecordid"] = matter.ToString("D"),
        ["sprk_regardingrecordname"] = "Acme v. Widget",
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({recordType ?? MatterType:D})",
    });

    /// <summary>
    /// The Office quick-create's input as task 018 hands it to the unified create: the row its matter → project mapping
    /// profile builds (the pair from the matter's id and record type), as a server-built mapped item.
    /// </summary>
    public static UnifiedCreateRequest OfficeStyleProject(Guid matter) => Mapped("sprk_project",
        new Dictionary<string, object?>
        {
            ["sprk_projectname"] = "Phase 2",
            ["sprk_regardingrecordid"] = matter.ToString("D"),
            ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
        },
        new DataverseWriteItemMapper.MappedLookup("sprk_regardingrecordtype", "sprk_recordtype_ref", "sprk_recordtype_refs", MatterType));

    /// <summary>A decision follow-on: the pair the commit route derives from the Signal's core record (upper-case id text).</summary>
    public UnifiedCreateRequest DecisionFollowOnProject(Guid matter) => WebApi("sprk_project", new Dictionary<string, object?>
    {
        ["sprk_projectname"] = "Phase 2",
        ["sprk_regardingrecordid"] = matter.ToString("D").ToUpperInvariant(),
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
    });

    /// <summary>The chat create: the item the model sends, mapped as the caller (lookups in their object form).</summary>
    public Task<UnifiedCreateRequest> ChatProjectAsync(Guid matter) => MappedAsync("sprk_project", new
    {
        sprk_projectname = "Phase 2",
        sprk_regardingrecordid = matter.ToString("D"),
        sprk_regardingrecordtype = Lookup("sprk_recordtype_ref", MatterType),
    });

    public async Task<UnifiedCreateRequest> MappedAsync(string table, object item)
    {
        var mapped = await DataverseWriteItemMapper.MapAsync(_caller, table, JsonSerializer.SerializeToElement(item), CancellationToken.None);
        mapped.Item.Should().NotBeNull(mapped.ValidationError);
        return new UnifiedCreateRequest(table, new RecordCreationPayload.Mapped(mapped.Item!));
    }

    /// <summary>A hand-built mapped item (for columns the caller's scripted metadata does not describe).</summary>
    public static UnifiedCreateRequest Mapped(string table, object body, params DataverseWriteItemMapper.MappedLookup[] lookups)
    {
        var json = JsonSerializer.Serialize(body);
        var columns = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!.Keys
            .Select(k => k.Contains('@', StringComparison.Ordinal) ? k[..k.IndexOf('@')].ToLowerInvariant() : k)
            .ToList();
        return new UnifiedCreateRequest(table, new RecordCreationPayload.Mapped(
            new DataverseWriteItemMapper.MappedItem(json, columns) { Lookups = lookups }));
    }

    private static UnifiedCreateRequest WebApi(string table, Dictionary<string, object?> body) =>
        new(table, new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(body)));

    // ── The unified create ────────────────────────────────────────────────────────────────────────────────────────

    public Task<RecordCreationOutcome> CreateAsync(UnifiedCreateRequest request, bool system = false, RecordRequester? requestedBy = null) =>
        UnifiedService().CreateAsync(request,
            system ? RecordCreationPrincipal.ForSystem(requestedBy) : RecordCreationPrincipal.ForCaller(_caller, Directory.CallerOid),
            CancellationToken.None);

    /// <summary>The app-only reader the services here use, for a test that calls a static step directly.</summary>
    public IGenericEntityService EntitiesForTest() => Entities();

    public async Task<Guid> UnifiedOwnerAsync(UnifiedCreateRequest request)
    {
        var outcome = await CreateAsync(request);
        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var created = Creates.Single(c => c.Id == outcome.CreatedId);
        Owner(created.Fields).Should().Be(outcome.OwningTeamId!.Value, "the create binds the team it reports");
        return outcome.OwningTeamId!.Value;
    }

    private RecordCreationService UnifiedService()
    {
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
                Creates.Add((table, id, new Dictionary<string, object?>(fields))))
            .Returns(Task.CompletedTask);

        var stamps = new StampWorld();
        return new RecordCreationService(
            new CountingResolver(_directory.Resolver(), () => _ownerQuestions++), appOnly.Object, Entities(serviceReader: true),
            SecureRootFilingGateFixtures.Over(Entities()),
            new CoreAncestorRestamper(stamps.Service, stamps.Resolver, NullLogger<CoreAncestorRestamper>.Instance),
            SecureChildShareWorld.SynchronizerOver(SecureChildShareWorld.Standard, new FakeRecordShareTable()),
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new Mock<IMembershipEventPublisher>().Object, NullLogger<RecordCreationService>.Instance);
    }

    // ── The app-only Dataverse every service here reads ───────────────────────────────────────────────────────────

    /// <summary>
    /// The app-only reader: queries answered by the directory (the resolver's, the gate's and the pair's record-type
    /// reads); a team's business unit; a business unit's search-index defaults; a matter's row and the caller's business
    /// unit.
    /// </summary>
    private IGenericEntityService Entities(bool serviceReader = false)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(_directory.Answer(query)));
        // #2191 K2: the Office create derives a project's filed-under pair from the catalog row named by the source's table,
        // and the plan reads that row back by id; the directory's own catalog rows answer every other catalog query.
        entities
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => RecordTypeCatalogSetup.IsCatalogQuery(q)), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
            {
                if (serviceReader && ServiceCatalogReadFault is { } fault)
                {
                    ServiceCatalogReadsFaulted++;
                    return Task.FromException<EntityCollection>(fault);
                }

                return Task.FromResult(RecordTypeCatalogSetup.Answer(query) is { Entities.Count: > 0 } row ? row : _directory.Answer(query));
            });
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        entities
            .Setup(e => e.RetrieveAsync("team", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) =>
            {
                var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid") };
                query.Criteria.AddCondition("teamid", ConditionOperator.Equal, id);
                return Task.FromResult(_directory.Answer(query).Entities.Single());
            });
        entities
            .Setup(e => e.RetrieveAsync("businessunit", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) =>
            {
                BusinessUnitReads.Add(id);
                if (BusinessUnitReadFault is { } fault)
                    return Task.FromException<Entity>(fault);
                var row = new Entity("businessunit", id);
                if (SearchIndexDefaults.TryGetValue(id, out var defaults))
                {
                    if (defaults.Name is { } name)
                        row["sprk_searchindexname"] = name;
                    if (defaults.Index is { } index)
                        row["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", index);
                }

                return Task.FromResult(row);
            });
        entities
            .Setup(e => e.RetrieveAsync("systemuser", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] _, CancellationToken _) =>
            {
                var query = new QueryExpression("systemuser") { ColumnSet = new ColumnSet("businessunitid") };
                query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, id);
                return Task.FromResult(_directory.Answer(query).Entities.SingleOrDefault() ?? new Entity("systemuser", id));
            });
        entities
            .Setup(e => e.RetrieveAsync("sprk_matter", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Guid id, string[] columns, CancellationToken _) =>
            {
                var matter = new Entity("sprk_matter", id)
                {
                    ["sprk_matterid"] = id,
                    ["sprk_matternumber"] = "MAT-000015", // the number read-back of a created matter
                    ["sprk_recordtype"] = new EntityReference("sprk_recordtype_ref", MatterType),
                };

                // #2191: the source's own secure flag, explicit (an empty flag is never read as not secure).
                if (columns.Contains(SecurableEntityRegistry.SecureFlagAttribute))
                    matter[SecurableEntityRegistry.SecureFlagAttribute] = id == SecureMatter;
                return Task.FromResult(matter);
            });
        return entities.Object;
    }

    /// <summary>The REAL resolver, counting the owner decisions asked of it.</summary>
    private sealed class CountingResolver(IRecordOwnershipResolver inner, Action asked) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            asked();
            return inner.ResolveOwningTeamAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            asked();
            return inner.ResolveOwnerAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent reparent, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(reparent, applyChange, ct);
    }
}
