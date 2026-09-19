/**
 * Unit tests for task 020 (FR-06): the Document Name field defaults to the open document's own file
 * name (minus its extension) for Word, is editable in-pane behind a pencil affordance, and the final
 * value reaches `document.title` on Save — independent of the sanitized `document.fileName`.
 *
 * Kept in a SEPARATE file from `SaveFlow.test.tsx` (which pre-dates this task and already carries
 * unrelated drift against the current component — the 2026-09-09 "consciously accepted" test-file
 * bucket, project CLAUDE.md Decisions Made), following the precedent set by `SaveFlow.versionMode.test.tsx`
 * and `SaveFlow.openRecord.test.tsx`. A light rendering smoke-check ALSO lives in `SaveFlow.test.tsx`
 * per the task's own instruction to add tests there.
 *
 * The escalation trigger this task carries (CLAUDE.md §6 / the POML's own `<escalation>`): separating the
 * display name from the SPE filename must NOT change behavior for Email/Attachment. The "Outlook path
 * untouched" tests below are the regression guard for that trigger — Outlook gets no default, no pencil,
 * and its `email.subject` / `email.isNameSystemDerived` wiring (task 046 (b)) is unaffected.
 *
 * Coordinator follow-up (post-merge review of this task): the default/pencil UI is gated on the
 * `canProvideDocumentName` capability (`HostCapabilities`, `shared/adapters/types.ts`) rather than on
 * `hostType` directly, per NFR-10 and task 040's precedent (`canShowLinkedTodos` / `canSuggestRelatedRecords`).
 * `renderWord()` below passes it explicitly (mirroring what `SaveView` derives from a real Word
 * adapter); `renderOutlook()` deliberately does NOT pass it, so it defaults to `false` — mirroring
 * what `SaveView` derives from a real Outlook adapter. Behavior is unchanged either way.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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

// jsdom has no ResizeObserver; Fluent's MessageBar (auto layout — the pane's error bar) needs one to render.
class ResizeObserverStub {
  observe(): void {
    /* no-op */
  }
  unobserve(): void {
    /* no-op */
  }
  disconnect(): void {
    /* no-op */
  }
}
Object.defineProperty(window, 'ResizeObserver', { configurable: true, writable: true, value: ResizeObserverStub });

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

