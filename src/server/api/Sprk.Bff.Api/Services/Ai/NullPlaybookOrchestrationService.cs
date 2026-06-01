using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IPlaybookOrchestrationService"/> registered when
/// the compound AI kill-switch is OFF.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 L3. Silently returning an empty SSE stream or a
/// "no nodes" status would mislead playbook consumers — fail-fast surfaces the kill-switch
/// state at the endpoint layer as a 503 ProblemDetails.
/// </para>
/// <para>
/// The <see cref="IAsyncEnumerable{T}"/> methods throw eagerly on the first
/// <c>MoveNextAsync</c> call (via <c>yield</c> never reached); the consumer's
/// <c>await foreach</c> wrapped in <c>try/catch</c> converts to 503.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 2.</para>
/// </remarks>
public sealed class NullPlaybookOrchestrationService : IPlaybookOrchestrationService
{
    private const string ErrorCode = "ai.playbook.orchestration.disabled";
    private const string DetailMessage =
        "Playbook orchestration requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullPlaybookOrchestrationService> _logger;

    public NullPlaybookOrchestrationService(ILogger<NullPlaybookOrchestrationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IAsyncEnumerable<PlaybookStreamEvent> ExecuteAsync(
        PlaybookRunRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        LogDisabled(nameof(ExecuteAsync));
        return ThrowDisabledAsyncEnumerable(cancellationToken);
    }

    public IAsyncEnumerable<PlaybookStreamEvent> ExecuteAppOnlyAsync(
        PlaybookRunRequest request,
        string tenantId,
        CancellationToken cancellationToken)
    {
        LogDisabled(nameof(ExecuteAppOnlyAsync));
        return ThrowDisabledAsyncEnumerable(cancellationToken);
    }

    public Task<PlaybookValidationResult> ValidateAsync(
        Guid playbookId,
        CancellationToken cancellationToken)
    {
        LogDisabled(nameof(ValidateAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookRunStatus?> GetRunStatusAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        LogDisabled(nameof(GetRunStatusAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<bool> CancelAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        LogDisabled(nameof(CancelAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookRunHistoryResponse> GetRunHistoryAsync(
        Guid playbookId,
        int page = 1,
        int pageSize = 20,
        string? stateFilter = null,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetRunHistoryAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookRunDetail?> GetRunDetailAsync(
        Guid runId,
        CancellationToken cancellationToken)
    {
        LogDisabled(nameof(GetRunDetailAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullPlaybookOrchestrationService.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }

#pragma warning disable CS1998 // Method lacks await — yield is unreachable past the throw
    private static async IAsyncEnumerable<PlaybookStreamEvent> ThrowDisabledAsyncEnumerable(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
        // Unreachable, but required to satisfy the IAsyncEnumerable signature.
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }
#pragma warning restore CS1998
}
