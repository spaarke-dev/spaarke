/**
 * Shared test helpers for the decision wizard tests: service / lookup / loader doubles and one render function.
 * Not a test file (no `.test.`), so jest does not run it and the package build excludes it.
 */

import * as React from 'react';
import { render } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { ILookupItem } from '../../../types/LookupTypes';
import { DecisionWizard } from '../DecisionWizard';
import type { DecisionWizardProps } from '../DecisionWizard';
import { BUDGET_A, CONTACT, TODAY, commitResult, suppression } from '../__fixtures__/decisionWizardFixtures';
import type {
  DecisionLookupRequest,
  DecisionPlan,
  DecisionWizardItem,
  DecisionWizardService,
  SuppressionStatus,
} from '../decisionWizardTypes';

export function makeLookup(budgets: ILookupItem[] = [BUDGET_A]) {
  return jest.fn(async (request: DecisionLookupRequest): Promise<ILookupItem[]> => {
    if (request.kind === 'Person') return [CONTACT];
    if (request.entity === 'sprk_budget') return budgets;
    if (request.entity === 'sprk_organization')
      return [{ id: 'ffffffff-0000-0000-0000-000000000001', name: 'Acme LLP' }];
    return [];
  });
}

export interface FakeServiceOptions {
  plans: Record<string, DecisionPlan>;
  suppressions?: Record<string, SuppressionStatus>;
}

/** A service double: plans by id, a settled commit by default. Every call is a jest.fn. */
export function makeService(options: FakeServiceOptions) {
  const service: jest.Mocked<DecisionWizardService> = {
    getPlan: jest.fn(async (id: string) => {
      const plan = options.plans[id];
      if (!plan) throw new Error(`No plan for ${id}`);
      return plan;
    }),
    getSuppression: jest.fn(async (id: string) => options.suppressions?.[id] ?? suppression({ signalId: id })),
    commit: jest.fn(async () => commitResult()),
  };
  return service;
}

export function makeLoader(items: DecisionWizardItem[]) {
  const byId = new Map(items.map(i => [i.row.signalId, i]));
  return jest.fn(async (id: string) => {
    const found = byId.get(id);
    if (!found) throw new Error(`No item ${id}`);
    return found;
  });
}

export type WizardTestProps = Partial<DecisionWizardProps> &
  Pick<DecisionWizardProps, 'service' | 'loadItem' | 'itemIds' | 'startItemId'>;

export function renderWizard(props: WizardTestProps) {
  const onClose = jest.fn();
  const onRecorded = jest.fn();
  const all: DecisionWizardProps = { onClose, onRecorded, lookup: makeLookup(), today: TODAY, ...props };
  const view = render(
    <FluentProvider theme={webLightTheme}>
      <DecisionWizard {...all} />
    </FluentProvider>
  );
  return { ...view, onClose: all.onClose, onRecorded: all.onRecorded as jest.Mock };
}
