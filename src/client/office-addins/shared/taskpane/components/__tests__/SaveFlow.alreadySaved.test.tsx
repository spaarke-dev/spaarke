/**
 * Task 111 (spaarkeai-word-add-in-r1, owner UAT round 11 items 3-6): a document that is ALREADY in Spaarke opens the
 * Save tab with the same green "Saved to Spaarke" box a save ends on.
 *
 * - filed → the box and the "Filed to" card name the record; no footer "Open Document" (the box has View Document);
 * - known unfiled (a URL-resolved identity, relatedRecord === null) → the box says "Not filed to a record yet." and
 *   the record picker + "File to record" replace the empty card; filing is `PUT /api/v1/documents/{id}` with EXACTLY
 *   ONE lookup, then the card/box show the record without a reload; a 403 shows a message and keeps the picker;
 * - unknown (a stamp-only identity, relatedRecordKnown === false) → neutral line only: NO picker, NO "not filed",
 *   and nothing is ever sent;
 * - a fresh unsaved document is unchanged (no box).
 *
 * Real SaveFlow + real useSaveFlow + the real documentFilingService; only fetch, SSE, the Profile section and the
 * picker (a stub that "picks" a fixed record) are doubled. The harness holds the identity state the way `App` does.
 */
import { useState } from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import {
  applyFiledRecord,
  type DocumentIdentityState,
  type ResolvedRelatedRecord,
} from '../../services/documentIdentityService';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return {
    RelatedToPicker: (props: { onChange: (e: unknown) => void; disabled?: boolean }) =>
      ReactModule.createElement(
        'div',
        { 'data-testid': 'related-to-picker' },
        ReactModule.createElement(
          'button',
          {
            type: 'button',
            disabled: props.disabled,
            onClick: () =>
              props.onChange({
                id: 'AAAAAAAA-0000-0000-0000-00000000000A',
                entityType: 'Matter',
                logicalName: 'sprk_matter',
                name: 'Acme v. Beta',
                displayInfo: 'MAT-000123',
              }),
          },
          'Pick matter'
        ),
        ReactModule.createElement(
          'button',
          {
            type: 'button',
            onClick: () =>
              props.onChange({
                id: 'BBBBBBBB-0000-0000-0000-00000000000B',
                entityType: 'Project',
                logicalName: 'sprk_project',
                name: 'Project Zed',
                displayInfo: 'PRJ-000009',
              }),
          },
          'Pick project'
        )
      ),
  };
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

const DOCUMENT_ID = '8c135b45-5da8-f111-aaab-7ced8ddc4a05';
const ORG_URL = 'https://contoso.crm.dynamics.com';
const originalOrgUrl = process.env.ORG_URL;

const FILED_RECORD: ResolvedRelatedRecord = {
  entityType: 'sprk_matter',
  id: '22222222-2222-2222-2222-222222222222',
  name: 'MAT-000777',
  displayName: 'Gamma Merger',
  number: 'MAT-000777',
};

const resolved = (
  extra: Partial<Extract<DocumentIdentityState, { kind: 'resolved' }>> = {}
): DocumentIdentityState => ({
  kind: 'resolved',
  documentId: DOCUMENT_ID,
  documentName: 'Engagement Letter',
  fileName: 'Engagement Letter.docx',
  relatedRecord: null,
  ...extra,
});

const FILED = resolved({ relatedRecord: FILED_RECORD });
const UNFILED = resolved();
/** What `applyStampPrecedence` produces for a stamp-only match: no names, no record, record UNKNOWN. */
const STAMP_ONLY = resolved({ documentName: '', fileName: '', relatedRecordKnown: false });

type FakeResponse = { ok: boolean; status: number; json: () => Promise<unknown> };
const fake = (ok: boolean, status: number, body: unknown = {}): FakeResponse => ({
  ok,
  status,
  json: async () => body,
});

function Pane({ initial, canOpenRecord = false }: { initial: DocumentIdentityState; canOpenRecord?: boolean }) {
  const [identity, setIdentity] = useState<DocumentIdentityState | undefined>(initial);
  return (
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Engagement%20Letter.docx"
        itemName="Engagement Letter"
        documentContentBase64="UEsDBBQAAAAIAAAAIQA="
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        canOpenRecord={canOpenRecord}
        {...(identity !== undefined ? { documentIdentity: identity } : {})}
        onRetryDocumentIdentity={jest.fn()}
        onDocumentFiled={(documentId, record) => setIdentity(prev => applyFiledRecord(prev, documentId, record))}
      />
    </FluentProvider>
  );
}

const putCalls = () => mockFetch.mock.calls.filter(([, init]) => (init as RequestInit | undefined)?.method === 'PUT');

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  mockFetch.mockResolvedValue(fake(true, 200, { data: {} }));
  delete process.env.ORG_URL;
});

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
});

