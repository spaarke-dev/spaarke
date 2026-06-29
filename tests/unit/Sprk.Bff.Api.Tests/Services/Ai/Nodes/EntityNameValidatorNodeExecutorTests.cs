// R4 spaarke-daily-update-service-r4 — Tests for EntityNameValidatorNodeExecutor (task 003)
//
// Covers spec AC-3b ("Johnson & Lee LLP" scrubbed AND hallucination_detected event emitted) +
// the 8 mandatory test cases listed in the task prompt + node-executor-authoring.md.
//
// AAA pattern + Moq + FluentAssertions — mirrors LookupUserMembershipNodeExecutorTests.

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="EntityNameValidatorNodeExecutor"/>.
/// Validates config parsing, sentence-level scrubbing, allow-list pass-through,
/// case-insensitive matching, and the structured <c>hallucination_detected</c> log
/// event emitted per removed term (spec AC-3b).
/// </summary>
public class EntityNameValidatorNodeExecutorTests
{
    private readonly Mock<ILogger<EntityNameValidatorNodeExecutor>> _loggerMock;
    private readonly EntityNameValidatorNodeExecutor _executor;

    public EntityNameValidatorNodeExecutorTests()
    {
        _loggerMock = new Mock<ILogger<EntityNameValidatorNodeExecutor>>();
        _executor = new EntityNameValidatorNodeExecutor(_loggerMock.Object);
    }

    #region SupportedExecutorTypes

    [Fact]
    public void SupportedActionTypes_ContainsEntityNameValidator()
    {
        _executor.SupportedExecutorTypes.Should().Contain(ExecutorType.EntityNameValidator);
        _executor.SupportedExecutorTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate

    [Fact]
    public void Validate_MissingCandidateText_ReturnsError()
    {
        // Arrange — config has allowList but no candidateText
        var context = CreateContext(
            configJson: """{"allowList":["ACME Corp"]}""",
            outputVariable: "scrubResult");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("candidateText"));
    }

