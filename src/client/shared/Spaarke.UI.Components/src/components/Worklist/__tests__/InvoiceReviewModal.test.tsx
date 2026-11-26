/**
 * InvoiceReviewModal (task 195): Invoice Review in-app on the shared modal shell, with "N of M" browse and the cross-frame dirty check.
 *  - the iframe `src` is the URL helper's for the current target; previous / next swap it; the ends disable the matching button;
 *    a one-invoice set shows "1 of 1" with both disabled
 *  - the dirty check: the frame answers dirty then Cancel keeps the record and confirm moves; clean, or no answer within 1000 ms,
 *    moves without a prompt; an answer from another window, another origin or with another correlationId counts as no answer
 *  - the URL helper and the browse-set builder
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

import { buildInvoiceBrowseSet, buildInvoiceReviewUrl } from '..';
import type { InvoiceReviewTarget } from '..';
import { InvoiceReviewModal } from '../InvoiceReviewModal';
import { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE } from '../../RecordNavigationModalShell';

const INV_A = '11111111-1111-1111-1111-111111111111';
const INV_B = '22222222-2222-2222-2222-222222222222';
const INV_C = '33333333-3333-3333-3333-333333333333';
const LINE_A = 'a0000000-0000-0000-0000-00000000000a';
const LINE_B = 'b0000000-0000-0000-0000-00000000000b';

// A frame that never answers is treated as clean after 1000 ms, so a browse step can take that long: wait longer than findBy's default.
const SETTLE = { timeout: 4000 };
const CLIENT = 'http://localhost'; // jsdom's origin, so the frame is same-origin as this window

const TARGETS: InvoiceReviewTarget[] = [
  { invoiceId: INV_A, lineId: LINE_A },
  { invoiceId: INV_B, lineId: LINE_B },
  { invoiceId: INV_C },
];
const urlOf = (t: InvoiceReviewTarget) => buildInvoiceReviewUrl(CLIENT, t);

function renderModal(props: Partial<React.ComponentProps<typeof InvoiceReviewModal>> = {}) {
  const onClose = jest.fn();
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewModal open onClose={onClose} clientUrl={CLIENT} targets={TARGETS} startIndex={0} {...props} />
    </FluentProvider>
  );
  return { ...utils, onClose };
}

const frame = () => document.querySelector('iframe') as HTMLIFrameElement;
const src = () => frame().getAttribute('src');
const next = () => screen.getByRole('button', { name: 'Next record' });
const prev = () => screen.getByRole('button', { name: 'Previous record' });

/** Spy on what the modal posts into the frame; returns the spy. */
function watchFrame() {
  const win = frame().contentWindow as Window;
  return jest.spyOn(win, 'postMessage').mockImplementation(() => undefined);
}

function lastRequest(spy: jest.SpyInstance): { type: string; correlationId: string } {
  const calls = spy.mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1][0];
}

/** The frame (or someone else) answers the dirty check. */
function answer(
  request: { correlationId: string },
  dirty: boolean,
  from: { source?: Window | null; origin?: string; correlationId?: string } = {}
) {
  window.dispatchEvent(
    new MessageEvent('message', {
      data: { type: DIRTY_CHECK_RESULT_TYPE, correlationId: from.correlationId ?? request.correlationId, dirty },
      origin: from.origin ?? window.location.origin,
      source: from.source === undefined ? frame().contentWindow : from.source,
    })
  );
}

afterEach(() => jest.useRealTimers());

