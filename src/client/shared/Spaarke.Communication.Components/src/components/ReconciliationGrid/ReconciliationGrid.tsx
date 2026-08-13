/**
 * ReconciliationGrid.tsx
 *
 * The Pillar E "Needs review" reconciliation grid (email-communication-intelligence-r2
 * task 050, FR-E2). A THIN context-agnostic wrapper over the shared
 * `<DataGrid configId={...} />` framework (`@spaarke/ui-components`) — it does NOT
 * fork or hand-roll a table (ADR-012). All of the seams this component uses
 * (`onRecordOpen`, `overrides.columnRenderers`, `hostFilters`, `membershipResolver`,
 * `parentContext`, `dataverseClient`) already ship on `<DataGrid />`; this task adds
 * NO changes to `Spaarke.UI.Components`.
 *
 * Renders unreconciled `sprk_communication` (type = Email) rows via the
 * "Needs-review" `sprk_gridconfiguration` (`needs-review.gridconfiguration.json`,
 * seeded via Web API at deploy time — task 059). Columns: Subject (the grid's
 * primary/clickable column — see the framework-constraint note below), Preview
 * (truncated body), Status (association-review badge), Date, From, To.
 *
 * **Row-open design note (framework constraint, not a deviation from behavior,
 * only from the POML's literal column-order example):** `<DataGrid />`'s
 * `col.isPrimaryName` is ALWAYS the layoutXml's first `<cell>` (see
 * `configResolution.ts` `parseLayoutColumns` — `isFirstCell: ... || index === 0`),
 * and the framework renders that ONE column via its own `Link` + `effectiveRecordOpen`
 * (`onRecordOpen ?? defaultRecordOpen`) — bypassing `overrides.columnRenderers` for
 * that specific column (`DataGrid.tsx` `renderCell`: the `isPrimaryName` branch
 * returns before the custom-renderer branch is ever reached). Two custom, richly
 * formatted cells (a Fluent `Badge` for review status, a truncated preview string)
 * therefore CANNOT also be the framework's primary/clickable column. Given that
 * constraint, `sprk_subject` is the primary column (plain text, click-to-open —
 * exactly the natural "click the subject to open the email" mail-client
 * convention) and the truncated body preview is a SEPARATE adjacent non-primary
 * column instead of stacked inside one combined "subject+body-preview" cell.
 * `onRecordOpen` is passed straight through to `<DataGrid />` — no additional
 * click-handling code is needed here; the framework's existing Subject-column
 * Link already calls it (or the framework's own `Xrm.Navigation.navigateTo`
 * default when `onRecordOpen` is omitted). Task 053 (the browse shell) is the
 * intended `onRecordOpen` injector.
 *
 * ADR-012 (context-agnostic — no LegalWorkspace/PCF coupling), ADR-021 (Fluent v9
 * semantic tokens, light/dark), ADR-022 (React-version-agnostic — `React.FC` +
 * standard event/hook APIs only, dual-use across the code-page host and a
 * SpaarkeAi workspace widget), ADR-045 (reconciliation unit stays
 * `sprk_communication` — no `sprk_reconciliation` entity introduced).
 */
import * as React from 'react';
import { Badge, Text, makeStyles, tokens, type BadgeProps } from '@fluentui/react-components';
import {
  DataGrid,
  type DataGridProps,
  type DataGridOverrides,
  type HostFilterCondition,
  type IDataverseClient,
  type MembershipResolver,
  type DataGridParentContext,
} from '@spaarke/ui-components';
import { TRIAGE_COLUMN_RENDERERS } from './triageColumnRenderers';
import { RelatedToCell } from './RelatedToCell';
import type { EmailConnectionsReviewProps } from '../EmailAssociationsAndTracking/EmailAssociationsAndTracking.types';

/**
 * Host binding that turns the reconciliation grid's association column into an
 * interactive "Related to" cell (task 052, FR-E3). The host resolves each row to
 * the `EmailConnectionsReview` props (`writeContext` with `hostRecordId` ===
 * `communicationId`, picker webApi, catalog, the optional create-new intent) —
 * the cell reuses that component wholesale (ADR-024 single write path). Left
 * unset, the association column keeps its static status badge.
 */
