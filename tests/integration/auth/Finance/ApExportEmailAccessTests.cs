using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.DataMutation.Ebilling;
using Sprk.Bff.Api.Tests.Finance.Files;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Auth.Finance;

/// <summary>
/// ebilling-r1 task 151c-1a, uac-r2 access review (items 1-3): Email to AP end to end through the REAL delivery service, the REAL <see cref="CommunicationService"/>
/// send-as-user path (<see cref="RecordingUserEmailRig"/>: the Graph channel and Dataverse writes are the only stand-ins) and a read model that applies
/// Dataverse's rule for a team-owned row. The question: can a user who is in the SAME business unit as the finance team, but is NOT in it, read the sent
/// <c>sprk_communication</c> or anything that holds the files?
/// </summary>
public sealed class ApExportEmailAccessTests
{
    private static readonly Guid FinanceTeam = Guid.Parse("15100000-0000-4000-8000-0000000000f1");
    private static readonly Guid UnitTeam = Guid.Parse("15100000-0000-4000-8000-0000000000a1");
    private const string FinanceUser = "finance-user";
    private const string UnitOnlyUser = "unit-only-user";

    /// <summary>
    /// The Read seam: <see cref="IAccessDataSource"/> answers Read on a row only for a member of the team that owns it (Dataverse's team-owned-row rule at the
    /// depth the app's roles use). Membership: the finance user is in the finance team and the unit's default team; the unit-only user in the unit team alone.
    /// </summary>
    private sealed class OwnerTeamReadModel : IAccessDataSource
    {
        private readonly Dictionary<Guid, HashSet<string>> _members = new()
        {
            [FinanceTeam] = [FinanceUser],
            [UnitTeam] = [FinanceUser, UnitOnlyUser],
        };

        public Dictionary<(string Set, Guid Id), Entity> Rows { get; } = new();

        public Task<AccessSnapshot> GetUserAccessAsync(string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            Answer(userId, "sprk_documents", Guid.Parse(resourceId));

        public Task<AccessSnapshot> GetRecordAccessAsync(string userId, string entitySetName, Guid recordId, string? userAccessToken, CancellationToken ct = default) =>
            Answer(userId, entitySetName, recordId);

        private Task<AccessSnapshot> Answer(string userId, string set, Guid id)
        {
            var rights = AccessRights.None;
            if (Rows.TryGetValue((set, id), out var row)
                && row.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "team" } owner
                && _members.TryGetValue(owner.Id, out var members)
                && members.Contains(userId))
            {
                rights = AccessRights.Read;
            }

            return Task.FromResult(new AccessSnapshot { UserId = userId, ResourceId = id.ToString(), AccessRights = rights });
        }
    }