describe('the URL helper (design B)', () => {
  it('builds the page URL with the data envelope encoded once, GUIDs bare and lower case', () => {
    expect(
      buildInvoiceReviewUrl('https://org.crm.dynamics.com', {
        invoiceId: `{${INV_A.toUpperCase()}}`,
        lineId: `{${LINE_A.toUpperCase()}}`,
      })
    ).toBe(
      `https://org.crm.dynamics.com/WebResources/sprk_invoicereviewpage?data=${encodeURIComponent(`invoiceId=${INV_A}&lineId=${LINE_A}`)}`
    );
    const url = buildInvoiceReviewUrl('https://org.crm.dynamics.com', { invoiceId: INV_A, lineId: LINE_A });
    expect(url).toContain('?data=invoiceId%3D');
    expect(url).toContain('%26lineId%3D'); // the & is encoded: the page's own parser reads one `data` parameter
    expect(url).not.toContain('%25'); // encoded once, never twice
  });

  it('leaves lineId out when there is none (absent, null or blank)', () => {
    for (const lineId of [undefined, null, '']) {
      const url = buildInvoiceReviewUrl(CLIENT, { invoiceId: INV_A, lineId });
      expect(decodeURIComponent(url.split('?data=')[1])).toBe(`invoiceId=${INV_A}`);
    }
  });

  it('a trailing slash on the client URL does not make a double slash', () => {
    expect(buildInvoiceReviewUrl('https://org.crm.dynamics.com/', { invoiceId: INV_A })).toBe(
      buildInvoiceReviewUrl('https://org.crm.dynamics.com', { invoiceId: INV_A })
    );
    expect(buildInvoiceReviewUrl('https://org.crm.dynamics.com//', { invoiceId: INV_A })).not.toContain('com//');
  });
});

describe('the browse set (design D)', () => {
  it("keeps one entry per invoice in display order, with the first item's line", () => {
    const set = buildInvoiceBrowseSet(
      [
        { invoiceId: INV_A, lineId: LINE_A },
        null, // not a billing item
        { invoiceId: INV_B },
        { invoiceId: INV_A.toUpperCase(), lineId: LINE_B }, // the same invoice again, another line, other casing
      ],
      { invoiceId: INV_B }
    );
    expect(set.targets).toEqual([
      { invoiceId: INV_A, lineId: LINE_A },
      { invoiceId: INV_B, lineId: null },
    ]);
    expect(set.startIndex).toBe(1);
  });

  it("the activated item's own line replaces the first line of its invoice", () => {
    const set = buildInvoiceBrowseSet(
      [
        { invoiceId: INV_A, lineId: LINE_A },
        { invoiceId: INV_A, lineId: LINE_B },
      ],
      { invoiceId: INV_A, lineId: LINE_B }
    );
    expect(set.targets).toEqual([{ invoiceId: INV_A, lineId: LINE_B }]);
    expect(set.startIndex).toBe(0);
  });

  it('negative: an activated invoice that is not in the list still opens, first', () => {
    const set = buildInvoiceBrowseSet([{ invoiceId: INV_B }], { invoiceId: INV_A });
    expect(set.targets.map(t => t.invoiceId)).toEqual([INV_A, INV_B]);
    expect(set.startIndex).toBe(0);
  });
});