describe('SaveFlow — a document already in Spaarke (task 111)', () => {
  it('filed: the green box and the "Filed to" card name the record; no Open Document, no picker', () => {
    process.env.ORG_URL = ORG_URL;
    render(<Pane initial={FILED} canOpenRecord />);

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    // The box's own line: "Filed to <record>." (the card's title is the bare words "Filed to").
    expect(screen.getAllByText('Gamma Merger').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Filed to', { selector: 'span' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'View Document' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open Document' })).not.toBeInTheDocument();
    expect(screen.queryByTestId('related-to-picker')).toBeNull();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
  });

  it('known unfiled: "Not filed to a record yet." + the picker and File to record; filing sends ONE lookup, then shows the record', async () => {
    render(<Pane initial={UNFILED} />);

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByText('Not filed to a record yet.')).toBeInTheDocument();
    expect(screen.getByTestId('related-to-picker')).toBeTruthy();
    const fileButton = screen.getByRole('button', { name: 'File to record' });
    expect((fileButton as HTMLButtonElement).disabled).toBe(true);

    fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
    expect((screen.getByRole('button', { name: 'File to record' }) as HTMLButtonElement).disabled).toBe(false);
    fireEvent.click(screen.getByRole('button', { name: 'File to record' }));

    await waitFor(() => expect(screen.getAllByText('Acme v. Beta').length).toBeGreaterThanOrEqual(2));
    const puts = putCalls();
    expect(puts).toHaveLength(1);
    const [url, init] = puts[0]!;
    expect(url).toBe(`https://bff/api/v1/documents/${DOCUMENT_ID}`);
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({
      matterLookup: 'aaaaaaaa-0000-0000-0000-00000000000a',
    });
    // The box and card now show the record; the picker is gone; no reload.
    expect(screen.queryByText('Not filed to a record yet.')).toBeNull();
    expect(screen.queryByTestId('related-to-picker')).toBeNull();
    expect(screen.queryByRole('button', { name: 'File to record' })).toBeNull();
    expect(screen.getAllByText('Acme v. Beta').length).toBeGreaterThanOrEqual(2);
  });

  it('a Project pick sends projectLookup alone', async () => {
    render(<Pane initial={UNFILED} />);

    fireEvent.click(screen.getByRole('button', { name: 'Pick project' }));
    fireEvent.click(screen.getByRole('button', { name: 'File to record' }));

    await waitFor(() => expect(putCalls()).toHaveLength(1));
    expect(JSON.parse(String((putCalls()[0]![1] as RequestInit).body))).toEqual({
      projectLookup: 'bbbbbbbb-0000-0000-0000-00000000000b',
    });
  });

  it('a 403 shows a clear message and keeps the picker (nothing is shown as filed)', async () => {
    mockFetch.mockResolvedValue(fake(false, 403, { type: 'x', title: 'Forbidden', status: 403 }));
    render(<Pane initial={UNFILED} />);

    fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
    fireEvent.click(screen.getByRole('button', { name: 'File to record' }));

    expect(await screen.findByText(/don't have permission to file this document/i)).toBeInTheDocument();
    expect(screen.getByTestId('related-to-picker')).toBeTruthy();
    expect(screen.getByText('Not filed to a record yet.')).toBeInTheDocument();
  });

  it('another failure shows the server message and keeps the picker', async () => {
    mockFetch.mockResolvedValue(
      fake(false, 409, { type: 'x', title: 'Conflict', status: 409, detail: 'The record owner could not be resolved.' })
    );
    render(<Pane initial={UNFILED} />);

    fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
    fireEvent.click(screen.getByRole('button', { name: 'File to record' }));

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(screen.getByTestId('related-to-picker')).toBeTruthy();
  });

  it('unknown record (stamp-only): saved-to-Spaarke box with a neutral line — NO picker, NO "not filed", nothing sent', () => {
    render(<Pane initial={STAMP_ONLY} />);

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.getAllByText('Filing record not available here.').length).toBeGreaterThanOrEqual(1);
    expect(screen.queryByTestId('related-to-picker')).toBeNull();
    expect(screen.queryByRole('button', { name: 'File to record' })).toBeNull();
    expect(screen.queryByText(/not filed to a record/i)).toBeNull();
    expect(screen.queryByText(/not filed/i)).toBeNull();
    expect(putCalls()).toHaveLength(0);
  });

  it('a fresh unsaved document is unchanged: no green box, the plain create form with its picker', () => {
    render(<Pane initial={{ kind: 'new', reason: 'not_cloud_document' }} />);

    expect(screen.queryByText('Saved to Spaarke')).toBeNull();
    expect(screen.getByTestId('related-to-picker')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'File to record' })).toBeNull();
  });

  it('"Save as new document" leaves the box (the pane is composing a different document)', () => {
    render(<Pane initial={FILED} />);

    fireEvent.click(screen.getByRole('button', { name: 'Save as new document' }));

    expect(screen.queryByText('Saved to Spaarke')).toBeNull();
  });

  describe('Copy Link', () => {
    it('is hidden without ORG_URL for the record link', () => {
      render(<Pane initial={FILED} />);

      expect(screen.queryByRole('button', { name: 'Copy Link' })).toBeNull();
    });

    it("copies the document's Spaarke record link", async () => {
      process.env.ORG_URL = ORG_URL;
      const writeText = jest.fn().mockResolvedValue(undefined);
      Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
      render(<Pane initial={FILED} />);

      fireEvent.click(screen.getByRole('button', { name: 'Copy Link' }));

      await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
      const copied = String(writeText.mock.calls[0]![0]);
      expect(copied.startsWith(`${ORG_URL}/main.aspx?`)).toBe(true);
      expect(copied).toContain('etn=sprk_document');
      expect(copied).toContain(`id=${DOCUMENT_ID}`);
    });
  });
});
