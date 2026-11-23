/**
 * Task 134 slice C: the inline "add a firm to this record" panel (ebilling 095g). The two service calls are the panel's
 * injected seams (doubled here); the real service and the view around the panel are covered by
 * `InvoiceView.addFirm.test.tsx`.
 */
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import {
  AddFirmPanel,
  ADD_FIRM_SEARCH_DEBOUNCE_MS,
  FIRM_ACCESS_PENDING_MESSAGE,
  NO_LAW_FIRMS_MESSAGE,
  type AddFirmPanelProps,
} from '../AddFirmPanel';
import type { AddFirmOutcome, InvoiceFirmOption, LawFirmPage } from '../../services/invoiceSubmissionSeam';

const TIMEOUT = 30000;
const target = { entityType: 'Matter' as const, entityId: '6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41' };
const firm = (n: number): InvoiceFirmOption => ({
  firmId: `8b0c6f1e-4c2a-4f7e-9a51-${String(n).padStart(12, '0')}`,
  name: `Firm ${n}`,
});

// jsdom has no IntersectionObserver. Capture each observer so a test can scroll the sentinel into view.
let observers: { callback: IntersectionObserverCallback; disconnected: boolean }[] = [];
class IntersectionObserverMock {
  private readonly entry: { callback: IntersectionObserverCallback; disconnected: boolean };
  constructor(callback: IntersectionObserverCallback) {
    this.entry = { callback, disconnected: false };
    observers.push(this.entry);
  }
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    this.entry.disconnected = true;
  }
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
(globalThis as any).IntersectionObserver = IntersectionObserverMock;

