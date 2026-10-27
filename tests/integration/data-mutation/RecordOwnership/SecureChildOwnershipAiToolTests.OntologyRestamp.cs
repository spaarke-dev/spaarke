using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// Ontology task 144 (D-131, child-record contract C2 §5 items 4-6): a Signal and a Decision Record about a communication follow
/// it when it is re-filed — re-stamped (<see cref="CoreAncestorRestamper"/>, now registered for both tables) and re-owned
/// (<see cref="Sprk.Bff.Api.Services.Access.SecureChildReconciler.AfterRefileAsync"/>) in the same operation — through the
/// communication filing route and the <c>dataverse.update_record</c> chat tool. The Office reconcile is driven in
/// <c>ReconciledEmailFilingTests</c>.
/// </summary>
/// <remarks>
/// Unlike the rest of this harness, the restamper here runs over the SAME share world as the ownership pass
/// (<see cref="SecureChildShareWorld.RestamperOver"/>): the stamp is one of the parents the ownership rule reads, so the
/// back-move case only releases a row once its stamp has followed the communication.
/// </remarks>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid OntologyCommunication = Guid.Parse("a1440000-0000-4000-8000-0000000000c1");
    private static readonly Guid OntologySignal = Guid.Parse("a1440000-0000-4000-8000-0000000000e1");
    private static readonly Guid OntologyDecisionRecord = Guid.Parse("a1440000-0000-4000-8000-0000000000d1");
    private static readonly Guid OntologyCoreType = Guid.Parse("a1440000-0000-4000-8000-0000000000f1");
    private static readonly Guid OntologyPolicyVersion = Guid.Parse("a1440000-0000-4000-8000-0000000000f2");

    private static readonly string[] OntologyTables = ["sprk_signal", "sprk_decisionrecord"];

    private static readonly string[] DecisionContextColumns =
        ["sprk_matter", "sprk_corerecordid", "sprk_corerecordtype", "sprk_policyversion", "sprk_snapshot"];

    public static TheoryData<string> OntologyRefileRoutes => new() { "communication-filing", "update-tool" };

    private static Guid OntologyRow(string table) => table == "sprk_signal" ? OntologySignal : OntologyDecisionRecord;

    // ── Criteria 1 and 4: ordinary M → secure X ──────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OntologyRefileRoutes))]
    public async Task OntologyRestamp_CommunicationMovedUnderASecureMatter_ReownsAndRestampsItsSignalAndDecisionRecord_DecisionContextUnchanged(
        string route)
    {
        _world.WithRecord("sprk_communication", OntologyCommunication, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
        });
        ShareWorld.OrdinaryChild("sprk_communication", OntologyCommunication, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        OntologyRowsAboutTheCommunication(stamp: OrdinaryMatter, isolated: false);
        var before = OntologyTables.ToDictionary(t => t, DecisionContextOf);

        await RefileOntologyCommunication(route, SecureMatter, OntologyRestamper());

        foreach (var table in OntologyTables)
        {
            var row = OntologyRow(table);
            ShareWorld.OwnerOf(table, row).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam),
                $"the {table} follows its communication into isolation in the same operation");
            ShareWorld.ValueOf<EntityReference>(table, row, "sprk_regardingmatter")!.Id.Should().Be(SecureMatter,
                "and its stamp follows the communication's root");
            DecisionContextOf(table).Should().Equal(before[table], "the decision context is immutable (D-34)");
            ShareWorld.StampWrites.Where(w => w.Id == row).SelectMany(w => w.Fields.Keys)
                .Should().OnlyContain(c => CoreAncestorResolver.CoreAncestorLookups.Any(l => l.LookupAttribute == c));
        }
    }

    // ── Task 149 (D-144 as amended): a gate Decision Record about an UNFILED communication is owned by the communication's own USER ──

    [Theory]
    [MemberData(nameof(OntologyRefileRoutes))]
    public async Task OntologyRestamp_AUserOwnedGateDecisionRecord_FollowsItsCommunicationIntoIsolation_WhenTheCommunicationIsLaterFiledUnderASecureMatter(
        string route)
    {
        // The gate decided while the communication stood alone, so the record is owned by the communication's owning user (D-144 as
        // amended), not by a team. When the communication is later filed under a secure matter, the secure-child walk reaches the record
        // through sprk_regardingcommunication and moves it to the Secure team, exactly as it moves a team-owned one.
        _world.WithRecord("sprk_communication", OntologyCommunication, Directory.ChildBu, owningTeam: Directory.ChildTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", OrdinaryMatter),
        });
        ShareWorld.OrdinaryChild("sprk_communication", OntologyCommunication, ("sprk_regardingmatter", "sprk_matter", SecureMatter));
        OntologyRowsAboutTheCommunication(stamp: OrdinaryMatter, isolated: false, userOwnedDecisionRecord: true);
        ShareWorld.OwnerOf("sprk_decisionrecord", OntologyDecisionRecord).Should().Be(DataversePrincipalRef.User(SecureChildShareWorld.SomeUser),
            "the seed: the decision record starts user-owned");
        var before = DecisionContextOf("sprk_decisionrecord");

        await RefileOntologyCommunication(route, SecureMatter, OntologyRestamper());

        ShareWorld.OwnerOf("sprk_decisionrecord", OntologyDecisionRecord).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam),
            "a user-owned gate record follows its communication into isolation (secure-child walk via sprk_regardingcommunication)");
        DecisionContextOf("sprk_decisionrecord").Should().Equal(before, "the decision context is immutable (D-34)");
    }

    // ── Criterion 3: back to M (a Full Access holder) ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(OntologyRefileRoutes))]
    public async Task OntologyRestamp_CommunicationMovedBackToTheOrdinaryMatter_ReleasesItsSignalAndDecisionRecord_ToTheBusinessUnitTeam(
        string route)
    {
        CommunicationMovedOutOfTheSecureMatter();
        var before = OntologyTables.ToDictionary(t => t, DecisionContextOf);

        await RefileOntologyCommunication(route, OrdinaryMatter, OntologyRestamper());

        foreach (var table in OntologyTables)
        {
            var row = OntologyRow(table);
            ShareWorld.ValueOf<EntityReference>(table, row, "sprk_regardingmatter")!.Id.Should().Be(OrdinaryMatter);
            ShareWorld.OwnerOf(table, row).Should().Be(DataversePrincipalRef.Team(Directory.ChildTeam),
                $"the {table} follows the communication out to the ordinary matter's business unit team (the harness's directory "
                + "places the matter, and so the released communication, in that unit)");
            _shareTable.MaskOf(table, row, DataversePrincipalRef.User(Sharee)).Should().BeNull(
                "its mirror of the secure matter's sharees goes with it");
            DecisionContextOf(table).Should().Equal(before[table]);
        }
    }

    [Fact]
    public async Task OntologyRestamp_WithoutTheRestamp_AStaleSecureStampKeepsTheRowIsolated()
    {
        // Secure-precedence seed (and why the restamp is load-bearing for criterion 3): the restamper is the harness's
        // disconnected one, so the Signal's stamp still names the SECURE matter after the communication moved back.
        // Secure-if-any: the row stays isolated — over-restricted, never widened.
        CommunicationMovedOutOfTheSecureMatter();

        await RefileOntologyCommunication("communication-filing", OrdinaryMatter, Restamper());

        foreach (var table in OntologyTables)
        {
            var row = OntologyRow(table);
            ShareWorld.ValueOf<EntityReference>(table, row, "sprk_regardingmatter")!.Id.Should().Be(SecureMatter);
            ShareWorld.OwnerOf(table, row).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
            _shareTable.MaskOf(table, row, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        }
    }

    [Fact]
    public async Task OntologyRestamp_AReownThatIsRefused_LeavesTheRowIsolated_TheStampStillFollows_AndTheReFileStands()
    {
        // Fail-closed seed: the release is refused by Dataverse; the row keeps its Secure owner (never a wider one) and
        // its mirror, the re-file answers 204, and the stamp (which the restamper writes, never the owner) still follows.
        CommunicationMovedOutOfTheSecureMatter();
        ShareWorld.RefusingOwnerWritesOf(OntologySignal);

        var result = await RefileOntologyCommunication("communication-filing", OrdinaryMatter, OntologyRestamper());

        Status(result!).Should().Be(StatusCodes.Status204NoContent, Detail(result!));
        ShareWorld.OwnerOf("sprk_signal", OntologySignal).Should().Be(DataversePrincipalRef.Team(Directory.SecureNamedTeam));
        _shareTable.MaskOf("sprk_signal", OntologySignal, DataversePrincipalRef.User(Sharee)).Should().Be(CollaborateMask);
        ShareWorld.ValueOf<EntityReference>("sprk_signal", OntologySignal, "sprk_regardingmatter")!.Id.Should().Be(OrdinaryMatter);
        ShareWorld.OwnerOf("sprk_decisionrecord", OntologyDecisionRecord).Should().Be(DataversePrincipalRef.Team(Directory.ChildTeam),
            "a refusal on one row does not hold the others");
    }

    // ── Harness ────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The REAL restamper over the share world the ownership pass reads.</summary>
    private CoreAncestorRestamper OntologyRestamper() => SecureChildShareWorld.RestamperOver(() => ShareWorld);

    /// <summary>
    /// The communication is isolated under the secure matter, its Signal and Decision Record too (stamped with it, mirrored);
    /// a Full Access holder moves it to the ordinary matter. The share world sees the PATCH's result.
    /// </summary>
    private void CommunicationMovedOutOfTheSecureMatter()
    {
        _world.WithRecord("sprk_communication", OntologyCommunication, Directory.SecureBu, owningTeam: Directory.SecureNamedTeam, extra: new()
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
        });
        _user.FullAccessOn.Add(SecureMatter);
        _user.OwningTeamOf[OntologyCommunication] = Directory.SecureNamedTeam;
        ShareWorld.Add("businessunit", Directory.ChildBu, ("name", "Spaarke Business Unit 1"));
        ShareWorld.Team(Directory.ChildTeam, Directory.ChildBu, "Spaarke Business Unit 1", isDefault: true, teamType: 0);
        // 194b: the share world places the ordinary matter where the harness's directory does (its unit's team). The Signal's and
        // Decision Record's core IS that matter, and their writers ask about the core first, so the two worlds must agree on it.
        ShareWorld.Add("sprk_matter", OrdinaryMatter, ("owningteam", new EntityReference("team", Directory.ChildTeam)), ("sprk_issecure", false));
        ShareWorld.SecureChild("sprk_communication", OntologyCommunication, ("sprk_regardingmatter", "sprk_matter", OrdinaryMatter));
        OntologyRowsAboutTheCommunication(stamp: SecureMatter, isolated: true);
        foreach (var table in OntologyTables)
            _shareTable.Seed(table, OntologyRow(table), DataversePrincipalRef.User(Sharee), CollaborateMask);
    }

    /// <summary>A Signal and a Decision Record whose subject is the communication, stamped with <paramref name="stamp"/>, and
    /// a full decision context naming the ordinary matter they were decided about.</summary>
    private void OntologyRowsAboutTheCommunication(Guid stamp, bool isolated, bool userOwnedDecisionRecord = false)
    {
        foreach (var table in OntologyTables)
        {
            var row = OntologyRow(table);
            var lookups = new[]
            {
                ("sprk_regardingcommunication", "sprk_communication", OntologyCommunication),
                ("sprk_regardingmatter", "sprk_matter", stamp),
            };
            if (isolated)
                ShareWorld.SecureChild(table, row, lookups);
            else if (userOwnedDecisionRecord && table == "sprk_decisionrecord")
                ShareWorld.UserOwnedChild(table, row, lookups);
            else
                ShareWorld.OrdinaryChild(table, row, lookups);
            ShareWorld.Set(table, row, "sprk_matter", new EntityReference("sprk_matter", OrdinaryMatter));
            ShareWorld.Set(table, row, "sprk_corerecordid", OrdinaryMatter.ToString("D"));
            ShareWorld.Set(table, row, "sprk_corerecordtype", new EntityReference("sprk_recordtype_ref", OntologyCoreType));
            // 194b: as the writers file them: the regarding pair names the subject, the catalog row names the core's table.
            ShareWorld.Set(table, row, "sprk_regardingrecordid", OntologyCommunication.ToString("D"));
            ShareWorld.Add("sprk_recordtype_ref", OntologyCoreType, ("sprk_recordlogicalname", "sprk_matter"));
            ShareWorld.Set(table, row, "sprk_policyversion", new EntityReference("sprk_policyversion", OntologyPolicyVersion));
            ShareWorld.Set(table, row, "sprk_snapshot", "{\"evidence\":[1,2,3]}");
        }
    }

    private List<string> DecisionContextOf(string table) =>
        DecisionContextColumns.Select(c => c + "=" + (ShareWorld.ValueOf<object>(table, OntologyRow(table), c) switch
        {
            EntityReference r => $"{r.LogicalName}:{r.Id:D}",
            null => "<absent>",
            var v => v.ToString(),
        })).ToList();

    /// <summary>Re-files the communication to <paramref name="matter"/> through one route; returns the route's result
    /// (null for the chat tool, whose success is asserted here).</summary>
    private async Task<IResult?> RefileOntologyCommunication(string route, Guid matter, CoreAncestorRestamper restamper)
    {
        if (route == "communication-filing")
        {
            var result = await ChildRecordEndpoints.UpdateAsync(
                "sprk_communication", OntologyCommunication,
                Payload(new() { ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})" }),
                _user, _world.Resolver(), restamper, Children(), HttpContextOfCaller(), NullLogger<Program>.Instance,
                CancellationToken.None, filingOnly: true);
            Status(result).Should().Be(StatusCodes.Status204NoContent, Detail(result));
            return result;
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => Children());
        services.AddSingleton(new Mock<IGenericEntityService>().Object);
        var tool = await new Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler(
                _user, new CoreAncestorAfterWriteRestamp(restamper),
                NullLogger<Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler>.Instance, _world.Resolver(),
                Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>())
            .ExecuteChatAsync(
                BuildChatInvocationContext(toolArgumentsJson: JsonSerializer.Serialize(new
                {
                    tablename = "sprk_communication",
                    recordId = OntologyCommunication,
                    item = new Dictionary<string, JsonElement> { ["sprk_regardingmatter"] = Lookup("sprk_regardingmatter", "sprk_matter", matter).Value },
                })) with { UserId = Guid.NewGuid().ToString() },
                BuildAnalysisTool(nameof(Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler)), CancellationToken.None);
        tool.Success.Should().BeTrue(tool.ErrorMessage);
        return null;
    }
}
