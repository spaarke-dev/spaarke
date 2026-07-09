/**
 * ComposeReanchorConflictPanel.tsx — return-from-Word conflict UX (FR-27, task 054).
 *
 * Project: spaarkeai-compose-r2, task 054 (Phase 5 Word).
 *
 * The resolution surface behind the ComposeReanchorBanner's "Review changes" button. Lists every
 * anchor that did NOT auto-anchor — flagged (band `'review'`, incl. ambiguous near-ties) and
 * orphaned (band `'orphan'`) — and lets the user resolve each: Accept the proposed re-anchor, Keep
 * it pending, or Discard it. Discard is the ONLY path that removes an annotation (explicit user
 * action) — the engine never drops one silently (FR-27).
 *
 * Presentational only — it emits `ReanchorResolutionDecision`s via `onResolve`; the host applies
 * them against Compose session state (`anchoredAnnotations`). Mirrors ComposeConflictDialog's
 * host-callback pattern (that dialog is multi-tab checkout conflict; this is re-anchor conflict —
 * distinct concern, distinct payload).
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: ComposeConflictDialog handles the multi-tab CHECKOUT conflict (FR-16) — two fixed
 *     buttons, no per-item list, no re-anchor payload. It cannot model a variable-length list of
 *     flagged/orphaned anchors each with three resolution actions.
 *   - Extension: not viable — the checkout dialog's contract (verbatim FR-16 labels, alert-modal,
 *     non-dismissible) is unrelated to a browsable re-anchor conflict list.
 *   - Cost-of-doing-nothing: FR-27's "lets the user resolve flagged/orphaned anchors" fails;
 *     ambiguous anchors would have nowhere to be surfaced.
 *
 * Constraints honored (BINDING):
 *   - ADR-021: Fluent v9 semantic tokens only — no hex; correct in light AND dark theme.
 *   - ADR-022: React 19 functional component + hooks.
 *
 * @see ./ComposeReanchor.types.ts (ReanchorSummary, ReanchorResolutionDecision)
 * @see ./ComposeReanchorBanner.tsx (opens this panel)
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
  Badge,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';

import type {
  ReanchorSummary,
  ReanchoredAnnotation,
  ReanchorResolution,
  ReanchorResolutionDecision,
} from './ComposeReanchor.types';

export interface ComposeReanchorConflictPanelProps {
  /** Whether the panel is open. Host owns the open state. */
  open: boolean;
  /** The re-anchor summary whose flagged/orphaned anchors are listed. */
  summary: ReanchorSummary | null | undefined;
  /**
   * Called when the user resolves a single anchor (Accept / Keep / Discard). The host applies the
   * decision against Compose session state.
   */
  onResolve: (decision: ReanchorResolutionDecision) => void;
  /** Closes the panel. */
  onClose: () => void;
}

const useStyles = makeStyles({
  content: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    maxHeight: '60vh',
    overflowY: 'auto',
  },
  item: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  itemHeader: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
  },
  preview: {
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
  },
  meta: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
  actions: {
    display: 'flex',
    columnGap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
    marginTop: tokens.spacingVerticalXS,
  },
  empty: {
    color: tokens.colorNeutralForeground2,
  },
});

/** Badge intent + label per band. Orphans read as "danger" (needs a decision), review as "warning". */
function bandBadge(anchor: ReanchoredAnnotation): { color: 'warning' | 'danger'; label: string } {
  if (anchor.band === 'orphan') {
    return { color: 'danger', label: 'Orphaned' };
  }
  return { color: 'warning', label: anchor.ambiguous ? 'Ambiguous' : 'Needs review' };
}

function confidencePercent(confidence: number): string {
  return `${Math.round(confidence * 100)}% confidence`;
}

export function ComposeReanchorConflictPanel(
  props: ComposeReanchorConflictPanelProps
): React.JSX.Element {
  const { open, summary, onResolve, onClose } = props;
  const styles = useStyles();

  // Only anchors that did NOT auto-anchor need resolution.
  const unresolved = React.useMemo<ReanchoredAnnotation[]>(
    () => (summary?.annotations ?? []).filter((a) => a.band !== 'auto'),
    [summary]
  );

  const resolve = React.useCallback(
    (annotationId: string, resolution: ReanchorResolution): void =>
      onResolve({ annotationId, resolution }),
    [onResolve]
  );

  return (
    <Dialog open={open} onOpenChange={(_, data) => !data.open && onClose()}>
      <DialogSurface
        data-testid="compose-reanchor-conflict-panel"
        aria-labelledby="compose-reanchor-conflict-title"
      >
        <DialogBody>
          <DialogTitle id="compose-reanchor-conflict-title">Resolve re-anchored annotations</DialogTitle>
          <DialogContent className={styles.content}>
            {unresolved.length === 0 ? (
              <Text className={styles.empty} data-testid="compose-reanchor-conflict-empty">
                All annotations re-anchored cleanly — nothing needs your review.
              </Text>
            ) : (
              unresolved.map((anchor) => {
                const badge = bandBadge(anchor);
                const isOrphan = anchor.band === 'orphan';
                return (
                  <div
                    key={anchor.id}
                    className={styles.item}
                    data-testid={`compose-reanchor-conflict-item-${anchor.id}`}
                  >
                    <div className={styles.itemHeader}>
                      <Badge
                        appearance="filled"
                        color={badge.color}
                        data-testid={`compose-reanchor-conflict-band-${anchor.id}`}
                      >
                        {badge.label}
                      </Badge>
                      <Text className={styles.meta}>
                        {anchor.type} · {confidencePercent(anchor.confidence)}
                      </Text>
                    </div>

                    {anchor.preview ? (
                      <Text className={styles.preview}>{anchor.preview}</Text>
                    ) : null}

                    {isOrphan ? (
                      <Text className={styles.meta}>
                        No confident match in the updated document. Keep it pending or discard it.
                      </Text>
                    ) : (
                      <Text className={styles.meta}>
                        {anchor.matchedParagraphPreview
                          ? `Best match: “${anchor.matchedParagraphPreview}”`
                          : 'A candidate match was found in the updated document.'}
                      </Text>
                    )}

                    <div className={styles.actions}>
                      {/* Accept only makes sense when there is a candidate paragraph (review band). */}
                      {!isOrphan ? (
                        <Button
                          appearance="primary"
                          size="small"
                          onClick={() => resolve(anchor.id, 'accept')}
                          data-testid={`compose-reanchor-conflict-accept-${anchor.id}`}
                        >
                          Accept here
                        </Button>
                      ) : null}
                      <Button
                        appearance="secondary"
                        size="small"
                        onClick={() => resolve(anchor.id, 'keep')}
                        data-testid={`compose-reanchor-conflict-keep-${anchor.id}`}
                      >
                        Keep pending
                      </Button>
                      <Button
                        appearance="subtle"
                        size="small"
                        onClick={() => resolve(anchor.id, 'discard')}
                        data-testid={`compose-reanchor-conflict-discard-${anchor.id}`}
                      >
                        Discard
                      </Button>
                    </div>
                  </div>
                );
              })
            )}
          </DialogContent>
          <DialogActions>
            <Button
              appearance="secondary"
              onClick={onClose}
              data-testid="compose-reanchor-conflict-close"
            >
              Done
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

ComposeReanchorConflictPanel.displayName = 'ComposeReanchorConflictPanel';

export default ComposeReanchorConflictPanel;
