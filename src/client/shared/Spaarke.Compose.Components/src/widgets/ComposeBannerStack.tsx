/**
 * ComposeBannerStack.tsx — workspace banner stack (errors / warnings / status).
 *
 * Project:   spaarkeai-compose-r1
 * Extracted: R2 refactor (ComposeWorkspace.tsx 1795 → ~400 LOC) — pure render
 *            composition lifted to keep the orchestrator thin.
 *
 * Renders, in this order:
 *   1. Save error MessageBar         — when `errorMessage` is non-null.
 *   2. Cross-user 409 conflict banner (Task 050) — when `checkoutStatus === 'conflict'`.
 *   3. Non-fatal checkout failure banner — when `checkoutStatus === 'failed'`.
 *   4. Multi-tab cancelled banner (Task 051) — when `checkoutStatus === 'cancelled'`.
 *   5. Import warnings banner — when mammoth surfaced any warnings.
 *   6. Pending assistant draft banner (Flow 5) — when there is a staged draft.
 *
 * The whole stack renders only when at least one row would surface; the parent
 * decides whether to mount it at all. This keeps the DOM minimal.
 *
 * AI actions (Summarize etc.) render in the Assistant pane via chat
 * messages — this stack owns only CRUD/lifecycle status.
 *
 * Constraints:
 *   - ADR-021: Fluent v9 only; semantic tokens; no hex colors.
 *   - ADR-022: React 19; pure functional component.
 *
 * @see ./ComposeWorkspace.tsx (consumer)
 * @see ./ComposeWorkspace.types.ts (state shape)
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  MessageBarActions,
  Button,
} from '@fluentui/react-components';
import { Dismiss16Regular, DocumentRegular } from '@fluentui/react-icons';

import type { ComposeCheckoutLockedByInfo, ComposeCheckoutStatus } from './ComposeWorkspace.types';
import type { ComposeAssistantToWorkspaceFlow } from '../types/compose-contracts';

export interface ComposeBannerStackProps {
  errorMessage: string | null;
  checkoutStatus: ComposeCheckoutStatus;
  checkoutLockedBy: ComposeCheckoutLockedByInfo | null;
  checkoutFailureMessage: string | null;
  importWarnings: Array<{ type: string; message: string }>;
  pendingAssistantInsert: ComposeAssistantToWorkspaceFlow | null;
  /**
   * UAT #7 (compose-r2): a monotonically-incrementing token bumped by the parent on every
   * successful Save. A CHANGE in value (not its magnitude) surfaces a transient "Saved ✓"
   * MessageBar that auto-dismisses after {@link SAVE_SUCCESS_VISIBLE_MS}. 0 = no save yet.
   */
  saveSuccessToken?: number;

  /**
   * spaarkeai-compose-r2 #1(b) — the `sprk_documentid` of the document persisted by the last
   * successful Save (create-on-save "Add to DMS" or a replace Save). When present alongside
   * {@link onOpenPreview}, the Saved ✓ banner surfaces an "Open preview" affordance that opens the
   * File Preview modal for THAT document (not a workspace mount). Undefined until a Save yields an id.
   */
  savedDocumentId?: string;

  /**
   * spaarkeai-compose-r2 #1(b) — invoked when the user clicks "Open preview" on the Saved ✓ banner.
   * The parent (ComposeWorkspace, workspace-side) opens the File Preview modal for
   * {@link savedDocumentId}. Absent → the affordance is not rendered (back-compat: pre-existing
   * callers that only pass `saveSuccessToken` get the unchanged banner).
   */
  onOpenPreview?: () => void;
}

/** How long the transient "Saved ✓" confirmation stays up before auto-dismissing. */
const SAVE_SUCCESS_VISIBLE_MS = 4000;

const useStyles = makeStyles({
  bannerStack: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
});

