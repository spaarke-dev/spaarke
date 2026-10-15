/**
 * WorklistWidget - the Console's worklist: two lanes (Decide always above Do), each with its count, its count filters and its
 * start button, one MatterCard per core record, and the Closed today / Suppressed / Closed earlier disclosures below. A line,
 * a start button or a disclosure line opens the decision wizard in-app (D-26) on the list the user is looking at.
 *
 * Membership and order come from the Signal read route (task 038) and from nowhere else (D-15): this component reads no
 * `sprk_signal` row itself and never sorts or filters by a rule of its own. A count filter is a lens on the list already held
 * (task 052), is kept in component state only and is gone on the next mount (HANDOFF section 1.4 L123).
 *
 * Slots the host fills (this library has no Dataverse or Xrm dependency, ADR-012):
 *  - `aggregateRow` - the email reconciliation row (task 054's card). Top of the Do lane, outside both lanes' counts, hidden
 *    while a Do filter is on.
 *  - `narrative` - the Briefing narrative. Hidden while ANY filter is on; framed as written by the Assistant, not choosing or
 *    ordering the list (W-10). What it may say (never an item that is not on the list) is the narrative's own contract (task 062).
 *  - `lookup` - the wizard's search for Person / Lookup fields.
 *
 * Task: spaarke-ontology-platform-r1, task 059. Fluent v9 tokens only (ADR-021). No React 18-only API (shared with PCF hosts).
 */

import * as React from 'react';
import {
  Button,
  Caption1,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { SparkleRegular } from '@fluentui/react-icons';
import { launchDecisionWizard } from '../DecisionWizard/decisionWizardLauncher';
import type { DecisionWizardLaunchRequest, DecisionWizardLaunchResult } from '../DecisionWizard/decisionWizardLauncher';
import type { DecisionItemLoader, DecisionLookupSearch } from '../DecisionWizard/decisionWizardTypes';
import type { LaneFilterKey } from './laneCountFilters';
import type { OpenItemEvent, WorklistLane } from './types';
import { useWorklistData } from './useWorklistData';
import { WorklistDisclosures } from './WorklistDisclosures';
import type { DisclosureSetName } from './WorklistDisclosures';
import { WorklistLanePanel } from './WorklistLanePanel';
import type { WorklistService, WorklistSetName } from './worklistService';
import { buildLaneView } from './worklistView';
import { buildWizardItem } from './worklistWizardItem';

export interface WorklistWidgetProps {
  /** Reads the Signal read route. `createWorklistService` builds one over the host's authenticated fetch. */
  service: WorklistService;
  /** The wizard's search for Person / Lookup fields (the item's own budgets, firms, people). */
  lookup?: DecisionLookupSearch;
  /** Opens the decision wizard. Defaults to the in-app host's launcher; tests inject one. */
  launchWizard?: (request: DecisionWizardLaunchRequest) => Promise<DecisionWizardLaunchResult>;
  /** The email aggregate row (task 054's card), placed at the top of the Do lane. */
  aggregateRow?: React.ReactNode;
  /** The Briefing narrative, shown above the lanes while no filter is on. */
  narrative?: React.ReactNode;
  /** "Now" for ages and due states; tests inject a fixed date. Defaults to the time the list was last read. */
  today?: Date;
  className?: string;
}

const LANES: readonly WorklistLane[] = ['Decide', 'Do'];
const ALL_SETS: readonly WorklistSetName[] = ['lanes', 'closedToday', 'suppressed', 'closedEarlier'];

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL, minWidth: 0 },
  narrative: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalL,
    paddingRight: tokens.spacingHorizontalL,
  },
  narrativeTag: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },
  center: { display: 'flex', justifyContent: 'center', padding: tokens.spacingVerticalXXL },
});

const coreRef = (core: { recordType: string; recordId: string } | null) =>
  core ? { recordType: core.recordType, recordId: core.recordId } : null;

