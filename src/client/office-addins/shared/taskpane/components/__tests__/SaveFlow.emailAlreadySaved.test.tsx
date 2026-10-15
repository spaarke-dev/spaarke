/**
 * Task 120 (spaarkeai-word-add-in-r1, owner UAT round 12 O6): an Outlook email that is ALREADY saved to Spaarke opens
 * the Save tab with the same green "Saved to Spaarke" box a save ends on (task 111's machinery) — and an email has NO
 * version mode.
 *
 * - filed → the box and the "Filed to" card name the record; View Document / Copy Link; NO Save, NO Cancel, NO
 *   attachment picker, NO "Related to" picker — nothing to send;
 * - known unfiled → "Not filed to a record yet." + the record picker and "File to record";
 * - saving again is an explicit, collapsed-by-default action ("Save again as a new document"): it opens the ordinary
 *   email form, and its Save is a CREATE — never a version; "Don't save again" returns to the box;
 * - while the lookup runs, the line names the item as an email.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the picker (a stub) are doubled. No
 * document bytes are supplied — exactly what an Outlook pane has (`canGetDocumentContent` false).
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
import type { AttachmentInfo } from '@shared/adapters/types';

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

const EML_ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';
const ORG_URL = 'https://contoso.crm.dynamics.com';
const originalOrgUrl = process.env.ORG_URL;

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'att-1', name: 'Exhibit A.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
];

const FILED_RECORD: ResolvedRelatedRecord = {
  entityType: 'sprk_matter',
  id: '22222222-2222-2222-2222-222222222222',
  name: 'MAT-000777',
  displayName: 'Gamma Merger',
  number: 'MAT-000777',
};

const savedEmail = (relatedRecord: ResolvedRelatedRecord | null): DocumentIdentityState => ({
  kind: 'resolved',
  documentId: EML_ID,
  documentName: 'RE: Discovery schedule',
  fileName: 'RE_ Discovery schedule_a1b2.eml',
  relatedRecord,
});

type FakeResponse = { ok: boolean; status: number; json: () => Promise<unknown> };
const fake = (ok: boolean, status: number, body: unknown = {}): FakeResponse => ({
  ok,
  status,
  json: async () => body,
});

function OutlookPane({ initial }: { initial: DocumentIdentityState }) {
  const [identity, setIdentity] = useState<DocumentIdentityState | undefined>(initial);
  return (
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="outlook"
        itemId="AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def="
        itemName="RE: Discovery schedule"
        senderEmail="opposing@counsel.example"
        attachments={ATTACHMENTS}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        canOpenRecord
        {...(identity !== undefined ? { documentIdentity: identity } : {})}
        onRetryDocumentIdentity={jest.fn()}
        onDocumentFiled={(documentId, record) => setIdentity(prev => applyFiledRecord(prev, documentId, record))}
      />
    </FluentProvider>
  );
}

const callsTo = (fragment: string, method?: string) =>
  mockFetch.mock.calls.filter(
    ([url, init]) =>
      String(url).includes(fragment) && (method === undefined || (init as RequestInit | undefined)?.method === method)
  );

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  mockFetch.mockResolvedValue(fake(true, 200, { data: {} }));
  process.env.ORG_URL = ORG_URL;
});

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
});

describe('SaveFlow — an Outlook email already in Spaarke (task 120)', () => {
  it('filed: the box and the card name the record; View Document + Copy Link; nothing to send, no version mode', () => {
    render(<OutlookPane initial={savedEmail(FILED_RECORD)} />);

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.getAllByText('Gamma Merger').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByRole('button', { name: 'View Document' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeInTheDocument();
    // Nothing to send: no Save, no Cancel, no attachment choice, no "Related to" picker.
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull();
    expect(screen.queryByText('Exhibit A.pdf')).toBeNull();
    expect(screen.queryByTestId('related-to-picker')).toBeNull();
    // Never a version of an email.
    expect(screen.queryByText(/new version/i)).toBeNull();
    expect(screen.queryByRole('button', { name: 'Keep as version' })).toBeNull();
    // Saving again is offered, collapsed, behind one explicit action.
    expect(screen.getByRole('button', { name: 'Save again as a new document' })).toBeInTheDocument();
  });

  it('known unfiled: "Not filed to a record yet." + the picker; File to record sends ONE lookup for the .eml', async () => {
    render(<OutlookPane initial={savedEmail(null)} />);

    expect(screen.getByText('Not filed to a record yet.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
    fireEvent.click(screen.getByRole('button', { name: 'File to record' }));

    await waitFor(() => expect(callsTo('/api/v1/documents/', 'PUT')).toHaveLength(1));
    const [url, init] = callsTo('/api/v1/documents/', 'PUT')[0]!;
    expect(url).toBe(`https://bff/api/v1/documents/${EML_ID}`);
    expect(JSON.parse(String((init as RequestInit).body))).toEqual({
      matterLookup: 'aaaaaaaa-0000-0000-0000-00000000000a',
    });
    await waitFor(() => expect(screen.queryByText('Not filed to a record yet.')).toBeNull());
  });

  it('"Save again as a new document" opens the email form; its Save is a CREATE, never a version', async () => {
    render(<OutlookPane initial={savedEmail(FILED_RECORD)} />);

    fireEvent.click(screen.getByRole('button', { name: 'Save again as a new document' }));

    expect(screen.queryByText('Saved to Spaarke')).toBeNull();
    expect(screen.getByTestId('related-to-picker')).toBeTruthy();
    expect(screen.getByText('Exhibit A.pdf')).toBeInTheDocument();
    expect(screen.getByText(/store this email in Spaarke again/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(callsTo('/api/office/save', 'POST').length).toBeGreaterThanOrEqual(1));
    const body = String((callsTo('/api/office/save', 'POST')[0]![1] as RequestInit).body);
    expect(JSON.parse(body).email).toBeDefined();
    expect(body).not.toContain('existingDocumentId');
    expect(body).not.toContain('isNewVersion');
  });

  it('"Don\'t save again" returns to the saved box with nothing to send', () => {
    render(<OutlookPane initial={savedEmail(FILED_RECORD)} />);

    fireEvent.click(screen.getByRole('button', { name: 'Save again as a new document' }));
    fireEvent.click(screen.getByRole('button', { name: 'Don’t save again' }));

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
    expect(screen.queryByTestId('related-to-picker')).toBeNull();
  });

  it('while the lookup runs, the line names the item as an email', () => {
    render(<OutlookPane initial={'checking'} />);

    expect(screen.getByText('Checking whether this email is already in Spaarke…')).toBeInTheDocument();
  });
});