async function scrollSentinelIntoView(): Promise<void> {
  const live = [...observers].reverse().find(o => !o.disconnected);
  if (!live) throw new Error('No live observer: no sentinel is mounted');
  await act(async () => {
    live.callback([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
  });
}

const added = (over: Partial<Extract<AddFirmOutcome, { kind: 'added' }>> = {}): AddFirmOutcome => ({
  kind: 'added',
  changed: true,
  firms: [firm(1)],
  accessState: 'applied',
  recordActive: true,
  notice: "Firm 1's people will get access to this record.",
  ...over,
});

function setup(over: Partial<AddFirmPanelProps> = {}) {
  const listLawFirms = jest.fn(
    async (args: { search?: string; page?: number }): Promise<LawFirmPage> => ({
      items: [firm(1), firm(2)],
      page: args.page ?? 1,
      hasMore: false,
    })
  );
  const addFirmToRecord = jest.fn(async (): Promise<AddFirmOutcome> => added());
  const props: AddFirmPanelProps = {
    target,
    listLawFirms,
    addFirmToRecord,
    onAdded: jest.fn(),
    onClose: jest.fn(),
    announce: jest.fn(),
    ...over,
  };
  const { unmount } = render(
    <FluentProvider theme={webLightTheme}>
      <AddFirmPanel {...props} />
    </FluentProvider>
  );
  return {
    unmount,
    props,
    listLawFirms: props.listLawFirms as jest.Mock,
    addFirmToRecord: props.addFirmToRecord as jest.Mock,
    onAdded: props.onAdded as jest.Mock,
    onClose: props.onClose as jest.Mock,
    announce: props.announce as jest.Mock,
  };
}

/** The elements that say, in words, that a firm's people have (or are given) access NOW. */
const CLAIMS_ACCESS_NOW = /gives .* access|have access|has access/i;
const visibleAccessClaims = (): HTMLElement[] =>
  screen.queryAllByText(CLAIMS_ACCESS_NOW).filter(el => !el.closest('[aria-live]'));

const confirmButton = () => screen.getByRole('button', { name: 'Add firm' });
async function chooseFirm(name: string) {
  await userEvent.click(await screen.findByRole('option', { name }));
}

beforeEach(() => {
  observers = [];
});

describe('AddFirmPanel — the list', () => {
  it(
    'loads the first page on open and lists the firms, with a labelled search',
    async () => {
      const { listLawFirms } = setup();
      expect(await screen.findByRole('option', { name: 'Firm 1' })).toBeInTheDocument();
      expect(screen.getByRole('option', { name: 'Firm 2' })).toBeInTheDocument();
      expect(listLawFirms).toHaveBeenCalledTimes(1);
      expect(listLawFirms).toHaveBeenCalledWith({ page: 1 });
      expect(screen.getByLabelText('Search law firms')).toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: the request carries the search (debounced) and page 1, and replaces the list',
    async () => {
      const { listLawFirms } = setup();
      await screen.findByRole('option', { name: 'Firm 1' });
      listLawFirms.mockImplementation(async (args: { search?: string; page?: number }) => ({
        items: [{ firmId: '8b0c6f1e-4c2a-4f7e-9a51-0000000000aa', name: 'Ravenscroft & Vance LLP' }],
        page: args.page ?? 1,
        hasMore: false,
      }));
      fireEvent.change(screen.getByLabelText('Search law firms'), { target: { value: '  rave ' } });
      // Not before the debounce has passed.
      expect(listLawFirms).toHaveBeenCalledTimes(1);
      await waitFor(() => expect(listLawFirms).toHaveBeenCalledTimes(2), { timeout: ADD_FIRM_SEARCH_DEBOUNCE_MS * 5 });
      expect(listLawFirms).toHaveBeenLastCalledWith({ search: 'rave', page: 1 });
      expect(await screen.findByRole('option', { name: 'Ravenscroft & Vance LLP' })).toBeInTheDocument();
      expect(screen.queryByRole('option', { name: 'Firm 1' })).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'must-fire: scrolling loads page 2 while hasMore is true, and stops when it is false (no pager, no Load more)',
    async () => {
      const pages: Record<number, LawFirmPage> = {
        1: { items: [firm(1), firm(2)], page: 1, hasMore: true },
        2: { items: [firm(3)], page: 2, hasMore: false },
      };
      const { listLawFirms } = setup({
        listLawFirms: jest.fn(async (args: { page?: number }) => pages[args.page ?? 1]!),
      });
      await screen.findByRole('option', { name: 'Firm 1' });
      expect(screen.getByTestId('add-firm-sentinel')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /load more|next/i })).not.toBeInTheDocument();
      expect(listLawFirms).toHaveBeenCalledTimes(1);

      await scrollSentinelIntoView();
      expect(await screen.findByRole('option', { name: 'Firm 3' })).toBeInTheDocument();
      expect(listLawFirms).toHaveBeenCalledTimes(2);
      expect(listLawFirms).toHaveBeenLastCalledWith({ page: 2 });
      // The rows already shown stay, and hasMore false removes the sentinel: nothing more can be asked.
      expect(screen.getByRole('option', { name: 'Firm 1' })).toBeInTheDocument();
      expect(screen.queryByTestId('add-firm-sentinel')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'a search with no results says "No law firms match."',
    async () => {
      const { announce } = setup({ listLawFirms: jest.fn(async () => ({ items: [], page: 1, hasMore: false })) });
      expect(await screen.findByText(NO_LAW_FIRMS_MESSAGE)).toBeInTheDocument();
      expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
      expect(announce).toHaveBeenCalledWith(NO_LAW_FIRMS_MESSAGE);
    },
    TIMEOUT
  );

  it(
    'a list error shows its message with Retry, and Retry asks again',
    async () => {
      const listLawFirms = jest
        .fn()
        .mockRejectedValueOnce(new Error("Spaarke couldn't do this right now. Try again."))
        .mockResolvedValue({ items: [firm(1)], page: 1, hasMore: false });
      setup({ listLawFirms });
      const error = await screen.findByTestId('add-firm-list-error');
      expect(within(error).getByText("Spaarke couldn't do this right now. Try again.")).toBeInTheDocument();
      await userEvent.click(within(error).getByRole('button', { name: 'Retry' }));
      expect(await screen.findByRole('option', { name: 'Firm 1' })).toBeInTheDocument();
      expect(listLawFirms).toHaveBeenCalledTimes(2);
      expect(screen.queryByTestId('add-firm-list-error')).not.toBeInTheDocument();
    },
    TIMEOUT
  );
});

describe('AddFirmPanel — choosing and adding', () => {
  it(
    'must-fire: choosing a firm names it in the access notice, and says nothing about access they already have',
    async () => {
      setup();
      expect(screen.queryByTestId('add-firm-access-notice')).not.toBeInTheDocument();
      await chooseFirm('Firm 2');
      expect(screen.getByTestId('add-firm-access-notice')).toHaveTextContent(
        "Adding Firm 2 can give that firm's people access to this record. Spaarke will say what it did."
      );
      expect(screen.getByTestId('add-firm-access-notice')).not.toHaveTextContent(/now|already/i);
    },
    TIMEOUT
  );

  it(
    'F1: the notice promises nothing (no visible element says the firm gives, has or will have access), before or after choosing',
    async () => {
      setup();
      await screen.findByRole('option', { name: 'Firm 1' });
      expect(visibleAccessClaims()).toHaveLength(0);
      await chooseFirm('Firm 1');
      // The one sentence the pane may say: "can give ... Spaarke will say what it did."
      expect(screen.getByTestId('add-firm-access-notice')).toHaveTextContent(
        /^Adding Firm 1 can give that firm's people access to this record\. Spaarke will say what it did\.$/
      );
      expect(visibleAccessClaims()).toHaveLength(0);
    },
    TIMEOUT
  );

  it(
    'can be chosen with the keyboard (arrow keys and Enter on the list)',
    async () => {
      setup();
      await screen.findByRole('option', { name: 'Firm 1' });
      act(() => screen.getByRole('listbox').focus());
      await userEvent.keyboard('{ArrowDown}');
      await userEvent.keyboard('{Enter}');
      await waitFor(() => expect(screen.getByTestId('add-firm-access-notice')).toHaveTextContent('Adding Firm'));
      expect(confirmButton()).toBeEnabled();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: nothing is sent until "Add firm" is clicked, and Add firm is disabled with no firm chosen',
    async () => {
      const { addFirmToRecord } = setup();
      await screen.findByRole('option', { name: 'Firm 1' });
      expect(confirmButton()).toBeDisabled();
      await chooseFirm('Firm 1');
      expect(addFirmToRecord).not.toHaveBeenCalled();
      expect(confirmButton()).toBeEnabled();
    },
    TIMEOUT
  );

  it(
    'must-not-fire: Cancel sends nothing and closes the panel',
    async () => {
      const { addFirmToRecord, onClose, onAdded } = setup();
      await chooseFirm('Firm 1');
      await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      expect(onClose).toHaveBeenCalledTimes(1);
      expect(addFirmToRecord).not.toHaveBeenCalled();
      expect(onAdded).not.toHaveBeenCalled();
    },
    TIMEOUT
  );

  it(
    'a 200 hands the result and the firm to the view, then closes',
    async () => {
      const { addFirmToRecord, onAdded, onClose } = setup();
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
      expect(addFirmToRecord).toHaveBeenCalledTimes(1);
      expect(addFirmToRecord).toHaveBeenCalledWith(target, firm(1).firmId);
      expect(onAdded).toHaveBeenCalledWith(added(), firm(1));
    },
    TIMEOUT
  );

  it(
    'changed: false (the firm was already there) is a success too',
    async () => {
      const { onAdded, onClose } = setup({
        addFirmToRecord: jest.fn(async () => added({ changed: false, notice: null })),
      });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(onClose).toHaveBeenCalled());
      expect(onAdded).toHaveBeenCalledTimes(1);
    },
    TIMEOUT
  );

  it(
    'disables the buttons while the request is in flight',
    async () => {
      let resolve: (outcome: AddFirmOutcome) => void = () => undefined;
      const addFirmToRecord = jest.fn(() => new Promise<AddFirmOutcome>(r => (resolve = r)));
      setup({ addFirmToRecord });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(addFirmToRecord).toHaveBeenCalledTimes(1));
      expect(confirmButton()).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
      // A second click while in flight cannot send a second request.
      fireEvent.click(confirmButton());
      expect(addFirmToRecord).toHaveBeenCalledTimes(1);
      await act(async () => resolve(added()));
    },
    TIMEOUT
  );
});

describe('AddFirmPanel — a refusal', () => {
  it(
    'shows the sentence inside the panel and keeps it open, with the buttons usable again',
    async () => {
      const { onClose, onAdded } = setup({
        addFirmToRecord: jest.fn(
          async (): Promise<AddFirmOutcome> => ({
            kind: 'refused',
            status: 422,
            code: 'ebilling.firm_assign.firm_inactive',
            message: 'This firm is inactive. Choose another firm.',
          })
        ),
      });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      const refusal = await screen.findByTestId('add-firm-refusal');
      expect(refusal).toHaveTextContent('This firm is inactive. Choose another firm.');
      expect(onClose).not.toHaveBeenCalled();
      expect(onAdded).not.toHaveBeenCalled();
      expect(confirmButton()).toBeEnabled();
      // A refusal without the slots_full code offers no link to Spaarke.
      expect(screen.queryByTestId('open-link')).not.toBeInTheDocument();
    },
    TIMEOUT
  );

  it(
    'slots_full also shows the "Open in Spaarke" link the view supplies; no other code does',
    async () => {
      const outcome = (code: string): AddFirmOutcome => ({
        kind: 'refused',
        status: 409,
        code,
        message: `Refused: ${code}`,
      });
      const addFirmToRecord = jest
        .fn()
        .mockResolvedValueOnce(outcome('ebilling.firm_assign.slots_full'))
        .mockResolvedValueOnce(outcome('ebilling.firm_assign.record_changed'));
      setup({ addFirmToRecord, openInSpaarke: <a data-testid="open-link">Open in Spaarke</a> });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      expect(await screen.findByTestId('open-link')).toBeInTheDocument();
      await userEvent.click(confirmButton());
      await waitFor(() => expect(screen.getByTestId('add-firm-refusal')).toHaveTextContent('record_changed'));
      expect(screen.queryByTestId('open-link')).not.toBeInTheDocument();
    },
    TIMEOUT
  );
});

describe('AddFirmPanel — the access pass is still pending', () => {
  it(
    'shows the saved text with Retry, still hands the firm to the view, and Retry resends the SAME request',
    async () => {
      const addFirmToRecord = jest
        .fn()
        .mockResolvedValueOnce(added({ accessState: 'pending', notice: null }))
        .mockResolvedValueOnce(added());
      const { onAdded, onClose } = setup({ addFirmToRecord });
      await chooseFirm('Firm 2');
      await userEvent.click(confirmButton());

      const pending = await screen.findByTestId('add-firm-pending');
      expect(pending).toHaveTextContent(FIRM_ACCESS_PENDING_MESSAGE);
      expect(FIRM_ACCESS_PENDING_MESSAGE).toBe('Saved. Spaarke is still updating who can see this record.');
      // The firm is already on the record: the view refreshes and selects it now. The panel stays open for Retry.
      expect(onAdded).toHaveBeenCalledTimes(1);
      expect(onAdded.mock.calls[0]![1]).toEqual(firm(2));
      expect(onClose).not.toHaveBeenCalled();

      await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
      await waitFor(() => expect(onClose).toHaveBeenCalledTimes(1));
      expect(addFirmToRecord).toHaveBeenCalledTimes(2);
      // The same request: the same record and the same firm.
      expect(addFirmToRecord.mock.calls[1]).toEqual(addFirmToRecord.mock.calls[0]);
      expect(addFirmToRecord.mock.calls[1]).toEqual([target, firm(2).firmId]);
    },
    TIMEOUT
  );

  it(
    'a Retry that is refused shows the refusal and keeps Retry',
    async () => {
      const addFirmToRecord = jest
        .fn()
        .mockResolvedValueOnce(added({ accessState: 'pending', notice: null }))
        .mockResolvedValueOnce({
          kind: 'refused',
          status: 503,
          code: 'ebilling.submission.record_unavailable',
          message: "Spaarke couldn't do this right now. Try again.",
        });
      const { onClose } = setup({ addFirmToRecord });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await userEvent.click(await screen.findByRole('button', { name: 'Retry' }));
      expect(await screen.findByTestId('add-firm-refusal')).toHaveTextContent("Spaarke couldn't do this right now.");
      expect(screen.getByRole('button', { name: 'Retry' })).toBeEnabled();
      expect(onClose).not.toHaveBeenCalled();
    },
    TIMEOUT
  );
});

describe('AddFirmPanel — focus', () => {
  it(
    'a11y: opening the panel puts focus on its search box (the entry button that opened it is gone)',
    async () => {
      setup();
      await screen.findByRole('option', { name: 'Firm 1' });
      expect(screen.getByLabelText('Search law firms')).toHaveFocus();
    },
    TIMEOUT
  );

  it(
    'a11y: the pending view puts focus on Retry, and a refused Retry puts it back on Retry',
    async () => {
      const addFirmToRecord = jest
        .fn()
        .mockResolvedValueOnce(added({ accessState: 'pending', notice: null }))
        .mockResolvedValueOnce({
          kind: 'refused',
          status: 503,
          code: 'ebilling.submission.record_unavailable',
          message: "Spaarke couldn't do this right now. Try again.",
        });
      setup({ addFirmToRecord });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await screen.findByTestId('add-firm-pending');
      await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toHaveFocus());
      await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
      await screen.findByTestId('add-firm-refusal');
      await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toHaveFocus());
    },
    TIMEOUT
  );
});

describe('AddFirmPanel — an add in flight (K2) and stale answers (F3)', () => {
  it(
    'K2: reports busy while the add is in flight, and not busy once it ends (and when the panel goes away)',
    async () => {
      let resolve: (outcome: AddFirmOutcome) => void = () => undefined;
      const addFirmToRecord = jest.fn(() => new Promise<AddFirmOutcome>(r => (resolve = r)));
      const onBusyChange = jest.fn();
      setup({ addFirmToRecord, onBusyChange });
      await screen.findByRole('option', { name: 'Firm 1' });
      expect(onBusyChange).not.toHaveBeenCalledWith(true);
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true));
      await act(async () => resolve(added({ accessState: 'pending', notice: null })));
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(false));
    },
    TIMEOUT
  );

  it(
    'F3: when the panel goes away while the add is in flight, the late answer is dropped (no onAdded, no onClose) and busy ends',
    async () => {
      let resolve: (outcome: AddFirmOutcome) => void = () => undefined;
      const addFirmToRecord = jest.fn(() => new Promise<AddFirmOutcome>(r => (resolve = r)));
      const onBusyChange = jest.fn();
      const { unmount, onAdded, onClose } = setup({ addFirmToRecord, onBusyChange });
      await chooseFirm('Firm 1');
      await userEvent.click(confirmButton());
      await waitFor(() => expect(onBusyChange).toHaveBeenLastCalledWith(true));
      unmount();
      expect(onBusyChange).toHaveBeenLastCalledWith(false);
      await act(async () => resolve(added()));
      expect(onAdded).not.toHaveBeenCalled();
      expect(onClose).not.toHaveBeenCalled();
    },
    TIMEOUT
  );

  it(
    'F3: type search A, then B; B page 1 resolves first and A page 1 LAST: only B results show',
    async () => {
      const waiting = new Map<string, (page: LawFirmPage) => void>();
      const listLawFirms = jest.fn(
        (args: { search?: string; page?: number }) =>
          new Promise<LawFirmPage>(resolve => {
            waiting.set(args.search ?? '', resolve);
          })
      );
      setup({ listLawFirms });
      await waitFor(() => expect(waiting.has('')).toBe(true));
      await act(async () => waiting.get('')!({ items: [firm(1)], page: 1, hasMore: false }));
      await screen.findByRole('option', { name: 'Firm 1' });

      const search = screen.getByLabelText('Search law firms');
      fireEvent.change(search, { target: { value: 'A' } });
      await waitFor(() => expect(waiting.has('A')).toBe(true), { timeout: ADD_FIRM_SEARCH_DEBOUNCE_MS * 5 });
      fireEvent.change(search, { target: { value: 'B' } });
      await waitFor(() => expect(waiting.has('B')).toBe(true), { timeout: ADD_FIRM_SEARCH_DEBOUNCE_MS * 5 });

      await act(async () => waiting.get('B')!({ items: [firm(20)], page: 1, hasMore: false }));
      expect(await screen.findByRole('option', { name: 'Firm 20' })).toBeInTheDocument();
      // A answers last, after B: it belongs to a search that is gone.
      await act(async () => waiting.get('A')!({ items: [firm(10)], page: 1, hasMore: false }));
      expect(screen.getByRole('option', { name: 'Firm 20' })).toBeInTheDocument();
      expect(screen.queryByRole('option', { name: 'Firm 10' })).not.toBeInTheDocument();
      expect(screen.queryByRole('option', { name: 'Firm 1' })).not.toBeInTheDocument();
    },
    TIMEOUT
  );
});
