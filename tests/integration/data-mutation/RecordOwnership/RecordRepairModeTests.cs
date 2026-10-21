using System.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 016 (spec FR-14, FR-25, NFR-03) — Repair mode: steps 3 (mapping), 7 (owner), 8
/// (search-index default) and 10 (I-1 stamp) of the unified create, applied to an EXISTING row, fill-only, against its
/// current values, in at most ONE conditional update and as the system principal.
/// </summary>
/// <remarks>
/// Only the Dataverse module boundary is doubled: the row store is <see cref="Directory"/> (whole-row reads, the conditional
/// update applied with its version check), the stamp sources are a <see cref="StampWorld"/>, and the owner resolver, the
/// restamper and the service are the real code. Every update the service sends is recorded in <see cref="RepairWorld.Writes"/>.
/// </remarks>
[Trait("status", "new")]
public sealed class RecordRepairModeTests
{
    private readonly RepairWorld _world = new();

    // =====================================================================================
    // Mapping (FR-02 against the row's current values)
    // =====================================================================================

    [Fact]
    public async Task AnUnmappedColumn_IsFilledFromTheMapping_AndABlankStringCountsAsNotSupplied()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "   ", priority: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields["sprk_clientname"].Should().Be(RepairWorld.MatterClient);
        write.Fields["sprk_practicearea"].Should().Be(RepairWorld.MatterPracticeArea, "a blank string is not supplied (FR-02)");
        write.Fields["sprk_priorityname"].Should().Be(RepairWorld.DefaultPriority);
        Field(outcome, RecordRepairPart.Mapping, "sprk_practicearea").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task APresentValue_IsNeverOverwritten_EvenWhenItDiffersFromTheMapping()
    {
        var id = _world.EventUnderTheMatter(clientName: "Someone Else Ltd", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys)
            .Should().NotContain(["sprk_clientname", "sprk_practicearea", "sprk_priorityname"]);
        var row = _world.Directory.Row("sprk_event", id);
        row.GetAttributeValue<string>("sprk_clientname").Should().Be("Someone Else Ltd");
        row.GetAttributeValue<string>("sprk_priorityname").Should().Be("Urgent");
        Field(outcome, RecordRepairPart.Mapping, "sprk_clientname").State.Should().Be(RecordRepairFieldState.Present);
    }

