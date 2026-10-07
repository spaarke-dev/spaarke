/**
 * Task 088 (spaarkeai-word-add-in-r1, UAT round 3 — UAT-1/6/7; owner 2026-10-03 "Keep the form"): after a save
 * the Save tab STAYS on the form instead of replacing it with a success card. Rewritten for task 094 (the name
 * lock; the content-change re-enable) and for task 099 (owner, UAT round 5, item 6 + decision A, 2026-10-05):
 *
 * - the confirmation bar names the record the save was filed to, or says it was filed to none, with View
 *   Document + Copy Link; the Profile has a Refresh button (not Generate Profile); the footer is GONE — no
 *   Cancel, no Open Document, no Save, no gray "Saved";
 * - no Save until the document is edited: with content-change detection supported, Save (= a new version of
 *   this document) appears on a content-change event (simulated via the lifted `savedState` — the real
 *   subscription lives in `SaveView`);
 * - WITHOUT content-change detection the host cannot know, so Save is offered immediately — the owner's binding
 *   "never block a save" rule (the one place decision A yields);
 * - the button NEVER reads "Save version" anywhere in the pane;
 * - once a document is in Spaarke, its name shows LOCKED in the Document header (read-only, with a hint);
 *   "Save as new document" is the only way to rename;
 * - Refresh re-reads the document's identity and updates the name and the filed-to record the pane shows;
 * - View Document and the duplicate card's View Existing Document open the Spaarke document RECORD
 *   (`appname=` from SPAARKE_APP_NAME; none when it is empty) — never the Graph webUrl;
 * - an Outlook save (no document bytes) reaches the same saved state: read-only name, and no Save at all.
 *
 * Real SaveFlow + real useSaveFlow + the REAL openRecordLauncher (the opened URL is asserted through the
 * global `Office.context.ui.openBrowserWindow` mock from jest.setup.js). Only fetch, SSE, the Profile section
 * and the Related-to picker are doubled.
 */
import React from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow, DEFAULT_SAVED_DOCUMENT_PANE_STATE, type SavedDocumentPaneState } from '../SaveFlow';
import type { DocumentIdentityState } from '../../services/documentIdentityService';

