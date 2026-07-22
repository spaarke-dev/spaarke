using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai.Nodes;

/// <summary>
/// CHARACTERIZATION seam tests for <see cref="CreateNotificationNodeExecutor"/> (task 030, FR-07 pre-req).
/// </summary>
/// <remarks>
/// <para>
/// These pin the CURRENT, shipped behavior of the executor BEFORE the Layer-A extraction (task 031),
/// so the extraction is provably behavior-neutral: if these pass unmodified after 031, nothing
/// observable changed. This is Feathers' characterization pattern — pin what IS, not what should be.
/// </para>
/// <para>
/// Seam category (ADR-038 / tests/CLAUDE.md): the slice is production <see cref="NodeExecutionContext"/>
/// → executor <c>ExecuteAsync</c>/<c>Validate</c> → the <see cref="Entity"/> built and handed to the
/// Dataverse boundary. A REAL <see cref="TemplateEngine"/> renders the template fields (rendering is
/// part of the behavior being pinned); only the outermost Dataverse-boundary service
/// (<see cref="IGenericEntityService"/>) is a Moq double, matching the existing executor unit-test
/// convention. <see cref="CreateNotificationNodeExecutor"/> had ZERO prior coverage — this is its
/// first test suite (flagged in the task-030 POML background).
/// </para>
/// </remarks>
public class CreateNotificationNodeExecutorSeamTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock;
    private readonly CreateNotificationNodeExecutor _executor;

    // Fixed so the pinned entity fields are deterministic.
    private static readonly Guid RecipientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CreatedNotificationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RunId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RegardingId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public CreateNotificationNodeExecutorSeamTests()
    {
        _entityServiceMock = new Mock<IGenericEntityService>();

        // Default CreateAsync returns a fixed id so the pinned NodeOutput data is deterministic.
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreatedNotificationId);

        // Default idempotency query returns no rows (no duplicate) unless a test overrides it.
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        _executor = new CreateNotificationNodeExecutor(
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            _entityServiceMock.Object,
            NullLogger<CreateNotificationNodeExecutor>.Instance);
    }

    // ── Happy path — exact appnotification field set (criterion 1) ────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithValidConfig_BuildsExactAppNotificationEntityFieldSet()
    {
        // Arrange — {title, body, category, recipientId}; NO regarding/actionUrl/enrichment,
        // so `data`, sprk_regardingid, sprk_regardingtype are NOT set. Idempotency is skipped
        // (needs regardingId + category).
        var config = $$"""
        {
          "title": "New document uploaded",
          "body": "A document was uploaded to your matter.",
          "category": "document-upload",
          "recipientId": "{{RecipientId}}"
        }
        """;
        var context = CreateContext(config);
        Entity? captured = null;
        _entityServiceMock
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync(CreatedNotificationId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — NodeOutput shape
        result.Success.Should().BeTrue();
        result.TextContent.Should().Be("Notification created: New document uploaded");
        var data = result.GetData<NotificationCreatedData>();
        data.Should().NotBeNull();
        data!.Skipped.Should().BeFalse();
        data.NotificationId.Should().Be(CreatedNotificationId);
        data.Title.Should().Be("New document uploaded");
        data.RecipientId.Should().Be(RecipientId);
        data.Category.Should().Be("document-upload");

        // Assert — EXACT Dataverse field set written today
        captured.Should().NotBeNull();
        captured!.LogicalName.Should().Be("appnotification");
        captured.Attributes.Keys.Should().BeEquivalentTo(new[]
        {
            "title", "body", "priority", "toasttype", "ownerid",
            "ttlinseconds", "sprk_category", "sprk_source", "sprk_playbookrunid"
        });
        captured.GetAttributeValue<string>("title").Should().Be("New document uploaded");
        captured.GetAttributeValue<string>("body").Should().Be("A document was uploaded to your matter.");
        captured.GetAttributeValue<OptionSetValue>("priority").Value.Should().Be(200_000_000);
        captured.GetAttributeValue<OptionSetValue>("toasttype").Value.Should().Be(200_000_000);
        captured.GetAttributeValue<EntityReference>("ownerid").LogicalName.Should().Be("systemuser");
        captured.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(RecipientId);
        captured.GetAttributeValue<int>("ttlinseconds").Should().Be(604800);
        captured.GetAttributeValue<string>("sprk_category").Should().Be("document-upload");
        captured.GetAttributeValue<string>("sprk_source").Should().Be("playbook");
        captured.GetAttributeValue<string>("sprk_playbookrunid").Should().Be(RunId.ToString());
    }

    // ── Idempotency skip (criterion 2) ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithDuplicateUnreadNotification_SkipsCreateAndReturnsSkipped()
    {
        // Arrange — regardingId + category present ⇒ idempotency check runs; return an existing row.
        var config = $$"""
        {
          "title": "New document uploaded",
          "body": "Body",
          "category": "document-upload",
          "recipientId": "{{RecipientId}}",
          "regardingId": "{{RegardingId}}",
          "regardingType": "sprk_document"
        }
        """;
        var context = CreateContext(config);
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { new("appnotification") { Id = Guid.NewGuid() } }));

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — never creates; returns Ok with skipped=true and the exact reason string
        result.Success.Should().BeTrue();
        var data = result.GetData<NotificationSkippedData>();
        data.Should().NotBeNull();
        data!.Skipped.Should().BeTrue();
        data.Reason.Should().Be("Duplicate unread notification exists");
        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Iterate-items (criterion 3) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithIterateItems_CreatesOnePerResolvableItemAndCountsMatchInput()
    {
        // Arrange — 3 upstream items; item[2] has an unparseable recipient ⇒ skipped.
        // No category/regarding ⇒ no idempotency ⇒ the two resolvable items are created.
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var config = """
        {
          "title": "unused-top-level",
          "body": "unused-top-level",
          "iterateItems": true,
          "itemNotification": {
            "title": "Hello {{item.name}}",
            "body": "You have an update",
            "recipientId": "{{item.userId}}"
          }
        }
        """;
        var upstream = NodeOutput.Ok(
            Guid.NewGuid(),
            "queryNode",
            new
            {
                items = new object[]
                {
                    new { userId = user1.ToString(), name = "Alice" },
                    new { userId = user2.ToString(), name = "Bob" },
                    new { userId = "not-a-guid", name = "Carol" }
                }
            });
        var context = CreateContext(config) with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput> { ["queryNode"] = upstream }
        };

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — counts mirror the input array exactly
        result.Success.Should().BeTrue();
        var data = result.GetData<IterateData>();
        data.Should().NotBeNull();
        data!.Created.Should().Be(2);
        data.Skipped.Should().Be(1);
        data.TotalItems.Should().Be(3);
        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // ── Negative/authorization case — unresolvable recipient (criterion 4) ────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenNoRecipientResolvable_ReturnsValidationErrorAndNeverCreates()
    {
        // Arrange — no recipientId in config AND no run-context userId (UserId left null).
        var config = """
        {
          "title": "Orphan notification",
          "body": "Nobody to send to"
        }
        """;
        var context = CreateContext(config); // UserId is null by default

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — no notification is ever created for an unresolvable recipient
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Be("Cannot determine notification recipient: recipientId is required");
        _entityServiceMock.Verify(
            s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Validate() exact error messages (criterion 5) ─────────────────────────────────────────

    [Fact]
    public void Validate_WithNullConfig_ReturnsExactConfigRequiredMessage()
    {
        var result = _executor.Validate(CreateContext(null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Be("CreateNotification node requires configuration (ConfigJson)");
    }

    [Fact]
    public void Validate_WithMissingTitle_ReturnsTitleRequiredMessage()
    {
        var result = _executor.Validate(CreateContext(@"{""body"":""has body""}"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Notification title is required");
    }

    [Fact]
    public void Validate_WithMissingBody_ReturnsBodyRequiredMessage()
    {
        var result = _executor.Validate(CreateContext(@"{""title"":""has title""}"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Notification body is required");
    }

    [Fact]
    public void Validate_WithMalformedJson_ReturnsInvalidJsonMessage()
    {
        var result = _executor.Validate(CreateContext("{not valid json"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid notification configuration JSON"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static NodeExecutionContext CreateContext(string? configJson)
    {
        var actionId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = RunId,
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = Guid.NewGuid(),
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Create Notification Node",
                ExecutionOrder = 1,
                OutputVariable = "notificationResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction { Id = actionId, Name = "Create Notification" },
            ExecutorType = ExecutorType.CreateNotification,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private sealed class NotificationCreatedData
    {
        public Guid NotificationId { get; set; }
        public string Title { get; set; } = string.Empty;
        public Guid RecipientId { get; set; }
        public string? Category { get; set; }
        public bool Skipped { get; set; }
    }

    private sealed class NotificationSkippedData
    {
        public bool Skipped { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class IterateData
    {
        public int Created { get; set; }
        public int Skipped { get; set; }
        public int TotalItems { get; set; }
    }
}