    [Fact]
    public async Task AMappedLookupThatWouldFileTheRowUnderARecord_IsNotWritten()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");
        _world.MapMattersProjectOntoTheEvent();

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_regardingproject");
        Field(outcome, RecordRepairPart.Mapping, "sprk_regardingproject").ReasonCode
            .Should().Be(RecordRepairReason.MappedValueWouldRefile);
    }

    // =====================================================================================
    // Idempotency (NFR-03)
    // =====================================================================================

    [Fact]
    public async Task TwoConsecutiveRepairs_WriteNothingTheSecondTime()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);

        var first = await _world.RepairAsync("sprk_event", id);
        var second = await _world.RepairAsync("sprk_event", id);

        first.Status.Should().Be(RecordRepairStatus.Repaired);
        second.Status.Should().Be(RecordRepairStatus.NothingToRepair);
        second.Wrote.Should().BeFalse();
        _world.Writes.Should().HaveCount(1, "the second repair finds nothing missing and writes nothing");
    }

    [Fact]
    public async Task ARowChangedAfterItWasRead_IsNotWritten_AndNotRetried()
    {
        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: "Tax", priority: "Urgent");
        _world.BeforeWrite = () => _world.Directory.Write("sprk_event", id,
            new Dictionary<string, object?> { ["sprk_clientname"] = "Set By Another Writer" });

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.RowChanged);
        outcome.Wrote.Should().BeFalse();
        _world.Writes.Should().ContainSingle().Which.Applied.Should().BeFalse();
        _world.Directory.Row("sprk_event", id).GetAttributeValue<string>("sprk_clientname").Should().Be("Set By Another Writer");
        outcome.Fields.Should().NotContain(f => f.State == RecordRepairFieldState.Repaired);
    }

    // =====================================================================================
    // Owner (FR-09; only a systemuser-owned row, never an exception)
    // =====================================================================================

    [Fact]
    public async Task AUserOwnedRow_TakesTheOwnerItsParentGives()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["ownerid"]
            .Should().Be(new EntityReference("team", Directory.ChildTeam));
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(Directory.ChildTeam);
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Repaired);
        _world.Evictions.Should().ContainSingle().Which.Should().Be(("sprk_event", "sprk_events", id));
    }

    [Fact]
    public async Task ATeamOwnedRow_KeepsItsOwner_EvenOneTheRuleWouldNotGive()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("team", Directory.GeneralTeam), owningUnit: Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.NothingToRepair);
        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerTeamOwned);
        _world.Evictions.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnfiledCommunication_KeepsItsCreatorOwner_TheRegistryException()
    {
        var id = _world.UnfiledCommunicationOwnedBy(RepairWorld.PersonInChild);

        var outcome = await _world.RepairAsync("sprk_communication", id);

        _world.Writes.Should().BeEmpty();
        _world.Directory.Row("sprk_communication", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.PersonInChild));
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerKeptByException);
    }

    [Fact]
    public async Task AnUnresolvableOwner_IsLeftUnchanged_WithATypedRefusal_AndTheOtherFieldsAreStillRepaired()
    {
        // An APPLICATION-owned document filed under nothing: there is no person to act (I-6), so no owner can be decided —
        // and it is never given the application user's own team.
        var id = _world.UnfiledDocumentOwnedBy(RepairWorld.AppUser, Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_document", id);

        outcome.OwnerRefusal.Should().NotBeNull();
        outcome.OwnerRefusal!.Code.Should().Be(RecordOwnerRefusal.NoOwnerSource);
        Field(outcome, RecordRepairPart.Owner, "ownerid").State.Should().Be(RecordRepairFieldState.Refused);
        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Should().NotContainKey("ownerid");
        write.Fields["sprk_searchindexname"].Should().Be(RepairWorld.GeneralIndexName, "the row's own unit, owner unchanged");
        _world.Directory.Row("sprk_document", id).GetAttributeValue<EntityReference>("ownerid")
            .Should().Be(new EntityReference("systemuser", RepairWorld.AppUser));
    }

    [Fact]
    public async Task AnOwnerInAnotherBusinessUnit_IsNotWritten_EscalationTrigger2()
    {
        // The person who owns the event sits in the GENERAL unit; the matter it is filed under is in the CHILD unit.
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent",
            owner: new EntityReference("systemuser", Directory.CallerUserId), owningUnit: Directory.GeneralBu);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().BeEmpty();
        Field(outcome, RecordRepairPart.Owner, "ownerid").ReasonCode.Should().Be(RecordRepairReason.OwnerMovesBusinessUnit);
    }

    // =====================================================================================
    // Search-index default (FR-10) and the I-1 stamp, fill-only
    // =====================================================================================

    [Fact]
    public async Task TheSearchIndexDefault_IsFilledOnlyWhereUnset()
    {
        var id = _world.DocumentUnderTheMatter(indexName: "kept-index", index: null);

        var outcome = await _world.RepairAsync("sprk_document", id);

        var write = _world.Writes.Should().ContainSingle().Subject;
        write.Fields.Should().NotContainKey("sprk_searchindexname");
        write.Fields["sprk_ai_search_index"].Should().Be(new EntityReference("sprk_aisearchindex", RepairWorld.ChildIndex));
        Field(outcome, RecordRepairPart.SearchIndex, "sprk_searchindexname").State.Should().Be(RecordRepairFieldState.Present);
        _world.Directory.Row("sprk_document", id).GetAttributeValue<string>("sprk_searchindexname").Should().Be("kept-index");
    }

    [Fact]
    public async Task TheI1Stamp_IsAddedWhenMissing()
    {
        var id = _world.EventUnderTheCommunication(heldMatterStamp: null);

        var outcome = await _world.RepairAsync("sprk_event", id);

        _world.Writes.Should().ContainSingle().Which.Fields["sprk_regardingmatter"]
            .Should().Be(new EntityReference("sprk_matter", RepairWorld.Matter));
        Field(outcome, RecordRepairPart.CoreAncestorStamp, "sprk_regardingmatter").State.Should().Be(RecordRepairFieldState.Repaired);
    }

    [Fact]
    public async Task AHeldI1Stamp_IsNeverOverwritten_EvenAStaleOne()
    {
        var id = _world.EventUnderTheCommunication(heldMatterStamp: RepairWorld.OtherMatter);

        await _world.RepairAsync("sprk_event", id);

        _world.Writes.SelectMany(w => w.Fields.Keys).Should().NotContain("sprk_regardingmatter");
        _world.Directory.Row("sprk_event", id).GetAttributeValue<EntityReference>("sprk_regardingmatter").Id
            .Should().Be(RepairWorld.OtherMatter, "a stale copy is the reconciliation job's, never Repair's");
    }

    // =====================================================================================
    // System principal; the invoice-family seam (ebilling-r1 #1911)
    // =====================================================================================

    [Fact]
    public async Task Repair_RunsAsTheSystemPrincipal_NoCallerIdentity_NoWhoAmI()
    {
        typeof(RecordCreationService).GetMethod(nameof(RecordCreationService.RepairAsync),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetParameters().Select(p => p.ParameterType)
            .Should().Equal([typeof(string), typeof(Guid), typeof(CancellationToken)], "no caller client, token or principal");

        var id = _world.EventUnderTheMatter(clientName: null, practiceArea: null, priority: null);
        await _world.RepairAsync("sprk_event", id);

        _world.OwnerContexts.Should().NotBeEmpty();
        _world.OwnerContexts.Should().OnlyContain(c => c.CallerObjectId == null, "the owner is never resolved as a caller");
        _world.Directory.UserKeyColumns.Should().NotContain("azureactivedirectoryobjectid", "no caller is looked up by token");
    }

    [Fact]
    public async Task AnInvoiceFamilyRow_IsNotWritten_UntilTheInvoiceGuardIsOnThisBuild()
    {
        var id = _world.InvoiceUnderTheMatter();

        var outcome = await _world.RepairAsync("sprk_invoice", id);

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.InvoiceFamilyGuardUnavailable);
        _world.Writes.Should().BeEmpty();
        outcome.Fields.Should().Contain(f => f.Part == RecordRepairPart.Owner
            && f.State == RecordRepairFieldState.Skipped && f.ReasonCode == RecordRepairReason.InvoiceFamilyGuardUnavailable);
    }

    [Fact]
    public async Task ARowOutsideTheInvoiceFamily_IsWritten_TheGuardDoesNotFire()
    {
        var id = _world.EventUnderTheMatter(clientName: "Acme", practiceArea: "Tax", priority: "Urgent");

        var outcome = await _world.RepairAsync("sprk_event", id);

        outcome.Status.Should().Be(RecordRepairStatus.Repaired);
        _world.Writes.Should().ContainSingle();
    }

    [Fact]
    public async Task ATableThisServiceDoesNotCreate_IsSkipped_WithNothingRead()
    {
        var outcome = await _world.RepairAsync("account", Guid.NewGuid());

        outcome.Status.Should().Be(RecordRepairStatus.Skipped);
        outcome.ReasonCode.Should().Be(RecordRepairReason.TableUnsupported);
        _world.Directory.RecordQueries.Should().BeEmpty();
    }

    private static RecordRepairField Field(RecordRepairOutcome outcome, RecordRepairPart part, string column) =>
        outcome.Fields.Should().ContainSingle(f => f.Part == part && f.Column == column).Subject;
}

