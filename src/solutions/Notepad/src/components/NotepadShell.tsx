/**
 * NotepadShell — Integration container for the Notepad code page.
 *
 * Composes:
 *   • useLaunchContext          — parse URL for regardingEntity + regardingId (FR-13 read-side)
 *   • useSprkMemoRepository     — CRUD hook (FR-14 list, FR-15 create, FR-17 save)
 *   • MemoList                  — dropdown of prior memos (FR-16)
 *   • MemoEditor                — <textarea> bound to current memo body (FR-17 keybindings)
 *   • CreatedByPopover          — "i" info popover for the current memo (FR-18)
 *   • deriveTitle               — top-bar title precedence (name → first non-empty body line → "Untitled")
 *
 * UI shape (design §3.6):
 *   ┌─────────────────────────────────────────────────────────────────┐
 *   │  {derivedTitle}                          [+]   [list]   [i]     │  <- topBar
 *   ├─────────────────────────────────────────────────────────────────┤
 *   │                                                                 │
 *   │  <MemoEditor value={currentMemo.sprk_memobody} />                │  <- editorContainer
 *   │                                                                 │
 *   └─────────────────────────────────────────────────────────────────┘
 *
 * Debounce-flush-before-switch (per task 037 notes):
 *   Before mutating currentMemo (via `+` create OR MemoList select), we call
 *   `updateBody(currentBody, { immediate: true })` to force any pending
 *   debounced save to flush against the OLD memo id. Without this the timer
 *   would fire AFTER we switched memos and write body text into the wrong
 *   record, or lose it because currentMemoIdRef flipped mid-flight.
 *
 * Error-state hooks for task 038 (FR-13 MessageBar):
 *   When `!valid` OR `unsupported`, we render a small placeholder div with
 *   `data-error-state` set for task 038 to swap in a MessageBar. This task
 *   intentionally does not implement the MessageBar UI — 038 owns FR-13's
 *   full user-facing error surface.
 *
 * ADR-021: Fluent v9 semantic tokens only; zero hex/rgb literals.
 * React 18 (Notepad SPA); zero @spaarke/auth (NFR-05); zero BFF calls (NFR-07).
 *
 * @see projects/record-header-and-notepad-r1/spec.md FR-14, FR-15, FR-16, FR-18
 * @see projects/record-header-and-notepad-r1/design.md §3.6
 */

import * as React from "react";
import {
  makeStyles,
  mergeClasses,
  tokens,
  Button,
  Text,
} from "@fluentui/react-components";
import { AddRegular } from "@fluentui/react-icons";

import { useLaunchContext } from "../hooks/useLaunchContext";
import { useSprkMemoRepository } from "../hooks/useSprkMemoRepository";
import { MemoList } from "./MemoList";
import { MemoEditor } from "./MemoEditor";
import { CreatedByPopover } from "./CreatedByPopover";
import { deriveTitle } from "../utils/deriveTitle";

// ---------------------------------------------------------------------------
// Styles — semantic tokens only per ADR-021
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  topBar: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    columnGap: tokens.spacingHorizontalS,
  },
  title: {
    flex: 1,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    overflow: "hidden",
    textOverflow: "ellipsis",
    whiteSpace: "nowrap",
    minWidth: 0,
  },
  toolbar: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalXS,
    flexShrink: 0,
  },
  editorContainer: {
    flex: 1,
    display: "flex",
    minHeight: 0,
    padding: tokens.spacingHorizontalM,
  },
  // Placeholder styling for the error-state div (task 038 replaces with
  // MessageBar; we want it visible + non-jarring in the meantime).
  errorState: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    color: tokens.colorNeutralForeground2,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Top-level Notepad UI. Composes launch context + repository + all sub-widgets.
 *
 * No props — reads everything from `window.location.search` (via
 * `useLaunchContext`) and `Xrm.WebApi` (via `useSprkMemoRepository`).
 */
