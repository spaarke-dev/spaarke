/**
 * ConversationWorkspace.tsx
 *
 * The shared two-pane conversation shell (task 012, FR-01/10): a thread-list
 * pane on the left (`<ThreadList />`) and the selected thread's conversation
 * on the right, rendered through an injected `renderConversation` seam (see
 * "Renderer seam" below). Mount-agnostic (FR-01) — the SAME component renders
 * inline in the workspace, as a standalone code page, or inside a
 * record-scoped modal; the only difference across mounts is the optional
 * `regarding` prop + the host wrapper. Anchors its mount conventions to
 * `CommunicationsWorkspaceWidget.tsx` (keeps the `communications-list` type
 * string at the WIDGET layer — this shell has no widget "type" of its own).
 *
 * --- Renderer seam (right pane) ---
 * `<ConversationView />` (task 011) is being built CONCURRENTLY in this same
 * parallel wave and does not exist in the tree yet. Per the task 012 file-
 * ownership boundary, this component does NOT import `ConversationView`
 * directly. Instead it accepts an optional `renderConversation` prop:
 *
 *   renderConversation?: (props: IConversationRendererProps) => React.ReactNode
 *
 * When supplied, the shell calls it with `{ threadId, authenticatedFetch,
 * bffBaseUrl }` for the currently-selected thread and renders the result in
 * the right pane. When omitted, a local placeholder pane renders instead (see
 * `DefaultConversationPane` below) so this component is independently
 * mountable/testable before `ConversationView` lands. Once both tasks merge,
 * the host wires the real component in:
 *
 *   <ConversationWorkspace
 *     renderConversation={({ threadId, authenticatedFetch, bffBaseUrl }) => (
 *       <ConversationView threadId={threadId} authenticatedFetch={authenticatedFetch} bffBaseUrl={bffBaseUrl} />
 *     )}
 *     ...
 *   />
 *
 * --- Regarding filter + the FR-16 contract gap (documented deviation) ---
 * See `communicationThreadListApi.ts` module header for the full writeup:
 * FR-16's `GET /api/communications/threads` has NO regarding-filter query
 * parameter (task 003 shipped it that way on purpose, so record-less Direct
 * threads are included in the all-mode list). Record mode
 * (`regarding` present) therefore routes to the EXISTING, already
 * access-filtered `GET /api/communications/by-regarding/{entityType}/{id}`
 * endpoint instead — NOT a client-side filter of the all-mode result (NFR-01
 * is fully preserved: both paths are 100% server-filtered; this component
 * only chooses which server-filtered endpoint to call).
 *
 * All list/unread-count reads flow through the injected `authenticatedFetch`
 * (ADR-028); this component never imports `@spaarke/auth`. Fluent v9 semantic
 * tokens only (ADR-021) — dark mode passes through the host `FluentProvider`.
 */
import * as React from 'react';
import { Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import {
  getThreadUnreadCount,
  listThreads,
  listThreadsByRegarding,
  setThreadPinned,
  type IThreadListApiClientOptions,
  type IThreadListItemDto,
} from '../../services/communicationThreadListApi';
import { ThreadList, type IThreadListRow, type ThreadListStatus } from './subcomponents/ThreadList';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** Identifies an optional Dataverse record the thread list should be scoped to (record mode). */
export interface IConversationWorkspaceRegarding {
  /** Logical entity name (e.g. `sprk_matter`, `contact`) — one of the 11 ADR-024 families. */
  entityType: string;
  id: string;
}

/** Props handed to the injected `renderConversation` seam for the currently-selected thread. */
export interface IConversationRendererProps {
  threadId: string;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
}

export interface ConversationWorkspaceProps {
  /** Injected auth (ADR-028) — forwarded to `communicationThreadListApi` and the `renderConversation` seam. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Host only, no `/api` — forwarded exactly as `communicationThreadListApi` expects. */
  bffBaseUrl?: string;

  /**
   * Optional regarding scope. Present (record mode) → only that record's
   * threads are listed. Absent (all mode) → every thread the caller may see,
   * including record-less Direct threads (FR-16).
   */
  regarding?: IConversationWorkspaceRegarding;

  /**
   * Right-pane renderer seam — see the module header "Renderer seam" note.
   * Omit to render the built-in placeholder pane (pre-task-011 / tests).
   */
  renderConversation?: (props: IConversationRendererProps) => React.ReactNode;

  /** Fired when the ＋ (create thread) affordance is activated. The NewThreadModal itself is task 024 — this shell only wires the callback. */
  onCreateThread?: () => void;

  /** Fired whenever the selected thread changes (including the initial default-select). */
  onThreadSelected?: (threadId: string | undefined) => void;

  /** Fired on a list-load failure. The shell also renders an inline error state. */
  onError?: (error: Error) => void;

  /** All-mode page size forwarded to `listThreads`'s `top` param. Default 50 (server default). */
  pageSize?: number;

  /** Optional className applied to the root layout container. */
  className?: string;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'row',
    height: '100%',
    width: '100%',
    minHeight: 0,
    minWidth: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  rightPane: {
    display: 'flex',
    flexDirection: 'column',
    flex: '1 1 auto',
    minHeight: 0,
    minWidth: 0,
    overflow: 'hidden',
  },
  placeholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flexGrow: 1,
    padding: tokens.spacingVerticalXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
});

