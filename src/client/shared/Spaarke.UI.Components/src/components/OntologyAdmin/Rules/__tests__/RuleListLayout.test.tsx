/**
 * Rules list layout contract (task 201): the Rule column shows the whole rule code and name.
 *
 * Defect: the Rule cell carried `display: flex` (the `stack` style) on the table cell itself. That takes the cell out of the table-cell
 * layout, so Fluent's fixed 44 px cell height became a hard height, and `overflow: hidden` cut a two-line name off (measured on dev at
 * 1440x900: clientHeight 44, scrollHeight 64, width 120 px). The fix stacks the code over the name in a wrapper INSIDE a normal table
 * cell, so the row grows to fit.
 *
 * jsdom cannot measure layout, so this pins what it can see: the computed style of the cell and the column widths from the stylesheet
 * Griffel injects, and that the full code and name are in the DOM.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { RuleList } from '../RuleList';
import type { OntologyRulesService, RuleListResponse, RuleSummary } from '../rulesService';

const LONG_CODE = 'POL-COMMIT-BUDGET-VARIANCE-WITH-A-VERY-LONG-CODE-THAT-HAS-NO-BREAK-POINTS-AT-ALL';
const LONG_NAME = 'Fee or scope change with no budget revision in 30 days';

const summary = (over: Partial<RuleSummary> = {}): RuleSummary => ({
  policyId: '11111111-1111-4111-8111-111111111111',
  code: LONG_CODE,
  name: LONG_NAME,
  lane: 'Decide',
  state: 'On',
  enabled: true,
  versionNumber: 2,
  headline: null,
  severity: 'Warning',
  subjectType: 'Event',
  metrics: {
    openItems: 2,
    lastFiredOn: '2026-10-15T09:30:00Z',
    actionRate: { acted: 5, dismissed: 3, rate: 0.625, tooFewToJudge: false },
    needsALook: true,
  },
  ...over,
});

const response: RuleListResponse = {
  rules: [summary()],
  metricsAvailable: true,
  metricsAvailability: { openItems: true, lastFired: true, actionRate: true },
  actionRateWindowDays: 90,
  lastFiredWindowDays: 90,
  minimumJudged: 5,
};

const service = {
  list: jest.fn(async () => ({ ok: true as const, data: response })),
} as unknown as OntologyRulesService;

const renderList = () =>
  render(
    <FluentProvider theme={webLightTheme}>
      <RuleList service={service} onOpen={jest.fn()} />
    </FluentProvider>
  );

const widthOf = (el: Element): number => parseFloat(getComputedStyle(el).width);

describe('RuleList layout (task 201)', () => {
  it('renders the full rule code and the full rule name', async () => {
    renderList();
    const row = await screen.findByTestId(`rule-row-${LONG_CODE}`);
    expect(row).toHaveTextContent(LONG_CODE);
    expect(row).toHaveTextContent(LONG_NAME);
  });

  it.each([
    ['Rule', `rule-open-${LONG_CODE}`],
    ['Action rate', 'rule-action-rate'],
  ])(
    'the %s cell stays a table cell and is not clipped by a fixed height with overflow hidden',
    async (_label, testId) => {
      renderList();
      await screen.findByTestId(`rule-row-${LONG_CODE}`);
      const target = screen.getByTestId(testId);
      const cell = (target.matches('td, [role="cell"]') ? target : target.closest('td, [role="cell"]')) as HTMLElement;
      expect(cell).not.toBeNull();
      const style = getComputedStyle(cell);
      // `display: flex` on the cell itself is what turned Fluent's 44 px cell height into a hard height.
      expect(style.display).toBe('table-cell');
      expect(style.overflow).not.toBe('hidden');
      // the stack is a wrapper inside the cell and carries no height or clip either
      const stack = cell.querySelector('[data-stack]') as HTMLElement;
      expect(stack).not.toBeNull();
      expect(getComputedStyle(stack).overflow).not.toBe('hidden');
      expect(getComputedStyle(stack).height).toBe('');
    }
  );

  it('gives the Rule column the largest share of the width', async () => {
    renderList();
    await screen.findByTestId(`rule-row-${LONG_CODE}`);
    const headers = screen.getAllByRole('columnheader');
    const widths = headers.map(widthOf);
    expect(widths).toHaveLength(7);
    expect(widths.every(w => !Number.isNaN(w))).toBe(true);
    expect(widths[0]).toBe(Math.max(...widths));
  });
});
