/**
 * ComposeReanchorBanner.tsx — return-from-Word re-anchor summary banner (FR-27, task 054).
 *
 * Project: spaarkeai-compose-r2, task 054 (Phase 5 Word).
 *
 * Renders the Workspace banner design §2.5 calls for:
 *   "Document updated in Word — N re-anchored, M need review"
 * with a "Review changes" affordance that opens the conflict panel when anything needs attention
 * (review + orphan > 0). When everything re-anchored cleanly (review + orphan === 0) the banner is
 * a quiet success confirmation with no action button.
 *
 * Presentational only — the parent owns the summary (fetched via useComposeReanchor) and the
 * open-panel callback. This mirrors ComposeBannerStack's pure-render composition.
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: ComposeBannerStack renders CRUD/lifecycle status rows (save error, checkout
 *     conflict, import warnings). It does NOT model a re-anchor summary (per-band counts + a
 *     review affordance) and its rows are host-state-driven, not summary-driven.
 *   - Extension: a new row could be added to ComposeBannerStack, but the re-anchor banner carries
 *     a distinct payload (ReanchorSummary), a distinct action (open conflict panel), and pairs with
 *     ComposeReanchorConflictPanel — keeping it a focused sibling avoids widening BannerStack's prop
 *     surface with re-anchor concerns. A follow-on MAY compose this INTO BannerStack's stack.
 *   - Cost-of-doing-nothing: FR-27's "banner reports N re-anchored, M need review" fails; a Word
 *     round-trip silently drops or blindly re-applies prior annotations.
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 semantic tokens only — no hex literals; correct in light AND dark theme.
 *   - ADR-022: React 19 pure functional component.
 *
 * @see ./ComposeReanchor.types.ts (ReanchorSummary)
 * @see ./ComposeReanchorConflictPanel.tsx (the panel the "Review changes" button opens)
 * @see ./useComposeReanchor.ts (fetch hook that produces the summary)
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

import type { ReanchorSummary } from './ComposeReanchor.types';

export interface ComposeReanchorBannerProps {
  /**
   * The re-anchor summary to render. When `null`/`undefined`, the banner renders nothing (no Word
   * round-trip has been re-anchored this session).
   */
  summary: ReanchorSummary | null | undefined;
  /**
   * Opens the conflict panel to resolve flagged/orphaned anchors. Called from the "Review changes"
   * button (only shown when `reviewCount + orphanCount > 0`).
   */
  onReview: () => void;
  /**
   * Dismisses the banner (e.g. after the user has reviewed). Optional — when omitted the dismiss
   * affordance is not rendered.
   */
  onDismiss?: () => void;
}

const useStyles = makeStyles({
  root: {
    paddingInline: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    flexShrink: 0,
  },
});

/**
 * Human-readable count phrase. Uses the design's own wording ("re-anchored" / "need review") and
 * folds orphans into the "need attention" count so the user is told about EVERY anchor that isn't
 * silently placed — orphans are never hidden (FR-27).
 */
function summarize(summary: ReanchorSummary): { title: string; needsAttention: number } {
  const needsAttention = summary.reviewCount + summary.orphanCount;
  const reAnchored = summary.autoCount;

  const reAnchoredPhrase = `${reAnchored} re-anchored`;
  const attentionPhrase =
    summary.orphanCount > 0
      ? `${summary.reviewCount} need review, ${summary.orphanCount} orphaned`
      : `${summary.reviewCount} need review`;

  const title =
    needsAttention > 0
      ? `Document updated in Word — ${reAnchoredPhrase}, ${attentionPhrase}`
      : `Document updated in Word — ${reAnchoredPhrase}, all anchors kept`;

  return { title, needsAttention };
}

export function ComposeReanchorBanner(props: ComposeReanchorBannerProps): React.JSX.Element | null {
  const { summary, onReview, onDismiss } = props;
  const styles = useStyles();

  if (!summary || summary.total === 0) {
    return null;
  }

  const { title, needsAttention } = summarize(summary);
  const intent = needsAttention > 0 ? 'warning' : 'success';

  return (
    <div className={styles.root}>
      <MessageBar intent={intent} data-testid="compose-reanchor-banner" aria-live="polite">
        <MessageBarBody>
          <MessageBarTitle>Document updated in Word</MessageBarTitle>
          <span data-testid="compose-reanchor-banner-summary">{title}</span>
        </MessageBarBody>
        <MessageBarActions
          containerAction={
            onDismiss ? (
              <Button
                appearance="transparent"
                size="small"
                onClick={onDismiss}
                aria-label="Dismiss re-anchor banner"
                data-testid="compose-reanchor-banner-dismiss"
              >
                Dismiss
              </Button>
            ) : undefined
          }
        >
          {needsAttention > 0 ? (
            <Button appearance="primary" size="small" onClick={onReview} data-testid="compose-reanchor-banner-review">
              Review changes
            </Button>
          ) : null}
        </MessageBarActions>
      </MessageBar>
    </div>
  );
}

ComposeReanchorBanner.displayName = 'ComposeReanchorBanner';

export default ComposeReanchorBanner;
