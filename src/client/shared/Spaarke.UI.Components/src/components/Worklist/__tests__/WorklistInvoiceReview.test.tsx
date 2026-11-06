/**
 * Billing Work Items in the Worklist (task 175, D-207): decided only in Invoice Review.
 *  - the Decide lane has an "Invoice decisions" card (items with `invoiceReview`), after All; the other cards exclude them
 *  - activating a billing item opens `sprk_invoicereviewpage` as a dialog through `Xrm.Navigation.navigateTo` (target 2) with the
 *    invoice id and the line id (an invoice-level item omits it), never the Decision wizard
 *  - the lanes are read again once, whether the dialog closes or fails; no Xrm means a notice and no wizard
 *  - an item without `invoiceReview` still opens the wizard, whose browse set leaves billing items out
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { WorklistWidget, hasInvoiceReview, openInvoiceReview, parseWorklistResponse } from '..';
import type { WorklistService } from '..';
import type { DecisionWizardLaunchRequest } from '../../DecisionWizard';
import { A1, A2, body, fakeService, group, matterCore, routeItem } from '../__fixtures__/worklistRouteFixtures';

const TODAY = new Date(2026, 9, 9, 10, 30);

const INV_LINE = 'b1000000-0000-0000-0000-000000000001'; // Decide, invoice line (billing)
const INV_ONLY = 'b1000000-0000-0000-0000-000000000002'; // Decide, invoice (billing)
const INVOICE = '11111111-1111-1111-1111-111111111111';
const LINE = '22222222-2222-2222-2222-222222222222';
const INVOICE_2 = '33333333-3333-3333-3333-333333333333';

const lanesWithBilling = body(
  'lanes',
  [
    group(matterCore, [
      routeItem(INV_LINE, 'Decide', {
        subjectName: 'Line 3 of invoice 4411',
        workType: 'approveOrRebudget',
        invoiceReview: { invoiceId: INVOICE, lineId: LINE },
      }),
      routeItem(A1, 'Decide', { workType: 'askOutsideCounsel' }),
      routeItem(INV_ONLY, 'Decide', {
        subjectName: 'Invoice 4412',
        workType: 'approveOrRebudget',
        invoiceReview: { invoiceId: INVOICE_2 },
      }),
      routeItem(A2, 'Decide', { workType: 'chaseReply' }),
    ]),
  ],
  []
);
const onlyBilling = body(
  'lanes',
  [
    group(matterCore, [
      routeItem(INV_LINE, 'Decide', {
        workType: 'approveOrRebudget',
        invoiceReview: { invoiceId: INVOICE, lineId: LINE },
      }),
    ]),
  ],
  []
);

type Launch = jest.Mock<Promise<{ opened: boolean }>, [DecisionWizardLaunchRequest]>;

function renderWidget(
  props: Partial<React.ComponentProps<typeof WorklistWidget>> & { service?: WorklistService } = {},
  lanes: Record<string, unknown> = lanesWithBilling
) {
  const service = props.service ?? fakeService({ lanes, closedToday: body('closedToday', [], []) });
  const launchWizard: Launch = jest.fn(async () => ({ opened: true }));
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <WorklistWidget service={service} today={TODAY} launchWizard={launchWizard} {...props} />
    </FluentProvider>
  );
  return { ...utils, service: service as ReturnType<typeof fakeService>, launchWizard };
}

beforeEach(() => window.localStorage.clear());
afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
});

const lane = (name: 'Decide' | 'Do') => screen.getByTestId(`worklist-lane-${name}`);
const lanesReads = (service: { read: jest.Mock }) => service.read.mock.calls.filter(c => c[0] === 'lanes').length;

async function ready() {
  await screen.findByTestId('worklist-widget');
}

/** The line for a Signal, inside an expanded Decide lane. */
function line(signalId: string): HTMLElement {
  const toggle = within(lane('Decide')).getByTestId('lane-toggle-Decide');
  if (toggle.getAttribute('aria-expanded') === 'false') fireEvent.click(toggle);
  const el = within(lane('Decide'))
    .getAllByTestId('issue-line')
    .find(l => l.getAttribute('data-signal-id') === signalId);
  expect(el).toBeDefined();
  expect(el).toBeVisible();
  return el!;
}

