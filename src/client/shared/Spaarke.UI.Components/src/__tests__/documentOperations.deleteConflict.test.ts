/**
 * Task 188 slice D (unified-access-control-r2, owner round 111 item 3) — `sprk_DocumentOperations.js` 1.28.2: the
 * model-driven app's Delete command no longer reads every 409 as "checked out".
 *
 * The BFF now refuses to delete a document that other documents name as their parent (an .eml and its attachments)
 * with 409 `reasonCode: document_has_child_documents` and a `childCount`. The dialog says how many attached documents to
 * delete first; "checked out" is kept for the checkout body (`error: document_locked`); any other 409 is generic.
 *
 * Runs the REAL script the way the form does (injected into the jsdom window), as accessRibbon.*.test.ts do; only
 * `Xrm`, `fetch` and the script's own init / token seams are replaced.
 */
import * as fs from 'fs';
import * as path from 'path';

const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_DocumentOperations.js');
const SOLUTION_COPY = path.resolve(
  CLIENT_ROOT,
  '../dataverse/solutions/SpaarkeMaster/WebResources/sprk_DocumentOperations.js'
);

const DOCUMENT_ID = 'aaaaaaaa-1111-2222-3333-444444444444';

type Win = typeof window & { Spaarke?: any; Xrm?: any; fetch?: any };
const win = window as Win;

function load() {
  // The script's top-level `var Spaarke` makes the global non-configurable (cannot be deleted): reset it instead.
  win.Spaarke = {};
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(SCRIPT, 'utf8');
  document.head.appendChild(script);
  expect(win.Spaarke.Document.Config.version).toBe('1.28.2'); // the real script ran
  return win.Spaarke.Document;
}

function xrm() {
  return {
    Navigation: {
      openConfirmDialog: jest.fn().mockResolvedValue({ confirmed: true }),
      openErrorDialog: jest.fn().mockResolvedValue(undefined),
      openAlertDialog: jest.fn().mockResolvedValue(undefined),
      navigateTo: jest.fn(),
    },
    Utility: { showProgressIndicator: jest.fn(), closeProgressIndicator: jest.fn() },
  };
}

function formContext() {
  return {
    data: { entity: { getId: () => `{${DOCUMENT_ID.toUpperCase()}}` } },
    getAttribute: (name: string) => (name === 'sprk_documentname' ? { getValue: () => 'Re: brief.eml' } : null),
  };
}

/** Runs the Delete command against a BFF that answers `status` with `body`; returns the Xrm double. */
async function deleteAnswered(status: number, body: unknown) {
  const ops = load();
  ops.ensureInitialized = jest.fn().mockResolvedValue(undefined);
  ops.getAccessToken = jest.fn().mockResolvedValue('test-token');
  ops.Config.bffApiUrl = 'https://bff.test';
  const x = xrm();
  win.Xrm = x;
  win.fetch = jest.fn().mockResolvedValue({ ok: false, status, json: () => Promise.resolve(body) });

  await ops.deleteDocument(formContext());

  expect(win.fetch).toHaveBeenCalledWith(
    `https://bff.test/api/documents/${DOCUMENT_ID.toUpperCase()}`,
    expect.objectContaining({ method: 'DELETE' })
  );
  return x;
}

function shownMessage(x: ReturnType<typeof xrm>): string {
  expect(x.Navigation.openErrorDialog).toHaveBeenCalledTimes(1);
  return x.Navigation.openErrorDialog.mock.calls[0][0].message as string;
}

let consoleSpies: jest.SpyInstance[];

beforeEach(() => {
  consoleSpies = (['log', 'warn', 'error'] as const).map(m => jest.spyOn(console, m).mockImplementation(() => {}));
});

afterEach(() => {
  consoleSpies.forEach(s => s.mockRestore());
  delete win.fetch;
});

describe('sprk_DocumentOperations.js 1.28.2 — Delete 409 message', () => {
  it('a parent with attached documents says how many to delete first, never "checked out"', async () => {
    const x = await deleteAnswered(409, {
      title: 'Document has child documents',
      status: 409,
      reasonCode: 'document_has_child_documents',
      childCount: 3,
    });

    const message = shownMessage(x);
    expect(message).toBe('This document has 3 attached documents. Delete them first, then delete this document.');
    expect(message).not.toMatch(/checked out/i);
    expect(x.Navigation.navigateTo).not.toHaveBeenCalled(); // the document still exists
  });

  it('the checkout body keeps the checked-out message with the holder', async () => {
    const x = await deleteAnswered(409, {
      error: 'document_locked',
      detail: 'Cannot delete document. It is checked out by Paula Partner.',
      checkedOutBy: { id: 'u1', name: 'Paula Partner' },
    });

    expect(shownMessage(x)).toBe(
      'This document is currently checked out and cannot be deleted.\n\nChecked out by: Paula Partner'
    );
  });

  it('any other 409 is generic and carries the server detail, not "checked out"', async () => {
    const x = await deleteAnswered(409, { title: 'Conflict', detail: 'Something else is in the way.' });

    const message = shownMessage(x);
    expect(message).toBe(
      'This document could not be deleted because it conflicts with its current state.\n\nSomething else is in the way.'
    );
    expect(message).not.toMatch(/checked out/i);
  });

  it.each([
    [
      { reasonCode: 'document_has_child_documents', childCount: 1 },
      'This document has 1 attached document. Delete it first, then delete this document.',
    ],
    [
      { reasonCode: 'document_has_child_documents' },
      'This document has attached documents. Delete them first, then delete this document.',
    ],
    [
      { error: 'document_locked' },
      'This document is currently checked out and cannot be deleted.\n\nPlease wait for it to be checked in.',
    ],
    [
      { reasonCode: 'document_locked' },
      'This document is currently checked out and cannot be deleted.\n\nPlease wait for it to be checked in.',
    ],
    [{}, 'This document could not be deleted because it conflicts with its current state.'],
    [null, 'This document could not be deleted because it conflicts with its current state.'],
  ])('getDeleteConflictMessage(%j)', (body, expected) => {
    expect(load().Utils.getDeleteConflictMessage(body)).toBe(expected);
  });

  it('the solution copy is the same script', () => {
    expect(fs.readFileSync(SOLUTION_COPY, 'utf8')).toBe(fs.readFileSync(SCRIPT, 'utf8'));
  });
});
