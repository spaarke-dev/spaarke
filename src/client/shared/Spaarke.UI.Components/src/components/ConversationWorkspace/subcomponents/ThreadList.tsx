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
import { Button, Spinner, Text, Tooltip, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { AddRegular, PinFilled, PinRegular } from '@fluentui/react-icons';

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
   * Fired when the row's pin toggle is activated (task 041, FR-24). `nextPinned` is the DESIRED next state (the
   * inverse of the row's current `isPinned`). The host owns optimistic UI + rollback (see `ConversationWorkspace`);
   * this component only renders whatever `isPinned` it is given and reports the user's intent. Omit to hide the
   * pin toggle entirely (e.g. a read-only host).
   */
  onTogglePin?: (threadId: string, nextPinned: boolean) => void;
  className?: string;
}

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    minWidth: '240px',
    borderRightWidth: tokens.strokeWidthThin,
    borderRightStyle: 'solid',
    borderRightColor: tokens.colorNeutralStroke2,
    // Subtle contrast fill vs. the message pane (`colorNeutralBackground1`) so
    // the thread/side pane reads as distinct (task 062 / §B6). Semantic token —
    // adapts in dark mode.
    backgroundColor: tokens.colorNeutralBackground2,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    // Icon-only ＋ sits at the trailing edge (task 062 / §B5) — Teams-style.
    justifyContent: 'flex-end',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  list: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
    // Breathing room between thread rows (R3 UAT 2026-07-22 item 5b).
    rowGap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalS,
    paddingRight: tokens.spacingHorizontalS,
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    cursor: 'pointer',
    // Roomier rows (item 5b) — vertical M padding + a rounded card look now that
    // rows are separated by a gap rather than a divider line.
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    outlineStyle: 'none',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
  },
  rowSelected: {
    backgroundColor: tokens.colorBrandBackground2,
  },
  rowFocused: {
    boxShadow: `inset 0 0 0 2px ${tokens.colorStrokeFocus2}`,
  },
  rowHeader: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
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
  pinButton: {
    flexShrink: 0,
    minWidth: 'auto',
    padding: 0,
    // Smaller pin glyph (item 5b) — the icon font-size drives the SVG size.
    fontSize: tokens.fontSizeBase200,
  },
  pinButtonActive: {
    color: tokens.colorBrandForeground1,
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
  onTogglePin,
  className,
}) => {
  const styles = useStyles();
  const rowRefs = React.useRef<Map<string, HTMLDivElement>>(new Map());
  const [focusedThreadId, setFocusedThreadId] = React.useState<string | undefined>(undefined);

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
        {/* Icon-only ＋ create control (task 062 / §B5) — the accessible name
            stays "New thread" so keyboard + screen-reader users still identify
            it despite the label being visually icon-only (NFR-05). */}
        <Tooltip content="New thread" relationship="label">
          <Button
            appearance="primary"
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
            const isFocused = row.threadId === focusedThreadId;
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
                className={mergeClasses(
                  styles.row,
                  isSelected ? styles.rowSelected : undefined,
                  isFocused ? styles.rowFocused : undefined
                )}
                onClick={() => onSelectThread(row.threadId)}
                onFocus={() => setFocusedThreadId(row.threadId)}
              >
                <div className={styles.rowHeader}>
                  <Text className={mergeClasses(styles.rowName, isSelected ? styles.rowNameSelected : undefined)}>
                    {displayName}
                  </Text>
                  {/* Unread signal as a compact brand dot (item 5b) — replaces the
                      former "N new messages" text + inline Mark-as-read row. */}
                  {typeof row.unreadCount === 'number' && row.unreadCount > 0 && (
                    <span
                      className={styles.unreadDot}
                      role="img"
                      aria-label={`${row.unreadCount} unread message${row.unreadCount === 1 ? '' : 's'}`}
                      title={`${row.unreadCount} unread message${row.unreadCount === 1 ? '' : 's'}`}
                    />
                  )}
                  {onTogglePin && (
                    <Button
                      appearance="transparent"
                      size="small"
                      className={mergeClasses(styles.pinButton, isPinned ? styles.pinButtonActive : undefined)}
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
            );
          })}
        </div>
      )}
    </div>
  );
};

ThreadList.displayName = 'ThreadList';
