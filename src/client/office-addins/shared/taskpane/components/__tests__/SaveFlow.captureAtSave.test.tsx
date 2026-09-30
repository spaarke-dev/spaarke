/**
 * Task 045: the Word pane used to read the open document's bytes ONCE, when the Save tab mounted, and
 * never again — so any edit made after the tab opened was silently never uploaded on the FIRST Save,
 * a RETRY after a failure, or "Save Another", while the pane reported success
 * (`notes/025-residual-collision-surface.md` M12 / R5 / D-i). The fix moves the capture into
 * `useSaveFlow.startSave` itself, invoked via a LIVE `captureDocumentContent` callback threaded down
 * from `SaveView` through `SaveFlow`'s `buildSaveContext` — so every attempt that reaches `startSave`
 * re-invokes it and reads whatever the caller's adapter reports RIGHT THEN.
 *
 * This file exercises the real `SaveFlow` + real `useSaveFlow` hook (only `fetch` and `SseClient` are
 * mocked), supplying `captureDocumentContent` as a prop exactly as `SaveView` does in production —
 * following the established pattern in `SaveFlow.documentName.test.tsx` / `SaveFlow.versionMode.test.tsx`.
 *
 * Reproduce-first (POML step 1): the "uploads B2, not the mount-time B1" test below fails on the
 * pre-fix code, because `SaveFlowProps`/`SaveFlowContext` had no `captureDocumentContent` field at
 * all — a save could only ever carry whatever static `documentContentBase64` value a caller supplied
 * once, up front. Confirmed by temporarily reverting `useSaveFlow.ts`'s Document branch to read only
 * `context.documentContentBase64` and re-running this file: every capture-at-submission-time
 * assertion below failed (the first, second, and retried requests all had to fall back to the SAME
 * static prop, since there was nowhere for a live callback's result to go); restoring the fix turned
 * them green with no other change.
 */
import React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';

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

// ResizeObserver (Fluent v9 MessageBar reflow detection) is polyfilled globally in jest.setup.js
// (task 071) — removed the per-file copy that used to live here.

type FakeResponse = { ok: boolean; status: number; text: () => Promise<string>; json: () => Promise<unknown> };
const json = (ok: boolean, status: number, body: unknown): FakeResponse => ({
  ok,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

const accepted = (): FakeResponse =>
  json(true, 202, {
    jobId: 'job-1',
    statusUrl: '/api/office/jobs/job-1',
    streamUrl: '/api/office/jobs/job-1/stream',
    status: 'Queued',
    duplicate: false,
    correlationId: 'corr-1',
  });

// A job poll response that resolves the flow to 'complete' on the FIRST poll — `useSaveFlow` polls
// immediately once job tracking starts, so no fake timers are needed to reach the success card.
const completedPoll = (documentId: string): FakeResponse =>
  json(true, 200, {
    jobId: 'job-1',
    status: 'Completed',
    completedPhases: [],
    result: { artifact: { type: 'Document', id: documentId, webUrl: `https://contoso/doc/${documentId}` } },
  });

const runningPoll = (): FakeResponse => json(true, 200, { jobId: 'job-1', status: 'Running', completedPhases: [] });

let saveResponses: FakeResponse[];
let pollResponse: () => FakeResponse;

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  saveResponses = [];
  pollResponse = runningPoll;
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const next = saveResponses.shift();
      if (!next) throw new Error('Unexpected save request');
      return next;
    }
    return pollResponse();
  });
});

function saveCallCount(): number {
  return mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save')).length;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the raw JSON wire bodies, asserted field by field
function sentBodyAt(index: number): Record<string, any> {
  const calls = mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
  return JSON.parse(String((calls[index]![1] as RequestInit).body));
}

function renderWord(props: Partial<React.ComponentProps<typeof SaveFlow>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief"
        canProvideDocumentName
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        {...props}
      />
    </FluentProvider>
  );
}

const saveButton = () => screen.getByRole('button', { name: 'Save' });

