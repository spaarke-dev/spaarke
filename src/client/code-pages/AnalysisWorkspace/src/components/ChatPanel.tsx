/**
 * ChatPanel — Thin wrapper that mounts SprkChat from @spaarke/ui-components.
 *
 * This is the ONLY AnalysisWorkspace-specific file for chat integration.
 * All props are sourced from AnalysisAiContext — no direct service calls.
 *
 * In the unified workspace, cross-pane communication is handled via React
 * context (AnalysisAiContext) rather than BroadcastChannel, so `bridge`
 * is explicitly set to null.
 *
 * @see ADR-012 — Import SprkChat from shared component library
 * @see ADR-021 — Fluent UI v9 design system; makeStyles; design tokens
 */

import { memo, useCallback, useMemo } from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import { SprkChat } from '@spaarke/ui-components';
import type { IChatSession, IHostContext, IDocumentStreamSseEvent } from '@spaarke/ui-components';
import { useAnalysisAi } from '../context/AnalysisAiContext';

// ---------------------------------------------------------------------------
// Styles — fill parent container (right panel of the 2-panel layout)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    minHeight: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ChatPanel renders SprkChat with all props derived from AnalysisAiContext.
 *
 * Wrapped in React.memo to avoid re-renders when sibling panels (e.g.,
 * EditorPanel) update state that doesn't affect chat props.
 */
export const ChatPanel = memo(function ChatPanel(): JSX.Element {
  const styles = useStyles();
  const {
    chatSessionId,
    setChatSessionId,
    documentId,
    analysisId,
    playbookId,
    setPlaybookId,
    bffBaseUrl,
    token,
    streaming,
  } = useAnalysisAi();

  // Map onSessionCreated to setChatSessionId (extract sessionId from session object)
  const handleSessionCreated = useCallback(
    (session: IChatSession) => {
      setChatSessionId(session.sessionId);
    },
    [setChatSessionId]
  );

  // Host context for entity-scoped AI interactions
  const hostContext: IHostContext | undefined = useMemo(() => {
    if (!analysisId) return undefined;
    return {
      entityType: 'sprk_analysisoutput',
      entityId: analysisId,
      workspaceType: 'AnalysisWorkspace',
    };
  }, [analysisId]);

  // ── Task 007: Wire document stream events through context callbacks ────
  //
  // In the unified workspace, bridge is null so SprkChat cannot forward
  // document_stream SSE events via BroadcastChannel. Instead, we pass a
  // direct callback that routes events to AnalysisAiContext's streaming
  // callbacks, which write tokens directly to the Lexical editor via ref.
  //
  // Data flow: BFF SSE → useSseStream → onDocumentStreamEvent callback →
  //            streaming.onStreamStart/onStreamToken/onStreamEnd →
  //            editorRef.current.insert() → Lexical editor
  const handleDocumentStreamEvent = useCallback(
    (event: IDocumentStreamSseEvent) => {
      switch (event.type) {
        case 'document_stream_start':
          streaming.onStreamStart(event.operationId);
          break;
        case 'document_stream_token':
          streaming.onStreamToken(event.token);
          break;
        case 'document_stream_end':
          streaming.onStreamEnd(event.operationId);
          break;
      }
    },
    [streaming]
  );

  // Note on contentRef: SprkChat's contentRef expects a RefObject<HTMLElement>
  // for DOM-level text selection detection (highlight-refine). In the unified
  // workspace, editor selection is managed via AnalysisAiContext.editorSelection
  // and propagated through context rather than DOM selection events on a shared
  // element ref. The contentRef is omitted here; highlight-refine integration
  // will be wired through the context-based selection mechanism.

  return (
    <div className={styles.root} data-testid="chat-panel">
      <SprkChat
        sessionId={chatSessionId ?? undefined}
        documentId={documentId || undefined}
        analysisId={analysisId || undefined}
        playbookId={playbookId}
        apiBaseUrl={bffBaseUrl}
        accessToken={token ?? ''}
        onSessionCreated={handleSessionCreated}
        onPlaybookChange={setPlaybookId}
        hostContext={hostContext}
        bridge={null}
        onDocumentStreamEvent={handleDocumentStreamEvent}
      />
    </div>
  );
});

ChatPanel.displayName = 'ChatPanel';

export default ChatPanel;
