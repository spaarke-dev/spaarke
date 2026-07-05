using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Null-Object subclass of <see cref="FileSummarizeService"/> registered when the compound AI
/// kill-switch is OFF (<c>Analysis:Enabled=false</c> or <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per ADR-032. <see cref="FileSummarizeService"/> is a concrete class
/// (ADR-010 DI minimalism — no new interface solely for the Null-Object), so the Null peer is
/// a subclass using the protected logger-only base ctor — mirrors
/// <see cref="Chat.NullSessionSummarizeOrchestrator"/> / <c>NullSprkChatAgentFactory</c>.
/// </para>
/// <para>
/// Required because <c>WorkspaceFileEndpoints.HandleSummarize</c> injects the concrete type
/// and <c>MapWorkspaceFileEndpoints</c> is mapped UNCONDITIONALLY: with the LinearConsumers
/// stack gated under the compound AI gate (ai-architecture-redesign-r1 task 006, FR-P0-05),
/// minimal-API parameter inference would abort host startup on the compound-OFF path without
/// this peer. <see cref="ExecuteAsync"/> throws <see cref="FeatureDisabledException"/> on the
/// first <c>MoveNextAsync()</c>; the endpoint's <c>catch (FeatureDisabledException)</c>
/// surfaces the 503 kill-switch pattern (SSE error chunk / <c>AsFeatureDisabled503</c>).
/// </para>
/// </remarks>
public sealed class NullFileSummarizeService : FileSummarizeService
{
    /// <summary>Stable errorCode carried in the 503 ProblemDetails — clients switch on this string.</summary>
    public const string ErrorCode = "ai.linear-consumers.disabled";

    private const string DetailMessage =
        "Linear AI consumers (file/chat summarize) require Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<FileSummarizeService> _logger;

    public NullFileSummarizeService(ILogger<FileSummarizeService> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

#pragma warning disable CS1998 // Method lacks await — throw precedes any yield (P3 fail-fast)
    public override async IAsyncEnumerable<AnalysisStreamChunk> ExecuteAsync(
        string extractedText,
        string fileName,
        string consumerType,
        HttpContext httpContext,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "NullFileSummarizeService.ExecuteAsync invoked while AI feature is disabled (errorCode={ErrorCode}, consumerType={ConsumerType}).",
            ErrorCode, consumerType);

        // Throwing inside the async iterator surfaces on the FIRST MoveNextAsync() — the
        // consuming endpoint's try/catch converts to the 503 kill-switch pattern.
        throw new FeatureDisabledException(ErrorCode, DetailMessage);

#pragma warning disable CS0162 // unreachable — required to make this a valid iterator method
        yield break;
#pragma warning restore CS0162
    }
#pragma warning restore CS1998
}
