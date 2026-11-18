/**
 * DocumentEmailWizard.lookupLayer.test.tsx - spaarke-ontology-platform-r1 task 194 (owner 185 checks C1/C2).
 *
 * In a PCF host the wizard shares the Dataverse page's document, so the To/Cc/Bcc side pane opened behind the blocking
 * wizard. With `nonBlocking` the wizard yields to the pane through the existing SprkModal `yieldToSidePane`
 * (sidePaneLayering.ts), the SendEmailDialog `wrapLookup` contract. Contract under test, on the real shell and composer:
 *
 * - a lookup that starts lowers the wizard's page layer just below the pane (dimmed, inert); the pick, a cancel or a
 *   failure restores it;
 * - a pick lands as chips; a cancel keeps the earlier recipients; a failure behaves as a cancel (no crash);
 * - `onSidePaneLookupChange` hears the lookup open and settle (the framed host's signal), without the wizard yielding;
 * - without either prop the wizard is the blocking modal it was and the host handlers reach the composer unchanged.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { DocumentEmailWizard } from '../DocumentEmailWizard';
import type { IDocumentEmailWizardProps } from '../DocumentEmailWizard';
import type { IEmailComposeHandlers } from '../../EmailComposer/composeOnlySend';
import type { IRecipient } from '../../EmailComposer/EmailComposer.types';
import type { IDataService } from '../../../types/serviceInterfaces';

const PANE_Z = '1000';

interface Deferred<T> {
  promise: Promise<T>;
  resolve: (value: T) => void;
  reject: (err: unknown) => void;
}
function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (err: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

function pageLayerOf(el: Element): HTMLElement {
  let node = el as HTMLElement;
  while (node.parentElement && node.parentElement !== document.body) node = node.parentElement;
  return node;
}

/**
 * The host's lookup: like `Xrm.Utility.lookupObjects`, the pane renders in its own body-level layer a moment after the
 * call, and the promise settles when the user picks or cancels.
 */
function hostLookup() {
  const calls: Deferred<IRecipient[] | null>[] = [];
  let paneLayer: HTMLElement | null = null;
  const pane = document.createElement('div');
  (document as { elementsFromPoint?: unknown }).elementsFromPoint = jest.fn(() =>
    paneLayer?.isConnected ? [pane] : []
  );
  const onLookupRecipients = jest.fn((_field: 'to' | 'cc' | 'bcc') => {
    const call = deferred<IRecipient[] | null>();
    calls.push(call);
    setTimeout(() => {
      paneLayer = document.createElement('div');
      paneLayer.style.position = 'fixed';
      paneLayer.style.zIndex = PANE_Z;
      paneLayer.appendChild(pane);
      document.body.appendChild(paneLayer);
    }, 60);
    return call.promise.finally(() => paneLayer?.remove());
  });
  return { onLookupRecipients, last: () => calls[calls.length - 1] };
}

const summaryFetch = jest
  .fn()
  .mockResolvedValue({ ok: false, status: 503, json: async () => ({}), text: async () => '' });

function renderWizard(overrides: Partial<IDocumentEmailWizardProps>, handlers: IEmailComposeHandlers) {
  const props: IDocumentEmailWizardProps = {
    open: true,
    onClose: jest.fn(),
    selectedDocuments: [{ documentId: 'doc-1', name: 'Master Agreement.pdf', fileSizeBytes: 2048 }],
    authenticatedFetch: summaryFetch as never,
    bffBaseUrl: 'https://bff.example.test',
    clientUrl: 'https://org.crm.example',
    dataService: {} as IDataService,
    emailComposeHandlers: handlers,
    ...overrides,
  };
  return renderWithProviders(<DocumentEmailWizard {...props} />);
}

/** Confirm Selection → Summary → Compose. */
async function openComposeStep() {
  fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
  fireEvent.click(await screen.findByRole('button', { name: /^next$/i }));
  return screen.findByRole('button', { name: 'Look up To recipients' });
}

const wizard = () => screen.getByRole('dialog');
const toChips = () => within(screen.getByRole('group', { name: 'To' }));

