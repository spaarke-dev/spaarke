using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai.PublicContracts;

/// <summary>
/// Seam tests for the <see cref="IActionSeam"/> facade (task 031 / FR-07, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// PROOF of the task's core goal: each seam method is invokable and produces the correct Dataverse
/// write with <b>NO</b> <c>NodeExecutionContext</c>, <b>NO</b> playbook run, and <b>NO</b> chat session
/// constructed anywhere in this file (note the usings — none reference <c>Services.Ai.Nodes</c>,
/// <c>NodeExecutionContext</c>, <c>PlaybookNodeDto</c>, or any chat/playbook type). Only the
/// Dataverse-boundary services are doubled, exactly as the node-executor seam tests do.
/// </para>
/// <para>
/// Because the facade delegates to the SAME cores the executors call, the writes asserted here match
/// the writes task 030's characterization tests pin for the executors — that is the "byte-for-byte
/// parity" claim, demonstrated from the non-playbook entry point.
/// </para>
/// </remarks>
public class ActionSeamTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly Mock<IFieldMappingDataverseService> _fieldMappingMock;
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;

    private static readonly JsonSerializerOptions MetadataCacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Guid RecipientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CreatedNotificationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CorrelationGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public ActionSeamTests()
    {
        _entityServiceMock = new Mock<IGenericEntityService>();
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatedNotificationId);
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        _fieldMappingMock = new Mock<IFieldMappingDataverseService>();
        _fieldMappingMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Returns(Task.CompletedTask);

        _scopeFactoryMock = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
    }

    private IActionSeam CreateSeam() => new ActionSeam(
        _entityServiceMock.Object,
        _fieldMappingMock.Object,
        _scopeFactoryMock.Object,
        NullLogger<ActionSeam>.Instance);

    // ── CreateNotification: parity + negative case ────────────────────────────────────────────

    [Fact]
    public async Task CreateNotificationAsync_WithValidRequest_WritesSameAppNotificationFieldSetAsExecutor()
    {
        // Arrange — no NodeExecutionContext anywhere; the caller supplies plain values.
        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(CreatedNotificationId);

        var request = new CreateNotificationRequest
        {
            Title = "New document uploaded",
            Body = "A document was uploaded to your matter.",
            Category = "document-upload",
            RecipientId = RecipientId,
            Source = "playbook",
            CorrelationId = CorrelationGuid.ToString()
        };

        // Act
        var result = await CreateSeam().CreateNotificationAsync(request, CancellationToken.None);

        // Assert — result
        result.Success.Should().BeTrue();
        result.Skipped.Should().BeFalse();
        result.NotificationId.Should().Be(CreatedNotificationId);
        result.Error.Should().BeNull();

        // Assert — SAME field set the CreateNotification node executor writes today (parity with the
        // task-030 characterization test), reached without a playbook run.
        captured.Should().NotBeNull();
        captured!.LogicalName.Should().Be("appnotification");
        captured.Attributes.Keys.Should().BeEquivalentTo(new[]
        {
            "title", "body", "priority", "toasttype", "ownerid",
            "ttlinseconds", "sprk_category", "sprk_source", "sprk_playbookrunid"
        });
        captured.GetAttributeValue<OptionSetValue>("priority").Value.Should().Be(200_000_000);
        captured.GetAttributeValue<OptionSetValue>("toasttype").Value.Should().Be(200_000_000);
        captured.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(RecipientId);
        captured.GetAttributeValue<int>("ttlinseconds").Should().Be(604800);
        captured.GetAttributeValue<string>("sprk_source").Should().Be("playbook");
        captured.GetAttributeValue<string>("sprk_playbookrunid").Should().Be(CorrelationGuid.ToString());
    }

    [Fact]
    public async Task CreateNotificationAsync_WithNonPlaybookSource_WritesCallerSuppliedSourceValue()
    {
        // Proves the formerly playbook-baked field is now caller-controlled (Phase 4/5 need this).
        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(CreatedNotificationId);

        var request = new CreateNotificationRequest
        {
            Title = "Communication arrived",
            Body = "A new message arrived.",
            RecipientId = RecipientId,
            Source = "communication-arrived",
            CorrelationId = "corr-42"
        };

        var result = await CreateSeam().CreateNotificationAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        captured!.GetAttributeValue<string>("sprk_source").Should().Be("communication-arrived");
        captured.GetAttributeValue<string>("sprk_playbookrunid").Should().Be("corr-42");
    }

    [Fact]
    public async Task CreateNotificationAsync_WithNoRecipient_ReturnsTypedFailureAndNeverCreates()
    {
        // Negative/authorization case (criterion 7): no recipient → typed failure, not a throw/no-op.
        var request = new CreateNotificationRequest
        {
            Title = "Orphan",
            Body = "Nobody",
            RecipientId = null
        };

        var result = await CreateSeam().CreateNotificationAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("recipientId is required");
        result.NotificationId.Should().BeNull();
        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateNotificationAsync_WithDuplicateUnread_SkipsCreate(
        )
    {
        // Idempotency parity — reached without a playbook run.
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("appnotification") { Id = Guid.NewGuid() } }));

        var request = new CreateNotificationRequest
        {
            Title = "Dup",
            Body = "Body",
            Category = "document-upload",
            RecipientId = RecipientId,
            RegardingId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            RegardingType = "sprk_document"
        };

        var result = await CreateSeam().CreateNotificationAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Skipped.Should().BeTrue();
        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── CreateTask: parity + degraded success ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateTaskAsync_WithValidRequest_WritesSameTaskFieldSetAsExecutor()
    {
        var regardingId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var ownerId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var createdTaskId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(createdTaskId);

        var request = new CreateTaskRequest
        {
            Subject = "Review contract",
            Description = "Please review.",
            DueDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            RegardingObjectId = regardingId,
            RegardingObjectType = "sprk_document",
            OwnerId = ownerId
        };

        var result = await CreateSeam().CreateTaskAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TaskId.Should().Be(createdTaskId);
        captured.Should().NotBeNull();
        captured!.LogicalName.Should().Be("task");
        captured.Attributes.Keys.Should().BeEquivalentTo(new[]
        {
            "subject", "description", "scheduledend", "regardingobjectid", "ownerid"
        });
        captured.GetAttributeValue<EntityReference>("regardingobjectid").Id.Should().Be(regardingId);
        captured.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(ownerId);
    }

    [Fact]
    public async Task CreateTaskAsync_WhenCreateThrows_ReturnsDegradedSuccessWithEmptyTaskId()
    {
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse rejected"));

        var result = await CreateSeam().CreateTaskAsync(
            new CreateTaskRequest { Subject = "X" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TaskId.Should().Be(Guid.Empty);
    }

    // ── UpdateRecord: coercion parity + fail-loud negative case ───────────────────────────────

    [Fact]
    public async Task UpdateRecordAsync_WithTypedMappings_CoercesSamePayloadAsExecutor()
    {
        var recordId = Guid.NewGuid();
        UseMetadataFor("sprk_document", BuildDocumentMetadata());
        Dictionary<string, object?>? captured = null;
        _fieldMappingMock
            .Setup(s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((_, _, fields, _, _) => captured = fields)
            .Returns(Task.CompletedTask);

        var request = new UpdateRecordRequest
        {
            EntityLogicalName = "sprk_document",
            RecordId = recordId,
            FieldMappings = new[]
            {
                new ActionFieldMapping("sprk_summary", ActionFieldType.String, "Free-form summary"),
                new ActionFieldMapping("sprk_status", ActionFieldType.Choice, "Complete",
                    new Dictionary<string, int> { ["pending"] = 100000000, ["complete"] = 100000002 }),
                new ActionFieldMapping("sprk_isconfidential", ActionFieldType.Boolean, "yes"),
                new ActionFieldMapping("sprk_partycount", ActionFieldType.Number, "42")
            }
        };

        var result = await CreateSeam().UpdateRecordAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!["sprk_summary"].Should().Be("Free-form summary");
        captured["sprk_status"].Should().Be(100000002);
        captured["sprk_isconfidential"].Should().Be(true);
        captured["sprk_partycount"].Should().Be(42);
    }

    [Fact]
    public async Task UpdateRecordAsync_WithInvalidChoiceLabel_ReturnsTypedFailureAndNeverPatches()
    {
        var recordId = Guid.NewGuid();
        UseMetadataFor("sprk_document", BuildDocumentMetadata());

        var request = new UpdateRecordRequest
        {
            EntityLogicalName = "sprk_document",
            RecordId = recordId,
            FieldMappings = new[]
            {
                new ActionFieldMapping("sprk_documenttype", ActionFieldType.String, "Bogus")
            }
        };

        var result = await CreateSeam().UpdateRecordAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Bogus");
        result.Error.Should().Contain("Contract");
        _fieldMappingMock.Verify(
            s => s.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()),
            Times.Never);
    }

    // ── Helpers (metadata cache-HIT for the Choice fail-loud path) ────────────────────────────

    private void UseMetadataFor(string entityLogicalName, EntityMetadataDto dto)
    {
        var cacheKey = $"sdap:dv:entitymetadata:{entityLogicalName.ToLowerInvariant()}";
        var cacheMock = new Mock<IDistributedCache>();
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto, MetadataCacheJsonOptions));
        cacheMock
            .Setup(c => c.GetAsync(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        var metadataService = new MetadataService(
            Mock.Of<IDataverseService>(),
            cacheMock.Object,
            NullLogger<MetadataService>.Instance);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock
            .Setup(sp => sp.GetService(typeof(MetadataService)))
            .Returns(metadataService);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(serviceProviderMock.Object);

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);
    }

    private static EntityMetadataDto BuildDocumentMetadata(string entity = "sprk_document") =>
        new(
            LogicalName: entity,
            PrimaryIdAttribute: $"{entity}id",
            PrimaryNameAttribute: "sprk_name",
            Attributes: new List<AttributeDto>
            {
                new(
                    LogicalName: "sprk_documenttype",
                    AttributeType: "Picklist",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: new OptionSetDto(new List<OptionDto>
                    {
                        new(100000000, "Contract", null),
                        new(100000001, "Invoice", null),
                        new(100000012, "Other", null)
                    })),
                new(
                    LogicalName: "sprk_summary",
                    AttributeType: "Memo",
                    Format: null,
                    IsPrimaryName: false,
                    IsPrimaryId: false,
                    OptionSet: null)
            });
}
