/**
 * ActionStepView - one step per plan action (and per ticked Next step): what it does (the catalog's own lines), its fields,
 * and - on a closed item - exactly what was sent. "Next takes it; Skip leaves it out" is the shell's footer, so this view
 * holds no buttons.
 *
 * The recommendation (the first action of the plan) is badged. An action that excludes later ones says so on its own step:
 * taking it removes them from the review. Nothing here is written; the values are a draft until Record decision.
 */

import * as React from 'react';
import { Badge, Body1, Caption1, Text, makeStyles, tokens } from '@fluentui/react-components';
import { actionScope, nextScope, fieldLabel } from '../decisionDraft';
import { describeValues, joinLabels } from '../decisionDisplay';
import { useDecisionItem } from '../decisionItemContext';
import type { DecisionAction } from '../decisionWizardTypes';
import { LockedNote, ParameterFields } from './ParameterFields';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL },
  titleRow: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  meta: { color: tokens.colorNeutralForeground3 },
  effects: { margin: 0, paddingLeft: tokens.spacingHorizontalXL },
  note: {
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground2,
  },
  values: {
    display: 'grid',
    gridTemplateColumns: 'minmax(120px, max-content) minmax(0, 1fr)',
    columnGap: tokens.spacingHorizontalM,
    rowGap: tokens.spacingVerticalXS,
    margin: 0,
  },
  valueLabel: { color: tokens.colorNeutralForeground3 },
  valueText: { margin: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' },
});

export interface ActionStepViewProps {
  kind: 'action' | 'next';
  code: string;
}

export const ActionStepView: React.FC<ActionStepViewProps> = ({ kind, code }) => {
  const { plan, draft, resolution, locked } = useDecisionItem();
  const styles = useStyles();
  const list = kind === 'action' ? plan.actions : plan.nextSteps;
  const action: DecisionAction | undefined = list.find(a => a.code === code);
  if (!action) return null;
  const scope = kind === 'action' ? actionScope(code) : nextScope(code);

  const laterExcluded =
    kind === 'action'
      ? plan.actions
          .filter((a, i) => i > plan.actions.indexOf(action) && action.excludes.includes(a.code))
          .map(a => a.label)
      : [];

  const nameOf = (parameterCode: string) => fieldLabel(draft, scope, parameterCode);
  const decided = resolution?.steps?.find(s => s.code === code);
  const followOn = resolution?.followOns?.filter(f => f.code === code) ?? [];
  const rows = describeValues(action, decided?.values ?? null, nameOf);

  return (
    <div className={styles.root} data-testid={`step-${kind}-${code}`}>
      <div className={styles.titleRow}>
        <Text as="h2" size={500} weight="semibold">
          {action.label}
        </Text>
        {kind === 'action' && action.isRecommended ? (
          <Badge appearance="filled" color="brand" data-testid="recommended-badge">
            Recommended
          </Badge>
        ) : null}
        {resolution ? (
          <Badge
            appearance="tint"
            color={(kind === 'action' ? decided?.taken : followOn.length > 0) ? 'success' : 'subtle'}
            data-testid="taken-badge"
          >
            {(kind === 'action' ? decided?.taken : followOn.length > 0) ? 'Taken' : 'Skipped'}
          </Badge>
        ) : null}
      </div>

      {action.effectLines.length > 0 ? (
        <div>
          <Text weight="semibold">What it does</Text>
          <ul className={styles.effects} data-testid="effect-lines">
            {action.effectLines.map(line => (
              <li key={line}>
                <Body1>{line}</Body1>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {resolution ? (
        <>
          {rows.length > 0 ? (
            <dl className={styles.values} data-testid="sent-values">
              {rows.map(r => (
                <React.Fragment key={r.code}>
                  <dt className={styles.valueLabel}>
                    <Caption1>{r.label}</Caption1>
                  </dt>
                  <dd className={styles.valueText}>
                    <Body1>{r.text}</Body1>
                  </dd>
                </React.Fragment>
              ))}
            </dl>
          ) : (
            <Caption1 className={styles.meta}>No values were sent for this step.</Caption1>
          )}
        </>
      ) : (
        <>
          {locked ? <LockedNote /> : null}
          {laterExcluded.length > 0 ? (
            <Body1 className={styles.note} data-testid="excludes-note">
              Taking this removes {joinLabels(laterExcluded)} from this review: they cannot be combined.
            </Body1>
          ) : null}
          <Caption1 className={styles.meta}>
            Next takes this {kind === 'action' ? 'action' : 'step'}. Skip leaves it out. Nothing is written until you
            record the decision.
          </Caption1>
          <ParameterFields action={action} scope={scope} />
        </>
      )}
    </div>
  );
};
