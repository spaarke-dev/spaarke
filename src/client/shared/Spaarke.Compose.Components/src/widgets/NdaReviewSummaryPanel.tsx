/**
 * NdaReviewSummaryPanel.tsx — review-summary docked panel (ai-advanced-capabilities-nda-r1 task 030,
 * FR-07).
 *
 * Renders the fuller advisory review inside Compose — overall risk + the flagged-section list, each
 * with its citation (sectionRef + quotedText + standardRef) — directly in the Compose column.
 * **Compose is the single surface** (design.md "Surface" decision, spec.md resolved item): there is
 * NO separate Analysis widget in r1; this panel is the entire review-summary UI.
 *
 * A Fluent v9 docked panel mirroring {@link ./ComposeCommentThread.tsx}'s conventions verbatim:
 * `open` / `onClose` props, `makeStyles` + semantic `tokens.*` only (ADR-021), a header with a
 * dismiss button, and a scrollable list body. This component is presentational only — all data
 * (findings, the placement-failure count) is supplied by the host (`ComposeWorkspace.tsx`), which
 * captures it from the SAME `compose_advisory_comments` PaneEventBus event task 031's
 * `onAdvisoryComments` receiver already handles (ADR-040 render-follows-store: one ledgered
 * NDA-REVIEW result, two renderings — in-document Comments (031) and this panel (030) — never a
 * second server disposition or model call).
 *
 * DISCLAIMER (binding, per the NDA-REVIEW Action's `$comment-disclaimer`,
 * `infra/dataverse/actions/nda-review.action.json`): the not-legal-advice framing is a FIXED
 * platform constant this panel renders as a standing banner — deliberately NOT a model-generated
 * output field (a per-run disclaimer would be ungrounded free-form text, breaking the closed
 * `{overallRisk, flaggedSections[]}` output contract tasks 020/030/031/041 share).
 *
 * OVERALL RISK — derivation note: the `compose_advisory_comments` event (task 031,
 * `PaneEventTypes.ts` `WorkspacePaneEvent.advisoryComments`) carries one entry per flagged section
 * but NOT the Action's top-level `overallRisk` field verbatim (that field never crossed the wire —
 * 031's bridge only projects `flaggedSections[]`). Rather than omit the "overall risk" the spec
 * requires (FR-07 / C7), this panel DERIVES it client-side as the maximum severity among the
 * rendered findings' `riskLevel` — the SAME rule the Action's own rubric uses to compute it
 * server-side ("Derive overallRisk from the flagged findings — it is at least as severe as the most
 * severe finding", `nda-review.action.json` systemPrompt). The banner is labelled "Overall risk
 * (from findings)" so it is never mistaken for a distinct server-asserted value. A follow-on to
 * thread the Action's own `overallRisk` string across the same event (making this derivation
 * unnecessary) is a natural next step once a project owns `PaneEventTypes.ts` again — out of this
 * task's file scope (Compose.Components client only).
 *
 * Component justification (CLAUDE.md §11):
 *   - Existing: `ComposeCommentThread`/`ComposeFindReplace` are the only docked panels in Compose;
 *     neither renders a risk-rated, citation-bearing finding list — no overlap to extend.
 *   - Extension: the review summary is a DIFFERENT capability (read-only advisory digest of a whole-
 *     document AI review) from either sibling (comment threads / find-replace); folding it into
 *     either would blur their scope guards. A new docked panel, mirroring the SAME conventions, is
 *     the reuse-first move (no new panel framework — spec.md resolved item / task constraint).
 *   - Cost-of-doing-nothing: without it, the ONLY place the review's cited findings are visible is
 *     the Assistant's short bullet summary — an attorney reviewing IN Compose has no fuller,
 *     citation-bearing digest alongside the document (FR-07 / C7 fails).
 *
 * @see ./ComposeCommentThread.tsx — the docked-panel convention this mirrors
 * @see ./ComposeWorkspace.tsx — mount + data wiring (captures the same event 031 handles)
 * @see ./useComposeWorkspaceReceivers.ts — `onAdvisoryComments` (031) — the event this panel's data
 *      is a sibling rendering of
 * @see infra/dataverse/outputschemas/nda-review.schema.json — the closed `{overallRisk,
 *      flaggedSections[]}` output contract this panel renders
 * @see projects/ai-advanced-capabilities-nda-r1/spec.md FR-07
 */
