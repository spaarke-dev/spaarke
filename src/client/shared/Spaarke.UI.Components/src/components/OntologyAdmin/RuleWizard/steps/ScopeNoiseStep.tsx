/**
 * ScopeNoiseStep - step 5 of the rule wizard: where the rule applies and how much noise it may make (spaarke-ontology-platform-r1
 * task 102; D-22).
 *
 * The save route takes no tenant or matter, so this step authors no scope: it says so, and for a new version it shows the scope the
 * rule already has (a version never changes it). Priority and "counts toward suppression" are set when a rule is CREATED; for a new
 * version they are shown, not edited. The quiet window is part of the rule body, so a new version can change it; it applies to the
 * lane whose dismissed items wait before they can be raised again, and the other lane has none.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Input, Switch, Text } from '@fluentui/react-components';
import { priorityProblem, QUIET_WINDOW_LANE, quietWindowProblem } from '../ruleDraft';
import type { RuleWizardMode } from '../ruleWizardTypes';
import { Facts, RefusableField, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';

export interface ScopeNoiseStepProps extends StepBaseProps {
  mode: RuleWizardMode;
}

export const ScopeNoiseStep: React.FC<ScopeNoiseStepProps> = ({ catalog, draft, onChange, errors, locked, mode }) => {
  const styles = useStepStyles();
  const hasQuietWindow = draft.lane === QUIET_WINDOW_LANE;

  return (
    <div className={styles.step} data-testid="rule-wizard-scope">
      <Text className={styles.intro}>
        {mode.kind === 'new'
          ? 'Set how the rule is ranked and how its dismissals are counted.'
          : "A new version keeps the rule's scope, priority and how its dismissals are counted."}
      </Text>

      <section className={styles.group} aria-label="Scope" data-testid="rule-wizard-scope-facts">
        <Text weight="semibold">Scope</Text>
        {mode.kind === 'new' ? (
          <Text className={styles.muted}>
            This wizard does not set a scope. A new rule is saved without a tenant or matter limit.
          </Text>
        ) : (
          <Facts
            rows={[
              ['Tenant', mode.scope.tenant === null ? 'None' : mode.scope.tenant],
              ['Matter', mode.scope.matter === null ? 'None' : mode.scope.matter],
            ]}
          />
        )}
      </section>

      {mode.kind === 'new' ? (
        <>
          <RefusableField
            label="Priority"
            field="priority"
            errors={errors}
            localProblem={priorityProblem(draft.priority)}
            hint="Optional. A whole number; the lowest wins. Leave it empty to use the service's default."
          >
            <Input
              value={draft.priority}
              disabled={locked}
              inputMode="numeric"
              onChange={(_event, data) => onChange(d => ({ ...d, priority: data.value }))}
              data-testid="rule-wizard-priority"
            />
          </RefusableField>

          <RefusableField label="Counts toward muting" field="countsTowardSuppression" errors={errors}>
            <Switch
              checked={draft.countsTowardSuppression}
              disabled={locked}
              label={draft.countsTowardSuppression ? 'On: dismissals count toward the noise limits' : 'Off: dismissals are not counted'}
              onChange={(_event, data) => onChange(d => ({ ...d, countsTowardSuppression: data.checked }))}
              data-testid="rule-wizard-counts-toward-suppression"
            />
          </RefusableField>
        </>
      ) : (
        <Facts
          rows={[
            ['Priority', mode.priority === null ? '-' : String(mode.priority)],
            ['Counts toward muting', mode.countsTowardSuppression ? 'Yes' : 'No'],
          ]}
        />
      )}

      {hasQuietWindow ? (
        <RefusableField
          label="Quiet window (days)"
          field="quietWindowDays"
          errorField="ruleBody"
          errors={errors}
          localProblem={quietWindowProblem(draft.quietWindowDays, catalog)}
          hint={`After a signal is dismissed it waits this many days before it can be raised again. Leave it empty for ${catalog.limits.defaultQuietWindowDays}. The most allowed is ${catalog.limits.maxQuietWindowDays}.`}
        >
          <Input
            value={draft.quietWindowDays}
            disabled={locked}
            inputMode="numeric"
            onChange={(_event, data) => onChange(d => ({ ...d, quietWindowDays: data.value }))}
            data-testid="rule-wizard-quiet-window"
          />
        </RefusableField>
      ) : (
        <Text size={200} className={styles.muted} data-testid="rule-wizard-no-quiet-window">
          {draft.lane === ''
            ? 'The quiet window depends on the lane. Choose a lane in Basics.'
            : `A dismissed item in the ${draft.lane} lane is raised again when its date changes, so this lane has no quiet window.`}
        </Text>
      )}
    </div>
  );
};

ScopeNoiseStep.displayName = 'ScopeNoiseStep';
