/**
 * Task 195: the page's side of the cross-frame dirty-check protocol. The Console opens Invoice Review in an iframe inside the shared modal
 * shell, with "N of M" browse; before it moves to another invoice it posts `request-dirty-check` to this page and asks "Discard what you
 * haven't saved?" when the page answers dirty.
 *
 *  - fresh load: dirty = false
 *  - dirty = true while a note box holds text, a reduce amount was changed, a correction value was changed, the decision wizard is open
 *    or a write is in flight; back to false when that is gone
 *  - the reply carries the request's correlationId and goes to the requesting window only
 *  - a request from another origin, or one that is not a dirty-check request, gets no answer
 *  - the five count cards are laid out on one row by a page-local class (the shared MetricCardRow defaults are untouched)
 */

import '@testing-library/jest-dom';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { InvoiceReviewWithDecisions } from '../components/InvoiceReviewWithDecisions';
import { InvoiceReviewPage } from '../components/InvoiceReviewPage';
import { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE } from '@spaarke/ui-components';
import { INVOICE_ID, flagged, line, lineId, mockApi, mockDecisionApi, review, type MockApi } from './testKit';

const normal = () => review([line(1), flagged(2), flagged(4)]);
const row = (n: number) => screen.getByTestId(`line-row-${lineId(n)}`);

/** A stand-in for the Console modal's window: what the page replies to. */
function fakeParent() {
  return { postMessage: jest.fn() };
}

/** Post a request to the page as the Console modal would (jsdom's own postMessage carries no origin or source, so dispatch the event). */
function ask(opts: { origin?: string; type?: string; correlationId?: string | null; source?: unknown } = {}) {
  const parent = fakeParent();
  const correlationId = opts.correlationId === undefined ? 'corr-1' : opts.correlationId;
  act(() => {
    window.dispatchEvent(
      new MessageEvent('message', {
        data: { type: opts.type ?? DIRTY_CHECK_REQUEST_TYPE, ...(correlationId === null ? {} : { correlationId }) },
        origin: opts.origin ?? window.location.origin,
        source: ('source' in opts ? opts.source : parent) as MessageEventSource,
      })
    );
  });
  return parent;
}

/** What the page answered (the reply's `dirty`), failing when it did not answer exactly once. */
function reply(parent: ReturnType<typeof fakeParent>): { type: string; correlationId: string; dirty: boolean } {
  expect(parent.postMessage).toHaveBeenCalledTimes(1);
  return parent.postMessage.mock.calls[0][0];
}
const isDirty = () => reply(ask()).dirty;

function renderPage(api: MockApi) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewPage invoiceId={INVOICE_ID} api={api as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
}

function renderWithDecisions(api: MockApi) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <InvoiceReviewWithDecisions invoiceId={INVOICE_ID} api={api as never} decisionApi={mockDecisionApi() as never} pollIntervalMs={60_000} />
    </FluentProvider>
  );
}

async function openLine2(api: MockApi) {
  renderPage(api);
  await screen.findByTestId('review-title');
  fireEvent.click(row(2));
  await screen.findByTestId(`pane-line-${lineId(2)}`);
}