// The Profile section is replaced by a stub exposing the seams SaveFlow uses: `mode` and `onRefresh` (task 099).
jest.mock('../DocumentProfileSection', () => {
  const ReactModule = jest.requireActual('react');
  return {
    DocumentProfileSection: (props: { documentId?: string; mode?: string; onRefresh?: () => void }) =>
      ReactModule.createElement(
        'button',
        {
          type: 'button',
          'data-document-id': props.documentId ?? '',
          'data-mode': props.mode ?? 'generate',
          onClick: () => props.onRefresh?.(),
        },
        'Stub: profile'
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
        // Task 094: this suite's default host CAN detect content changes (the owner's "Re-enable on
        // document edits" scenario) — AC4's "unsupported" case gets its own describe block below with
        // this explicitly turned off.
        canDetectDocumentChanges
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
  await screen.findByText(/saved to Spaarke/i);
}

/** Task 099 (owner item 6 + decision A): the post-save footer is gone — none of its buttons exist. */
function expectNoPostSaveFooter(): void {
  expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Open Document' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  expect(screen.queryByRole('button', { name: 'Saved' })).not.toBeInTheDocument();
}

const recordUrl = (id: string, appName = 'sprk_MatterManagement') =>
  `${ORG}/main.aspx?${appName ? `appname=${appName}&` : ''}etn=sprk_document&id=${id}&pagetype=entityrecord&navbar=off`;

describe('SaveFlow — the saved state keeps the form (task 088)', () => {
  it('AC1: after a create filed to a record — the bar names it, with View Document + Copy Link; the Profile has Refresh; there is no footer', async () => {
    fileToMatter();
    renderWord();

    await saveAndWaitForSavedState();

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'View Document' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeEnabled();
    // The form stays: the name and the profile of the saved document — no success card, no "Save Another".
    expect(screen.getByText('Brief')).toBeInTheDocument();
    const profile = screen.getByRole('button', { name: 'Stub: profile' });
    expect(profile).toHaveAttribute('data-document-id', SAVED_ID);
    expect(profile).toHaveAttribute('data-mode', 'refresh');
    expect(screen.queryByRole('button', { name: 'Save Another' })).not.toBeInTheDocument();
    // No "Document Details" header or name card any more — the name lives in the Document header.
    expect(screen.queryByText('Document Details')).not.toBeInTheDocument();
    expect(screen.getByText('Document')).toBeInTheDocument();

    // Owner decision A: no Save until the document is edited — and no Cancel / Open Document at all.
    expectNoPostSaveFooter();
  });

  it('focus moves to the confirmation when a save completes (the Save button the user pressed is gone)', async () => {
    fileToMatter();
    renderWord();

    await saveAndWaitForSavedState();

    const active = document.activeElement as HTMLElement;
    expect(active.getAttribute('tabindex')).toBe('-1');
    expect(within(active).getByText('Saved to Spaarke')).toBeInTheDocument();
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

    // Task 094: the document is already in Spaarke — pre-save, the name box is LOCKED (Spaarke's own
    // name, "Brief"), never an editable field, and the button never says "Save version".
    expect(screen.queryByRole('textbox', { name: 'Document name' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save version' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('New version saved to Spaarke');

    expect(sentBody(0).document).toMatchObject({ existingDocumentId: SAVED_ID, isNewVersion: true });
    expect(screen.getByText('New version saved to Spaarke')).toBeInTheDocument();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument();
    // The locked name shown after the version save is the record's OWN name, not editable.
    expect(screen.getByText('Brief')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Save version' })).not.toBeInTheDocument();
  });

  it('AC1/AC2: once saved, the name box is LOCKED with a hint and "Save as new document" — never editable, never re-enabled by an edit', async () => {
    fileToMatter();
    renderWord();
    await saveAndWaitForSavedState();
    expect(sentBody(0).document.existingDocumentId).toBeUndefined(); // the first save was a create

    // No pencil, no input — editing the name is no longer possible from the saved state.
    expect(screen.queryByRole('button', { name: 'Edit document name' })).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Document name' })).not.toBeInTheDocument();
    expect(screen.getByText(/This document.s name is set in Spaarke/)).toBeInTheDocument();
    // Task 099: no content-change event has fired yet — there is no Save button at all.
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument();
  });

  it('AC2: "Save as new document" unlocks the name box; saving sends a CREATE (no existingDocumentId)', async () => {
    fileToMatter();
    renderWord();
    await saveAndWaitForSavedState();

    fireEvent.click(screen.getByRole('button', { name: 'Save as new document' }));

    // Unlocked: the plain editable new-document form is back, pencil affordance included.
    fireEvent.click(screen.getByRole('button', { name: 'Edit document name' }));
    const name = screen.getByRole('textbox', { name: 'Document name' });
    fireEvent.change(name, { target: { value: 'Brief — copy' } });
    fireEvent.keyDown(name, { key: 'Enter' });

    saveResponses.push(accepted());
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(sentBodies()).toHaveLength(2));
    const created = sentBody(1);
    expect(created.document).not.toHaveProperty('existingDocumentId');
    expect(created.document).not.toHaveProperty('isNewVersion');
    expect(created.document.title).toBe('Brief — copy');
  });

  // Task 094 (AC3): the real content-change SUBSCRIPTION lives in `SaveView` (covered by
  // `WordAdapter.documentChangeDetection.test.ts` + `SaveView.documentChangeDetection.test.tsx`).
  // SaveFlow's own responsibility — pinned here directly via the lifted, controlled `savedState` — is
  // just reading `contentChangedSinceSave` correctly once it is set.
  it('AC3 (decision A): with content-change detection supported, contentChangedSinceSave=true makes an enabled "Save" appear — a new version, alone (no Cancel / Open Document)', async () => {
    const savedState: SavedDocumentPaneState = {
      savedDocument: { documentId: SAVED_ID, savedName: 'Brief', filedTo: 'Gamma Merger', lastSave: 'create' },
      profileRefreshSignal: 0,
      contentChangedSinceSave: true,
    };
    renderWord({ savedState, onSavedStateChange: jest.fn(), canDetectDocumentChanges: true });

    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Saved' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open Document' })).not.toBeInTheDocument();
  });

  it('AC3 (decision A): with content-change detection supported, contentChangedSinceSave=false shows NO Save', async () => {
    const savedState: SavedDocumentPaneState = {
      savedDocument: { documentId: SAVED_ID, savedName: 'Brief', filedTo: 'Gamma Merger', lastSave: 'create' },
      profileRefreshSignal: 0,
      contentChangedSinceSave: false,
    };
    renderWord({ savedState, onSavedStateChange: jest.fn(), canDetectDocumentChanges: true });

    expectNoPostSaveFooter();
  });

  it('AC4: WITHOUT content-change detection, an enabled "Save" is offered immediately after saving (never block a save)', async () => {
    fileToMatter();
    renderWord({ canDetectDocumentChanges: false });

    saveResponses.push(accepted());
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(sentBodies()).toHaveLength(1));
    // The owner's binding "never block a save" rule: with detection unsupported the pane cannot know the
    // document changed, so Save is not hidden.
    expect(await screen.findByRole('button', { name: 'Save' })).toBeEnabled();
    expect(screen.queryByRole('button', { name: 'Saved' })).not.toBeInTheDocument();
  });

  it('AC5: Refresh re-reads the document identity and updates the name and the record the pane shows', async () => {
    fileToMatter();
    const onRetryDocumentIdentity = jest.fn();
    const view = renderWord({ onRetryDocumentIdentity });
    await saveAndWaitForSavedState();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Stub: profile' }));
    expect(onRetryDocumentIdentity).toHaveBeenCalledTimes(1);

    // The identity re-read settles on THIS document with a (newer) name and filing.
    const identity: DocumentIdentityState = {
      kind: 'resolved',
      documentId: SAVED_ID,
      documentName: 'Brief (final)',
      fileName: 'Brief.docx',
      relatedRecord: { entityType: 'sprk_matter', id: 'dddd', name: 'PAT-9', displayName: 'Delta Co', number: 'PAT-9' },
    };
    view.rerender(
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
          onRetryDocumentIdentity={onRetryDocumentIdentity}
          documentIdentity={identity}
        />
      </FluentProvider>
    );

    await screen.findByText('Delta Co');
    expect(screen.getByText('Brief (final)')).toBeInTheDocument();
    expect(screen.queryByText('Gamma Merger')).not.toBeInTheDocument();
  });

  it('a refused version save (OFFICE_009) still offers "Save as new document", which leaves the saved state for a create', async () => {
    // Detection off so Save is offered right after the first save (the version save is what gets refused).
    renderWord({ canDetectDocumentChanges: false });
    await saveAndWaitForSavedState();
    saveResponses.push(
      json(false, 403, {
        type: 'https://spaarke.com/errors/office/forbidden',
        title: 'Access Denied',
        status: 403,
        detail: 'You do not have permission to write this document file. Nothing was saved.',
        errorCode: 'OFFICE_009',
      })
    );

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    // Exactly one "Save as new document" button: the saved-state name box suppresses its own copy
    // while the error banner is already offering the same action (production rule, not a test artifact).
    fireEvent.click(await screen.findByRole('button', { name: 'Save as new document' }));

    expect(screen.queryByText('Saved to Spaarke')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    expect(sentBodies()).toHaveLength(2); // nothing sent by the switch itself
  });

  // Task 094: the lifted saved-state bundle survives a Save-tab remount (switching to To Do/Find and
  // back) — simulated here with a REAL React owner (so the controlled prop actually re-renders on each
  // update, exactly like `App.tsx`'s `useState`): save while mounted, capture the settled bundle, unmount
  // (the tab switch away), then mount a FRESH `SaveFlow` with that same captured bundle (the tab switch
  // back) and confirm the saved state is there with no second save.
  it('the saved-state bundle survives a remount when the caller (App.tsx) holds it across the switch', async () => {
    let latest: SavedDocumentPaneState = DEFAULT_SAVED_DOCUMENT_PANE_STATE;
    function Owner(): React.ReactElement {
      const [savedState, setSavedState] = React.useState<SavedDocumentPaneState>(DEFAULT_SAVED_DOCUMENT_PANE_STATE);
      latest = savedState;
      return (
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
            savedState={savedState}
            onSavedStateChange={setSavedState}
          />
        </FluentProvider>
      );
    }

    fileToMatter();
    const { unmount } = render(<Owner />);
    saveResponses.push(accepted());
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('Saved to Spaarke');
    expect(latest.savedDocument).not.toBeNull();
    const settled = latest;

    // Simulate switching to Find/To Do (unmount the Save tab) and back (remount with the SAME bundle —
    // App.tsx's own `useState` is what actually persists it; this re-render is the proof it was handed
    // through unchanged).
    unmount();
    renderWord({ savedState: settled, onSavedStateChange: jest.fn() });

    expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument();
    expectNoPostSaveFooter();
    expect(sentBodies()).toHaveLength(1); // no second save happened on remount
  });

  it('AC3: View Document opens the Spaarke document RECORD in the Spaarke app — never the file URL', async () => {
    const windowOpen = jest.spyOn(window, 'open').mockImplementation(() => null);
    try {
      renderWord();
      await saveAndWaitForSavedState();

      fireEvent.click(screen.getByRole('button', { name: 'View Document' }));

      expect(openBrowserWindow().mock.calls).toEqual([[recordUrl(SAVED_ID)]]);
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
  ])('AC4: when %s, View Document is not rendered (not rendered-and-disabled)', async (_case, props, orgUrl) => {
    process.env.ORG_URL = orgUrl;
    renderWord(props);
    await saveAndWaitForSavedState();

    expect(screen.queryByRole('button', { name: 'View Document' })).not.toBeInTheDocument();
    // The rest of the saved state is unaffected.
    expect(screen.getByRole('button', { name: 'Copy Link' })).toBeInTheDocument();
  });

  it('AC4: the duplicate card hides View Existing Document when the host cannot open a browser window', async () => {
    saveResponses.push(json(true, 200, { duplicate: true, documentId: SAVED_ID, message: 'Already saved.' }));
    renderWord({ canOpenRecord: false });

    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByText('Document Already Saved');
    expect(screen.queryByRole('button', { name: 'View Existing Document' })).not.toBeInTheDocument();
  });

  it('Outlook (no document bytes) reaches the same saved state: read-only name, and no Save / Cancel / Open Document at all', async () => {
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
    // Nothing to version: no editable name, and nothing to re-save.
    expect(screen.queryByRole('textbox', { name: 'Document name' })).not.toBeInTheDocument();
    expectNoPostSaveFooter();
    expect(screen.queryByRole('button', { name: 'Save version' })).not.toBeInTheDocument();
  });
});
