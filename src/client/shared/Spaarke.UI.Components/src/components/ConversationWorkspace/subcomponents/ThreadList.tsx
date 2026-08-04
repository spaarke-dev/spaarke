/**
 * ThreadList.tsx
 *
 * The left-pane thread list of `<ConversationWorkspace />` (task 012, FR-01/10).
 * Rows show the thread's name + an unread indicator (NO preview text, per
 * FR-10) + a pin toggle (task 041, FR-24). A create-thread (＋) icon button
 * invokes the host-supplied `onCreateThread` callback (the NewThreadModal
 * itself is task 024 — this component only wires the callback). Pin only — no
 * archive/mute/tag control (spec FR-24).
 *
 * Teams-style side pane (R3 task 062 / UAT §B4-6): NO "Filter threads" text
 * input (removed — not needed), the create control is an icon-only ＋ (not
 * "＋ New"), and the pane carries a subtle contrast fill
 * (`colorNeutralBackground2`) that sets it apart from the message pane
 * (`colorNeutralBackground1`) — semantic tokens only, so the contrast adapts in
 * dark mode.
 *
 * ARIA: `role="list"` / `role="listitem"` (NFR-05) with roving-tabIndex
 * keyboard navigation (ArrowUp/ArrowDown moves selection + focus, Enter/Space
 * selects). The pin toggle is a native `<Button>` (independently Tab-reachable,
 * Enter/Space-activatable) with `aria-pressed` reflecting state. Fluent v9
 * semantic tokens only (ADR-021) — dark mode passes through the host
 * `FluentProvider`.
 */
import * as React from 'react';
import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Spinner,
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { AddRegular, CommentMultipleRegular, DeleteRegular, PinFilled, PinRegular } from '@fluentui/react-icons';

export interface IThreadListRow {
  threadId: string;
  /** `sprk_name`. Falls back to a placeholder label when null/blank (not-yet-named thread). */
  name: string | null;
  /** Server-computed readable-message signal for this thread (see `communicationThreadListApi.ts` header note). Undefined while not yet loaded. */
  unreadCount?: number;
  /**
   * `sprk_ispinned` (task 040/041, FR-24). Host already normalizes the field's Dataverse-`null` reading (on a
   * pre-existing thread row) to `false` before this row is built — see `ConversationWorkspace`'s `threadListRows`
   * memo — so this component only ever renders a definite pinned/unpinned state. Undefined while not yet loaded
   * (treated the same as unpinned).
   */
  isPinned?: boolean;
}

export type ThreadListStatus = 'loading' | 'ready' | 'error';

