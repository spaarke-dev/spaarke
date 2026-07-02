/**
 * ComposeToolbar.tsx — Compose workspace command-bar (FR-12 + FR-20 dispatch surface).
 *
 * Renders three Fluent v9 toolbar buttons:
 *   1. Open in Word for Web      — reuses `useDocumentActions.openInWeb` from
 *      `@spaarke/document-operations` (extracted in task 031 / SemanticSearch
 *      precedent).
 *   2. Open in Word Desktop      — reuses `useDocumentActions.openInDesktop`.
 *   3. Summarize with Assistant  — dispatches a PaneEventBus event on the
 *      `conversation` channel (additive discriminant per ADR-030; receiver is
 *      the Assistant pane stub in R1 per FR-20; Phase 6 smoke test wires the
 *      `compose-summarize` BFF dispatch via task 060/061).
 *
 * R1 responsibility split (deliberate per POML 043 + design.md):
 *   - This toolbar is PRESENTATIONAL + ACTION-DISPATCH only. It does NOT own
 *     DOCX state. Document editing controls (bold, italic, headings, lists,
 *     alignment, link) live on the EDITOR's own toolbar layer (task 045's
 *     `ComposeEditor.tsx` inside `@spaarke/compose-components`). That is
 *     intentional separation: this toolbar is the workspace-level command
 *     bar (Open-in-Word + AI dispatch); the editor toolbar is the
 *     prose-mirror-level formatting bar. See design.md §5 + Spike #1 §3.1.
 *
 * Wiring:
 *   - The `documentId` prop is the Compose document pointer (SPE drive-item id
 *     for ephemeral docs per Spike #1 §3.2; `sprk_documentid` after first-Save
 *     promotion per CLAUDE.md "ChatSession binding strategy"). Both flow
 *     through the same `useDocumentActions` hook because the open-links BFF
 *     endpoint handles both id shapes (see Spike #3 §2.3 + design.md §6).
 *   - The `sessionId` prop is the active ChatSession id; emitted on every
 *     dispatched event so the Assistant pane can correlate.
 *   - The `bffBaseUrl` is resolved by the host (ComposeWorkspace.tsx in
 *     task 042 will pass it from `getBffBaseUrl()` per the host-init pattern
 *     in `src/solutions/SpaarkeAi/src/main.tsx` runtime-config bootstrap).
 *
 * Standards (binding):
 *   - ADR-021 (Fluent UI v9 + semantic tokens; dark-mode parity)
 *   - ADR-028 (Spaarke Auth v2 — `useDocumentActions` uses `authenticatedFetch`
 *     from `@spaarke/auth` internally; this component handles NO auth tokens)
 *   - ADR-030 (typed PaneEventBus channels; additive discriminants tolerated)
 *   - ADR-012 (Fluent v9 component patterns; subtle appearance for action
 *     affordances)
 *   - CLAUDE.md §11 (component justification — see task POML <justification>)
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: SemanticSearch's `SearchCommandBar.tsx` has the SAME Open-in-
 *     Word buttons. Hook extracted to `@spaarke/document-operations` in
 *     task 031.
 *   - Extension: Compose has a distinct toolbar (different action set; no
 *     selection/delete; adds compose-summarize dispatch). A separate component
 *     is needed; the shared hook is the reuse surface.
 *   - Cost-of-doing-nothing: FR-12 fails — Compose has no Open-in-Word entry
 *     points; FR-20 smoke test cannot fire `compose-summarize` from the UI.
 *
 * @see projects/spaarkeai-compose-r1/tasks/043-frontend-create-compose-toolbar.poml
 * @see projects/spaarkeai-compose-r1/spec.md FR-12, FR-19, FR-20
 * @see projects/spaarkeai-compose-r1/notes/spikes/spike-1-tiptap-docx-roundtrip.md §3.1
 * @see src/client/shared/Spaarke.DocumentOperations/src/hooks/useDocumentActions.ts
 * @see src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts (ConversationPaneEvent)
 * @see src/solutions/SpaarkeAi/src/types/compose-contracts.ts (Flow 2 contract shape)
 * @see src/solutions/SpaarkeAi/src/components/workspace/SendToWorkspaceButton.tsx (dispatch pattern)
 */

import * as React from 'react';
import {
  Toolbar,
  ToolbarButton,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  OpenRegular,
  DesktopRegular,
  SparkleRegular,
  SaveRegular,
} from '@fluentui/react-icons';
import { useDispatchPaneEvent } from '@spaarke/ai-widgets/events';
import { useDocumentActions } from '@spaarke/document-operations';

// ---------------------------------------------------------------------------
// Public contract — Compose-summarize dispatch shape
// ---------------------------------------------------------------------------

