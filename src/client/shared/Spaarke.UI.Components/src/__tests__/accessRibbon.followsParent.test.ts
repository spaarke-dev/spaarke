/**
 * `sprk_access_ribbon.js` 1.8.0 — task 175 (unified-access-control-r2, owner round 87: "the parent sets a FLOOR").
 *
 * A work assignment or project filed under a matter or project inherits Secure from it and may never be looser, but a
 * user may make it stricter by hand. The ribbon learns the floor from the SAME can-manage-access answer it already asks
 * for the Write verdict:
 *  - `floorSecure === true` (its Secure is inherited): Remove Secure hidden — the server refuses it 409
 *    `sdap.access.access_follows_parent`; Make Secure follows its normal rules;
 *  - `floorSecure === false` (a child secured on its own): Remove Secure follows its normal rules (the server enforces F3);
 *  - `parentUnverifiable === true`: both hidden (fail closed);
 *  - an answer without the fields (an older BFF): no floor — the 1.6.0 behaviour.
 *
 * Runs the REAL scripts in jsdom, as accessRibbon.secureCommands.test.ts does; the page's `fetch` (the gate) and
 * `Spaarke.BffAuth.authenticatedFetch` (the commands) are the network seams.
 */
import * as fs from 'fs';
import * as path from 'path';

/* eslint-disable @typescript-eslint/no-explicit-any */

const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const RIBBON_SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_access_ribbon.js');
const ASSIGNED_ACCESS_SCRIPT = path.resolve(CLIENT_ROOT, '../solutions/webresources/sprk_assignedaccess_postsave.js');

const BFF = 'https://bff.example.test';
const RECORD_ID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID = 'bbbbbbbb-1111-2222-3333-444444444444';

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

const RECORD_TYPES: Record<string, string> = {
  sprk_project: 'project',
  sprk_matter: 'matter',
  sprk_workassignment: 'workassignment',
};

/** A record that is not secure (a user owns it): the read alone decides, no owner question. */
const NOT_SECURE = {
  sprk_issecure: false,
  sprk_containerid: null,
  _owninguser_value: 'eeeeeeee-1111-2222-3333-444444444444',
};

/** A record flagged secure that a user still owns (unfinished): both commands when nothing else hides them. */
const SECURE_USER_OWNED = {
  sprk_issecure: true,
  sprk_containerid: 'b!its-own-container',
  _owninguser_value: 'eeeeeeee-1111-2222-3333-444444444444',
};

const FILED_UNDER_MATTER = [{ recordType: 'matter', recordId: MATTER_ID, name: 'PAT-176903' }];

/** A filed record whose floor is SECURE (the parent is secure). */
const SECURE_FLOOR = {
  followsParents: FILED_UNDER_MATTER,
  parentUnverifiable: false,
  floorSecure: true,
  floorAccessPermission: 'restricted',
};

/** A filed record whose floor is NOT secure (the parent is not secure). */
const OPEN_FLOOR = {
  followsParents: FILED_UNDER_MATTER,
  parentUnverifiable: false,
  floorSecure: false,
  floorAccessPermission: 'limited',
};

/** What it is filed under could not be read: both floors null. */
const UNVERIFIABLE = {
  followsParents: [],
  parentUnverifiable: true,
  floorSecure: null,
  floorAccessPermission: null,
};

function inject(file: string): void {
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(file, 'utf8');
  document.head.appendChild(script);
}

const gateAnswer = (extra: Record<string, unknown>) =>
  ({
    ok: true,
    status: 200,
    json: async () => ({ recordId: RECORD_ID, canManageAccess: true, ...extra }),
  }) as unknown as Response;

const response = (status: number, body: unknown) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

