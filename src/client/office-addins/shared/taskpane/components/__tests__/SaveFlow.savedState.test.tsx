/**
 * Task 088 (spaarkeai-word-add-in-r1, UAT round 3 — UAT-1/6/7; owner 2026-10-03 "Keep the form"): after a save
 * the Save tab STAYS on the form instead of replacing it with a success card.
 *
 * In scope (the POML's closed acceptance set, AC1–AC4, plus the Outlook "same state, nothing Word-only" rule):
 * - the confirmation bar names the record the save was filed to, or says it was filed to none, with View
 *   Document + Copy Link; the footer shows Open Document immediately left of a gray, disabled "Saved";
 * - an edited name or an accepted Generate Profile turns "Saved" into "Save version", which sends a VERSION of
 *   the saved document (existingDocumentId + isNewVersion); reverting the name returns to "Saved";
 * - View Document, Open Document and the duplicate card's View Existing Document open the Spaarke document
 *   RECORD (`appname=` from SPAARKE_APP_NAME; none when it is empty) — never the Graph webUrl;
 * - without `canOpenRecord` or without ORG_URL those buttons are not rendered at all;
 * - an Outlook save (no document bytes) reaches the same saved state, whose name is read-only and whose button
 *   stays "Saved"; Cancel returns to an empty form (what "Save Another" did).
 *
 * Real SaveFlow + real useSaveFlow + the REAL openRecordLauncher (the opened URL is asserted through the
 * global `Office.context.ui.openBrowserWindow` mock from jest.setup.js). Only fetch, SSE, the Profile section
 * and the Related-to picker are doubled.
 */
import React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import type { DocumentIdentityState } from '../../services/documentIdentityService';

// The Profile section is replaced by a stub exposing the one seam SaveFlow uses: onProfileGenerated (task 088).
jest.mock('../DocumentProfileSection', () => {
  const ReactModule = jest.requireActual('react');
  return {
    DocumentProfileSection: (props: { documentId?: string; onProfileGenerated?: () => void }) =>
      ReactModule.createElement(
        'button',
        { type: 'button', 'data-document-id': props.documentId ?? '', onClick: () => props.onProfileGenerated?.() },
        'Stub: profile generated'
      ),
  };
});
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

const ORG = 'https://contoso.crm.dynamics.com';
const SAVED_ID = 'aaaa1111-0000-4000-8000-000000000088';
const MATTER = {
  id: 'bbbb2222-0000-4000-8000-000000000001',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Gamma Merger',
};
const FILE_URL = `https://contoso.sharepoint.com/contentstorage/x/Brief.docx`;

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
const originalAppName = process.env.SPAARKE_APP_NAME;
const openBrowserWindow = (): jest.Mock => Office.context.ui.openBrowserWindow as unknown as jest.Mock;

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  process.env.ORG_URL = ORG;
  process.env.SPAARKE_APP_NAME = 'sprk_MatterManagement';
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

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
  if (originalAppName === undefined) delete process.env.SPAARKE_APP_NAME;
  else process.env.SPAARKE_APP_NAME = originalAppName;
});

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the raw JSON wire bodies, asserted field by field
type WireBody = Record<string, any>;

function sentBodies(): WireBody[] {
  return mockFetch.mock.calls
    .filter(([url]) => String(url).includes('/api/office/save'))
    .map(([, init]) => JSON.parse(String((init as RequestInit).body)) as WireBody);
}

function sentBody(index: number): WireBody {
  const body = sentBodies()[index];
  if (!body) throw new Error(`No save request #${index} was sent`);
  return body;
}

function fileToMatter(): void {
  // useSaveFlow pre-selects the last "Related to" record from sessionStorage — the picker itself is stubbed.
  sessionStorage.setItem('spaarke-last-association', JSON.stringify(MATTER));
}

function renderWord(props: Partial<React.ComponentProps<typeof SaveFlow>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SaveFlow
        hostType="word"
        itemId="https://contoso.sharepoint.com/Brief.docx"
        itemName="Brief.docx"
        canProvideDocumentName
        canOpenRecord
        captureDocumentContent={jest.fn().mockResolvedValue('UEsDBBQ=')}
        getAccessToken={jest.fn().mockResolvedValue('token')}
        apiBaseUrl="https://bff"
        {...props}
      />
    </FluentProvider>
  );
}

async function saveAndWaitForSavedState(): Promise<void> {
  saveResponses.push(accepted());
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(screen.getByRole('button', { name: 'Saved' })).toBeInTheDocument());
}

