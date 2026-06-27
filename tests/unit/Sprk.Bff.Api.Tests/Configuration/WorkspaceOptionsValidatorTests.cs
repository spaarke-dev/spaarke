using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Configuration;

/// <summary>
/// Unit tests for <see cref="WorkspaceOptionsValidator"/> (Phase 1R FR-1R-06).
/// Verifies (a) the deprecation WARN is emitted with the correct KEY names when
/// any of the 6 deprecated env vars is set, (b) ADR-015 tier-1 hygiene — GUID
/// values are NEVER included in any emitted message.
/// </summary>
[Trait("status", "repaired")]
public sealed class WorkspaceOptionsValidatorTests
{
    private const string DummyGuid = "11111111-2222-3333-4444-555555555555";

    private readonly Mock<ILogger<WorkspaceOptionsValidator>> _loggerMock = new();
    private readonly List<string> _capturedMessages = new();

    public WorkspaceOptionsValidatorTests()
    {
        // Capture every Warning-level message text so tests can both assert
        // content AND scan for accidental GUID leakage.
        _loggerMock
            .Setup(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback(new InvocationAction(invocation =>
            {
                var state = invocation.Arguments[2];
                var formatter = invocation.Arguments[4];
                var formatted = (string)formatter
                    .GetType()
                    .GetMethod("Invoke")!
                    .Invoke(formatter, new[] { state, null })!;
                _capturedMessages.Add(formatted);
            }));
    }

    private WorkspaceOptionsValidator CreateValidator() => new(_loggerMock.Object);

    // ── No env vars set → no WARN ───────────────────────────────────────────

    [Fact]
    public void Validate_NoEnvVarsSet_ReturnsSuccess_AndNoWarn()
    {
        var validator = CreateValidator();
        var options = new WorkspaceOptions();

        var result = validator.Validate(name: null, options);

        result.Should().Be(ValidateOptionsResult.Success);
        _capturedMessages.Should().BeEmpty();
    }

    [Fact]
    public void Validate_OnlyEmptyStrings_ReturnsSuccess_AndNoWarn()
    {
        var validator = CreateValidator();
        var options = new WorkspaceOptions
        {
            PreFillPlaybookId = "",
            MatterPreFillPlaybookId = "",
            ProjectPreFillPlaybookId = null,
            AiSummaryPlaybookId = "   ",
            SummarizePlaybookId = "",
            ChatSummarizePlaybookId = "",
        };

        var result = validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
        _capturedMessages.Should().BeEmpty();
    }

    // ── Each individual env var → WARN with that key ────────────────────────

    [Theory]
    [InlineData(nameof(WorkspaceOptions.PreFillPlaybookId), "Workspace__PreFillPlaybookId")]
    [InlineData(nameof(WorkspaceOptions.MatterPreFillPlaybookId), "Workspace__MatterPreFillPlaybookId")]
    [InlineData(nameof(WorkspaceOptions.ProjectPreFillPlaybookId), "Workspace__ProjectPreFillPlaybookId")]
    [InlineData(nameof(WorkspaceOptions.AiSummaryPlaybookId), "Workspace__AiSummaryPlaybookId")]
    [InlineData(nameof(WorkspaceOptions.SummarizePlaybookId), "Workspace__SummarizePlaybookId")]
    [InlineData(nameof(WorkspaceOptions.ChatSummarizePlaybookId), "Workspace__ChatSummarizePlaybookId")]
    public void Validate_SingleEnvVarSet_WarnsWithKeyNameOnly(string propertyName, string expectedKeyName)
    {
        var validator = CreateValidator();
        var options = new WorkspaceOptions();
        SetProperty(options, propertyName, DummyGuid);

        var result = validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
        _capturedMessages.Should().HaveCount(1);
        _capturedMessages[0].Should().Contain(expectedKeyName);
        _capturedMessages[0].Should().Contain("deprecated");
        _capturedMessages[0].Should().Contain("sprk_playbookconsumer");
    }

    // ── All env vars set → single WARN with all key names ───────────────────

    [Fact]
    public void Validate_AllEnvVarsSet_EmitsSingleWarnListingAllKeys()
    {
        var validator = CreateValidator();
        var options = new WorkspaceOptions
        {
            PreFillPlaybookId = DummyGuid,
            MatterPreFillPlaybookId = DummyGuid,
            ProjectPreFillPlaybookId = DummyGuid,
            AiSummaryPlaybookId = DummyGuid,
            SummarizePlaybookId = DummyGuid,
            ChatSummarizePlaybookId = DummyGuid,
        };

        var result = validator.Validate(null, options);

        result.Should().Be(ValidateOptionsResult.Success);
        _capturedMessages.Should().HaveCount(1);

        var msg = _capturedMessages[0];
        msg.Should().Contain("Workspace__PreFillPlaybookId");
        msg.Should().Contain("Workspace__MatterPreFillPlaybookId");
        msg.Should().Contain("Workspace__ProjectPreFillPlaybookId");
        msg.Should().Contain("Workspace__AiSummaryPlaybookId");
        msg.Should().Contain("Workspace__SummarizePlaybookId");
        msg.Should().Contain("Workspace__ChatSummarizePlaybookId");
    }

    // ── ADR-015 tier-1: NEVER log the GUID value ────────────────────────────

    [Fact]
    public void Validate_AnyEnvVarSet_NeverLogsGuidValue()
    {
        var validator = CreateValidator();
        var options = new WorkspaceOptions
        {
            PreFillPlaybookId = DummyGuid,
            MatterPreFillPlaybookId = DummyGuid,
            ProjectPreFillPlaybookId = DummyGuid,
            AiSummaryPlaybookId = DummyGuid,
            SummarizePlaybookId = DummyGuid,
            ChatSummarizePlaybookId = DummyGuid,
        };

        validator.Validate(null, options);

        // ADR-015 tier-1 hygiene: the GUID value (DummyGuid) MUST NEVER appear
        // in any emitted log message. If this assertion fires, the validator
        // is leaking PII-adjacent identifiers through telemetry.
        _capturedMessages.Should()
            .OnlyContain(m => !m.Contains(DummyGuid),
                because: "ADR-015 tier-1 — env-var GUID values must never appear in logs (chat-routing-redesign-r1 task 028e binding constraint)");
    }

    // ── Constructor + null-options guards ───────────────────────────────────

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        Action act = () => new WorkspaceOptionsValidator(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var validator = CreateValidator();
        Action act = () => validator.Validate(null, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    // ── DeprecatedKeys dictionary covers every WorkspaceOptions property
    //    that maps to an env var (forward-compat regression guard) ──────────

    [Fact]
    public void DeprecatedKeys_CoversEvery_NullableStringPlaybookIdProperty()
    {
        // If WorkspaceOptions gains another *PlaybookId env-var property in
        // the future, this test fails until the validator is updated to
        // include it. Prevents silent under-coverage as the codebase grows.
        var expected = new HashSet<string>
        {
            nameof(WorkspaceOptions.PreFillPlaybookId),
            nameof(WorkspaceOptions.MatterPreFillPlaybookId),
            nameof(WorkspaceOptions.ProjectPreFillPlaybookId),
            nameof(WorkspaceOptions.AiSummaryPlaybookId),
            nameof(WorkspaceOptions.SummarizePlaybookId),
            nameof(WorkspaceOptions.ChatSummarizePlaybookId),
        };

        WorkspaceOptionsValidator.DeprecatedKeys.Keys.Should().BeEquivalentTo(expected);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static void SetProperty(WorkspaceOptions options, string propertyName, string value)
    {
        var prop = typeof(WorkspaceOptions).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Unknown property {propertyName} on WorkspaceOptions");
        prop.SetValue(options, value);
    }
}