function load(gate: Record<string, unknown>) {
  delete win.Spaarke;
  const retrieveRecord = jest.fn();
  const openConfirmDialog = jest.fn();
  const openAlertDialog = jest.fn().mockResolvedValue(undefined);
  const addGlobalNotification = jest.fn().mockResolvedValue('n1');
  const authenticatedFetch = jest.fn();
  const gateFetch = jest.fn().mockImplementation(async () => gateAnswer(gate));
  (win as any).fetch = gateFetch;

  win.Xrm = {
    WebApi: { retrieveRecord, retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('not used')) },
    Navigation: { openConfirmDialog, openAlertDialog },
    App: { addGlobalNotification, clearGlobalNotification: jest.fn() },
  };
  win.Spaarke = {
    BffAuth: { getToken: jest.fn().mockResolvedValue('token-not-a-credential'), authenticatedFetch },
  };

  inject(ASSIGNED_ACCESS_SCRIPT);
  inject(RIBBON_SCRIPT);
  win.Spaarke.AssignedAccess._cachedApiBaseUrl = BFF;

  const ribbon = win.Spaarke?.Access?.Ribbon;
  expect(ribbon?.VERSION).toBe('1.8.0'); // the real script ran
  return { ribbon, retrieveRecord, openConfirmDialog, openAlertDialog, addGlobalNotification, authenticatedFetch, gateFetch };
}

function form(entityName: string) {
  return {
    data: {
      entity: { getEntityName: () => entityName, getId: () => `{${RECORD_ID.toUpperCase()}}` },
      refresh: jest.fn().mockResolvedValue(undefined),
    },
    ui: { refreshRibbon: jest.fn() },
  };
}

const cacheKey = (entityName: string) => `sprk_access_canmanage_${RECORD_TYPES[entityName]}_${RECORD_ID}`;

const settle = () => new Promise(resolve => setTimeout(resolve, 0));

let consoleWarn: jest.SpyInstance;
let consoleError: jest.SpyInstance;
let consoleInfo: jest.SpyInstance;

beforeEach(() => {
  window.sessionStorage.clear();
  consoleWarn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
  consoleInfo = jest.spyOn(console, 'info').mockImplementation(() => {});
});

afterEach(() => {
  consoleWarn.mockRestore();
  consoleError.mockRestore();
  consoleInfo.mockRestore();
  jest.restoreAllMocks();
});

describe('task 175 (round 87) — Make Secure on a filed record follows its normal rules', () => {
  it.each([
    ['sprk_workassignment', OPEN_FLOOR],
    ['sprk_project', OPEN_FLOOR],
    ['sprk_workassignment', { followsParents: FILED_UNDER_MATTER, parentUnverifiable: false }],
  ])('a NOT secure %s filed under a parent whose floor is not secure: Make Secure shown (%o)', async (entityName, gate) => {
    const { ribbon, retrieveRecord, gateFetch } = load(gate);
    retrieveRecord.mockResolvedValue(NOT_SECURE);

    const [make, remove] = await Promise.all([
      ribbon.canMakeSecure(form(entityName)),
      ribbon.canRemoveSecure(form(entityName)),
    ]);

    expect(make).toBe(true);
    expect(remove).toBe(false); // not secure
    expect(gateFetch).toHaveBeenCalledTimes(1); // ONE can-manage-access answer for every rule
    expect(gateFetch.mock.calls[0][0]).toBe(
      `${BFF}/api/v1/external-access/can-manage-access?recordType=${RECORD_TYPES[entityName]}&recordId=${RECORD_ID}`
    );
  });

  it('an unfinished secure child under a secure floor: Make Secure ("finish") shown, Remove Secure hidden', async () => {
    const { ribbon, retrieveRecord } = load(SECURE_FLOOR);
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(true);
    expect(await ribbon.canRemoveSecure(form('sprk_workassignment'))).toBe(false);
  });
});

