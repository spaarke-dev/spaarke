/**
 * DecisionRecordsWidget - the Console's Decision Record tab (task 045, D-47; reconciliation W-18): the decided items with the
 * Decision Record that closed each, filtered by record class (All / Judgements / Routine / Dismissals). A row's one action is Open,
 * which reopens the item in the decision wizard READ-ONLY (D-26); there is no edit or delete anywhere (FR-21, R-13: `RecordRow`
 * has neither, and the tab adds none).
 *
 * Data: `GET /api/v1/signals/worklist?set=decided` through the worklist service (task 038/059) and nowhere else (D-15): the route
 * reads every record AS THE CALLER, so a Decision Record on a Secure matter reaches only that matter's members and a no-core item
 * only its owner (D-33, D-35, D-123). This component reads no `sprk_signal` or `sprk_decisionrecord` row, through Xrm or otherwise.
 * "Who decided" is the record's own confirming user, never the row owner (D-129).
 *
 * Reuse (FR-27): `RecordRow` (ConsoleKit) is the row, `buildWizardItem` (Worklist) builds the wizard's item from the answer already
 * held, and `launchDecisionWizard` is the in-app host's launcher. Nothing new is built for the list itself.
 *
 * Fluent v9 tokens only (ADR-021). No React 18-only API (shared with PCF hosts, ADR-022).
 */

import * as React from 'react';
import {
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  Tab,
  TabList,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { RecordRow, resolveDecisionRecordState } from '../ConsoleKit';
import { launchDecisionWizard } from '../DecisionWizard/decisionWizardLauncher';
import type { DecisionWizardLaunchRequest, DecisionWizardLaunchResult } from '../DecisionWizard/decisionWizardLauncher';
import type { DecisionItemLoader, DecisionLookupSearch } from '../DecisionWizard/decisionWizardTypes';
import type { WorklistData, WorklistService } from '../Worklist/worklistService';
import { buildWizardItem } from '../Worklist/worklistWizardItem';
import {
  DECISION_CLASS_FILTERS,
  buildDecisionRows,
  countByFilter,
  filterDecisionRows,
  rowTitle,
} from './decisionRecordsView';
import type { DecisionClassFilter, DecisionRecordRow } from './decisionRecordsView';

export interface DecisionRecordsWidgetProps {
  /** Reads the Signal read route. `createWorklistService` builds one over the host's authenticated fetch. */
  service: WorklistService;
  /** Opens the decision wizard. Defaults to the in-app host's launcher; tests inject one. */
  launchWizard?: (request: DecisionWizardLaunchRequest) => Promise<DecisionWizardLaunchResult>;
  /** The wizard's search for Person / Lookup fields (a read-only replay does not search; passed for parity with the worklist). */
  lookup?: DecisionLookupSearch;
  className?: string;
}

type Load =
  | { status: 'loading' }
  | { status: 'ready'; data: WorklistData }
  | { status: 'error'; message: string; httpStatus: number | null };

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM, minWidth: 0 },
  header: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalXXS },
  muted: { color: tokens.colorNeutralForeground3 },
  list: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    listStyleType: 'none',
    marginTop: 0,
    marginBottom: 0,
    marginLeft: 0,
    marginRight: 0,
    paddingTop: 0,
    paddingBottom: 0,
    paddingLeft: 0,
    paddingRight: 0,
  },
  center: { display: 'flex', justifyContent: 'center', padding: tokens.spacingVerticalXXL },
});

const errorText = (err: unknown): { message: string; httpStatus: number | null } => ({
  message: err instanceof Error && err.message ? err.message : 'The Decision Records could not be read.',
  httpStatus:
    typeof err === 'object' && err !== null && 'httpStatus' in err && typeof err.httpStatus === 'number'
      ? err.httpStatus
      : null,
});

