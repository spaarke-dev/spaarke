/**
 * useAiSession — consumer hook for AiSessionContext
 *
 * Provides access to the R2 AI session state from any component in the
 * SpaarkeAi Code Page tree. Must be used within an AiSessionProvider (which
 * itself must be inside a PaneEventBusProvider). `initAuth(...)` from
 * @spaarke/auth must have been called before the provider mounts.
 *
 * Replaces R1's useStandaloneAi() from @spaarke/ai-context. Key differences:
 *  - No `subscribePaneEvents` — pane components use usePaneEvent() instead.
 *  - Exposes `turnCount` (number of completed turns) instead of raw tokenCount.
 *  - `entityContext` is provided by the host shell, not resolved internally.
 *  - Auth surface is function-based: `getAccessToken` / `authenticatedFetch` /
 *    `tenantId` / `isAuthenticated`. NO `token: string` — that snapshot pattern
 *    was the root cause of the 401-after-refresh bug Spaarke Auth v2 fixes.
 *    See AUDIT-FINDINGS-AUTH-SYSTEM §H-4.
 *
 * @example
 * function ConversationPane() {
 *   const {
 *     authenticatedFetch,            // preferred — token never crosses the boundary
 *     entityContext,
 *     chatSessionId, setChatSessionId,
 *     playbookId, setPlaybookId,
 *     streaming,
 *     streamingState,
 *     turnCount,
 *     isLoading,
 *   } = useAiSession();
 *
 *   return (
 *     <SprkChat
 *       sessionId={chatSessionId}
 *       onSessionCreated={setChatSessionId}
 *       playbookId={playbookId}
 *       streaming={streaming}
 *       authenticatedFetch={authenticatedFetch}
 *       {...}
 *     />
 *   );
 * }
 *
 * @throws Error if used outside an AiSessionProvider
 *
 * Standards:
 *  - ADR-012 — abstracted hook, not direct context access
 *  - ADR-022 — React 19 (NOT PCF-safe)
 *
 * @see AiSessionProvider — the provider that populates this context
 * @see useStandaloneAi (Spaarke.AI.Context) — R1 hook being replaced
 */

import { useContext } from 'react';
import { AiSessionContext } from './AiSessionProvider';
import type { AiSessionContextValue } from './AiSessionProvider';

/**
 * useAiSession — access R2 AI session state from any child component.
 *
 * Returns the full AiSessionContextValue:
 *   - isAuthenticated / getAccessToken / authenticatedFetch / tenantId: auth surface
 *     (function-based; no token string crosses the boundary — Spaarke Auth v2 §H-4)
 *   - bffBaseUrl: BFF HOST URL (use buildBffApiUrl() to construct endpoint URLs)
 *   - chatSessionId / setChatSessionId: chat session lifecycle
 *   - playbookId / setPlaybookId: active playbook (persisted to sessionStorage)
 *   - entityContext: resolved host entity (null in entityless mode)
 *   - contextMapping: BFF-loaded playbook recommendation for the entity
 *   - streaming: StreamingCallbacks to pass to SprkChat (includes onPaneEvent → PaneEventBus)
 *   - streamingState: current streaming phase for UI indicators
 *   - turnCount: number of completed conversation turns this session
 *   - isLoading: true while context mapping is in flight
 *
 * @throws Error — descriptive message if used outside an AiSessionProvider
 */
export function useAiSession(): AiSessionContextValue {
  const context = useContext(AiSessionContext);

  if (context === null) {
    throw new Error(
      'useAiSession must be used within an AiSessionProvider. ' +
        'Wrap your component tree with <PaneEventBusProvider><AiSessionProvider> ' +
        'before consuming this hook.'
    );
  }

  return context;
}