export interface IThreadListProps {
  rows: IThreadListRow[];
  status: ThreadListStatus;
  errorMessage?: string;
  selectedThreadId?: string;
  onSelectThread: (threadId: string) => void;
  onCreateThread?: () => void;
  /**
   * Collapse the thread pane. When wired, clicking the "Threads" title row
   * collapses the pane (R3 UAT 2026-07-23 round 3 item 5 — no separate chevron).
   * `ConversationWorkspace` owns the collapsed state + renders the icon-only
   * re-expand strip. Omit to hide the collapse affordance.
   */
  onCollapse?: () => void;
  /**
   * Optional accessory rendered to the right of the "Threads" title (round 3
   * item 3) — e.g. the widget's "N new communications" count. Kept out of the
   * collapse click target.
   */
  titleAccessory?: React.ReactNode;
  /**
   * Fired when the row's pin toggle is activated (task 041, FR-24). `nextPinned` is the DESIRED next state (the
   * inverse of the row's current `isPinned`). The host owns optimistic UI + rollback (see `ConversationWorkspace`);
   * this component only renders whatever `isPinned` it is given and reports the user's intent. Omit to hide the
   * pin toggle entirely (e.g. a read-only host).
   */
  onTogglePin?: (threadId: string, nextPinned: boolean) => void;
  /**
   * Fired when the row's delete (trash) affordance is confirmed (R3 UAT round 7 item 7). The host owns the
   * soft-delete (deactivate) API call + list refresh + re-selection; this component only surfaces the trash icon
   * (revealed on row select/hover, left of the pin) and runs the confirmation dialog before calling back. Omit to
   * hide the delete affordance entirely (e.g. a read-only host).
   */
  onDeleteThread?: (threadId: string) => void;
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    // FILL the parent pane's width at any resized width (round 4 PCF item 2 /
    // widget) — without width:100% a flex child sizes to content, so the rows
    // didn't scale as the splitter widened the pane. min-width:0 keeps the pane
    // shrinkable below the 240px floor.
    width: '100%',
    minWidth: 0,
    // Light-grey contrast fill vs. the white message pane (`colorNeutralBackground1`)
    // so the thread pane reads as distinct (item 4). Semantic token — adapts in
    // dark mode.
    backgroundColor: tokens.colorNeutralBackground2,
  },
  // Thread-pane header (items 5/8/3): "Threads" title, a leading ＋ (create), and
  // a collapse affordance. The title area + trailing chevron both collapse.
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  // "Threads" title — icon + label, a clickable region that collapses the pane
  // (round 3 item 5 — whole row collapses, no separate chevron). Content-width
  // so the ＋ can right-align (item 6).
  headerTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    backgroundColor: 'transparent',
    borderTopStyle: 'none',
    borderRightStyle: 'none',
    borderBottomStyle: 'none',
    borderLeftStyle: 'none',
    paddingTop: tokens.spacingVerticalXXS,
    paddingBottom: tokens.spacingVerticalXXS,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusSmall,
    cursor: 'pointer',
    textAlign: 'left',
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    outlineStyle: 'none',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  // Static (non-collapsible) title when no `onCollapse` is wired.
  headerTitleStatic: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
  },
  // "Threads" icon (round 3 item 4).
  titleIcon: {
    fontSize: '18px',
    color: tokens.colorNeutralForeground2,
    flexShrink: 0,
  },
  // Count accessory right of the title (item 3) — e.g. "82 new communications".
  titleAccessory: {
    minWidth: 0,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  // Pushes the ＋ to the right edge of the header row (item 6).
  headerSpacer: {
    flex: '1 1 auto',
    minWidth: tokens.spacingHorizontalS,
  },
  list: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    // Thin modern scrollbar (owner UAT 2026-08-04 item 1 — consistency with the Email
    // widget's list + reading pane). Semantic tokens (ADR-021) so it themes in dark mode.
    scrollbarWidth: 'thin',
    scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
    '::-webkit-scrollbar': { width: '8px', height: '8px' },
    '::-webkit-scrollbar-thumb': {
      backgroundColor: tokens.colorNeutralStroke1,
      borderRadius: tokens.borderRadiusMedium,
    },
    '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
    '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
    // Rows are separated by thin divider lines (round 7 item 6, Email-widget
    // style) — no inter-row gap, no card radius. Padding lives on the rows so the
    // divider spans the full row width.
    paddingTop: 0,
    paddingBottom: 0,
    paddingLeft: 0,
    paddingRight: 0,
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    cursor: 'pointer',
    // Roomier rows: vertical M padding + 2px (round 7 item 2). Horizontal padding
    // on the row (not the list) so the divider line spans the full width.
    paddingTop: `calc(${tokens.spacingVerticalM} + 2px)`,
    paddingBottom: `calc(${tokens.spacingVerticalM} + 2px)`,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    // Thin subtle divider between rows (round 7 item 6) — like the Email widget.
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    outlineStyle: 'none',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      // Reveal the row's trailing actions (delete + pin) on hover (items 4/7).
      '& [data-row-action]': { opacity: 1 },
    },
    // Keyboard focus keeps the ring; mouse-click selection does NOT (round 7 item
    // 5 — no dark border on select, only the light-blue fill). `:focus-visible`
    // fires for keyboard/roving-tabindex focus but not pointer clicks.
    ':focus-visible': {
      boxShadow: `inset 0 0 0 2px ${tokens.colorStrokeFocus2}`,
    },
    // Keyboard users reaching the row actions via Tab reveals them too.
    ':focus-within': {
      '& [data-row-action]': { opacity: 1 },
    },
  },
  rowSelected: {
    // Light-blue highlight only (round 7 item 5) — no border/outline.
    backgroundColor: tokens.colorBrandBackground2,
    // Selected row reveals its trailing actions (items 4/7).
    '& [data-row-action]': { opacity: 1 },
  },
  rowHeader: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
  },
  // Trailing row-action cluster (delete + pin). Right-aligned after the name.
  rowActions: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
    flexShrink: 0,
  },
  rowName: {
    flex: '1 1 auto',
    minWidth: 0,
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  rowNameSelected: {
    fontWeight: tokens.fontWeightSemibold,
  },
  // Trailing action buttons (delete + pin). Hidden by default (opacity 0, but
  // kept in the DOM + tab order — NFR-05); revealed when the row is selected,
  // hovered, or focus-within (items 4/7). `data-row-action` is the reveal hook
  // the row's :hover/:focus-within + `.rowSelected` target.
  rowActionButton: {
    flexShrink: 0,
    minWidth: 'auto',
    padding: 0,
    fontSize: tokens.fontSizeBase100,
    opacity: 0,
    transitionProperty: 'opacity',
    transitionDuration: tokens.durationFaster,
    transitionTimingFunction: tokens.curveEasyEase,
    ':focus-visible': { opacity: 1 },
  },
  pinButtonActive: {
    color: tokens.colorBrandForeground1,
  },
  // A pinned thread ALWAYS shows its (filled) pin — even when the row is not
  // selected/hovered — so the pinned state stays visible (item 4 caveat). Wins the
  // opacity over `rowActionButton` (later class in mergeClasses).
  pinButtonPinned: {
    opacity: 1,
  },
  // Delete (trash) tint — subtle by default, danger-red on hover so the destructive
  // affordance reads clearly.
  deleteButton: {
    ':hover': { color: tokens.colorPaletteRedForeground1 },
  },
  // Compact unread signal (item 5b) — a brand dot replaces the "N new messages"
  // text row + its inline "Mark as read" button (that action moved to the message
  // toolbar, item 5c). Semantic token → adapts in dark mode (ADR-021).
  unreadDot: {
    flexShrink: 0,
    width: '8px',
    height: '8px',
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground,
    // Extra breathing room between the status dot and the thread name (round 7
    // item 3) — on top of the rowHeader gap.
    marginInlineEnd: tokens.spacingHorizontalXS,
  },
  centeredState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flexGrow: 1,
    padding: tokens.spacingVerticalXL,
    gap: tokens.spacingVerticalS,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  errorState: {
    color: tokens.colorPaletteRedForeground1,
  },
});

