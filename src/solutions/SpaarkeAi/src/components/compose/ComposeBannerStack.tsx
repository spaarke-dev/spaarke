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
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Button,
  Text,
  Spinner,
} from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';

import type {
  ComposeCheckoutLockedByInfo,
  ComposeCheckoutStatus,
} from './ComposeWorkspace.types';
import type { ComposeAssistantToWorkspaceFlow } from '../../types/compose-contracts';

export interface ComposeBannerStackProps {
  errorMessage: string | null;
  checkoutStatus: ComposeCheckoutStatus;
  checkoutLockedBy: ComposeCheckoutLockedByInfo | null;
  checkoutFailureMessage: string | null;
  importWarnings: Array<{ type: string; message: string }>;
  pendingAssistantInsert: ComposeAssistantToWorkspaceFlow | null;
  summaryStatus?: 'idle' | 'in-flight' | 'ready' | 'error';
  summaryText?: string | null;
  summaryError?: string | null;
  onDismissSummary?: () => void;
}

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
    summaryStatus = 'idle',
    summaryText = null,
    summaryError = null,
    onDismissSummary,
  } = props;

  const showStack =
    importWarnings.length > 0 ||
    !!errorMessage ||
    !!pendingAssistantInsert ||
    summaryStatus !== 'idle' ||
    checkoutStatus === 'conflict' ||
    checkoutStatus === 'failed' ||
    checkoutStatus === 'cancelled';

  if (!showStack) return null;

  return (
    <div className={styles.bannerStack}>
      {errorMessage ? (
        <MessageBar intent="error" data-testid="compose-workspace-error-banner" aria-live="polite">
          <MessageBarBody>
            <MessageBarTitle>Save error</MessageBarTitle>
            {errorMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'conflict' && checkoutLockedBy ? (
        <MessageBar
          intent="warning"
          data-testid="compose-workspace-checkout-conflict-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>Document is checked out</MessageBarTitle>
            {checkoutLockedBy.checkedOutAt
              ? `Locked by ${checkoutLockedBy.name} since ${new Date(checkoutLockedBy.checkedOutAt).toLocaleString()}. You can view the document but changes cannot be saved until the lock is released.`
              : `Locked by ${checkoutLockedBy.name}. You can view the document but changes cannot be saved until the lock is released.`}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'failed' && checkoutFailureMessage ? (
        <MessageBar
          intent="info"
          data-testid="compose-workspace-checkout-failed-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>Lock not acquired</MessageBarTitle>
            {checkoutFailureMessage}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {checkoutStatus === 'cancelled' ? (
        <MessageBar
          intent="info"
          data-testid="compose-workspace-checkout-cancelled-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>This session is no longer active</MessageBarTitle>
            This document is open in another Compose session. Refresh this page to attempt to
            acquire the lock again, or close this tab.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {importWarnings.length > 0 ? (
        <MessageBar
          intent="warning"
          data-testid="compose-workspace-import-warning-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>
              Document opened with {importWarnings.length} simplification(s)
            </MessageBarTitle>
            Some advanced features may not be preserved on save.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {pendingAssistantInsert ? (
        <MessageBar
          intent="info"
          data-testid="compose-workspace-pending-assistant-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>Assistant draft ready</MessageBarTitle>
            A draft from the Assistant is staged for insertion. (R2 wires the insert action; R1
            acknowledges receipt only.)
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {summaryStatus === 'in-flight' ? (
        <MessageBar
          intent="info"
          data-testid="compose-workspace-summary-in-flight-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>
              <Spinner size="tiny" style={{ display: 'inline-block', marginInlineEnd: tokens.spacingHorizontalXS }} />
              Summarizing document…
            </MessageBarTitle>
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {summaryStatus === 'ready' && summaryText ? (
        <MessageBar
          intent="success"
          data-testid="compose-workspace-summary-ready-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>Document summary</MessageBarTitle>
            <Text style={{ whiteSpace: 'pre-wrap' }}>{summaryText}</Text>
          </MessageBarBody>
          {onDismissSummary ? (
            <MessageBarActions
              containerAction={
                <Button
                  appearance="transparent"
                  icon={<DismissRegular />}
                  aria-label="Dismiss summary"
                  onClick={onDismissSummary}
                />
              }
            />
          ) : null}
        </MessageBar>
      ) : null}

      {summaryStatus === 'error' && summaryError ? (
        <MessageBar
          intent="error"
          data-testid="compose-workspace-summary-error-banner"
          aria-live="polite"
        >
          <MessageBarBody>
            <MessageBarTitle>Summarize failed</MessageBarTitle>
            {summaryError}
          </MessageBarBody>
          {onDismissSummary ? (
            <MessageBarActions
              containerAction={
                <Button
                  appearance="transparent"
                  icon={<DismissRegular />}
                  aria-label="Dismiss summary error"
                  onClick={onDismissSummary}
                />
              }
            />
          ) : null}
        </MessageBar>
      ) : null}
    </div>
  );
}
