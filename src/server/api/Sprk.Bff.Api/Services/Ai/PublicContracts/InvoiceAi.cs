using OpenAI.Chat;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IInvoiceAi"/>: a thin wrapper over
/// <see cref="IPlaybookService"/> + <see cref="IOpenAiClient"/>.
/// </summary>
/// <remarks>
/// Per ADR-007 facade pattern: zero behavior change, narrow surface, single concrete
/// class. All resilience / retry / logging concerns remain inside the wrapped types.
/// </remarks>
public sealed class InvoiceAi : IInvoiceAi
{
    private readonly IPlaybookService _playbook;
    private readonly IOpenAiClient _openAi;

    public InvoiceAi(IPlaybookService playbook, IOpenAiClient openAi)
    {
        _playbook = playbook ?? throw new ArgumentNullException(nameof(playbook));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
    }

    /// <inheritdoc />
    public Task<PlaybookResponse> GetPlaybookByNameAsync(
        string playbookName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playbookName);
        return _playbook.GetByNameAsync(playbookName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<T> GetStructuredCompletionAsync<T>(
        IEnumerable<ChatMessage> messages,
        BinaryData jsonSchema,
        string schemaName,
        string deploymentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(jsonSchema);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deploymentName);

        return _openAi.GetStructuredCompletionAsync<T>(
            messages,
            jsonSchema,
            schemaName,
            deploymentName,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string text,
        string? model = null,
        int? dimensions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return _openAi.GenerateEmbeddingAsync(text, model, dimensions, cancellationToken);
    }
}
