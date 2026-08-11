/**
 * triageColumnRenderers.tsx
 *
 * Additive `sprk_communication` TRIAGE cell renderers for the Pillar E
 * reconciliation grid (email-communication-intelligence-r2 task 051, FR-E1).
 * Triage is captured (task 025 → `CommunicationEnrichmentService`) but rendered
 * nowhere; these plug into the task-050 `DataGridOverrides.columnRenderers[field]
 * = (value, record) => node` seam — NO bespoke table, NO new data path (ADR-045:
 * existing triage fields on the existing grid).
 *
 * Fields (logical names + shapes as-built in `CommunicationEnrichmentService`):
 *   - `sprk_triagecategory`  — LOOKUP → taxonomy (renders the formatted name).
 *   - `sprk_triagepriority`  — CHOICE (Urgent=100000000 … Low=100000003); the
 *     default grid sort (config `<order>`), so this column reads top-down urgent→low.
 *   - `sprk_triagesummary`   — multiline text (truncated + hover for the full text).
 *   - `sprk_riconfidence`    — double 0..1 (rendered as a confidence %).
 *   - `sprk_reviewoutcome`   — CHOICE (File=100000000 … Pending=100000004).
 *
 * ADR-021 (Fluent v9 semantic tokens / `Badge` colors, light + dark — no literal
 * colors), ADR-022 (`React.FC` + standard APIs — dual-use on the PCF form mount).
 * Every renderer degrades a missing/null value to a neutral em-dash placeholder —
 * never a crash and never a literal "undefined".
 */
import * as React from 'react';
import { Badge, Text, makeStyles, tokens, type BadgeProps } from '@fluentui/react-components';
import type { DataGridOverrides } from '@spaarke/ui-components';

/** `sprk_triagepriority` CHOICE integers (as-built, `CommunicationEnrichmentService.TriagePriorityOptionSetValues`). */
export const TRIAGE_PRIORITY = {
  URGENT: 100000000,
  HIGH: 100000001,
  MEDIUM: 100000002,
  LOW: 100000003,
} as const;

/** `sprk_reviewoutcome` CHOICE integers (as-built, `CommunicationEnrichmentService.ReviewOutcomeOptionSetValues`). */
export const REVIEW_OUTCOME = {
  FILE: 100000000,
  UPDATE: 100000001,
  ROUTE: 100000002,
  DISMISS: 100000003,
  PENDING: 100000004,
} as const;

const SUMMARY_MAX_CHARS = 120;
const PLACEHOLDER = '—';

const useStyles = makeStyles({
  summary: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    display: 'block',
    maxWidth: '100%',
  },
  placeholder: { color: tokens.colorNeutralForeground4 },
  category: { color: tokens.colorNeutralForeground1, fontSize: tokens.fontSizeBase200 },
});

/** Coerce an option-set cell value (which may arrive as a number or numeric string) to a number, or NaN. */
function toOptionSet(value: unknown): number {
  if (typeof value === 'number') return value;
  if (typeof value === 'string' && value.trim() !== '') return Number(value);
  return NaN;
}

