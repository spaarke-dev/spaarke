using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Communication.Engine;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Seam (ADR-038 <c>tests/integration/seam/**</c> KEEP path; email-communication-intelligence-r2 task 030,
/// FR-D1 / FR-06) for the RAG grounding key computed by <see cref="RegardingParentEntityMapper"/> and passed
/// as <c>PostUploadIndexingRequest.ParentEntity</c> at both index-enqueue sites
/// (<c>IncomingCommunicationProcessor.EnqueueRagIndexingAsync</c> inbound +
/// <c>CommunicationEnrichmentService.RunRagIndexingAsync</c> outbound). Before this fix both passed
/// <c>ParentEntity: null</c>, so indexed correspondence was never scoped to its matter and matter-scoped RAG
/// returned zero. Doubles only the module boundary <see cref="IGenericEntityService"/>.
///
/// Guards the trust-critical properties:
/// (a) the primary regarding (highest-priority set, <see cref="RegardingFieldMap"/> order) becomes the
///     grounding key when its type is representable in <see cref="ParentEntityContext"/>;
/// (b) MISFILE GUARD — a non-representable primary (e.g. service request) degrades to null and does NOT fall
///     through to a lower-priority representable regarding (which would scope the document to the wrong parent);
/// (c) no regarding → null grounding (handler runs its own resolver chain);
/// (d) NEGATIVE (NFR-04) — a failure resolving the regarding returns null and NEVER throws into capture/send.
/// </summary>
public sealed class RagGroundingParentEntitySeamTests
{
    private const string CommunicationEntity = "sprk_communication";

    private static Entity Communication(params (string RegardingField, string TargetType, Guid Id, string? Name)[] regardings)
    {
        var e = new Entity(CommunicationEntity, Guid.NewGuid());
        foreach (var (field, targetType, id, name) in regardings)
        {
            e[field] = new EntityReference(targetType, id) { Name = name };
        }
        return e;
    }

    [Fact]
    public void FromCommunication_MatterRegarding_ReturnsMatterGroundingKey()
    {
        var matterId = Guid.NewGuid();
        var comm = Communication(("sprk_regardingmatter", "sprk_matter", matterId, "Acme v. Widget"));

        var result = RegardingParentEntityMapper.FromCommunication(comm);

        result.Should().NotBeNull();
        result!.EntityType.Should().Be(ParentEntityContext.EntityTypes.Matter);
        result.EntityId.Should().Be(matterId.ToString());
        result.EntityName.Should().Be("Acme v. Widget");
    }

    [Fact]
    public void FromCommunication_ProjectRegardingOnly_ReturnsProjectGroundingKey()
    {
        var projectId = Guid.NewGuid();
        var comm = Communication(("sprk_regardingproject", "sprk_project", projectId, "Migration"));

        var result = RegardingParentEntityMapper.FromCommunication(comm);

        result!.EntityType.Should().Be(ParentEntityContext.EntityTypes.Project);
        result.EntityId.Should().Be(projectId.ToString());
    }

    [Fact]
    public void FromCommunication_MatterAndContact_PrefersHigherPriorityMatter()
    {
        var matterId = Guid.NewGuid();
        // Both a matter (priority 1) and a contact (priority last) are set; matter is the primary.
        var comm = Communication(
            ("sprk_regardingperson", "contact", Guid.NewGuid(), "Ralph Schroeder"),
            ("sprk_regardingmatter", "sprk_matter", matterId, "Acme v. Widget"));

        var result = RegardingParentEntityMapper.FromCommunication(comm);

        result!.EntityType.Should().Be(ParentEntityContext.EntityTypes.Matter);
        result.EntityId.Should().Be(matterId.ToString());
    }

    [Fact]
    public void FromCommunication_NonRepresentablePrimary_ReturnsNull_DoesNotFallThroughToSecondary()
    {
        // Service request is the primary (higher priority than account) but is NOT representable in
        // ParentEntityContext. The mapper must degrade to null rather than misfiling to the account.
        var comm = Communication(
            ("sprk_regardingservicerequest", "sprk_servicerequest", Guid.NewGuid(), "SR-1"),
            ("sprk_regardingaccount", "account", Guid.NewGuid(), "Contoso"));

        var result = RegardingParentEntityMapper.FromCommunication(comm);

        result.Should().BeNull();
    }

    [Fact]
    public void FromCommunication_NoRegarding_ReturnsNull()
    {
        var result = RegardingParentEntityMapper.FromCommunication(Communication());

        result.Should().BeNull();
    }

    [Fact]
    public void FromCommunication_MatterWithBlankReferenceName_UsesTypeFallbackName()
    {
        var comm = Communication(("sprk_regardingmatter", "sprk_matter", Guid.NewGuid(), null));

        var result = RegardingParentEntityMapper.FromCommunication(comm);

        result!.EntityName.Should().Be("Unknown matter");
    }

    [Fact]
    public async Task ResolveAsync_WhenRegardingResolves_ReturnsMappedGroundingKey()
    {
        var communicationId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var service = new Mock<IGenericEntityService>();
        service
            .Setup(s => s.RetrieveAsync(CommunicationEntity, communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Communication(("sprk_regardingmatter", "sprk_matter", matterId, "Acme v. Widget")));

        var result = await RegardingParentEntityMapper.ResolveAsync(
            service.Object, communicationId, NullLogger.Instance, CancellationToken.None);

        result!.EntityType.Should().Be(ParentEntityContext.EntityTypes.Matter);
        result.EntityId.Should().Be(matterId.ToString());
    }

    [Fact]
    public async Task ResolveAsync_WhenRetrieveThrows_ReturnsNullAndDoesNotThrow()
    {
        // NFR-04: grounding is a search-quality enhancement; a resolve failure must degrade to null
        // grounding and NEVER propagate into the capture/send path.
        var communicationId = Guid.NewGuid();
        var service = new Mock<IGenericEntityService>();
        service
            .Setup(s => s.RetrieveAsync(CommunicationEntity, communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var act = async () => await RegardingParentEntityMapper.ResolveAsync(
            service.Object, communicationId, NullLogger.Instance, CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.Should().BeNull();
    }
}
