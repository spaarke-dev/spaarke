using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// ebilling-r1 task 086 (procedure model 5.2/9.1): an invoice Stored File (<c>sprk_storedfile.sprk_invoice</c>) is a leaf child of its
/// invoice in the secure family. Driven over the REAL <see cref="SecureChildReconciler"/>, the REAL ownership resolver and one in-memory
/// Dataverse (<see cref="SecureChildShareWorld"/>), as <see cref="SecureChildRecordOverrideLineageTests"/>: a Stored File follows its
/// invoice (and the invoice its matter) into isolation and back out; the source document and communication it names are citations,
/// never parents; and a Procedure-source or export Stored File (no invoice) is never reached.
/// </summary>
public class SecureChildStoredFileLineageTests
{
    private static readonly Guid GeneralTeam = SecureChildShareWorld.GeneralTeam;
    private static readonly Guid SecureTeam = SecureChildShareWorld.SecureTeam;
    private static readonly Guid Author = SecureChildShareWorld.SomeUser;
    private static readonly Guid OtherBu = Guid.Parse("20860000-0000-4000-8000-0000000000b1");
    private static readonly Guid OtherBuTeam = Guid.Parse("20860000-0000-4000-8000-0000000000b2");

    private readonly SecureChildShareWorld _world = SecureChildShareWorld.Standard()
        .Add("businessunit", OtherBu, ("name", "Billing"))
        .Team(OtherBuTeam, OtherBu, "Billing", isDefault: true, teamType: 0);

    private readonly Sprk.Bff.Api.Tests.Api.ExternalAccess.StubDataverseWebApiClient _webApi = new();

    private SecureChildReconciler Reconciler() =>
        SecureChildShareWorld.ReconcilerOver(() => _world, new FakeRecordShareTable(), _webApi);

    [Fact(DisplayName = "086 must-fire: a matter turning Secure takes its invoice's Stored File into isolation with the invoice, and no other Stored File")]
    public async Task AMatterTurningSecure_ReownsTheInvoicesStoredFile()
    {
        // A Procedure-source Stored File that CITES a document of the matter is in the same pass's reach only if a source lookup were
        // a lineage lookup (must not fire: it keeps its author while the document it cites moves).
        var (matter, invoice, file, document, procedureFile) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", matter)
            .UserOwnedChild("sprk_invoice", invoice, ("sprk_matter", "sprk_matter", matter))
            .UserOwnedChild("sprk_document", document, ("sprk_matter", "sprk_matter", matter))
            .UserOwnedChild("sprk_storedfile", file, ("sprk_invoice", "sprk_invoice", invoice), ("sprk_sourcedocument", "sprk_document", document))
            .UserOwnedChild("sprk_storedfile", procedureFile,
                ("sprk_procedureversion", "sprk_procedureversion", Guid.NewGuid()), ("sprk_sourcedocument", "sprk_document", document));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_invoice", invoice).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(SecureTeam));
        _world.OwnerOf("sprk_storedfile", file).Should().Be(DataversePrincipalRef.Team(SecureTeam), "the file is filed under the Secure matter's invoice");
        _world.OwnerOf("sprk_storedfile", procedureFile).Should().Be(DataversePrincipalRef.User(Author),
            "a Procedure-source file names no invoice; the document it cites does not file it");
    }

    [Fact(DisplayName = "086 must-fire: a matter leaving isolation takes the Stored File out to the matter's team, not the team of the source it cites")]
    public async Task AMatterLeavingIsolation_ReleasesTheStoredFile_AndItsSourcesAreNotParents()
    {
        // The source document and communication live in another business unit, and their lookups come FIRST in the row: if either
        // were read as a parent it would decide the file's team (RecordOwnershipResolver.CitationLookups).
        var (matter, invoice, file) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (document, communication) = (Guid.NewGuid(), Guid.NewGuid());
        _world.FlaggedNotIsolatedRoot("sprk_matter", matter) // mid-unsecure: moved off the Secure team, flag cleared last
            .Add("sprk_document", document, ("owningteam", new EntityReference("team", OtherBuTeam)))
            .Add("sprk_communication", communication, ("owningteam", new EntityReference("team", OtherBuTeam)))
            .SecureChild("sprk_invoice", invoice, ("sprk_matter", "sprk_matter", matter))
            .SecureChild("sprk_storedfile", file,
                ("sprk_sourcedocument", "sprk_document", document),
                ("sprk_sourcecommunication", "sprk_communication", communication),
                ("sprk_invoice", "sprk_invoice", invoice));

        var report = await Reconciler().ReconcileAsync(
            "sprk_matter", matter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Unsecure, CancellationToken.None);

        report.Status.Should().Be(SecureChildReconcileStatus.Completed, string.Join("; ", report.Changes.Select(c => c.Detail)));
        _world.OwnerOf("sprk_invoice", invoice).Should().Be(DataversePrincipalRef.Team(GeneralTeam));
        _world.OwnerOf("sprk_storedfile", file).Should().Be(DataversePrincipalRef.Team(GeneralTeam), "the file leaves isolation with its invoice");
        _world.OwnerOf("sprk_document", document).Should().Be(DataversePrincipalRef.Team(OtherBuTeam), "a cited source document is never reached");
        _world.OwnerOf("sprk_communication", communication).Should().Be(DataversePrincipalRef.Team(OtherBuTeam), "a cited communication is never reached");
    }

    [Fact(DisplayName = "086 must-not-fire: a Procedure-source and an export Stored File, and an ordinary invoice's file, keep their owner")]
    public async Task NonInvoiceStoredFiles_AndAnOrdinaryInvoicesFile_AreUntouched()
    {
        var (secureMatter, ordinaryMatter, ordinaryInvoice) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (procedureFile, exportFile, ordinaryFile) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        _world.SecureRoot("sprk_matter", secureMatter)
            .OrdinaryRoot("sprk_matter", ordinaryMatter)
            // A Procedure document and an export file name no invoice (model 5.2); their lookups are not lineage lookups.
            .UserOwnedChild("sprk_storedfile", procedureFile, ("sprk_procedureversion", "sprk_procedureversion", Guid.NewGuid()))
            .UserOwnedChild("sprk_storedfile", exportFile, ("sprk_billingexport", "sprk_billingexport", Guid.NewGuid()))
            .UserOwnedChild("sprk_invoice", ordinaryInvoice, ("sprk_matter", "sprk_matter", ordinaryMatter))
            .UserOwnedChild("sprk_storedfile", ordinaryFile, ("sprk_invoice", "sprk_invoice", ordinaryInvoice));

        await Reconciler().ReconcileAsync(
            "sprk_matter", secureMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Provisioning, CancellationToken.None);
        await Reconciler().ReconcileAsync(
            "sprk_matter", ordinaryMatter, SecureChildReconcileMode.Apply, SecureChildPassTrigger.Sweep, CancellationToken.None);

        _world.OwnerWrites.Should().BeEmpty("no Stored File here is filed under a Secure matter's invoice");
        foreach (var id in new[] { procedureFile, exportFile, ordinaryFile })
        {
            _world.OwnerOf("sprk_storedfile", id).Should().Be(DataversePrincipalRef.User(Author), $"Stored File {id} keeps its author");
        }
    }
}