export const NotepadShell: React.FC = () => {
  const styles = useStyles();

  // ─── Launch context (URL params) ────────────────────────────────────────
  const { regardingEntity, regardingId, valid, error: launchError } =
    useLaunchContext();

  // ─── Repository (CRUD) ──────────────────────────────────────────────────
  // Pass through even when !valid — the hook is defensive and returns safe
  // defaults when regardingEntity/regardingId are null. That keeps the hook
  // call order stable across renders (React rules-of-hooks).
  const {
    memos,
    loading,
    error: repoError,
    unsupported,
    currentMemo,
    setCurrentMemo,
    createMemo,
    updateBody,
  } = useSprkMemoRepository(regardingEntity, regardingId);

  // ─── Handlers ───────────────────────────────────────────────────────────

  /**
   * `+` new memo. FR-15.
   *
   * Flushes any pending debounced save against the CURRENT memo BEFORE
   * calling createMemo, so keystrokes typed just before clicking `+` are
   * persisted against the old memo (not the new one). `updateBody` is a
   * no-op if there's no current memo, so this is safe on first-load empty.
   */
  const handleCreate = React.useCallback(async (): Promise<void> => {
    if (currentMemo) {
      updateBody(currentMemo.sprk_memobody ?? "", { immediate: true });
    }
    await createMemo();
  }, [createMemo, updateBody, currentMemo]);

  /**
   * MemoList row click. FR-16.
   *
   * Same flush-before-switch protection as `handleCreate`: forces any
   * pending debounced write against the current memo id BEFORE we set
   * the new memo id.
   */
  const handleSelect = React.useCallback(
    (memoId: string): void => {
      if (currentMemo && memoId !== currentMemo.sprk_memoid) {
        updateBody(currentMemo.sprk_memobody ?? "", { immediate: true });
      }
      setCurrentMemo(memoId);
    },
    [updateBody, currentMemo, setCurrentMemo]
  );

  /**
   * MemoEditor onChange. Forwards to repository.updateBody with the same
   * signature; the repository owns the debounce timer.
   */
  const handleEditorChange = React.useCallback(
    (body: string, options?: { immediate?: boolean }): void => {
      updateBody(body, options);
    },
    [updateBody]
  );

  // ─── Render: error states (task 038 will wrap with MessageBar) ──────────

  // FR-13 read-side: invalid launch context (missing/malformed URL params).
  if (!valid) {
    return (
      <div
        className={mergeClasses(styles.root, styles.errorState)}
        data-testid="notepad-shell-invalid-launch"
        data-error-state="invalid-launch"
      >
        <Text>
          Notepad cannot open: {launchError ?? "missing regarding context"}
        </Text>
      </div>
    );
  }

  // FR-19: unsupported parent entity (not one of the 6 memo parents).
  if (unsupported) {
    return (
      <div
        className={mergeClasses(styles.root, styles.errorState)}
        data-testid="notepad-shell-unsupported"
        data-error-state="unsupported"
      >
        <Text>
          Notepad cannot open: entity type &quot;{regardingEntity}&quot; is not
          supported.
        </Text>
      </div>
    );
  }

  // Repository-level failure (Xrm.WebApi unavailable, CRUD error).
  if (repoError) {
    return (
      <div
        className={mergeClasses(styles.root, styles.errorState)}
        data-testid="notepad-shell-repo-error"
        data-error-state="repo-error"
      >
        <Text>Failed to load memos: {repoError.message}</Text>
      </div>
    );
  }

  // ─── Render: normal state ───────────────────────────────────────────────

  const derivedTitle = currentMemo ? deriveTitle(currentMemo) : "No memo";

  return (
    <div className={styles.root} data-testid="notepad-shell">
      <div className={styles.topBar}>
        <div
          className={styles.title}
          title={derivedTitle}
          data-testid="notepad-shell-title"
        >
          {derivedTitle}
        </div>
        <div className={styles.toolbar}>
          <Button
            appearance="subtle"
            icon={<AddRegular />}
            onClick={handleCreate}
            aria-label="New memo"
            disabled={loading}
            data-testid="notepad-shell-new"
          />
          <MemoList
            memos={memos}
            currentMemoId={currentMemo?.sprk_memoid ?? null}
            onSelect={handleSelect}
            disabled={loading}
          />
          {currentMemo && (
            <CreatedByPopover
              createdBy={currentMemo.createdby}
              createdOn={currentMemo.createdon}
            />
          )}
        </div>
      </div>

      <div className={styles.editorContainer}>
        <MemoEditor
          value={currentMemo?.sprk_memobody ?? ""}
          onChange={handleEditorChange}
          disabled={!currentMemo}
          placeholder={
            currentMemo
              ? "Start typing your memo..."
              : "Create a memo to start typing..."
          }
        />
      </div>
    </div>
  );
};

NotepadShell.displayName = "NotepadShell";
