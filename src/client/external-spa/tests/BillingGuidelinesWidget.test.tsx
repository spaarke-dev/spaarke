/**
 * ebilling-r1 task 135 (FR-49, D-66): the Billing Guidelines widget over its REAL client (`billing-guidelines-client`) and the real
 * registry descriptor. Substituted: the network seam (`bffApiCall`, throwing `ApiError` as bff-client builds it).
 *
 *   GET /api/v1/external/ebilling/billing-guidelines[?coreRecordId=]  ->  { sets: [{ firmName, name, scope, version, effectiveFrom,
 *   effectiveTo, rules: [{ name, descriptionAvailable, description, parameters, clauseQuote, sectionReference, inForceFrom }] }] }
 */
import * as React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

const h = vi.hoisted(() => ({ bffApiCall: vi.fn() }));

vi.mock('../src/auth/bff-client', async () => {
  const actual = await vi.importActual<typeof import('../src/auth/bff-client')>('../src/auth/bff-client');
  return { ...actual, bffApiCall: h.bffApiCall };
});

import { ApiError } from '../src/types';
import type { BillingGuidelineSet, BillingGuidelinesResponse } from '../src/api/billing-guidelines-client';
import { getBillingGuidelines } from '../src/api/billing-guidelines-client';
import {
  BillingGuidelinesView,
  BillingGuidelinesWidgetBody,
  guidelineSetMeta,
} from '../src/widgets/BillingGuidelinesWidget';
import { WIDGET_DEFINITIONS, getWidgetDefinition } from '../src/registry/widgetRegistry';

const WAIT = { timeout: 12000 };
const PATH = '/api/v1/external/ebilling/billing-guidelines';
const RECORD = '00000000-0000-4000-8000-000000000135';

const problem = (status: number, extra: Record<string, unknown> = {}): string =>
  JSON.stringify({ status, title: 'refused', detail: 'server detail text that must not be shown', ...extra });

function set(over: Partial<BillingGuidelineSet> = {}): BillingGuidelineSet {
  return {
    firmName: 'Alpha LLP',
    name: 'Outside counsel guidelines',
    scope: 'Applies to all records',
    version: 3,
    effectiveFrom: '2026-01-01',
    effectiveTo: null,
    rules: [
      {
        name: 'Approved rates',
        descriptionAvailable: true,
        description: 'Each line is billed at most the approved rate.',
        parameters: [{ name: 'Tolerance', value: '5', unit: 'percent' }],
        clauseQuote: 'The billed rate may exceed the approved rate by 5 percent.',
        sectionReference: 'Section 4.2',
        inForceFrom: '2026-01-01',
      },
    ],
    ...over,
  };
}

function answer(...sets: BillingGuidelineSet[]): BillingGuidelinesResponse {
  return { sets };
}

function renderWidget(ui: React.ReactElement = <BillingGuidelinesWidgetBody title="Billing Guidelines" />) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

beforeEach(() => {
  h.bffApiCall.mockReset();
  vi.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  vi.restoreAllMocks();
  Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 1024 });
});

describe('billing guidelines client', () => {
  it('reads the general view from the route, and the record view with the record id', async () => {
    h.bffApiCall.mockResolvedValue(answer());

    await getBillingGuidelines();
    await getBillingGuidelines(RECORD);

    expect(h.bffApiCall).toHaveBeenNthCalledWith(1, PATH);
    expect(h.bffApiCall).toHaveBeenNthCalledWith(2, `${PATH}?coreRecordId=${RECORD}`);
  });

  it('refuses a record id that is not a guid before any request, and an answer that is not the contract', async () => {
    await expect(getBillingGuidelines('not-a-guid')).rejects.toThrow('Invalid record id.');
    expect(h.bffApiCall).not.toHaveBeenCalled();

    h.bffApiCall.mockResolvedValue({ unexpected: true });
    await expect(getBillingGuidelines()).rejects.toThrow();
  });
});

