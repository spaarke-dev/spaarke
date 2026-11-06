using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
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
/// ebilling-r1 task 151c-1a, uac-r2 access review (F3, F4): Email to AP end to end through the REAL delivery service and the REAL
/// <see cref="CommunicationService"/> send-as-user path (<see cref="RecordingUserEmailRig"/>: the Graph channel and Dataverse writes are the
/// only stand-ins). The property the owner accepted: <b>the email is readable exactly like the batch, and no row holds the bytes.</b> Batches
/// are owned by the creator's business-unit default team (task 150), so same-unit users who can read batches can read AP emails; that is by
/// decision, not a leak.
/// </summary>
/// <remarks>
/// <para><b>The owner question</b> is answered from the batch ROW, as the real resolver answers a content row of a team-owned parent: the
/// default team of the batch's owning business unit (<c>RecordOwnershipResolver</c> skips the look-through for a team-owned parent). So the
/// check that the answer equals the batch's owning team (F4) is exercised for real: a batch owned by any other team is refused.</para>
/// <para><b>The read model</b> applies Dataverse's team-owned-row rule (Read for a member of the owning team) to the batch and the communication,
/// and models the stored-file gate as what it is: Read on the parent batch.</para>
/// <para><b>Not wired here</b>: the participant index and the communication-arrived producer (null in the rig). They write metadata about the
/// message (addresses, a refresh signal), never the files; the no-bytes assertion covers every row this path does create.</para>
/// </remarks>
public sealed class ApExportEmailAccessTests
{
    private static readonly Guid BatchUnit = Guid.Parse("15100000-0000-4000-8000-0000000000b1");
    private static readonly Guid BatchUnitTeam = ApExportWorld.TeamId; // the batch unit's default team: what task 150 owns batches by
    private static readonly Guid OtherTeamInTheUnit = Guid.Parse("15100000-0000-4000-8000-0000000000f1");
    private static readonly Guid SenderOtherUnitTeam = Guid.Parse("15100000-0000-4000-8000-0000000000a2");

    private const string UnitMember = "unit-member";            // in the batch unit's default team
    private const string OtherTeamMember = "other-team-member"; // in another team of the same unit only
    private const string SenderColleague = "sender-colleague";  // in the sender's other unit only

    /// <summary>Read for a member of the team that owns the row (Dataverse's team-owned-row rule at the depth the app's roles use).</summary>
    private sealed class OwnerTeamReadModel
    {
        private readonly Dictionary<Guid, HashSet<string>> _members = new()
        {
            [BatchUnitTeam] = [UnitMember],
            [OtherTeamInTheUnit] = [OtherTeamMember],
            [SenderOtherUnitTeam] = [SenderColleague],
        };

        public bool CanRead(string user, Entity row) =>
            row.GetAttributeValue<EntityReference>("ownerid") is { LogicalName: "team" } owner
            && _members.TryGetValue(owner.Id, out var members)
            && members.Contains(user);
    }

    private static readonly string[] Users = [UnitMember, OtherTeamMember, SenderColleague];

    /// <summary>
    /// A world with one Completed, scanned batch whose row carries its business unit, a settings row with the AP address, and a rig whose
    /// resolver answers a content row of the batch with the default team of the batch row's business unit (and the sender's other-unit team
    /// for any other question: the default the email would get if the batch were ignored).
    /// </summary>
    private static async Task<(ApExportWorld World, RecordingUserEmailRig Rig, Guid BatchId, ApExportDeliveryService Delivery)> ArrangeAsync()
    {
        var world = new ApExportWorld();
        var rig = new RecordingUserEmailRig();
        world.Probe.CallerSystemUserId = ApExportWorld.CallerSystemUser;
        world.Probe.Hold(ApExportAuthorization.CreateBatchPrivilege);
        var export = await world.ExportAsync(world.AddInvoice("INV-A", approved: 900m), world.AddInvoice("INV-B", approved: 600.5m));
        var batchId = export.BatchId!.Value;
        world.Dataverse.Touch("sprk_billingexport", batchId, new Dictionary<string, object> { ["owningbusinessunit"] = new EntityReference("businessunit", BatchUnit) });
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

        var unitDefaultTeams = new Dictionary<Guid, Guid> { [BatchUnit] = BatchUnitTeam };
        rig.Owners.AnswerFor = context =>
        {
            if (context.TargetEntityLogicalName != "sprk_billingexport" || context.TargetRecordId is not { } id)
            {
                return RecordOwnerResolution.Owned(SenderOtherUnitTeam);
            }

            var batch = world.Dataverse.Get("sprk_billingexport", id);
            var unit = batch.GetAttributeValue<EntityReference>("owningbusinessunit")?.Id;
            return unit is { } u && unitDefaultTeams.TryGetValue(u, out var team)
                ? RecordOwnerResolution.Owned(team)
                : RecordOwnerResolution.Refused(RecordOwnerRefusal.NoDefaultOwnerTeam, "no default team for the batch's unit");
        };

        var accounts = new CommunicationAccountService(
            Mock.Of<ICommunicationDataverseService>(), Mock.Of<IGenericEntityService>(), Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            NullLogger<CommunicationAccountService>.Instance);
        var delivery = new ApExportDeliveryService(
            world.Service, world.Dataverse.Service, world.Files, new ApExportMailer(rig.Service), accounts, NullLogger<ApExportDeliveryService>.Instance);
        return (world, rig, batchId, delivery);
    }

