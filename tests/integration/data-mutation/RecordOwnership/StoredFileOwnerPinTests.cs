using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Finance.Files;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.RecordOwnership;

/// <summary>
/// ebilling-r1 task 086 (procedure model 5.2/9.1): the owner pin of the ONE sprk_storedfile create (<see cref="StoredFileService.BuildRow"/>).
/// Driven over the REAL <see cref="RecordOwnershipResolver"/> reading one in-memory Dataverse (<see cref="SecureChildShareWorld"/>): an
/// invoice Stored File of a Secure matter, project or work assignment is the Secure team's on both create paths — the service's own
/// (StageCreateFlipAsync: the row's own question, <see cref="RecordOwnershipContext.ForRow"/> over its invoice, the caller as RequestedBy)
/// and the Submit Invoice persister's (<see cref="InvoiceSubmissionPersister.StoredFileOwnerContext"/>: one resolution over the core
/// record, handed to BuildRow). The caller's own business-unit team never owns it; an ordinary invoice's file goes to the restricted-content
/// owner team (uac-r2 194b R4b-2, sprk_storedfile is a hard-deny table); the resolver is asked once; a refusal writes nothing.
/// </summary>
public class StoredFileOwnerPinTests
{
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid ContentTeam = SecureChildShareWorld.RestrictedContentTeam;
    private static readonly Guid CustomerTeam = SecureChildShareWorld.CustomerTeam;
    private static readonly Guid OtherBu = Guid.Parse("20860000-0000-4000-8000-0000000000c1");
    private static readonly Guid OtherBuTeam = Guid.Parse("20860000-0000-4000-8000-0000000000c2");
    private static readonly Guid Caller = Guid.Parse("20860000-0000-4000-8000-0000000000c3");
    private static readonly Guid OtherContentTeam = Guid.Parse("20860000-0000-4000-8000-0000000000c4");
    private static readonly byte[] Pdf = "%PDF-1.7 086 owner pin"u8.ToArray();

