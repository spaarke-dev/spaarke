/**
 * Task 138 (#2209): a re-save that adds attachments to an email already saved to the record says so — "Adding N
 * attachments to the saved email. If they don't appear ... save the email again." — in place of "Saved to Spaarke". It
 * never says "Added": the documents are made by a background job after the answer. Any other answer keeps "Saved to Spaarke".
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE and the Profile / Related-to sections are doubled (the harness of
 * SaveFlow.emailFilingNotice.test.tsx), driven as the Outlook pane — the only host that saves emails.
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

const ADDING_2 =
  "Adding 2 attachments to the saved email. If they don't appear on the record in a few minutes, save the email again.";
const ADDING_1 =
  "Adding 1 attachment to the saved email. If it doesn't appear on the record in a few minutes, save the email again.";
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

describe('SaveFlow — attachments added to the saved email (task 138)', () => {
  it('two attachments added: the confirmation says so instead of "Saved to Spaarke"', async () => {
    serverAnswers({ attachmentsAdded: ['Exhibit A.pdf', 'Exhibit B.pdf'] });

    renderAndSave();

    expect(await screen.findByText(ADDING_2)).toBeInTheDocument();
    expect(screen.queryByText(/^Added /)).not.toBeInTheDocument();
    expect(screen.queryByText('Saved to Spaarke')).not.toBeInTheDocument();
  });

  it('one attachment added: the confirmation says "1 attachment"', async () => {
    serverAnswers({ attachmentsAdded: ['Exhibit A.pdf'] });

    renderAndSave();

    expect(await screen.findByText(ADDING_1)).toBeInTheDocument();
  });

  it.each([[undefined], [[]]])('attachmentsAdded %p: the confirmation is unchanged', async value => {
    serverAnswers(value !== undefined ? { attachmentsAdded: value } : {});

    renderAndSave();

    expect(await screen.findByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.queryByText(/to the saved email/)).not.toBeInTheDocument();
  });
});
