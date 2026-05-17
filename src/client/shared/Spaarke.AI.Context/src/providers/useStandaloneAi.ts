/**
 * useStandaloneAi — consumer hook for StandaloneAiContext
 *
 * Provides access to the standalone AI session state from any component
 * in the SpaarkeAi Code Page tree. Must be used within a StandaloneAiProvider.
 *
 * @example
 * function ChatPane() {
 *   const { entityContext, chatSessionId, playbookId, streaming, isLoading } = useStandaloneAi();
 *   // pass to <SprkChat ... />
 * }
 *
 * @throws Error if used outside of a StandaloneAiProvider
 *
 * Standards: ADR-012 (shared library — abstracted hook, not direct context access)
 */

import { useContext } from 'react';
import { StandaloneAiContext } from './StandaloneAiContext';
import type { StandaloneAiContextValue } from '../types/standalone-context';

/**
 * useStandaloneAi — access standalone AI session state from any child component.
 *
 * Returns the full StandaloneAiContextValue:
 *   - entityContext: resolved entity (matter/project/document) or null
 *   - chatSessionId / setChatSessionId: chat session lifecycle
 *   - playbookId / setPlaybookId: active playbook (persisted to sessionStorage)
 *   - streaming: StreamingCallbacks for SSE token flow
 *   - streamingState: StreamingState for UI indicators (isStreaming, tokenCount)
 *   - contextMapping: BFF-loaded playbook recommendation for the entity
 *   - isLoading: true while entity resolution OR context mapping is in flight
 *   - token / isAuthenticated: auth state for direct BFF calls (advanced use)
 *   - bffBaseUrl: HOST-only base URL — use buildBffApiUrl() to construct endpoints
 */
export function useStandaloneAi(): StandaloneAiContextValue {
  const context = useContext(StandaloneAiContext);
  if (!context) {
    throw new Error(
      'useStandaloneAi must be used within a StandaloneAiProvider. ' +
        'Wrap your component tree with <StandaloneAiProvider> before consuming this hook.'
    );
  }
  return context;
}
