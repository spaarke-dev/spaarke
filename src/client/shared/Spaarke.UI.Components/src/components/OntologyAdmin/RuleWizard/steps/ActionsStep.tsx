/**
 * ActionsStep - step 4 of the rule wizard: what can be done about a signal (spaarke-ontology-platform-r1 task 102; D-22, D-149).
 *
 * Every code, its label and what it cannot be combined with come from the catalog's entry for the rule's lane. The wizard holds no
 * action list of its own. A code is shown ONCE with a role to give it (an action, or a next step), so it can never be offered, or
 * chosen, in both places (the plan resolver refuses a code in both). A code that cannot be combined with one already chosen is
 * disabled, and the rows say what each cannot be combined with. The dismissal reasons are the lane's and are shown for information:
 * a rule does not choose them.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Select, Text } from '@fluentui/react-components';
import { ArrowDownRegular, ArrowUpRegular } from '@fluentui/react-icons';
import type { CatalogAction } from '../authoringService';
import { laneOf } from '../ruleDraft';
import { errorMessage, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';

type Role = '' | 'action' | 'nextStep';

/** One entry per code across the lane's actions and next steps, in catalog order, with the roles the catalog allows it. */
interface CodeEntry {
  entry: CatalogAction;
  isAction: boolean;
  isNextStep: boolean;
}

function entriesOf(actions: readonly CatalogAction[], nextSteps: readonly CatalogAction[]): CodeEntry[] {
  const byCode = new Map<string, CodeEntry>();
  for (const a of actions) {
    byCode.set(a.code, { entry: a, isAction: true, isNextStep: false });
  }
  for (const n of nextSteps) {
    const existing = byCode.get(n.code);
    if (existing === undefined) {
      byCode.set(n.code, { entry: n, isAction: false, isNextStep: true });
    } else {
      existing.isNextStep = true;
    }
  }
  return Array.from(byCode.values());
}

export const ActionsStep: React.FC<StepBaseProps> = ({ catalog, draft, onChange, errors, locked }) => {
  const styles = useStepStyles();
  const lane = laneOf(catalog, draft.lane);
  const planError = errorMessage(errors, 'decisionPlan');

  if (lane === undefined) {
    return (
      <div className={styles.step} data-testid="rule-wizard-actions">
        <Text className={styles.muted}>Choose a lane in Basics first. The actions depend on the lane.</Text>
      </div>
    );
  }

  const entries = entriesOf(lane.actions, lane.nextSteps);
  const labelOf = (code: string): string => {
    const found = entries.find(e => e.entry.code === code);
    return found === undefined ? code : found.entry.label;
  };
  const chosen = [...draft.actions, ...draft.nextSteps];

  const roleOf = (code: string): Role => (draft.actions.includes(code) ? 'action' : draft.nextSteps.includes(code) ? 'nextStep' : '');

  /** The chosen codes that cannot be combined with `code` (the catalog says the exclusion is mutual, so either side may name the other). */
  const blockedBy = (code: string, excludes: readonly string[]): string[] =>
    chosen.filter(c => {
      if (c === code) {
        return false;
      }
      const other = entries.find(e => e.entry.code === c);
      return excludes.includes(c) || (other !== undefined && other.entry.excludes.includes(code));
    });

  const setRole = (code: string, role: Role) =>
    onChange(d => {
      const actions = d.actions.filter(c => c !== code);
      const nextSteps = d.nextSteps.filter(c => c !== code);
      if (role === 'action') {
        actions.push(code);
      } else if (role === 'nextStep') {
        nextSteps.push(code);
      }
      return { ...d, actions, nextSteps };
    });

  const move = (code: string, by: -1 | 1) =>
    onChange(d => {
      const at = d.actions.indexOf(code);
      const to = at + by;
      if (at < 0 || to < 0 || to >= d.actions.length) {
        return d;
      }
      const actions = [...d.actions];
      actions[at] = actions[to];
      actions[to] = code;
      return { ...d, actions };
    });

  return (
    <div className={styles.step} data-testid="rule-wizard-actions">
      <Text className={styles.intro}>
        Choose what a person can do when this rule raises a signal. Each choice is used once: as an action or as a next step. The first
        action is the one recommended.
      </Text>

      {planError ? (
        <Text role="alert" className={styles.error} data-testid="rule-wizard-actions-refusal">
          {planError}
        </Text>
      ) : null}

      <section className={styles.group} aria-label="What can be done" data-testid="rule-wizard-code-list">
        {entries.length === 0 ? <Text className={styles.muted}>The service lists no actions for this lane.</Text> : null}
        {entries.map(({ entry, isAction, isNextStep }) => {
          const role = roleOf(entry.code);
          const blockers = blockedBy(entry.code, entry.excludes);
          // A code already chosen can always be changed or cleared; a code that conflicts with a chosen one cannot be newly chosen.
          const disabled = locked || (role === '' && blockers.length > 0);
          return (
            <div key={entry.code} className={styles.group} data-testid={`rule-wizard-code-${entry.code}`}>
              <div className={styles.inline}>
                <Text weight="semibold">{entry.label}</Text>
                <Select
                  value={role}
                  disabled={disabled}
                  aria-label={`Use ${entry.label} as`}
                  onChange={(_event, data) => setRole(entry.code, data.value as Role)}
                  data-testid={`rule-wizard-code-${entry.code}-role`}
                >
                  <option value="">Not used</option>
                  {isAction ? <option value="action">Action</option> : null}
                  {isNextStep ? <option value="nextStep">Next step</option> : null}
                </Select>
              </div>
              {entry.excludes.length > 0 ? (
                <Text size={200} className={styles.muted} data-testid={`rule-wizard-code-${entry.code}-excludes`}>
                  Cannot be combined with: {entry.excludes.map(labelOf).join(', ')}.
                </Text>
              ) : null}
              {role === '' && blockers.length > 0 ? (
                <Text size={200} className={styles.muted} data-testid={`rule-wizard-code-${entry.code}-blocked`}>
                  Not available with {blockers.map(labelOf).join(', ')}.
                </Text>
              ) : null}
            </div>
          );
        })}
      </section>

      {draft.actions.length > 1 ? (
        <section className={styles.group} aria-label="Order of the actions" data-testid="rule-wizard-action-order">
          <Text weight="semibold">Order of the actions</Text>
          {draft.actions.map((code, index) => (
            <div key={code} className={styles.inline}>
              <Text>
                {index + 1}. {labelOf(code)}
              </Text>
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowUpRegular />}
                aria-label={`Move ${labelOf(code)} up`}
                disabled={locked || index === 0}
                onClick={() => move(code, -1)}
                data-testid={`rule-wizard-action-up-${code}`}
              />
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowDownRegular />}
                aria-label={`Move ${labelOf(code)} down`}
                disabled={locked || index === draft.actions.length - 1}
                onClick={() => move(code, 1)}
                data-testid={`rule-wizard-action-down-${code}`}
              />
            </div>
          ))}
        </section>
      ) : null}

      <section className={styles.group} aria-label="Dismissal reasons" data-testid="rule-wizard-dismissal-reasons">
        <Text weight="semibold">Reasons a person can give for dismissing a signal</Text>
        <Text size={200} className={styles.muted}>
          These belong to the {lane.lane} lane. A rule does not choose them.
        </Text>
        {lane.dismissalReasons.length === 0 ? (
          <Text className={styles.muted}>The service lists none for this lane.</Text>
        ) : (
          <ul className={styles.muted}>
            {lane.dismissalReasons.map(reason => (
              <li key={reason.value}>{reason.label}</li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
};

ActionsStep.displayName = 'ActionsStep';
