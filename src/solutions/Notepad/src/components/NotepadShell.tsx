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
 * Error-state surface (task 038 · FR-13):
 *   When `!valid`, `unsupported`, OR `repoError` is set, we render the
 *   `NotepadErrorBanner` (Fluent v9 MessageBar with a Close button). The
 *   banner replaces the entire shell (top bar hidden) — matches FR-13's
 *   "MessageBar error and offer to close" phrasing. The `data-error-state`
 *   attribute is preserved on the outer div so integration tests continue to
 *   assert on the same hooks that task 037 established. Close-button
 *   mechanism (window.close + postMessage fallback) documented in
 *   `notes/close-mechanism.md` (U-01 resolution).
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
  tokens,
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  type MessageBarProps,
} from "@fluentui/react-components";
import { AddRegular, DismissRegular } from "@fluentui/react-icons";

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
  // Error-state container: centered MessageBar with breathing room. Replaces
  // the top-bar-and-editor shell entirely when an error is present (per FR-13
  // "MessageBar error and offer to close" — top bar is hidden in error state).
  errorRoot: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: `${tokens.spacingVerticalXXL} ${tokens.spacingHorizontalXXL}`,
    rowGap: tokens.spacingVerticalM,
  },
  errorBarWrapper: {
    width: "100%",
    maxWidth: "640px",
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

  // ─── Render: error states (task 038 — FR-13 MessageBar) ────────────────

  // FR-13 read-side: invalid launch context (missing/malformed URL params).
  if (!valid) {
    return (
      <NotepadErrorBanner
        kind="invalid-launch"
        contextError={launchError ?? null}
      />
    );
  }

  // FR-19: unsupported parent entity (not one of the 6 memo parents).
  if (unsupported) {
    return (
      <NotepadErrorBanner
        kind="unsupported"
        regardingEntity={regardingEntity}
      />
    );
  }

  // Repository-level failure (Xrm.WebApi unavailable, CRUD error).
  if (repoError) {
    return <NotepadErrorBanner kind="repo-error" repoError={repoError} />;
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

// ---------------------------------------------------------------------------
// NotepadErrorBanner — FR-13 error surface
// ---------------------------------------------------------------------------

/**
 * Kinds of unrecoverable error the Notepad shell surfaces to the user.
 *
 * - `invalid-launch` — `useLaunchContext` returned `valid: false` (missing or
 *   malformed `regardingEntity` / `regardingId`). FR-13 read-side.
 * - `unsupported`   — Launch context was valid but `regardingEntity` isn't one
 *   of the 6 memo-parent types. FR-19.
 * - `repo-error`    — `useSprkMemoRepository` reported a runtime error
 *   (Xrm.WebApi missing, CRUD failure, etc.).
 */
export type NotepadErrorKind = "invalid-launch" | "unsupported" | "repo-error";

interface INotepadErrorBannerProps {
  kind: NotepadErrorKind;
  regardingEntity?: string | null;
  contextError?: string | null;
  repoError?: Error | null;
}

/**
 * Modal close handler.
 *
 * Two-strategy sequence per U-01 resolution (see
 * `notes/close-mechanism.md`):
 *   1. `window.close()` — works when Notepad was launched via
 *      `Xrm.Navigation.navigateTo({pageType: "webresource"}, {target: 2})`
 *      (modal target=2).
 *   2. `postMessage({type: "notepad-close"})` to `window.parent` — fallback
 *      for hosts that intercept close via postMessage.
 *
 * Both attempts are wrapped in defensive try/catch so a broken host never
 * throws unhandled from a Close-button click. Neither path is guaranteed to
 * close every possible Xrm host; documented as an expected limitation in
 * `notes/close-mechanism.md`. Users can also close via browser ESC or the
 * ambient modal chrome.
 */
function handleNotepadClose(): void {
  try {
    // Primary: modal target=2 responds to window.close.
    window.close();
  } catch {
    /* ignore */
  }
  try {
    // Fallback: some Xrm hosts intercept close via postMessage.
    window.parent?.postMessage({ type: "notepad-close" }, "*");
  } catch {
    /* ignore */
  }
}

/**
 * Compose title + body + intent for a given error kind. Pure function so the
 * banner can render as a stateless React.FC.
 */
function composeErrorMessage(props: INotepadErrorBannerProps): {
  intent: MessageBarProps["intent"];
  title: string;
  body: string;
} {
  switch (props.kind) {
    case "invalid-launch":
      return {
        intent: "error",
        title: "Cannot open Notepad",
        body:
          props.contextError ??
          "Notepad cannot open: missing regarding context.",
      };
    case "unsupported":
      return {
        intent: "warning",
        title: "Unsupported entity type",
        body: `Notepad does not support memos for entity type "${
          props.regardingEntity ?? "(unknown)"
        }". Contact your admin.`,
      };
    case "repo-error":
      return {
        intent: "error",
        title: "Failed to load memos",
        body:
          props.repoError?.message ??
          "An unexpected error occurred while loading memos.",
      };
  }
}

/**
 * Renders a Fluent v9 MessageBar with a Close button for one of three FR-13
 * error states. Semantic tokens only per ADR-021.
 *
 * data-testid values are preserved from the previous placeholder divs
 * (`notepad-shell-invalid-launch` / `-unsupported` / `-repo-error`) so tests
 * from task 037 continue to pass.
 */
const NotepadErrorBanner: React.FC<INotepadErrorBannerProps> = (props) => {
  const styles = useStyles();
  const { intent, title, body } = composeErrorMessage(props);

  const testId =
    props.kind === "invalid-launch"
      ? "notepad-shell-invalid-launch"
      : props.kind === "unsupported"
        ? "notepad-shell-unsupported"
        : "notepad-shell-repo-error";

  return (
    <div
      className={styles.errorRoot}
      data-testid={testId}
      data-error-state={props.kind}
    >
      <div className={styles.errorBarWrapper}>
        <MessageBar intent={intent}>
          <MessageBarBody>
            <MessageBarTitle>{title}</MessageBarTitle>
            {body}
          </MessageBarBody>
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                icon={<DismissRegular />}
                onClick={handleNotepadClose}
                aria-label="Close"
                data-testid="notepad-error-close-icon"
              />
            }
          >
            <Button
              onClick={handleNotepadClose}
              data-testid="notepad-error-close-button"
            >
              Close
            </Button>
          </MessageBarActions>
        </MessageBar>
      </div>
    </div>
  );
};

NotepadErrorBanner.displayName = "NotepadErrorBanner";