describe('Billing Guidelines widget', () => {
  it('groups rules under their set with scope, version and dates, and shows description, parameters and clause', async () => {
    h.bffApiCall.mockResolvedValue(
      answer(
        set(),
        set({
          name: 'Alpha rate card',
          scope: 'Applies to your firm',
          version: 1,
          effectiveFrom: '2026-02-01',
          effectiveTo: '2026-06-30',
          rules: [
            {
              name: 'Daily cap',
              descriptionAvailable: true,
              description: 'No more than 8 hours a day.',
              parameters: [{ name: 'Maximum hours per day', value: '8', unit: 'hours' }],
              clauseQuote: null,
              sectionReference: null,
              inForceFrom: '2026-02-01',
            },
          ],
        })
      )
    );

    renderWidget();

    await screen.findAllByTestId('billing-guideline-set', undefined, WAIT);
    const sets = screen.getAllByTestId('billing-guideline-set');
    expect(sets).toHaveLength(2);
    expect(sets[0].textContent).toContain('Outside counsel guidelines');
    expect(sets[0].textContent).toContain('Applies to all records');
    expect(sets[0].textContent).toContain('Version 3 · From Jan 1, 2026');
    expect(sets[0].textContent).toContain('Approved rates');
    expect(sets[0].textContent).toContain('Each line is billed at most the approved rate.');
    expect(sets[0].textContent).toContain('Tolerance');
    expect(sets[0].textContent).toContain('5 percent');
    expect(sets[0].textContent).toContain('The billed rate may exceed the approved rate by 5 percent.');
    expect(sets[0].textContent).toContain('Section 4.2');
    expect(sets[1].textContent).toContain('Alpha rate card');
    expect(sets[1].textContent).toContain('Applies to your firm');
    expect(sets[1].textContent).toContain('Version 1 · Feb 1, 2026 to Jun 30, 2026');
    expect(sets[1].textContent).toContain('8 hours');
    // A rule with no clause shows no clause block.
    expect(sets[1].querySelector('[data-testid="billing-guideline-clause"]')).toBeNull();
    // One firm: no firm heading.
    expect(screen.getAllByTestId('billing-guideline-firm')).toHaveLength(1);
    expect(screen.queryByRole('heading', { name: 'Alpha LLP' })).toBeNull();
  });

  it('labels each firm when the caller has several', async () => {
    h.bffApiCall.mockResolvedValue(answer(set(), set({ firmName: 'Beta Partners', name: 'Beta card' })));

    renderWidget();

    await screen.findAllByTestId('billing-guideline-firm', undefined, WAIT);
    expect(screen.getAllByTestId('billing-guideline-firm')).toHaveLength(2);
    expect(screen.getByText('Alpha LLP')).toBeTruthy();
    expect(screen.getByText('Beta Partners')).toBeTruthy();
  });

  it('shows the marker for a rule that has no description and writes no description of its own', async () => {
    h.bffApiCall.mockResolvedValue(
      answer(
        set({
          rules: [
            {
              name: 'Refused rule',
              descriptionAvailable: false,
              description: null,
              parameters: [],
              clauseQuote: 'Contract wording stays visible.',
              sectionReference: null,
              inForceFrom: '2026-01-01',
            },
          ],
        })
      )
    );

    renderWidget();

    await screen.findByTestId('billing-guideline-no-description', undefined, WAIT);
    const rule = screen.getByTestId('billing-guideline-rule');
    expect(rule.textContent).toContain('Refused rule');
    expect(rule.textContent).toContain("A plain-language summary isn't available for this rule.");
    expect(rule.textContent).toContain('Contract wording stays visible.');
    expect(screen.queryByTestId('billing-guideline-description')).toBeNull();
    expect(screen.queryByTestId('billing-guideline-parameter')).toBeNull();
  });

  it('shows an empty state when no guidelines have been shared', async () => {
    h.bffApiCall.mockResolvedValue(answer());

    renderWidget();

    expect(await screen.findByText('No billing guidelines', undefined, WAIT)).toBeTruthy();
    expect(screen.getByText('No billing guidelines have been shared with you yet.')).toBeTruthy();
    expect(screen.queryByTestId('billing-guideline-set')).toBeNull();
  });

  it('is hidden when the eBilling flag is off (503 ebilling.disabled): nothing is rendered, no message and no Retry', async () => {
    h.bffApiCall.mockRejectedValue(new ApiError(503, problem(503, { errorCode: 'ebilling.disabled' })));

    const { container } = renderWidget();

    await waitFor(() => expect(h.bffApiCall).toHaveBeenCalled(), WAIT);
    await waitFor(() => expect(screen.queryByText('Loading billing guidelines…')).toBeNull(), WAIT);
    expect(container.querySelector('[data-testid="billing-guidelines"]')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(screen.queryByTestId('billing-guidelines-error')).toBeNull();
  });

  it('is not hidden for an outage 503: it says so and offers Retry, which reads again', async () => {
    h.bffApiCall.mockRejectedValueOnce(
      new ApiError(503, problem(503, { errorCode: 'ebilling.billing-guidelines.unavailable' }))
    );
    h.bffApiCall.mockResolvedValueOnce(answer(set()));

    renderWidget();

    const error = await screen.findByTestId('billing-guidelines-error', undefined, WAIT);
    expect(error.textContent).toContain("Billing guidelines couldn't be loaded just now. Please try again.");
    expect(error.textContent).not.toContain('server detail text');
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await screen.findByTestId('billing-guideline-set', undefined, WAIT);
    expect(h.bffApiCall).toHaveBeenCalledTimes(2);
  });

  it('says a 403 and a 404 by status, with no Retry and no server text', async () => {
    h.bffApiCall.mockRejectedValue(
      new ApiError(403, problem(403, { errorCode: 'ebilling.billing-guidelines.forbidden' }))
    );
    const first = renderWidget();
    expect((await screen.findByTestId('billing-guidelines-error', undefined, WAIT)).textContent).toContain(
      "You don't have access to billing guidelines."
    );
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    first.unmount();

    h.bffApiCall.mockRejectedValue(new ApiError(404, problem(404)));
    renderWidget(<BillingGuidelinesView coreRecordId={RECORD} />);
    const error = await screen.findByTestId('billing-guidelines-error', undefined, WAIT);
    expect(error.textContent).toContain("These billing guidelines aren't available.");
    expect(error.textContent).not.toContain('server detail text');
    expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    expect(h.bffApiCall).toHaveBeenLastCalledWith(`${PATH}?coreRecordId=${RECORD}`);
  });

  it('renders at a 360 px viewport with all content and no fixed width wider than the viewport', async () => {
    Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 360 });
    window.dispatchEvent(new Event('resize'));
    h.bffApiCall.mockResolvedValue(answer(set({ name: 'A very long rule set name '.repeat(8) })));

    const { container } = renderWidget();

    await screen.findByTestId('billing-guideline-set', undefined, WAIT);
    expect(container.textContent).toContain('The billed rate may exceed the approved rate by 5 percent.');
    // jsdom does no layout, so the guard is on what the styles can do: no element carries an inline width, and no rule the widget's
    // styles add fixes a width (or min-width) in px above the viewport; text wraps instead of overflowing.
    for (const el of Array.from(container.querySelectorAll<HTMLElement>('*'))) {
      expect(el.style.width).toBe('');
      expect(el.style.minWidth).toBe('');
    }
    const fixed: string[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try {
        rules = sheet.cssRules;
      } catch {
        continue;
      }
      for (const rule of Array.from(rules)) {
        const text = rule.cssText;
        for (const m of text.matchAll(/(?<![-\w])(?:min-)?width:\s*(\d+(?:\.\d+)?)px/g)) {
          if (
            Number(m[1]) > 360 &&
            container.querySelector(rule instanceof CSSStyleRule ? rule.selectorText : '*:not(*)')
          ) {
            fixed.push(text);
          }
        }
      }
    }
    expect(fixed).toEqual([]);
    expect(container.querySelector('dl')).not.toBeNull();
  });
});

describe('registry descriptor', () => {
  it('registers one billing-guidelines widget: ciam only, no Tier-1 gate, not a default tab', () => {
    const matches = WIDGET_DEFINITIONS.filter(d => d.id === 'billing-guidelines');
    expect(matches).toHaveLength(1);
    const def = getWidgetDefinition('billing-guidelines')!;
    expect(def.planes).toEqual(['ciam']);
    expect(def.requiredEntitlement).toBeUndefined();
    expect(def.defaultForRoles).toEqual([]);
  });

  it('resolves its loader to the widget body', async () => {
    const def = getWidgetDefinition('billing-guidelines')!;
    expect((await def.lazyLoader()).default).toBe(BillingGuidelinesWidgetBody);
  });
});

describe('guidelineSetMeta', () => {
  it('leaves out parts that are not there', () => {
    expect(guidelineSetMeta({ version: null, effectiveFrom: null, effectiveTo: null })).toBe('');
    expect(guidelineSetMeta({ version: 2, effectiveFrom: null, effectiveTo: null })).toBe('Version 2');
    expect(guidelineSetMeta({ version: null, effectiveFrom: null, effectiveTo: '2026-06-30' })).toBe(
      'Until Jun 30, 2026'
    );
  });
});
