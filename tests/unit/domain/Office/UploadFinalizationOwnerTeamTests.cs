using FluentAssertions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Office;

/// <summary>
/// The owner-team rule of <see cref="UploadFinalizationWorker"/> (spaarkeai-word-add-in-r1 task 080): every document
/// the worker creates for a save — an email's attachment children, and the fallback create — takes the team the save
/// CARRIED on its payload, so children always land in their parent's business unit. Only a payload without one (a
/// message enqueued before task 080) asks the resolver, and then from the save's own association and user.
/// </summary>
/// <remarks>
/// Asserted on the extracted static rule (ADR-038 A2: an internal member extracted because it carries a contract the
/// Service Bus worker's public surface cannot express without a full attachment pipeline). The writer side — that the
/// save puts the team ON the payload — is pinned end to end by <c>OfficeRecordOwnershipTests</c>.
/// </remarks>
[Trait("status", "new")]
public class UploadFinalizationOwnerTeamTests
{
    private static readonly Guid CarriedTeam = Guid.Parse("0a0a0a0a-0080-4080-8080-00000000000a");
    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CallerOid = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe");

    [Fact]
    public async Task ResolveDocumentOwnerTeam_WhenThePayloadCarriesATeam_UsesItWithoutAskingTheResolver()
    {
        var resolver = new RecordOwnershipResolverDouble { TeamId = Guid.NewGuid() }; // a DIFFERENT answer, if asked

        var team = await UploadFinalizationWorker.ResolveDocumentOwnerTeamAsync(
            resolver, Payload(owningTeamId: CarriedTeam), CallerOid.ToString(), CancellationToken.None);

        team.Should().Be(CarriedTeam, "an attachment child must match the parent the save already owned");
        resolver.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveDocumentOwnerTeam_WhenThePayloadPredatesTheField_ResolvesFromTheAssociationAndUser()
    {
        var resolver = new RecordOwnershipResolverDouble();

        var team = await UploadFinalizationWorker.ResolveDocumentOwnerTeamAsync(
            resolver, Payload(owningTeamId: null), CallerOid.ToString(), CancellationToken.None);

        team.Should().Be(RecordOwnershipResolverDouble.DefaultTeamId);
        var asked = resolver.Requests.Should().ContainSingle().Subject;
        asked.TargetEntityLogicalName.Should().Be("matter", "the queued spelling; the resolver maps it");
        asked.TargetRecordId.Should().Be(MatterId);
        asked.CallerObjectId.Should().Be(CallerOid);
    }

    [Fact]
    public async Task ResolveDocumentOwnerTeam_WhenNothingResolves_ReturnsNull_SoTheWorkerCreatesNothing()
    {
        var resolver = new RecordOwnershipResolverDouble { TeamId = null };

        var team = await UploadFinalizationWorker.ResolveDocumentOwnerTeamAsync(
            resolver, Payload(owningTeamId: null), CallerOid.ToString(), CancellationToken.None);

        team.Should().BeNull("the worker refuses (fallback create) or skips (attachment children) — never app-owns");
    }

    private static UploadFinalizationPayload Payload(Guid? owningTeamId) => new()
    {
        ContentType = Sprk.Bff.Api.Models.Office.SaveContentType.Email,
        AssociationType = "matter",
        AssociationId = MatterId,
        ContainerId = "b!drive",
        TempFileLocation = "spe://b!drive/item",
        FileName = "Status.eml",
        OwningTeamId = owningTeamId,
    };
}
