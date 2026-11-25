/**
 * Task 138 (#2209): a re-save that ticked attachments the STORED email does not hold says so in plain words, on both
 * answers: beside the confirmation of an add (202), and in the already-saved card (200, duplicate). Nothing is claimed as
 * added for them.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE and the Profile / Related-to sections are doubled (the harness of
 * SaveFlow.attachmentsAdded.test.tsx), driven as the Outlook pane — the only host that saves emails.
 */
import { render, screen, fireEvent } from '@testing-library/react';
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

const EML_ID = 'aaaa1111-0000-4000-8000-000000000138';
const MATTER = {
  id: 'bbbb2222-0000-4000-8000-000000000138',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Delta Settlement',
};

type FakeResponse = { ok: boolean; status: number; text: () => Promise<string>; json: () => Promise<unknown> };
const json = (status: number, body: unknown): FakeResponse => ({
  ok: status < 400,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

function serverAnswers(saveBody: Record<string, unknown>) {
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? json(202, {
          jobId: 'job-138',
          statusUrl: '/api/office/jobs/job-138',
          streamUrl: '/api/office/jobs/job-138/stream',
          status: 'Queued',
          duplicate: false,
          correlationId: 'corr-138',
          ...saveBody,
        })
      : json(200, {
          jobId: 'job-138',
          status: 'Completed',
          completedPhases: [],
          result: { artifact: { type: 'Document', id: EML_ID } },
        })
  );
}

function serverSaysAlreadySaved(extra: Record<string, unknown>) {
  mockFetch.mockImplementation(async () => json(200, { duplicate: true, documentId: EML_ID, ...extra }));
}

function renderAndSave(): void {
  sessionStorage.setItem('spaarke-last-association', JSON.stringify(MATTER));
  render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="outlook"
        itemId="<msg-138@contoso.com>"
        itemName="Re: Settlement"
        senderEmail="counsel@contoso.com"
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('SaveFlow — attachments the stored email does not hold (task 138)', () => {
  it('an add that also names one the email lacks: says it is adding, and warns about the other', async () => {
    serverAnswers({ attachmentsAdded: ['Exhibit A.pdf'], attachmentsNotStored: ['Exhibit B.pdf'] });

    renderAndSave();

    expect(await screen.findByText(/^Adding 1 attachment to the saved email\./)).toBeInTheDocument();
    expect(
      await screen.findByText(
        'This attachment could not be added because it was not saved with the email: Exhibit B.pdf. To include it, choose "Save again as a new document", which saves a second copy of the email.'
      )
    ).toBeInTheDocument();
  });

  it('the already-saved card names the attachments the email lacks', async () => {
    serverSaysAlreadySaved({ attachmentsNotStored: ['Exhibit A.pdf', 'Exhibit B.pdf'] });

    renderAndSave();

    expect(
      await screen.findByText(
        /These attachments could not be added because they were not saved with the email: Exhibit A\.pdf, Exhibit B\.pdf\./
      )
    ).toBeInTheDocument();
  });

  it.each([[undefined], [[]]])('attachmentsNotStored %p: no warning', async value => {
    serverAnswers(value !== undefined ? { attachmentsNotStored: value } : {});

    renderAndSave();

    expect(await screen.findByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.queryByText(/could not be added/)).not.toBeInTheDocument();
  });
});