/// <summary>
/// The world of <see cref="RecordRepairModeTests"/>: an ordinary matter in the CHILD unit (owned by its default team) with a
/// matter → event mapping profile; a person in the child unit who owns rows natively; an application user; a communication
/// filed under the matter (its stamp source); each unit's search-index defaults.
/// </summary>
internal sealed class RepairWorld
{
    public static readonly Guid Matter = Guid.Parse("a0160000-0000-4000-8000-000000000001");
    public static readonly Guid OtherMatter = Guid.Parse("a0160000-0000-4000-8000-000000000002");
    public static readonly Guid Project = Guid.Parse("a0160000-0000-4000-8000-000000000003");
    public static readonly Guid Communication = Guid.Parse("a0160000-0000-4000-8000-000000000004");
    public static readonly Guid PersonInChild = Guid.Parse("a0160000-0000-4000-8000-000000000005");
    public static readonly Guid AppUser = Guid.Parse("a0160000-0000-4000-8000-000000000006");
    public static readonly Guid GeneralIndex = Guid.Parse("a0160000-0000-4000-8000-0000000000a1");
    public static readonly Guid ChildIndex = Guid.Parse("a0160000-0000-4000-8000-0000000000a2");
    public const string GeneralIndexName = "spaarke-general-index";
    public const string ChildIndexName = "spaarke-child-index";
    public const string MatterClient = "Acme Corp";
    public const string MatterPracticeArea = "Litigation";
    public const string DefaultPriority = "Normal";

