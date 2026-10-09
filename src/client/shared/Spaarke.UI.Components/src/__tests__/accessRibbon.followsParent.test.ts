/**
 * `sprk_access_ribbon.js` 1.7.0 — task 175 (unified-access-control-r2, owner round 84: "a child's access always follows
 * its parent, both ways, and is locked while it has a parent").
 *
 * A work assignment or project filed under a matter or project takes its Secure designation from that parent, so Make
 * Secure and Remove Secure are HIDDEN on it. The ribbon learns it from the SAME can-manage-access answer it already asks
 * for the Write verdict (`followsParents`: the record's direct filing parents; `parentUnverifiable`: what it is filed under
 * could not be read — fail closed). An answer without those fields (an older BFF) is "no parent". The server refuses both
 * calls on such a record (409 `sdap.access.access_follows_parent`): the ribbon shows the server's message, or its own
 * fallback when there is none.
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

/** A record flagged secure that a user still owns (unfinished): both commands when it has no parent. */
const SECURE_USER_OWNED = {
  sprk_issecure: true,
  sprk_containerid: 'b!its-own-container',
  _owninguser_value: 'eeeeeeee-1111-2222-3333-444444444444',
};

const FILED_UNDER_MATTER = [{ recordType: 'matter', recordId: MATTER_ID, name: 'PAT-176903' }];

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
  expect(ribbon?.VERSION).toBe('1.7.0'); // the real script ran
  return {
    ribbon,
    retrieveRecord,
    openConfirmDialog,
    openAlertDialog,
    addGlobalNotification,
    authenticatedFetch,
    gateFetch,
  };
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

describe('task 175 — Make Secure and Remove Secure are hidden on a record that follows a parent', () => {
  it.each([
    ['sprk_workassignment', NOT_SECURE],
    ['sprk_workassignment', SECURE_USER_OWNED],
    ['sprk_project', NOT_SECURE],
    ['sprk_project', SECURE_USER_OWNED],
  ])(
    'a %s filed under a matter (followsParents non-empty): both hidden, Update Access stays (%o)',
    async (entityName, row) => {
      const { ribbon, retrieveRecord, gateFetch } = load({
        followsParents: FILED_UNDER_MATTER,
        parentUnverifiable: false,
      });
      retrieveRecord.mockResolvedValue(row);

      const [make, remove] = await Promise.all([
        ribbon.canMakeSecure(form(entityName)),
        ribbon.canRemoveSecure(form(entityName)),
      ]);

      expect(make).toBe(false);
      expect(remove).toBe(false);
      expect(await ribbon.canUpdateAccess(form(entityName))).toBe(true); // the cached Write verdict: no second call
      expect(gateFetch).toHaveBeenCalledTimes(1); // ONE can-manage-access answer for every rule
      expect(gateFetch.mock.calls[0][0]).toBe(
        `${BFF}/api/v1/external-access/can-manage-access?recordType=${RECORD_TYPES[entityName]}&recordId=${RECORD_ID}`
      );
    }
  );

  it.each(['sprk_workassignment', 'sprk_project'])(
    'a %s whose filing could not be read (parentUnverifiable: true, no parents listed): both hidden (fail closed)',
    async entityName => {
      const { ribbon, retrieveRecord } = load({ followsParents: [], parentUnverifiable: true });
      retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(false);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
      expect(await ribbon.canUpdateAccess(form(entityName))).toBe(true);
    }
  );

  it.each(Object.keys(RECORD_TYPES))(
    'a parentless %s (followsParents empty, parentUnverifiable false): shown as before',
    async entityName => {
      const { ribbon, retrieveRecord } = load({ followsParents: [], parentUnverifiable: false });
      retrieveRecord.mockResolvedValue(NOT_SECURE);

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
    }
  );

  it.each(Object.keys(RECORD_TYPES))(
    'an answer without the fields (an older BFF) is "no parent" on a %s: shown as before',
    async entityName => {
      const { ribbon, retrieveRecord } = load({});
      retrieveRecord.mockResolvedValue(SECURE_USER_OWNED);

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
    }
  );

  it('a caller without Write sees neither, parent or not (the parent facts are moot)', async () => {
    const { ribbon, retrieveRecord } = load({ canManageAccess: false, followsParents: [] });
    retrieveRecord.mockResolvedValue(NOT_SECURE);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
    expect(await ribbon.canUpdateAccess(form('sprk_project'))).toBe(false);
  });

  it('the answer is cached with its parent facts; a fresh cache answers the rules without another call', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load({ followsParents: FILED_UNDER_MATTER });
    retrieveRecord.mockResolvedValue(NOT_SECURE);

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(false);
    expect(JSON.parse(window.sessionStorage.getItem(cacheKey('sprk_workassignment'))!)).toEqual({
      can: true,
      followsParent: true,
      at: expect.any(Number),
    });
    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(false);
    expect(ribbon.canUpdateAccess(form('sprk_workassignment'))).toBe(true); // a cache hit answers synchronously
    expect(gateFetch).toHaveBeenCalledTimes(1);
  });

  it('past 30 seconds the secure rules ask again: a record un-filed since shows Make Secure', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load({ followsParents: FILED_UNDER_MATTER });
    retrieveRecord.mockResolvedValue(NOT_SECURE);
    const start = Date.now();
    const now = jest.spyOn(Date, 'now').mockReturnValue(start);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
    gateFetch.mockImplementation(async () => gateAnswer({ followsParents: [], parentUnverifiable: false }));
    now.mockReturnValue(start + 31000);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(true);
    expect(gateFetch).toHaveBeenCalledTimes(2);
  });

  it('a value an older script version cached ("true", no parent facts) is asked again', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load({ followsParents: FILED_UNDER_MATTER });
    retrieveRecord.mockResolvedValue(NOT_SECURE);
    window.sessionStorage.setItem(cacheKey('sprk_workassignment'), 'true');

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(false);
    expect(gateFetch).toHaveBeenCalledTimes(1);
  });
});

