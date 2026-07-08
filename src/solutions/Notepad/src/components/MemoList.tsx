/**
 * MemoList — Fluent v9 Menu-based memo picker (FR-16).
 *
 * Presents a list icon (`TextBulletList24Regular`) button that opens a Menu of
 * prior memos. Each MenuItem shows:
 *   Line 1: derived title (via `deriveTitle`)
 *   Line 2: locale-formatted `createdon` datetime
 *
 * The current memo (matching `currentMemoId`) is visually highlighted via a
 * semantic-token background. Clicking a MenuItem invokes `onSelect` with the
 * memo's id. Empty state shows a single non-clickable MenuItem.
 *
 * @remarks
 * - The `memos` prop MUST already be sorted (most-recent first). Sorting lives
 *   in the parent `useSprkMemoRepository` hook (task 033).
 * - All colors and spacing come from Fluent v9 semantic tokens per ADR-021.
 * - React 18 (Notepad code page).
 *
 * @see projects/record-header-and-notepad-r1/spec.md FR-16
 * @see src/solutions/Notepad/src/utils/deriveTitle.ts
 */

import * as React from "react";
import {
  Button,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  makeStyles,
  mergeClasses,
  tokens,
  shorthands,
} from "@fluentui/react-components";
import { TextBulletList24Regular } from "@fluentui/react-icons";
import type { Memo } from "../types/memo";
import { deriveTitle } from "../utils/deriveTitle";

export interface IMemoListProps {
  /** Memos sorted by createdon descending (most-recent first). */
  memos: Memo[];
  /** The currently open memo's id, or null if none. Used for highlighting. */
  currentMemoId: string | null;
  /** Invoked when the user picks a memo. */
  onSelect: (memoId: string) => void;
  /** Disables the trigger button (e.g. during a save-in-flight). */
  disabled?: boolean;
}

const useStyles = makeStyles({
  itemContent: {
    display: "flex",
    flexDirection: "column",
    minWidth: 0,
  },
  itemTitle: {
    color: tokens.colorNeutralForeground1,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
  },
  itemDate: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  itemHighlighted: {
    backgroundColor: tokens.colorNeutralBackground3,
  },
  emptyItem: {
    color: tokens.colorNeutralForeground3,
    fontStyle: "italic",
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },
});

/**
 * Formats an ISO datetime string using the caller's locale.
 * Wrapped for testability — tests may spy on `Intl.DateTimeFormat`.
 */
function formatCreatedOn(iso: string): string {
  try {
    return new Intl.DateTimeFormat(undefined, {
      dateStyle: "medium",
      timeStyle: "short",
    }).format(new Date(iso));
  } catch {
    return iso;
  }
}

// v1.0.8 fix — wrap in React.memo so the list doesn't re-render on every
// parent state change (typing in the editor bumps NotepadShell state ~1x/sec
// on debounce commit; without memoization the MenuItem map re-runs each time
// and Fluent v9 Menu has non-trivial re-render cost, producing visible input
// lag). Props are shallow-comparable — `memos` array identity is stable when
// the repository hook returns the same list; `onSelect` is a stable callback.
export const MemoList: React.FC<IMemoListProps> = React.memo(function MemoList({
  memos,
  currentMemoId,
  onSelect,
  disabled = false,
}) {
  const styles = useStyles();

  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <Button
          appearance="subtle"
          icon={<TextBulletList24Regular />}
          disabled={disabled}
          aria-label="Prior memos"
        />
      </MenuTrigger>

      <MenuPopover>
        <MenuList>
          {memos.length === 0 ? (
            <MenuItem
              className={styles.emptyItem}
              disabled
              data-testid="memo-list-empty"
            >
              No memos yet
            </MenuItem>
          ) : (
            memos.map((memo) => {
              const isCurrent = memo.sprk_memoid === currentMemoId;
              return (
                <MenuItem
                  key={memo.sprk_memoid}
                  className={mergeClasses(
                    isCurrent ? styles.itemHighlighted : undefined
                  )}
                  onClick={() => onSelect(memo.sprk_memoid)}
                  data-memoid={memo.sprk_memoid}
                  data-current={isCurrent ? "true" : "false"}
                >
                  <span className={styles.itemContent}>
                    <span className={styles.itemTitle}>{deriveTitle(memo)}</span>
                    <span className={styles.itemDate}>
                      {formatCreatedOn(memo.createdon)}
                    </span>
                  </span>
                </MenuItem>
              );
            })
          )}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
},
// v1.0.9 custom comparator — MemoList only DISPLAYS title + createdon per
// memo, NOT bodies. But the parent hook now updates the `memos` array on
// every keystroke (to keep the controlled Textarea's value in sync). Default
// React.memo shallow-compare picks up the array reference change and forces
// a re-render every keystroke — which was the residual lag after v1.0.8.
// This comparator ignores body-only diffs so MemoList only re-renders on
// list membership + name + date changes.
(prev, next) => {
  if (prev.currentMemoId !== next.currentMemoId) return false;
  if (prev.disabled !== next.disabled) return false;
  if (prev.onSelect !== next.onSelect) return false;
  if (prev.memos.length !== next.memos.length) return false;
  for (let i = 0; i < prev.memos.length; i++) {
    const a = prev.memos[i];
    const b = next.memos[i];
    if (a.sprk_memoid !== b.sprk_memoid) return false;
    if (a.sprk_name !== b.sprk_name) return false;
    if (a.createdon !== b.createdon) return false;
  }
  return true;
});
MemoList.displayName = "MemoList";