    [Fact]
    public void Validate_MissingAllowList_ReturnsError()
    {
        // Arrange — null allowList is INVALID (must be at least [] explicitly)
        var context = CreateContext(
            configJson: """{"candidateText":"ACME Corp filed a brief."}""",
            outputVariable: "scrubResult");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("allowList"));
    }

    [Fact]
    public void Validate_EmptyAllowList_IsValid()
    {
        // Arrange — empty allowList ([]) is acceptable: means "scrub all proper-noun sentences"
        var context = CreateContext(
            configJson: """{"candidateText":"ACME Corp filed a brief.","allowList":[]}""",
            outputVariable: "scrubResult");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingOutputVariable_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: """{"candidateText":"ACME Corp filed a brief.","allowList":["ACME Corp"]}""",
            outputVariable: null);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("OutputVariable"));
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsError()
    {
        // Arrange
        var context = CreateContext(
            configJson: "{not-json}",
            outputVariable: "scrubResult");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid"));
    }

    #endregion

    #region ExecuteAsync — allow-list pass-through

    [Fact]
    public async Task ExecuteAsync_AllowListPassThrough_NoChanges()
    {
        // Arrange — all named entities are in the allow-list, so nothing is scrubbed.
        var candidate = "ACME Corp received an engagement letter. ACME Corp will respond by Friday.";
        var context = CreateContext(
            configJson: $$"""{"candidateText":"{{candidate}}","allowList":["ACME Corp"]}""",
            outputVariable: "scrubResult");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData.Should().NotBeNull();
        var data = result.StructuredData!.Value;
        data.GetProperty("scrubbedText").GetString().Should().Be(candidate);
        data.GetProperty("removedTerms").GetArrayLength().Should().Be(0);

        // No hallucination warning emitted on the pass-through case.
        VerifyHallucinationLogCount(Times.Never());
    }

    #endregion

    #region ExecuteAsync — Spec AC-3b core scrub case

    [Fact]
    public async Task ExecuteAsync_ScrubsHallucination_Johnson_Lee_LLP()
    {
        // Spec AC-3b: input with "Johnson & Lee LLP" (NOT in allow-list) is scrubbed AND
        // a hallucination_detected warning event is emitted.
        // Arrange
        var candidate = "ACME Corp received an engagement letter from Johnson & Lee LLP yesterday. ACME Corp filed a response.";
        var context = CreateContext(
            configJson: $$"""{"candidateText":"{{candidate}}","allowList":["ACME Corp"]}""",
            outputVariable: "scrubResult");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData.Should().NotBeNull();
        var data = result.StructuredData!.Value;

        var scrubbed = data.GetProperty("scrubbedText").GetString()!;
        scrubbed.Should().NotContain("Johnson & Lee LLP",
            "the hallucinated firm name MUST be removed (spec AC-3b)");
        scrubbed.Should().Contain("ACME Corp filed a response",
            "allow-listed-entity-bearing sentences MUST be preserved");

        var removedArr = data.GetProperty("removedTerms");
        removedArr.GetArrayLength().Should().BeGreaterThan(0,
            "at least one removed term MUST be recorded");
        var removed = removedArr.EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        removed.Should().Contain(t => t!.Contains("Johnson & Lee LLP"),
            "the offending term MUST be reported in removedTerms");

        // Spec AC-3b: hallucination_detected event MUST be emitted.
        VerifyHallucinationLogCount(Times.AtLeastOnce());
    }

    #endregion

    #region ExecuteAsync — per-removal log assertion

    [Fact]
    public async Task ExecuteAsync_EmitsHallucinationEventPerRemoval()
    {
        // Two hallucinated entities in distinct sentences → two log events.
        // Arrange
        var candidate = "ACME Corp filed a brief. Johnson & Lee LLP responded promptly. Davis v. Metro Transit was cited as precedent.";
        var context = CreateContext(
            configJson: $$"""{"candidateText":"{{candidate}}","allowList":["ACME Corp"]}""",
            outputVariable: "scrubResult");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var removedTerms = result.StructuredData!.Value
            .GetProperty("removedTerms")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        removedTerms.Should().HaveCountGreaterThanOrEqualTo(2,
            "two hallucinated entities should each be recorded");

        // Exactly one warning event per recorded removed term.
        VerifyHallucinationLogCount(Times.Exactly(removedTerms.Count));
    }

    #endregion

    #region ExecuteAsync — case-insensitive allow-list matching

    [Fact]
    public async Task ExecuteAsync_CaseInsensitiveMatching()
    {
        // Arrange — LLM emits the entity name in lowercase; allow-list has it Title-Case.
        // Must still pass through (no scrub).
        var candidate = "Acme Corp received correspondence today.";
        var context = CreateContext(
            configJson: $$"""{"candidateText":"{{candidate}}","allowList":["ACME Corp"]}""",
            outputVariable: "scrubResult");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.StructuredData!.Value;
        data.GetProperty("scrubbedText").GetString().Should().Contain("Acme Corp",
            "case-insensitive allow-list match must pass through");
        data.GetProperty("removedTerms").GetArrayLength().Should().Be(0);
        VerifyHallucinationLogCount(Times.Never());
    }

    #endregion

    #region ExecuteAsync — pattern-required: per-invocation independence

    [Fact]
    public async Task ExecuteAsync_UsesScopePerInvocation()
    {
        // Pattern-required (node-executor-authoring.md). This executor has no Scoped
        // dependencies (pure string analysis, ILogger only — matches SanitizerNodeExecutor).
        // The test asserts the equivalent property: executor is reusable + reentrant —
        // two back-to-back invocations on the same instance produce independent,
        // correct results without cross-contamination.
        var c1 = "ACME Corp filed.";
        var c2 = "Johnson & Lee LLP filed.";

        var ctx1 = CreateContext(
            configJson: $$"""{"candidateText":"{{c1}}","allowList":["ACME Corp"]}""",
            outputVariable: "v1");
        var ctx2 = CreateContext(
            configJson: $$"""{"candidateText":"{{c2}}","allowList":["ACME Corp"]}""",
            outputVariable: "v2");

        // Act — back-to-back on the same Singleton instance
        var r1 = await _executor.ExecuteAsync(ctx1, CancellationToken.None);
        var r2 = await _executor.ExecuteAsync(ctx2, CancellationToken.None);

        // Assert — independent outcomes; first invocation must NOT leak into the second.
        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();

        r1.StructuredData!.Value.GetProperty("removedTerms").GetArrayLength()
            .Should().Be(0, "first invocation: ACME Corp is in allow-list, no removals");

        var r2Removed = r2.StructuredData!.Value.GetProperty("removedTerms")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        r2Removed.Should().NotBeEmpty("second invocation: Johnson & Lee LLP is hallucinated");
        r2Removed.Should().Contain(t => t!.Contains("Johnson & Lee LLP"));
    }

    #endregion

    #region ExecuteAsync — error paths

    [Fact]
    public async Task ExecuteAsync_ValidationFails_ReturnsValidationError()
    {
        // Arrange — config missing candidateText
        var context = CreateContext(
            configJson: """{"allowList":["ACME Corp"]}""",
            outputVariable: "scrubResult");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        VerifyHallucinationLogCount(Times.Never());
    }

    #endregion

    #region Helpers

    private static NodeExecutionContext CreateContext(
        string? configJson,
        string? outputVariable)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Entity Name Validator",
                ExecutionOrder = 1,
                OutputVariable = outputVariable!,
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Entity Name Validator"
            },
            ExecutorType = ExecutorType.EntityNameValidator,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N")
        };
    }

    /// <summary>
    /// Verifies the executor emitted exactly the expected number of structured
    /// <c>hallucination_detected</c> warning events. Matches messages that literally
    /// contain the canonical event-name token in their formatted-output string.
    /// </summary>
    private void VerifyHallucinationLogCount(Times expected)
    {
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, _) =>
                    o.ToString()!.Contains(EntityNameValidatorNodeExecutor.HallucinationDetectedEvent)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            expected);
    }

    #endregion
}
