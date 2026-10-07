/**
 * Task 105 (spaarkeai-word-add-in-r1, UAT round 7 item 3): View Document and Copy Link visibly confirm the click.
 * Copy Link -> check icon + "Copied" for ~2 s (+ a polite "Link copied" announcement); a clipboard failure shows
 * "Couldn't copy" (+ an assertive announcement). View Document -> "Opened" after a successful open; a failed open
 * shows "Couldn't open". Both revert to their normal label after the timeout.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the Related-to picker are doubled.
 */
import { render, screen, fireEvent, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import * as launcher from '../../services/openRecordLauncher';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => ({ RelatedToPicker: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));
jest.mock('../../services/openRecordLauncher', () => {
  const actual = jest.requireActual('../../services/openRecordLauncher');
  return { ...actual, openRecord: jest.fn(actual.openRecord) };
});

const writeText = jest.fn();
Object.assign(navigator, { clipboard: { writeText } });

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: { digest: jest.fn(async () => new Uint8Array(32).buffer) },
});

const ORG = 'https://contoso.crm.dynamics.com';
const SAVED_ID = 'aaaa1111-0000-4000-8000-000000000088';
const MATTER = {
  id: 'bbbb2222-0000-4000-8000-000000000001',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Gamma Merger',
};
const FILE_URL = 'https://contoso.sharepoint.com/contentstorage/x/Brief.docx';

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
const completed = (documentId: string): FakeResponse =>
  json(true, 200, {
    jobId: 'job-1',
    status: 'Completed',
    completedPhases: [],
    result: { artifact: { type: 'Document', id: documentId, webUrl: FILE_URL } },
  });

let saveResponses: FakeResponse[];
const originalOrgUrl = process.env.ORG_URL;
const openBrowserWindow = (): jest.Mock => Office.context.ui.openBrowserWindow as unknown as jest.Mock;

beforeEach(() => {
  jest.clearAllMocks();
  writeText.mockResolvedValue(undefined);
  sessionStorage.clear();
  process.env.ORG_URL = ORG;
  saveResponses = [];
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const next = saveResponses.shift();
      if (!next) throw new Error('Unexpected save request');
      return next;
    }
    return completed(SAVED_ID);
  });
});

afterEach(() => {
  jest.useRealTimers();
});

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
});

function renderSavedWord() {
  sessionStorage.setItem('spaarke-last-association', JSON.stringify(MATTER));
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        canProvideDocumentName
        canOpenRecord
        canDetectDocumentChanges
        captureDocumentContent={jest.fn().mockResolvedValue('UEsDBBQ=')}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

async function saveAndWaitForSavedState(): Promise<void> {
  renderSavedWord();
  saveResponses.push(accepted());
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await screen.findByText(/saved to Spaarke/i);
}

describe('SaveFlow — post-save button feedback (task 105)', () => {
  it('Copy Link: shows "Copied" with a check, announces it, then reverts after ~2 s', async () => {
    await saveAndWaitForSavedState();

    jest.useFakeTimers();
    fireEvent.click(screen.getByRole('button', { name: 'Copy Link' }));
    await act(async () => {
      await Promise.resolve();
    });

    expect(writeText).toHaveBeenCalledWith(FILE_URL);
    expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copy Link' })).not.toBeInTheDocument();
    // The live region writes on the next animation frame (useAnnounce).
    act(() => {
      jest.advanceTimersByTime(20);
    });
    expect(screen.getByText('Link copied')).toBeInTheDocument();

    act(() => {
      jest.advanceTimersByTime(1860);
    });
    expect(screen.getByRole('button', { name: 'Copied' })).toBeInTheDocument();
    act(() => {
      jest.advanceTimersByTime(200);
    });
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeInTheDocument();
  });

  it('Copy Link: a clipboard failure shows "Couldn\'t copy" and announces the failure', async () => {
    await saveAndWaitForSavedState();
    writeText.mockRejectedValue(new Error('denied'));

    jest.useFakeTimers();
    fireEvent.click(screen.getByRole('button', { name: 'Copy Link' }));
    await act(async () => {
      await Promise.resolve();
    });

    expect(screen.getByRole('button', { name: "Couldn't copy" })).toBeInTheDocument();
    act(() => {
      jest.advanceTimersByTime(20);
    });
    expect(screen.getByText('Failed to copy link')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copied' })).not.toBeInTheDocument();
    act(() => {
      jest.advanceTimersByTime(2100);
    });
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeInTheDocument();
  });

  it('View Document: opens the record, shows "Opened", then reverts', async () => {
    await saveAndWaitForSavedState();

    jest.useFakeTimers();
    fireEvent.click(screen.getByRole('button', { name: 'View Document' }));

    expect(openBrowserWindow()).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('button', { name: 'Opened' })).toBeInTheDocument();
    act(() => {
      jest.advanceTimersByTime(2100);
    });
    expect(screen.getByRole('button', { name: 'View Document' })).toBeInTheDocument();
  });

  it('View Document: a failed open shows "Couldn\'t open", not "Opened"', async () => {
    await saveAndWaitForSavedState();

    (launcher.openRecord as jest.Mock).mockReturnValueOnce({ opened: false, reason: 'no id' });
    fireEvent.click(screen.getByRole('button', { name: 'View Document' }));

    expect(screen.getByRole('button', { name: "Couldn't open" })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Opened' })).not.toBeInTheDocument();
  });
});