export const DecisionRecordsWidget: React.FC<DecisionRecordsWidgetProps> = ({
  service,
  launchWizard = launchDecisionWizard,
  lookup,
  className,
}) => {
  const styles = useStyles();
  const [load, setLoad] = React.useState<Load>({ status: 'loading' });
  const [filter, setFilter] = React.useState<DecisionClassFilter>('all');
  const [notice, setNotice] = React.useState<string | null>(null);
  const alive = React.useRef(true);
  const request = React.useRef(0);

  const read = React.useCallback(() => {
    request.current += 1;
    const mine = request.current;
    setLoad(prev => (prev.status === 'ready' ? prev : { status: 'loading' }));
    service
      .read('decided')
      .then(data => {
        if (alive.current && request.current === mine) setLoad({ status: 'ready', data });
      })
      .catch((err: unknown) => {
        // The last good list stays on screen when a refresh fails.
        if (alive.current && request.current === mine) {
          setLoad(prev => (prev.status === 'ready' ? prev : { status: 'error', ...errorText(err) }));
        }
      });
  }, [service]);

  React.useEffect(() => {
    alive.current = true;
    read();
    return () => {
      alive.current = false;
    };
  }, [read]);

  const data = load.status === 'ready' ? load.data : null;
  const allRows = React.useMemo(() => (data ? buildDecisionRows(data) : []), [data]);
  const counts = React.useMemo(() => countByFilter(allRows), [allRows]);
  const rows = React.useMemo(() => filterDecisionRows(allRows, filter), [allRows, filter]);

  const open = React.useCallback(
    async (row: DecisionRecordRow) => {
      setNotice(null);
      // The wizard asks only for ids of the list it was given; the item is built from the answer already held (it never re-reads).
      const loadItem: DecisionItemLoader = async signalId => {
        const found = rows.find(r => r.item.signalId === signalId);
        if (!found) throw new Error('This item is no longer on the list. Close the wizard and refresh the list.');
        return buildWizardItem({
          item: found.item,
          group: { core: found.core, items: [found.item] },
          set: 'decided',
          today: new Date(),
        });
      };
      try {
        const result = await launchWizard({
          itemId: row.item.signalId,
          core: row.core ? { recordType: row.core.recordType, recordId: row.core.recordId } : null,
          visibleList: rows.map(r => r.item.signalId),
          loadItem,
          lookup,
        });
        if (!result.opened && alive.current) {
          setNotice(
            result.reason === 'no-host'
              ? 'The decision wizard is not available in this view.'
              : 'A decision is already open. Close it first.'
          );
        }
      } catch {
        if (alive.current) setNotice('The decision wizard could not be opened.');
      }
    },
    [launchWizard, lookup, rows]
  );

  if (load.status === 'loading') {
    return (
      <div className={styles.center} data-testid="decision-records-loading">
        <Spinner label="Loading the Decision Records" />
      </div>
    );
  }

  if (load.status === 'error') {
    // A 403 is the caller's access, not a fault: another try gives the same answer, so there is no button.
    const forbidden = load.httpStatus === 403;
    return (
      <div className={className}>
        <MessageBar intent="error" data-testid="decision-records-error">
          <MessageBarBody>
            {forbidden
              ? "Decision Records aren't available for your account. Contact your administrator."
              : load.message}
          </MessageBarBody>
          {!forbidden && (
            <MessageBarActions>
              <Button size="small" onClick={read}>
                Try again
              </Button>
            </MessageBarActions>
          )}
        </MessageBar>
      </div>
    );
  }

  const label = DECISION_CLASS_FILTERS.find(f => f.key === filter)?.label ?? 'All';
  return (
    <div className={mergeClasses(styles.root, className)} data-testid="decision-records-widget">
      <div className={styles.header}>
        <Text weight="semibold" size={500}>
          Decision Records
        </Text>
        <Text size={200} className={styles.muted}>
          Items decided in the last 90 days. Open shows the decision as it was recorded; it cannot be changed.
        </Text>
      </div>

      <TabList
        selectedValue={filter}
        onTabSelect={(_, d) => setFilter(d.value as DecisionClassFilter)}
        size="small"
        aria-label="Filter by record class"
      >
        {DECISION_CLASS_FILTERS.map(f => (
          <Tab key={f.key} value={f.key} data-testid={`decision-filter-${f.key}`}>
            {f.label} ({counts[f.key]})
          </Tab>
        ))}
      </TabList>

      {notice && (
        <MessageBar intent="warning" data-testid="decision-records-notice">
          <MessageBarBody>{notice}</MessageBarBody>
        </MessageBar>
      )}

      {rows.length === 0 ? (
        <Text className={styles.muted} data-testid="decision-records-empty">
          {filter === 'all' ? 'No decisions in the last 90 days.' : `No ${label.toLowerCase()} in the last 90 days.`}
        </Text>
      ) : (
        <ul className={styles.list} aria-label={`${label} decision records`}>
          {rows.map(row => (
            <li key={row.item.signalId}>
              <RecordRow
                recordId={row.decision.number ?? row.decision.recordId}
                title={rowTitle(row)}
                state={resolveDecisionRecordState(row.decision.outcome, row.decision.recordClass)}
                recordClass={row.decision.recordClass}
                decidedBy={row.decision.decidedByName}
                decidedOn={row.decision.decidedOn ?? row.item.resolvedOn}
                onOpen={() => void open(row)}
              />
            </li>
          ))}
        </ul>
      )}
    </div>
  );
};

DecisionRecordsWidget.displayName = 'DecisionRecordsWidget';
