namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// A reference to a document available to the agent for context or retrieval.
///
/// Passed via <see cref="AgentRequest.ContextDocuments"/> to indicate which documents
/// the agent may access when responding to the user's message.
/// </summary>
/// <param name="DocumentId">The Dataverse <c>sprk_document</c> record GUID.</param>
/// <param name="DisplayName">Human-readable document name for logging and tracing.</param>
public sealed record DocumentReference(
    string DocumentId,
    string? DisplayName = null);

/// <summary>
/// Encapsulates all inputs required by <see cref="ISprkAgent.ProcessAsync"/> to
/// process a single user turn and produce a streamed response.
///
/// This record is the provider-agnostic request contract for all agent implementations
/// (FR-701). It must not reference Azure OpenAI SDK types, Foundry SDK types, or any
/// other provider-specific types. The agent implementation is responsible for
/// translating these fields into provider-specific API calls.
/// </summary>
/// <param name="SessionId">
/// Opaque session identifier. Used for logging, distributed tracing, and
/// Cosmos DB / Redis key scoping. Stable for the lifetime of a user session.
/// </param>
/// <param name="UserId">
/// Authenticated user object ID (from the JWT <c>oid</c> claim). Used for
/// per-user rate limiting (ADR-016) and audit logging (ADR-015).
/// </param>
/// <param name="TenantId">
/// Tenant identifier extracted from the JWT claims. Used for data isolation
/// and Redis/Cosmos key scoping (ADR-014).
/// </param>
/// <param name="UserMessage">
/// The raw text of the user's current message. The agent is responsible for
/// injecting this into the provider-specific conversation payload.
/// </param>
/// <param name="ConversationHistory">
/// Ordered list of prior turns in this session (oldest first). The agent uses this
/// to maintain context across exchanges. May be empty for the first turn.
/// </param>
/// <param name="ContextDocuments">
/// Optional list of documents available to the agent for retrieval or reference.
/// Null when no document context is applicable (e.g. standalone chat mode).
/// </param>
/// <param name="RequestedCapabilities">
/// Optional list of capability identifiers (from <c>PlaybookCapabilities</c>) that
/// should be active for this request. Null means use the agent's default capability set.
/// In R2 Phase 1, the DirectOpenAiAgent does not use this field; it is reserved for
/// the R3 multi-agent router.
/// </param>
public sealed record AgentRequest(
    string SessionId,
    string UserId,
    string TenantId,
    string UserMessage,
    IReadOnlyList<ConversationTurn> ConversationHistory,
    IReadOnlyList<DocumentReference>? ContextDocuments = null,
    IReadOnlyList<string>? RequestedCapabilities = null);