export function ComposeBannerStack(props: ComposeBannerStackProps): React.JSX.Element | null {
  const styles = useStyles();
  const {
    errorMessage,
    checkoutStatus,
    checkoutLockedBy,
    checkoutFailureMessage,
    importWarnings,
    pendingAssistantInsert,
    saveSuccessToken = 0,
    savedDocumentId,
    onOpenPreview,
  } = props;

  // DEF-15 (UAT-R3): the "Document opened with N simplification(s)" warning is
  // informational and permanent-until-dismissed. Per-mount dismissal is enough
  // (owner: "it need not persist across mounts") — a plain local flag. It resets
  // whenever a NEW set of import warnings arrives (a fresh document load hands a
  // new `importWarnings` array reference) so a later, genuinely-different import
  // is not silently swallowed by a stale dismissal.
  const [importWarningsDismissed, setImportWarningsDismissed] = React.useState(false);
  React.useEffect(() => {
    setImportWarningsDismissed(false);
  }, [importWarnings]);

  const showImportWarnings = importWarnings.length > 0 && !importWarningsDismissed;

  // UAT #7: a successful Save previously showed no confirmation — the button flipped from
  // "Saving" back to idle silently. Surface a transient success MessageBar whenever the parent
  // bumps `saveSuccessToken`, auto-dismissing after SAVE_SUCCESS_VISIBLE_MS. Keyed on the token
  // value (not a boolean) so a second identical Save re-triggers the banner. An in-flight save
  // error (a fresh `errorMessage`) suppresses the stale success row.
  const [showSaveSuccess, setShowSaveSuccess] = React.useState(false);
  React.useEffect(() => {
    if (saveSuccessToken <= 0) return;
    setShowSaveSuccess(true);
    const timer = setTimeout(() => setShowSaveSuccess(false), SAVE_SUCCESS_VISIBLE_MS);
    return () => clearTimeout(timer);
  }, [saveSuccessToken]);

  const showSaveSuccessBanner = showSaveSuccess && !errorMessage;

  const showStack =
    showImportWarnings ||
    !!errorMessage ||
    !!pendingAssistantInsert ||
    showSaveSuccessBanner ||
    checkoutStatus === 'conflict' ||
    checkoutStatus === 'failed' ||
    checkoutStatus === 'cancelled';

  if (!showStack) return null;

  return (
    <div className={styles.bannerStack}>
      {showSaveSuccessBanner ? (
        <MessageBar intent="success" data-testid="compose-workspace-save-success-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Saved to matter files</MessageBarTitle>
            Your document was saved and is available in the matter&apos;s files.
          </MessageBarBody>
          {/* #1(b): when the just-saved document's id is known, offer an "Open preview" link that
              opens the File Preview modal for THAT document (parent-owned; not a workspace mount). */}
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-save-success-dismiss"
                onClick={() => setShowSaveSuccess(false)}
              />
            }
          >
            {onOpenPreview && savedDocumentId ? (
              <Button
                appearance="transparent"
                icon={<DocumentRegular />}
                data-testid="compose-workspace-save-success-open-preview"
                onClick={onOpenPreview}
              >
                Open preview
              </Button>
            ) : null}
          </MessageBarActions>
        </MessageBar>
      ) : null}

      {errorMessage ? (
        <MessageBar intent="error" data-testid="compose-workspace-error-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Save error</MessageBarTitle>
            {errorMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'conflict' && checkoutLockedBy ? (
        <MessageBar intent="warning" data-testid="compose-workspace-checkout-conflict-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Document is checked out</MessageBarTitle>
            {checkoutLockedBy.checkedOutAt
              ? `Locked by ${checkoutLockedBy.name} since ${new Date(checkoutLockedBy.checkedOutAt).toLocaleString()}. You can view the document but changes cannot be saved until the lock is released.`
              : `Locked by ${checkoutLockedBy.name}. You can view the document but changes cannot be saved until the lock is released.`}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'failed' && checkoutFailureMessage ? (
        <MessageBar intent="info" data-testid="compose-workspace-checkout-failed-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Lock not acquired</MessageBarTitle>
            {checkoutFailureMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'cancelled' ? (
        <MessageBar intent="info" data-testid="compose-workspace-checkout-cancelled-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>This session is no longer active</MessageBarTitle>
            This document is open in another Compose session. Refresh this page to attempt to acquire the lock again, or
            close this tab.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {showImportWarnings ? (
        <MessageBar intent="warning" data-testid="compose-workspace-import-warning-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Document opened with {importWarnings.length} simplification(s)</MessageBarTitle>
            Some advanced features may not be preserved on save.
          </MessageBarBody>
          {/* DEF-15: Fluent v9's MessageBar dismiss affordance — the trailing
              container action. Clears the banner for this mount only. */}
          <MessageBarActions
            containerAction={
              <Button
                appearance="transparent"
                aria-label="Dismiss"
                icon={<Dismiss16Regular />}
                data-testid="compose-workspace-import-warning-dismiss"
                onClick={() => setImportWarningsDismissed(true)}
              />
            }
          />
        </MessageBar>
      ) : null}

      {pendingAssistantInsert ? (
        <MessageBar intent="info" data-testid="compose-workspace-pending-assistant-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Assistant draft ready</MessageBarTitle>A draft from the Assistant is staged for insertion.
            (R2 wires the insert action; R1 acknowledges receipt only.)
          </MessageBarBody>
        </MessageBar>
      ) : null}
    </div>
  );
}