    private readonly Dictionary<(string Source, string Target), FieldMappingProfileEntity> _profiles = new();
    private readonly StampWorld _stamps = new();

    public RepairWorld()
    {
        Directory = Directory.Standard()
            .WithUser(PersonInChild, Guid.NewGuid(), Directory.ChildBu)
            .WithUser(AppUser, Guid.NewGuid(), Directory.GeneralBu)
            .WithRecord("sprk_matter", Matter, Directory.ChildBu, isSecure: false, owningTeam: Directory.ChildTeam,
                extra: new Dictionary<string, object>
                {
                    ["sprk_clientname"] = MatterClient,
                    ["sprk_practicearea"] = MatterPracticeArea,
                    ["sprk_project"] = new EntityReference("sprk_project", Project),
                })
            .WithOrdinaryRoot("sprk_matter", OtherMatter)
            .WithRecord("sprk_communication", Communication, Directory.ChildBu, owningTeam: Directory.ChildTeam,
                extra: new Dictionary<string, object> { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter) });
        _stamps.Row("sprk_communication", Communication, [("sprk_regardingmatter", "sprk_matter", Matter)]);

        SearchIndexDefaults[Directory.GeneralBu] = (GeneralIndexName, GeneralIndex);
        SearchIndexDefaults[Directory.ChildBu] = (ChildIndexName, ChildIndex);

