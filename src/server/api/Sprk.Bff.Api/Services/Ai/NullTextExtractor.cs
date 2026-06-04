using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Null-Object implementation of <see cref="ITextExtractor"/> registered when
/// <c>DocumentIntelligence:Enabled=false</c>.
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 L4. Silently returning an empty
/// <see cref="TextExtractionResult"/> would mislead workspace/chat upload flows into a
/// "uploaded 0-char document" success path. Fail-fast surfaces the kill-switch state.
/// </para>
/// <para>
/// <see cref="IsSupported"/> and <see cref="GetMethod"/> return defaults that match the
/// real implementation's "unknown extension" semantics — these probes are non-throwing on
/// the real path and must stay non-throwing here to avoid false 503s on file-listing UX.
/// Only the <c>ExtractAsync</c> methods (which would actually invoke Document Intelligence)
/// throw.
/// </para>
/// <para>Introduced 2026-06-01 by task 011 Phase 1b Tier 2.</para>
/// </remarks>
public sealed class NullTextExtractor : ITextExtractor
{
    private const string ErrorCode = "ai.text-extraction.disabled";
    private const string DetailMessage =
        "Document text extraction requires DocumentIntelligence:Enabled=true.";

    private readonly ILogger<NullTextExtractor> _logger;

    public NullTextExtractor(ILogger<NullTextExtractor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ExtractAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public Task<TextExtractionResult> ExtractAsync(
        Stream fileStream,
        string fileName,
        string? driveId,
        string? itemId,
        string? etag,
        CancellationToken cancellationToken = default)
    {
        LogDisabled(nameof(ExtractAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public bool IsSupported(string extension) => false;

    public ExtractionMethod? GetMethod(string extension) => null;

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullTextExtractor.{Method} invoked while AI feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