function renderWord(itemName = 'Acme Merger Agreement.docx') {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Acme%20Merger%20Agreement.docx"
        itemName={itemName}
        documentContentBase64="UEsDBBQAAAAIAAAAIQA="
        // Coordinator follow-up: the default/pencil UI is gated on this capability (NFR-10), never
        // on hostType — SaveView derives it from hostAdapter.getCapabilities().canProvideDocumentName
        // (true for Word); tests supply it directly since there is no live hostAdapter here.
        canProvideDocumentName
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

function renderOutlook(itemName = 'Q3 Renewal Terms') {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="outlook"
        itemId="msg-1"
        itemName={itemName}
        senderEmail="alex@example.com"
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

const editButton = () => screen.getByRole('button', { name: /edit document name/i });
const documentNameInput = () => screen.getByRole('textbox', { name: /document name/i }) as HTMLInputElement;

describe('SaveFlow — Document Name defaulting (task 020 / FR-06, Word)', () => {
  it("defaults to the open document's filename minus its extension, read-only behind a pencil", () => {
    renderWord();

    expect(screen.getByText('Acme Merger Agreement')).toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: /document name/i })).not.toBeInTheDocument();
    expect(editButton()).toBeInTheDocument();
  });

  it('leaves a value with no extension (e.g. an untitled document) unchanged', () => {
    // "Untitled Document" also appears verbatim in the (unrelated) Document Info header above, since
    // itemName carries no extension to strip here — open the field itself for an unambiguous read.
    renderWord('Untitled Document');
    fireEvent.click(editButton());
    expect(documentNameInput().value).toBe('Untitled Document');
  });

  it('the pencil affordance reveals an editable Input pre-filled with the current value', () => {
    renderWord();

    fireEvent.click(editButton());

    expect(documentNameInput().value).toBe('Acme Merger Agreement');
    expect(screen.queryByRole('button', { name: /edit document name/i })).not.toBeInTheDocument();
  });

  it('is keyboard-activatable: Enter opens edit mode and commits it, announcing both transitions (NFR-11)', async () => {
    const user = userEvent.setup();
    renderWord();

    editButton().focus();
    await user.keyboard('{Enter}');

    expect(documentNameInput()).toHaveFocus();
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Editing document name'));

    await user.keyboard('{Enter}');

    expect(screen.queryByRole('textbox', { name: /document name/i })).not.toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Document name updated'));
  });

  it('Escape cancels an edit without changing the displayed or saved value', () => {
    renderWord();

    fireEvent.click(editButton());
    fireEvent.change(documentNameInput(), { target: { value: 'Something Else Entirely' } });
    fireEvent.keyDown(documentNameInput(), { key: 'Escape' });

    expect(screen.getByText('Acme Merger Agreement')).toBeInTheDocument();
    expect(screen.queryByText('Something Else Entirely')).not.toBeInTheDocument();
  });

  it('an edited Document Name reaches document.title on Save, independent of the sanitized document.fileName', async () => {
    saveResponses.push(accepted());
    renderWord();

    fireEvent.click(editButton());
    fireEvent.change(documentNameInput(), { target: { value: 'Acme Merger Agreement - Execution Copy' } });
    fireEvent.keyDown(documentNameInput(), { key: 'Enter' });

    expect(screen.getByText('Acme Merger Agreement - Execution Copy')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body!.document.title).toBe('Acme Merger Agreement - Execution Copy');
    expect(body!.document.fileName).toBe('Acme Merger Agreement - Execution Copy.docx');
  });

  it('committing via blur (clicking away) also saves the edit', async () => {
    saveResponses.push(accepted());
    renderWord();

    fireEvent.click(editButton());
    fireEvent.change(documentNameInput(), { target: { value: 'Brief - Redline' } });
    fireEvent.blur(documentNameInput());

    expect(screen.getByText('Brief - Redline')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    const [body] = await sentBodies();
    expect(body!.document.title).toBe('Brief - Redline');
  });

  it('committing an emptied edit falls back to the filename default — sprk_documentname is never empty', async () => {
    saveResponses.push(accepted());
    renderWord();

    fireEvent.click(editButton());
    fireEvent.change(documentNameInput(), { target: { value: '   ' } });
    fireEvent.keyDown(documentNameInput(), { key: 'Enter' });

    expect(screen.getByText('Acme Merger Agreement')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    const [body] = await sentBodies();
    expect(body!.document.title).toBe('Acme Merger Agreement');
    expect(body!.document.title).not.toBe('');
  });

  it('a Document Name over 850 characters is sent to the server unchanged — the server bounds it, not the client', async () => {
    saveResponses.push(accepted());
    renderWord();
    const longName = 'A'.repeat(900);

    fireEvent.click(editButton());
    fireEvent.change(documentNameInput(), { target: { value: longName } });
    fireEvent.keyDown(documentNameInput(), { key: 'Enter' });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    const [body] = await sentBodies();
    expect(body!.document.title).toHaveLength(900);
  });

  it('Cancel returns the field to the filename default, discarding an in-progress edit', () => {
    renderWord();

    fireEvent.click(editButton());
    fireEvent.change(documentNameInput(), { target: { value: 'Discarded Draft Name' } });

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.getByText('Acme Merger Agreement')).toBeInTheDocument();
    expect(screen.queryByText('Discarded Draft Name')).not.toBeInTheDocument();
  });

  it('sends no top-level documentMetadata field (the dead wire field useSaveFlow.ts used to send)', async () => {
    saveResponses.push(accepted());
    renderWord();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body).not.toHaveProperty('documentMetadata');
  });
});

describe('SaveFlow — Document Name, Outlook (Email) path untouched (escalation-trigger regression guard)', () => {
  it('shows no default and no pencil affordance — the field stays the plain, empty Textarea it was before task 020', () => {
    renderOutlook();

    expect(screen.queryByRole('button', { name: /edit document name/i })).not.toBeInTheDocument();
    const field = screen.getByRole('textbox', { name: /document name/i }) as HTMLTextAreaElement;
    expect(field.value).toBe('');
    expect(field.tagName).toBe('TEXTAREA');
  });

  it('an untouched save uses the subject as email.subject with isNameSystemDerived true (task 046 (b) unaffected)', async () => {
    saveResponses.push(accepted());
    renderOutlook('Q3 Renewal Terms');

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body!.email.subject).toBe('Q3 Renewal Terms');
    expect(body!.email.isNameSystemDerived).toBe(true);
  });

  it('typing a name still marks it user-typed (isNameSystemDerived false) — the pre-existing behavior', async () => {
    saveResponses.push(accepted());
    renderOutlook('Q3 Renewal Terms');

    const field = screen.getByRole('textbox', { name: /document name/i });
    fireEvent.change(field, { target: { value: 'Renewal — filed copy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body!.email.subject).toBe('Renewal — filed copy');
    expect(body!.email.isNameSystemDerived).toBe(false);
  });

  it('sends no top-level documentMetadata field for Email either', async () => {
    saveResponses.push(accepted());
    renderOutlook();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const [body] = await sentBodies();
    expect(body).not.toHaveProperty('documentMetadata');
  });
});