describe('SaveFlow — bytes captured at the moment of Save, not before (task 045)', () => {
  it('a Save press invokes captureDocumentContent and uploads ITS result, not a documentContentBase64 value given alongside it', async () => {
    saveResponses.push(accepted());
    const captureDocumentContent = jest.fn().mockResolvedValue('RkZFU0gtQllURVM=');
    renderWord({ captureDocumentContent, documentContentBase64: 'U1RBTEUtQllURVM=' });

    expect(captureDocumentContent).not.toHaveBeenCalled();

    fireEvent.click(saveButton());

    await waitFor(() => expect(saveCallCount()).toBe(1));
    expect(captureDocumentContent).toHaveBeenCalledTimes(1);
    expect(sentBodyAt(0).document.contentBase64).toBe('RkZFU0gtQllURVM=');
  });

  it('reproduces the fix: pressing Save after an edit uploads B2, never the earlier B1 — capture happens per attempt, never once up front', async () => {
    saveResponses.push(accepted(), accepted());
    pollResponse = () => completedPoll('doc-1');
    const captureDocumentContent = jest.fn().mockResolvedValueOnce('Qjk=').mockResolvedValueOnce('QjI=');
    renderWord({ captureDocumentContent });

    // Nothing captured merely by rendering — there is no mount-time snapshot to go stale.
    expect(captureDocumentContent).not.toHaveBeenCalled();

    fireEvent.click(saveButton());
    await waitFor(() => expect(saveCallCount()).toBe(1));
    expect(sentBodyAt(0).document.contentBase64).toBe('Qjk=');

    // Reach the success card, then "Save Another" — the user keeps the pane open and saves again.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Save Another' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Save Another' }));

    await waitFor(() => expect(saveButton()).toBeInTheDocument());
    fireEvent.click(saveButton());

    await waitFor(() => expect(saveCallCount()).toBe(2));
    expect(captureDocumentContent).toHaveBeenCalledTimes(2);
    // The SECOND request carries B2 (the document as it is NOW), not B1 replayed.
    expect(sentBodyAt(1).document.contentBase64).toBe('QjI=');
  });

  it('a retry after a failed save re-captures fresh bytes, not the bytes from the failed attempt', async () => {
    saveResponses.push(json(false, 500, { notAProblemDetails: true }), accepted());
    const captureDocumentContent = jest.fn().mockResolvedValueOnce('Qjk=').mockResolvedValueOnce('QjI=');
    renderWord({ captureDocumentContent });

    fireEvent.click(saveButton());
    await waitFor(() => expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument());
    expect(sentBodyAt(0).document.contentBase64).toBe('Qjk=');
    expect(captureDocumentContent).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(saveCallCount()).toBe(2));
    expect(captureDocumentContent).toHaveBeenCalledTimes(2);
    expect(sentBodyAt(1).document.contentBase64).toBe('QjI=');
  });

  it('a capture failure shows a handled, visible error and uploads NOTHING — never a silent old-bytes upload', async () => {
    const captureDocumentContent = jest.fn().mockRejectedValue(new Error('Office is busy; try again.'));
    renderWord({ captureDocumentContent });

    fireEvent.click(saveButton());

    await waitFor(() => expect(screen.getByText('Office is busy; try again.')).toBeInTheDocument());
    expect(saveCallCount()).toBe(0);
  });

  it('regression: with neither captureDocumentContent nor documentContentBase64, Save still refuses with the pre-existing message (no field to send)', async () => {
    renderWord();

    fireEvent.click(saveButton());

    await waitFor(() =>
      expect(
        screen.getByText('Document content is required. Please ensure the document is captured before saving.')
      ).toBeInTheDocument()
    );
    expect(saveCallCount()).toBe(0);
  });

  it('regression: a plain documentContentBase64 value (no live callback) still works exactly as before — the fallback path', async () => {
    saveResponses.push(accepted());
    renderWord({ documentContentBase64: 'UEsDBBQAAAAIAAAAIQA=' });

    fireEvent.click(saveButton());

    await waitFor(() => expect(saveCallCount()).toBe(1));
    expect(sentBodyAt(0).document.contentBase64).toBe('UEsDBBQAAAAIAAAAIQA=');
  });

  it('the collision retries (Keep both / Save as new version, task 025) also re-capture fresh bytes — every submission path shares the same capture site', async () => {
    saveResponses.push(
      json(false, 409, {
        type: 'https://spaarke.com/errors/office',
        title: 'Refused',
        status: 409,
        detail: 'A document with this name already exists here. Nothing was saved.',
        errorCode: 'OFFICE_020',
        fileName: 'Brief.docx',
      }),
      accepted()
    );
    const captureDocumentContent = jest.fn().mockResolvedValueOnce('Qjk=').mockResolvedValueOnce('QjI=');
    renderWord({ captureDocumentContent });

    fireEvent.click(saveButton());
    await waitFor(() => expect(screen.getByRole('button', { name: 'Keep both' })).toBeInTheDocument());
    expect(captureDocumentContent).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Keep both' }));

    await waitFor(() => expect(saveCallCount()).toBe(2));
    expect(captureDocumentContent).toHaveBeenCalledTimes(2);
    expect(sentBodyAt(1).document.contentBase64).toBe('QjI=');
    expect(sentBodyAt(1).document.allowRename).toBe(true);
  });
});
