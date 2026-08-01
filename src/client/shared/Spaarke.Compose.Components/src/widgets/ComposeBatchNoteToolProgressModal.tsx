/**
 * ComposeBatchNoteToolProgressModal — persistent progress indicator for a multi-select batch AI
 * action over Review Notes (ai-advanced-capabilities-agreements-r1 task 041, spec FR-11 / design.md
 * Lens 2 #3).
 *
 * LIGHTENED, NOT FORKED, VERSION OF THE SHIPPED PATTERN: `NdaReviewProgressModal`
 * (`src/solutions/SpaarkeAi/src/components/conversation/NdaReviewProgressModal.tsx`) is the
 * canonical Dialog-based persistent-progress reference this reuses (Dialog + DialogSurface +
 * DialogBody, `modalType="alert"` — no light-dismiss, tokens-only styling, dark-mode-safe,
 * complete/error terminal states). It cannot be reused VERBATIM here because its steps are
 * SYNTHESIZED (a single whole-document review emits no per-stage events, so it advances a fake
 * timer) — a batch of N notes instead has REAL, per-note completion events (this component's whole
 * reason to exist), so the honest representation is a DETERMINATE `ProgressBar` (`completed/total`)
 * driven by {@link BatchNoteToolProgress}, not a synthesized step track. `AiProgressStepper`
 * (`@spaarke/ui-components`) was evaluated and rejected for the SAME reason in the other direction:
 * its horizontal step-chip track assumes a small, fixed step count — unusable once N approaches the
 * ~25-note soft cap (task 041's own `BATCH_NOTE_TOOL_SOFT_CAP`).
 *
 * Per ASSISTANT-UI-ELEMENT-CRITERIA.md, a running/summarizing batch operation is a PERSISTENT
 * operation indicator (Dialog), never a chip — this mirrors `NdaReviewProgressModal`'s own framing.
 *
 * ADR-041 (no new outcome shape): this modal renders ONLY the batch-level rollup (counts + a short
 * per-note failure list for triage) — it NEVER substitutes for a note's own Assistant confirmation,
 * which continues to render via the EXISTING `dispatchComposeAction` → `makeComposeEditControlsMessage`
 * path, unchanged, once each note's dispatch resolves.
 *
 * DISMISSAL: an all-success run auto-dismisses after a short linger (mirrors
 * `NdaReviewProgressModal`'s `COMPLETE_LINGER_MS`) — nothing needs the reviewer's attention. A run
 * with ANY failure stays open with an explicit Close button so the failure list is not missed
 * (Failure isolation — spec FR-11 acceptance: "the summary reports success/failure per note").
 *
 * @see ./batchNoteToolRunner.ts — the sequential loop this renders the progress of
 * @see ./ComposeCommentGutter.tsx — the selection + sub-toolbar that triggers a batch run
 * @see ./ComposeEditor.tsx — owns the `BatchNoteToolRunState` this component is driven by
 * @see ../../../../solutions/SpaarkeAi/src/components/conversation/NdaReviewProgressModal.tsx — the
 *      canonical Dialog-based progress pattern this lightens (see file header above)
 */
import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  Button,
  ProgressBar,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircle20Filled, ErrorCircle20Filled } from '@fluentui/react-icons';
import type { BatchNoteToolOutcome, BatchNoteToolProgress } from './batchNoteToolRunner';

/** How long an ALL-SUCCESS run lingers before auto-dismissing (mirrors NdaReviewProgressModal). */
const ALL_SUCCESS_LINGER_MS = 1200;

const useStyles = makeStyles({
  surface: {
    width: '420px',
    maxWidth: '92vw',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    width: '100%',
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  statusLine: {
    color: tokens.colorNeutralForeground2,
  },
  progressWrap: {
    width: '100%',
  },
  summaryRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
  },
  summarySuccess: {
    color: tokens.colorStatusSuccessForeground1,
  },
  summaryError: {
    color: tokens.colorStatusDangerForeground1,
  },
  failureList: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    maxHeight: '160px',
    overflowY: 'auto',
    paddingTop: tokens.spacingVerticalXS,
  },
  failureItem: {
    display: 'flex',
    alignItems: 'flex-start',
    columnGap: tokens.spacingHorizontalXS,
  },
  failureIcon: {
    color: tokens.colorStatusDangerForeground1,
    flexShrink: 0,
    marginTop: '2px',
  },
  failureText: {
    color: tokens.colorNeutralForeground2,
  },
  actions: {
    display: 'flex',
    justifyContent: 'flex-end',
  },
});

