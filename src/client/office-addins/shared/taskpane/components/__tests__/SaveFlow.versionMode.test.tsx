/**
 * FR-11 end to end through the pane (spaarkeai-word-add-in-r1 task 024): the identity state SaveFlow receives
 * decides what the save REQUEST carries — a version by default for a resolved document, a create on the
 * explicit override or for an unidentified one, nothing at all while the mode is unsettled.
 *
 * The Profile section and the Related-to picker are stubbed: they are covered by their own suites, and
 * neither decides what Save sends. Plain DOM assertions (`.checked`, `.disabled`) rather than jest-dom
 * matchers — see SaveModeSection.test.tsx for why.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import type { DocumentIdentityOutcome, DocumentIdentityState } from '../../services/documentIdentityService';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return { RelatedToPicker: () => ReactModule.createElement('div', { 'data-testid': 'related-to-picker' }) };
});
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: { digest: jest.fn(async () => new Uint8Array(32).buffer) },
});

// jsdom has no ResizeObserver; Fluent's MessageBar (auto layout — the pane's error bar) needs one to render.
class ResizeObserverStub {
  observe(): void {
    /* no-op: layout is irrelevant to what Save sends */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
Object.defineProperty(window, 'ResizeObserver', { configurable: true, writable: true, value: ResizeObserverStub });

const DOCUMENT_ID = '8c135b45-5da8-f111-aaab-7ced8ddc4a05';
const RESOLVED: DocumentIdentityOutcome = {
  kind: 'resolved',
  documentId: DOCUMENT_ID,
  documentName: 'Engagement Letter',
  fileName: 'Engagement Letter.docx',
  relatedRecord: null,
};

type FakeResponse = { ok: boolean; status: number; text: () => Promise<string>; json: () => Promise<unknown> };
const json = (ok: boolean, status: number, body: unknown): FakeResponse => ({
  ok,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

let saveResponses: FakeResponse[];

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  saveResponses = [];
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const next = saveResponses.shift();
      if (!next) throw new Error('Unexpected save request');
      return next;
    }
    return json(true, 200, { jobId: 'job-1', status: 'Running', completedPhases: [] });
  });
});

const accepted = () =>
  json(true, 202, {
    jobId: 'job-1',
    statusUrl: '/api/office/jobs/job-1',
    streamUrl: '/api/office/jobs/job-1/stream',
    status: 'Queued',
    duplicate: false,
    correlationId: 'corr-1',
  });

const isChecked = (element: HTMLElement): boolean => (element as HTMLInputElement).checked;
const isDisabled = (element: HTMLElement): boolean => (element as HTMLButtonElement).disabled;

function renderPane(documentIdentity: DocumentIdentityState | undefined) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Engagement%20Letter.docx"
        itemName="Engagement Letter"
        documentContentBase64="UEsDBBQAAAAIAAAAIQA="
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        {...(documentIdentity !== undefined ? { documentIdentity } : {})}
        onRetryDocumentIdentity={jest.fn()}
      />
    </FluentProvider>
  );
}

function saveCallCount(): number {
  return mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save')).length;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the raw JSON wire bodies, asserted field by field
async function sentBodies(): Promise<Array<Record<string, any>>> {
  await waitFor(() => expect(saveCallCount()).toBeGreaterThan(0));
  return mockFetch.mock.calls
    .filter(([url]) => String(url).includes('/api/office/save'))
    .map(([, init]) => JSON.parse(String((init as RequestInit).body)));
}

describe('SaveFlow — FR-11 save mode (task 024)', () => {
  it('a resolved identity defaults to a version save: the request names the existing document', async () => {
    saveResponses.push(accepted());
    renderPane(RESOLVED);

    expect(isChecked(screen.getByRole('radio', { name: 'A new version of “Engagement Letter”' }))).toBe(true);
    // A version save neither re-files nor renames, so those inputs are not offered.
    expect(screen.queryByTestId('related-to-picker')).toBeNull();
    expect(screen.queryByLabelText('Document name')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Save version' }));

    const [body] = await sentBodies();
    expect(body!.document.existingDocumentId).toBe(DOCUMENT_ID);
    expect(body!.document.isNewVersion).toBe(true);
    expect(body).not.toHaveProperty('targetEntity');
  });

  it('the explicit "A new document" override forces a create even though the identity is resolved', async () => {
    saveResponses.push(accepted());
    renderPane(RESOLVED);

    fireEvent.click(screen.getByRole('radio', { name: 'A new document' }));
    expect(screen.getByTestId('related-to-picker')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body!.contentType).toBe('Document');
    expect(body!.document).not.toHaveProperty('existingDocumentId');
    expect(body!.document).not.toHaveProperty('isNewVersion');
  });

  it.each<[string, DocumentIdentityState | undefined]>([
    ['a document Spaarke does not track', { kind: 'new', reason: 'not_spaarke_document' }],
    ['no identity at all', undefined],
  ])('%s: a plain create, with no version affordance', async (_label, identity) => {
    saveResponses.push(accepted());
    renderPane(identity);

    expect(screen.queryByRole('radio')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body!.document).not.toHaveProperty('existingDocumentId');
  });

  it('an undetermined identity sends nothing until the user explicitly chooses a new document', async () => {
    saveResponses.push(accepted());
    renderPane({ kind: 'indeterminate', reason: 'unavailable' });

    const save = screen.getByRole('button', { name: 'Save' });
    expect(isDisabled(save)).toBe(true);

    fireEvent.click(screen.getByRole('checkbox', { name: 'Save it as a new document anyway' }));
    expect(isDisabled(save)).toBe(false);
    fireEvent.click(save);

    const [body] = await sentBodies();
    expect(body!.document).not.toHaveProperty('existingDocumentId');
  });

  it.each<[string, DocumentIdentityState]>([
    ['conflict', { kind: 'conflict' }],
    ['checking', 'checking'],
  ])('%s: Save stays disabled and no save-as-new is offered', (_label, identity) => {
    renderPane(identity);

    expect(isDisabled(screen.getByRole('button', { name: 'Save' }))).toBe(true);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.queryByRole('radio', { name: 'A new document' })).toBeNull();
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('a refused version save (OFFICE_016) offers "Save as new document", which switches the mode without saving', async () => {
    saveResponses.push(
      json(false, 404, {
        type: 'https://spaarke.com/errors/office/OFFICE_016',
        title: 'Version Target Not Found',
        status: 404,
        detail: 'The document this save was meant to add a version to could not be found. Nothing was saved.',
        errorCode: 'OFFICE_016',
      })
    );
    renderPane(RESOLVED);

    fireEvent.click(screen.getByRole('button', { name: 'Save version' }));
    const offer = await screen.findByRole('button', { name: 'Save as new document' });
    fireEvent.click(offer);

    expect(isChecked(screen.getByRole('radio', { name: 'A new document' }))).toBe(true);
    expect(isDisabled(screen.getByRole('button', { name: 'Save' }))).toBe(false);
    expect(saveCallCount()).toBe(1);
  });

  it('announces a mode change through the live region (NFR-11)', async () => {
    renderPane(RESOLVED);

    fireEvent.click(screen.getByRole('radio', { name: 'A new document' }));

    await waitFor(() =>
      expect(screen.getByRole('status').textContent).toContain(
        'Save mode: a new document. The existing document will not be changed.'
      )
    );
  });
});
