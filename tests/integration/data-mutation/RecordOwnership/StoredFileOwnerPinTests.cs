using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 task 086 (procedure model 5.2/9.1): the owner pin of the ONE sprk_storedfile create (<see cref="StoredFileService.BuildRow"/>).
/// Driven over the REAL <see cref="RecordOwnershipResolver"/> reading one in-memory Dataverse (<see cref="SecureChildShareWorld"/>): an
/// invoice Stored File of a Secure matter, project or work assignment is the Secure team's on both create paths — the service's own
/// (StageCreateFlipAsync: <see cref="RecordOwnershipContext.ContentOf"/> its invoice, the caller as RequestedBy) and the Submit Invoice
/// persister's (task 092: its one <see cref="RecordOwnershipContext.ForParents"/> resolution over the core record, handed to BuildRow).
/// The caller's own business-unit team never owns it; the resolver is asked once; a refusal writes nothing.
/// </summary>
public class StoredFileOwnerPinTests
{
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid OtherBu = Guid.Parse("20860000-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherBuTeam = Guid.Parse("20860000-0000-4000-8000-0000000000c2");
    private static readonly Guid Caller = Guid.Parse("20860000-0000-4000-8000-0000000000c3");
    private static readonly byte[] Pdf = "%PDF-1.7 086 owner pin"u8.ToArray();

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Billing"))
        .Team(OtherBuTeam, OtherBu, "Billing", isDefault: true, teamType: 0)
        .User(Caller, OtherBu);

    private int _questions;

    private IRecordOwnershipResolver Resolver() => new Counting(
        new RecordOwnershipResolver(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, SecureChildShareWorld.Configuration(),
            NullLogger<RecordOwnershipResolver>.Instance),
        () => _questions++);

    private StoredFileService ServiceOver(StoredFileRig rig) =>
        new(rig.Dataverse.Service, rig.Blobs, rig.Probe, Resolver(), rig.Provider, NullLogger<StoredFileService>.Instance);

    private static StoredFileRowSpec Spec(Guid invoice) => new()
    {
        FileId = Guid.NewGuid(),
        Purpose = StoredFilePurpose.InvoicePdf,
        Parent = new StoredFileParent(StoredFileParentKind.Invoice, invoice),
        Name = "invoice.pdf",
        ContentType = "application/pdf",
    };

    [Theory(DisplayName = "086 must-fire: the service's create of a Secure core's invoice file is owned by the Secure team, never the caller's")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task TheServicesCreate_OfASecureCoresInvoiceFile_IsTheSecureTeams(string coreTable)
    {
        var (core, invoice) = (Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot(coreTable, core).SecureChild("sprk_invoice", invoice, (coreTable, coreTable, core));
        var rig = new StoredFileRig();

        var result = await ServiceOver(rig).CreateForAppAsync(Spec(invoice), new MemoryStream(Pdf), requestedBy: Caller, CancellationToken.None);

        result.IsCreated.Should().BeTrue();
        var row = rig.Dataverse.Rows[result.FileId!.Value];
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeam));
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(OtherBuTeam, "the caller's business-unit team never owns it");
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Caller);
        _questions.Should().Be(1, "one owner decision per create");
    }

    [Theory(DisplayName = "086 must-fire: the persister's one ForParents resolution over a Secure core owns the file through BuildRow")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task ThePersistersResolution_OverASecureCore_OwnsTheFile(string coreTable)
    {
        var (core, invoice) = (Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot(coreTable, core);
        var rig = new StoredFileRig();
        var service = ServiceOver(rig);
        var spec = Spec(invoice);
        var staged = await service.StageAsync(spec, new MemoryStream(Pdf), CancellationToken.None);

        // InvoiceSubmissionPersister (task 092): one resolution over [core] for every row of its transaction, no RequestedBy.
        var owner = await Resolver().ResolveOwnerAsync(
            RecordOwnershipContext.ForParents(new RecordOwnershipParent?[] { new(coreTable, core) }), CancellationToken.None);
        var row = service.BuildRow(spec, staged.Blob!, owner, Caller).Target;

        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeam));
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Caller, "the persister names the submitter");
        row.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(invoice);
    }

    [Fact(DisplayName = "086 must-not-fire: an ordinary invoice's file takes the invoice's team, not the caller's business-unit team")]
    public async Task AnOrdinaryInvoicesFile_TakesTheInvoicesTeam()
    {
        var (matter, invoice) = (Guid.NewGuid(), Guid.NewGuid());
        _world.OrdinaryRoot("sprk_matter", matter).OrdinaryChild("sprk_invoice", invoice, ("sprk_matter", "sprk_matter", matter));
        var rig = new StoredFileRig();

        var result = await ServiceOver(rig).CreateForAppAsync(Spec(invoice), new MemoryStream(Pdf), requestedBy: Caller, CancellationToken.None);

        result.IsCreated.Should().BeTrue();
        rig.Dataverse.Rows[result.FileId!.Value].GetAttributeValue<EntityReference>("ownerid")
            .Should().BeEquivalentTo(new EntityReference("team", GeneralTeam));
        _questions.Should().Be(1);
    }

    [Fact(DisplayName = "086 must-fire: an invoice the resolver cannot place refuses OwnerUnresolved, with no blob and no row written")]
    public async Task AnUnplaceableInvoice_RefusesAndWritesNothing()
    {
        // The invoice does not exist in this Dataverse: the resolver cannot read its owner.
        var rig = new StoredFileRig();

        var result = await ServiceOver(rig).CreateForAppAsync(Spec(Guid.NewGuid()), new MemoryStream(Pdf), requestedBy: Caller, CancellationToken.None);

        result.RefusalCode.Should().Be(StoredFileRefusal.OwnerUnresolved);
        rig.Gateway.StorageCalls.Should().Be(0);
        rig.Dataverse.Writes.Should().Be(0);
        _questions.Should().Be(1);
    }

    private sealed class Counting(IRecordOwnershipResolver inner, Action asked) : IRecordOwnershipResolver
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