describe('task 175 (round 87) — Remove Secure and the floor', () => {
  it.each(['sprk_workassignment', 'sprk_project'])(
    'a secure %s whose floor is secure (floorSecure: true): Remove Secure hidden, Update Access stays',
    async entityName => {
      const { ribbon, retrieveRecord, gateFetch } = load(SECURE_FLOOR);
      retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
      expect(await ribbon.canUpdateAccess(form(entityName))).toBe(true); // the cached Write verdict: no second call
      expect(gateFetch).toHaveBeenCalledTimes(1);
    }
  );

  it.each(['sprk_workassignment', 'sprk_project'])(
    'a %s secured on its own under a floor that is not secure (floorSecure: false): Remove Secure shown',
    async entityName => {
      const { ribbon, retrieveRecord } = load(OPEN_FLOOR);
      retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
    }
  );

  it.each(['sprk_workassignment', 'sprk_project'])(
    'a %s whose filing could not be read (parentUnverifiable: true): both hidden (fail closed), Update Access stays',
    async entityName => {
      const { ribbon, retrieveRecord } = load(UNVERIFIABLE);
      retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(false);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
      expect(await ribbon.canUpdateAccess(form(entityName))).toBe(true);
    }
  );

  it('parentUnverifiable hides Make Secure on a not-secure record too', async () => {
    const { ribbon, retrieveRecord } = load(UNVERIFIABLE);
    retrieveRecord.mockResolvedValue(NOT_SECURE);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
  });
});

describe('task 175 — an answer without the floor fields behaves as 1.6.0', () => {
  it.each(Object.keys(RECORD_TYPES))('an older BFF answer on a secure %s: both shown', async entityName => {
    const { ribbon, retrieveRecord } = load({});
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

    expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
    expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
  });

  it.each(Object.keys(RECORD_TYPES))('an older BFF answer on a not-secure %s: Make Secure only', async entityName => {
    const { ribbon, retrieveRecord } = load({});
    retrieveRecord.mockResolvedValue(NOT_SECURE);

    expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
    expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
  });

  it('a parentless record (floors null, followsParents empty): shown as before', async () => {
    const { ribbon, retrieveRecord } = load({
      followsParents: [],
      parentUnverifiable: false,
      floorSecure: null,
      floorAccessPermission: null,
    });
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

    expect(await ribbon.canMakeSecure(form('sprk_matter'))).toBe(true);
    expect(await ribbon.canRemoveSecure(form('sprk_matter'))).toBe(true);
  });

  it('a caller without Write sees neither, floor or not', async () => {
    const { ribbon, retrieveRecord } = load({ canManageAccess: false, floorSecure: false });
    retrieveRecord.mockResolvedValue(NOT_SECURE);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
    expect(await ribbon.canUpdateAccess(form('sprk_project'))).toBe(false);
  });
});

describe('task 175 — the cached answer', () => {
  it('is cached with its floor facts; a fresh cache answers the rules without another call', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load(SECURE_FLOOR);
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

    expect(await ribbon.canRemoveSecure(form('sprk_workassignment'))).toBe(false);
    expect(JSON.parse(window.sessionStorage.getItem(cacheKey('sprk_workassignment'))!)).toEqual({
      can: true,
      unverifiable: false,
      floorSecure: true,
      at: expect.any(Number),
    });
    expect(await ribbon.canRemoveSecure(form('sprk_workassignment'))).toBe(false);
    expect(ribbon.canUpdateAccess(form('sprk_workassignment'))).toBe(true); // a cache hit answers synchronously
    expect(gateFetch).toHaveBeenCalledTimes(1);
  });

  it('past 30 seconds the secure rules ask again: a record re-filed under a non-secure parent shows Remove Secure', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load(SECURE_FLOOR);
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);
    const start = Date.now();
    const now = jest.spyOn(Date, 'now').mockReturnValue(start);

    expect(await ribbon.canRemoveSecure(form('sprk_project'))).toBe(false);
    gateFetch.mockImplementation(async () => gateAnswer(OPEN_FLOOR));
    now.mockReturnValue(start + 31000);

    expect(await ribbon.canRemoveSecure(form('sprk_project'))).toBe(true);
    expect(gateFetch).toHaveBeenCalledTimes(2);
  });

  it.each([
    ['"true" (1.6.0)', 'true'],
    ['{ can, followsParent } (1.7.0)', JSON.stringify({ can: true, followsParent: false, at: Date.now() })],
  ])('a value an older script version cached, %s, is asked again', async (_shape, value) => {
    const { ribbon, retrieveRecord, gateFetch } = load(SECURE_FLOOR);
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);
    window.sessionStorage.setItem(cacheKey('sprk_workassignment'), value);

    expect(await ribbon.canRemoveSecure(form('sprk_workassignment'))).toBe(false);
    expect(gateFetch).toHaveBeenCalledTimes(1);
  });
});

