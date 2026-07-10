/**
 * useDocQaCitationBridge — FR-35 Document Q&A over the session-mounted
 * document (spaarkeai-compose-r2 task 072, stretch).
 *
 * Bridges SprkChat's EXISTING, SHIPPED citation mechanism (the `citations` SSE
 * event → `ICitation[]`, already rendered as `[N]` superscript markers in the
 * Assistant transcript — see `onCitations` on `ISprkChatProps`) to the two
 * Compose choreography reactions:
 *
 *   - `compose_qa_highlight` on the `workspace` channel — asks ComposeEditor to
 *     ephemerally highlight the cited excerpt's span, IF it is found in the
 *     currently-open document. No-op elsewhere (no editor mounted, or the
 *     excerpt doesn't match this document's text — e.g. the citation came
 *     from a different knowledge source). ComposeEditor owns that decision;
 *     this bridge does not know or care whether a document is open.
 *   - `compose_assistant_insight` on the `context` channel (the EXISTING Flow
 *     6 discriminant, `insightKind: 'citation'`) — audit-only surfacing of
 *     which source was cited, in the Context pane.
 *
 * ADR-039: rides the EXISTING Text-path citation mechanism. NO new capability,
 * playbook, or dispatch endpoint — `compose-document` remains session context
 * only; this hook only OBSERVES a citation SprkChat already produced.
 * ADR-015: `citation.excerpt` is Tier-3 content already rendered to the user;
 * dispatched only to drive in-document text matching, never logged here.
 *
 * Grounded-output invariant (ADR-039 / project CLAUDE.md): an answer with NO
 * citations must NOT drive a highlight or a Context-pane insight. This bridge
 * enforces that by construction — `onCitations` is only ever invoked by
 * SprkChat with a non-empty array (see SprkChat.tsx), and this hook further
 * requires a non-empty `excerpt` before dispatching anything.
 *
 * @see @spaarke/ui-components SprkChat's `onCitations` prop
 * @see @spaarke/compose-components useComposeWorkspaceReceivers — `onQaHighlight`
 * @see ../context/ContextPaneController.tsx — existing `compose_assistant_insight` receiver
 */
import * as React from 'react';
import type { ICitation } from '@spaarke/ui-components';
import type { DispatchPaneEvent } from '@spaarke/ai-widgets';

export interface UseDocQaCitationBridgeOptions {
  /** `PaneEventBus` dispatcher (from `useDispatchPaneEvent()`). */
  dispatch: DispatchPaneEvent;
  /** Getter for the active ChatSession id (stable across renders). */
  getSessionId: () => string | null;
}

export interface UseDocQaCitationBridgeResult {
  /** Wire directly to `<SprkChat onCitations={...}>`. */
  onCitations: (citations: ICitation[]) => void;
}

/**
 * @param options see {@link UseDocQaCitationBridgeOptions}
 */
export function useDocQaCitationBridge(
  options: UseDocQaCitationBridgeOptions
): UseDocQaCitationBridgeResult {
  const { dispatch, getSessionId } = options;

  // Dedupe guard: SprkChat's onCitations effect can re-fire with the same
  // citation set across unrelated re-renders (React effect semantics /
  // StrictMode double-invoke in dev). Key on the first citation's id+excerpt
  // so we do not re-dispatch (and re-scroll/re-flash the highlight) for a
  // citation set we already reacted to.
  const lastDispatchedKeyRef = React.useRef<string | null>(null);

  const onCitations = React.useCallback(
    (citations: ICitation[]): void => {
      if (citations.length === 0) return;

      const citation = citations[0];
      const sourceText = citation.excerpt?.trim();
      if (!sourceText) return;

      const dedupeKey = `${citation.id}:${citation.chunkId ?? ''}:${sourceText.length}`;
      if (lastDispatchedKeyRef.current === dedupeKey) return;
      lastDispatchedKeyRef.current = dedupeKey;

      const sessionId = getSessionId() ?? undefined;
      const timestamp = new Date().toISOString();
      const sectionLabel = citation.source;

      // Workspace leg — ephemeral highlight (no-op if no doc, or not found).
      dispatch('workspace', {
        type: 'compose_qa_highlight',
        qaSourceText: sourceText,
        qaSectionLabel: sectionLabel,
        sessionId,
        timestamp,
      });

      // Context leg — audit-only surfacing (EXISTING Flow 6 discriminant).
      dispatch('context', {
        type: 'compose_assistant_insight',
        insightKind: 'citation',
        insightText: sectionLabel ? `Cited from ${sectionLabel}` : 'Cited from the open document',
        sessionId,
        timestamp,
      });
    },
    [dispatch, getSessionId]
  );

  return { onCitations };
}

export default useDocQaCitationBridge;