        _profiles[("sprk_matter", "sprk_event")] = Profile("sprk_matter", "sprk_event",
            Copy("sprk_clientname", "sprk_clientname", 1),
            Copy("sprk_practicearea", "sprk_practicearea", 2),
            new FieldMappingRuleEntity
            {
                Id = Guid.NewGuid(), Name = "priority", SourceField = string.Empty, TargetField = "sprk_priorityname", TargetFieldType = 0,
                MappingType = CreateTimeFieldMapping.MappingTypeDefault, DefaultValue = DefaultPriority, ExecutionOrder = 3,
                IsActive = true,
            });
    }

    public OwnershipDirectory Directory { get; }

    /// <summary>Every conditional update the service sent: the version it was conditioned on, and whether it applied.</summary>
    public List<(string Table, Guid Id, string Version, Dictionary<string, object> Fields, bool Applied)> Writes { get; } = [];

    /// <summary>Every owner decision asked of the resolver.</summary>
    public List<RecordOwnershipContext> OwnerContexts { get; } = [];

    /// <summary>Every owner-change eviction: (table, entity set, id).</summary>
    public List<(string Table, string Set, Guid Id)> Evictions { get; } = [];

    /// <summary>Per business unit: its search-index defaults.</summary>
    public Dictionary<Guid, (string? Name, Guid? Index)> SearchIndexDefaults { get; } = [];

    /// <summary>Runs once, just before the update reaches the row (another writer's change lands in between).</summary>
    public Action? BeforeWrite { get; set; }

    public Task<RecordRepairOutcome> RepairAsync(string table, Guid id) => Service().RepairAsync(table, id, CancellationToken.None);

    // ── Rows ────────────────────────────────────────────────────────────────────────────────────────────────────

    public Guid EventUnderTheMatter(
        string? clientName, string? practiceArea, string? priority, EntityReference? owner = null, Guid? owningUnit = null)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object>
        {
            ["ownerid"] = owner ?? new EntityReference("systemuser", PersonInChild),
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Matter),
        };
        if (owner is { LogicalName: "team" })
            extra["owningteam"] = owner;
        if (clientName is not null)
            extra["sprk_clientname"] = clientName;
        if (practiceArea is not null)
            extra["sprk_practicearea"] = practiceArea;
        if (priority is not null)
            extra["sprk_priorityname"] = priority;
        Directory.WithRecord("sprk_event", id, owningUnit ?? Directory.ChildBu, extra: extra);
        return id;
    }

    /// <summary>An event filed under the communication (a stamp source), owned by the child unit's team already.</summary>
    public Guid EventUnderTheCommunication(Guid? heldMatterStamp)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object>
        {
            ["sprk_regardingcommunication"] = new EntityReference("sprk_communication", Communication),
        };
        if (heldMatterStamp is { } held)
            extra["sprk_regardingmatter"] = new EntityReference("sprk_matter", held);
        Directory.WithRecord("sprk_event", id, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: extra);
        return id;
    }

    public Guid UnfiledCommunicationOwnedBy(Guid person)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_communication", id, Directory.ChildBu,
            extra: new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", person) });
        return id;
    }

    public Guid UnfiledDocumentOwnedBy(Guid user, Guid owningUnit)
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_document", id, owningUnit,
            extra: new Dictionary<string, object> { ["ownerid"] = new EntityReference("systemuser", user) });
        return id;
    }

    /// <summary>A document filed under the matter, owned by the child unit's team, with the search-index values given.</summary>
    public Guid DocumentUnderTheMatter(string? indexName, Guid? index)
    {
        var id = Guid.NewGuid();
        var extra = new Dictionary<string, object> { ["sprk_matter"] = new EntityReference("sprk_matter", Matter) };
        if (indexName is not null)
            extra["sprk_searchindexname"] = indexName;
        if (index is { } catalog)
            extra["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", catalog);
        Directory.WithRecord("sprk_document", id, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: extra);
        return id;
    }

    /// <summary>A natively created invoice filed under the matter, owned by the person in the child unit.</summary>
    public Guid InvoiceUnderTheMatter()
    {
        var id = Guid.NewGuid();
        Directory.WithRecord("sprk_invoice", id, Directory.ChildBu, extra: new Dictionary<string, object>
        {
            ["ownerid"] = new EntityReference("systemuser", PersonInChild),
            ["sprk_matter"] = new EntityReference("sprk_matter", Matter),
        });
        return id;
    }

    /// <summary>Adds a rule copying the matter's project lookup into the event's project filing column.</summary>
    public void MapMattersProjectOntoTheEvent()
    {
        var profile = _profiles[("sprk_matter", "sprk_event")];
        profile.Rules = [.. profile.Rules!, new FieldMappingRuleEntity
        {
            Id = Guid.NewGuid(), Name = "project", SourceField = "sprk_project", SourceFieldType = 1,
            TargetField = "sprk_regardingproject", TargetFieldType = CreateTimeFieldMapping.FieldTypeLookup,
            MappingType = CreateTimeFieldMapping.MappingTypeCopy, ExecutionOrder = 4, IsActive = true,
        }];
    }

    // ── The service, over the doubled boundary ───────────────────────────────────────────────────────────────────

    private RecordCreationService Service()
    {
        var entities = Entities();
        var appOnly = new Mock<IFieldMappingDataverseService>(MockBehavior.Strict);
        appOnly
            .Setup(a => a.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns((string source, string target, bool _, CancellationToken _) =>
                Task.FromResult(_profiles.TryGetValue((source, target), out var profile) ? profile : null));

        var invalidator = new Mock<IMembershipCacheInvalidator>();
        invalidator
            .Setup(i => i.InvalidateRecordOwnerChangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid, string?, CancellationToken>((table, set, id, _, _) => Evictions.Add((table, set, id)))
            .Returns(Task.CompletedTask);
        var scopes = new ServiceCollection().AddSingleton(invalidator.Object).BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new RecordCreationService(
            new CapturingResolver(Directory.Resolver(), OwnerContexts), appOnly.Object, entities,
            SecureRootFilingGateFixtures.Over(entities),
            new CoreAncestorRestamper(_stamps.Service, _stamps.Resolver, NullLogger<CoreAncestorRestamper>.Instance),
            SecureChildShareWorld.SynchronizerOver(SecureChildShareWorld.Standard, new FakeRecordShareTable()),
            scopes, new Mock<IMembershipEventPublisher>().Object, NullLogger<RecordCreationService>.Instance);
    }

    /// <summary>
    /// The app-only Dataverse (STRICT: any call not set up here fails the test): whole-row and catalog queries answered by the
    /// directory, a team's and a user's row, a business unit's defaults, a mapping source's columns, and the conditional
    /// update — applied to the row only while it is still at the version the repair read.
    /// </summary>
    private IGenericEntityService Entities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(Directory.Answer(query)));
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) => Task.FromResult(Read(table, id, columns)));
        entities
            .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                BeforeWrite?.Invoke();
                BeforeWrite = null;
                var current = Directory.Version(table, id).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var applied = current == version;
                Writes.Add((table, id, version, new Dictionary<string, object>(fields), applied));
                if (!applied)
                    throw new DBConcurrencyException($"Test: {table} {id} is at version {current}, not {version}.");
                Directory.Write(table, id, fields.ToDictionary(f => f.Key, f => (object?)f.Value));
                return Task.CompletedTask;
            });
        return entities.Object;
    }

    private Entity Read(string table, Guid id, string[] columns)
    {
        switch (table)
        {
            case "team":
            {
                var query = new QueryExpression("team") { ColumnSet = new ColumnSet("businessunitid") };
                query.Criteria.AddCondition("teamid", ConditionOperator.Equal, id);
                return Directory.Answer(query).Entities.Single();
            }

            case "systemuser":
            {
                var user = new Entity("systemuser", id);
                if (id == AppUser)
                    user[ApplicationUserCheck.ApplicationIdAttribute] = Guid.Parse("a0160000-0000-4000-8000-0000000000ff");
                return user;
            }

            case "businessunit":
            {
                var unit = new Entity("businessunit", id);
                if (SearchIndexDefaults.TryGetValue(id, out var defaults))
                {
                    if (defaults.Name is { } name)
                        unit["sprk_searchindexname"] = name;
                    if (defaults.Index is { } index)
                        unit["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", index);
                }

                return unit;
            }

            default:
            {
                // A mapping source's columns (the system principal's app-only read).
                var row = Directory.Row(table, id);
                var copy = new Entity(table, id);
                foreach (var column in columns)
                {
                    if (row.Attributes.TryGetValue(column, out var value))
                        copy[column] = value;
                }

                return copy;
            }
        }
    }

    private static FieldMappingProfileEntity Profile(string source, string target, params FieldMappingRuleEntity[] rules) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{source} to {target}",
        SourceEntity = source,
        TargetEntity = target,
        IsActive = true,
        Rules = [.. rules],
    };

    private static FieldMappingRuleEntity Copy(string source, string target, int order) => new()
    {
        Id = Guid.NewGuid(), Name = $"{source} -> {target}", SourceField = source, SourceFieldType = 0, TargetField = target,
        TargetFieldType = CreateTimeFieldMapping.FieldTypeText, MappingType = CreateTimeFieldMapping.MappingTypeCopy,
        ExecutionOrder = order, IsActive = true,
    };

    /// <summary>The REAL resolver, recording every owner decision asked of it.</summary>
    private sealed class CapturingResolver(IRecordOwnershipResolver inner, List<RecordOwnershipContext> asked) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            asked.Add(context);
            return inner.ResolveOwningTeamAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            asked.Add(context);
            return inner.ResolveOwnerAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent reparent, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(reparent, applyChange, ct);
    }
}
