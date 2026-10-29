/**
 * SaveStep - step 7 of the rule wizard: read it through, then save or publish (spaarke-ontology-platform-r1 task 102; D-22, FR-10).
 *
 * A NEW rule is saved Off: nothing is evaluated or raised until the admin turns it On from the rule's page. A NEW VERSION is
 * published: it becomes the version in force, its open items are replaced and the ones that still hold are raised again (the counts
 * come from the test and are confirmed before it happens). The button is the wizard footer's (Save rule / Publish), enabled only
 * while the last test is for exactly this rule.
 *
 * Any refusal the BFF made about a field the wizard has no control for is listed here, so nothing is lost.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Text } from '@fluentui/react-components';
import type { CatalogAction, DryRunResult } from '../authoringService';
import { laneOf, QUIET_WINDOW_LANE } from '../ruleDraft';
import type { RuleWizardMode } from '../ruleWizardTypes';
import { Facts, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';

export interface SaveStepProps extends StepBaseProps {
  mode: RuleWizardMode;
  /** The test for this exact draft, or null when there is none (or it is stale). */
  testResult: DryRunResult | null;
}

export const SaveStep: React.FC<SaveStepProps> = ({ catalog, draft, errors, mode, testResult }) => {
  const styles = useStepStyles();
  const lane = laneOf(catalog, draft.lane);
  const labelOf = (list: readonly CatalogAction[], code: string): string => {
    const found = list.find(a => a.code === code);
    return found === undefined ? code : found.label;
  };
  const allCodes: CatalogAction[] = lane === undefined ? [] : [...lane.actions, ...lane.nextSteps];
  const severity = lane === undefined ? undefined : lane.severities.find(s => s.value === draft.severity);
  const workType = lane === undefined ? undefined : lane.workTypes.find(w => w.value === draft.workType);

  const rows: Array<readonly [string, React.ReactNode]> = [];
  if (mode.kind === 'new') {
    rows.push(['Code', draft.policyCode.trim()], ['Name', draft.name.trim()], ['Lane', draft.lane]);
    rows.push(['Severity', severity === undefined ? 'Not set' : severity.label]);
    rows.push(['Work type', workType === undefined ? 'Not set' : workType.label]);
  } else {
    rows.push(['Rule', `${mode.code}: ${mode.name}`], ['Lane', mode.lane]);
  }
  rows.push(
    ['Headline', draft.shortHeadline],
    ['Message', draft.messageTemplate],
    ['Proposed action', draft.proposedAction],
    ['Actions', draft.actions.length === 0 ? 'None' : draft.actions.map(c => labelOf(allCodes, c)).join(', ')],
    ['Next steps', draft.nextSteps.length === 0 ? 'None' : draft.nextSteps.map(c => labelOf(allCodes, c)).join(', ')]
  );
  if (mode.kind === 'new') {
    rows.push(['Priority', draft.priority.trim() === '' ? "The service's default" : draft.priority.trim()]);
    rows.push(['Counts toward muting', draft.countsTowardSuppression ? 'Yes' : 'No']);
  }
  if (draft.lane === QUIET_WINDOW_LANE) {
    rows.push(['Quiet window', draft.quietWindowDays.trim() === '' ? `${catalog.limits.defaultQuietWindowDays} days (default)` : `${draft.quietWindowDays.trim()} days`]);
  }

  const stray = Object.entries(errors).flatMap(([field, messages]) => messages.map(message => `${field}: ${message}`));

  return (
    <div className={styles.step} data-testid="rule-wizard-save">
      <Text className={styles.intro}>
        {mode.kind === 'new'
          ? 'Read the rule through. Saving creates it Off: nothing is raised until you turn it On from its page.'
          : 'Read the new version through. Publishing makes it the version in force.'}
      </Text>

      <Facts rows={rows} />

      {testResult === null ? (
        <Text role="alert" className={styles.error} data-testid="rule-wizard-save-untested">
          This rule has not been tested as it stands. Go back to Test and run it.
        </Text>
      ) : (
        <Text className={styles.muted} data-testid="rule-wizard-save-tested">
          Tested: {testResult.new} would be raised, {testResult.open} already open, {testResult.suppressed} muted.
        </Text>
      )}

      {mode.kind === 'version' && !mode.enabled ? (
        <Text size={200} className={styles.muted} data-testid="rule-wizard-save-off">
          This rule is Off. The new version is published, but nothing is evaluated until the rule is turned On.
        </Text>
      ) : null}

      {stray.length > 0 ? (
        <ul className={styles.error} data-testid="rule-wizard-save-problems">
          {stray.map(line => (
            <li key={line}>{line}</li>
          ))}
        </ul>
      ) : null}
    </div>
  );
};

SaveStep.displayName = 'SaveStep';