describe('task 175 — the 409 sdap.access.access_follows_parent refusal (Remove Secure under a secure floor)', () => {
  const REFUSAL = 'sdap.access.access_follows_parent';

  it("the server's message, no retry; the cached answer is forgotten and asked again", async () => {
    const { ribbon, retrieveRecord, gateFetch, openConfirmDialog, authenticatedFetch, openAlertDialog } = load(OPEN_FLOOR);
    retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);
    const page = form('sprk_workassignment');
    expect(await ribbon.canRemoveSecure(page)).toBe(true);

    // Filed under a secure matter since the ribbon asked: the server refuses.
    const detail =
      'This work assignment is filed under PAT-176903, which is secure; its secure designation comes from there.';
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    authenticatedFetch.mockResolvedValue(
      response(409, {
        detail,
        reasonCode: REFUSAL,
        parentRecordType: 'matter',
        parentRecordId: MATTER_ID,
        parentName: 'PAT-176903',
      })
    );
    gateFetch.mockImplementation(async () => gateAnswer(SECURE_FLOOR));

    await ribbon.removeSecure(page);
    await settle();

    expect(authenticatedFetch.mock.calls[0][0]).toBe(`${BFF}/api/v1/external-access/unsecure-project`);
    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Remove Secure', text: detail });
    expect(openConfirmDialog).toHaveBeenCalledTimes(1); // the confirmation only: not offered again in place
    expect(page.ui.refreshRibbon).toHaveBeenCalled();
    expect(window.sessionStorage.getItem(cacheKey('sprk_workassignment'))).toBeNull();

    expect(await ribbon.canRemoveSecure(page)).toBe(false);
    expect(await ribbon.canMakeSecure(page)).toBe(true); // unfinished secure child: Make Secure is not the floor's
    expect(gateFetch).toHaveBeenCalledTimes(2);
  });

  it.each([
    ['sprk_workassignment', 'work assignment', 'PAT-176903', 'PAT-176903'],
    ['sprk_project', 'project', '  ', 'its parent'],
    ['sprk_project', 'project', undefined, 'its parent'],
  ])(
    'without a server message on a %s: the fallback ({record} = %s, {parent} from parentName %p)',
    (entityName, record, parentName, parent) => {
      const { ribbon } = load({});

      expect(
        ribbon.refusalFor('Remove Secure', { status: 409, body: { reasonCode: REFUSAL, parentName } }, entityName)
      ).toBe(
        `This ${record} is filed under ${parent}, which is secure; its secure designation comes from there. Remove it there, or file it elsewhere.`
      );
    }
  );

  it('the fallback is one constant; REFUSAL_COPY is unchanged (the server words this refusal)', () => {
    const { ribbon } = load({});

    expect(ribbon.ACCESS_FOLLOWS_PARENT).toBe(REFUSAL);
    expect(ribbon.ACCESS_FOLLOWS_PARENT_FALLBACK).toBe(
      'This {record} is filed under {parent}, which is secure; its secure designation comes from there. Remove it there, or file it elsewhere.'
    );
    expect(Object.keys(ribbon.REFUSAL_COPY)).toEqual(['sdap.provision.caller_rights_unverifiable']);
  });
});