export const WorklistWidget: React.FC<WorklistWidgetProps> = ({
  service,
  lookup,
  launchWizard = launchDecisionWizard,
  aggregateRow,
  narrative,
  today: todayProp,
  className,
}) => {
  const styles = useStyles();
  const { sets, latest, ensure, retry, reload } = useWorklistData(service);

  // Component state only: nothing here is stored, so a reload opens with every filter off.
  const [filters, setFilters] = React.useState<Record<WorklistLane, LaneFilterKey>>({ Decide: 'all', Do: 'all' });
  const [notice, setNotice] = React.useState<string | null>(null);

  // "Today" moves forward each time the lanes are read again.
  const lanesData = sets.lanes.data;
  // `lanesData` is the trigger on purpose: a fresh read is when "now" is taken again.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const today = React.useMemo(() => todayProp ?? new Date(), [todayProp, lanesData]);

  const views = {
    Decide: buildLaneView(lanesData?.Decide ?? [], 'Decide', filters.Decide),
    Do: buildLaneView(lanesData?.Do ?? [], 'Do', filters.Do),
  };

  // A filter whose cards emptied (the list changed under it) falls back to All, so it cannot come back unannounced.
  React.useEffect(() => {
    setFilters(prev => {
      const next = { ...prev };
      let changed = false;
      for (const lane of LANES) {
        if (views[lane].filterKey !== prev[lane]) {
          next[lane] = views[lane].filterKey;
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }, [views.Decide.filterKey, views.Do.filterKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const loadItem = React.useCallback<DecisionItemLoader>(
    async signalId => {
      for (const set of ALL_SETS) {
        const data = latest.current[set].data;
        if (!data) continue;
        for (const lane of LANES) {
          for (const group of data[lane]) {
            const item = group.items.find(i => i.signalId === signalId);
            if (item) return buildWizardItem({ item, group, set, today: new Date() });
          }
        }
      }
      throw new Error('This item is no longer on the list. Close the wizard and refresh the worklist.');
    },
    [latest]
  );

  const openWizard = React.useCallback(
    async (event: OpenItemEvent) => {
      setNotice(null);
      let recorded = false;
      try {
        const result = await launchWizard({
          ...event,
          loadItem,
          lookup,
          onRecorded: () => {
            recorded = true;
          },
        });
        if (!result.opened && result.reason === 'no-host') {
          setNotice('The decision wizard is not available in this view.');
        }
      } catch {
        setNotice('The decision wizard could not be opened.');
      }
      // What was recorded has left the lanes: read them again once the wizard is closed.
      if (recorded) reload();
    },
    [launchWizard, loadItem, lookup, reload]
  );

  const startLane = (lane: WorklistLane) => {
    const view = views[lane];
    const first = view.groups[0];
    if (!first) return;
    void openWizard({
      itemId: view.visibleIds[0],
      core: coreRef(first.group.core),
      visibleList: [...view.visibleIds],
    });
  };

  const doneToday = (lane: WorklistLane): number | undefined => {
    const data = sets.closedToday.data;
    if (!data) return undefined;
    return data[lane].reduce((n, g) => n + g.items.length, 0);
  };

  if (!lanesData) {
    if (sets.lanes.status === 'error') {
      return (
        <div className={className}>
          <MessageBar intent="error" data-testid="worklist-error">
            <MessageBarBody>{sets.lanes.error ?? 'The worklist could not be read.'}</MessageBarBody>
            <MessageBarActions>
              <Button size="small" onClick={() => retry('lanes')}>
                Try again
              </Button>
            </MessageBarActions>
          </MessageBar>
        </div>
      );
    }
    return (
      <div className={styles.center} data-testid="worklist-loading">
        <Spinner label="Loading the worklist" />
      </div>
    );
  }

  const anyFilter = LANES.some(l => views[l].filterKey !== 'all');

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="worklist-widget">
      {sets.lanes.status === 'error' && (
        <MessageBar intent="warning">
          <MessageBarBody>The worklist could not be refreshed. This is the last list that was read.</MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => retry('lanes')}>
              Try again
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}
      {notice && (
        <MessageBar intent="warning" data-testid="worklist-notice">
          <MessageBarBody>{notice}</MessageBarBody>
        </MessageBar>
      )}

      {narrative && !anyFilter && (
        <section className={styles.narrative} aria-label="Briefing" data-testid="worklist-narrative">
          {narrative}
          <span className={styles.narrativeTag}>
            <SparkleRegular aria-hidden />
            <Caption1>Written by the Assistant from the lists below. It doesn&apos;t choose or order them.</Caption1>
          </span>
        </section>
      )}

      {LANES.map(lane => (
        <WorklistLanePanel
          key={lane}
          lane={lane}
          view={views[lane]}
          doneToday={doneToday(lane)}
          today={today}
          onSelectKey={key => setFilters(prev => ({ ...prev, [lane]: key }))}
          onOpenItem={event => void openWizard(event)}
          onStart={() => startLane(lane)}
          aggregateRow={lane === 'Do' ? aggregateRow : undefined}
        />
      ))}

      <WorklistDisclosures
        sets={{
          closedToday: sets.closedToday,
          suppressed: sets.suppressed,
          closedEarlier: sets.closedEarlier,
        }}
        today={today}
        onOpenSet={(set: DisclosureSetName) => ensure(set)}
        onRetry={(set: DisclosureSetName) => retry(set)}
        onOpenItem={event => void openWizard(event)}
      />
    </div>
  );
};

WorklistWidget.displayName = 'WorklistWidget';
