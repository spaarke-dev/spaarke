/**
 * Task 131 (#2171): an Outlook save that worked but could not FILE its email to the chosen record tells the user, beside
 * the "Saved to Spaarke" confirmation. A non-blocking warning (never an error, never naming a record) for `NotPermitted`
 * and `Failed`; nothing extra for `Filed` / `AlreadyFiled` / absent / a value from a newer server.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE and the Profile / Related-to sections are doubled (the harness of
 * SaveFlow.savedState.test.tsx). The notice is host-neutral, so the Word harness drives it.
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

const SAVED_ID = 'aaaa1111-0000-4000-8000-000000000131';
const MATTER = {
  id: 'bbbb2222-0000-4000-8000-000000000131',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Gamma Merger',
};

const NOT_PERMITTED = "Saved. The email wasn't filed to this record because you don't have permission to change it.";
const FAILED = "Saved. The email couldn't be filed to this record. Try again later.";

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
          jobId: 'job-1',
          statusUrl: '/api/office/jobs/job-1',
          streamUrl: '/api/office/jobs/job-1/stream',
          status: 'Queued',
          duplicate: false,
          correlationId: 'corr-1',
          ...saveBody,
        })
      : json(200, {
          jobId: 'job-1',
          status: 'Completed',
          completedPhases: [],
          result: { artifact: { type: 'Document', id: SAVED_ID } },
        })
  );
}

async function saveToTheMatter(): Promise<void> {
  sessionStorage.setItem('spaarke-last-association', JSON.stringify(MATTER));
  render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        canProvideDocumentName
        captureDocumentContent={jest.fn().mockResolvedValue('UEsDBBQ=')}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await screen.findByText(/saved to Spaarke/i);
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('SaveFlow — the email was saved but not filed (task 131)', () => {
  it('NotPermitted: a warning says the email was not filed because the user may not change it', async () => {
    serverAnswers({ emailFiling: 'NotPermitted' });

    await saveToTheMatter();

    expect(await screen.findByText(NOT_PERMITTED)).toBeInTheDocument();
    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument(); // the save itself still shows as done
  });

  it('Failed: a warning says the email could not be filed, try again later', async () => {
    serverAnswers({ emailFiling: 'Failed' });

    await saveToTheMatter();

    expect(await screen.findByText(FAILED)).toBeInTheDocument();
  });

  it('the notice names nothing about the record or why access was refused beyond "permission to change it"', async () => {
    serverAnswers({ emailFiling: 'NotPermitted' });

    await saveToTheMatter();

    const notice = await screen.findByText(NOT_PERMITTED);
    expect(notice.textContent).not.toContain(MATTER.name);
    expect(notice.textContent).not.toMatch(/secure|matter/i);
  });

  it.each([['Filed'], ['AlreadyFiled'], ['SomethingNewer'], [undefined]])(
    'emailFiling %s: no notice (an unknown value is ignored, nothing crashes)',
    async outcome => {
      serverAnswers(outcome !== undefined ? { emailFiling: outcome } : {});

      await saveToTheMatter();

      expect(screen.queryByText(/wasn't filed|couldn't be filed/)).not.toBeInTheDocument();
      expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    }
  );
});