    [Fact(DisplayName = "151c-1a access: the sent communication is owned by the BATCH's team, no row or attachment holds the files, and a same-unit non-finance user can read neither")]
    public async Task SameUnitNonFinanceUser_CannotReadTheSentCommunicationOrAnyAttachment()
    {
        var world = new ApExportWorld();
        var rig = new RecordingUserEmailRig();
        rig.Owners.AnswerFor = context => context.TargetEntityLogicalName == "sprk_billingexport"
            ? RecordOwnerResolution.Owned(FinanceTeam)
            : RecordOwnerResolution.Owned(UnitTeam);
        world.Probe.CallerSystemUserId = ApExportWorld.CallerSystemUser;
        world.Probe.Hold(ApExportAuthorization.CreateBatchPrivilege);
        var export = await world.ExportAsync(world.AddInvoice("INV-A", approved: 900m), world.AddInvoice("INV-B", approved: 600.5m));
        var batchId = export.BatchId!.Value;
        foreach (var (container, name) in world.Gateway.Names)
        {
            world.Gateway.Get(container, name)!.Tags = StoredFileRig.CleanTags(world.Gateway.Now.AddMinutes(1));
        }

        world.Dataverse.Put(new Entity("sprk_ebillingsetting", Guid.NewGuid())
        {
            ["statecode"] = new OptionSetValue(0),
            ["createdon"] = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            ["sprk_apexportemail"] = "ap@example.com",
        });
        var delivery = new ApExportDeliveryService(
            world.Service, world.Dataverse.Service, world.Files, new ApExportMailer(rig.Service), NullLogger<ApExportDeliveryService>.Instance);
        var storedFilesBefore = world.StoredFiles().Count;
        var documentsBefore = world.Dataverse.RowsOf("sprk_document").Count;
        var csv = world.TextOf(world.FileOf(batchId, "text/csv"));

        var result = await delivery.EmailAsync(
            batchId, new ApExportEmailRequest(null, null), ApExportWorld.CallerSystemUser, RecordingUserEmailRig.UserContext(), CancellationToken.None);

        // The email went, as the user, with the stored bytes -- to the mail channel only.
        result.Outcome.Should().Be(ApExportDeliveryOutcome.Done);
        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        sent.Attachments!.Select(a => a.Name).Should().Equal($"ap-export-{batchId:D}.csv", $"ap-export-{batchId:D}.json");
        Encoding.UTF8.GetString(sent.Attachments![0].Content).Should().Be(csv);

        // Owner: the batch's team (what the resolver answers for the batch), never the unit's default team.
        var communication = rig.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        communication.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", FinanceTeam));
        rig.Owners.Requests.Should().Contain(c => c.TargetEntityLogicalName == "sprk_billingexport" && c.TargetRecordId == batchId);

        // Copies: no document, attachment or any other row holds the files. The communication is the only row the send wrote, and it carries no bytes.
        rig.Created.Select(e => e.LogicalName).Should().OnlyContain(n => n == "sprk_communication");
        rig.Created.SelectMany(e => e.Attributes.Values).Should().NotContain(v => v is byte[]);
        rig.Created.SelectMany(e => e.Attributes.Keys).Should().NotContain(k => k.Contains("attachment", StringComparison.OrdinalIgnoreCase) && k != "sprk_hasattachments");
        communication.Contains("sprk_hasattachments").Should().BeFalse("no attachment rows exist to count");
        world.Dataverse.RowsOf("sprk_document").Count.Should().Be(documentsBefore, "no sprk_document was created");
        world.Dataverse.RowsOf("sprk_communicationattachment").Should().BeEmpty();
        world.StoredFiles().Count.Should().Be(storedFilesBefore, "the only stored files are the batch's own, written at export");
        world.StoredFiles().Should().OnlyContain(f => f.GetAttributeValue<EntityReference>("sprk_billingexport")!.Id == batchId, "each is parented to the batch, so keeps the stored-file hard-deny");

        // Archive: nothing was archived (the send path would have asked for the SPE / document services, which are null in the rig, and ArchiveToSpe is false).
        // Read: a finance-team member reads the communication; a same-unit user outside the team reads neither it nor any file row.
        var readModel = new OwnerTeamReadModel();
        var communicationId = result.Email!.CommunicationId!.Value;
        readModel.Rows[("sprk_communications", communicationId)] = communication;
        foreach (var file in world.StoredFiles())
        {
            // A stored file is not owned by a team a unit user is in either; and the Stored File routes hard-deny a caller without Read on the PARENT batch.
            readModel.Rows[("sprk_storedfiles", file.Id)] = file;
        }

        var financeRead = await readModel.GetRecordAccessAsync(FinanceUser, "sprk_communications", communicationId, null);
        var unitRead = await readModel.GetRecordAccessAsync(UnitOnlyUser, "sprk_communications", communicationId, null);

        financeRead.AccessRights.HasFlag(AccessRights.Read).Should().BeTrue("a member of the batch's team can read the sent communication");
        unitRead.AccessRights.HasFlag(AccessRights.Read).Should().BeFalse("a same-unit user who is not in the batch's team cannot");
        foreach (var file in world.StoredFiles())
        {
            (await readModel.GetRecordAccessAsync(UnitOnlyUser, "sprk_storedfiles", file.Id, null)).AccessRights.HasFlag(AccessRights.Read).Should().BeFalse();
        }

        // Control: had the owner been left to the default (the creator's unit team), the same user WOULD read it -- the test can fail.
        var defaultOwned = new Entity("sprk_communication") { ["ownerid"] = new EntityReference("team", UnitTeam) };
        var defaultId = Guid.NewGuid();
        readModel.Rows[("sprk_communications", defaultId)] = defaultOwned;
        (await readModel.GetRecordAccessAsync(UnitOnlyUser, "sprk_communications", defaultId, null)).AccessRights.HasFlag(AccessRights.Read)
            .Should().BeTrue("the read model does grant a unit-team member Read on a unit-team-owned row");
    }
}