import * as React from 'react';
import { Badge, Text, Button, Tooltip, makeStyles, tokens } from '@fluentui/react-components';
import { Dismiss16Regular, Info16Regular } from '@fluentui/react-icons';

const useStyles = makeStyles({
  panel: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalS,
    maxHeight: '40vh',
    overflowY: 'auto',
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  header: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
  },
  disclaimer: {
    display: 'flex',
    alignItems: 'flex-start',
    columnGap: tokens.spacingHorizontalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
  disclaimerIcon: {
    flexShrink: 0,
    marginTop: '2px',
    color: tokens.colorNeutralForeground2,
  },
  overallRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
  },
  failureNotice: {
    color: tokens.colorPaletteYellowForeground1,
  },
  empty: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalS,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
  },
  finding: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  findingHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalXS,
  },
  sectionRef: {
    color: tokens.colorNeutralForeground1,
  },
  quotedText: {
    color: tokens.colorNeutralForeground2,
    fontStyle: 'italic',
  },
  explanation: {
    color: tokens.colorNeutralForeground1,
  },
  standardRef: {
    color: tokens.colorNeutralForeground3,
  },
});

/** The not-legal-advice framing — a FIXED platform constant (see file header). */
export const NDA_REVIEW_DISCLAIMER_TEXT =
  'AI-generated advisory review — not legal advice. Verify every finding before relying on it; ' +
  'only High/Critical items are the attorney-review signal.';

/** Closed severity order the Action's rubric uses ("at least as severe as the most severe finding"). */
const RISK_SEVERITY_ORDER = ['Low', 'Medium', 'High', 'Critical'] as const;
type RiskSeverity = (typeof RISK_SEVERITY_ORDER)[number];

function isRiskSeverity(value: string | undefined): value is RiskSeverity {
  return value !== undefined && (RISK_SEVERITY_ORDER as readonly string[]).includes(value);
}

/** Maps a riskLevel/overallRisk string to the Fluent v9 semantic Badge color (ADR-021 — no hex). */
function riskBadgeColor(risk: string | undefined): 'success' | 'warning' | 'severe' | 'danger' | 'subtle' {
  switch (risk) {
    case 'Low':
      return 'success';
    case 'Medium':
      return 'warning';
    case 'High':
      return 'severe';
    case 'Critical':
      return 'danger';
    default:
      return 'subtle';
  }
}

/** Truncated, log-safe-length label for a quoted excerpt (mirrors ComposeCommentThread's helper). */
function truncate(text: string, max: number): string {
  const trimmed = text.trim();
  return trimmed.length > max ? `${trimmed.slice(0, max)}…` : trimmed;
}

/** One flagged-section finding — the CLOSED contract's 5 fields (nda-review.schema.json), minus the
 *  array-position identity. `quotedText`/`explanation` required (a finding is never emitted without
 *  them — see `projectFlaggedSectionsToAdvisoryComments`'s guard); the rest are optional so a
 *  partially-grounded finding never crashes the panel. */
export interface NdaReviewFindingSummary {
  /** Page/section/paragraph locator (e.g. "Section 4.2, para 2 (p. 3)"). */
  sectionRef?: string;
  /** Verbatim excerpt from the NDA — the document-span citation. */
  quotedText: string;
  /** Low | Medium | High | Critical severity of this finding. */
  riskLevel?: string;
  /** Advisory explanation — grounded fact + reasoned judgment. */
  explanation: string;
  /** The firm-standard clause this finding is measured against — the standard-side citation. */
  standardRef?: string;
}

/** Derives an overall-risk band from the rendered findings — see the file header's derivation note. */
export function deriveOverallRisk(
  findings: readonly Pick<NdaReviewFindingSummary, 'riskLevel'>[]
): RiskSeverity | undefined {
  let worst: RiskSeverity | undefined;
  for (const finding of findings) {
    if (!isRiskSeverity(finding.riskLevel)) continue;
    if (worst === undefined || RISK_SEVERITY_ORDER.indexOf(finding.riskLevel) > RISK_SEVERITY_ORDER.indexOf(worst)) {
      worst = finding.riskLevel;
    }
  }
  return worst;
}

