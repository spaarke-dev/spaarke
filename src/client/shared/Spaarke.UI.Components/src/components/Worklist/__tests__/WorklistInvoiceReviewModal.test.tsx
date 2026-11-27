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
const INVOICE_4 = '66666666-6666-6666-6666-666666666666';
const INVOICE_5 = '77777777-7777-7777-7777-777777777777';
const DO_B1 = 'b3000000-0000-0000-0000-000000000001'; // the filter test's Do lane: Coming due, invoice 3
const DO_B2 = 'b3000000-0000-0000-0000-000000000002'; // Finish or reschedule, invoice 4
const DO_B3 = 'b3000000-0000-0000-0000-000000000003'; // Coming due, invoice 5

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
// The widget adds the theme it resolved (F2): with nothing stored and no flag the Console resolves light.
const urlFor = (invoiceId: string, lineId?: string, theme: 'dark' | 'light' = 'light') =>
  buildInvoiceReviewUrl(CLIENT, { invoiceId, lineId }, theme);

async function ready() {
  await screen.findByTestId('worklist-widget');
}

function line(signalId: string, laneName: 'Decide' | 'Do' = 'Decide'): HTMLElement {
  const toggle = within(lane(laneName)).getByTestId(`lane-toggle-${laneName}`);
  if (toggle.getAttribute('aria-expanded') === 'false') fireEvent.click(toggle);
  const el = within(lane(laneName))
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
    // A real click focuses the button, as a user's does; the move then disables it (the end), and the modal hands focus on (F4).
    await userEvent.click(screen.getByRole('button', { name: 'Next record' }));
    await screen.findByText('2 of 2', undefined, SETTLE);
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_2));
    expect(screen.getByRole('button', { name: 'Next record' })).toBeDisabled();
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Previous record' }));
    await userEvent.click(screen.getByRole('button', { name: 'Previous record' }));
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

  it('a one-invoice lane shows "1 of 1" with both buttons disabled', async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT }, onlyOneInvoice);
    await ready();
    await userEvent.click(line(INV_LINE));
    await screen.findByRole('dialog');
    expect(screen.getByText('1 of 1')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous record' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next record' })).toBeDisabled();
  });

  // Do lane, three billing items on three invoices: Coming due, Finish or reschedule, Coming due.
  const doLane = () =>
    body(
      'lanes',
      [],
      [
        group(projectCore, [
          routeItem(DO_B1, 'Do', { workType: 'comingDue', invoiceReview: { invoiceId: INVOICE_3 } }),
          routeItem(DO_B2, 'Do', { workType: 'finishOrReschedule', invoiceReview: { invoiceId: INVOICE_4 } }),
          routeItem(DO_B3, 'Do', { workType: 'comingDue', invoiceReview: { invoiceId: INVOICE_5 } }),
        ]),
      ]
    );

  it('with no count filter the set is the whole lane (fix round 1, F5 control): three invoices', async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT }, doLane());
    await ready();
    await userEvent.click(line(DO_B3, 'Do'));
    await screen.findByRole('dialog', undefined, SETTLE);
    expect(screen.getByText('3 of 3')).toBeInTheDocument();
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_5));
  });

  it('follows the count filter in force (fix round 1, F5): M and the order come from the FILTERED list, not the whole lane', async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT }, doLane());
    await ready();
    // Select the Coming due count filter, then activate an item: the set is the two Coming due invoices, in the order shown.
    fireEvent.click(await screen.findByRole('button', { name: /^Coming due/ }));
    await waitFor(() =>
      expect(screen.getByRole('button', { name: /^Coming due/ })).toHaveAttribute('aria-pressed', 'true')
    );
    await userEvent.click(line(DO_B3, 'Do'));
    await screen.findByRole('dialog', undefined, SETTLE);
    expect(screen.getByText('2 of 2')).toBeInTheDocument();
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_5));
    expect(screen.getByRole('button', { name: 'Next record' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Previous record' }));
    await screen.findByText('1 of 2', undefined, SETTLE);
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_3)); // invoice 4 (Finish or reschedule) is not in the filtered list
  });

  it("the frame URL carries the Console's resolved theme (fix round 1, F2)", async () => {
    window.localStorage.setItem('spaarke-theme', 'dark');
    renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    await userEvent.click(line(INV_LINE));
    await screen.findByRole('dialog');
    expect(frame().getAttribute('src')).toBe(
      buildInvoiceReviewUrl(CLIENT, { invoiceId: INVOICE_1, lineId: LINE_1 }, 'dark')
    );
  });

  it('the theme follows the resolver: a stored light preference gives light; none stored and no flag gives light', async () => {
    window.localStorage.setItem('spaarke-theme', 'light');
    const first = renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    await userEvent.click(line(INV_LINE));
    await screen.findByRole('dialog');
    expect(new URL(frame().getAttribute('src')!).searchParams.get('theme')).toBe('light');
    first.unmount();

    window.localStorage.clear();
    renderWidget({ invoiceReviewClientUrl: CLIENT });
    await ready();
    await userEvent.click(line(INV_LINE));
    await screen.findByRole('dialog');
    expect(new URL(frame().getAttribute('src')!).searchParams.get('theme')).toBe('light');
  });

  it('the theme is dark when the app set the dark URL flag and nothing is stored (the "auto" user with a dark app)', async () => {
    window.history.pushState({}, '', '/?flags=themeOption%3Ddarkmode');
    try {
      renderWidget({ invoiceReviewClientUrl: CLIENT });
      await ready();
      await userEvent.click(line(INV_LINE));
      await screen.findByRole('dialog');
      expect(new URL(frame().getAttribute('src')!).searchParams.get('theme')).toBe('dark');
    } finally {
      window.history.pushState({}, '', '/');
    }
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

  it('#2658: closing leaves no dialog or frame, and the next opening starts fresh on the item activated then', async () => {
    renderWidget({ invoiceReviewClientUrl: CLIENT }, doLane());
    await ready();
    await userEvent.click(line(DO_B1, 'Do'));
    await screen.findByRole('dialog', undefined, SETTLE);
    expect(screen.getByText('1 of 3')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument(), SETTLE);
    expect(document.querySelector('iframe')).toBeNull();
    await userEvent.click(line(DO_B3, 'Do'));
    await screen.findByRole('dialog', undefined, SETTLE);
    expect(screen.getByText('3 of 3')).toBeInTheDocument();
    expect(frame().getAttribute('src')).toBe(urlFor(INVOICE_5));
  }, 15000); // two full opens: under the whole-package run this takes longer than jest's 5 s default
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