describe('DocumentEmailWizard — the host lookup pane opens above the wizard (task 194)', () => {
  const originalElementsFromPoint = (document as { elementsFromPoint?: unknown }).elementsFromPoint;
  let warn: jest.SpyInstance;

  beforeEach(() => {
    summaryFetch.mockClear();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    warn.mockRestore();
    if (originalElementsFromPoint)
      (document as { elementsFromPoint?: unknown }).elementsFromPoint = originalElementsFromPoint;
    else delete (document as { elementsFromPoint?: unknown }).elementsFromPoint;
  });

  it('lowers the wizard under the pane while the lookup is open; a pick lands as chips and restores the wizard', async () => {
    const lookup = hostLookup();
    // The PCF passes both: the wizard yields, and the host hears it (to make a preview under the wizard yield too).
    const onSidePaneLookupChange = jest.fn();
    renderWizard({ nonBlocking: true, onSidePaneLookupChange }, { onLookupRecipients: lookup.onLookupRecipients });
    const toButton = await openComposeStep();
    const layer = pageLayerOf(wizard());
    const zBefore = layer.style.zIndex;
    expect(wizard()).not.toHaveAttribute('inert');

    fireEvent.click(toButton);
    expect(lookup.onLookupRecipients).toHaveBeenCalledWith('to');
    await waitFor(() => expect(layer.style.zIndex).toBe(String(Number(PANE_Z) - 1)));
    expect(wizard()).toHaveAttribute('inert');
    expect(wizard().style.filter).toBe('brightness(0.75)');
    expect(wizard().style.marginRight).toBe(''); // under the pane, not docked beside it
    expect(onSidePaneLookupChange.mock.calls).toEqual([[true]]);

    await act(async () => {
      lookup.last().resolve([{ email: 'partner@firm.example', displayName: 'Pat Partner', resolved: true }]);
    });
    await waitFor(() => expect(wizard()).not.toHaveAttribute('inert'));
    expect(layer.style.zIndex).toBe(zBefore);
    expect(wizard().style.filter).toBe('');
    expect(toChips().getByText('Pat Partner (partner@firm.example)')).toBeInTheDocument();
    expect(onSidePaneLookupChange.mock.calls).toEqual([[true], [false]]);
  });

  it('a cancel keeps the earlier recipients and restores the wizard', async () => {
    const lookup = hostLookup();
    renderWizard({ nonBlocking: true }, { onLookupRecipients: lookup.onLookupRecipients });
    const toButton = await openComposeStep();
    const layer = pageLayerOf(wizard());
    const zBefore = layer.style.zIndex;

    fireEvent.click(toButton);
    await act(async () => {
      lookup.last().resolve([{ email: 'first@firm.example', resolved: true }]);
    });
    await waitFor(() => expect(toChips().getByText('first@firm.example')).toBeInTheDocument());

    fireEvent.click(toButton);
    await waitFor(() => expect(wizard()).toHaveAttribute('inert'));
    await act(async () => {
      lookup.last().resolve(null);
    });
    await waitFor(() => expect(wizard()).not.toHaveAttribute('inert'));
    expect(layer.style.zIndex).toBe(zBefore);
    expect(toChips().getByText('first@firm.example')).toBeInTheDocument();
  });

  it('a failed lookup behaves as a cancel: no crash, earlier recipients kept, wizard restored', async () => {
    const lookup = hostLookup();
    renderWizard({ nonBlocking: true }, { onLookupRecipients: lookup.onLookupRecipients });
    const toButton = await openComposeStep();
    const layer = pageLayerOf(wizard());
    const zBefore = layer.style.zIndex;

    fireEvent.click(toButton);
    await act(async () => {
      lookup.last().resolve([{ email: 'first@firm.example', resolved: true }]);
    });
    await waitFor(() => expect(toChips().getByText('first@firm.example')).toBeInTheDocument());

    fireEvent.click(toButton);
    await waitFor(() => expect(wizard()).toHaveAttribute('inert'));
    await act(async () => {
      lookup.last().reject(new Error('pane failed'));
    });
    await waitFor(() => expect(wizard()).not.toHaveAttribute('inert'));
    expect(layer.style.zIndex).toBe(zBefore);
    expect(toChips().getByText('first@firm.example')).toBeInTheDocument();
    // The composer reports the failure and carries on.
    expect(warn).toHaveBeenCalledWith('[RecipientField] lookup failed:', 'To', expect.any(Error));
    expect(screen.getByRole('button', { name: 'Look up To recipients' })).toBeInTheDocument();
  });

  it('tells onSidePaneLookupChange when the lookup opens and settles, without the wizard itself yielding', async () => {
    const lookup = hostLookup();
    const onSidePaneLookupChange = jest.fn();
    renderWizard({ onSidePaneLookupChange }, { onLookupRecipients: lookup.onLookupRecipients });
    const toButton = await openComposeStep();

    fireEvent.click(toButton);
    expect(onSidePaneLookupChange).toHaveBeenLastCalledWith(true);
    expect(wizard()).not.toHaveAttribute('inert');
    await act(async () => {
      lookup.last().reject(new Error('pane failed'));
    });
    expect(onSidePaneLookupChange).toHaveBeenLastCalledWith(false);

    fireEvent.click(toButton);
    expect(onSidePaneLookupChange).toHaveBeenLastCalledWith(true);
    await act(async () => {
      lookup.last().resolve([{ email: 'partner@firm.example', resolved: true }]);
    });
    expect(onSidePaneLookupChange).toHaveBeenLastCalledWith(false);
    expect(onSidePaneLookupChange).toHaveBeenCalledTimes(4);
    expect(toChips().getByText('partner@firm.example')).toBeInTheDocument();
  });

  it('without the opt-ins the wizard stays the blocking modal it was and never yields', async () => {
    const lookup = hostLookup();
    renderWizard({}, { onLookupRecipients: lookup.onLookupRecipients });
    const toButton = await openComposeStep();
    expect(wizard()).toHaveAttribute('aria-modal', 'true');
    const layer = pageLayerOf(wizard());
    const zBefore = layer.style.zIndex;

    fireEvent.click(toButton);
    await new Promise(r => setTimeout(r, 200)); // the pane is up
    expect(wizard()).not.toHaveAttribute('inert');
    expect(layer.style.zIndex).toBe(zBefore);
    await act(async () => {
      lookup.last().resolve(null);
    });
  });
});