// ---------------------------------------------------------------------------
// Default right-pane placeholder (used when `renderConversation` is omitted)
// ---------------------------------------------------------------------------

const DefaultConversationPane: React.FC<{ threadId?: string }> = ({ threadId }) => {
  const styles = useStyles();
  return (
    <div className={styles.placeholder} role="region" aria-label="Conversation">
      <Text>
        {threadId ? `Conversation placeholder for thread ${threadId}.` : 'Select a thread to view the conversation.'}
      </Text>
    </div>
  );
};
DefaultConversationPane.displayName = 'DefaultConversationPane';

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const ConversationWorkspace: React.FC<ConversationWorkspaceProps> = ({
  authenticatedFetch,
  bffBaseUrl,
  regarding,
  renderConversation,
  onCreateThread,
  onThreadSelected,
  onError,
  pageSize = 50,
  className,
}) => {
  const styles = useStyles();

  const client = React.useMemo<IThreadListApiClientOptions>(
    () => ({ authenticatedFetch, bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  const entityType = regarding?.entityType;
  const recordId = regarding?.id;
  const regardingKey = entityType && recordId ? `${entityType}:${recordId}` : undefined;

  // — Thread list load (regarding-scoped vs. all-mode) —
  // The thread-list text filter was removed in the Teams-style redesign (task
  // 062 / §B4 — "not needed"), so the list always loads the full access-
  // filtered set for the current scope (no `search` param, no client narrowing).
  const [allRows, setAllRows] = React.useState<IThreadListItemDto[]>([]);
  const [listStatus, setListStatus] = React.useState<ThreadListStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | undefined>(undefined);

  React.useEffect(() => {
    let cancelled = false;
    setListStatus('loading');
    setErrorMessage(undefined);

    (async () => {
      try {
        const result =
          entityType && recordId
            ? await listThreadsByRegarding(entityType, recordId, client)
            : await listThreads({ top: pageSize }, client);
        if (cancelled) return;
        setAllRows(result.threads);
        setListStatus('ready');
      } catch (err) {
        if (cancelled) return;
        const message = err instanceof Error ? err.message : 'Failed to load threads.';
        setErrorMessage(message);
        setListStatus('error');
        if (err instanceof Error) onError?.(err);
      }
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entityType, recordId, pageSize, client]);

  // No thread-list text filter anymore (task 062 / §B4) — the visible set is
  // exactly the loaded, access-filtered set for the current scope.
  const visibleRows = allRows;

  // — Per-thread unread signal (see communicationThreadListApi.ts "getThreadUnreadCount" note) —
  const [unreadCounts, setUnreadCounts] = React.useState<Record<string, number>>({});
  const rowIdsKey = visibleRows.map(r => r.threadId).join(',');

  React.useEffect(() => {
    if (visibleRows.length === 0) return;
    let cancelled = false;

    (async () => {
      const results = await Promise.allSettled(visibleRows.map(r => getThreadUnreadCount(r.threadId, client)));
      if (cancelled) return;
      setUnreadCounts(prev => {
        const next = { ...prev };
        results.forEach((res, idx) => {
          if (res.status === 'fulfilled') {
            next[visibleRows[idx].threadId] = res.value.unreadCount;
          } else {
            // Soft-fail per row (code-review Suggestion, task 012): don't block or
            // error the whole list on one thread's unread-count call failing — just
            // leave that row's indicator absent and surface the failure for
            // telemetry/debugging visibility instead of swallowing it entirely.
            console.warn(
              `ConversationWorkspace: unread-count fetch failed for thread ${visibleRows[idx].threadId}`,
              res.reason
            );
          }
        });
        return next;
      });
    })();

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rowIdsKey, client]);

  // — Selection (default-select first/most-recent on load; reset on regarding-scope change) —
  const [selectedThreadId, setSelectedThreadId] = React.useState<string | undefined>(undefined);
  const prevRegardingKeyRef = React.useRef<string | undefined>(regardingKey);

  React.useEffect(() => {
    if (listStatus !== 'ready') return;

    const regardingChanged = prevRegardingKeyRef.current !== regardingKey;
    if (regardingChanged) {
      prevRegardingKeyRef.current = regardingKey;
      const next = allRows[0]?.threadId;
      setSelectedThreadId(next);
      onThreadSelected?.(next);
      return;
    }

    if (!selectedThreadId && allRows.length > 0) {
      const next = allRows[0].threadId;
      setSelectedThreadId(next);
      onThreadSelected?.(next);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [listStatus, allRows, regardingKey]);

  const handleSelectThread = React.useCallback(
    (threadId: string) => {
      setSelectedThreadId(threadId);
      onThreadSelected?.(threadId);
    },
    [onThreadSelected]
  );

  const handleMarkThreadRead = React.useCallback((threadId: string) => {
    // Optimistic local clear — no persisted per-user watermark endpoint exists
    // for the list pane (see communicationThreadListApi.ts note); the true
    // incremental unread mechanic lives in the open-thread timeline.
    setUnreadCounts(prev => ({ ...prev, [threadId]: 0 }));
  }, []);

  // — Pin/unpin (task 041, FR-24): optimistic local update + rollback on failure —
  const handleTogglePin = React.useCallback(
    (threadId: string, nextPinned: boolean) => {
      const previous = allRows.find(r => r.threadId === threadId)?.isPinned ?? false;

      // Optimistic: flip the row immediately so the pin toggle + the sort below react without waiting on the
      // network round trip.
      setAllRows(prev => prev.map(r => (r.threadId === threadId ? { ...r, isPinned: nextPinned } : r)));

      setThreadPinned(threadId, nextPinned, client).catch(err => {
        // Rollback — restore the pre-toggle value on ANY failure (network, 403 "cannot see thread", etc.). The
        // list's own error state is unaffected (this is a row-scoped write failure, not a list-load failure); the
        // failure still flows through the same onError seam the list-load path uses so a host can surface it.
        setAllRows(prev => prev.map(r => (r.threadId === threadId ? { ...r, isPinned: previous } : r)));
        if (err instanceof Error) onError?.(err);
      });
    },
    [allRows, client, onError]
  );

  const threadListRows: IThreadListRow[] = React.useMemo(() => {
    const mapped = visibleRows.map(r => ({
      threadId: r.threadId,
      name: r.name,
      unreadCount: unreadCounts[r.threadId],
      isPinned: r.isPinned,
    }));
    // Pinned threads float to the top (FR-24). Array.prototype.sort is spec-guaranteed stable (ES2019+), so within
    // the pinned/unpinned groups the existing order — server createdon-desc (all-mode) or server order
    // (record-mode) — and the word filter's already-narrowed set are both preserved untouched.
    return [...mapped].sort((a, b) => Number(!!b.isPinned) - Number(!!a.isPinned));
  }, [visibleRows, unreadCounts]);

  const rightPane = selectedThreadId ? (
    renderConversation ? (
      renderConversation({ threadId: selectedThreadId, authenticatedFetch, bffBaseUrl })
    ) : (
      <DefaultConversationPane threadId={selectedThreadId} />
    )
  ) : (
    <DefaultConversationPane />
  );

  return (
    <div className={mergeClasses(styles.root, className)}>
      <ThreadList
        rows={threadListRows}
        status={listStatus}
        errorMessage={errorMessage}
        selectedThreadId={selectedThreadId}
        onSelectThread={handleSelectThread}
        onMarkThreadRead={handleMarkThreadRead}
        onCreateThread={onCreateThread}
        onTogglePin={handleTogglePin}
      />
      <div className={styles.rightPane}>{rightPane}</div>
    </div>
  );
};

ConversationWorkspace.displayName = 'ConversationWorkspace';

export default ConversationWorkspace;
