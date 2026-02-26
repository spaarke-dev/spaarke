using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Provides a <see cref="ChatContext"/> for a given document and tenant.
///
/// Implementations load the playbook Action (ACT-*) record from Dataverse, compose the
/// system prompt, and optionally attach analysis metadata.  The context is used by
/// <see cref="SprkChatAgent"/> to inject the system prompt into every streaming completion.
///
/// Seam justification (ADR-010): IChatContextProvider has a real seam — the production
/// implementation calls Dataverse/ScopeResolverService, while tests inject a stub.
/// </summary>
public interface IChatContextProvider
{
    /// <summary>
    /// Loads and composes the <see cref="ChatContext"/> for the given document.
    /// </summary>
    /// <param name="documentId">Dataverse sprk_document ID.</param>
    /// <param name="tenantId">Tenant ID extracted from the user's JWT claims.</param>
    /// <param name="playbookId">
    /// Playbook to resolve the Action (system prompt) from.  Passed explicitly so that
    /// context can be switched between documents without creating a new session.
    /// </param>
    /// <param name="hostContext">
    /// Optional host context describing where SprkChat is embedded.  When provided,
    /// entity type and ID are propagated into <see cref="ChatKnowledgeScope"/> for
    /// entity-scoped search filtering.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A fully composed <see cref="ChatContext"/>.</returns>
    Task<ChatContext> GetContextAsync(
        string documentId,
        string tenantId,
        Guid playbookId,
        ChatHostContext? hostContext = null,
        CancellationToken cancellationToken = default);
}
