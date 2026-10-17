/**
 * The Rules area with the rule wizard (task 102; FR-57, FR-58): "New rule" on the list and "New version" on a rule's page open the
 * wizard in-app, only when the host passes an authoring service; a rule that was written reloads what is behind the wizard; a rule
 * that is retired or inactive offers no New version (the BFF refuses to publish to it).
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RulesArea } from '../../Rules/RulesArea';
import type { OntologyRulesService, RuleDetail, RuleListResponse, RuleSummary } from '../../Rules/rulesService';
import { failure, makeAuthoringService } from './fixtures';

jest.setTimeout(120000);

const WAIT = { timeout: 20000 };
const find = (id: string) => screen.findByTestId(id, undefined, WAIT);
const byId = (id: string) => screen.getByTestId(id);
const change = (id: string, value: string) => fireEvent.change(byId(id), { target: { value } });
const click = (id: string) => fireEvent.click(byId(id));

const metrics = (over: Partial<NonNullable<RuleSummary['metrics']>> = {}): NonNullable<RuleSummary['metrics']> => ({
  openItems: 2,
  lastFiredOn: '2026-10-15T09:30:00Z',
  actionRate: { acted: 5, dismissed: 3, rate: 0.625, tooFewToJudge: false },
  needsALook: false,
  ...over,
});

const summary = (over: Partial<RuleSummary> = {}): RuleSummary => ({
  policyId: '11111111-1111-4111-8111-111111111111',
  code: 'POL-A',
  name: 'Rule A',
  lane: 'Do',
  state: 'On',
  enabled: true,
  versionNumber: 2,
  headline: 'Open task past its due date',
  severity: 'Warning',
  subjectType: 'Event',
  metrics: metrics(),
  ...over,
});

const allRead = { openItems: true, lastFired: true, actionRate: true };

const list = (rules: RuleSummary[], over: Partial<RuleListResponse> = {}): RuleListResponse => ({
  rules,
  metricsAvailable: true,
  metricsAvailability: allRead,
  actionRateWindowDays: 90,
  lastFiredWindowDays: 90,
  minimumJudged: 5,
  ...over,
});

const detail = (over: Partial<RuleDetail> = {}): RuleDetail => ({
  summary: summary(),
  condition: {
    ruleType: 'Existence',
    clauses: [
      {
        clauseId: 'c1',
        kind: 'when',
        text: 'The task is open and past due',
        formal: 'statuscode = 1 and sprk_duedate <= now-1d',
      },
    ],
    refusal: null,
  },
  wording: {
    headline: 'Open task past its due date',
    messageTemplate: 'This task is open.',
    proposedAction: 'mark-complete: finish it.',
  },
  plan: {
    actions: [{ code: 'mark-complete', label: 'Mark complete', effectLines: [] }],
    nextSteps: [],
    dismissalReasons: ['Not relevant'],
    refusal: null,
  },
  scope: { tenant: null, matterId: null, matterName: null, priority: 500 },
  noise: { countsTowardSuppression: true, suppressAfterDismissals: 3, suppressionWindowDays: 30, quietWindowDays: 14 },
  behaviour: {
    lane: 'Do',
    severity: 'Warning',
    workType: 'Finish or reschedule',
    subjectType: 'Event',
    state: 'On',
    retiredReason: null,
  },
  versions: [
    {
      versionId: 'v2',
      number: 2,
      headline: 'Open task past its due date',
      authoredBy: 'Ada Author',
      createdOn: '2026-10-14T17:30:00Z',
      inForceFrom: '2026-10-14T00:00:00Z',
      inForceTo: null,
      inForce: true,
    },
    {
      versionId: 'v1',
      number: 1,
      headline: 'Old',
      authoredBy: 'Ada Author',
      createdOn: '2026-10-01T10:00:00Z',
      inForceFrom: null,
      inForceTo: '2026-10-14T00:00:00Z',
      inForce: false,
    },
  ],
  history: [{ when: '2026-10-14T17:30:00Z', by: 'Ada Author', event: 'Version 2 authored' }],
  metricsAvailable: true,
  metricsAvailability: allRead,
  actionRateWindowDays: 90,
  lastFiredWindowDays: 90,
  minimumJudged: 5,
  ...over,
});

const makeRules = (over: Partial<OntologyRulesService> = {}): OntologyRulesService => ({
  list: jest.fn(async () => ({ ok: true as const, data: list([summary()]) })),
  get: jest.fn(async () => ({ ok: true as const, data: detail() })),
  setEnabled: jest.fn(),
  retire: jest.fn(),
  ...over,
});

const renderArea = (rules: OntologyRulesService, withWizard = true, authoring = makeAuthoringService()) => {
  render(
    <FluentProvider theme={webLightTheme}>
      <RulesArea service={rules} authoringService={withWizard ? authoring : undefined} />
    </FluentProvider>
  );
  return authoring;
};

describe('RulesArea - the rule wizard', () => {
  it('offers neither New rule nor New version without an authoring service', async () => {
    renderArea(makeRules(), false);
    await find('rule-row-POL-A');
    expect(screen.queryByTestId('rules-new-rule')).toBeNull();

    click('rule-row-POL-A');
    await find('ontology-rule-page');
    expect(screen.queryByTestId('rule-new-version-button')).toBeNull();
  });

  it('opens the wizard in-app from New rule and closes it again without writing anything', async () => {
    const rules = makeRules();
    renderArea(rules);
    await find('rule-row-POL-A');

    click('rules-new-rule');
    await find('rule-wizard-basics');
    // Fluent's focus manager marks the dialog aria-hidden while the page behind it is mounted in jsdom; it is still the dialog.
    expect(screen.getByRole('dialog', { hidden: true })).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).toBeNull());
    // Nothing was written, so the list behind it was not reloaded.
    expect(rules.list).toHaveBeenCalledTimes(1);
  });

  it('opens the wizard on a new version from the rule page, with the identity of the rule fixed', async () => {
    renderArea(makeRules());
    await find('rule-row-POL-A');
    click('rule-row-POL-A');
    await find('ontology-rule-page');

    click('rule-new-version-button');
    const basics = await find('rule-wizard-basics');
    expect(basics).toHaveTextContent('POL-A');
    expect(screen.queryByTestId('rule-wizard-code')).toBeNull();
  });

  it('offers no New version on a retired or inactive rule', async () => {
    for (const state of ['Retired', 'Inactive']) {
      const rules = makeRules({
        get: jest.fn(async () => ({
          ok: true as const,
          data: detail({ summary: summary({ state, enabled: false }), behaviour: { ...detail().behaviour, state } }),
        })),
      });
      const { unmount } = render(
        <FluentProvider theme={webLightTheme}>
          <RulesArea service={rules} authoringService={makeAuthoringService()} />
        </FluentProvider>
      );
      await find('rule-row-POL-A');
      click('rule-row-POL-A');
      await find('ontology-rule-page');
      expect(screen.queryByTestId('rule-new-version-button')).toBeNull();
      unmount();
    }
  });

  it('a new rule that was saved shows in the list: the list is read again when the wizard is done', async () => {
    const rules = makeRules();
    const authoring = renderArea(rules);
    await find('rule-row-POL-A');

    click('rules-new-rule');
    await find('rule-wizard-basics');
    change('rule-wizard-code', 'POL-ZZ');
    change('rule-wizard-name', 'Zed rule');
    change('rule-wizard-lane', 'Decide');
    for (const arrives of ['rule-wizard-condition']) {
      click('rule-wizard-next');
      await find(arrives);
    }
    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-next');
    await find('rule-wizard-wording');
    change('rule-wizard-headline', 'A headline');
    change('rule-wizard-message', 'About it.');
    change('rule-wizard-proposed-action', 'Do the thing');
    for (const arrives of ['rule-wizard-actions']) {
      click('rule-wizard-next');
      await find(arrives);
    }
    change('rule-wizard-code-frobnicate-role', 'action');
    for (const arrives of ['rule-wizard-scope', 'rule-wizard-test']) {
      click('rule-wizard-next');
      await find(arrives);
    }
    click('rule-wizard-run-test');
    await find('rule-wizard-test-result');
    click('rule-wizard-next');
    await find('rule-wizard-save');

    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-saved');
    expect(authoring.savePolicy).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Done', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).toBeNull());
    await waitFor(() => expect(rules.list).toHaveBeenCalledTimes(2));
  });

  it('a save whose outcome is unknown reloads the list when the wizard is closed, without the discard question', async () => {
    const rules = makeRules();
    const savePolicy = jest.fn(async () =>
      failure({ status: 503, outcomeUnknown: true, message: 'The change may not have been saved. Reload to check.' })
    );
    renderArea(rules, true, makeAuthoringService({ savePolicy }));
    await find('rule-row-POL-A');

    click('rules-new-rule');
    await find('rule-wizard-basics');
    change('rule-wizard-code', 'POL-ZZ');
    change('rule-wizard-name', 'Zed rule');
    change('rule-wizard-lane', 'Decide');
    click('rule-wizard-next');
    await find('rule-wizard-condition');
    change('rule-wizard-subject', 'zz_widget');
    click('rule-wizard-next');
    await find('rule-wizard-wording');
    change('rule-wizard-headline', 'A headline');
    change('rule-wizard-message', 'About it.');
    change('rule-wizard-proposed-action', 'Do the thing');
    click('rule-wizard-next');
    await find('rule-wizard-actions');
    change('rule-wizard-code-frobnicate-role', 'action');
    for (const arrives of ['rule-wizard-scope', 'rule-wizard-test']) {
      click('rule-wizard-next');
      await find(arrives);
    }
    click('rule-wizard-run-test');
    await find('rule-wizard-test-result');
    click('rule-wizard-next');
    await find('rule-wizard-save');
    fireEvent.click(screen.getByRole('button', { name: 'Save rule', hidden: true }));
    await find('rule-wizard-write-lock');
    expect(rules.list).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Cancel', hidden: true }));
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).toBeNull());
    expect(screen.queryByRole('alertdialog', { hidden: true })).toBeNull();
    await waitFor(() => expect(rules.list).toHaveBeenCalledTimes(2));
  });
});
