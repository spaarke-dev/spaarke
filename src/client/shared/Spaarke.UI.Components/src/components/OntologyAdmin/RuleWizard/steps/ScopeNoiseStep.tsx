/**
 * ScopeNoiseStep - step 5 of the rule wizard: where the rule applies and how much noise it may make (spaarke-ontology-platform-r1
 * task 102; D-22).
 *
 * Scope (task 102b, D-165): a rule about matters can be limited to ONE matter, picked with the shared `LookupField` over the host's
 * `MatterPicker` (its search runs as the caller; the wizard holds no matter list, names no table and sends only the id), or apply to all
 * matters. The picker is offered only for a rule about the picker's subject; a limit left set otherwise is shown with the BFF's refusal
 * (`scope_subject_mismatch`, placed on this field) and can be removed. A new version starts with the
 * rule's current scope and can change it. There is no tenant scope (every environment is single-tenant). Priority and "counts toward
 * suppression" are set when a rule is CREATED; for a new version they are shown, not edited. The quiet window is part of the rule body, so a new version can change it; it applies to the
 * lane whose dismissed items wait before they can be raised again, and the other lane has none.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Input, Switch, Text } from '@fluentui/react-components';
import type { ILookupItem } from '../../../../types/LookupTypes';
import { LookupField } from '../../../LookupField/LookupField';
import { priorityProblem, QUIET_WINDOW_LANE, quietWindowProblem } from '../ruleDraft';
import type { MatterPicker, RuleWizardMode } from '../ruleWizardTypes';
import { errorMessage, Facts, RefusableField, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';

export interface ScopeNoiseStepProps extends StepBaseProps {
  mode: RuleWizardMode;
  /** The host's matter picker (task 102b). Without it the step shows the scope and can remove a limit, but offers no picker. */
  matterPicker?: MatterPicker;
}

/** What the scope reads as: the matter's name (its id when no name is known), or all matters. */
function scopeText(matterId: string | null, matterName: string | null): string {
  if (matterId === null) {
    return 'All matters';
  }
  return matterName === null || matterName === '' ? matterId : matterName;
}

export const ScopeNoiseStep: React.FC<ScopeNoiseStepProps> = ({
  catalog,
  draft,
  onChange,
  errors,
  locked,
  mode,
  matterPicker,
}) => {
  const styles = useStepStyles();
  const hasQuietWindow = draft.lane === QUIET_WINDOW_LANE;
  const aboutMatters = matterPicker !== undefined && draft.condition.subject === matterPicker.subject;
  const scopeError = errorMessage(errors, 'matterId');
  const setMatter = (item: ILookupItem | null) =>
    onChange(d => ({ ...d, matterId: item === null ? null : item.id, matterName: item === null ? null : item.name }));

  return (
    <div className={styles.step} data-testid="rule-wizard-scope">
      <Text className={styles.intro}>
        {mode.kind === 'new'
          ? 'Set which matters the rule applies to, how it is ranked and how its dismissals are counted.'
          : "A new version can change which matters the rule applies to. It keeps the rule's priority and how its dismissals are counted."}
      </Text>

      <section className={styles.group} aria-label="Scope" data-testid="rule-wizard-scope-facts">
        <Text weight="semibold">Scope</Text>
        {mode.kind === 'version' ? (
          <Facts
            rows={
              mode.scope.tenant === null
                ? [['In force now', scopeText(mode.scope.matterId, mode.scope.matter)]]
                : [
                    ['In force now', scopeText(mode.scope.matterId, mode.scope.matter)],
                    // Written outside the wizard; a tenant scope is not evaluated (every environment is single-tenant).
                    ['Tenant', mode.scope.tenant],
                  ]
            }
          />
        ) : null}
        {matterPicker !== undefined && aboutMatters && !locked ? (
          <div data-testid="rule-wizard-matter-picker">
            <LookupField
              label="Limit to one matter"
              placeholder="Search matters by name"
              value={
                draft.matterId === null
                  ? null
                  : { id: draft.matterId, name: scopeText(draft.matterId, draft.matterName) }
              }
              onChange={setMatter}
              onSearch={matterPicker.search}
            />
          </div>
        ) : null}
        <Text data-testid="rule-wizard-scope-value">Applies to: {scopeText(draft.matterId, draft.matterName)}</Text>
        {matterPicker === undefined ? (
          <Text size={200} className={styles.muted} data-testid="rule-wizard-scope-no-picker">
            Matters cannot be searched here, so a matter limit cannot be chosen.
          </Text>
        ) : aboutMatters ? (
          <Text size={200} className={styles.muted}>
            Leave it empty and the rule applies to all matters.
          </Text>
        ) : (
          <Text size={200} className={styles.muted} data-testid="rule-wizard-scope-not-offered">
            {draft.matterId === null
              ? 'A matter limit is offered only for a rule about matters.'
              : 'A matter limit is offered only for a rule about matters. Remove it, or make the rule about matters in Condition.'}
          </Text>
        )}
        {!aboutMatters && draft.matterId !== null && !locked ? (
          <div>
            <Button
              appearance="secondary"
              size="small"
              onClick={() => setMatter(null)}
              data-testid="rule-wizard-scope-clear"
            >
              Remove the matter limit
            </Button>
          </div>
        ) : null}
        {scopeError === undefined ? null : (
          <Text className={styles.error} role="alert">
            {scopeError}
          </Text>
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
              label={
                draft.countsTowardSuppression
                  ? 'On: dismissals count toward the noise limits'
                  : 'Off: dismissals are not counted'
              }
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
