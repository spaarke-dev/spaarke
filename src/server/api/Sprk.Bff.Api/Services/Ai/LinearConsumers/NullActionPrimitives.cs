using System.Text.Json;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Null-Object peers for the prompted-executor primitives, registered when the compound AI
/// kill-switch is OFF (<c>Analysis:Enabled=false</c> or <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032. Required because
/// <c>WorkspaceFileEndpoints.HandleSummarize</c> injects <see cref="IActionResolver"/> +
/// <see cref="IActionRunner"/> directly (FR-P3-05 wrapper absorption, ai-architecture-
/// redesign-r1 task 044) and <c>MapWorkspaceFileEndpoints</c> is mapped UNCONDITIONALLY:
/// with the LinearConsumers stack gated under the compound AI gate (task 006, FR-P0-05),
/// minimal-API parameter inference would abort host startup on the compound-OFF path
/// without these peers. Both throw <see cref="FeatureDisabledException"/>; the endpoint's
/// <c>catch (FeatureDisabledException)</c> surfaces the 503 kill-switch pattern
/// (SSE error chunk / <c>AsFeatureDisabled503</c>).
/// </para>
/// </remarks>
public sealed class NullActionResolver : IActionResolver
{
    /// <summary>Stable errorCode carried in the 503 ProblemDetails — clients switch on this string.</summary>
    public const string ErrorCode = "ai.linear-consumers.disabled";

    private const string DetailMessage =
        "The prompted executor (Action resolution) requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullActionResolver> _logger;

    public NullActionResolver(ILogger<NullActionResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<AnalysisAction> ResolveAsync(string consumerType, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "NullActionResolver.ResolveAsync invoked while AI feature is disabled (errorCode={ErrorCode}, consumerType={ConsumerType}).",
            ErrorCode, consumerType);
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }
}

/// <summary>
/// Null-Object peer for <see cref="IActionRunner"/> — see <see cref="NullActionResolver"/> remarks.
/// </summary>
public sealed class NullActionRunner : IActionRunner
{
    private const string DetailMessage =
        "The prompted executor (ActionRunner) requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullActionRunner> _logger;

    public NullActionRunner(ILogger<NullActionRunner> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<JsonElement> RunAsync(
        AnalysisAction action,
        DocumentText documentText,
        LinearRunContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "NullActionRunner.RunAsync invoked while AI feature is disabled (errorCode={ErrorCode}, consumerType={ConsumerType}).",
            NullActionResolver.ErrorCode, context?.ConsumerType);
        throw new FeatureDisabledException(NullActionResolver.ErrorCode, DetailMessage);
    }
}
