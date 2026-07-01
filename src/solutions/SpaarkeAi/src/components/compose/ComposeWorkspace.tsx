/**
 * ComposeWorkspace.tsx — workspace-level orchestrator for the Spaarke Compose surface.
 *
 * Project:   spaarkeai-compose-r1
 * Tasks:     042 (W5)  — initial orchestrator
 *            050 (W6)  — checkout-on-mount (POST /api/documents/{id}/checkout)
 *            051 (W7)  — multi-tab UX (BroadcastChannel + ConflictDialog + handlers)
 *            R2/R3 refactor — decompose 1795 LOC → ~400 LOC + 3 hooks; FU-1 heartbeat-gate fix
 *
 * Purpose:
 *   Composes the three Compose Phase-4 surfaces into a single mountable widget:
 *   - W4-045 `ComposeEditor`     (shared lib `@spaarke/compose-components`)
 *   - W4-043 `ComposeToolbar`    (SpaarkeAi solution, sibling file)
 *   - W4-044 `ComposeEmptyState` (SpaarkeAi solution, sibling file)
 *
 *   The workspace owns the document-context state machine, the BFF load/save
 *   wiring, and the PaneEventBus subscribers for Flows 1/2/5 per
 *   `COMPOSE_FLOW_RECEIVER_MATRIX`. The multi-tab UX (BroadcastChannel) +
 *   checkout lifecycle (probe + acquire + conflict resolution) + heartbeat
 *   (gated on `checkoutStatus === 'acquired'`) are extracted to three hooks
 *   under `./hooks/`.
 *
 * Refactor history:
 *   - W5-042 / W6-050 / W7-051: 1795 LOC monolith (orchestrator + checkout +
 *     broadcast + conflict handlers + heartbeat duplicated in ComposeEditor).
 *   - R2/R3 refactor (this version): decomposed to ~400 LOC orchestrator +
 *     3 hooks (`useComposeBroadcastChannel`, `useComposeCheckoutLifecycle`,
 *     `useComposeHeartbeatGate`). FU-1 heartbeat-gate bug fixed by hoisting
 *     the heartbeat to this workspace level and gating on
 *     `checkoutStatus === 'acquired'`. Behaviour is otherwise preserved.
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 only; `makeStyles` + `tokens.*` (semantic).
 *   - ADR-022: React 19.
 *   - ADR-028: `authenticatedFetch` from `@spaarke/auth` only.
 *   - ADR-030: typed PaneEventBus event signatures.
 *   - CLAUDE.md §3 sub-agent write boundary — no `.claude/` writes.
 *   - CLAUDE.md §6.5 ADR Conflict Resolution — no new tensions surfaced.
 *   - LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md 21 host MUSTs.
 *
 * @see projects/spaarkeai-compose-r1/tasks/042-frontend-create-compose-workspace.poml
 * @see ./hooks/useComposeBroadcastChannel.ts
 * @see ./hooks/useComposeCheckoutLifecycle.ts
 * @see ./hooks/useComposeHeartbeatGate.ts
 * @see ./ComposeWorkspace.types.ts
 * @see src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs (Load + Save contracts)
 * @see docs/architecture/LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  mergeClasses,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Text,
  Spinner,
} from '@fluentui/react-components';
import { ComposeBannerStack } from './ComposeBannerStack';
import {
  ComposeEditor,
  type ComposeEditorHandle,
  type ComposeEditorDocumentRef,
} from '@spaarke/compose-components';
import {
  useDispatchPaneEvent,
  usePaneEvent,
  type WorkspacePaneEvent,
  type ContextPaneEvent,
  type ConversationPaneEvent,
} from '@spaarke/ai-widgets';
import { authenticatedFetch } from '@spaarke/auth';

import { ComposeToolbar, type ComposeSummarizeRequestEvent } from './ComposeToolbar';
import { ComposeEmptyState } from './ComposeEmptyState';
import { ComposeConflictDialog } from './ComposeConflictDialog';
import { composeWorkspaceReducer, INITIAL_STATE } from './ComposeWorkspace.types';
import {
  useComposeBroadcastChannel,
  useComposeCheckoutLifecycle,
  useComposeHeartbeatGate,
} from './hooks';
import type {
  ComposeDocumentRef,
  ComposeAssistantToWorkspaceFlow,
  ComposeWorkspaceToContextFlow,
  ComposeWorkspaceToAssistantFlow,
} from '../../types/compose-contracts';

// Re-export types for backwards-compatible consumer imports.
export type {
  ComposeCheckoutStatus,
  ComposeWorkspaceState,
  ComposeWorkspaceAction,
} from './ComposeWorkspace.types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/**
 * Props for `ComposeWorkspace`. See file header for state-machine semantics.
 */
