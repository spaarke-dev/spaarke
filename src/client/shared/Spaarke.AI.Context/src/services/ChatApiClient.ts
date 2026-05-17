/**
 * ChatApiClient — BFF API client for chat sessions and context operations.
 *
 * Wraps all BFF /api/ai/chat/* calls using:
 * - buildBffApiUrl() from @spaarke/auth for all URL construction
 * - authenticatedFetch() from @spaarke/auth for all non-streaming requests
 *
 * For SSE streaming (POST /messages), use useSseStream hook directly —
 * ReadableStream responses cannot be wrapped by authenticatedFetch.
 *
 * @see ADR-012 - Shared Component Library
 * @see ADR-013 - AI Architecture (extend BFF, not separate service)
 * @see .claude/constraints/auth.md — MUST use buildBffApiUrl()
 */

import { buildBffApiUrl, authenticatedFetch } from '@spaarke/auth';
import type {
  IChatSession,
  IChatMessage,
  IHostContext,
  IPlaybookOption,
  IAnalysisChatContextResponse,
} from '../types/chat';

// ─────────────────────────────────────────────────────────────────────────────
// Request / Response shapes (mirror BFF API contract)
// ─────────────────────────────────────────────────────────────────────────────

interface CreateSessionRequest {
  documentId?: string | null;
  playbookId?: string | null;
  hostContext?: IHostContext | null;
}

interface SwitchContextRequest {
  documentId?: string | null;
  playbookId?: string | null;
  hostContext?: IHostContext | null;
  additionalDocumentIds?: string[];
}

interface CreateSessionResponse {
  sessionId: string;
  createdAt: string;
}

interface HistoryResponse {
  messages: Array<{ role: string; content: string; timestamp: string }>;
}

interface PlaybooksResponse {
  playbooks: Array<{
    id: string;
    name: string;
    description?: string;
    isPublic?: boolean;
  }>;
}

// ─────────────────────────────────────────────────────────────────────────────
// ChatApiClient
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Service client for BFF chat API endpoints.
 *
 * All URL construction uses buildBffApiUrl(). All HTTP calls use
 * authenticatedFetch(), which auto-attaches the Bearer token and retries on 401.
 *
 * @example
 * ```ts
 * const client = new ChatApiClient(bffBaseUrl);
 * const session = await client.createSession({ playbookId: 'abc123' });
 * ```
 */
export class ChatApiClient {
  private readonly bffBaseUrl: string;

  constructor(bffBaseUrl: string) {
    if (!bffBaseUrl || bffBaseUrl.trim() === '') {
      throw new Error('[ChatApiClient] bffBaseUrl is required. Call buildBffApiUrl() to obtain it.');
    }
    this.bffBaseUrl = bffBaseUrl;
  }

  // ── Session lifecycle ──────────────────────────────────────────────────────

  /**
   * Create a new chat session.
   * POST /api/ai/chat/sessions
   */
  async createSession(request: CreateSessionRequest): Promise<IChatSession> {
    const url = buildBffApiUrl(this.bffBaseUrl, '/ai/chat/sessions');

    const response = await authenticatedFetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        documentId: request.documentId ?? null,
        playbookId: request.playbookId ?? null,
        hostContext: request.hostContext ?? null,
      }),
    });

    const data: CreateSessionResponse = await response.json();
    return {
      sessionId: data.sessionId,
      createdAt: data.createdAt,
    };
  }

  /**
   * Load message history for a session.
   * GET /api/ai/chat/sessions/{sessionId}/history
   */
  async getSessionHistory(sessionId: string): Promise<IChatMessage[]> {
    const url = buildBffApiUrl(this.bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(sessionId)}/history`);

    const response = await authenticatedFetch(url, { method: 'GET' });
    const data: HistoryResponse = await response.json();

    return (data.messages ?? []).map(m => ({
      role: m.role as IChatMessage['role'],
      content: m.content,
      timestamp: m.timestamp,
    }));
  }

  /**
   * Switch the document/playbook context for an existing session.
   * PATCH /api/ai/chat/sessions/{sessionId}/context
   */
  async switchContext(sessionId: string, request: SwitchContextRequest): Promise<void> {
    const url = buildBffApiUrl(this.bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(sessionId)}/context`);

    const body: Record<string, unknown> = {
      documentId: request.documentId ?? null,
      playbookId: request.playbookId ?? null,
      hostContext: request.hostContext ?? null,
    };

    // Only include additionalDocumentIds when explicitly provided
    // (undefined = keep current, [] = clear, [...ids] = set new list)
    if (request.additionalDocumentIds !== undefined) {
      body.additionalDocumentIds = request.additionalDocumentIds;
    }

    await authenticatedFetch(url, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  /**
   * Delete a session.
   * DELETE /api/ai/chat/sessions/{sessionId}
   */
  async deleteSession(sessionId: string): Promise<void> {
    const url = buildBffApiUrl(this.bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(sessionId)}`);

    await authenticatedFetch(url, { method: 'DELETE' });
  }

  // ── Playbooks ──────────────────────────────────────────────────────────────

  /**
   * Fetch available playbooks.
   * GET /api/ai/chat/playbooks
   */
  async getPlaybooks(nameFilter?: string): Promise<IPlaybookOption[]> {
    const path = nameFilter ? `/ai/chat/playbooks?nameFilter=${encodeURIComponent(nameFilter)}` : '/ai/chat/playbooks';

    const url = buildBffApiUrl(this.bffBaseUrl, path);
    const response = await authenticatedFetch(url, { method: 'GET' });
    const data: PlaybooksResponse = await response.json();

    return (data.playbooks ?? []).map(pb => ({
      id: pb.id,
      name: pb.name,
      description: pb.description,
      isPublic: pb.isPublic,
    }));
  }

  // ── Context mapping ────────────────────────────────────────────────────────

  /**
   * Fetch the analysis-scoped chat context mapping.
   * GET /api/ai/chat/context-mappings/analysis/{analysisId}
   *
   * Returns null when the analysis record is not found (404).
   */
  async getAnalysisContextMapping(analysisId: string): Promise<IAnalysisChatContextResponse | null> {
    const url = buildBffApiUrl(this.bffBaseUrl, `/ai/chat/context-mappings/analysis/${encodeURIComponent(analysisId)}`);

    // 404 is a valid "not found" response — not an error for the UI
    let response: Response;
    try {
      response = await authenticatedFetch(url, { method: 'GET' });
    } catch (err) {
      // authenticatedFetch throws ApiError for non-2xx. Re-check for 404.
      // Check if the error is a 404 (ApiError with status 404)
      const apiErr = err as { status?: number };
      if (apiErr.status === 404) {
        return null;
      }
      throw err;
    }

    const data: IAnalysisChatContextResponse = await response.json();
    return data;
  }

  // ── SSE streaming URL builder ───────────────────────────────────────────────

  /**
   * Build the POST URL for sending a chat message (SSE streaming endpoint).
   * POST /api/ai/chat/sessions/{sessionId}/messages
   *
   * The caller is responsible for the fetch() call since ReadableStream
   * responses cannot be proxied through authenticatedFetch().
   */
  buildMessagesUrl(sessionId: string): string {
    return buildBffApiUrl(this.bffBaseUrl, `/ai/chat/sessions/${encodeURIComponent(sessionId)}/messages`);
  }
}