describe('the route member (task 174 contract)', () => {
  const parsed = (extra: Record<string, unknown>) =>
    parseWorklistResponse(body('lanes', [group(matterCore, [routeItem(A1, 'Decide', extra)])], []), 'lanes').Decide[0]
      .items[0];

  it('parses invoiceReview with and without a line', () => {
    expect(parsed({ invoiceReview: { invoiceId: INVOICE, lineId: LINE } }).invoiceReview).toEqual({
      invoiceId: INVOICE,
      lineId: LINE,
    });
    expect(parsed({ invoiceReview: { invoiceId: INVOICE, lineId: null } }).invoiceReview).toEqual({
      invoiceId: INVOICE,
      lineId: null,
    });
    expect(parsed({ invoiceReview: { invoiceId: INVOICE } }).invoiceReview).toEqual({
      invoiceId: INVOICE,
      lineId: null,
    });
  });

  it('negative: an item without it, or with one that names no invoice, is not a billing item and gains no member', () => {
    for (const extra of [
      {},
      { invoiceReview: null },
      { invoiceReview: {} },
      { invoiceReview: { lineId: LINE } },
      { invoiceReview: 'x' },
    ]) {
      const item = parsed(extra);
      expect(hasInvoiceReview(item)).toBe(false);
      expect('invoiceReview' in item).toBe(false);
    }
  });
});

describe('the Invoice decisions card', () => {
  it('sits right after All, counts the billing items, and the other cards sum with it to All', async () => {
    renderWidget();
    await ready();
    const filters = within(lane('Decide')).getByRole('group', { name: 'Decide filters' });
    const labels = within(filters)
      .getAllByRole('button')
      .map(b => b.getAttribute('aria-label'));
    expect(labels[0]).toMatch(/^All, 4 Decide items/);
    expect(labels[1]).toMatch(/^Invoice decisions, 2 Decide items/);
    expect(labels[2]).toMatch(/^Ask outside counsel, 1 Decide item/);
    expect(labels[3]).toMatch(/^Approve or rebudget, 0 Decide items/);
    expect(labels[4]).toMatch(/^Chase a reply, 1 Decide item/);
  });

  it('selecting it shows exactly the billing items; choosing it again returns to All', async () => {
    renderWidget();
    await ready();
    const card = () => within(lane('Decide')).getByRole('button', { name: /^Invoice decisions/ });
    await userEvent.click(card());
    const ids = () =>
      within(lane('Decide'))
        .getAllByTestId('issue-line')
        .map(l => l.getAttribute('data-signal-id'));
    expect(ids()).toEqual([INV_LINE, INV_ONLY]);
    await userEvent.click(card());
    expect(ids()).toEqual([INV_LINE, A1, INV_ONLY, A2]);
  });

  it('negative: with no billing item there is no Invoice decisions card', async () => {
    renderWidget({}, body('lanes', [group(matterCore, [routeItem(A1, 'Decide', { workType: 'chaseReply' })])], []));
    await ready();
    expect(within(lane('Decide')).queryByRole('button', { name: /^Invoice decisions/ })).not.toBeInTheDocument();
  });
});

