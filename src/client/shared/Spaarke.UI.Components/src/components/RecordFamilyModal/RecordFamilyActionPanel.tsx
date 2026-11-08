/**
 * RecordFamilyActionPanel — "Push field updates" and "Update Assigned To" for the records ticked in the Family modal
 * (field-mapping-server-write-path-r1 task 040; spec FR-34 server side, FR-35 client side).
 *
 * The flow is PREVIEW → CONFIRM → APPLY, and nothing is written before the person presses the confirm button:
 *
 *  1. On open the panel asks `POST /api/v1/field-mappings/push` with `preview: true` once per target table. A preview
 *     writes nothing; it lists, per child, the value that would change (current → new) and the Assigned-To policies
 *     (Secure / Restricted / Limited) the change meets.
 *  2. The person reviews that list. A record the caller may not write is counted, never listed. A list the server cut is
 *     said to be partial. A list with nothing in it has no confirm button.
 *  3. On confirm the panel sends the same request without `preview`, with `childIds` = exactly the records previewed as
 *     changing. The server re-derives the children as the caller and intersects: an id it does not derive is refused per id
 *     with a fixed text. The result (updated, failed, and the server's per-record text) is shown.
 *
 * "Update Assigned To" differs from "Push field updates" only by `includeAssignedTo: true` (the server then also needs Write
 * on the source). The server applies the Assigned-To rules in addition to the ordinary Copy rules, and the preview lists
 * every value that would change, so nothing is applied that was not shown.
 *
 * Every failure is ONE fixed text (ADR-019); a cause is never rendered.
 */

import * as React from 'react';
import {
  Badge,
  Button,
  makeStyles,
  MessageBar,
  MessageBarBody,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  tokens,
} from '@fluentui/react-components';
import { SprkModal } from '../SprkModal';
import type { ResponseFetchFn } from '../../utils/fetchTypes';
import type { IFamilySelection } from './recordFamilyService';
import type { RecordFamilyAction } from './RecordFamilyModal';
import {
  buildPushRequest,
  groupRowsByTable,
  isNoFilingEdgeRefusal,
  isNoProfileRefusal,
  PUSH_FAILED_TEXT,
  pushFieldMappings,
  type IPushError,
  type IPushPreviewChange,
  type IPushSource,
} from './recordFamilyPushService';

export interface RecordFamilyActionPanelProps {
  open: boolean;
  /** Closes the panel. Nothing is written by closing. */
  onClose: () => void;
  action: RecordFamilyAction;
  /** The record the values are copied from (the Family's anchor). */
  source: IPushSource;
  /** The rows the person ticked (what `RecordFamilyModal`'s `onAction` was given). */
  rows: IFamilySelection[];
  /** Host-supplied `authenticatedFetch` (ADR-028: never a token). */
  authenticatedFetch: ResponseFetchFn;
  /**
   * Called once after an apply that wrote or attempted something, with the totals over the tables whose push completed. A
   * table whose request failed is not counted in either total (nothing is known of it); `failedTables` names those tables.
   * Not called when every request failed.
   */
  onApplied?: (result: { updatedCount: number; failedCount: number; failedTables: string[] }) => void;
  /** The `--sprk-ui-scale` factor, forwarded to the shell. */
  uiScale?: number;
}

// Partial: a flag the server adds later is not in the table, and the lookup below falls back to the flag text.
const FLAG_LABELS: Readonly<Partial<Record<string, { label: string; hint: string }>>> = {
  secure: { label: 'Secure', hint: 'Secure record: the change is a suggestion and no access is written.' },
  restricted: { label: 'Restricted', hint: 'Restricted: no contact access is granted for this change.' },
  limited: { label: 'Limited', hint: 'Limited: no organization access is granted for this change.' },
};

const TITLES: Readonly<Record<RecordFamilyAction, string>> = {
  push: 'Push field updates',
  assignedTo: 'Update Assigned To',
};

