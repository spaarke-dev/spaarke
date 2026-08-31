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
 * FR-C07 (r8 task 053) — name WHAT re-attached, not just how many things did.
 *
 * `ReanchoredAnnotation.type` is the annotation kind the BFF carries through (`'comment'`,
 * `'insertion-suggestion'`, `'deletion-suggestion'`, …). It was already on the wire and already
 * mirrored in `ComposeReanchor.types.ts`; nothing here asks the server for anything new — this is the
 * "deterministic information available at capture time MUST be carried, not re-derived" invariant (7)
 * applied to a message.
 *
 * Buckets are deliberately coarse: a comment is a comment, and every suggestion kind reads to the user
 * as a tracked change. An unrecognized kind falls into "annotations" rather than being dropped.
 */
function describeKinds(annotations: readonly { type: string }[]): string {
  let comments = 0;
  let trackedChanges = 0;
  let other = 0;
  for (const a of annotations) {
    const kind = (a.type ?? '').toLowerCase();
    if (kind.includes('comment')) comments += 1;
    else if (kind.includes('insertion') || kind.includes('deletion') || kind.includes('suggestion'))
      trackedChanges += 1;
    else other += 1;
  }
  const parts: string[] = [];
  if (comments > 0) parts.push(`${comments} comment${comments === 1 ? '' : 's'}`);
  if (trackedChanges > 0) parts.push(`${trackedChanges} tracked change${trackedChanges === 1 ? '' : 's'}`);
  if (other > 0) parts.push(`${other} annotation${other === 1 ? '' : 's'}`);
  if (parts.length === 0) return '';
  if (parts.length === 1) return parts[0];
  return `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`;
}

/**
 * Human-readable summary of the return-from-Word re-anchor pass.
 *
 * FR-C07 (r8 task 053): this is the ONE place a fuzzy match is still allowed to speak to the user, so
 * it has to say what actually happened rather than gesture at it. Word regenerates `w14:paraId`s on
 * save (Open-XML-SDK #925), so anchors from an externally edited document are genuinely re-located by
 * similarity — an honest, ADR-sanctioned use of fuzzy matching (`AnnotationReanchorService`, a KEEP
 * asset). The message therefore leads with the CAUSE ("this document was edited in Word"), names WHAT
 * re-attached by kind, and separates what still needs a human.
 *
 * `AnnotationReanchorService`'s BEHAVIOUR is untouched by this task — bands, thresholds, the ambiguity
 * guard and the never-silently-drop rule are exactly as they were. Only the sentence changed, and it
 * is composed here on the client from fields the summary already carried.
 *
 * Orphans stay folded into the "needs attention" count so no anchor is ever hidden (FR-27).
 */
function summarize(summary: ReanchorSummary): { title: string; needsAttention: number } {
  const needsAttention = summary.reviewCount + summary.orphanCount;
  const annotations = summary.annotations ?? [];
  const autoAnnotations = annotations.filter(a => a.band === 'auto');
  // Fall back to the bare count if the per-annotation detail is missing (an older/partial payload):
  // "4 re-attached" is less specific but still true, which beats asserting a composition we don't have.
  const reAttachedDetail = describeKinds(autoAnnotations) || `${summary.autoCount}`;
  const reAttachedPhrase = `${reAttachedDetail} re-attached to ${
    summary.autoCount === 1 ? 'its paragraph' : 'their paragraphs'
  }`;

  const attentionPhrase =
    summary.orphanCount > 0
      ? `${summary.reviewCount} need review and ${summary.orphanCount} couldn't be re-attached`
      : `${summary.reviewCount} need review`;

  const prefix = 'This document was edited in Word';
  const title =
    summary.autoCount === 0
      ? needsAttention > 0
        ? `${prefix} — nothing re-attached automatically; ${attentionPhrase}`
        : `${prefix} — there was nothing to re-attach`
      : needsAttention > 0
        ? `${prefix} — ${reAttachedPhrase}; ${attentionPhrase}`
        : `${prefix} — ${reAttachedPhrase}, and nothing needs your attention`;

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