/** A batch outcome enriched with a display label (the host resolves `threadId` → a clause location). */
export interface BatchNoteToolOutcomeDisplay extends BatchNoteToolOutcome {
  /** Human-readable label for the failed note (e.g. its clause location) — falls back to a generic label. */
  readonly label: string;
}

export interface ComposeBatchNoteToolProgressModalProps {
  /** The action's display label (e.g. "Draft compliant alternative") — shown in the title. */
  toolLabel: string;
  /** Live progress while the batch is running; `null` once it has finished. */
  progress: BatchNoteToolProgress | null;
  /** Final outcomes (host-resolved display labels) once the batch has finished; `null` while running. */
  outcomes: readonly BatchNoteToolOutcomeDisplay[] | null;
  /** Dismiss the modal — called on Close, or automatically after an all-success linger. */
  onClose: () => void;
}

export function ComposeBatchNoteToolProgressModal(
  props: ComposeBatchNoteToolProgressModalProps
): React.JSX.Element {
  const { toolLabel, progress, outcomes, onClose } = props;
  const styles = useStyles();

  const failed = React.useMemo(() => (outcomes ?? []).filter(o => !o.ok), [outcomes]);
  const succeededCount = outcomes ? outcomes.length - failed.length : 0;
  const allSucceeded = outcomes !== null && failed.length === 0;

  // Auto-dismiss an all-success run after a short linger; a run with any failure stays open.
  React.useEffect(() => {
    if (!allSucceeded) return;
    const id = setTimeout(onClose, ALL_SUCCESS_LINGER_MS);
    return () => clearTimeout(id);
  }, [allSucceeded, onClose]);

  const running = progress !== null;
  const total = running ? progress.total : (outcomes?.length ?? 0);
  const completed = running ? progress.completed : total;
  const currentIndex = running ? Math.min(progress.completed + 1, progress.total) : total;

  return (
    <Dialog open modalType="alert">
      <DialogSurface className={styles.surface} data-testid="compose-batch-note-tool-progress-modal">
        <DialogBody>
          <div className={styles.body}>
            <Text size={400} className={styles.title}>
              {running ? `Running "${toolLabel}"…` : 'Batch run complete'}
            </Text>

            {running ? (
              <>
                <Text
                  size={200}
                  className={styles.statusLine}
                  aria-live="polite"
                  data-testid="compose-batch-note-tool-progress-status"
                >
                  {`Note ${currentIndex} of ${total}…`}
                </Text>
                <div className={styles.progressWrap}>
                  <ProgressBar
                    value={total > 0 ? completed / total : 0}
                    thickness="medium"
                    aria-label={`Batch progress: ${completed} of ${total} notes complete`}
                  />
                </div>
              </>
            ) : (
              <>
                <div className={styles.summaryRow} data-testid="compose-batch-note-tool-progress-summary">
                  <CheckmarkCircle20Filled className={styles.summarySuccess} aria-hidden />
                  <Text size={300}>{`${succeededCount} succeeded`}</Text>
                  {failed.length > 0 ? (
                    <>
                      <ErrorCircle20Filled className={styles.summaryError} aria-hidden />
                      <Text size={300}>{`${failed.length} failed`}</Text>
                    </>
                  ) : null}
                </div>
                {failed.length > 0 ? (
                  <div className={styles.failureList} role="list" aria-label="Notes that failed">
                    {failed.map(f => (
                      <div key={f.threadId} className={styles.failureItem} role="listitem">
                        <ErrorCircle20Filled className={styles.failureIcon} aria-hidden />
                        <Text size={200} className={styles.failureText}>
                          {f.label}
                          {f.error ? ` — ${f.error}` : ''}
                        </Text>
                      </div>
                    ))}
                  </div>
                ) : null}
              </>
            )}

            {!running && failed.length > 0 ? (
              <div className={styles.actions}>
                <Button appearance="primary" onClick={onClose} data-testid="compose-batch-note-tool-progress-close">
                  Close
                </Button>
              </div>
            ) : null}
          </div>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

ComposeBatchNoteToolProgressModal.displayName = 'ComposeBatchNoteToolProgressModal';

export default ComposeBatchNoteToolProgressModal;