const recordUrl = (id: string, appName = 'sprk_MatterManagement') =>
  `${ORG}/main.aspx?${appName ? `appname=${appName}&` : ''}etn=sprk_document&id=${id}&pagetype=entityrecord&navbar=off`;

describe('SaveFlow — the saved state keeps the form (task 088)', () => {
  it('AC1: after a create filed to a record — the bar names it, with View Document + Copy Link; the footer shows Open Document immediately left of a disabled "Saved"', async () => {
    fileToMatter();
    renderWord();

    await saveAndWaitForSavedState();

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'View Document' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeEnabled();
    // The form stays: the name and the profile of the saved document — no success card, no "Save Another".
    expect(screen.getByText('Brief')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Stub: profile generated' })).toHaveAttribute(
      'data-document-id',
      SAVED_ID
    );
    expect(screen.queryByRole('button', { name: 'Save Another' })).not.toBeInTheDocument();

    const saved = screen.getByRole('button', { name: 'Saved' });
    expect(saved).toBeDisabled();
    const openDocument = screen.getByRole('button', { name: 'Open Document' });
    expect(openDocument.nextElementSibling).toBe(saved);
  });

  it('AC1: a save filed to no record says so', async () => {
    renderWord();

    await saveAndWaitForSavedState();

    expect(screen.getByText('Not filed to a record.')).toBeInTheDocument();
    expect(sentBody(0)).not.toHaveProperty('targetEntity');
  });

  it('AC1: a VERSION save of a resolved document lands in the same state, naming the record the document is filed to', async () => {
    const identity: DocumentIdentityState = {
      kind: 'resolved',
      documentId: SAVED_ID,
      documentName: 'Brief',
      fileName: 'Brief.docx',
      relatedRecord: {
        entityType: 'sprk_matter',
        id: MATTER.id,
        name: 'PAT-1',
        displayName: 'Gamma Merger',
        number: 'PAT-1',
      },
    };
    renderWord({ documentIdentity: identity, resolvedDocumentId: SAVED_ID });
    saveResponses.push(accepted());

    fireEvent.click(screen.getByRole('button', { name: 'Save version' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Saved' })).toBeDisabled());

    expect(sentBody(0).document).toMatchObject({ existingDocumentId: SAVED_ID, isNewVersion: true });
    expect(screen.getByText('New version saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument();
  });

  it('AC2: an edited name turns "Saved" into "Save version", which sends a version of THE SAVED document; reverting the name returns to "Saved"', async () => {
    fileToMatter();
    renderWord();
    await saveAndWaitForSavedState();
    expect(sentBody(0).document.existingDocumentId).toBeUndefined(); // the first save was a create

    fireEvent.click(screen.getByRole('button', { name: 'Edit document name' }));
    const name = screen.getByRole('textbox', { name: 'Document name' });
    fireEvent.change(name, { target: { value: 'Brief — final' } });
    expect(screen.getByRole('button', { name: 'Save version' })).toBeEnabled();

    fireEvent.change(name, { target: { value: 'Brief' } });
    expect(screen.getByRole('button', { name: 'Saved' })).toBeDisabled();

    fireEvent.change(name, { target: { value: 'Brief — final' } });
    saveResponses.push(accepted());
    fireEvent.click(screen.getByRole('button', { name: 'Save version' }));

    await waitFor(() => expect(sentBodies()).toHaveLength(2));
    const version = sentBody(1);
    expect(version.document).toMatchObject({
      existingDocumentId: SAVED_ID,
      isNewVersion: true,
      title: 'Brief — final',
    });
    expect(version).not.toHaveProperty('targetEntity'); // a version never re-files the document
    await waitFor(() => expect(screen.getByRole('button', { name: 'Saved' })).toBeDisabled());
    expect(screen.getByText('New version saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument(); // the filing it already had
  });

  it('AC2: an accepted Generate Profile turns "Saved" into "Save version" (a version of the saved document)', async () => {
    renderWord();
    await saveAndWaitForSavedState();

    fireEvent.click(screen.getByRole('button', { name: 'Stub: profile generated' }));
    saveResponses.push(accepted());
    fireEvent.click(screen.getByRole('button', { name: 'Save version' }));

    await waitFor(() => expect(sentBodies()).toHaveLength(2));
    expect(sentBody(1).document).toMatchObject({ existingDocumentId: SAVED_ID, isNewVersion: true });
  });

  it('a refused "Save version" (OFFICE_009) still offers "Save as new document", which leaves the saved state for a create', async () => {
    renderWord();
    await saveAndWaitForSavedState();
    fireEvent.click(screen.getByRole('button', { name: 'Stub: profile generated' }));
    saveResponses.push(
      json(false, 403, {
        type: 'https://spaarke.com/errors/office/forbidden',
        title: 'Access Denied',
        status: 403,
        detail: 'You do not have permission to write this document file. Nothing was saved.',
        errorCode: 'OFFICE_009',
      })
    );

    fireEvent.click(screen.getByRole('button', { name: 'Save version' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Save as new document' }));

    expect(screen.queryByText('Saved to Spaarke')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    expect(sentBodies()).toHaveLength(2); // nothing sent by the switch itself
  });

  it('AC3: View Document and Open Document open the Spaarke document RECORD in the Spaarke app — never the file URL', async () => {
    const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => null);
    try {
      renderWord();
      await saveAndWaitForSavedState();

      fireEvent.click(screen.getByRole('button', { name: 'View Document' }));
      fireEvent.click(screen.getByRole('button', { name: 'Open Document' }));

      expect(openBrowserWindow().mock.calls).toEqual([[recordUrl(SAVED_ID)], [recordUrl(SAVED_ID)]]);
      expect(openBrowserWindow()).not.toHaveBeenCalledWith(FILE_URL);
      expect(windowOpen).not.toHaveBeenCalled();
    } finally {
      windowOpen.mockRestore();
    }
  });

  it('AC3: with SPAARKE_APP_NAME empty, the same record URL WITHOUT appname', async () => {
    process.env.SPAARKE_APP_NAME = '';
    renderWord();
    await saveAndWaitForSavedState();

    fireEvent.click(screen.getByRole('button', { name: 'View Document' }));

    expect(openBrowserWindow()).toHaveBeenCalledWith(recordUrl(SAVED_ID, ''));
  });

  it("AC3: the duplicate card's View Existing Document opens that document's record (it used to pass an id as a URL)", async () => {
    const existing = 'cccc3333-0000-4000-8000-000000000003';
    saveResponses.push(json(true, 200, { duplicate: true, documentId: existing, message: 'Already saved.' }));
    renderWord();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    fireEvent.click(await screen.findByRole('button', { name: 'View Existing Document' }));

    expect(openBrowserWindow()).toHaveBeenCalledWith(recordUrl(existing));
  });

  it.each([
    ['canOpenBrowserWindow is false', { canOpenRecord: false }, ORG],
    ['ORG_URL is empty', {}, ''],
  ])(
    'AC4: when %s, View Document, Open Document and View Existing Document are not rendered (not rendered-and-disabled)',
    async (_case, props, orgUrl) => {
      process.env.ORG_URL = orgUrl;
      renderWord(props);
      await saveAndWaitForSavedState();

      expect(screen.queryByRole('button', { name: 'View Document' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Open Document' })).not.toBeInTheDocument();
      // The rest of the saved state is unaffected.
      expect(screen.getByRole('button', { name: 'Saved' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Copy Link' })).toBeInTheDocument();
    }
  );

  it('AC4: the duplicate card hides View Existing Document when the host cannot open a browser window', async () => {
    saveResponses.push(json(true, 200, { duplicate: true, documentId: SAVED_ID, message: 'Already saved.' }));
    renderWord({ canOpenRecord: false });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByText('Document Already Saved');
    expect(screen.queryByRole('button', { name: 'View Existing Document' })).not.toBeInTheDocument();
  });

  it('Outlook (no document bytes) reaches the same saved state: read-only name, "Saved" stays disabled, Cancel returns to an empty form', async () => {
    fileToMatter();
    render(
      <FluentProvider theme={webLightTheme}>
        <SaveFlow
          hostType="outlook"
          itemId="<msg-1@contoso.com>"
          itemName="Re: Filing"
          canOpenRecord
          senderEmail="counsel@contoso.com"
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
        />
      </FluentProvider>
    );

    await saveAndWaitForSavedState();

    expect(sentBody(0).contentType).toBe('Email');
    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Document' })).toBeInTheDocument();
    // Nothing to version: no editable name, and a re-generated profile leaves the button "Saved".
    expect(screen.queryByRole('textbox', { name: 'Document name' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Stub: profile generated' }));
    expect(screen.getByRole('button', { name: 'Saved' })).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save version' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    expect(screen.queryByText('Saved to Spaarke')).not.toBeInTheDocument();
    expect(within(screen.getByRole('form')).getByTestId('related-to-picker')).toBeInTheDocument();
  });
});
