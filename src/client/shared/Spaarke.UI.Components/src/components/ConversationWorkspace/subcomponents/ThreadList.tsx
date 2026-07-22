/**
 * ThreadList.tsx
 *
 * The left-pane thread list of `<ConversationWorkspace />` (task 012, FR-01/10).
 * Rows show the thread's name + an unread indicator (NO preview text, per
 * FR-10) + a pin toggle (task 041, FR-24) — a word-filter box narrows the
 * displayed rows and a create-thread (＋) button invokes the host-supplied
 * `onCreateThread` callback (the NewThreadModal itself is task 024 — this
 * component only wires the callback). Pin only — no archive/mute/tag control
 * (spec FR-24).
 *
 * ARIA: `role="list"` / `role="listitem"` (NFR-05) with roving-tabIndex
 * keyboard navigation (ArrowUp/ArrowDown moves selection + focus, Enter/Space
 * selects). The pin toggle is a native `<Button>` (independently Tab-reachable,
 * Enter/Space-activatable) with `aria-pressed` reflecting state. Fluent v9
 * semantic tokens only (ADR-021) — dark mode passes through the host
 * `FluentProvider`.
 */
import * as React from 'react';
import { Button, Input, Spinner, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { AddRegular, PinFilled, PinRegular } from '@fluentui/react-icons';
import { UnreadIndicator } from '../../CommunicationTimeline/subcomponents/UnreadIndicator';

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
  onMarkThreadRead?: (threadId: string) => void;
  searchTerm: string;
  onSearchTermChange: (value: string) => void;
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
    backgroundColor: tokens.colorNeutralBackground1,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingHorizontalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    flexShrink: 0,
  },
  searchInput: {
    flexGrow: 1,
    minWidth: 0,
  },
  list: {
    flex: '1 1 auto',
    minHeight: 0,
    overflowY: 'auto',
    display: 'flex',
    flexDirection: 'column',
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    cursor: 'pointer',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke3,
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
  },
  pinButtonActive: {
    color: tokens.colorBrandForeground1,
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
  onMarkThreadRead,
  searchTerm,
  onSearchTermChange,
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
        <Input
          className={styles.searchInput}
          contentBefore={undefined}
          value={searchTerm}
          placeholder="Filter threads"
          aria-label="Filter threads by name"
          onChange={(_e, data) => onSearchTermChange(data.value)}
        />
        <Button
          appearance="primary"
          icon={<AddRegular />}
          aria-label="New thread"
          onClick={() => onCreateThread?.()}
          disabled={!onCreateThread}
        >
          New
        </Button>
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
          <Text>{searchTerm ? 'No threads match your filter.' : 'No threads yet.'}</Text>
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
                {typeof row.unreadCount === 'number' && row.unreadCount > 0 && (
                  <UnreadIndicator
                    unreadCount={row.unreadCount}
                    onMarkAsRead={() => onMarkThreadRead?.(row.threadId)}
                  />
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

ThreadList.displayName = 'ThreadList';
