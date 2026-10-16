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
  MessageBar,
  MessageBarBody,
  Spinner,
  Tab,
  TabList,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import type { SelectTabData, SelectTabEvent } from '@fluentui/react-components';
import { StatusBadge } from '../../StatusBadge';
import { RULE_FILTERS, describeActionRate, describeLastFired, matchesFilter, severityTone, stateTone } from './ruleDisplay';
import type { RuleFilter } from './ruleDisplay';
import type { OntologyRulesService, RuleListResponse } from './rulesService';

export interface RuleListProps {
  service: OntologyRulesService;
  /** Open the rule page. */
  onOpen: (policyId: string) => void;
}

const COLUMNS = 'minmax(0, 2.2fr) minmax(0, 0.8fr) minmax(0, 0.8fr) minmax(0, 0.7fr) minmax(0, 0.7fr) minmax(0, 1.2fr) minmax(0, 1.6fr)';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    minWidth: 0,
  },
  header: {
    display: 'grid',
    gridTemplateColumns: COLUMNS,
    columnGap: tokens.spacingHorizontalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    color: tokens.colorNeutralForeground3,
  },
  row: {
    display: 'grid',
    gridTemplateColumns: COLUMNS,
    columnGap: tokens.spacingHorizontalM,
    alignItems: 'center',
    width: '100%',
    boxSizing: 'border-box',
    textAlign: 'left',
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    borderBottomStyle: 'solid',
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomColor: tokens.colorNeutralStroke2,
    backgroundColor: 'transparent',
    borderTopStyle: 'none',
    borderLeftStyle: 'none',
    borderRightStyle: 'none',
    color: tokens.colorNeutralForeground1,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
    ':focus-visible': {
      outlineStyle: 'solid',
      outlineWidth: tokens.strokeWidthThick,
      outlineColor: tokens.colorStrokeFocus2,
    },
  },
  retired: {
    color: tokens.colorNeutralForeground3,
  },
  cell: {
    minWidth: 0,
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

export const RuleList: React.FC<RuleListProps> = ({ service, onOpen }) => {
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
      <TabList size="small" selectedValue={filter} onTabSelect={onFilter} aria-label="Filter rules">
        {RULE_FILTERS.map(f => (
          <Tab key={f.id} value={f.id} data-testid={`rules-filter-${f.id}`}>
            {f.label}
          </Tab>
        ))}
      </TabList>

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
            The open-item and action-rate numbers could not be read just now, so Needs a look is empty. The rules are all listed.
          </MessageBarBody>
        </MessageBar>
      ) : null}

      {list && !loading ? (
        <div role="table" aria-label="Rules">
          <div role="row" className={styles.header}>
            <Text role="columnheader" size={200}>Rule</Text>
            <Text role="columnheader" size={200}>Lane</Text>
            <Text role="columnheader" size={200}>On / Off</Text>
            <Text role="columnheader" size={200}>Version</Text>
            <Text role="columnheader" size={200}>Open items</Text>
            <Text role="columnheader" size={200}>Last fired</Text>
            <Text role="columnheader" size={200}>Action rate ({list.actionRateWindowDays} days)</Text>
          </div>
          {shown.map(rule => (
            <button
              key={rule.policyId}
              type="button"
              role="row"
              className={mergeClasses(styles.row, rule.state === 'Retired' && styles.retired)}
              onClick={() => onOpen(rule.policyId)}
              data-testid={`rule-row-${rule.code}`}
              aria-label={`Open ${rule.code}`}
            >
              <span role="cell" className={mergeClasses(styles.cell, styles.stack)}>
                <Text weight="semibold">{rule.code}</Text>
                <Text size={200} className={styles.muted}>
                  {rule.headline ?? rule.name}
                </Text>
              </span>
              <span role="cell" className={styles.cell} data-testid="rule-lane">
                <Text>{rule.lane}</Text>
              </span>
              <span role="cell" className={styles.cell}>
                <StatusBadge label={rule.state} tone={stateTone(rule.state)} />
              </span>
              <span role="cell" className={styles.cell} data-testid="rule-version">
                <Text>{rule.versionNumber === null ? '-' : `v${rule.versionNumber}`}</Text>
              </span>
              <span role="cell" className={styles.cell}>
                <Text>{rule.metrics === null ? '-' : rule.metrics.openItems}</Text>
              </span>
              <span role="cell" className={styles.cell}>
                <Text size={200}>{describeLastFired(rule.metrics)}</Text>
              </span>
              <span role="cell" className={mergeClasses(styles.cell, styles.stack)}>
                <Text size={200}>{describeActionRate(rule.metrics, list.minimumJudged)}</Text>
                {rule.metrics?.needsALook === true ? <StatusBadge label="Needs a look" tone={severityTone('Warning')} /> : null}
              </span>
            </button>
          ))}
          {shown.length === 0 ? (
            <Text className={styles.muted} data-testid="rules-list-empty">
              {list.rules.length === 0 ? 'There are no rules yet.' : 'No rules match this filter.'}
            </Text>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};

RuleList.displayName = 'RuleList';