/**
 * Additive discriminant on the `conversation` channel for the user-initiated
 * compose-summarize action. Per ADR-030, additive discriminants are tolerated
 * by existing subscribers (they narrow on `event.type` before reading
 * discriminant-specific fields).
 *
 * Aligns with the contracts pattern in
 * `src/solutions/SpaarkeAi/src/types/compose-contracts.ts` (Flow 2/4 shape):
 *   - `type` is the discriminant
 *   - `documentRef` carries the document pointer
 *   - `jpsScope` is the registered JPS scope name (`compose-document` for
 *     whole-doc summarize per spec.md FR-08 + notes/jps-scopes/)
 *   - `sessionId` + `timestamp` are correlation metadata
 *
 * R1 receiver: Assistant pane stub (logs payload). R2+ binds to the
 * BFF `POST /api/compose/action/compose-summarize` dispatch (Phase 6 smoke
 * test in tasks 060/061). The CONTRACT shape is the deliverable here;
 * runtime wiring lands in those tasks.
 *
 * @see projects/spaarkeai-compose-r1/notes/jps-scopes/compose-document.scope.json
 */
export interface ComposeSummarizeRequestEvent {
  /** Always 'compose_summarize_request' — additive on `conversation` channel. */
  type: 'compose_summarize_request';
  /**
   * Document pointer. `documentId` is the SPE drive-item id (for ephemeral
   * Path B docs) OR the `sprk_documentid` (after first-Save promotion). The
   * BFF `/api/compose/action/compose-summarize` endpoint handles both shapes.
   *
   * Task 098 (Phase 9) note: for Path B ephemeral docs, `documentId` MUST be
   * the SPE drive-item id (not the sprk_documentid) because the BFF's Load
   * endpoint keys on `documentSpeId`. Post-promotion, the payload adds the
   * `sprkDocumentId` field so the ConversationPane consumer forwards it to
   * the BFF as `documentRecordId` (Dataverse correlation).
   */
  documentRef: {
    documentId: string;
    sprkDocumentId?: string;
    fileName?: string;
  };
  /**
   * JPS scope name binding the request to the registered playbook scope.
   * R1: always `'compose-document'` per spec.md FR-08.
   */
  jpsScope: 'compose-document';
  /** Active ChatSession id (Tier 1 safe identifier). */
  sessionId: string;
  /**
   * SPE driveId — required query param for the BFF Load endpoint. Added in
   * task 098 (Phase 9) so ConversationPane can invoke the BFF directly.
   */
  driveId: string;
  /**
   * Microsoft Entra tenant id (ADR-015 Tier 3 scoping). Added in task 098.
   */
  tenantId: string;
  /** ISO-8601 UTC timestamp. */
  timestamp: string;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ComposeToolbarProps {
  /**
   * The active Compose document identifier.
   *
   * Either the SPE drive-item id (ephemeral / pre-first-Save docs per
   * design.md §8 Path B) or the `sprk_documentid` GUID (post-promotion). The
   * Open-in-Word buttons pass this verbatim to `useDocumentActions`; the
   * BFF `/api/documents/{id}/open-links` endpoint resolves both shapes.
   *
   * Empty string disables the Open-in-Word buttons + summarize button.
   */
  documentId: string;

  /**
   * Human-readable file name for the active document. Optional; emitted on
   * the `compose_summarize_request` event so the Assistant pane can render
   * a recognizable label.
   */
  fileName?: string;

  /**
   * Active ChatSession id correlating this toolbar's events to the Compose
   * session (existing ChatSession infrastructure per design.md §6).
   * Required for the dispatch payload's correlation field.
   *
   * Empty string disables the summarize button (no session to correlate).
   */
  sessionId: string;

  /**
   * BFF base URL (host only, e.g. `https://host.azurewebsites.net`). Passed
   * to `useDocumentActions` per the hook's `bffBaseUrl` contract (task 031
   * decoupling). Host resolves via `getBffBaseUrl()` from the runtime-config
   * bootstrap (see `src/solutions/SpaarkeAi/src/main.tsx`).
   *
   * Empty string disables all actions.
   */
  bffBaseUrl: string;

  /**
   * SPE driveId of the container that holds the Compose document. Added in
   * task 098 (Phase 9) — carried into the `compose_summarize_request` event
   * so ConversationPane can invoke `POST /api/compose/action/compose-summarize`
   * with the required `driveId` body field.
   *
   * Empty string disables the summarize button.
   */
  driveId: string;

  /**
   * Microsoft Entra tenant id (ADR-015 Tier 3 scoping). Added in task 098 —
   * carried into the `compose_summarize_request` event so ConversationPane
   * can invoke the BFF with the required `tenantId` body field.
   *
   * Empty string disables the summarize button.
   */
  tenantId: string;

  /**
   * Optional `sprk_documentid` GUID (post-promotion). When present, forwarded
   * on the `compose_summarize_request` event so ConversationPane can pass it
   * to the BFF as `documentRecordId` (Dataverse correlation). Task 098.
   */
  sprkDocumentId?: string;

  /**
   * Disable the toolbar entirely (e.g. while the host is hydrating the
   * editor state or no document is loaded). When `true`, all three buttons
   * are non-interactive.
   */
  disabled?: boolean;

  /**
   * Optional className applied to the underlying Fluent v9 `Toolbar` root.
   * The host may pass margins / positioning styles without forcing the
   * toolbar to re-export Fluent's makeStyles surface.
   */
  className?: string;

  /**
   * Optional observer callback. Fires AFTER the compose-summarize event has
   * been dispatched on the PaneEventBus. Receives the same payload the bus
   * carried (so the caller can correlate / surface a toast / focus the
   * Assistant pane).
   *
   * Component does NOT await this callback — the bus dispatch is the
   * primary effect.
   */
  onComposeSummarizeRequest?: (payload: ComposeSummarizeRequestEvent) => void;

  /**
   * Save handler. When provided, the toolbar renders a Save button that
   * calls this on click (in addition to Ctrl+S which stays active in the
   * host). Omit to hide the Save button.
   */
  onSaveRequested?: () => void;

  /**
   * True when the document has unsaved changes. Drives Save button visual
   * state — enabled when dirty, disabled when clean. Ignored when
   * `onSaveRequested` is omitted.
   */
  isDirty?: boolean;

  /**
   * True while a save is in flight. Ignored when `onSaveRequested` is
   * omitted. The parent typically flips `disabled` as well; kept separate
   * so the label can read "Saving…" specifically.
   */
  isSaving?: boolean;
}

// ---------------------------------------------------------------------------
// Styles — ADR-021 semantic tokens only; no hard-coded hex
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  /**
   * Toolbar root. `width: fit-content` mirrors `SearchCommandBar.tsx`'s
   * approach — Fluent v9 Toolbar defaults to stretch as a flex child; this
   * overrides so the parent's `justifyContent: flex-end` (when present) can
   * right-align the toolbar. Host can override via `className` prop.
   */
  toolbar: {
    width: 'fit-content',
    columnGap: tokens.spacingHorizontalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    paddingInline: tokens.spacingHorizontalS,
  },
  /**
   * Visual emphasis on the AI dispatch button — it is the highest-value
   * action on the toolbar. Uses `colorBrandForeground1` (semantic) for the
   * icon tint so dark-mode parity is preserved.
   */
  summarizeIcon: {
    color: tokens.colorBrandForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Compose workspace toolbar. Renders three Fluent v9 toolbar buttons:
 *
 * - **Open in Word for Web** — opens the document in Word Online via the
 *   shared-lib hook + `/api/documents/{id}/open-links`.
 * - **Open in Word Desktop** — opens the document in the desktop Word app
 *   (or falls back to web URL if no desktop protocol URL is available).
 * - **Summarize with Assistant** — dispatches a PaneEventBus event for the
 *   Assistant pane to consume (R1: stub receiver logs the payload;
 *   Phase 6 wires the BFF dispatch).
 *
 * Each button carries a Fluent v9 `Tooltip` + ARIA label for accessibility.
 *
 * The summarize button uses a `SparkleRegular` icon (Fluent's AI-action
 * convention) tinted with `colorBrandForeground1` to visually distinguish it
 * from the Word-handoff buttons.
 *
 * @see ComposeToolbarProps for the controlled-component contract.
 */
export function ComposeToolbar(props: ComposeToolbarProps): React.JSX.Element {
  const styles = useStyles();
  const {
    documentId,
    fileName,
    sessionId,
    bffBaseUrl,
    driveId,
    tenantId,
    sprkDocumentId,
    disabled,
    className,
    onComposeSummarizeRequest,
    onSaveRequested,
    isDirty = false,
    isSaving = false,
  } = props;

  // Shared-lib hook — reuse the SemanticSearch precedent. Per ADR-013 + the
  // task POML constraint, do NOT call `/api/documents/.../open-links`
  // directly; route through this hook so behavior stays single-source-of-
  // truth with SemanticSearch.
  const { openInWeb, openInDesktop, isActing } = useDocumentActions({
    bffBaseUrl,
  });

  const dispatch = useDispatchPaneEvent();

  // Disabled-state logic:
  // - Open-in-Word buttons require a document AND bff URL AND not currently
  //   in flight on another action (debounce).
  // - Summarize button additionally requires a session id (so the dispatch
  //   payload's correlation field is meaningful).
  const isToolbarDisabled = disabled === true;
  const hasDocument = documentId.length > 0 && bffBaseUrl.length > 0;
  const hasSession = sessionId.length > 0;
  // Task 098 (Phase 9): the summarize action ALSO requires driveId + tenantId
  // now — the event carries them so ConversationPane can invoke the BFF
  // without re-resolving. If either is missing, the button is inert.
  const hasSummarizeContext = driveId.length > 0 && tenantId.length > 0;

  const openInWebDisabled = isToolbarDisabled || !hasDocument || isActing;
  const openInDesktopDisabled = isToolbarDisabled || !hasDocument || isActing;
  const summarizeDisabled =
    isToolbarDisabled || !hasDocument || !hasSession || !hasSummarizeContext;

  const handleOpenInWeb = React.useCallback((): void => {
    if (openInWebDisabled) return;
    void openInWeb(documentId);
  }, [openInWebDisabled, openInWeb, documentId]);

  const handleOpenInDesktop = React.useCallback((): void => {
    if (openInDesktopDisabled) return;
    void openInDesktop(documentId);
  }, [openInDesktopDisabled, openInDesktop, documentId]);

  const handleSummarizeRequest = React.useCallback((): void => {
    if (summarizeDisabled) return;

    const payload: ComposeSummarizeRequestEvent = {
      type: 'compose_summarize_request',
      documentRef: {
        documentId,
        sprkDocumentId,
        fileName,
      },
      jpsScope: 'compose-document',
      sessionId,
      driveId,
      tenantId,
      timestamp: new Date().toISOString(),
    };

    // Additive discriminant on `conversation` channel. ConversationPaneEvent's
    // typed union does not include `'compose_summarize_request'` (additive
    // per ADR-030 — file at PaneEventTypes.ts:1117 enumerates a closed set of
    // R1 discriminants; Compose dispatches extend that set without modifying
    // the file). The double-cast is the canonical escape hatch for additive
    // dispatch in this codebase — see `compose-contracts.ts` §"IMPORTANT —
    // additive contract" and `SendToWorkspaceButton.tsx` for the precedent
    // (workspace.widget_load + custom widgetData shape).
    dispatch(
      'conversation',
      payload as unknown as Parameters<typeof dispatch<'conversation'>>[1],
    );

    onComposeSummarizeRequest?.(payload);
  }, [
    summarizeDisabled,
    dispatch,
    documentId,
    sprkDocumentId,
    fileName,
    sessionId,
    driveId,
    tenantId,
    onComposeSummarizeRequest,
  ]);

  return (
    <Toolbar
      className={mergeClasses(styles.toolbar, className)}
      size="small"
      aria-label="Compose document actions"
    >
      <Tooltip
        content={
          hasDocument
            ? 'Open this document in Word for the Web'
            : 'No document loaded'
        }
        relationship="label"
      >
        <ToolbarButton
          icon={<OpenRegular />}
          disabled={openInWebDisabled}
          onClick={handleOpenInWeb}
          aria-label="Open in Word for Web"
        >
          Open in Word Web
        </ToolbarButton>
      </Tooltip>

      <Tooltip
        content={
          hasDocument
            ? 'Open this document in the Word desktop app'
            : 'No document loaded'
        }
        relationship="label"
      >
        <ToolbarButton
          icon={<DesktopRegular />}
          disabled={openInDesktopDisabled}
          onClick={handleOpenInDesktop}
          aria-label="Open in Word Desktop"
        >
          Open in Word Desktop
        </ToolbarButton>
      </Tooltip>

      <Tooltip
        content={
          summarizeDisabled
            ? hasDocument
              ? 'Session not ready — wait a moment'
              : 'No document loaded'
            : 'Summarize this document with the Assistant'
        }
        relationship="label"
      >
        <ToolbarButton
          icon={<SparkleRegular className={styles.summarizeIcon} />}
          disabled={summarizeDisabled}
          onClick={handleSummarizeRequest}
          aria-label="Summarize with Assistant"
        >
          Summarize
        </ToolbarButton>
      </Tooltip>

      {onSaveRequested ? (
        <Tooltip
          content={
            isToolbarDisabled || !hasDocument
              ? 'No document loaded'
              : isSaving
                ? 'Saving…'
                : isDirty
                  ? 'Save changes (Ctrl+S)'
                  : 'No unsaved changes'
          }
          relationship="label"
        >
          <ToolbarButton
            icon={<SaveRegular />}
            disabled={isToolbarDisabled || !hasDocument || isSaving || !isDirty}
            onClick={onSaveRequested}
            aria-label={isSaving ? 'Saving' : 'Save changes'}
          >
            {isSaving ? 'Saving…' : 'Save'}
          </ToolbarButton>
        </Tooltip>
      ) : null}
    </Toolbar>
  );
}