describe('task 175 — the 409 sdap.access.access_follows_parent refusal', () => {
  const REFUSAL = 'sdap.access.access_follows_parent';

  it.each([
    ['Make Secure', 'makeSecure', '/api/v1/external-access/provision-project'],
    ['Remove Secure', 'removeSecure', '/api/v1/external-access/unsecure-project'],
  ])(
    "%s refused: the server's message, no retry; the cached answer is forgotten and asked again",
    async (title, command, endpoint) => {
      const { ribbon, retrieveRecord, gateFetch, openConfirmDialog, authenticatedFetch, openAlertDialog } = load({
        followsParents: [],
      });
      retrieveRecord.mockResolvedValue(command === 'makeSecure' ? NOT_SECURE : SECURE_USER_OWNED);
      const page = form('sprk_workassignment');
      expect(await (command === 'makeSecure' ? ribbon.canMakeSecure(page) : ribbon.canRemoveSecure(page))).toBe(true);

      // Filed under a matter since the ribbon asked: the server refuses.
      const detail = 'This work assignment is filed under PAT-176903, and its access follows it. Change it there.';
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
      gateFetch.mockImplementation(async () => gateAnswer({ followsParents: FILED_UNDER_MATTER }));

      await ribbon[command](page);
      await settle();

      expect(authenticatedFetch.mock.calls[0][0]).toBe(`${BFF}${endpoint}`);
      expect(openAlertDialog).toHaveBeenCalledWith({ title, text: detail });
      expect(openConfirmDialog).toHaveBeenCalledTimes(1); // the confirmation only: not offered again in place
      expect(page.ui.refreshRibbon).toHaveBeenCalled();
      expect(window.sessionStorage.getItem(cacheKey('sprk_workassignment'))).toBeNull();

      expect(await ribbon.canMakeSecure(page)).toBe(false);
      expect(await ribbon.canRemoveSecure(page)).toBe(false);
      expect(gateFetch).toHaveBeenCalledTimes(2);
    }
  );

  it.each([
    ['sprk_workassignment', 'work assignment', 'PAT-176903', 'PAT-176903'],
    ['sprk_project', 'project', '  ', 'its parent'],
    ['sprk_project', 'project', undefined, 'its parent'],
  ])(
    'without a server message on a %s: the fallback ({record} = %s, {parent} from parentName %p)',
    (entityName, record, parentName, parent) => {
      const { ribbon } = load({});

      expect(
        ribbon.refusalFor('Make Secure', { status: 409, body: { reasonCode: REFUSAL, parentName } }, entityName)
      ).toBe(`This ${record} is filed under ${parent}, and its access follows it. Change it there.`);
    }
  );

  it('the fallback is one constant; REFUSAL_COPY is unchanged (the server words this refusal)', () => {
    const { ribbon } = load({});

    expect(ribbon.ACCESS_FOLLOWS_PARENT).toBe(REFUSAL);
    expect(ribbon.ACCESS_FOLLOWS_PARENT_FALLBACK).toBe(
      'This {record} is filed under {parent}, and its access follows it. Change it there.'
    );
    expect(Object.keys(ribbon.REFUSAL_COPY)).toEqual(['sdap.provision.caller_rights_unverifiable']);
  });
});
