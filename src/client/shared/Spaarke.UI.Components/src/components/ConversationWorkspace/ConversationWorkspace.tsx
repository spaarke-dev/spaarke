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
 *
 * --- Messages view (fm task 055) ---
 * In all mode an All / Messages switch sits under the thread-pane header. Messages lists only the caller's own 1:1
 * Direct conversations (`listThreads({ kind: 'direct' })`, decided server-side) and names each row by the other person
 * (`counterpartName`); its ＋ opens `<NewDirectMessageModal />` (one internal user + an optional first message). All is
 * today's list and its ＋ keeps `<NewThreadModal />`. Record mode shows no switch.
 *
 * --- Private messages in a record (fm task 056) ---
 * In record mode on a matter, project or work assignment, with a `navigationService`, the thread pane gains "Send
 * privately…": the `<NewDirectMessageModal />` person picker with the record fixed as context. The list also gains one
 * "Private · you and {name}" entry per counterpart (`listThreads({ about })`, caller-filtered server-side). An entry stands
 * for the pair's Direct thread but is never mixed into a record thread: opening it reads that thread narrowed to the
 * messages about the record (`IConversationRendererProps.about`), with an "Open full conversation" link
 * (`onOpenFullConversation`) to the whole pair thread. Without a `regarding` the shell shows none of this.
 */
