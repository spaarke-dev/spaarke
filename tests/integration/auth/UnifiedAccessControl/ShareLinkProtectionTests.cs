using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 171 — owner round 72 item 2 and round 74 V2/V3: the share-link refusal
/// (<see cref="FileAccessEndpoints.ShareLinkProtectionRefusalAsync"/>) reaches a document's records through EVERY link —
/// a root directly, a child (communication, event, …) through its one-hop core ancestor — and fails closed whenever the
/// protection cannot be determined.
/// </summary>
/// <remarks>
/// Real <see cref="CoreAncestorResolver"/> (sealed; its Dataverse boundary and column probe are substituted) and the
/// real document-container derivation (<see cref="TestRecordContainerResolver.DocumentPointerWorld"/>); the root flags are
/// stated by <see cref="GrantPolicyTestDoubles.FlagStubParticipationService"/> (ADR-038 §4 — no HTTP double).
/// </remarks>
public class ShareLinkProtectionTests
{
    private static readonly Guid DocumentId = Guid.Parse("17140000-0000-4000-8000-000000000001");
    private static readonly Guid ProjectId = Guid.Parse("17140000-0000-4000-8000-000000000002");
    private static readonly Guid MatterId = Guid.Parse("17140000-0000-4000-8000-000000000003");
    private static readonly Guid EventId = Guid.Parse("17140000-0000-4000-8000-000000000004");
    private static readonly Guid CommunicationId = Guid.Parse("17140000-0000-4000-8000-000000000005");

    private readonly Mock<IGenericEntityService> _rows = new();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _flags =
        new(new RootRecordFlags(IsSecure: false, IsRestricted: false));
    private readonly World _world = new();

    private void Row(string entity, Guid id, Entity? row)
        => _rows.Setup(r => r.RetrieveAsync(entity, id, It.IsAny<string[]>(), It.IsAny<CancellationToken>())).ReturnsAsync(row!);

    private static Entity Document(params (string Column, string Target, Guid Id)[] links)
    {
        var row = new Entity("sprk_document", DocumentId);
        foreach (var (column, target, id) in links)
        {
            row[column] = new EntityReference(target, id);
        }

        return row;
    }

    private Task<(string ReasonCode, string Detail)?> RefusalAsync()
    {
        var ancestors = new CoreAncestorResolver(
            _rows.Object,
            (string _, CancellationToken _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment", "sprk_regardingservicerequest",
            }),
            NullLogger<CoreAncestorResolver>.Instance);
        return FileAccessEndpoints.ShareLinkProtectionRefusalAsync(
            DocumentId, _world.Build(), _rows.Object, _flags, ancestors, NullLogger.Instance, CancellationToken.None);
    }

    [Fact(DisplayName = "Round 74 V3: a document filed under a CHILD (an event) of a SECURE project is refused 403 secure")]
    public async Task ChildOfASecureRecord_IsRefusedSecure()
    {
        Row("sprk_document", DocumentId, Document(("sprk_relatedevent", "sprk_event", EventId)));
        Row("sprk_event", EventId, new Entity("sprk_event", EventId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId) });
        _flags.Flags[ProjectId] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        (await RefusalAsync())!.Value.ReasonCode.Should().Be(FileAccessEndpoints.ShareLinkSecureRecordCode);
    }

    [Fact(DisplayName = "Round 74 V3: an email archive / attachment (only sprk_relatedcommunication) of a RESTRICTED matter is refused 403 restricted")]
    public async Task CommunicationLinkedDocumentOfARestrictedMatter_IsRefusedRestricted()
    {
        Row("sprk_document", DocumentId, Document(("sprk_relatedcommunication", "sprk_communication", CommunicationId)));
        Row("sprk_communication", CommunicationId, new Entity("sprk_communication", CommunicationId)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", MatterId),
        });
        _flags.Flags[MatterId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        (await RefusalAsync())!.Value.ReasonCode.Should().Be(FileAccessEndpoints.ShareLinkRestrictedRecordCode,
            "a communication inherits its parent's access permission — and every email archive carries only this link");
    }

    [Fact(DisplayName = "Round 74 V3: an unreadable document row is refused share_link_protection_unverifiable")]
    public async Task UnreadableDocumentRow_IsUnverifiable()
    {
        Row("sprk_document", DocumentId, null);

        (await RefusalAsync())!.Value.ReasonCode.Should().Be(FileAccessEndpoints.ShareLinkProtectionUnverifiableCode);
    }

    [Fact(DisplayName = "Round 74 V3: a child link whose ancestor read ERRORS is refused share_link_protection_unverifiable")]
    public async Task AncestorReadFault_IsUnverifiable()
    {
        Row("sprk_document", DocumentId, Document(("sprk_relatedcommunication", "sprk_communication", CommunicationId)));
        _rows.Setup(r => r.RetrieveAsync("sprk_communication", CommunicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Dataverse unavailable"));

        (await RefusalAsync())!.Value.ReasonCode.Should().Be(FileAccessEndpoints.ShareLinkProtectionUnverifiableCode);
    }

    [Fact(DisplayName = "Round 74 V3: a child link + a derivation that cannot be decided is refused share_link_protection_unverifiable")]
    public async Task ChildLinkWithAnUndecidedDerivation_IsUnverifiable()
    {
        Row("sprk_document", DocumentId, Document(("sprk_relatedevent", "sprk_event", EventId)));
        Row("sprk_event", EventId, new Entity("sprk_event", EventId)); // filed under nothing: NoAncestor — a child link
        // The derivation's own read of the document names the event, which the derivation cannot resolve -> undecided.
        _world.Rows[("sprk_document", DocumentId)] = Document(("sprk_relatedevent", "sprk_event", EventId));

        (await RefusalAsync())!.Value.ReasonCode.Should().Be(FileAccessEndpoints.ShareLinkProtectionUnverifiableCode,
            "with a child link, an undecided derivation may hide a secure ancestor — fail closed");
    }

    [Fact(DisplayName = "Round 74 V3: a document of a STANDARD project (directly and through an event) is not refused")]
    public async Task StandardRecords_AreNotRefused()
    {
        Row("sprk_document", DocumentId, Document(
            ("sprk_project", "sprk_project", ProjectId), ("sprk_relatedevent", "sprk_event", EventId)));
        Row("sprk_event", EventId, new Entity("sprk_event", EventId) { ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId) });
        // The derivation's view of the same records: a plain project whose business unit stamps a container.
        var unit = Guid.Parse("17140000-0000-4000-8000-0000000000b0");
        _world.BusinessUnits[unit] = (TestRecordContainerResolver.PointerWorldRootBusinessUnit, "b!standard-unit-container");
        _world.Rows[("sprk_project", ProjectId)] = new Entity("sprk_project", ProjectId)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", unit),
        };
        _world.Rows[("sprk_event", EventId)] = new Entity("sprk_event", EventId)
        {
            ["sprk_regardingproject"] = new EntityReference("sprk_project", ProjectId),
            ["owningbusinessunit"] = new EntityReference("businessunit", unit),
        };
        _world.Rows[("sprk_document", DocumentId)] = Document(
            ("sprk_project", "sprk_project", ProjectId), ("sprk_relatedevent", "sprk_event", EventId));

        (await RefusalAsync()).Should().BeNull();
        _flags.Reads.Should().Contain(("sprk_project", ProjectId));
    }
}