    [Fact(DisplayName = "151c-1a access (F3): the email is readable exactly like the batch; no row holds the bytes")]
    public async Task TheEmailIsReadableExactlyLikeTheBatch_AndNoRowHoldsTheBytes()
    {
        var (world, rig, batchId, delivery) = await ArrangeAsync();
        var storedFilesBefore = world.StoredFiles().Count;
        var documentsBefore = world.Dataverse.RowsOf("sprk_document").Count;
        var csv = world.TextOf(world.FileOf(batchId, "text/csv"));

        var result = await delivery.EmailAsync(
            batchId, new ApExportEmailRequest(null, null), ApExportWorld.CallerSystemUser, RecordingUserEmailRig.UserContext(), CancellationToken.None);

        // The email went, as the user, with the stored bytes, to the mail channel only.
        result.Outcome.Should().Be(ApExportDeliveryOutcome.Done);
        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        sent.Attachments!.Select(a => a.Name).Should().Equal($"ap-export-{batchId:D}.csv", $"ap-export-{batchId:D}.json");
        Encoding.UTF8.GetString(sent.Attachments![0].Content).Should().Be(csv);

        // Owner: exactly the batch's owner.
        var batch = world.Dataverse.Get("sprk_billingexport", batchId);
        var communication = rig.Created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Subject;
        communication.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(batch.GetAttributeValue<EntityReference>("ownerid"));
        rig.Owners.Requests.Should().Contain(c => c.TargetEntityLogicalName == "sprk_billingexport" && c.TargetRecordId == batchId);

        // Read: for every user, the communication answers exactly as the batch does; the stored files are gated by Read on the batch.
        var model = new OwnerTeamReadModel();
        foreach (var user in Users)
        {
            model.CanRead(user, communication).Should().Be(model.CanRead(user, batch), $"{user} reads the email exactly as they read the batch");
            foreach (var file in world.StoredFiles())
            {
                // The stored-file gate: Read on the file's parent batch (the Stored File hard-deny).
                var parent = world.Dataverse.Get("sprk_billingexport", file.GetAttributeValue<EntityReference>("sprk_billingexport")!.Id);
                model.CanRead(user, parent).Should().Be(model.CanRead(user, communication), $"{user} reads each file exactly as they read the email");
            }
        }

        model.CanRead(UnitMember, communication).Should().BeTrue("a same-unit user who can read batches reads the AP email (owner decision)");
        model.CanRead(SenderColleague, communication).Should().BeFalse();

        // No row holds the bytes: every row this send created carries no byte value and no attachment column; no document or attachment row exists;
        // the only stored files are the batch's own, written at export.
        rig.Created.SelectMany(e => e.Attributes.Values).Should().NotContain(v => v is byte[]);
        rig.Created.SelectMany(e => e.Attributes.Keys).Should().NotContain(k => k.Contains("attachment", StringComparison.OrdinalIgnoreCase) && k != "sprk_hasattachments");
        rig.Created.Should().NotContain(e => e.LogicalName == "sprk_document" || e.LogicalName == "sprk_communicationattachment");
        communication.Contains("sprk_hasattachments").Should().BeFalse("no attachment rows exist to count");
        world.Dataverse.RowsOf("sprk_document").Count.Should().Be(documentsBefore);
        world.Dataverse.RowsOf("sprk_communicationattachment").Should().BeEmpty();
        world.StoredFiles().Count.Should().Be(storedFilesBefore);

        // Control: an email left with the default owner (the sender's other-unit team) would NOT read like the batch, so the property can fail.
        var defaultOwned = new Entity("sprk_communication") { ["ownerid"] = new EntityReference("team", SenderOtherUnitTeam) };
        Users.Any(u => model.CanRead(u, defaultOwned) != model.CanRead(u, batch)).Should().BeTrue();
    }

    [Fact(DisplayName = "151c-1a access (F4) SEEDED: a batch owned by another team than its unit's default team is refused (409 email_owner_refused) before anything is sent or created")]
    public async Task BatchOwnedByAnotherTeam_IsRefused_BeforeTheSend()
    {
        var (world, rig, batchId, delivery) = await ArrangeAsync();
        world.Dataverse.Touch("sprk_billingexport", batchId, new Dictionary<string, object> { ["ownerid"] = new EntityReference("team", OtherTeamInTheUnit) });

        var result = await delivery.EmailAsync(
            batchId, new ApExportEmailRequest(null, null), ApExportWorld.CallerSystemUser, RecordingUserEmailRig.UserContext(), CancellationToken.None);

        result.Outcome.Should().Be(ApExportDeliveryOutcome.SendRefused);
        result.Code.Should().Be(ApExportCodes.EmailOwnerRefused);
        rig.Channel.Sent.Should().BeEmpty("nothing is sent when the email could not be owned by the batch's team");
        rig.Created.Should().BeEmpty();
        world.Dataverse.Get("sprk_billingexport", batchId).GetAttributeValue<DateTime?>("sprk_senttoapdate").Should().BeNull();
    }
}