describe('the dirty-check responder (design E, page side)', () => {
  it('a fresh load is clean; the reply carries the request\'s correlationId and goes to the requesting window, pinned to its origin', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    const parent = ask({ correlationId: 'corr-fresh' });
    expect(parent.postMessage).toHaveBeenCalledTimes(1);
    expect(parent.postMessage).toHaveBeenCalledWith(
      { type: DIRTY_CHECK_RESULT_TYPE, correlationId: 'corr-fresh', dirty: false },
      window.location.origin
    );
  });

  it('is dirty while a note box holds unsaved text, and clean again when it is emptied', async () => {
    await openLine2(mockApi(normal()));
    expect(isDirty()).toBe(false);
    const note = screen.getByLabelText('Note for line 2') as HTMLTextAreaElement;
    fireEvent.change(note, { target: { value: 'Rate card 2025' } });
    expect(isDirty()).toBe(true);
    fireEvent.change(note, { target: { value: '' } });
    expect(isDirty()).toBe(false);
  });

  it('a note of only spaces is not unsaved work', async () => {
    await openLine2(mockApi(normal()));
    fireEvent.change(screen.getByLabelText('Note for line 2'), { target: { value: '   ' } });
    expect(isDirty()).toBe(false);
  });

  it('is dirty while a reduce entry has an unsaved change (the approved amount), and a reason counts too', async () => {
    await openLine2(mockApi(normal()));
    expect(isDirty()).toBe(false);
    const amount = screen.getByLabelText('Approved amount for line 2') as HTMLInputElement;
    const opened = amount.value;
    expect(opened).not.toBe('');
    fireEvent.change(amount, { target: { value: '900.00' } });
    expect(isDirty()).toBe(true);
    fireEvent.change(amount, { target: { value: opened } });
    // Back to the amount the form opened with: not a change.
    expect(isDirty()).toBe(false);
    fireEvent.change(screen.getByLabelText('Reason for line 2'), { target: { value: 'Over the cap' } });
    expect(isDirty()).toBe(true);
  });

  it('is dirty while the decision wizard is open, and clean once it closes', async () => {
    renderWithDecisions(mockApi(normal()));
    await screen.findByTestId('review-title');
    expect(isDirty()).toBe(false);
    fireEvent.click(await screen.findByTestId('decide-invoice'));
    const dialog = await screen.findByRole('dialog', { name: 'Decide invoice', hidden: true });
    expect(dialog).toBeInTheDocument();
    expect(isDirty()).toBe(true);
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close', hidden: true }));
    await waitFor(() => expect(isDirty()).toBe(false));
  });

  it('is dirty while a write is in flight, and clean when it settles', async () => {
    const api = mockApi(normal());
    let finish!: () => void;
    const original = api.decideLine.getMockImplementation()!;
    api.decideLine.mockImplementation(
      (...args: unknown[]) =>
        new Promise(resolve => {
          finish = () => resolve(original(...args));
        })
    );
    await openLine2(api);
    fireEvent.click(screen.getByTestId('decision-confirm'));
    await waitFor(() => expect(api.decideLine).toHaveBeenCalledTimes(1));
    expect(isDirty()).toBe(true);
    await act(async () => {
      finish();
    });
    await waitFor(() => expect(isDirty()).toBe(false));
  });

  it('negative: a request from another origin gets no answer', async () => {
    await openLine2(mockApi(normal()));
    fireEvent.change(screen.getByLabelText('Note for line 2'), { target: { value: 'typed' } });
    const parent = ask({ origin: 'https://evil.example.com' });
    expect(parent.postMessage).not.toHaveBeenCalled();
    expect(ask({ origin: '' }).postMessage).not.toHaveBeenCalled();
  });

  it('negative: a message that is not a dirty-check request, or has no correlationId, gets no answer', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    expect(ask({ type: 'something-else' }).postMessage).not.toHaveBeenCalled();
    expect(ask({ correlationId: null }).postMessage).not.toHaveBeenCalled();
    expect(ask({ correlationId: '' }).postMessage).not.toHaveBeenCalled();
    expect(ask({ source: null }).postMessage).not.toHaveBeenCalled();
  });

  it('stops answering when the page unmounts', async () => {
    const view = renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    view.unmount();
    expect(ask().postMessage).not.toHaveBeenCalled();
  });
});

/** The CSS rules a class name is part of, as text (the page's styles are Griffel classes injected into the document). */
function rulesFor(classNames: string[]): string {
  const out: string[] = [];
  for (const sheet of Array.from(document.styleSheets)) {
    for (const rule of Array.from(sheet.cssRules)) {
      if (classNames.some(c => rule.cssText.includes(`.${c}`))) out.push(rule.cssText);
    }
  }
  return out.join('\n');
}

describe('the count cards on one row (design G)', () => {
  it('the row carries a page-local class that lays the five cards out in five equal columns, and tightens each card', async () => {
    renderPage(mockApi(normal()));
    await screen.findByTestId('review-title');
    const group = screen.getByRole('group', { name: 'Line filters' });
    const cards = Array.from(group.children);
    expect(cards.map(c => c.getAttribute('aria-label'))).toEqual([
      expect.stringMatching(/^All lines/),
      expect.stringMatching(/^Processing/),
      expect.stringMatching(/^To decide/),
      expect.stringMatching(/^Clean/),
      expect.stringMatching(/^Decided/),
    ]);
    const css = rulesFor(Array.from(group.classList));
    expect(css).toMatch(/grid-template-columns:\s*repeat\(5,\s*minmax\(0(px)?,\s*1fr\)\)/);
    // The cards are shortened through the row's class (descendant rules), not through the shared card.
    expect(css).toMatch(/\[role="button"\]/);
    expect(css).toMatch(/flex-direction:\s*row/);
  });
});
