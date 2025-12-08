namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Interface for handling document events from Service Bus.
/// Implementations contain the business logic for processing document operations.
/// </summary>
public interface IDocumentEventHandler
{
    /// <summary>
    /// Handles a document event asynchronously.
    /// Must be idempotent - calling multiple times with the same event should be safe.
    /// </summary>
    /// <param name="documentEvent">The document event to handle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task HandleEventAsync(DocumentEvent documentEvent, CancellationToken cancellationToken = default);
}
