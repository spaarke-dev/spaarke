/**
 * Task 117 (spaarkeai-word-add-in-r1, owner UAT round 12 O3/W1, 2026-10-08): Copy Link copies the document's Spaarke
 * RECORD link (`sprk_document`, built with `openRecordLauncher.buildOpenRecordUrl` from ORG_URL + SPAARKE_APP_NAME) in
 * BOTH hosts — never the stored file's SPE `webUrl`, which for a saved .eml is the placeholder
 * `https://aka.ms/spe-openfilelocation`. Without ORG_URL, Copy Link is not rendered (no file-URL fallback).
 *
 * Real SaveFlow + real useSaveFlow + the real openRecordLauncher; only fetch, SSE, the Profile section and the
 * Related-to picker are doubled.
 */
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => ({ RelatedToPicker: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: { digest: jest.fn(async () => new Uint8Array(32).buffer) },
});

const ORG = 'https://contoso.crm.dynamics.com';
const APP = 'sprk_MatterManagement';
const SAVED_ID = 'aaaa1111-0000-4000-8000-000000000117';
const MATTER = {
  id: 'bbbb2222-0000-4000-8000-000000000001',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Gamma Merger',
};
/** What SPE returns as the webUrl of a saved .eml — the owner's O3 report. */
const EML_PLACEHOLDER_URL = 'https://aka.ms/spe-openfilelocation';
const DOCX_FILE_URL = 'https://contoso.sharepoint.com/contentstorage/x/Brief.docx';
const RECORD_URL = `${ORG}/main.aspx?appname=${APP}&etn=sprk_document&id=${SAVED_ID}&pagetype=entityrecord&navbar=off`;

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

let fileWebUrl: string;
let saveResponses: FakeResponse[];
const writeText = jest.fn();
const originalOrgUrl = process.env.ORG_URL;
const originalAppName = process.env.SPAARKE_APP_NAME;

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  sessionStorage.setItem('spaarke-last-association', JSON.stringify(MATTER));
  process.env.ORG_URL = ORG;
  process.env.SPAARKE_APP_NAME = APP;
  writeText.mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
  saveResponses = [];
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      const next = saveResponses.shift();
      if (!next) throw new Error('Unexpected save request');
      return next;
    }
    return json(true, 200, {
      jobId: 'job-1',
      status: 'Completed',
      completedPhases: [],
      result: { artifact: { type: 'Document', id: SAVED_ID, webUrl: fileWebUrl } },
    });
  });
});

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
  if (originalAppName === undefined) delete process.env.SPAARKE_APP_NAME;
  else process.env.SPAARKE_APP_NAME = originalAppName;
});

function renderOutlook() {
  fileWebUrl = EML_PLACEHOLDER_URL;
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="outlook"
        itemId="<msg-117@contoso.com>"
        itemName="Re: Filing"
        canOpenRecord
        senderEmail="counsel@contoso.com"
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
      />
    </FluentProvider>
  );
}

function renderWord() {
  fileWebUrl = DOCX_FILE_URL;
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

async function save(): Promise<void> {
  saveResponses.push(accepted());
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await screen.findByText('Saved to Spaarke');
}

describe('SaveFlow — Copy Link copies the Spaarke record link (task 117)', () => {
  it.each([
    ['Outlook (a saved .eml, whose SPE webUrl is the aka.ms placeholder)', renderOutlook, EML_PLACEHOLDER_URL],
    ['Word', renderWord, DOCX_FILE_URL],
  ])('%s: copies the sprk_document record link, not the file URL', async (_host, renderHost, fileUrl) => {
    renderHost();
    await save();

    fireEvent.click(screen.getByRole('button', { name: 'Copy Link' }));

    await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
    expect(writeText).toHaveBeenCalledWith(RECORD_URL);
    expect(writeText).not.toHaveBeenCalledWith(fileUrl);
  });

  it.each([
    ['Outlook', renderOutlook],
    ['Word', renderWord],
  ])('%s: without ORG_URL, Copy Link is not rendered (no fallback to the file URL)', async (_host, renderHost) => {
    delete process.env.ORG_URL;
    renderHost();
    await save();

    expect(screen.queryByRole('button', { name: 'Copy Link' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Copied' })).not.toBeInTheDocument();
    expect(writeText).not.toHaveBeenCalled();
  });
});
