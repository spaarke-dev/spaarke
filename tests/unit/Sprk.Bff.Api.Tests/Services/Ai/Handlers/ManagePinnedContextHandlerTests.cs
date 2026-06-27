using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Models.Memory;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Sprk.Bff.Api.Services.Ai.Memory;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Unit tests for <see cref="ManagePinnedContextHandler"/> (R6 Pillar 7 / task 069 / FR-47).
/// </summary>
/// <remarks>
/// Coverage:
/// - "remember X" voice command → create with pinType=user-preference.
/// - "always X" voice command → create with pinType=system-rule.
/// - "forget X" voice command → delete by (pinType, title) case-insensitive match.
/// - Forget with no matching pin → refused_not_found (success=true, structured response).
/// - Missing UserId → refused (chat session not authenticated).
/// - Validation: closed enums, length caps.
/// - ADR-015: telemetry asserts pin title/content body NEVER appear in log output.
/// </remarks>
public sealed class ManagePinnedContextHandlerTests : TypedToolHandlerTestFixture
{
    private static readonly DateTimeOffset DeterministicNow = new(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPinnedContextRepository> _pinnedContextRepository = new();
    private readonly FakeTimeProvider _timeProvider = new(DeterministicNow);

    private const string DefaultUserId = "11111111-2222-3333-4444-555555555555";

    private ManagePinnedContextHandler CreateHandler() => new(
        _pinnedContextRepository.Object,
        _timeProvider,
        CreateLogger<ManagePinnedContextHandler>());

    private static AnalysisTool BuildManageTool() =>
        BuildAnalysisTool(handlerClass: nameof(ManagePinnedContextHandler), toolType: ToolType.Custom);

    private static ChatInvocationContext BuildContext(
        string toolArgumentsJson,
        string? userId = DefaultUserId,
        string? tenantId = null)
    {
        return new ChatInvocationContext
        {
            ChatSessionId = Guid.NewGuid(),
            TenantId = tenantId ?? DefaultTenantId,
            DecisionId = Guid.NewGuid(),
            ToolArgumentsJson = toolArgumentsJson,
            UserId = userId
        };
    }

    private static string BuildArgsJson(string action, string pinType, string title, string? content = null)
    {
        var contentFragment = content is null
            ? ""
            : $",\"content\":\"{content}\"";
        return $$"""
                 {
                   "action": "{{action}}",
                   "pinType": "{{pinType}}",
                   "title": "{{title}}"
                   {{contentFragment}}
                 }
                 """;
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // "remember X" — create with pinType = user-preference
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Creates_UserPreference_PinOn_RememberCommand()
    {
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionCreate,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: "Terse responses",
            content: "Always respond in short, direct sentences without bullet lists.");
        var ctx = BuildContext(argsJson);

        PinnedContextItem? captured = null;
        _pinnedContextRepository
            .Setup(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
            .Callback<PinnedContextItem, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PinType.Should().Be(PinType.UserPreference);
        captured.Title.Should().Be("Terse responses");
        captured.Content.Should().Be("Always respond in short, direct sentences without bullet lists.");
        captured.TenantId.Should().Be(DefaultTenantId);
        captured.UserId.Should().Be(DefaultUserId);
        captured.CreatedBy.Should().Be(DefaultUserId);
        captured.MatterId.Should().BeNull(because: "voice-command pins are not matter-bound");
        captured.DocumentType.Should().Be("pinned-context");
        captured.Id.Should().StartWith($"pinned-context_{DefaultTenantId}_");

        var payload = result.GetData<ManagePinnedContextHandler.ManagePinnedContextPayload>();
        payload!.Status.Should().Be(ManagePinnedContextHandler.StatusCreated);
        payload.Action.Should().Be(ManagePinnedContextHandler.ActionCreate);
        payload.PinType.Should().Be(ManagePinnedContextHandler.PinTypeUserPreference);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // "always X" — create with pinType = system-rule
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Creates_SystemRule_PinOn_AlwaysCommand()
    {
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionCreate,
            pinType: ManagePinnedContextHandler.PinTypeSystemRule,
            title: "Never reveal cost data");
        var ctx = BuildContext(argsJson);

        PinnedContextItem? captured = null;
        _pinnedContextRepository
            .Setup(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
            .Callback<PinnedContextItem, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.PinType.Should().Be(PinType.SystemRule);
        captured.Title.Should().Be("Never reveal cost data");
        captured.Content.Should().Be("Never reveal cost data",
            because: "when content is omitted on create, the handler defaults it to the title text");

        var payload = result.GetData<ManagePinnedContextHandler.ManagePinnedContextPayload>();
        payload!.Status.Should().Be(ManagePinnedContextHandler.StatusCreated);
        payload.PinType.Should().Be(ManagePinnedContextHandler.PinTypeSystemRule);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // "forget X" — delete by (pinType, title) match
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Deletes_ExistingPin_OnForgetCommand_WhenTitleMatches()
    {
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionDelete,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: "Terse responses");
        var ctx = BuildContext(argsJson);

        var existingPin = new PinnedContextItem
        {
            Id = $"pinned-context_{DefaultTenantId}_abc123",
            DocumentType = "pinned-context",
            TenantId = DefaultTenantId,
            UserId = DefaultUserId,
            PinType = PinType.UserPreference,
            Title = "Terse responses",
            Content = "Always respond tersely.",
            MatterId = null,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CreatedBy = DefaultUserId
        };

        _pinnedContextRepository
            .Setup(r => r.GetByUserAsync(DefaultTenantId, DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PinnedContextItem> { existingPin });

        string? capturedPinId = null;
        _pinnedContextRepository
            .Setup(r => r.DeleteAsync(DefaultTenantId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, pinId, _) => capturedPinId = pinId)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedPinId.Should().Be("abc123",
            because: "the handler must extract the pinId portion from the existing doc id");

        var payload = result.GetData<ManagePinnedContextHandler.ManagePinnedContextPayload>();
        payload!.Status.Should().Be(ManagePinnedContextHandler.StatusDeleted);
        payload.Action.Should().Be(ManagePinnedContextHandler.ActionDelete);
        payload.PinId.Should().Be("abc123");
    }

    [Fact]
    public async Task ExecuteChatAsync_TitleMatchIs_CaseInsensitive_OnForgetCommand()
    {
        // Spec contract: "forget X" matches existing pins by title case-insensitively so the
        // LLM does not need to echo back the EXACT casing the user used at create time.
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionDelete,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: "TERSE RESPONSES");
        var ctx = BuildContext(argsJson);

        var existingPin = new PinnedContextItem
        {
            Id = $"pinned-context_{DefaultTenantId}_xyz789",
            DocumentType = "pinned-context",
            TenantId = DefaultTenantId,
            UserId = DefaultUserId,
            PinType = PinType.UserPreference,
            Title = "terse responses",
            Content = "be terse",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = DefaultUserId
        };

        _pinnedContextRepository
            .Setup(r => r.GetByUserAsync(DefaultTenantId, DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PinnedContextItem> { existingPin });

        _pinnedContextRepository
            .Setup(r => r.DeleteAsync(DefaultTenantId, "xyz789", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<ManagePinnedContextHandler.ManagePinnedContextPayload>();
        payload!.Status.Should().Be(ManagePinnedContextHandler.StatusDeleted);
        payload.PinId.Should().Be("xyz789");
    }

    [Fact]
    public async Task ExecuteChatAsync_ReturnsRefusedNotFound_OnForgetCommand_WhenNoTitleMatch()
    {
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionDelete,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: "Nonexistent label");
        var ctx = BuildContext(argsJson);

        _pinnedContextRepository
            .Setup(r => r.GetByUserAsync(DefaultTenantId, DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeTrue(because: "refused_not_found is a re-actable response, not an error");
        var payload = result.GetData<ManagePinnedContextHandler.ManagePinnedContextPayload>();
        payload!.Status.Should().Be(ManagePinnedContextHandler.StatusRefusedNotFound);
        payload.PinId.Should().BeNull();

        _pinnedContextRepository.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            failMessage: "no delete is issued when no match is found");
    }

    [Fact]
    public async Task ExecuteChatAsync_DoesNotDelete_WhenPinTypeMismatches()
    {
        // The user authored a system-rule pin "Be terse"; the LLM tries to delete a
        // user-preference pin titled "Be terse". The handler must NOT delete the wrong-type pin.
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionDelete,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: "Be terse");
        var ctx = BuildContext(argsJson);

        var existingPin = new PinnedContextItem
        {
            Id = $"pinned-context_{DefaultTenantId}_aaa",
            DocumentType = "pinned-context",
            TenantId = DefaultTenantId,
            UserId = DefaultUserId,
            PinType = PinType.SystemRule,                  // <-- system-rule, not user-preference
            Title = "Be terse",
            Content = "be terse",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedBy = DefaultUserId
        };

        _pinnedContextRepository
            .Setup(r => r.GetByUserAsync(DefaultTenantId, DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PinnedContextItem> { existingPin });

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeTrue();
        var payload = result.GetData<ManagePinnedContextHandler.ManagePinnedContextPayload>();
        payload!.Status.Should().Be(ManagePinnedContextHandler.StatusRefusedNotFound,
            because: "title match must be scoped to pinType — a system-rule pin must not be deleted by a user-preference delete request");

        _pinnedContextRepository.Verify(
            r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Authentication gate
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Refuses_WhenUserIdIsMissing()
    {
        // Standalone chat without OBO produces a context with UserId=null. The handler refuses
        // (pinned context is user-scoped — there is no tenant-global pin tier).
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionCreate,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: "Some pin");
        var ctx = BuildContext(argsJson, userId: null);

        var handler = CreateHandler();
        var result = await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ToolErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("UserId");

        _pinnedContextRepository.Verify(
            r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Validation: closed enums + length caps
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateChat_Fails_WhenActionIsInvalid()
    {
        const string argsJson = """{"action":"upsert","pinType":"user-preference","title":"x"}""";
        var ctx = BuildContext(argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildManageTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("action", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenPinTypeIsInvalid()
    {
        const string argsJson = """{"action":"create","pinType":"workspace-fact","title":"x"}""";
        var ctx = BuildContext(argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildManageTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("pinType", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTitleIsMissing()
    {
        const string argsJson = """{"action":"create","pinType":"user-preference"}""";
        var ctx = BuildContext(argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildManageTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateChat_Fails_WhenTitleExceedsLengthCap()
    {
        var oversizedTitle = new string('a', ManagePinnedContextHandler.MaxTitleLength + 1);
        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionCreate,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: oversizedTitle);
        var ctx = BuildContext(argsJson);

        var handler = CreateHandler();
        var result = handler.ValidateChat(ctx, BuildManageTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title", StringComparison.OrdinalIgnoreCase) && e.Contains("exceeds"));
    }

    [Fact]
    public void Validate_Fails_OnPlaybookContext()
    {
        // The handler is chat-only; the playbook-path Validate must reject.
        var ctx = BuildToolExecutionContext();
        var handler = CreateHandler();

        var result = handler.Validate(ctx, BuildManageTool());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("chat-context-only", StringComparison.OrdinalIgnoreCase));
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // ADR-015: title/content body never leaks to telemetry
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteChatAsync_Telemetry_Adheres_ToAdr015_OnCreate()
    {
        const string sensitiveTitle = "User secret authoring instructions x9z2pq8w7v3";
        const string sensitiveContent = "Confidential pin body abracadabra hocus pocus 0123";

        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionCreate,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: sensitiveTitle,
            content: sensitiveContent);
        var ctx = BuildContext(argsJson);

        _pinnedContextRepository
            .Setup(r => r.CreateAsync(It.IsAny<PinnedContextItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        // ADR-015 BINDING: the title body and content body must NEVER appear in any captured
        // log message. The handler is allowed to log title LENGTH and content PRESENCE only.
        AssertTelemetryRespectsAdr015(sensitiveTitle, sensitiveContent);
    }

    [Fact]
    public async Task ExecuteChatAsync_Telemetry_Adheres_ToAdr015_OnDelete()
    {
        const string sensitiveTitle = "User secret pin label x9z2pq8w7v3 to forget";

        var argsJson = BuildArgsJson(
            action: ManagePinnedContextHandler.ActionDelete,
            pinType: ManagePinnedContextHandler.PinTypeUserPreference,
            title: sensitiveTitle);
        var ctx = BuildContext(argsJson);

        _pinnedContextRepository
            .Setup(r => r.GetByUserAsync(DefaultTenantId, DefaultUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PinnedContextItem>());

        var handler = CreateHandler();
        await handler.ExecuteChatAsync(ctx, BuildManageTool(), CancellationToken.None);

        // ADR-015 BINDING: even on the refused_not_found path, the user-supplied title must
        // NEVER appear in telemetry (the response message contains the title because it flows
        // back to the user via the LLM, but that's the wire-response surface, not telemetry).
        AssertTelemetryRespectsAdr015(sensitiveTitle);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Verifies the pinId extraction helper handles the canonical doc id format.</summary>
    [Fact]
    public void ExtractPinIdFromDocumentId_ReturnsPinIdPortion()
    {
        var pinId = ManagePinnedContextHandler.ExtractPinIdFromDocumentId(
            $"pinned-context_{DefaultTenantId}_abc123def456",
            DefaultTenantId);

        pinId.Should().Be("abc123def456");
    }

    /// <summary>Defensive fallback path — last-underscore split when prefix mismatches.</summary>
    [Fact]
    public void ExtractPinIdFromDocumentId_FallsBackToLastUnderscore_WhenPrefixMismatches()
    {
        var pinId = ManagePinnedContextHandler.ExtractPinIdFromDocumentId(
            "some_legacy_format_xyz",
            DefaultTenantId);

        pinId.Should().Be("xyz");
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FakeTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