export interface NdaReviewSummaryPanelProps {
  /** Whether the panel is mounted/visible. When false, this component renders nothing. */
  open: boolean;
  /** Called when the user dismisses the panel (close button). */
  onClose: () => void;
  /** The flagged-section findings from the ledgered NDA-REVIEW result (task 020's closed contract). */
  findings: readonly NdaReviewFindingSummary[];
  /**
   * Count of advisory comments that could NOT be anchored in the live document (from
   * `ComposeEditorHandle.placeAdvisoryComments`'s `failed` list — task 031). Optional/omit when
   * unavailable or zero; the panel simply omits the notice (nice-to-have, not an acceptance
   * criterion).
   */
  placementFailureCount?: number;
}

export function NdaReviewSummaryPanel(props: NdaReviewSummaryPanelProps): React.JSX.Element | null {
  const { open, onClose, findings, placementFailureCount } = props;
  const styles = useStyles();

  if (!open) return null;

  const overallRisk = deriveOverallRisk(findings);

  return (
    <div
      className={styles.panel}
      role="complementary"
      aria-label="NDA review summary"
      data-testid="nda-review-summary-panel"
    >
      <div className={styles.header}>
        <Text weight="semibold">Review Summary</Text>
        <Tooltip content="Close review summary" relationship="description" withArrow>
          <Button
            appearance="subtle"
            size="small"
            icon={<Dismiss16Regular />}
            aria-label="Close review summary"
            onClick={onClose}
            data-testid="nda-review-summary-close"
          />
        </Tooltip>
      </div>

      <div className={styles.disclaimer} data-testid="nda-review-summary-disclaimer">
        <Info16Regular className={styles.disclaimerIcon} />
        <Text size={200}>{NDA_REVIEW_DISCLAIMER_TEXT}</Text>
      </div>

      <div className={styles.overallRow}>
        <Text weight="semibold" size={300}>
          Overall risk (from findings):
        </Text>
        {overallRisk ? (
          <Badge appearance="tint" color={riskBadgeColor(overallRisk)} data-testid="nda-review-summary-overall-risk">
            {overallRisk}
          </Badge>
        ) : (
          <Text size={200} className={styles.empty} data-testid="nda-review-summary-overall-risk-empty">
            Not yet available
          </Text>
        )}
      </div>

      {placementFailureCount && placementFailureCount > 0 ? (
        <Text size={200} className={styles.failureNotice} data-testid="nda-review-summary-placement-failures">
          {placementFailureCount} finding{placementFailureCount === 1 ? '' : 's'} could not be anchored as an
          in-document comment — the citation below still shows what was flagged.
        </Text>
      ) : null}

      <div className={styles.list}>
        {findings.length === 0 ? (
          <Text size={200} className={styles.empty} data-testid="nda-review-summary-empty">
            No flagged sections yet. Run NDA Review on this document to see findings here.
          </Text>
        ) : (
          findings.map((finding, index) => (
            <div key={index} className={styles.finding} data-testid={`nda-review-summary-finding-${index}`}>
              <div className={styles.findingHeader}>
                <Text weight="semibold" size={200} className={styles.sectionRef}>
                  {finding.sectionRef ?? 'Unreferenced section'}
                </Text>
                {finding.riskLevel ? (
                  <Badge appearance="tint" color={riskBadgeColor(finding.riskLevel)}>
                    {finding.riskLevel}
                  </Badge>
                ) : null}
              </div>
              <Text size={200} className={styles.quotedText}>
                &ldquo;{truncate(finding.quotedText, 240)}&rdquo;
              </Text>
              <Text size={200} className={styles.explanation}>
                {finding.explanation}
              </Text>
              {finding.standardRef ? (
                <Text size={100} className={styles.standardRef}>
                  Standard: {finding.standardRef}
                </Text>
              ) : null}
            </div>
          ))
        )}
      </div>
    </div>
  );
}

NdaReviewSummaryPanel.displayName = 'NdaReviewSummaryPanel';

export default NdaReviewSummaryPanel;
