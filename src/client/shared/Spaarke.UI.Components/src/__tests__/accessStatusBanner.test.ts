/**
 * Task 153 (unified-access-control-r2) - the form banner library `sprk_accessstatus_banner.js`, run the way the form
 * runs it (the REAL script injected into the jsdom window, as noAccessEntryForm.test.ts does); only `Xrm`, the form
 * context and `Spaarke.BffAuth` are replaced.
 *
 * Covers the fail-closed contract (POML constraint "ADR-003 fail closed", criterion 7) and the events (criterion 8):
 *  - each `applies` signal shows its closed-copy ERROR notification with its fixed id; `doesNotApply` shows nothing and
 *    clears a previous notification;
 *  - ANY `unknown`, a missing or unrecognised signal, a 403/404/500, an unparseable body, an answer naming another
 *    record, a missing Spaarke.BffAuth, no BFF URL, a null Response (no token), and a thrown call each show ONLY the
 *    INFO unavailable notice;
 *  - no text says "not secure" / "not restricted"; no count, name or reason is shown even when the body carries entries;
 *  - the route and the id sent; a create form makes no call; a late or superseded answer is dropped;
 *  - OnLoad registers the data OnLoad and OnPostSave handlers once, and each re-evaluates; nothing throws.
 */
import * as fs from 'fs';
import * as path from 'path';

const SCRIPT = path.resolve(__dirname, '../../../../../solutions/webresources/sprk_accessstatus_banner.js');

const RECORD_ID = '1b4e28ba-2fa1-11d2-883f-0016d3cca427';
const OTHER_ID = '2c5f39cb-3fb2-22e3-994f-1127e4ddb538';
const BASE = 'https://bff.example.test';

const TEXT = {
  secure:
    'SECURE RECORD — only people given access explicitly can see this record. People who can manage access see and change who has access in Manage Access (the person icon in the tracking panel).',
  noAccess:
    'NO ACCESS RESTRICTION — named people or organizations are blocked from this record. People who can manage access see the list in Manage Access › No Access (the person icon in the tracking panel).',
  unavailable:
    'Access status unavailable — whether this record is secure or under a No Access restriction could not be checked. Do not assume it is unrestricted; reload the form to try again.',
};

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

function inject(): void {
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(SCRIPT, 'utf8');
  document.head.appendChild(script);
}

const flush = async (): Promise<void> => {
  for (let i = 0; i < 8; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
};

function makeForm(table = 'sprk_matter', id: string | null = `{${RECORD_ID.toUpperCase()}}`) {
  const shown = new Map<string, { text: string; level: string }>();
  const form = {
    id,
    table,
    shown,
    dataOnLoad: [] as Array<(ctx: unknown) => void>,
    postSave: [] as Array<(ctx: unknown) => void>,
    ui: {
      setFormNotification: jest.fn((text: string, level: string, uid: string) => {
        shown.set(uid, { text, level });
        return true;
      }),
      clearFormNotification: jest.fn((uid: string) => {
        shown.delete(uid);
        return true;
      }),
    },
    data: {
      addOnLoad: (fn: (ctx: unknown) => void) => form.dataOnLoad.push(fn),
      removeOnLoad: (fn: (ctx: unknown) => void) => {
        form.dataOnLoad = form.dataOnLoad.filter(h => h !== fn);
      },
      entity: {
        getEntityName: () => form.table,
        getId: () => form.id ?? '',
        addOnPostSave: (fn: (ctx: unknown) => void) => form.postSave.push(fn),
        removeOnPostSave: (fn: (ctx: unknown) => void) => {
          form.postSave = form.postSave.filter(h => h !== fn);
        },
      },
    },
  };
  return form;
}

const ctxOf = (form: ReturnType<typeof makeForm>) => ({ getFormContext: () => form });

function jsonResponse(body: unknown, status = 200) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => {
      if (body instanceof Error) throw body;
      return body;
    },
  };
}

function statusBody(secure: unknown, noAccess: unknown, recordId = RECORD_ID, extra: Record<string, unknown> = {}) {
  return { recordType: 'sprk_matter', recordId, secure, noAccess, entriesState: 'notShown', entries: null, ...extra };
}

let fetchMock: jest.Mock;

function installXrm(value: string | null = BASE) {
  win.Xrm = {
    WebApi: {
      retrieveMultipleRecords: jest.fn(async (entity: string) => {
        if (entity === 'environmentvariabledefinition') {
          return { entities: [{ environmentvariabledefinitionid: 'def-1', defaultvalue: null }] };
        }
        return { entities: value === null ? [] : [{ value }] };
      }),
    },
  };
}

