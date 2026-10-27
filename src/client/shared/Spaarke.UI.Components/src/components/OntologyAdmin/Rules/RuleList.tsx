/**
 * RuleList - the Ontology admin Rules list (task 100; spec FR-57; #43, #44).
 *
 * One row per Ontology rule: its lane (Decide or Do), On / Off / Retired, the version in force, and the numbers #44 allows (open
 * items, last fired, action rate). The filters are All, Decide, Do and Needs a look. A rule never leaves the list because the numbers
 * could not be read: the list says so and shows the rest. A list that could not be read is an error with a retry, never an empty list.
 * Opening a row shows the read-only rule page (`RulePage`).
 *
 * Fluent v9 tokens only (ADR-021); StatusBadge for the states.
 */

import * as React from 'react';
import {
  Button,
  Link,
  MessageBar,
  MessageBarBody,
  Spinner,
  Tab,
  TabList,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import type { SelectTabData, SelectTabEvent } from '@fluentui/react-components';
import { StatusBadge } from '../../StatusBadge';
import {
  RULE_FILTERS,
  describeActionRate,
  describeLastFired,
  describeOpenItems,
  lastFiredLabel,
  matchesFilter,
  severityTone,
  stateTone,
  unreadNumbers,
} from './ruleDisplay';
import type { RuleFilter } from './ruleDisplay';
import type { OntologyRulesService, RuleListResponse } from './rulesService';

export interface RuleListProps {
  service: OntologyRulesService;
  /** Open the rule page. */
  onOpen: (policyId: string) => void;
  /** Open the rule wizard on a new rule. When omitted the list offers no New rule control. */
  onNewRule?: () => void;
}

// Column widths as flex ratios (the Fluent Table lays its rows out as flex rows): rule, lane, on/off, version, open items, last fired, action rate.
const COLUMN_GROW = [2.2, 0.8, 0.8, 0.7, 0.9, 1.4, 1.6] as const;

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    minWidth: 0,
  },
  toolbar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
  },
  row: {
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  col0: { flexGrow: COLUMN_GROW[0], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  col1: { flexGrow: COLUMN_GROW[1], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  col2: { flexGrow: COLUMN_GROW[2], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  col3: { flexGrow: COLUMN_GROW[3], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  col4: { flexGrow: COLUMN_GROW[4], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  col5: { flexGrow: COLUMN_GROW[5], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  col6: { flexGrow: COLUMN_GROW[6], flexShrink: 1, flexBasis: 0, minWidth: 0 },
  retired: {
    color: tokens.colorNeutralForeground3,
  },
  cell: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  stack: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  centered: {
    display: 'flex',
    justifyContent: 'center',
    padding: tokens.spacingVerticalXL,
  },
});

export const RuleList: React.FC<RuleListProps> = ({ service, onOpen, onNewRule }) => {
  const styles = useStyles();
  const [filter, setFilter] = React.useState<RuleFilter>('all');
  const [list, setList] = React.useState<RuleListResponse | undefined>(undefined);
  const [error, setError] = React.useState<string | undefined>(undefined);
  const [loading, setLoading] = React.useState(true);

  const load = React.useCallback(async () => {
    setLoading(true);
    setError(undefined);
    const answer = await service.list();
    if (answer.ok) {
      setList(answer.data);
    } else {
      setList(undefined);
      setError(answer.message);
    }
    setLoading(false);
  }, [service]);

  React.useEffect(() => {
    void load();
  }, [load]);

  const onFilter = React.useCallback((_event: SelectTabEvent, data: SelectTabData) => {
    setFilter(data.value as RuleFilter);
  }, []);

  const shown = list ? list.rules.filter(rule => matchesFilter(rule, filter)) : [];

  return (
    <div className={styles.root} data-testid="ontology-rules-list">
      <div className={styles.toolbar}>
        <TabList size="small" selectedValue={filter} onTabSelect={onFilter} aria-label="Filter rules">
          {RULE_FILTERS.map(f => (
            <Tab key={f.id} value={f.id} data-testid={`rules-filter-${f.id}`}>
              {f.label}
            </Tab>
          ))}
        </TabList>
        {onNewRule === undefined ? null : (
          <Button appearance="primary" onClick={onNewRule} data-testid="rules-new-rule">
            New rule
          </Button>
        )}
      </div>

      {loading ? (
        <div className={styles.centered}>
          <Spinner label="Loading rules" />
        </div>
      ) : null}

      {error ? (
        <MessageBar intent="error" data-testid="rules-list-error">
          <MessageBarBody>
            {error}{' '}
            <Button appearance="transparent" size="small" onClick={() => void load()}>
              Try again
            </Button>
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {list && !list.metricsAvailable ? (
        <MessageBar intent="warning" data-testid="rules-metrics-unavailable">
          <MessageBarBody>
            {`These numbers could not be read just now: ${unreadNumbers(list.metricsAvailability).join(', ')}. The rest are shown and every rule is listed.`}
            {list.metricsAvailability.actionRate ? '' : ' Needs a look is empty until the action rate can be read.'}
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {list && !loading ? (
        <>
          <Table aria-label="Rules">
            <TableHeader>
              <TableRow>
                <TableHeaderCell className={styles.col0}>Rule</TableHeaderCell>
                <TableHeaderCell className={styles.col1}>Lane</TableHeaderCell>
                <TableHeaderCell className={styles.col2}>On / Off</TableHeaderCell>
                <TableHeaderCell className={styles.col3}>Version</TableHeaderCell>
                <TableHeaderCell className={styles.col4}>Open items</TableHeaderCell>
                <TableHeaderCell className={styles.col5}>{lastFiredLabel(list.lastFiredWindowDays)}</TableHeaderCell>
                <TableHeaderCell className={styles.col6}>Action rate ({list.actionRateWindowDays} days)</TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {shown.map(rule => (
                // The rule code is a real button (keyboard and screen reader activation, as the DataGrid's primary-name link); a click
                // anywhere on the row opens it too for the pointer. One handler: the button's click bubbles to the row.
                <TableRow
                  key={rule.policyId}
                  className={mergeClasses(styles.row, rule.state === 'Retired' && styles.retired)}
                  onClick={() => onOpen(rule.policyId)}
                  data-testid={`rule-row-${rule.code}`}
                >
                  <TableCell className={mergeClasses(styles.col0, styles.cell, styles.stack)}>
                    <Link as="button" aria-label={`Open ${rule.code}`} data-testid={`rule-open-${rule.code}`}>
                      <Text weight="semibold">{rule.code}</Text>
                    </Link>
                    <Text size={200} className={styles.muted}>
                      {rule.headline ?? rule.name}
                    </Text>
                  </TableCell>
                  <TableCell className={mergeClasses(styles.col1, styles.cell)} data-testid="rule-lane">
                    <Text>{rule.lane}</Text>
                  </TableCell>
                  <TableCell className={mergeClasses(styles.col2, styles.cell)}>
                    <StatusBadge label={rule.state} tone={stateTone(rule.state)} />
                  </TableCell>
                  <TableCell className={mergeClasses(styles.col3, styles.cell)} data-testid="rule-version">
                    <Text>{rule.versionNumber === null ? '-' : `v${rule.versionNumber}`}</Text>
                  </TableCell>
                  <TableCell className={mergeClasses(styles.col4, styles.cell)} data-testid="rule-open-items">
                    <Text className={list.metricsAvailability.openItems ? undefined : styles.muted}>
                      {describeOpenItems(rule.metrics, list.metricsAvailability)}
                    </Text>
                  </TableCell>
                  <TableCell className={mergeClasses(styles.col5, styles.cell)} data-testid="rule-last-fired">
                    <Text size={200} className={list.metricsAvailability.lastFired ? undefined : styles.muted}>
                      {describeLastFired(rule.metrics, list.metricsAvailability, list.lastFiredWindowDays)}
                    </Text>
                  </TableCell>
                  <TableCell className={mergeClasses(styles.col6, styles.cell, styles.stack)} data-testid="rule-action-rate">
                    <Text size={200} className={list.metricsAvailability.actionRate ? undefined : styles.muted}>
                      {describeActionRate(rule.metrics, list.minimumJudged, list.metricsAvailability)}
                    </Text>
                    {rule.metrics.needsALook ? <StatusBadge label="Needs a look" tone={severityTone('Warning')} /> : null}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          {shown.length === 0 ? (
            <Text className={styles.muted} data-testid="rules-list-empty">
              {list.rules.length === 0 ? 'There are no rules yet.' : 'No rules match this filter.'}
            </Text>
          ) : null}
        </>
      ) : null}
    </div>
  );
};

RuleList.displayName = 'RuleList';