export interface RelatedToGridBinding {
  /** Resolve a grid row to the `EmailConnectionsReview` props for its Related-to picker. */
  resolveReview: (record: Record<string, unknown>) => EmailConnectionsReviewProps;
  /** NFR-10 handshake — fired with the row when its association is confirmed (Fields/Tasks tabs scope on it). */
  onConfirmed?: (record: Record<string, unknown>) => void;
  /** Grid column the interactive Related-to cell binds to (default `sprk_associationstatus`). */
  columnField?: string;
}

const DEFAULT_RELATED_TO_COLUMN = 'sprk_associationstatus';

/**
 * Placeholder GUID for the "Needs-review" `sprk_gridconfiguration` record.
 *
 * The real record is authored from `needs-review.gridconfiguration.json` and
 * seeded via Web API at deploy time (task 059) — at that point this constant
 * MUST be updated to the seeded record's actual `sprk_gridconfigurationid`.
 * Until then, any host that does not pass an explicit `configId` will resolve
 * `configId` to a non-existent record; per `DataGrid`'s FR-DG-04 contract that
 * degrades gracefully (no config record ⇒ falls back to entity-metadata-derived
 * columns), it does not throw.
 */
export const NEEDS_REVIEW_CONFIG_ID = '00000000-0000-4000-8000-000000005001';

/** `sprk_communication.sprk_associationstatus` option-set values (see docs/data-model/sprk_communication.md). */
const ASSOCIATION_STATUS = {
  RESOLVED: 100000000,
  PENDING_REVIEW: 100000001,
  /** Legacy value; the R4 Association Engine treats it as equivalent to Pending Review. */
  UNRESOLVED_LEGACY: 100000002,
  SUGGESTED: 100000003,
  AMBIGUOUS: 100000004,
} as const;

const BODY_PREVIEW_MAX_CHARS = 140;

/** Strips markup defensively so an HTML-formatted `sprk_body` reads as plain text in the preview. Never rendered via `dangerouslySetInnerHTML` — no sanitization/XSS surface to manage, this only removes visible `<tag>` noise. */
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

const useCellStyles = makeStyles({
  preview: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    display: 'block',
    maxWidth: '100%',
  },
});

/** Truncated `sprk_body` preview cell — Fluent v9 semantic tokens only (ADR-021). */
const BodyPreviewCell: React.FC<{ value: unknown }> = ({ value }) => {
  const styles = useCellStyles();
  const raw = typeof value === 'string' ? value : '';
  const preview = truncate(stripMarkup(raw), BODY_PREVIEW_MAX_CHARS);
  return (
    <Text as="span" className={styles.preview} title={preview || undefined}>
      {preview || '—'}
    </Text>
  );
};
BodyPreviewCell.displayName = 'BodyPreviewCell';

/** Review-status badge cell — maps `sprk_associationstatus` to a semantic `Badge` (no hardcoded colors, ADR-021). */
const ReviewedStatusCell: React.FC<{ value: unknown }> = ({ value }) => {
  const status = typeof value === 'number' ? value : Number(value);
  let color: BadgeProps['color'] = 'subtle';
  let label = 'Unknown';
  switch (status) {
    case ASSOCIATION_STATUS.RESOLVED:
      color = 'success';
      label = 'Resolved';
      break;
    case ASSOCIATION_STATUS.SUGGESTED:
      color = 'informative';
      label = 'Suggested';
      break;
    case ASSOCIATION_STATUS.AMBIGUOUS:
      color = 'severe';
      label = 'Ambiguous';
      break;
    case ASSOCIATION_STATUS.PENDING_REVIEW:
    case ASSOCIATION_STATUS.UNRESOLVED_LEGACY:
      color = 'danger';
      label = 'Needs Review';
      break;
    default:
      break;
  }
  return (
    <Badge appearance="tint" color={color}>
      {label}
    </Badge>
  );
};
ReviewedStatusCell.displayName = 'ReviewedStatusCell';

/** Default reconciliation-grid column renderers, keyed by `sprk_communication` field logical name. Caller-supplied `columnRenderers` (see {@link ReconciliationGridProps}) win over these on a per-field basis. Includes the task-051 triage renderers (category / priority / summary / RI-confidence / review-outcome). */
const DEFAULT_COLUMN_RENDERERS: NonNullable<DataGridOverrides['columnRenderers']> = {
  sprk_body: value => <BodyPreviewCell value={value} />,
  sprk_associationstatus: value => <ReviewedStatusCell value={value} />,
  ...TRIAGE_COLUMN_RENDERERS,
};