export const ThreadList: React.FC<IThreadListProps> = ({
  rows,
  status,
  errorMessage,
  selectedThreadId,
  onSelectThread,
  onCreateThread,
  onCollapse,
  titleAccessory,
  onTogglePin,
  onDeleteThread,
  className,
}) => {
  const styles = useStyles();
  const rowRefs = React.useRef<Map<string, HTMLDivElement>>(new Map());
  const [focusedThreadId, setFocusedThreadId] = React.useState<string | undefined>(undefined);
  // Pending delete target for the confirmation dialog (round 7 item 7). Null =
  // dialog closed. Captures the name so the prompt can reference the thread.
  const [pendingDelete, setPendingDelete] = React.useState<{ threadId: string; name: string } | null>(null);

  const focusRowByIndex = React.useCallback(
    (index: number) => {
      if (rows.length === 0) return;
      const clamped = Math.max(0, Math.min(index, rows.length - 1));
      const target = rows[clamped];
      setFocusedThreadId(target.threadId);
      rowRefs.current.get(target.threadId)?.focus();
    },
    [rows]
  );

  const handleListKeyDown = React.useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      if (rows.length === 0) return;
      const currentId = focusedThreadId ?? selectedThreadId ?? rows[0].threadId;
      const currentIndex = rows.findIndex(r => r.threadId === currentId);

      if (e.key === 'ArrowDown') {
        e.preventDefault();
        focusRowByIndex(currentIndex + 1);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        focusRowByIndex(currentIndex - 1);
      } else if (e.key === 'Home') {
        e.preventDefault();
        focusRowByIndex(0);
      } else if (e.key === 'End') {
        e.preventDefault();
        focusRowByIndex(rows.length - 1);
      } else if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        onSelectThread(currentId);
      }
    },
    [rows, focusedThreadId, selectedThreadId, focusRowByIndex, onSelectThread]
  );

  return (
    <div className={mergeClasses(styles.root, className)}>
      <div className={styles.toolbar}>
        {/* "Threads" title = icon + label (round 3 items 4/5). Clicking the row
            collapses the pane when `onCollapse` is wired (no separate chevron);
            otherwise a static label. */}
        {onCollapse ? (
          <button type="button" className={styles.headerTitle} aria-label="Collapse threads pane" onClick={onCollapse}>
            <CommentMultipleRegular className={styles.titleIcon} />
            <span>Threads</span>
          </button>
        ) : (
          <span className={styles.headerTitleStatic}>
            <CommentMultipleRegular className={styles.titleIcon} />
            <span>Threads</span>
          </span>
        )}
        {/* Count accessory (item 3) — kept out of the collapse click target. */}
        {titleAccessory && <span className={styles.titleAccessory}>{titleAccessory}</span>}
        <span className={styles.headerSpacer} />
        {/* ＋ create control right-aligned (item 6). */}
        <Tooltip content="New thread" relationship="label">
          <Button
            appearance="subtle"
            size="small"
            icon={<AddRegular />}
            aria-label="New thread"
            onClick={() => onCreateThread?.()}
            disabled={!onCreateThread}
          />
        </Tooltip>
      </div>

      {status === 'loading' && (
        <div className={styles.centeredState} role="status" aria-live="polite">
          <Spinner size="small" label="Loading threads…" />
        </div>
      )}

      {status === 'error' && (
        <div className={mergeClasses(styles.centeredState, styles.errorState)} role="alert">
          <Text>{errorMessage ?? 'Failed to load threads.'}</Text>
        </div>
      )}

      {status === 'ready' && rows.length === 0 && (
        <div className={styles.centeredState}>
          <Text>No threads yet.</Text>
        </div>
      )}

      {status === 'ready' && rows.length > 0 && (
        <div className={styles.list} role="list" aria-label="Conversation threads" onKeyDown={handleListKeyDown}>
          {rows.map(row => {
            const isSelected = row.threadId === selectedThreadId;
            const displayName = row.name && row.name.trim().length > 0 ? row.name : '(Untitled thread)';
            // Defensive falsy coercion (task 040 caveat: a pre-existing thread's sprk_ispinned reads back null,
            // not false — the host normalizes it before building rows, but this component never assumes a strict
            // boolean on the wire).
            const isPinned = !!row.isPinned;
            return (
              <div
                key={row.threadId}
                ref={el => {
                  if (el) rowRefs.current.set(row.threadId, el);
                  else rowRefs.current.delete(row.threadId);
                }}
                role="listitem"
                aria-selected={isSelected}
                tabIndex={isSelected || (!selectedThreadId && row === rows[0]) ? 0 : -1}
                // Round 7 item 5: no mouse-click focus border — selection is the
                // light-blue fill only; the keyboard ring comes from CSS
                // `:focus-visible` (see styles.row), not an onFocus-driven class.
                className={mergeClasses(styles.row, isSelected ? styles.rowSelected : undefined)}
                onClick={() => onSelectThread(row.threadId)}
                onFocus={() => setFocusedThreadId(row.threadId)}
              >
                <div className={styles.rowHeader}>
                  {/* Unread signal as a compact brand dot, now on the LEFT of the
                      row (R3 UAT 2026-07-23 item 6) — before the thread name. */}
                  {typeof row.unreadCount === 'number' && row.unreadCount > 0 && (
                    <span
                      className={styles.unreadDot}
                      role="img"
                      aria-label={`${row.unreadCount} unread message${row.unreadCount === 1 ? '' : 's'}`}
                      title={`${row.unreadCount} unread message${row.unreadCount === 1 ? '' : 's'}`}
                    />
                  )}
                  <Text className={mergeClasses(styles.rowName, isSelected ? styles.rowNameSelected : undefined)}>
                    {displayName}
                  </Text>
                  {/* Trailing actions (round 7 items 4/7): delete (trash) left of
                      the pin. Both are hidden until the row is selected/hovered
                      (except a pinned thread's filled pin, which stays visible).
                      `data-row-action` is the reveal hook. */}
                  <div className={styles.rowActions}>
                    {onDeleteThread && (
                      <Tooltip content="Delete thread" relationship="label">
                        <Button
                          data-row-action
                          appearance="transparent"
                          size="small"
                          className={mergeClasses(styles.rowActionButton, styles.deleteButton)}
                          icon={<DeleteRegular />}
                          aria-label={`Delete ${displayName}`}
                          onClick={e => {
                            e.stopPropagation();
                            setPendingDelete({ threadId: row.threadId, name: displayName });
                          }}
                        />
                      </Tooltip>
                    )}
                    {onTogglePin && (
                      <Button
                        data-row-action
                        appearance="transparent"
                        size="small"
                        className={mergeClasses(
                          styles.rowActionButton,
                          isPinned ? styles.pinButtonActive : undefined,
                          isPinned ? styles.pinButtonPinned : undefined
                        )}
                        icon={isPinned ? <PinFilled /> : <PinRegular />}
                        aria-label={isPinned ? `Unpin ${displayName}` : `Pin ${displayName}`}
                        aria-pressed={isPinned}
                        onClick={e => {
                          // Prevent the click from bubbling to the row's onClick (which would also select the
                          // thread) — the pin toggle is an independent affordance.
                          e.stopPropagation();
                          onTogglePin(row.threadId, !isPinned);
                        }}
                      />
                    )}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Delete-thread confirmation (round 7 item 7). Soft-delete (deactivate) is
          reversible, but a mis-click still removes the thread from the list, so we
          gate it behind an explicit confirm. The host owns the actual API call. */}
      <Dialog
        open={pendingDelete !== null}
        onOpenChange={(_e, data) => {
          if (!data.open) setPendingDelete(null);
        }}
      >
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Delete thread?</DialogTitle>
            <DialogContent>
              {pendingDelete
                ? `“${pendingDelete.name}” will be removed from your list. This deactivates the thread; it can be restored by an administrator.`
                : ''}
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setPendingDelete(null)}>
                Cancel
              </Button>
              <Button
                appearance="primary"
                onClick={() => {
                  if (pendingDelete) onDeleteThread?.(pendingDelete.threadId);
                  setPendingDelete(null);
                }}
              >
                Delete
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};

ThreadList.displayName = 'ThreadList';