/** What the preview found, kept through the apply. */
interface IReview {
  groups: IPreviewGroup[];
  skippedNoWriteCount: number;
  truncated: boolean;
  /** Ticked records the server will not update (not children of the source for this person). */
  refusedCount: number;
  /** Tables no Filing edge joins to the source: their ticked records cannot be pushed to. */
  unsupportedTables: string[];
  /** Tables with no field mapping profile from the source (404 `field_mapping_profile_missing`): nothing is set up to push there. */
  noProfileTables: string[];
}

type Phase =
  | { kind: 'previewing' }
  | { kind: 'failed' }
  | ({ kind: 'review' } & IReview)
  | ({ kind: 'applying' } & IReview)
  | {
      kind: 'done';
      updatedCount: number;
      failedCount: number;
      errors: IPushError[];
      names: ReadonlyMap<string, string | null>;
      /** Tables whose apply request failed as a whole: nothing is known of what was written there. */
      failedTables: string[];
      /** Tables where at least one record was updated. */
      updatedTables: string[];
    };

/** The changes one target table's preview listed. */
interface IPreviewGroup {
  table: string;
  changes: IPushPreviewChange[];
}

const useStyles = makeStyles({
  body: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  flags: {
    display: 'flex',
    flexWrap: 'wrap',
    columnGap: tokens.spacingHorizontalXS,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
});

/** Distinct record ids of a group's changes, in listed order. */
function changedIds(group: IPreviewGroup): string[] {
  return [...new Set(group.changes.map(c => c.recordId))];
}

export const RecordFamilyActionPanel: React.FC<RecordFamilyActionPanelProps> = ({
  open,
  onClose,
  action,
  source,
  rows,
  authenticatedFetch,
  onApplied,
  uiScale,
}) => {
  const styles = useStyles();
  const [phase, setPhase] = React.useState<Phase>({ kind: 'previewing' });
  const includeAssignedTo = action === 'assignedTo';
  // The preview depends on WHICH record is the source, not on the object the host passed: a host that re-creates `source`
  // on every render must not re-run the preview (and reset a review in progress).
  const sourceTable = source.table;
  const sourceId = source.id;

  // The preview runs when the panel opens (and again if it is reopened for other rows). It writes nothing.
  React.useEffect(() => {
    if (!open) return undefined;
    // An object, not a `let`: the cleanup below flips it while the async preview below reads it.
    const run = { cancelled: false };
    const from: IPushSource = { table: sourceTable, id: sourceId };
    setPhase({ kind: 'previewing' });
    (async () => {
      try {
        const review: IReview = {
          groups: [],
          skippedNoWriteCount: 0,
          truncated: false,
          refusedCount: 0,
          unsupportedTables: [],
          noProfileTables: [],
        };
        for (const { table, ids } of groupRowsByTable(rows)) {
          let response;
          try {
            response = await pushFieldMappings(
              authenticatedFetch,
              buildPushRequest(from, table, { preview: true, includeAssignedTo, childIds: ids })
            );
          } catch (error) {
            // No Filing edge joins this table to the source, or no mapping profile maps the source to it: reported for this
            // table; the other tables still preview. Any other failure (including a 404 without the profile code, i.e. the
            // source itself was not found) fails the whole preview.
            if (isNoFilingEdgeRefusal(error)) {
              review.unsupportedTables.push(table);
              continue;
            }
            if (isNoProfileRefusal(error)) {
              review.noProfileTables.push(table);
              continue;
            }
            throw error;
          }
          if (!response.preview) throw new Error('The server did not treat the request as a preview.');
          review.groups.push({ table, changes: response.changes });
          review.skippedNoWriteCount += response.skippedNoWriteCount;
          review.truncated = review.truncated || response.truncated;
          review.refusedCount += response.refusedChildIds.length;
        }
        if (!run.cancelled) setPhase({ kind: 'review', ...review });
      } catch (error) {
        console.warn('[RecordFamilyActionPanel] preview failed', error);
        if (!run.cancelled) setPhase({ kind: 'failed' });
      }
    })();
    return () => {
      run.cancelled = true;
    };
  }, [open, rows, sourceTable, sourceId, authenticatedFetch, includeAssignedTo]);

  const confirm = React.useCallback(async () => {
    if (phase.kind !== 'review') return;
    const review: IReview = {
      groups: phase.groups,
      skippedNoWriteCount: phase.skippedNoWriteCount,
      truncated: phase.truncated,
      refusedCount: phase.refusedCount,
      unsupportedTables: phase.unsupportedTables,
      noProfileTables: phase.noProfileTables,
    };
    setPhase({ kind: 'applying', ...review });
    // Each table is applied on its own: a table whose request fails is named, and what the other tables wrote is still
    // reported (and handed to onApplied).
    let updatedCount = 0;
    let failedCount = 0;
    const errors: IPushError[] = [];
    const failedTables: string[] = [];
    const updatedTables: string[] = [];
    for (const group of review.groups) {
      const ids = changedIds(group);
      if (ids.length === 0) continue;
      try {
        const response = await pushFieldMappings(
          authenticatedFetch,
          buildPushRequest(source, group.table, { preview: false, includeAssignedTo, childIds: ids })
        );
        updatedCount += response.updatedCount;
        failedCount += response.failedCount;
        errors.push(...response.errors);
        if (response.updatedCount > 0) updatedTables.push(group.table);
      } catch (error) {
        console.warn('[RecordFamilyActionPanel] apply failed for a table', error);
        failedTables.push(group.table);
      }
    }
    const attempted = review.groups.filter(g => changedIds(g).length > 0).length;
    if (failedTables.length > 0 && failedTables.length === attempted) {
      // Every request failed: nothing is known to have been written. One fixed text (ADR-019).
      setPhase({ kind: 'failed' });
      return;
    }
    const names = new Map<string, string | null>();
    for (const g of review.groups) for (const c of g.changes) names.set(c.recordId.toLowerCase(), c.name);
    setPhase({ kind: 'done', updatedCount, failedCount, errors, names, failedTables, updatedTables });
    onApplied?.({ updatedCount, failedCount, failedTables });
  }, [phase, authenticatedFetch, source, includeAssignedTo, onApplied]);

  const reviewing = phase.kind === 'review' || phase.kind === 'applying';
  const groups = reviewing ? phase.groups : [];
  const recordCount = groups.reduce((n, g) => n + changedIds(g).length, 0);
  const valueCount = groups.reduce((n, g) => n + g.changes.length, 0);
  const busy = phase.kind === 'applying';

  return (
    <SprkModal
      open={open}
      onClose={busy ? () => undefined : onClose}
      title={TITLES[action]}
      size="md"
      dismiss="explicit"
      maximizable={false}
      uiScale={uiScale}
      footerStart={
        phase.kind === 'done' ? undefined : (
          <Button appearance="secondary" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
        )
      }
      footer={
        phase.kind === 'done' || phase.kind === 'failed' ? (
          <Button appearance="primary" onClick={onClose}>
            Close
          </Button>
        ) : (
          <Button
            appearance="primary"
            onClick={confirm}
            disabled={phase.kind !== 'review' || recordCount === 0}
            icon={busy ? <Spinner size="tiny" /> : undefined}
          >
            {busy ? 'Updating…' : `Update ${recordCount} ${recordCount === 1 ? 'record' : 'records'}`}
          </Button>
        )
      }
    >
      <div className={styles.body}>
        {phase.kind === 'previewing' && <Spinner label="Checking what would change…" />}

        {phase.kind === 'failed' && (
          <MessageBar intent="error">
            <MessageBarBody>{PUSH_FAILED_TEXT}</MessageBarBody>
          </MessageBar>
        )}

        {reviewing && (
          <>
            {includeAssignedTo && (
              <MessageBar intent="info">
                <MessageBarBody>
                  This update includes the Assigned To fields. Changing them can change who has access to a record.
                </MessageBarBody>
              </MessageBar>
            )}
            {phase.truncated && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  Only part of the list is shown. Only the records listed will be updated.
                </MessageBarBody>
              </MessageBar>
            )}
            {phase.skippedNoWriteCount > 0 && (
              <MessageBar intent="info">
                <MessageBarBody>
                  {phase.skippedNoWriteCount === 1
                    ? '1 record is not listed because you cannot edit it.'
                    : `${phase.skippedNoWriteCount} records are not listed because you cannot edit them.`}
                </MessageBarBody>
              </MessageBar>
            )}
            {phase.refusedCount > 0 && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  {phase.refusedCount === 1
                    ? '1 selected record cannot be updated from this record, so it is not listed.'
                    : `${phase.refusedCount} selected records cannot be updated from this record, so they are not listed.`}
                </MessageBarBody>
              </MessageBar>
            )}
            {phase.unsupportedTables.length > 0 && (
              <MessageBar intent="warning">
                <MessageBarBody>
                  {`Records of ${phase.unsupportedTables.join(', ')} cannot be updated from this record, so they are not listed.`}
                </MessageBarBody>
              </MessageBar>
            )}
            {phase.noProfileTables.map(table => (
              <Text key={table} className={styles.muted}>
                {`No field push is set up from this record to ${table}.`}
              </Text>
            ))}
            {valueCount === 0 ? (
              <Text>
                {phase.refusedCount > 0 ||
                phase.unsupportedTables.length > 0 ||
                phase.noProfileTables.length > 0 ||
                phase.skippedNoWriteCount > 0
                  ? 'Nothing to update among the records that can be updated.'
                  : 'Nothing to update: the selected records already have the current values.'}
              </Text>
            ) : (
              <>
                <Text className={styles.muted}>
                  {valueCount} {valueCount === 1 ? 'value' : 'values'} on {recordCount}{' '}
                  {recordCount === 1 ? 'record' : 'records'} will change.
                </Text>
                <Table aria-label="Values that will change" size="small">
                  <TableHeader>
                    <TableRow>
                      <TableHeaderCell>Record</TableHeaderCell>
                      <TableHeaderCell>Field</TableHeaderCell>
                      <TableHeaderCell>Current</TableHeaderCell>
                      <TableHeaderCell>New</TableHeaderCell>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {groups.flatMap(g =>
                      g.changes.map(c => (
                        <TableRow key={`${g.table}|${c.recordId}|${c.column}`}>
                          <TableCell>{c.name ?? ''}</TableCell>
                          <TableCell>
                            {c.column}
                            {c.flags.length > 0 && (
                              <span className={styles.flags}>
                                {c.flags.map(flag => (
                                  <Badge
                                    key={flag}
                                    appearance="tint"
                                    color="warning"
                                    size="small"
                                    title={FLAG_LABELS[flag]?.hint ?? flag}
                                  >
                                    {FLAG_LABELS[flag]?.label ?? flag}
                                  </Badge>
                                ))}
                              </span>
                            )}
                          </TableCell>
                          <TableCell>{c.from ?? ''}</TableCell>
                          <TableCell>{c.to ?? ''}</TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
              </>
            )}
          </>
        )}

        {phase.kind === 'done' && (
          <>
            <MessageBar intent={phase.failedCount > 0 || phase.failedTables.length > 0 ? 'warning' : 'success'}>
              <MessageBarBody>
                {phase.updatedCount === 1 ? '1 record was updated.' : `${phase.updatedCount} records were updated.`}
                {phase.failedCount > 0 &&
                  (phase.failedCount === 1
                    ? ' 1 record was not updated.'
                    : ` ${phase.failedCount} records were not updated.`)}
              </MessageBarBody>
            </MessageBar>
            {phase.failedTables.length > 0 && (
              <MessageBar intent="error">
                <MessageBarBody>
                  {`Not completed: ${phase.failedTables.join(', ')}. `}
                  {phase.updatedTables.length > 0
                    ? `Updated: ${phase.updatedTables.join(', ')}. `
                    : 'Nothing was updated in the other tables. '}
                  Close this window and try again for the rest.
                </MessageBarBody>
              </MessageBar>
            )}
            {phase.errors.length > 0 && (
              <Table aria-label="Records that were not updated" size="small">
                <TableHeader>
                  <TableRow>
                    <TableHeaderCell>Record</TableHeaderCell>
                    <TableHeaderCell>Why</TableHeaderCell>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {phase.errors.map(e => (
                    <TableRow key={e.recordId}>
                      <TableCell>{phase.names.get(e.recordId.toLowerCase()) ?? e.recordId}</TableCell>
                      <TableCell>{e.error}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </>
        )}
      </div>
    </SprkModal>
  );
};

export default RecordFamilyActionPanel;