describe('the modal shell and browse (design A, D)', () => {
  it("is an SprkModal dialog titled Invoice Review whose iframe is the helper's URL for the start target", () => {
    renderModal({ startIndex: 1 });
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Invoice Review', { selector: 'span' })).toBeInTheDocument();
    expect(frame()).toHaveAttribute('title', 'Invoice Review');
    expect(src()).toBe(urlOf(TARGETS[1]));
    expect(screen.getByText('2 of 3')).toBeInTheDocument();
  });

  it("next and previous swap the iframe src to the neighbouring invoice, with that entry's line", async () => {
    renderModal();
    expect(screen.getByText('1 of 3')).toBeInTheDocument();
    expect(prev()).toBeDisabled();
    fireEvent.click(next());
    await screen.findByText('2 of 3', undefined, SETTLE);
    expect(src()).toBe(urlOf(TARGETS[1]));
    fireEvent.click(next());
    await screen.findByText('3 of 3', undefined, SETTLE);
    expect(src()).toBe(urlOf(TARGETS[2]));
    expect(decodeURIComponent(src()!.split('?data=')[1])).toBe(`invoiceId=${INV_C}`); // no line for the last
    expect(next()).toBeDisabled(); // the end
    fireEvent.click(prev());
    await screen.findByText('2 of 3', undefined, SETTLE);
    expect(src()).toBe(urlOf(TARGETS[1]));
  });

  it('a one-invoice set shows "1 of 1" with both buttons disabled', () => {
    renderModal({ targets: [TARGETS[0]] });
    expect(screen.getByText('1 of 1')).toBeInTheDocument();
    expect(prev()).toBeDisabled();
    expect(next()).toBeDisabled();
  });

  it('the × calls onClose once', () => {
    const { onClose } = renderModal();
    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});

describe('the page theme (fix round 1, F2)', () => {
  it('the helper adds theme=dark|light beside data, not inside it; with no theme the URL is as before', () => {
    const plain = buildInvoiceReviewUrl(CLIENT, { invoiceId: INV_A, lineId: LINE_A });
    for (const theme of ['dark', 'light'] as const) {
      const url = buildInvoiceReviewUrl(CLIENT, { invoiceId: INV_A, lineId: LINE_A }, theme);
      expect(url).toBe(`${plain}&theme=${theme}`);
      const params = new URL(url).searchParams;
      expect(params.get('theme')).toBe(theme);
      expect(params.get('data')).toBe(`invoiceId=${INV_A}&lineId=${LINE_A}`); // the envelope is untouched
    }
    expect(plain).not.toContain('theme=');
  });

  it('the modal puts the host theme on every record it shows', async () => {
    renderModal({ theme: 'dark' });
    expect(new URL(src()!).searchParams.get('theme')).toBe('dark');
    fireEvent.click(next());
    await screen.findByText('2 of 3', undefined, SETTLE);
    expect(src()).toBe(buildInvoiceReviewUrl(CLIENT, TARGETS[1], 'dark'));
  });

  it('negative: with no theme prop the frame URL has no theme parameter', () => {
    renderModal();
    expect(new URL(src()!).searchParams.has('theme')).toBe(false);
  });
});

describe('each record gets a fresh frame (fix round 1, F3)', () => {
  it('a move mounts a NEW iframe element (changing one frame src would add a history entry per move)', async () => {
    renderModal();
    const first = frame();
    fireEvent.click(next());
    await screen.findByText('2 of 3', undefined, SETTLE);
    const second = frame();
    expect(second).not.toBe(first);
    expect(first.isConnected).toBe(false);
    expect(document.querySelectorAll('iframe')).toHaveLength(1);
    fireEvent.click(prev());
    await screen.findByText('1 of 3', undefined, SETTLE);
    expect(frame()).not.toBe(second);
  });

  it('the dirty check after a move asks the CURRENT frame', async () => {
    renderModal();
    fireEvent.click(next());
    await screen.findByText('2 of 3', undefined, SETTLE);
    const post = watchFrame(); // the new frame's window
    fireEvent.click(next());
    const request = lastRequest(post);
    expect(request.type).toBe(DIRTY_CHECK_REQUEST_TYPE);
    act(() => answer(request, false)); // `answer` takes the current frame's window as the source
    await screen.findByText('3 of 3', undefined, SETTLE);
  });

  it('negative: Cancel on the discard prompt keeps the same frame element', async () => {
    renderModal();
    const first = frame();
    const post = watchFrame();
    fireEvent.click(next());
    act(() => answer(lastRequest(post), true));
    await screen.findByText("Discard what you haven't saved?");
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await act(async () => undefined);
    expect(frame()).toBe(first);
  });
});

describe('keyboard focus after a move (fix round 1, F4)', () => {
  it('reaching the last record: focus goes to Previous, the enabled nav button, not the body', async () => {
    renderModal({ startIndex: 1 });
    next().focus();
    expect(document.activeElement).toBe(next());
    fireEvent.click(next());
    await screen.findByText('3 of 3', undefined, SETTLE);
    expect(next()).toBeDisabled();
    expect(document.activeElement).toBe(prev());
  });

  it('reaching the first record: focus goes to Next', async () => {
    renderModal({ startIndex: 1 });
    prev().focus();
    fireEvent.click(prev());
    await screen.findByText('1 of 3', undefined, SETTLE);
    expect(prev()).toBeDisabled();
    expect(document.activeElement).toBe(next());
  });

  it('focus lost to the body mid-set goes back to the nav button in the direction of travel', async () => {
    renderModal({ startIndex: 0 });
    next().focus();
    fireEvent.click(next());
    (document.activeElement as HTMLElement | null)?.blur(); // what a real browser does when the old frame goes
    await screen.findByText('2 of 3', undefined, SETTLE);
    expect(document.activeElement).toBe(next());
  });

  it('negative: focus the person put on the close button is left alone', async () => {
    renderModal({ startIndex: 1 });
    const close = screen.getByRole('button', { name: 'Close' });
    close.focus();
    fireEvent.click(next());
    await screen.findByText('3 of 3', undefined, SETTLE);
    expect(document.activeElement).toBe(close);
  });
});

describe('the dirty check before browse (design E)', () => {
  it('asks the frame with a correlationId, and a clean answer moves without a prompt', async () => {
    renderModal();
    const post = watchFrame();
    fireEvent.click(next());
    const request = lastRequest(post);
    expect(request.type).toBe(DIRTY_CHECK_REQUEST_TYPE);
    expect(typeof request.correlationId).toBe('string');
    expect(request.correlationId.length).toBeGreaterThan(0);
    expect(post.mock.calls[0][1]).toBe(window.location.origin); // pinned to the frame's origin
    act(() => answer(request, false));
    await screen.findByText('2 of 3', undefined, SETTLE);
    expect(src()).toBe(urlOf(TARGETS[1]));
    expect(screen.queryByText("Discard what you haven't saved?")).not.toBeInTheDocument();
  });

  it('a dirty answer asks first; Cancel keeps the record (src unchanged)', async () => {
    renderModal();
    const post = watchFrame();
    fireEvent.click(next());
    act(() => answer(lastRequest(post), true));
    expect(await screen.findByText("Discard what you haven't saved?")).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await act(async () => undefined);
    expect(screen.queryByText("Discard what you haven't saved?")).not.toBeInTheDocument();
    expect(src()).toBe(urlOf(TARGETS[0]));
    expect(screen.getByText('1 of 3')).toBeInTheDocument();
  });

  it('a dirty answer then confirm moves to the next record', async () => {
    renderModal();
    const post = watchFrame();
    fireEvent.click(next());
    act(() => answer(lastRequest(post), true));
    await screen.findByText("Discard what you haven't saved?");
    fireEvent.click(screen.getByRole('button', { name: 'Discard and continue' }));
    await screen.findByText('2 of 3', undefined, SETTLE);
    expect(src()).toBe(urlOf(TARGETS[1]));
  });

  it('no answer within 1000 ms is clean: the record moves with no prompt (fake timers)', async () => {
    jest.useFakeTimers();
    renderModal();
    watchFrame();
    fireEvent.click(next());
    await act(async () => {
      jest.advanceTimersByTime(999);
    });
    expect(src()).toBe(urlOf(TARGETS[0])); // still waiting for the frame
    await act(async () => {
      jest.advanceTimersByTime(1);
    });
    expect(src()).toBe(urlOf(TARGETS[1]));
    expect(screen.queryByText("Discard what you haven't saved?")).not.toBeInTheDocument();
  });

  it.each([
    ['another window', { source: window as Window }],
    ['another origin', { origin: 'https://evil.example.com' }],
    ['the wrong correlationId', { correlationId: 'not-the-request' }],
  ])('negative: a dirty answer from %s is ignored, as if the frame never answered', async (_name, from) => {
    jest.useFakeTimers();
    renderModal();
    const post = watchFrame();
    fireEvent.click(next());
    const request = lastRequest(post);
    act(() => answer(request, true, from));
    await act(async () => {
      jest.advanceTimersByTime(1000);
    });
    expect(screen.queryByText("Discard what you haven't saved?")).not.toBeInTheDocument();
    expect(src()).toBe(urlOf(TARGETS[1])); // the timeout (clean) moved it
  });

  it('negative: an answer that is not a dirty-check result is ignored', async () => {
    jest.useFakeTimers();
    renderModal();
    const post = watchFrame();
    fireEvent.click(next());
    const request = lastRequest(post);
    act(() => {
      window.dispatchEvent(
        new MessageEvent('message', {
          data: { type: 'something-else', correlationId: request.correlationId, dirty: true },
          origin: window.location.origin,
          source: frame().contentWindow,
        })
      );
    });
    await act(async () => {
      jest.advanceTimersByTime(1000);
    });
    expect(screen.queryByText("Discard what you haven't saved?")).not.toBeInTheDocument();
    expect(src()).toBe(urlOf(TARGETS[1]));
  });
});
