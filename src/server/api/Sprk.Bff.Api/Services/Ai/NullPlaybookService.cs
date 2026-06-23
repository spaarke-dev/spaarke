using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="IPlaybookService"/> registered when the compound
/// AI kill-switch is OFF.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 B6. Returning empty playbook lists would let the chat
/// agent silently report "no playbooks available" instead of communicating that the AI
/// feature is off. Fail-fast surfaces the actual config state via 503 ProblemDetails.
/// </para>
/// <para>
/// Per D-09 §8 Risks: this Null-Object is registered via <c>AddSingleton</c> (NOT
/// <c>AddHttpClient</c>) — Null-Object as typed HttpClient would be awkward and HttpClient
/// machinery is unnecessary for a method-throws-immediately impl.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 2.</para>
/// </remarks>
public sealed class NullPlaybookService : IPlaybookService
{
    private const string ErrorCode = "ai.playbook.disabled";
    private const string DetailMessage =
        "Playbook services require Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullPlaybookService> _logger;

    public NullPlaybookService(ILogger<NullPlaybookService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<PlaybookResponse> CreatePlaybookAsync(
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(CreatePlaybookAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookResponse> UpdatePlaybookAsync(
        Guid playbookId,
        SavePlaybookRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(UpdatePlaybookAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookResponse?> GetPlaybookAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetPlaybookAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<bool> UserHasAccessAsync(
        Guid playbookId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(UserHasAccessAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookValidationResult> ValidateAsync(
        SavePlaybookRequest request,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ValidateAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookListResponse> ListUserPlaybooksAsync(
        Guid userId,
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ListUserPlaybooksAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookListResponse> ListPublicPlaybooksAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ListPublicPlaybooksAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookResponse> GetByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetByNameAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<CanvasLayoutResponse?> GetCanvasLayoutAsync(
        Guid playbookId,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(GetCanvasLayoutAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<CanvasLayoutResponse> SaveCanvasLayoutAsync(
        Guid playbookId,
        CanvasLayoutDto layout,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(SaveCanvasLayoutAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookListResponse> ListTemplatesAsync(
        PlaybookQueryParameters query,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ListTemplatesAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<PlaybookResponse> ClonePlaybookAsync(
        Guid sourcePlaybookId,
        Guid userId,
        string? newName = null,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ClonePlaybookAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    /// <summary>
    /// chat-routing-redesign-r1 FR-13 (task 034 follow-up — Gap 1): tenant-wide active
    /// playbook enumeration. Null-Object behavior: yield no rows (degrade gracefully
    /// rather than throw — the nightly drift job is a background scheduled task that
    /// MUST NOT crash when AI is disabled; instead it should run, find zero rows, and
    /// emit telemetry with scannedCount=0).
    /// </summary>
    public async IAsyncEnumerable<PlaybookResponse> ListAllActivePlaybooksAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ListAllActivePlaybooksAsync));
        await Task.CompletedTask;
        yield break;
    }

    /// <summary>
    /// chat-routing-redesign-r1 FR-13 (task 034 follow-up — Gap 3): tracking-field write.
    /// Null-Object behavior: no-op (background indexing writes MUST NOT crash when AI is
    /// disabled; the row simply isn't updated).
    /// </summary>
    public Task UpdateIndexStatusAsync(
        Guid playbookId,
        int statusCode,
        string? indexHash,
        string? lastError,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(UpdateIndexStatusAsync));
        return Task.CompletedTask;
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullPlaybookService.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
