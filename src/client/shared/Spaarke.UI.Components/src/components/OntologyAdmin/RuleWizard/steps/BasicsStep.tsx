/**
 * BasicsStep - step 1 of the rule wizard: the rule's name, code, lane, severity and work type (spaarke-ontology-platform-r1 task 102).
 *
 * For a NEW rule these are chosen here; the code is fixed once saved. The lane, severity and work type lists are the catalog's
 * (D-149): the wizard holds none of its own. For a NEW VERSION of an existing rule nothing here changes (a version never edits its
 * rule's identity), so the step only says which rule is being versioned.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Input, Select, Text } from '@fluentui/react-components';
import type { RuleWizardMode } from '../ruleWizardTypes';
import { laneOf } from '../ruleDraft';
import { Facts, RefusableField, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';

export interface BasicsStepProps extends StepBaseProps {
  mode: RuleWizardMode;
}

export const BasicsStep: React.FC<BasicsStepProps> = ({ catalog, draft, onChange, errors, locked, mode }) => {
  const styles = useStepStyles();

  if (mode.kind === 'version') {
    return (
      <div className={styles.step} data-testid="rule-wizard-basics">
        <Text className={styles.intro}>
          You are writing a new version of this rule. A version changes how the rule reads, what it says and what can be done; the
          rule's name, code and lane stay as they are.
        </Text>
        <Facts
          rows={[
            ['Code', mode.code],
            ['Name', mode.name],
            ['Lane', mode.lane],
            ['Current version', mode.versionNumber === null ? '-' : `v${mode.versionNumber}`],
          ]}
        />
      </div>
    );
  }

  const lane = laneOf(catalog, draft.lane);

  return (
    <div className={styles.step} data-testid="rule-wizard-basics">
      <Text className={styles.intro}>
        Name the rule and say which lane it works in. The code identifies the rule and cannot be changed once it is saved.
      </Text>

      <RefusableField label="Code" field="policyCode" errors={errors} required hint="Letters, digits and hyphens. Fixed once saved.">
        <Input
          value={draft.policyCode}
          disabled={locked}
          onChange={(_event, data) => onChange(d => ({ ...d, policyCode: data.value }))}
          data-testid="rule-wizard-code"
        />
      </RefusableField>

      <RefusableField label="Name" field="name" errors={errors} required>
        <Input
          value={draft.name}
          disabled={locked}
          onChange={(_event, data) => onChange(d => ({ ...d, name: data.value }))}
          data-testid="rule-wizard-name"
        />
      </RefusableField>

      <RefusableField label="Short label" field="shortName" errors={errors} hint="Optional. Shown where the full name is too long.">
        <Input
          value={draft.shortName}
          disabled={locked}
          onChange={(_event, data) => onChange(d => ({ ...d, shortName: data.value }))}
          data-testid="rule-wizard-short-name"
        />
      </RefusableField>

      <RefusableField label="Lane" field="lane" errors={errors} required>
        <Select
          value={draft.lane}
          disabled={locked}
          onChange={(_event, data) =>
            onChange(d => {
              // The lists differ per lane: what was chosen from the old lane's lists is dropped, not carried over.
              const next = laneOf(catalog, data.value);
              return {
                ...d,
                lane: data.value,
                actions: [],
                nextSteps: [],
                severity: next?.severities.some(s => s.value === d.severity) === true ? d.severity : null,
                workType: next?.workTypes.some(w => w.value === d.workType) === true ? d.workType : null,
              };
            })
          }
          data-testid="rule-wizard-lane"
        >
          <option value="">Choose a lane</option>
          {catalog.lanes.map(l => (
            <option key={l.lane} value={l.lane}>
              {l.lane}
            </option>
          ))}
        </Select>
      </RefusableField>

      <RefusableField label="Severity" field="severity" errors={errors}>
        <Select
          value={draft.severity === null ? '' : String(draft.severity)}
          disabled={locked || lane === undefined}
          onChange={(_event, data) => onChange(d => ({ ...d, severity: data.value === '' ? null : Number(data.value) }))}
          data-testid="rule-wizard-severity"
        >
          <option value="">Not set</option>
          {(lane?.severities ?? []).map(s => (
            <option key={s.value} value={String(s.value)}>
              {s.label}
            </option>
          ))}
        </Select>
      </RefusableField>

      <RefusableField label="Work type" field="workType" errors={errors}>
        <Select
          value={draft.workType === null ? '' : String(draft.workType)}
          disabled={locked || lane === undefined}
          onChange={(_event, data) => onChange(d => ({ ...d, workType: data.value === '' ? null : Number(data.value) }))}
          data-testid="rule-wizard-work-type"
        >
          <option value="">Not set</option>
          {(lane?.workTypes ?? []).map(w => (
            <option key={w.value} value={String(w.value)}>
              {w.label}
            </option>
          ))}
        </Select>
      </RefusableField>
    </div>
  );
};

BasicsStep.displayName = 'BasicsStep';
