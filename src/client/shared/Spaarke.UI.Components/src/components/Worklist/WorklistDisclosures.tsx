/**
 * WorklistDisclosures - the three sets below the lanes, each closed by default: Closed today, Suppressed (with the date it is
 * raised again) and Closed earlier (HANDOFF section 1.2 L83). Both lanes appear together, as cards in the route's order
 * (Decide's, then Do's). A line opens the decision wizard on that set's list, read-only (the wizard opens any closed or
 * suppressed item read-only).
 *
 * Not exported from the barrel: it is part of `WorklistWidget`.
 *
 * Task: spaarke-ontology-platform-r1, task 059.
 */

import * as React from 'react';
import {
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Button,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import type { AccordionToggleEventHandler } from '@fluentui/react-components';
import { MatterCard } from './MatterCard';
import type { OpenItemEvent, WorklistLane } from './types';
import type { WorklistSetState } from './useWorklistData';
import type { WorklistGroupData, WorklistSetName } from './worklistService';
import { closedLineText } from './worklistWizardItem';
import { groupKey } from './worklistView';

export type DisclosureSetName = Exclude<WorklistSetName, 'lanes'>;

const DISCLOSURES: ReadonlyArray<{ set: DisclosureSetName; label: string; empty: string }> = [
  { set: 'closedToday', label: 'Closed today', empty: 'Nothing was closed today.' },
  { set: 'suppressed', label: 'Suppressed', empty: 'Nothing is suppressed.' },
  { set: 'closedEarlier', label: 'Closed earlier', empty: 'Nothing was closed earlier.' },
];

const LANES: readonly WorklistLane[] = ['Decide', 'Do'];

export interface WorklistDisclosuresProps {
  sets: Readonly<Record<DisclosureSetName, WorklistSetState>>;
  today: Date;
  /** A disclosure was opened: the host loads its set if it has not been asked for. */
  onOpenSet: (set: DisclosureSetName) => void;
  onRetry: (set: DisclosureSetName) => void;
  /** A line was activated; `set` is the disclosure it is in, so the wizard opens that set's copy of the item. */
  onOpenItem: (event: OpenItemEvent, set: DisclosureSetName) => void;
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column' },
  panel: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
  muted: { color: tokens.colorNeutralForeground3 },
});

interface Cards {
  key: string;
  group: WorklistGroupData;
  items: WorklistGroupData['items'];
}

function cardsFor(state: WorklistSetState, set: DisclosureSetName, today: Date): { cards: Cards[]; ids: string[] } {
  const cards: Cards[] = [];
  const ids: string[] = [];
  if (!state.data) return { cards, ids };
  for (const lane of LANES) {
    for (const group of state.data[lane]) {
      // The line says how it closed (or until when it is suppressed) instead of an age or a due state.
      const items = group.items.map(i => ({ ...i, timingText: closedLineText(i, set, today) }));
      cards.push({ key: `${lane}:${groupKey(group)}`, group, items });
      for (const i of items) ids.push(i.signalId);
    }
  }
  return { cards, ids };
}

function countOf(state: WorklistSetState): number | null {
  if (!state.data) return null;
  return LANES.reduce((n, lane) => n + state.data![lane].reduce((m, g) => m + g.items.length, 0), 0);
}

export const WorklistDisclosures: React.FC<WorklistDisclosuresProps> = ({
  sets,
  today,
  onOpenSet,
  onRetry,
  onOpenItem,
}) => {
  const styles = useStyles();

  const onToggle: AccordionToggleEventHandler<string> = (_event, data) => {
    for (const value of data.openItems) onOpenSet(value as DisclosureSetName);
  };

  return (
    <Accordion multiple collapsible onToggle={onToggle} className={styles.root} data-testid="worklist-disclosures">
      {DISCLOSURES.map(({ set, label, empty }) => {
        const state = sets[set];
        const count = countOf(state);
        const { cards, ids } = cardsFor(state, set, today);
        return (
          <AccordionItem key={set} value={set} data-testid={`disclosure-${set}`}>
            <AccordionHeader>{count === null ? label : `${label} (${count})`}</AccordionHeader>
            <AccordionPanel>
              <div className={styles.panel}>
                {state.status === 'error' && (
                  <MessageBar intent="error">
                    <MessageBarBody>{state.error ?? 'This list could not be read.'}</MessageBarBody>
                    <MessageBarActions>
                      <Button size="small" onClick={() => onRetry(set)}>
                        Try again
                      </Button>
                    </MessageBarActions>
                  </MessageBar>
                )}
                {(state.status === 'loading' || state.status === 'idle') && !state.data && (
                  <Spinner size="tiny" label={`Loading ${label.toLowerCase()}`} labelPosition="after" />
                )}
                {cards.map(c => (
                  <MatterCard
                    key={c.key}
                    core={c.group.core}
                    items={c.items}
                    visibleList={ids}
                    onOpenItem={event => onOpenItem(event, set)}
                    today={today}
                  />
                ))}
                {state.data && cards.length === 0 && state.status !== 'error' && (
                  <Text className={styles.muted} data-testid={`disclosure-empty-${set}`}>
                    {empty}
                  </Text>
                )}
              </div>
            </AccordionPanel>
          </AccordionItem>
        );
      })}
    </Accordion>
  );
};

WorklistDisclosures.displayName = 'WorklistDisclosures';