export interface ReconciliationGridProps {
  /** GUID of the "Needs-review" `sprk_gridconfiguration` record. Defaults to {@link NEEDS_REVIEW_CONFIG_ID}. */
  configId?: string;
  /** OPTIONAL — Dataverse access implementation. Defaults to `<DataGrid />`'s own `XrmDataverseClient` (MDA hosts). Non-MDA hosts (Code Pages, SpaarkeAi widgets) MUST pass an explicit client (`BffDataverseClient`). */
  dataverseClient?: IDataverseClient;
  /**
   * OPTIONAL — fires when a row is opened (the Subject column's link is
   * clicked). Task 053 injects the browse-shell launcher here. When absent,
   * `<DataGrid />`'s own default `Xrm.Navigation.navigateTo` open applies —
   * see the row-open design note in the module doc comment above.
   */
  onRecordOpen?: DataGridProps['onRecordOpen'];
  /**
   * OPTIONAL — fires every time the framework resolves a fresh records page,
   * with the full accumulated rows (pass-through of `<DataGrid />`'s
   * `onRecordsLoaded` seam). `ReconciliationWorkspace` (task 061) consumes it to
   * build the browse shell's "N of M" queue from the same rows the grid shows.
   */
  onRecordsLoaded?: DataGridProps['onRecordsLoaded'];
  /** OPTIONAL — per-field custom cell renderers, merged with {@link DEFAULT_COLUMN_RENDERERS} (caller entries win on a field-name collision). */
  columnRenderers?: DataGridOverrides['columnRenderers'];
  /** OPTIONAL — host-injected FetchXML conditions overlaid onto the resolved query (e.g. a host-owned filter row). */
  hostFilters?: ReadonlyArray<HostFilterCondition>;
  /** OPTIONAL — resolver for `behavior.membershipFilter` (FR-E7 / task 057). Left unconsumed by the shipped config JSON here; wiring stays available for 057 to activate. */
  membershipResolver?: MembershipResolver;
  /** OPTIONAL — drill-through context (e.g. a Matter the review queue is scoped to). */
  parentContext?: DataGridParentContext;
  /** OPTIONAL — turns the association column into an interactive "Related to" cell (task 052, FR-E3) that reuses `EmailConnectionsReview`. Left unset, that column keeps its static status badge. */
  relatedTo?: RelatedToGridBinding;
  /** OPTIONAL — additional class merged after component classes. */
  className?: string;
}

/**
 * The Pillar E "Needs review" reconciliation grid. See the module doc comment
 * for the full design rationale (ADR-012 reuse boundary, row-open constraint).
 */
export const ReconciliationGrid: React.FC<ReconciliationGridProps> = ({
  configId = NEEDS_REVIEW_CONFIG_ID,
  dataverseClient,
  onRecordOpen,
  onRecordsLoaded,
  columnRenderers,
  hostFilters,
  membershipResolver,
  parentContext,
  relatedTo,
  className,
}) => {
  const mergedColumnRenderers = React.useMemo<NonNullable<DataGridOverrides['columnRenderers']>>(() => {
    const merged: NonNullable<DataGridOverrides['columnRenderers']> = {
      ...DEFAULT_COLUMN_RENDERERS,
      ...columnRenderers,
    };
    // When a host wires `relatedTo`, the association column becomes the interactive
    // "Related to" cell (task 052) — injected LAST so it wins over the static
    // status badge on that column.
    if (relatedTo) {
      const field = relatedTo.columnField ?? DEFAULT_RELATED_TO_COLUMN;
      merged[field] = (_value, record) => (
        <RelatedToCell
          review={relatedTo.resolveReview(record)}
          onConfirmed={relatedTo.onConfirmed ? () => relatedTo.onConfirmed!(record) : undefined}
        />
      );
    }
    return merged;
  }, [columnRenderers, relatedTo]);
  const overrides = React.useMemo<DataGridOverrides>(
    () => ({ columnRenderers: mergedColumnRenderers }),
    [mergedColumnRenderers]
  );

  return (
    <DataGrid
      configId={configId}
      dataverseClient={dataverseClient}
      onRecordOpen={onRecordOpen}
      onRecordsLoaded={onRecordsLoaded}
      overrides={overrides}
      hostFilters={hostFilters}
      membershipResolver={membershipResolver}
      parentContext={parentContext}
      className={className}
    />
  );
};
ReconciliationGrid.displayName = 'ReconciliationGrid';