export interface ComposeWorkspaceProps {
  /**
   * Optional initial document pointer. When supplied, the workspace fetches
   * DOCX bytes on mount via `GET /api/compose/documents/{speId}?driveId&tenantId`.
   * When `undefined` or `null`, renders `ComposeEmptyState` (Path A/B picker).
   */
  initialDocumentRef?: ComposeDocumentRef | null;

  /** Optional initial ChatSession id (correlation). */
  initialSessionId?: string;

  /** BFF base URL (host only, e.g. `https://host.azurewebsites.net`). */
  bffBaseUrl: string;

  /** SPE driveId — required query param for the BFF Load endpoint. */
  driveId: string;

  /** Microsoft Entra tenant id (multi-tenant scoping per ADR-015 Tier 3). */
  tenantId: string;

  /** Called when the user clicks Browse in the empty state. */
  onBrowseRequested?: () => void;

  /** Called when the user clicks Search for Document in the empty state. */
  onSearchRequested?: () => void;

  /** Called once the workspace has mounted (LEGALWORKSPACE-EMBEDDED-MODE §6). */
  onComposeMount?: () => void;

  /** Called when the workspace is about to unmount (same contract). */
  onComposeUnmount?: () => void;

  /** Optional className passed to the root container (for host styling). */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    boxSizing: 'border-box',
    overflow: 'hidden',
  },
  toolbarSlot: {
    flexShrink: 0,
  },
  bannerStack: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
  editorSlot: {
    flex: 1,
    minHeight: 0,
    display: 'flex',
    flexDirection: 'column',
  },
  loadingState: {
    display: 'flex',
    flex: 1,
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function ComposeWorkspace(props: ComposeWorkspaceProps): React.JSX.Element {
  const styles = useStyles();
  const {
    initialDocumentRef,
    initialSessionId,
    bffBaseUrl,
    driveId,
    tenantId,
    onBrowseRequested,
    onSearchRequested,
    onComposeMount,
    onComposeUnmount,
    className,
  } = props;

  const [state, dispatch] = React.useReducer(composeWorkspaceReducer, INITIAL_STATE);

  // Imperative editor ref for save (TipTap → DOCX bytes).
  const editorRef = React.useRef<ComposeEditorHandle | null>(null);

  // Stable PaneEventBus dispatch.
  const busDispatch = useDispatchPaneEvent();

  // -------------------------------------------------------------------------
  // Mount/Unmount host hooks per LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT §6
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    onComposeMount?.();
    return () => {
      onComposeUnmount?.();
    };
    // Intentionally fire-once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // -------------------------------------------------------------------------
  // Kick off initial load if initialDocumentRef supplied
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    if (!initialDocumentRef) return;
    if (state.status !== 'empty' && state.status !== 'error') return;
    if (!initialDocumentRef.speDriveItemId) return;

    dispatch({
      kind: 'requestLoad',
      documentRef: initialDocumentRef,
      sessionId: initialSessionId ?? '',
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialDocumentRef?.speDriveItemId]);

  // -------------------------------------------------------------------------
  // BFF Load — GET /api/compose/documents/{speId}?driveId&tenantId&documentRecordId&displayName
  // -------------------------------------------------------------------------
  React.useEffect(() => {
    if (state.status !== 'loading') return;
    if (!state.documentRef) return;
    if (!bffBaseUrl) {
      dispatch({
        kind: 'loadFailed',
        errorMessage: 'BFF base URL is not configured. Cannot load document.',
      });
      return;
    }
    if (!driveId || !tenantId) {
      dispatch({
        kind: 'loadFailed',
        errorMessage:
          'SPE drive id and tenant id are required to load Compose documents. ' +
          'Check the host configuration.',
      });
      return;
    }

    const ac = new AbortController();
    const docRef = state.documentRef;

    (async () => {
      try {
        const qs = new URLSearchParams({ driveId, tenantId });
        if (docRef.sprkDocumentId) qs.set('documentRecordId', docRef.sprkDocumentId);
        if (docRef.fileName) qs.set('displayName', docRef.fileName);

        const url = `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(
          docRef.speDriveItemId
        )}?${qs.toString()}`;

        const response = await authenticatedFetch(url, { method: 'GET', signal: ac.signal });

        if (!response.ok) {
          const msg =
            response.status === 404
              ? 'Document not found. It may have been deleted or moved.'
              : response.status === 403
                ? 'You do not have permission to open this document.'
                : `Failed to load document (HTTP ${response.status}).`;
          dispatch({ kind: 'loadFailed', errorMessage: msg });
          return;
        }

        const payload = (await response.json()) as {
          documentSpeId: string;
          driveId: string;
          sessionId: string;
          documentRecordId?: string;
          // ASP.NET Core serializes byte[] as a base64-encoded string in JSON,
          // NOT as a JSON array of numbers. Decode with atob() below.
          content: string;
          eTag?: string;
          fileName?: string;
          size: number;
          correlationId?: string;
        };

        // Decode base64 -> bytes. atob() returns a binary string (one char per byte).
        const binary = atob(payload.content ?? '');
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
          bytes[i] = binary.charCodeAt(i);
        }
        if (ac.signal.aborted) return;
        dispatch({
          kind: 'loadSucceeded',
          docxBytes: bytes.buffer,
          etag: payload.eTag ?? null,
          sessionId: payload.sessionId ?? '',
          sprkDocumentId: payload.documentRecordId,
          fileName: payload.fileName,
        });
      } catch (err) {
        if (ac.signal.aborted) return;
        const message = err instanceof Error ? err.message : String(err);
        dispatch({
          kind: 'loadFailed',
          errorMessage: `Failed to load document: ${message}`,
        });
      }
    })();

    return () => ac.abort();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state.status, state.documentRef?.speDriveItemId, bffBaseUrl, driveId, tenantId]);

  // -------------------------------------------------------------------------
  // Multi-tab BroadcastChannel hook — owns "focus-me" + "force-closed" signaling
  // -------------------------------------------------------------------------
  // When a sibling tab posts `force-closed` (after a successful discard), this
  // tab transitions to 'cancelled'.
  const handleForceClosedFromOther = React.useCallback((): void => {
    dispatch({ kind: 'checkoutCancelled' });
  }, []);

  const { postFocusMe, postForceClosed } = useComposeBroadcastChannel(
    state.documentRef?.sprkDocumentId,
    state.sessionId,
    handleForceClosedFromOther
  );

  // -------------------------------------------------------------------------
  // SPE check-out lifecycle hook — owns probe + acquire + force-close + cancel
  // -------------------------------------------------------------------------
  const { forceCloseAndAcquire, discardAndCancel } = useComposeCheckoutLifecycle({
    state,
    dispatch,
    bffBaseUrl,
    postForceClosed,
  });

  // -------------------------------------------------------------------------
  // FU-1 fix — heartbeat gated on checkoutStatus === 'acquired'
  // -------------------------------------------------------------------------
  // Previously lived in ComposeEditor and fired regardless of checkout state;
  // hoisted here and gated so a cancelled tab no longer heart-beats a lock it
  // doesn't hold.
  useComposeHeartbeatGate(
    state.checkoutStatus,
    state.documentRef?.sprkDocumentId,
    bffBaseUrl
  );

  // -------------------------------------------------------------------------
  // Conflict dialog handler — "Go to that session"
  // -------------------------------------------------------------------------
  const handleGoToOtherSession = React.useCallback((): void => {
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Conflict dialog: Go to that session');
    postFocusMe();
    dispatch({ kind: 'checkoutCancelled' });
  }, [postFocusMe]);

  // -------------------------------------------------------------------------
  // BFF Save — POST /api/compose/documents/{speId}/save
  // -------------------------------------------------------------------------
  const triggerSave = React.useCallback(async (): Promise<void> => {
    if (state.status !== 'loaded') return;
    if (!state.documentRef || !editorRef.current) return;
    if (!bffBaseUrl || !driveId || !tenantId) {
      dispatch({
        kind: 'saveFailed',
        errorMessage: 'Cannot save — BFF base URL or SPE configuration missing.',
      });
      return;
    }

    dispatch({ kind: 'requestSave' });
    try {
      const bytes = await editorRef.current.serialize();
      const url = `${bffBaseUrl}/api/compose/documents/${encodeURIComponent(
        state.documentRef.speDriveItemId
      )}/save`;

      // Encode bytes -> base64. ASP.NET Core deserializes byte[] from
      // base64 strings, NOT from JSON number arrays. Iterate rather than
      // spread to avoid call-stack overflow on large documents.
      const view = new Uint8Array(bytes);
      let binary = '';
      for (let i = 0; i < view.length; i++) {
        binary += String.fromCharCode(view[i]);
      }
      const base64Content = btoa(binary);

      const response = await authenticatedFetch(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          driveId,
          tenantId,
          sessionId: state.sessionId,
          content: base64Content,
          documentRecordId: state.documentRef.sprkDocumentId ?? null,
          displayName: state.documentRef.fileName ?? null,
        }),
      });

      if (!response.ok) {
        const msg =
          response.status === 403
            ? 'You do not have permission to save this document.'
            : `Failed to save document (HTTP ${response.status}).`;
        dispatch({ kind: 'saveFailed', errorMessage: msg });
        return;
      }

      const payload = (await response.json()) as {
        documentSpeId: string;
        documentRecordId?: string;
        eTag?: string;
        size: number;
        wasPromotedThisSave: boolean;
      };

      dispatch({
        kind: 'saveSucceeded',
        sprkDocumentId: payload.documentRecordId,
        etag: payload.eTag ?? null,
      });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      dispatch({ kind: 'saveFailed', errorMessage: `Save failed: ${message}` });
    }
  }, [
    state.status,
    state.documentRef,
    state.sessionId,
    bffBaseUrl,
    driveId,
    tenantId,
  ]);

  // Keyboard shortcut: Ctrl/Cmd+S → save.
  React.useEffect(() => {
    if (state.status !== 'loaded') return;
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
        e.preventDefault();
        void triggerSave();
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [state.status, triggerSave]);

  // -------------------------------------------------------------------------
  // PaneEventBus subscribers — Flow 1, 2, 5 (R1 WIRED per matrix)
  // -------------------------------------------------------------------------

  // Flow 1 — `compose_selection_changed` on `context`. R1: LOG only.
  usePaneEvent('context', (event: ContextPaneEvent) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const e = event as unknown as { type?: string };
    if (e.type !== 'compose_selection_changed') return;
    const narrowed = event as unknown as ComposeWorkspaceToContextFlow;
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Flow 1 (selection_changed) observed', {
      sessionId: narrowed.sessionId,
      timestamp: narrowed.timestamp,
      speId: narrowed.documentRef?.speDriveItemId,
    });
  });

  // Flow 2 — `compose_selection_offer` on `conversation`. R1: LOG only.
  usePaneEvent('conversation', (event: ConversationPaneEvent) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const e = event as unknown as { type?: string };
    if (e.type !== 'compose_selection_offer') return;
    const narrowed = event as unknown as ComposeWorkspaceToAssistantFlow;
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Flow 2 (selection_offer) observed', {
      sessionId: narrowed.sessionId,
      timestamp: narrowed.timestamp,
      jpsScope: narrowed.jpsScope,
      speId: narrowed.documentRef?.speDriveItemId,
    });
  });

  // Flow 5 — `compose_assistant_insert` on `workspace`.
  // R1 BINDING (Spike #2 §10.3): manual-confirm gate; stage as pending.
  usePaneEvent('workspace', (event: WorkspacePaneEvent) => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const e = event as unknown as { type?: string };
    if (e.type !== 'compose_assistant_insert') return;
    const narrowed = event as unknown as ComposeAssistantToWorkspaceFlow;
    // eslint-disable-next-line no-console
    console.info('[ComposeWorkspace] Flow 5 (assistant_insert) observed', {
      sessionId: narrowed.sessionId,
      timestamp: narrowed.timestamp,
      sourceNodeId: narrowed.sourceNodeId,
      insertMode: narrowed.insertMode,
    });
    dispatch({ kind: 'pendingAssistantInsert', payload: narrowed });
  });

  // -------------------------------------------------------------------------
  // Empty-state handlers — additive workspace-channel dispatch
  // -------------------------------------------------------------------------

  const handleBrowseRequested = React.useCallback((): void => {
    onBrowseRequested?.();
    busDispatch(
      'workspace',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      {
        type: 'compose_browse_requested',
        timestamp: new Date().toISOString(),
      } as any
    );
  }, [onBrowseRequested, busDispatch]);

  const handleSearchRequested = React.useCallback((): void => {
    onSearchRequested?.();
    busDispatch(
      'workspace',
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      {
        type: 'compose_search_requested',
        timestamp: new Date().toISOString(),
      } as any
    );
  }, [onSearchRequested, busDispatch]);

  // -------------------------------------------------------------------------
  // Editor-side callbacks
  // -------------------------------------------------------------------------

  const handleDirtyChange = React.useCallback((_dirty: boolean): void => {
    // Reducer doesn't track a separate dirty flag — status is source of truth.
    // R2 may add a "Unsaved changes" indicator via this callback.
  }, []);

  const handleImportWarnings = React.useCallback(
    (warnings: Array<{ type: string; message: string }>): void => {
      dispatch({ kind: 'importWarnings', warnings });
    },
    []
  );

  // Toolbar observer — log compose-summarize dispatches (Tier 1 safe).
  const handleComposeSummarizeRequest = React.useCallback(
    (payload: ComposeSummarizeRequestEvent): void => {
      // eslint-disable-next-line no-console
      console.info('[ComposeWorkspace] compose-summarize dispatched', {
        sessionId: payload.sessionId,
        timestamp: payload.timestamp,
        documentId: payload.documentRef.documentId,
      });
    },
    []
  );

  // -------------------------------------------------------------------------
  // Editor doc-ref shape (shared lib has its own narrower interface)
  // -------------------------------------------------------------------------
  const editorDocRef: ComposeEditorDocumentRef | undefined = state.documentRef
    ? {
        speDriveItemId: state.documentRef.speDriveItemId,
        sprkDocumentId: state.documentRef.sprkDocumentId,
        fileName: state.documentRef.fileName,
        containerId: state.documentRef.containerId,
      }
    : undefined;

  // Toolbar documentId (Open-in-Word handoff) — accepts SPE id or sprk_documentid.
  const toolbarDocumentId =
    state.documentRef?.sprkDocumentId ?? state.documentRef?.speDriveItemId ?? '';

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------
  const showEditor = state.status === 'loaded' || state.status === 'saving';

  return (
    <div
      className={mergeClasses(styles.root, className)}
      role="region"
      aria-label={state.documentRef?.fileName ?? 'Compose workspace'}
      data-compose-workspace-status={state.status}
      data-compose-checkout-status={state.checkoutStatus}
      data-testid="compose-workspace"
    >
      {/* Empty state — Path A/B picker */}
      {state.status === 'empty' ? (
        <ComposeEmptyState
          onBrowseRequested={handleBrowseRequested}
          onSearchRequested={handleSearchRequested}
        />
      ) : null}

      {/* Loading spinner */}
      {state.status === 'loading' ? (
        <div
          className={styles.loadingState}
          role="status"
          aria-live="polite"
          data-testid="compose-workspace-loading"
        >
          <Spinner size="medium" />
          <Text size={300}>Loading document…</Text>
        </div>
      ) : null}

      {/* Loaded / Saving — toolbar + editor + banners */}
      {showEditor ? (
        <>
          <div className={styles.toolbarSlot}>
            <ComposeToolbar
              documentId={toolbarDocumentId}
              fileName={state.documentRef?.fileName}
              sessionId={state.sessionId}
              bffBaseUrl={bffBaseUrl}
              disabled={state.status === 'saving'}
              onComposeSummarizeRequest={handleComposeSummarizeRequest}
            />
          </div>

          {/* Banner stack — errors / warnings / checkout status / assistant pending */}
          <ComposeBannerStack
            errorMessage={state.errorMessage}
            checkoutStatus={state.checkoutStatus}
            checkoutLockedBy={state.checkoutLockedBy}
            checkoutFailureMessage={state.checkoutFailureMessage}
            importWarnings={state.importWarnings}
            pendingAssistantInsert={state.pendingAssistantInsert}
          />


          <div className={styles.editorSlot}>
            <ComposeEditor
              ref={editorRef}
              docxBytes={state.docxBytes}
              documentRef={editorDocRef}
              bffBaseUrl={bffBaseUrl}
              sessionId={state.sessionId}
              onDirtyChange={handleDirtyChange}
              onImportWarnings={handleImportWarnings}
            />
          </div>
        </>
      ) : null}

      {/*
        Task 051: Multi-tab conflict dialog (FR-16 verbatim labels). Rendered
        when the /checkout-status probe revealed THIS user holds the lock from
        another session.
      */}
      <ComposeConflictDialog
        open={state.checkoutStatus === 'same-user-conflict'}
        documentDisplayName={state.documentRef?.fileName}
        conflictingSessionOpenedAt={state.sameUserConflictInfo?.checkedOutAt ?? null}
        onGoToOtherSession={handleGoToOtherSession}
        onForceCloseOtherSession={() => {
          void forceCloseAndAcquire();
        }}
        onCancel={discardAndCancel}
      />

      {/* Error state — load failed; no document loaded */}
      {state.status === 'error' ? (
        <div
          className={styles.bannerStack}
          data-testid="compose-workspace-error-empty"
          role="alert"
        >
          <MessageBar intent="error">
            <MessageBarBody>
              <MessageBarTitle>Cannot load document</MessageBarTitle>
              {state.errorMessage ?? 'An unknown error occurred.'}
            </MessageBarBody>
          </MessageBar>
        </div>
      ) : null}
    </div>
  );
}

export default ComposeWorkspace;
