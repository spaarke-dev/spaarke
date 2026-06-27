using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Manages conversation sessions for the M365 Copilot agent.
/// Maps M365 conversation references to BFF chat sessions, maintaining
/// context across turns (current entity, document, playbook).
/// </summary>
public sealed class AgentConversationService
{
    private readonly ITenantCache _cache;
    private readonly ILogger<AgentConversationService> _logger;

    // Resource identifier for ITenantCache (FR-05: tenant-scoped key format
    // tenant:{tenantId}:agent-conversation:{conversationId}:v1).
    private const string AgentConversationResource = "agent-conversation";
    private const int CacheVersion = 1;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    public AgentConversationService(
        ITenantCache cache,
        ILogger<AgentConversationService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Gets or creates a conversation context for the given M365 conversation.
    /// </summary>
    public async Task<AgentConversationContext> GetOrCreateContextAsync(
        string tenantId,
        string conversationId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cached = await _cache.GetAsync<AgentConversationContext>(
            tenantId, AgentConversationResource, conversationId, CacheVersion,
            ct: cancellationToken);

        if (cached is not null)
        {
            _logger.LogInformation(
                "Resuming agent conversation {ConversationId} for user {UserId}",
                conversationId, userId);

            return cached;
        }

        _logger.LogInformation(
            "Creating new agent conversation {ConversationId} for user {UserId}",
            conversationId, userId);

        return CreateNewContext(tenantId, conversationId, userId);
    }

    /// <summary>
    /// Updates the conversation context (e.g., after the user selects a document or playbook).
    /// </summary>
    public async Task UpdateContextAsync(
        AgentConversationContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _cache.SetAsync(
            context.TenantId, AgentConversationResource, context.ConversationId, CacheVersion,
            context, CacheTtl,
            ct: cancellationToken);

        _logger.LogInformation(
            "Updated agent conversation {ConversationId}, active document: {DocumentId}, active playbook: {PlaybookId}",
            context.ConversationId, context.ActiveDocumentId, context.ActivePlaybookId);
    }

    /// <summary>
    /// Gets the BFF chat session ID mapped to this conversation, if one exists.
    /// </summary>
    public async Task<string?> GetBffSessionIdAsync(
        string tenantId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var context = await GetOrCreateContextAsync(tenantId, conversationId, "", cancellationToken);
        return context.BffChatSessionId;
    }

    /// <summary>
    /// Maps an M365 conversation to a BFF chat session.
    /// </summary>
    public async Task SetBffSessionIdAsync(
        string tenantId,
        string conversationId,
        string bffSessionId,
        CancellationToken cancellationToken = default)
    {
        var context = await _cache.GetAsync<AgentConversationContext>(
            tenantId, AgentConversationResource, conversationId, CacheVersion,
            ct: cancellationToken);

        if (context is not null)
        {
            context.BffChatSessionId = bffSessionId;
            await UpdateContextAsync(context, cancellationToken);
        }
    }

    /// <summary>
    /// Removes a conversation context (e.g., on session end).
    /// </summary>
    public async Task RemoveContextAsync(
        string tenantId,
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _cache.RemoveAsync(
            tenantId, AgentConversationResource, conversationId, CacheVersion,
            ct: cancellationToken);

        _logger.LogInformation(
            "Removed agent conversation {ConversationId}", conversationId);
    }

    private static AgentConversationContext CreateNewContext(
        string tenantId, string conversationId, string userId) => new()
        {
            TenantId = tenantId,
            ConversationId = conversationId,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
}

/// <summary>
/// Tracks the state of an M365 Copilot agent conversation across turns.
/// </summary>
public sealed class AgentConversationContext
{
    public string TenantId { get; set; } = "";
    public string ConversationId { get; set; } = "";
    public string UserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>BFF chat session ID mapped to this conversation.</summary>
    public string? BffChatSessionId { get; set; }

    /// <summary>Currently active document in the conversation.</summary>
    public Guid? ActiveDocumentId { get; set; }

    /// <summary>Currently active document name (for display).</summary>
    public string? ActiveDocumentName { get; set; }

    /// <summary>Currently active matter in the conversation.</summary>
    public Guid? ActiveMatterId { get; set; }

    /// <summary>Currently active matter name (for display).</summary>
    public string? ActiveMatterName { get; set; }

    /// <summary>Currently selected playbook (if user chose one).</summary>
    public Guid? ActivePlaybookId { get; set; }

    /// <summary>Last analysis ID (for follow-up queries).</summary>
    public Guid? LastAnalysisId { get; set; }

    /// <summary>Entity context from the current Dataverse form (if available).</summary>
    public string? CurrentEntityType { get; set; }

    /// <summary>Entity ID from the current Dataverse form.</summary>
    public Guid? CurrentEntityId { get; set; }
}
