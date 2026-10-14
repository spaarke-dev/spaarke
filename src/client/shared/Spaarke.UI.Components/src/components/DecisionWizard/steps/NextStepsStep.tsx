/**
 * NextStepsStep - the Next-step cards (FollowOnGrid, used as is). Ticking a card adds its own step before Confirm; unticking
 * removes it. The steps only COLLECT values: the commit route creates the rows when the decision is recorded (D-17), and says
 * so here. A closed item lists what the record created instead of offering cards.
 */

import * as React from 'react';
import { Body1, Caption1, Text, makeStyles, tokens } from '@fluentui/react-components';
import { FollowOnGrid } from '../../WizardFollowOns/FollowOnGrid';
import type { FollowOnCardConfig } from '../../WizardFollowOns/followOnTypes';
import type { IWizardShellHandle } from '../../Wizard/wizardShellTypes';
import { withNextSelected } from '../decisionDraft';
import { useDecisionItem } from '../decisionItemContext';
import { LockedNote } from './ParameterFields';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM },
  list: { margin: 0, paddingLeft: tokens.spacingHorizontalXL },
  meta: { color: tokens.colorNeutralForeground3 },
});

export interface NextStepsStepProps {
  handle: IWizardShellHandle;
  cards: FollowOnCardConfig[];
  /** Step ids in plan order, Confirm last: the one canonical order every add uses. */
  canonicalOrder: string[];
}

export const NextStepsStep: React.FC<NextStepsStepProps> = ({ handle, cards, canonicalOrder }) => {
  const { draft, update, resolution, plan, locked } = useDecisionItem();
  const styles = useStyles();

  if (resolution) {
    const made = resolution.followOns ?? [];
    return (
      <div className={styles.root} data-testid="step-next">
        <Text as="h2" size={500} weight="semibold">
          Next steps
        </Text>
        {made.length === 0 ? (
          <Caption1 className={styles.meta}>No Next steps were added to this decision.</Caption1>
        ) : (
          <ul className={styles.list} data-testid="created-next-steps">
            {made.map((f, i) => (
              <li key={`${f.code}-${f.id ?? i}`}>
                <Body1>{plan.nextSteps.find(n => n.code === f.code)?.label ?? f.code}</Body1>
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  }

  if (locked) {
    const added = plan.nextSteps.filter(n => draft.nextSelected.includes(n.code));
    return (
      <div className={styles.root} data-testid="step-next">
        <Text as="h2" size={500} weight="semibold">
          Next steps
        </Text>
        <LockedNote />
        {added.length === 0 ? (
          <Caption1 className={styles.meta}>No Next steps were added to this decision.</Caption1>
        ) : (
          <ul className={styles.list}>
            {added.map(n => (
              <li key={n.code}>
                <Body1>{n.label}</Body1>
              </li>
            ))}
          </ul>
        )}
      </div>
    );
  }

  return (
    <div data-testid="step-next">
      <FollowOnGrid
        cards={cards}
        selected={draft.nextSelected}
        onSelectionChange={selected => update(d => withNextSelected(d, selected))}
        addDynamicStep={handle.addDynamicStep}
        removeDynamicStep={handle.removeDynamicStep}
        canonicalOrder={canonicalOrder}
        title="Next steps"
        subtitle="Add work that follows from this decision. Each card adds a step; the work is created only when you record the decision."
        emptyHint="No Next steps added. Continue to Confirm."
      />
    </div>
  );
};