describe('opening a billing item (D-207)', () => {
  it('a line item calls navigateTo on sprk_invoicereviewpage with the invoice and line ids, target 2, and never the wizard', async () => {
    const navigateTo = jest.fn(async () => undefined);
    (window as unknown as { Xrm: unknown }).Xrm = { Navigation: { navigateTo } };
    const { launchWizard } = renderWidget({ openInvoiceReview });
    await ready();
    await userEvent.click(line(INV_LINE));
    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    const [pageInput, options] = navigateTo.mock.calls[0] as unknown as [
      Record<string, unknown>,
      Record<string, unknown>,
    ];
    expect(pageInput).toEqual({
      pageType: 'webresource',
      webresourceName: 'sprk_invoicereviewpage',
      data: `invoiceId=${INVOICE}&lineId=${LINE}`,
    });
    expect(options).toMatchObject({
      target: 2,
      position: 1,
      width: { value: 90, unit: '%' },
      height: { value: 90, unit: '%' },
    });
    expect(launchWizard).not.toHaveBeenCalled();
  });

  it('an invoice item omits lineId', async () => {
    const navigateTo = jest.fn(async () => undefined);
    (window as unknown as { Xrm: unknown }).Xrm = { Navigation: { navigateTo } };
    const { launchWizard } = renderWidget({ openInvoiceReview });
    await ready();
    await userEvent.click(line(INV_ONLY));
    await waitFor(() => expect(navigateTo).toHaveBeenCalledTimes(1));
    expect((navigateTo.mock.calls[0] as unknown[])[0]).toMatchObject({ data: `invoiceId=${INVOICE_2}` });
    expect(((navigateTo.mock.calls[0] as unknown[])[0] as { data: string }).data).not.toContain('lineId');
    expect(launchWizard).not.toHaveBeenCalled();
  });

  it('reads the lanes once more after the dialog closes', async () => {
    let close!: () => void;
    const open = jest.fn(() => new Promise<void>(resolve => (close = resolve)));
    const { service } = renderWidget({ openInvoiceReview: open });
    await ready();
    await waitFor(() => expect(lanesReads(service)).toBe(1));
    await userEvent.click(line(INV_LINE));
    expect(open).toHaveBeenCalledWith({ invoiceId: INVOICE, lineId: LINE });
    expect(lanesReads(service)).toBe(1); // not while the dialog is open
    close();
    await waitFor(() => expect(lanesReads(service)).toBe(2));
    expect(lanesReads(service)).toBe(2);
  });

  it('reads the lanes once more when the launch rejects, and says Invoice Review could not be opened', async () => {
    const open = jest.fn(async () => {
      throw new Error('boom');
    });
    const { service, launchWizard } = renderWidget({ openInvoiceReview: open });
    await ready();
    await userEvent.click(line(INV_LINE));
    await waitFor(() => expect(lanesReads(service)).toBe(2));
    expect(screen.getByTestId('worklist-notice')).toHaveTextContent('Invoice Review could not be opened');
    expect(launchWizard).not.toHaveBeenCalled();
  });

  it('without Xrm the real opener rejects: the notice shows, the wizard stays closed', async () => {
    const { launchWizard } = renderWidget({ openInvoiceReview });
    await ready();
    await userEvent.click(line(INV_LINE));
    expect(await screen.findByTestId('worklist-notice')).toHaveTextContent('Invoice Review could not be opened');
    expect(launchWizard).not.toHaveBeenCalled();
  });

  it('a host that gives no opener gets the same notice, no wizard and no re-read', async () => {
    const { service, launchWizard } = renderWidget();
    await ready();
    await waitFor(() => expect(lanesReads(service)).toBe(1));
    await userEvent.click(line(INV_ONLY));
    expect(await screen.findByTestId('worklist-notice')).toHaveTextContent('Invoice Review could not be opened');
    expect(launchWizard).not.toHaveBeenCalled();
    expect(lanesReads(service)).toBe(1);
  });
});

describe('items without invoiceReview behave as before', () => {
  it('a matter item still opens the wizard, with a browse set that leaves the billing items out', async () => {
    const open = jest.fn(async () => undefined);
    const { launchWizard } = renderWidget({ openInvoiceReview: open });
    await ready();
    await userEvent.click(line(A1));
    expect(launchWizard).toHaveBeenCalledTimes(1);
    expect(launchWizard.mock.calls[0][0].itemId).toBe(A1);
    expect(launchWizard.mock.calls[0][0].visibleList).toEqual([A1, A2]);
    expect(open).not.toHaveBeenCalled();
  });

  it('Start review starts on the first item the wizard can decide, not on a billing item', async () => {
    const open = jest.fn(async () => undefined);
    const { launchWizard } = renderWidget({ openInvoiceReview: open });
    await ready();
    await userEvent.click(within(lane('Decide')).getByRole('button', { name: 'Start review' }));
    expect(launchWizard).toHaveBeenCalledTimes(1);
    expect(launchWizard.mock.calls[0][0].itemId).toBe(A1);
    expect(open).not.toHaveBeenCalled();
  });

  it('Start review with only billing items open opens Invoice Review for the first one', async () => {
    const open = jest.fn(async () => undefined);
    const { launchWizard } = renderWidget({ openInvoiceReview: open }, onlyBilling);
    await ready();
    await userEvent.click(within(lane('Decide')).getByRole('button', { name: 'Start review' }));
    expect(open).toHaveBeenCalledWith({ invoiceId: INVOICE, lineId: LINE });
    expect(launchWizard).not.toHaveBeenCalled();
  });
});