    // The caller sits under a SECOND customer top unit (a direct child of the root, beside the invoice's customer unit, which holds
    // ContentTeam), and that unit has a restricted-content owner team of its own. So "the invoice's customer team, not the caller's"
    // can fail: an owner question that followed the caller's business unit would answer OtherContentTeam (or OtherBuTeam), not
    // ContentTeam. Were the caller under CustomerBu, the caller's top unit and the invoice's would be one, and no wrong pick could show.
    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Billing"),
            ("parentbusinessunitid", new EntityReference("businessunit", SecureChildShareWorld.GeneralBu)))
        .Team(OtherBuTeam, OtherBu, "Billing", isDefault: true, teamType: 0)
        .Team(OtherContentTeam, OtherBu, Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam.DefaultOwnerTeamName,
            isDefault: false, teamType: 0)
        .User(Caller, OtherBu);

    private int _questions;

    private IRecordOwnershipResolver Resolver() => new Counting(
        new RecordOwnershipResolver(
            SecureChildShareWorld.EntitiesOver(() => _world).Object, SecureChildShareWorld.Configuration(),
            NullLogger<RecordOwnershipResolver>.Instance),
        () => _questions++);

    private StoredFileService ServiceOver(StoredFileRig rig) =>
        new(rig.Dataverse.Service, rig.Blobs, rig.Probe, Resolver(), rig.Provider, NullLogger<StoredFileService>.Instance);

    // A supporting file: the service's own create path (an invoice PDF is created only by the persister, task 094's writer guard).
    private static StoredFileRowSpec SupportingFile(Guid invoice) => new()
    {
        FileId = Guid.NewGuid(),
        Purpose = StoredFilePurpose.InvoiceSupportingFile,
        Parent = new StoredFileParent(StoredFileParentKind.Invoice, invoice),
        Name = "timesheet.pdf",
        ContentType = "application/pdf",
    };

    private static StoredFileRowSpec InvoicePdf(Guid invoice) => new()
    {
        FileId = Guid.NewGuid(),
        Purpose = StoredFilePurpose.InvoicePdf,
        Parent = new StoredFileParent(StoredFileParentKind.Invoice, invoice),
        Name = "invoice.pdf",
        ContentType = "application/pdf",
    };

    private static InvoiceCoreRecordKind KindOf(string coreTable) => coreTable switch
    {
        "sprk_matter" => InvoiceCoreRecordKind.Matter,
        "sprk_project" => InvoiceCoreRecordKind.Project,
        _ => InvoiceCoreRecordKind.WorkAssignment,
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

        var result = await ServiceOver(rig).CreateForAppAsync(SupportingFile(invoice), new MemoryStream(Pdf), requestedBy: Caller, CancellationToken.None);

        result.IsCreated.Should().BeTrue();
        var row = rig.Dataverse.Rows[result.FileId!.Value];
        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeam));
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().NotBe(OtherBuTeam, "the caller's business-unit team never owns it");
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Caller);
        _questions.Should().Be(1, "one owner decision per create");
    }

    [Theory(DisplayName = "086 must-fire: the persister's one resolution over a Secure core owns the file through BuildRow")]
    [InlineData("sprk_matter")]
    [InlineData("sprk_project")]
    [InlineData("sprk_workassignment")]
    public async Task ThePersistersResolution_OverASecureCore_OwnsTheFile(string coreTable)
    {
        var (core, invoice) = (Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot(coreTable, core);
        var rig = new StoredFileRig();
        var service = ServiceOver(rig);
        var spec = InvoicePdf(invoice);
        var staged = await service.StageAsync(spec, new MemoryStream(Pdf), CancellationToken.None);

        // InvoiceSubmissionPersister: one resolution over its core record for every Stored File of its transaction (the invoice is
        // created in the same transaction and cannot be read yet), asked for the sprk_storedfile table.
        var owner = await Resolver().ResolveOwnerAsync(
            InvoiceSubmissionPersister.StoredFileOwnerContext(new InvoiceCoreRecord(KindOf(coreTable), core)), CancellationToken.None);
        var row = service.BuildRow(spec, staged.Blob!, owner, Caller).Target;

        row.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("team", SecureTeam));
        row.GetAttributeValue<EntityReference>("sprk_createdbyperson").Id.Should().Be(Caller, "the persister names the submitter");
        row.GetAttributeValue<EntityReference>("sprk_invoice").Id.Should().Be(invoice);
    }

    [Fact(DisplayName = "086 must-not-fire: an ordinary invoice's file goes to the restricted-content owner team, never the caller's business-unit team")]
    public async Task AnOrdinaryInvoicesFile_TakesTheRestrictedContentTeam()
    {
        var (matter, invoice) = (Guid.NewGuid(), Guid.NewGuid());
        _world.Add("sprk_matter", matter, ("owningteam", new EntityReference("team", CustomerTeam)), ("sprk_issecure", false))
            .Add("sprk_invoice", invoice,
                ("owningteam", new EntityReference("team", CustomerTeam)), ("sprk_matter", new EntityReference("sprk_matter", matter)));
        var rig = new StoredFileRig();

        var result = await ServiceOver(rig).CreateForAppAsync(SupportingFile(invoice), new MemoryStream(Pdf), requestedBy: Caller, CancellationToken.None);

        result.IsCreated.Should().BeTrue();
        var owner = rig.Dataverse.Rows[result.FileId!.Value].GetAttributeValue<EntityReference>("ownerid");
        owner.Should().BeEquivalentTo(new EntityReference("team", ContentTeam), "sprk_storedfile is a hard-deny table (uac-r2 194b R4b-2)");
        owner.Id.Should().NotBe(OtherBuTeam, "the caller's business-unit team never owns it");
        owner.Id.Should().NotBe(OtherContentTeam, "nor the restricted-content team of the caller's own customer top unit");
        _questions.Should().Be(1);
    }

    [Fact(DisplayName = "086 must-fire: an invoice the resolver cannot place refuses OwnerUnresolved, with no blob and no row written")]
    public async Task AnUnplaceableInvoice_RefusesAndWritesNothing()
    {
        // The invoice does not exist in this Dataverse: the resolver cannot read its owner.
        var rig = new StoredFileRig();

        var result = await ServiceOver(rig).CreateForAppAsync(SupportingFile(Guid.NewGuid()), new MemoryStream(Pdf), requestedBy: Caller, CancellationToken.None);

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
