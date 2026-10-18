/**
 * TestStep - step 6 of the rule wizard: run the rule over current data before it can be saved or published (spaarke-ontology-platform-r1
 * task 102; D-22, FR-58).
 *
 * The dry run evaluates the draft over today's data and writes nothing to Signals; the BFF records a proof bound to the exact lane,
 * rule type, rule body and message that were tested. The wizard keeps the result only for the draft it was run on: ANY later edit makes
 * it stale, the counts are hidden and the next steps stay shut until the test is run again.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { PlayRegular } from '@fluentui/react-icons';
import type { DryRunResult } from '../authoringService';
import type { RuleWizardMode } from '../ruleWizardTypes';
import { Facts, useStepStyles } from '../stepCommon';

/** The test run the wizard holds: what was asked, and what came back. */
export interface TestRun {
  status: 'idle' | 'running' | 'done' | 'failed';
  /** The draft the run was for (`draftKey`). */
  forKey: string | null;
  result: DryRunResult | null;
  /** The BFF's readable reason when the run was refused or failed. */
  failure: string | null;
  /** The BFF refused a save or publish because it no longer holds the proof of this test (409 dry_run_required). */
  expired?: boolean;
}

export const NO_TEST_RUN: TestRun = { status: 'idle', forKey: null, result: null, failure: null };

/** How many shown rows the table lists; the counts above it are for all of them. */
const MAX_ROWS_SHOWN = 100;

const useStyles = makeStyles({
  table: {
    width: '100%',
    borderCollapse: 'collapse',
  },
  cell: {
    textAlign: 'left',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingRight: tokens.spacingHorizontalM,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    overflowWrap: 'anywhere',
  },
  scroll: {
    maxHeight: '16rem',
    overflowY: 'auto',
  },
});

const STATUS_WORDS: Readonly<Record<string, string | undefined>> = {
  open: 'Already open',
  suppressed: 'Muted',
  new: 'Would be raised',
};

export interface TestStepProps {
  mode: RuleWizardMode;
  /** The condition and wording are complete enough to ask the BFF. */
  ready: boolean;
  run: TestRun;
  /** The run is for the draft as it stands now. */
  isCurrent: boolean;
  onRun: () => void;
  locked: boolean;
}

export const TestStep: React.FC<TestStepProps> = ({ mode, ready, run, isCurrent, onRun, locked }) => {
  const styles = useStepStyles();
  const table = useStyles();
  const running = run.status === 'running';
  const result = run.result !== null && isCurrent ? run.result : null;

  return (
    <div className={styles.step} data-testid="rule-wizard-test">
      <Text className={styles.intro}>
        Test the rule over today's data. Nothing is raised and nothing is saved. You must test the rule exactly as it is before you can
        {mode.kind === 'new' ? ' save it' : ' publish it'}; if you change anything afterwards, test it again.
      </Text>

      <div className={styles.inline}>
        <Button
          appearance="primary"
          icon={running ? <Spinner size="tiny" /> : <PlayRegular />}
          disabled={locked || running || !ready}
          onClick={onRun}
          data-testid="rule-wizard-run-test"
        >
          {running ? 'Testing' : run.result === null ? 'Run test' : 'Run test again'}
        </Button>
        {!ready ? (
          <Text className={styles.muted} data-testid="rule-wizard-test-not-ready">
            Finish the condition and the wording first.
          </Text>
        ) : null}
      </div>

      {run.status === 'failed' && run.failure !== null ? (
        <Text role="alert" className={styles.error} data-testid="rule-wizard-test-failure">
          {run.failure}
        </Text>
      ) : null}

      {run.result !== null && !isCurrent ? (
        <Text role="status" className={styles.error} data-testid="rule-wizard-test-stale">
          {run.expired === true
            ? 'The proof of the last test has expired. Run the test again.'
            : 'The rule has changed since the last test. Run the test again.'}
        </Text>
      ) : null}

      {result === null ? null : (
        <section className={styles.group} aria-label="Test result" data-testid="rule-wizard-test-result">
          {result.recorded ? null : (
            <Text role="alert" className={styles.error} data-testid="rule-wizard-test-unrecorded">
              This test could not be recorded for you, so the rule cannot be {mode.kind === 'new' ? 'saved' : 'published'} yet. Run it again.
            </Text>
          )}
          <Facts
            rows={[
              ['Already open', <span data-testid="rule-wizard-count-open">{result.open}</span>],
              ['Muted', <span data-testid="rule-wizard-count-suppressed">{result.suppressed}</span>],
              ['Would be raised', <span data-testid="rule-wizard-count-new">{result.new}</span>],
              ['Would be replaced', <span data-testid="rule-wizard-count-supersede">{result.wouldSupersede}</span>],
              ['Would be raised again', <span data-testid="rule-wizard-count-reraise">{result.wouldReraise}</span>],
              ['Cannot be placed', <span data-testid="rule-wizard-count-not-placeable">{result.notPlaceable}</span>],
            ]}
          />
          {result.policyEnabled === false ? (
            <Text size={200} className={styles.muted} data-testid="rule-wizard-test-off">
              The rule is Off, so nothing is evaluated until it is turned On.
            </Text>
          ) : null}

          {result.rows.length === 0 ? (
            <Text className={styles.muted}>No records match the rule today.</Text>
          ) : (
            <div className={table.scroll}>
              <table className={table.table} data-testid="rule-wizard-test-rows">
                <thead>
                  <tr>
                    <th className={table.cell}>Record</th>
                    <th className={table.cell}>Number</th>
                    <th className={table.cell}>Result</th>
                  </tr>
                </thead>
                <tbody>
                  {result.rows.slice(0, MAX_ROWS_SHOWN).map(row => (
                    <tr key={`${row.subjectEntity}-${row.subjectId}`}>
                      <td className={table.cell}>{row.name === null ? row.subjectId : row.name}</td>
                      <td className={table.cell}>{row.number === null ? '-' : row.number}</td>
                      <td className={table.cell}>{STATUS_WORDS[row.status] === undefined ? row.status : STATUS_WORDS[row.status]}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          {result.rows.length > MAX_ROWS_SHOWN ? (
            <Text size={200} className={styles.muted}>
              Showing {MAX_ROWS_SHOWN} of {result.rows.length} records.
            </Text>
          ) : null}
        </section>
      )}
    </div>
  );
};

TestStep.displayName = 'TestStep';
