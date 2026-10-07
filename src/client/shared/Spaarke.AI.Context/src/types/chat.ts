/**
 * @spaarke/ai-context — Chat type definitions
 *
 * Only the live chat-session identity type remains. The ~20 other chat types
 * (messages, SSE events, citations, context-mapping responses, hook result
 * shapes) backed the never-adopted `useChatSession` / `useChatContextMapping` /
 * `useChatPlaybooks` hooks and `ChatApiClient`, which were DELETED 2026-10-03
 * (reuse audit C-14): zero consumers — SprkChat / SpaarkeAi use their own local
 * hooks of the same names.
 *
 * @see ADR-012 — Shared Component Library (no cross-library type coupling)
 */

/** A chat session, matching ChatSessionCreatedResponse from the create endpoint. */
export interface IChatSession {
  sessionId: string;
  createdAt: string;
}
