// unified-access-control-r2 task 194b fix round R (R3b-2) — the archived copy of a SECURE source attachment
// (CommunicationService.SecureCopyOwnershipOf) is asked about in sprk_document's primary order, through the REAL resolver.
// R3b-1 removed the override that named the copy's first secure link first; these pin what that changes and what it does not.

using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

[Trait("status", "task-194b-uac-r2")]
public class SecureCopyOwnershipTests
{
    private static readonly Guid CommunicationId = Guid.Parse("194b0000-0000-4000-8000-0000000000c0");
    private static readonly Guid RelatedMatterId = Guid.Parse("194b0000-0000-4000-8000-0000000000a1");
    private static readonly Guid ProjectId = Guid.Parse("194b0000-0000-4000-8000-0000000000b1");

    private static Entity CopyOf() => new("sprk_document")
    {
        ["sprk_documentname"] = "brief.pdf",
        ["sprk_relatedcommunication"] = new EntityReference("sprk_communication", CommunicationId),
    };

    /// <summary>The links in the order the placement lists them (DocumentLinkFields.All): sprk_relatedmatter before sprk_project.</summary>
    private static readonly IReadOnlyList<(string Column, EntityReference Record)> Links =
    [
        ("sprk_relatedmatter", new EntityReference("sprk_matter", RelatedMatterId)),
        ("sprk_project", new EntityReference("sprk_project", ProjectId)),
    ];

    [Fact]
    public async Task ACopyFiledUnderASecureAndAnOrdinaryRecord_IsOwnedByTheSecureTeam_WhicheverComesFirst()
    {
        // The first link (the related matter) is ordinary; the project is secure: secure-if-any, whatever the order.
        var directory = OwnershipDirectory.Standard()
            .WithRecord("sprk_communication", CommunicationId, OwnershipDirectory.GeneralBu, owningTeam: OwnershipDirectory.GeneralTeam)
            .WithOrdinaryRoot("sprk_matter", RelatedMatterId)
            .WithSecureRoot("sprk_project", ProjectId);

        var owner = await directory.Resolver().ResolveOwnerAsync(
            CommunicationService.SecureCopyOwnershipOf(CopyOf(), Links, requestedBy: null), CancellationToken.None);

        owner.IsOwned.Should().BeTrue(owner.Reason);
        owner.OwningTeamId.Should().Be(OwnershipDirectory.SecureNamedTeam);
        owner.IsSecureOwner.Should().BeTrue();
    }

    [Fact]
    public async Task ACopyFiledUnderOrdinaryRecordsInTwoUnits_IsOwnedByTheDocumentTablesPrimary_TheProject()
    {
        // None secure (the placement saw them secure; the owner reads them ordinary): sprk_document's primary order puts its
        // typed core lookup (sprk_project, general unit) before the sprk_related* links (the matter, child unit).
        var directory = OwnershipDirectory.Standard()
            .WithRecord("sprk_communication", CommunicationId, OwnershipDirectory.GeneralBu, owningTeam: OwnershipDirectory.GeneralTeam)
            .WithOrdinaryRoot("sprk_matter", RelatedMatterId)
            .WithRecord("sprk_project", ProjectId, OwnershipDirectory.GeneralBu, isSecure: false, owningTeam: OwnershipDirectory.GeneralTeam);

        var copy = CopyOf();
        var question = CommunicationService.SecureCopyOwnershipOf(copy, Links, requestedBy: null);
        var owner = await directory.Resolver().ResolveOwnerAsync(question, CancellationToken.None);

        question.Parents.Should().Equal(RecordOwnershipContext.OrderedParentsOf(copy), "the reconciler's question about the written copy");
        question.TargetEntityLogicalName.Should().Be("sprk_project");
        owner.IsOwned.Should().BeTrue(owner.Reason);
        owner.OwningTeamId.Should().Be(OwnershipDirectory.GeneralTeam, "the project, sprk_document's primary, is in the general unit");
        owner.IsSecureOwner.Should().BeFalse();
    }
}