beforeEach(() => {
  // The script's top-level `var Spaarke` is a non-deletable global after the first load; reset its namespace instead.
  if (win.Spaarke) {
    win.Spaarke.AccessStatus = undefined;
    win.Spaarke.BffAuth = undefined;
  }
  inject();
  installXrm();
  fetchMock = jest.fn(async () => jsonResponse(statusBody('doesNotApply', 'doesNotApply')));
  win.Spaarke.BffAuth = { authenticatedFetch: fetchMock };
  jest.spyOn(console, 'error').mockImplementation(() => {});
  jest.spyOn(console, 'warn').mockImplementation(() => {});
});

afterEach(() => {
  jest.restoreAllMocks();
});

async function load(form = makeForm()) {
  win.Spaarke.AccessStatus.onLoad(ctxOf(form));
  await flush();
  return form;
}

const ids = (form: ReturnType<typeof makeForm>) => Array.from(form.shown.keys()).sort();

describe('sprk_accessstatus_banner.js (task 153)', () => {
  it('asks 064 route for the form record with the canonical id, through Spaarke.BffAuth', async () => {
    await load();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, options, baseUrl] = fetchMock.mock.calls[0];
    expect(url).toBe(`${BASE}/api/v1/records/sprk_matter/${RECORD_ID}/no-access`);
    expect(options.method).toBe('GET');
    expect(baseUrl).toBe(BASE);
  });

  it.each([
    ['secure only', 'applies', 'doesNotApply', ['sprk_access_secure']],
    ['no access only', 'doesNotApply', 'applies', ['sprk_access_noaccess']],
    ['both', 'applies', 'applies', ['sprk_access_noaccess', 'sprk_access_secure']],
    ['neither', 'doesNotApply', 'doesNotApply', []],
  ])('%s → exactly the closed-copy ERROR notifications', async (_label, secure, noAccess, expected) => {
    fetchMock.mockResolvedValue(jsonResponse(statusBody(secure, noAccess)));
    const form = await load();
    expect(ids(form)).toEqual(expected);
    if (secure === 'applies')
      expect(form.shown.get('sprk_access_secure')).toEqual({ text: TEXT.secure, level: 'ERROR' });
    if (noAccess === 'applies')
      expect(form.shown.get('sprk_access_noaccess')).toEqual({ text: TEXT.noAccess, level: 'ERROR' });
  });

  const unavailableCases: Array<[string, () => void]> = [
    ['secure unknown', () => fetchMock.mockResolvedValue(jsonResponse(statusBody('unknown', 'doesNotApply')))],
    [
      'no access unknown beside secure applies',
      () => fetchMock.mockResolvedValue(jsonResponse(statusBody('applies', 'unknown'))),
    ],
    ['a missing signal', () => fetchMock.mockResolvedValue(jsonResponse({ recordId: RECORD_ID, secure: 'applies' }))],
    ['an unrecognised state', () => fetchMock.mockResolvedValue(jsonResponse(statusBody('yes', 'doesNotApply')))],
    ['a 404', () => fetchMock.mockResolvedValue(jsonResponse({ title: 'Not Found' }, 404))],
    ['a 403', () => fetchMock.mockResolvedValue(jsonResponse({}, 403))],
    ['a 500', () => fetchMock.mockResolvedValue(jsonResponse({}, 500))],
    ['an unparseable body', () => fetchMock.mockResolvedValue(jsonResponse(new Error('bad json')))],
    [
      'an answer naming another record',
      () => fetchMock.mockResolvedValue(jsonResponse(statusBody('applies', 'applies', OTHER_ID))),
    ],
    [
      'an answer naming no record',
      () => fetchMock.mockResolvedValue(jsonResponse({ secure: 'applies', noAccess: 'applies' })),
    ],
    ['a null Response (no token)', () => fetchMock.mockResolvedValue(null)],
    ['a thrown call', () => fetchMock.mockRejectedValue(new Error('network'))],
    ['a missing Spaarke.BffAuth', () => delete win.Spaarke.BffAuth],
    ['an unresolved BFF URL', () => installXrm(null)],
    [
      'a failed environment-variable read',
      () => {
        win.Xrm.WebApi.retrieveMultipleRecords = jest.fn(async () => {
          throw new Error('no read');
        });
      },
    ],
  ];

  it.each(unavailableCases)('%s → ONLY the INFO unavailable notice', async (_label, arrange) => {
    arrange();
    const form = await load();
    expect(ids(form)).toEqual(['sprk_access_unavailable']);
    expect(form.shown.get('sprk_access_unavailable')).toEqual({ text: TEXT.unavailable, level: 'INFO' });
  });

  it('never shows a count, a name or a reason, even when a Write caller receives entries', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(
        statusBody('applies', 'applies', RECORD_ID, {
          entriesState: 'complete',
          entries: [{ entryId: 'e-1', subjectName: 'Walter Walled', name: 'Wall on Walter' }],
        })
      )
    );
    const form = await load();
    const all = Array.from(form.shown.values())
      .map(n => n.text)
      .join(' ');
    expect(all).not.toMatch(/Walter|Wall on|\d/);
    expect(all).not.toMatch(/not secure|not restricted/i);
  });

  it('a create form (no record id) and a table the route does not answer for make no call', async () => {
    await load(makeForm('sprk_matter', null));
    await load(makeForm('sprk_invoice'));
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('a data refresh and a save each re-evaluate; doesNotApply clears the earlier notification', async () => {
    fetchMock.mockResolvedValue(jsonResponse(statusBody('doesNotApply', 'doesNotApply')));
    const form = await load();
    expect(ids(form)).toEqual([]);

    // Make Secure, then the ribbon's formContext.data.refresh(false) → data OnLoad.
    fetchMock.mockResolvedValue(jsonResponse(statusBody('applies', 'doesNotApply')));
    form.dataOnLoad.forEach(h => h(ctxOf(form)));
    await flush();
    expect(ids(form)).toEqual(['sprk_access_secure']);

    // The last entry deactivated, then a save.
    fetchMock.mockResolvedValue(jsonResponse(statusBody('doesNotApply', 'doesNotApply')));
    form.postSave.forEach(h => h(ctxOf(form)));
    await flush();
    expect(ids(form)).toEqual([]);
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it('registers each handler once even when OnLoad runs again on the same form', async () => {
    const form = await load();
    win.Spaarke.AccessStatus.onLoad(ctxOf(form));
    await flush();
    expect(form.dataOnLoad).toHaveLength(1);
    expect(form.postSave).toHaveLength(1);
  });

  it('drops an answer superseded by a newer evaluation', async () => {
    const pending: Array<(v: unknown) => void> = [];
    fetchMock.mockImplementation(() => new Promise(resolve => pending.push(resolve)));
    const form = makeForm();
    win.Spaarke.AccessStatus.onLoad(ctxOf(form)); // evaluation 1
    await flush();
    form.dataOnLoad.forEach(h => h(ctxOf(form))); // evaluation 2
    await flush();
    expect(pending).toHaveLength(2);

    pending[1](jsonResponse(statusBody('doesNotApply', 'doesNotApply'))); // the newer answer first
    await flush();
    pending[0](jsonResponse(statusBody('applies', 'applies'))); // the older answer last
    await flush();
    expect(ids(form)).toEqual([]);
  });

  it("two forms at once (a record opened in a dialog over another): one form's evaluation never drops the other's answer", async () => {
    const pending: Array<{ url: string; resolve: (v: unknown) => void }> = [];
    fetchMock.mockImplementation((url: string) => new Promise(resolve => pending.push({ url, resolve })));
    const matter = makeForm('sprk_matter', RECORD_ID);
    const project = makeForm('sprk_project', OTHER_ID);
    win.Spaarke.AccessStatus.onLoad(ctxOf(matter));
    await flush();
    win.Spaarke.AccessStatus.onLoad(ctxOf(project));
    await flush();
    expect(pending).toHaveLength(2);

    pending[1].resolve(
      jsonResponse({ ...statusBody('doesNotApply', 'doesNotApply', OTHER_ID), recordType: 'sprk_project' })
    );
    pending[0].resolve(jsonResponse(statusBody('applies', 'applies')));
    await flush();
    expect(ids(matter)).toEqual(['sprk_access_noaccess', 'sprk_access_secure']);
    expect(ids(project)).toEqual([]);
  });

  it('drops an answer for a record the form has left', async () => {
    let resolveFirst: (v: unknown) => void = () => {};
    fetchMock.mockImplementationOnce(() => new Promise(resolve => (resolveFirst = resolve)));
    const form = makeForm();
    win.Spaarke.AccessStatus.onLoad(ctxOf(form));
    await flush();
    form.id = OTHER_ID; // the form now shows another record
    resolveFirst(jsonResponse(statusBody('applies', 'applies')));
    await flush();
    expect(ids(form)).toEqual([]);
  });

  it('never throws, even when the form context is broken', async () => {
    const broken = {
      getFormContext: () => {
        throw new Error('no form');
      },
    };
    expect(() => win.Spaarke.AccessStatus.onLoad(broken)).not.toThrow();
    expect(() => win.Spaarke.AccessStatus.onDataLoad(broken)).not.toThrow();
    expect(() => win.Spaarke.AccessStatus.onPostSave(broken)).not.toThrow();
    const form = makeForm();
    form.ui.setFormNotification.mockImplementation(() => {
      throw new Error('ui gone');
    });
    fetchMock.mockResolvedValue(jsonResponse(statusBody('applies', 'applies')));
    await expect(load(form)).resolves.toBeDefined();
  });
});