function stripMarkup(input: string): string {
  return input
    .replace(/<[^>]*>/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function truncate(text: string, max: number): string {
  if (text.length <= max) return text;
  return `${text.slice(0, Math.max(0, max - 1)).trimEnd()}…`;
}

const Placeholder: React.FC = () => {
  const s = useStyles();
  return (
    <Text as="span" className={s.placeholder} aria-label="Not set">
      {PLACEHOLDER}
    </Text>
  );
};

/** Category (lookup → taxonomy name). Renders the formatted name as a subtle chip; missing → placeholder. */
const CategoryCell: React.FC<{ value: unknown }> = ({ value }) => {
  const s = useStyles();
  const text = typeof value === 'string' ? value.trim() : '';
  if (!text) return <Placeholder />;
  return (
    <Badge appearance="outline" color="informative" className={s.category}>
      {text}
    </Badge>
  );
};
CategoryCell.displayName = 'TriageCategoryCell';

/** Priority status badge (Urgent → Low). The grid's default sort, so this column reads urgent-first top-down. */
const PriorityCell: React.FC<{ value: unknown }> = ({ value }) => {
  const priority = toOptionSet(value);
  let color: BadgeProps['color'] = 'subtle';
  let label = '';
  switch (priority) {
    case TRIAGE_PRIORITY.URGENT:
      color = 'danger';
      label = 'Urgent';
      break;
    case TRIAGE_PRIORITY.HIGH:
      color = 'severe';
      label = 'High';
      break;
    case TRIAGE_PRIORITY.MEDIUM:
      color = 'warning';
      label = 'Medium';
      break;
    case TRIAGE_PRIORITY.LOW:
      color = 'informative';
      label = 'Low';
      break;
    default:
      return <Placeholder />;
  }
  return (
    <Badge appearance="tint" color={color}>
      {label}
    </Badge>
  );
};
PriorityCell.displayName = 'TriagePriorityCell';

/** Truncated summary with the full text on hover (reachable per FR-E1); missing → placeholder. */
const SummaryCell: React.FC<{ value: unknown }> = ({ value }) => {
  const s = useStyles();
  const raw = typeof value === 'string' ? stripMarkup(value) : '';
  if (!raw) return <Placeholder />;
  const preview = truncate(raw, SUMMARY_MAX_CHARS);
  return (
    <Text as="span" className={s.summary} title={raw}>
      {preview}
    </Text>
  );
};
SummaryCell.displayName = 'TriageSummaryCell';

/** RI-confidence (0..1) as a toned percentage badge; missing/NaN → placeholder. */
const ConfidenceCell: React.FC<{ value: unknown }> = ({ value }) => {
  const n = typeof value === 'number' ? value : typeof value === 'string' && value.trim() !== '' ? Number(value) : NaN;
  if (!Number.isFinite(n)) return <Placeholder />;
  const clamped = Math.max(0, Math.min(1, n));
  const pct = Math.round(clamped * 100);
  const color: BadgeProps['color'] = clamped >= 0.8 ? 'success' : clamped >= 0.5 ? 'informative' : 'warning';
  return (
    <Badge appearance="tint" color={color} aria-label={`Confidence ${pct} percent`}>
      {pct}%
    </Badge>
  );
};
ConfidenceCell.displayName = 'TriageConfidenceCell';

/** Review-outcome status badge (File / Update / Route / Dismiss / Pending); missing → placeholder. */
const ReviewOutcomeCell: React.FC<{ value: unknown }> = ({ value }) => {
  const outcome = toOptionSet(value);
  let color: BadgeProps['color'] = 'subtle';
  let label = '';
  switch (outcome) {
    case REVIEW_OUTCOME.FILE:
      color = 'success';
      label = 'File';
      break;
    case REVIEW_OUTCOME.UPDATE:
      color = 'brand';
      label = 'Update';
      break;
    case REVIEW_OUTCOME.ROUTE:
      color = 'warning';
      label = 'Route';
      break;
    case REVIEW_OUTCOME.DISMISS:
      color = 'subtle';
      label = 'Dismiss';
      break;
    case REVIEW_OUTCOME.PENDING:
      color = 'severe';
      label = 'Pending';
      break;
    default:
      return <Placeholder />;
  }
  return (
    <Badge appearance="tint" color={color}>
      {label}
    </Badge>
  );
};
ReviewOutcomeCell.displayName = 'TriageReviewOutcomeCell';

/**
 * The triage renderers keyed by `sprk_communication` field logical name — merged
 * into the reconciliation grid's `overrides.columnRenderers` (task 051). Pure /
 * presentational; caller-supplied renderers still win on a field collision.
 */
export const TRIAGE_COLUMN_RENDERERS: NonNullable<DataGridOverrides['columnRenderers']> = {
  sprk_triagecategory: value => <CategoryCell value={value} />,
  sprk_triagepriority: value => <PriorityCell value={value} />,
  sprk_triagesummary: value => <SummaryCell value={value} />,
  sprk_riconfidence: value => <ConfidenceCell value={value} />,
  sprk_reviewoutcome: value => <ReviewOutcomeCell value={value} />,
};
