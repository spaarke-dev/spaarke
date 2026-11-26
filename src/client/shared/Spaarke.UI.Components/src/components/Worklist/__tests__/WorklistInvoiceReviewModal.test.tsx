/**
 * The Worklist opens Invoice Review in-app on the shared modal shell (task 195, ADR-050):
 *  - with `invoiceReviewClientUrl`, activating a billing item renders InvoiceReviewModal (an SprkModal dialog titled "Invoice Review")
 *    whose iframe src is the URL helper's for that item's invoice and line; `openInvoiceReview` (navigateTo) is NOT called
 *  - without it, the existing `openInvoiceReview` path runs as before (WorklistInvoiceReview.test.tsx stays as it was)
 *  - the header's "N of M" is the de-duplicated billing items of the lane the item was activated from, in display order
 *  - closing the modal reads the lanes again, once
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { WorklistWidget, buildInvoiceReviewUrl } from '..';
import type { WorklistService } from '..';
import {
  A1,
  A2,
  body,
  fakeService,
  group,
  matterCore,
  projectCore,
  routeItem,
} from '../__fixtures__/worklistRouteFixtures';

const TODAY = new Date(2026, 9, 9, 10, 30);
// A frame that never answers is treated as clean after 1000 ms, so a browse step can take that long: wait longer than findBy's default.
const SETTLE = { timeout: 4000 };
const CLIENT = 'https://org.crm.dynamics.com';

const INV_LINE = 'b1000000-0000-0000-0000-000000000001'; // Decide, billing, invoice 1 line 1
const INV_LINE_2 = 'b1000000-0000-0000-0000-000000000002'; // Decide, billing, invoice 1 line 2 (same invoice again)
const INV_ONLY = 'b1000000-0000-0000-0000-000000000003'; // Decide, billing, invoice 2 (no line)
const DO_BILLING = 'b2000000-0000-0000-0000-000000000001'; // Do, billing, invoice 3 (another lane)
const INVOICE_1 = '11111111-1111-1111-1111-111111111111';
const LINE_1 = '22222222-2222-2222-2222-222222222222';
const LINE_2 = '44444444-4444-4444-4444-444444444444';
const INVOICE_2 = '33333333-3333-3333-3333-333333333333';
const INVOICE_3 = '55555555-5555-5555-5555-555555555555';

const lanes = body(
  'lanes',
  [
    group(matterCore, [
      routeItem(INV_LINE, 'Decide', {
        workType: 'approveOrRebudget',
        invoiceReview: { invoiceId: INVOICE_1, lineId: LINE_1 },
      }),
      routeItem(A1, 'Decide', { workType: 'askOutsideCounsel' }),
      routeItem(INV_LINE_2, 'Decide', {
        workType: 'approveOrRebudget',
        invoiceReview: { invoiceId: INVOICE_1, lineId: LINE_2 },
      }),
      routeItem(INV_ONLY, 'Decide', { workType: 'approveOrRebudget', invoiceReview: { invoiceId: INVOICE_2 } }),
      routeItem(A2, 'Decide', { workType: 'chaseReply' }),
    ]),
  ],
  [
    group(projectCore, [
      routeItem(DO_BILLING, 'Do', { workType: 'comingDue', invoiceReview: { invoiceId: INVOICE_3 } }),
    ]),
  ]
);
const onlyOneInvoice = body(
  'lanes',
  [
    group(matterCore, [
      routeItem(INV_LINE, 'Decide', {
        workType: 'approveOrRebudget',
        invoiceReview: { invoiceId: INVOICE_1, lineId: LINE_1 },
      }),
    ]),
  ],
  []
);

function renderWidget(
  props: Partial<React.ComponentProps<typeof WorklistWidget>> & { service?: WorklistService } = {},
  lanesBody: Record<string, unknown> = lanes
) {
  const service = props.service ?? fakeService({ lanes: lanesBody, closedToday: body('closedToday', [], []) });
  const launchWizard = jest.fn(async () => ({ opened: true }));
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <WorklistWidget service={service} today={TODAY} launchWizard={launchWizard} {...props} />
    </FluentProvider>
  );
  return { ...utils, service: service as ReturnType<typeof fakeService>, launchWizard };
}

beforeEach(() => window.localStorage.clear());

const lane = (name: 'Decide' | 'Do') => screen.getByTestId(`worklist-lane-${name}`);
const lanesReads = (service: { read: jest.Mock }) => service.read.mock.calls.filter(c => c[0] === 'lanes').length;
const frame = () => document.querySelector('iframe') as HTMLIFrameElement;
const urlFor = (invoiceId: string, lineId?: string) => buildInvoiceReviewUrl(CLIENT, { invoiceId, lineId });

async function ready() {
  await screen.findByTestId('worklist-widget');
}

function line(signalId: string): HTMLElement {
  const toggle = within(lane('Decide')).getByTestId('lane-toggle-Decide');
  if (toggle.getAttribute('aria-expanded') === 'false') fireEvent.click(toggle);
  const el = within(lane('Decide'))
    .getAllByTestId('issue-line')
    .find(l => l.getAttribute('data-signal-id') === signalId);
  expect(el).toBeDefined();
  return el!;
}

describe('with a client URL (design C)', () => {
  it('activating a billing item opens the in-app modal on that invoice and line, and never navigateTo', async () => {
    const openInvoiceReview = jest.fn(async () => undefined);
    const { launchWizard } = renderWidget({ invoiceReviewClientUrl: CLIENT, openInvoiceReview });
    await ready();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(line(INV_LINE));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('Invoice Review', { selector: 'span' })).toBeInTheDocument();
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_1, LINE_1));
    expect(openInvoiceReview).not.toHaveBeenCalled();
    expect(launchWizard).not.toHaveBeenCalled();
  });

  it('negative: an item that is not a billing item still opens the wizard, with no modal', async () => {
    const { launchWizard } = renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    await userEvent.click(line(A1));
    expect(launchWizard).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it("the header counts the lane's billing items, one per invoice, in display order; the activated item's line is kept", async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    // Invoice 1 appears twice (two lines) and invoice 3 sits in the Do lane: the Decide set is invoice 1, then invoice 2.
    await userEvent.click(line(INV_LINE_2));
    await screen.findByRole('dialog');
    expect(screen.getByText('1 of 2')).toBeInTheDocument();
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_1, LINE_2)); // the line that was activated, not the first
    expect(screen.getByRole('button', { name: 'Previous record' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Next record' }));
    await screen.findByText('2 of 2', undefined, SETTLE);
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_2));
    // `hidden: true`: after the move, jsdom's tabster modalizer (focus-driven, on a timer) marks the surface aria-hidden because nothing in
    // it holds focus (fireEvent does not focus). That is the shell's own focus bookkeeping, not this modal's; the buttons are still there.
    expect(screen.getByRole('button', { name: 'Next record', hidden: true })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Previous record', hidden: true }));
    await screen.findByText('1 of 2', undefined, SETTLE);
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_1, LINE_2));
  });

  it('starts at the activated invoice: activating the second one shows "2 of 2"', async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    await userEvent.click(line(INV_ONLY));
    await screen.findByRole('dialog');
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_2));
  });

  it('follows the filter in force: under Invoice decisions the set is the same billing items; a one-invoice lane shows "1 of 1"', async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT }, onlyOneInvoice);
    await ready();
    await userEvent.click(line(INV_LINE));
    await screen.findByRole('dialog');
    expect(screen.getByText('1 of 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous record' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next record' })).toBeDisabled();
  });

  it('closing the modal (the ×) reads the lanes again, once', async () => {
    const { service } = renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    await waitFor(() => expect(lanesReads(service)).toBe(1));
    await userEvent.click(line(INV_LINE));
    await screen.findByRole('dialog');
    expect(lanesReads(service)).toBe(1); // not while it is open
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(lanesReads(service)).toBe(2));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(lanesReads(service)).toBe(2);
  });
});

describe('without a client URL (the fallback)', () => {
  it('the existing openInvoiceReview path runs: no modal, the opener is called with the invoice and line', async () => {
    const openInvoiceReview = jest.fn(async () => undefined);
    renderWidget({ openInvoiceReview });
    await ready();
    await userEvent.click(line(INV_LINE));
    await waitFor(() => expect(openInvoiceReview).toHaveBeenCalledWith({ invoiceId: INVOICE_1, lineId: LINE_1 }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(document.querySelector('iframe')).toBeNull();
  });

  it('an empty client URL is the same as none', async () => {
    const openInvoiceReview = jest.fn(async () => undefined);
    renderWidget({ invoiceReviewClientUrl: '', openInvoiceReview });
    await ready();
    await userEvent.click(line(INV_LINE));
    await waitFor(() => expect(openInvoiceReview).toHaveBeenCalledTimes(1));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