import * as React from 'react';
import { Tab, TabList, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { CommentMultipleRegular } from '@fluentui/react-icons';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { INavigationService } from '../../types/serviceInterfaces';
import { isPrivateContextEntityType } from '../../services/communicationApi';
import {
  deactivateThread,
  getThreadUnreadCount,
  listThreads,
  listThreadsByRegarding,
  setThreadPinned,
  type IThreadListApiClientOptions,
  type IThreadListItemDto,
} from '../../services/communicationThreadListApi';
import { NewThreadModal } from '../NewThreadModal';
import { NewDirectMessageModal } from '../NewDirectMessageModal';
import { PanelSplitter } from '../PanelSplitter/PanelSplitter';
import { ThreadList, type IThreadListRow, type ThreadListStatus } from './subcomponents/ThreadList';
import { useThreadPaneLayout } from './useThreadPaneLayout';

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** Identifies an optional Dataverse record the thread list should be scoped to (record mode). */
export interface IConversationWorkspaceRegarding {
  /** Logical entity name (e.g. `sprk_matter`, `contact`) — one of the 11 ADR-024 families. */
  entityType: string;
  id: string;
  /** Optional display name, used in the "Send privately" dialog ("About {name}"). */
  name?: string;
}

/** Props handed to the injected `renderConversation` seam for the currently-selected thread. */
export interface IConversationRendererProps {
  threadId: string;
  /**
   * Display name of the selected thread, resolved from the shell's thread list (round-8.4 item 3b). Forward it to
   * `<ConversationView title={…} />` so the message pane header shows the thread name. The shell already has the names
   * loaded, so a renderer need not fetch them separately.
   */
  threadName?: string;
  /**
   * The selected thread's associated ("regarding") record (round-8.4 item 3), resolved from the thread list. Forward
   * it to `<ConversationView regarding={…} onOpenRecord={…} />` so the message pane can offer an "open record"
   * affordance. Undefined for a record-less Direct thread (or when the list row doesn't carry regarding).
   */
  regarding?: IConversationWorkspaceRegarding;
  /**
   * Set when the selected entry is a record's "Private · you and …" entry (fm task 056): the record the private messages
   * are about. Forward it to `<ConversationView about={…} />` so the read is narrowed to them.
   */
  about?: IConversationWorkspaceRegarding;
  /** Set with `about`: switches the pane to the pair's whole conversation. Forward it to `<ConversationView onOpenFullConversation={…} />`. */
  onOpenFullConversation?: () => void;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
  /**
   * Clears the currently-selected thread's list-pane unread badge (R3 UAT
   * 2026-07-22 item 5c). Wired to the shell's optimistic unread-clear so the
   * relocated "Mark as read" tool in `ConversationView`'s message toolbar can
   * dismiss the badge the thread row now shows as a dot. Forward it to
   * `<ConversationView onMarkThreadRead={…} />`.
   */
  onMarkThreadRead: () => void;
  /**
   * Called after the message pane renames the selected thread (round-8.4). The shell refreshes its thread list so the
   * new name shows on the left-pane row too. Forward it to `<ConversationView onThreadRenamed={…} />`.
   */
  onThreadRenamed: (newName: string) => void;
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

  /**
   * Record-lookup service for the built-in New-conversation modal (item 5a +
   * item 9). When provided, the shell OWNS the create flow: the ＋ affordance
   * opens `<NewThreadModal />` internally (name + associate-to-record picker +
   * optional plain-text message) and, on success, refreshes the list and selects
   * the new thread. The host binds `createXrmNavigationService()` (Xrm-backed
   * record lookup). Omit it (and `onCreateThread`) to hide the ＋.
   */
  navigationService?: INavigationService;

  /**
   * Fired when the ＋ (create thread) affordance is activated. OPTIONAL host
   * notification. When `onSearchRecipients` is supplied the shell also opens its
   * built-in `<NewThreadModal />`; a host that wants to own the create surface
   * itself can instead supply only `onCreateThread` (no `onSearchRecipients`)
   * and mount its own modal.
   */
  onCreateThread?: () => void;

  /** Fired whenever the selected thread changes (including the initial default-select). */
  onThreadSelected?: (threadId: string | undefined) => void;

  /**
   * Optional thread to select on first load instead of the most-recent default (round-8.4 item 8). Used when a host
   * opens the workspace targeting a specific thread — e.g. the PCF preview double-click opens the modal ON the
   * double-clicked message's thread. Ignored if the id isn't in the loaded, access-filtered thread set.
   */
  initialThreadId?: string;

  /**
   * Optional accessory rendered to the right of the "Threads" title (round 3
   * item 3) — the widget passes its "N new communications" count here so it
   * lives in the thread-pane header instead of a separate awareness bar.
   */
  threadsHeaderAccessory?: React.ReactNode;

  /** Fired on a list-load failure. The shell also renders an inline error state. */
  onError?: (error: Error) => void;

  /** All-mode page size forwarded to `listThreads`'s `top` param. Default 50 (server default). */
  pageSize?: number;

  /** Optional className applied to the root layout container. */
  className?: string;
}

/** Prefix of a private entry's list-row id, so it never collides with the pair thread's own id. */
const PRIVATE_ROW_PREFIX = 'private:';

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
  // Fixed-width thread pane wrapper (items 1/2) — the resizer owns the px width;
  // ThreadList fills it. flex-shrink:0 so the splitter, not flexbox, sets width.
  leftPane: {
    display: 'flex',
    flexShrink: 0,
    minWidth: 0,
    height: '100%',
    overflow: 'hidden',
  },
  rightPane: {
    display: 'flex',
    flexDirection: 'column',
    // flex-basis 0 (not auto) so the conversation is a proportion of the shell,
    // independent of the widest message bubble it contains (2026-07-22 item 3).
    flex: '1 1 0%',
    minHeight: 0,
    minWidth: 0,
    overflow: 'hidden',
    // White conversation surface vs. the grey thread pane (2026-07-23 item 4).
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Collapsed thread-pane strip (item 3) — a thin clickable rail that re-expands
  // the pane, mirroring the SpaarkeAi collapsed-pane pattern. Vertical "Threads"
  // label. Semantic tokens only (ADR-021).
  collapsedStrip: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalS,
    flexShrink: 0,
    width: '32px',
    height: '100%',
    paddingTop: tokens.spacingVerticalM,
    cursor: 'pointer',
    backgroundColor: tokens.colorNeutralBackground2,
    borderTopStyle: 'none',
    borderBottomStyle: 'none',
    borderLeftStyle: 'none',
    borderRightWidth: tokens.strokeWidthThin,
    borderRightStyle: 'solid',
    borderRightColor: tokens.colorNeutralStroke2,
    color: tokens.colorNeutralForeground2,
    outlineStyle: 'none',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '-2px',
    },
  },
  collapsedIcon: {
    fontSize: '20px',
    color: tokens.colorNeutralForeground2,
  },
  viewSwitch: {
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingVerticalXS,
    flexShrink: 0,
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
  navigationService,
  onCreateThread,
  onThreadSelected,
  initialThreadId,
  threadsHeaderAccessory,
  onError,
  pageSize = 50,
  className,
}) => {
  const styles = useStyles();

  // Resizable + collapsible thread pane (R3 UAT 2026-07-23 items 1/2/3). Default
  // width = 20% of the container; drag the splitter to resize; click the
  // "Threads" header (or chevron) to collapse to a thin re-expand rail.
  const { threadWidthPx, collapsed, toggleCollapse, splitterHandlers, isDragging, containerRef, currentRatio } =
    useThreadPaneLayout();

  // Built-in New-conversation modal (item 5a). The shell owns the create flow
  // when `onSearchRecipients` is supplied; `reloadToken` forces a list re-fetch
  // after a create so the new/reused thread appears.
  const [newThreadOpen, setNewThreadOpen] = React.useState(false);
  const [newDirectOpen, setNewDirectOpen] = React.useState(false);
  const [sendPrivatelyOpen, setSendPrivatelyOpen] = React.useState(false);
  const [reloadToken, setReloadToken] = React.useState(0);

  const client = React.useMemo<IThreadListApiClientOptions>(
    () => ({ authenticatedFetch, bffBaseUrl }),
    [authenticatedFetch, bffBaseUrl]
  );

  const entityType = regarding?.entityType;
  const recordId = regarding?.id;
  // All / Messages (fm task 055) — all mode only; record mode always lists the record's threads.
  const [view, setView] = React.useState<'all' | 'messages'>('all');
  const messagesView = !regarding && view === 'messages';
  // The list scope: a change of record or of view re-selects the first row of the new list.
  const regardingKey = entityType && recordId ? `${entityType}:${recordId}` : messagesView ? 'messages' : undefined;
  // Private messages (fm task 056) exist only for a matter, project or work assignment record.
  const privateRecord = !!entityType && !!recordId && isPrivateContextEntityType(entityType);
  const canSendPrivately = privateRecord && !!navigationService;
  // The record's "Private · you and …" entries: one pair thread per counterpart, from the `about` list.
  const [privateRows, setPrivateRows] = React.useState<IThreadListItemDto[]>([]);
  // True while the selected thread is shown as a private entry (narrowed to messages about the record).
  const [privateSelected, setPrivateSelected] = React.useState(false);

  // — Thread list load (regarding-scoped vs. all-mode) —
  // The thread-list text filter was removed in the Teams-style redesign (task
  // 062 / §B4 — "not needed"), so the list always loads the full access-
  // filtered set for the current scope (no `search` param, no client narrowing).
  const [allRows, setAllRows] = React.useState<IThreadListItemDto[]>([]);
  const [listStatus, setListStatus] = React.useState<ThreadListStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | undefined>(undefined);
  // Ontology task 221: the record's by-regarding read was cut by a bound (record mode only).
  const [listTruncated, setListTruncated] = React.useState(false);

  React.useEffect(() => {
    let cancelled = false;
    setListStatus('loading');
    setErrorMessage(undefined);
    setListTruncated(false);

    (async () => {
      try {
        const result =
          entityType && recordId
            ? await listThreadsByRegarding(entityType, recordId, client)
            : await listThreads(messagesView ? { top: pageSize, kind: 'direct' } : { top: pageSize }, client);
        if (cancelled) return;
        setAllRows(result.threads);
        setListTruncated(result.truncated === true);
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
  }, [entityType, recordId, messagesView, pageSize, client, reloadToken]);

  // — The record's private entries (fm task 056). A failure here must not break the record's own list. —
  React.useEffect(() => {
    if (!privateRecord || !entityType || !recordId) {
      setPrivateRows([]);
      return undefined;
    }
    let cancelled = false;
    listThreads({ top: pageSize, about: { entityType, id: recordId } }, client)
      .then(result => {
        if (!cancelled) setPrivateRows(result.threads);
      })
      .catch(err => {
        console.warn('ConversationWorkspace: the private-entries list failed to load', err);
        if (!cancelled) setPrivateRows([]);
      });
    return () => {
      cancelled = true;
    };
  }, [privateRecord, entityType, recordId, pageSize, client, reloadToken]);

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
      setPrivateSelected(false);
      setSelectedThreadId(next);
      onThreadSelected?.(next);
      return;
    }

    if (!selectedThreadId && allRows.length > 0) {
      // Round-8.4 item 8: prefer the host-requested initial thread when it's in the loaded set; else most-recent.
      const requested =
        initialThreadId && allRows.some(r => r.threadId === initialThreadId) ? initialThreadId : undefined;
      const next = requested ?? allRows[0].threadId;
      setSelectedThreadId(next);
      onThreadSelected?.(next);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [listStatus, allRows, regardingKey, initialThreadId]);

  const handleSelectThread = React.useCallback(
    (rowId: string) => {
      // A private entry selects its pair thread, narrowed to the messages about the record (fm task 056).
      const isPrivateEntry = rowId.startsWith(PRIVATE_ROW_PREFIX);
      const threadId = isPrivateEntry ? rowId.slice(PRIVATE_ROW_PREFIX.length) : rowId;
      setPrivateSelected(isPrivateEntry);
      setSelectedThreadId(threadId);
      onThreadSelected?.(threadId);
    },
    [onThreadSelected]
  );

  const handleSendPrivately = React.useCallback(() => setSendPrivatelyOpen(true), []);

  // The private dialog sent (or, on the "send as a direct message instead" fallback, sent plainly). A private send selects
  // the record's private entry for that pair, showing it before the list re-fetch lands; a plain fallback adds nothing to
  // the record's list. The modal closes itself on success.
  const handlePrivateSent = React.useCallback(
    (threadId: string, counterpartName: string, sentPrivately?: boolean) => {
      if (sentPrivately) {
        setPrivateRows(prev =>
          prev.some(r => r.threadId === threadId)
            ? prev
            : [
                {
                  threadId,
                  name: null,
                  threadType: 100000001,
                  createdOn: null,
                  isPinned: false,
                  regardingEntityType: null,
                  regardingId: null,
                  counterpartName,
                },
                ...prev,
              ]
        );
        setPrivateSelected(true);
        setSelectedThreadId(threadId);
        onThreadSelected?.(threadId);
      }
      setReloadToken(t => t + 1);
    },
    [onThreadSelected]
  );

  // "Open full conversation": the same pair thread, no longer narrowed.
  const handleOpenFullConversation = React.useCallback(() => setPrivateSelected(false), []);

  // — New-conversation create flow (item 5a / item 9) —
  // The ＋ affordance is enabled when the shell can start a create (its own modal
  // via `navigationService`, or a host-owned surface via `onCreateThread`).
  // fm task 055 (review F5): the host's `onCreateThread` surface starts a record-anchored conversation, so in the
  // Messages view only the shell's own new-direct-message modal can create — the ＋ shows there only with
  // `navigationService`, and the host callback fires only in All.
  const canCreateThread = messagesView ? !!navigationService : !!navigationService || !!onCreateThread;

  const handleOpenNewThread = React.useCallback(() => {
    if (messagesView) {
      if (navigationService) setNewDirectOpen(true);
      return;
    }
    // Notify the host (optional), and open the built-in modal when the shell owns
    // the create surface.
    onCreateThread?.();
    if (navigationService) setNewThreadOpen(true);
  }, [onCreateThread, navigationService, messagesView]);

  // fm task 055 (review F1): the create modals close themselves on success (onDismiss). The shell does not close them here,
  // because both call back BEFORE a first-message send failure is shown, and that warning must stay visible.
  const handleThreadCreated = React.useCallback(
    (threadId: string) => {
      // find-or-create returned a thread — select it immediately (the right pane
      // renders straight off `selectedThreadId`, so the conversation shows before
      // the list refresh completes) and re-fetch the list so the row appears.
      setPrivateSelected(false);
      setSelectedThreadId(threadId);
      onThreadSelected?.(threadId);
      setReloadToken(t => t + 1);
    },
    [onThreadSelected]
  );

  const handleViewChange = React.useCallback(
    (next: 'all' | 'messages') => {
      if (next === view) return;
      // Mark the list loading in the same update as the switch, so the selection effect waits for the new list and then
      // selects its first row (it would otherwise run once against the previous view's rows).
      setListStatus('loading');
      setAllRows([]);
      setView(next);
    },
    [view]
  );

  const handleConversationStarted = React.useCallback(
    (threadId: string, counterpartName: string) => {
      // The pair's thread (new or reused) — select it now and show it named by the person until the list re-fetch lands.
      // The modal closes itself on success (see handleThreadCreated).
      setAllRows(prev =>
        prev.some(r => r.threadId === threadId)
          ? prev
          : [
              {
                threadId,
                name: null,
                threadType: 100000001,
                createdOn: null,
                isPinned: false,
                regardingEntityType: null,
                regardingId: null,
                counterpartName,
              },
              ...prev,
            ]
      );
      setPrivateSelected(false);
      setSelectedThreadId(threadId);
      onThreadSelected?.(threadId);
      setReloadToken(t => t + 1);
    },
    [onThreadSelected]
  );

  const handleMarkThreadRead = React.useCallback((threadId: string) => {
    // Optimistic local clear — no persisted per-user watermark endpoint exists
    // for the list pane (see communicationThreadListApi.ts note); the true
    // incremental unread mechanic lives in the open-thread timeline.
    setUnreadCounts(prev => ({ ...prev, [threadId]: 0 }));
  }, []);

  // — Thread rename (round-8.4): the message pane already persisted the new name; here we just reflect it in the list.
  // Optimistic row update so the left-pane name changes instantly; reloadToken re-fetches to reconcile (server may
  // truncate to 200 chars).
  const handleThreadRenamed = React.useCallback((threadId: string, newName: string) => {
    setAllRows(prev => prev.map(r => (r.threadId === threadId ? { ...r, name: newName } : r)));
    setReloadToken(t => t + 1);
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

  // — Delete/deactivate thread (round 7 item 7): soft-delete on the server, then
  // drop the row locally + re-select a neighbor if the deleted thread was open. —
  const handleDeleteThread = React.useCallback(
    (threadId: string) => {
      const wasSelected = threadId === selectedThreadId;
      const remaining = allRows.filter(r => r.threadId !== threadId);

      // Optimistic: remove the row immediately.
      setAllRows(remaining);
      if (wasSelected) {
        const next = remaining[0]?.threadId;
        setSelectedThreadId(next);
        onThreadSelected?.(next);
      }

      deactivateThread(threadId, client).catch(err => {
        // Rollback — restore the list (a reload token re-fetches the authoritative set) on ANY failure.
        setReloadToken(t => t + 1);
        if (err instanceof Error) onError?.(err);
      });
    },
    [allRows, selectedThreadId, client, onThreadSelected, onError]
  );

  const threadListRows: IThreadListRow[] = React.useMemo(() => {
    const mapped = visibleRows.map(r => ({
      threadId: r.threadId,
      // A Messages row is named by the other person; every pair thread is called "Direct Conversation".
      name: messagesView && r.counterpartName ? r.counterpartName : r.name,
      unreadCount: unreadCounts[r.threadId],
      isPinned: r.isPinned,
    }));
    // Pinned threads float to the top (FR-24). Array.prototype.sort is spec-guaranteed stable (ES2019+), so within
    // the pinned/unpinned groups the existing order — server createdon-desc (all-mode) or server order
    // (record-mode) — and the word filter's already-narrowed set are both preserved untouched.
    const sorted = [...mapped].sort((a, b) => Number(!!b.isPinned) - Number(!!a.isPinned));
    // The record's private entries follow its own threads, one per counterpart (fm task 056). Read-only rows: pin and
    // delete would act on the whole pair conversation.
    const privateEntries: IThreadListRow[] = privateRows.map(r => ({
      threadId: `${PRIVATE_ROW_PREFIX}${r.threadId}`,
      name: `Private · you and ${r.counterpartName ?? 'a colleague'}`,
      readOnly: true,
    }));
    return [...sorted, ...privateEntries];
  }, [visibleRows, unreadCounts, messagesView, privateRows]);

  const rightPane = selectedThreadId ? (
    renderConversation ? (
      renderConversation({
        threadId: selectedThreadId,
        // Selected thread's display name for the message-pane header (round-8.4 item 3b).
        threadName: (() => {
          const row = allRows.find(r => r.threadId === selectedThreadId);
          if (row) return (messagesView && row.counterpartName) || row.name || undefined;
          // Not one of the record's own threads: a private entry's pair thread (narrowed or opened in full).
          const who = privateRows.find(r => r.threadId === selectedThreadId)?.counterpartName;
          if (!who) return undefined;
          return privateSelected ? `Private · you and ${who}` : who;
        })(),
        // Selected thread's associated record for the message-pane "open record" affordance (round-8.4 item 3).
        regarding: (() => {
          const row = allRows.find(r => r.threadId === selectedThreadId);
          return row?.regardingEntityType && row?.regardingId
            ? { entityType: row.regardingEntityType, id: row.regardingId }
            : undefined;
        })(),
        about: privateSelected && regarding ? regarding : undefined,
        onOpenFullConversation: privateSelected ? handleOpenFullConversation : undefined,
        authenticatedFetch,
        bffBaseUrl,
        // Relocated mark-as-read (item 5c): clear THIS thread's list badge when
        // the message-toolbar tool fires.
        onMarkThreadRead: () => handleMarkThreadRead(selectedThreadId),
        // Reflect an in-pane rename on the left-pane row (round-8.4).
        onThreadRenamed: (newName: string) => handleThreadRenamed(selectedThreadId, newName),
      })
    ) : (
      <DefaultConversationPane threadId={selectedThreadId} />
    )
  ) : (
    <DefaultConversationPane />
  );

  return (
    <div ref={containerRef} className={mergeClasses(styles.root, className)}>
      {collapsed ? (
        // Collapsed rail (round 3 item 5) — ICON ONLY (no "Threads" text); click to re-expand.
        <button
          type="button"
          className={styles.collapsedStrip}
          aria-label="Expand threads pane"
          title="Threads"
          onClick={toggleCollapse}
        >
          <CommentMultipleRegular className={styles.collapsedIcon} />
        </button>
      ) : (
        <>
          <div className={styles.leftPane} style={{ width: `${threadWidthPx}px` }}>
            <ThreadList
              rows={threadListRows}
              status={listStatus}
              errorMessage={errorMessage}
              selectedThreadId={
                selectedThreadId && privateSelected ? `${PRIVATE_ROW_PREFIX}${selectedThreadId}` : selectedThreadId
              }
              onSelectThread={handleSelectThread}
              onCreateThread={canCreateThread ? handleOpenNewThread : undefined}
              onSendPrivately={canSendPrivately ? handleSendPrivately : undefined}
              onCollapse={toggleCollapse}
              titleAccessory={threadsHeaderAccessory}
              onTogglePin={handleTogglePin}
              onDeleteThread={handleDeleteThread}
              createLabel={messagesView ? 'New message' : 'New thread'}
              emptyText={messagesView ? 'No messages yet.' : undefined}
              footnote={listTruncated ? 'Some messages may not be shown.' : undefined}
              subHeader={
                regarding ? undefined : (
                  <TabList
                    className={styles.viewSwitch}
                    size="small"
                    selectedValue={view}
                    onTabSelect={(_e, data) => handleViewChange(data.value === 'messages' ? 'messages' : 'all')}
                    aria-label="Thread list view"
                  >
                    <Tab value="all">All</Tab>
                    <Tab value="messages">Messages</Tab>
                  </TabList>
                )
              }
            />
          </div>
          <PanelSplitter
            onMouseDown={splitterHandlers.onMouseDown}
            onKeyDown={splitterHandlers.onKeyDown}
            onDoubleClick={splitterHandlers.onDoubleClick}
            isDragging={isDragging}
            currentRatio={currentRatio}
          />
        </>
      )}
      <div className={styles.rightPane}>{rightPane}</div>

      {/* Built-in New-conversation modal (item 5a / item 9) — only mounted when
          the shell owns the create surface (a navigation service was supplied). */}
      {navigationService && (
        <NewThreadModal
          open={newThreadOpen}
          onDismiss={() => setNewThreadOpen(false)}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          navigationService={navigationService}
          onThreadCreated={handleThreadCreated}
          regarding={regarding ? { entityType: regarding.entityType, id: regarding.id } : undefined}
          onError={onError}
        />
      )}
      {/* "Send privately…" (fm task 056): the same person picker, with the record fixed as context. */}
      {navigationService && canSendPrivately && (
        <NewDirectMessageModal
          open={sendPrivatelyOpen}
          onDismiss={() => setSendPrivatelyOpen(false)}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          navigationService={navigationService}
          privateContext={{ entityType: regarding.entityType, id: regarding.id, name: regarding.name }}
          onConversationStarted={handlePrivateSent}
          onError={onError}
        />
      )}
      {navigationService && !regarding && (
        <NewDirectMessageModal
          open={newDirectOpen}
          onDismiss={() => setNewDirectOpen(false)}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          navigationService={navigationService}
          onConversationStarted={handleConversationStarted}
          onError={onError}
        />
      )}
    </div>
  );
};

ConversationWorkspace.displayName = 'ConversationWorkspace';

export default ConversationWorkspace;
