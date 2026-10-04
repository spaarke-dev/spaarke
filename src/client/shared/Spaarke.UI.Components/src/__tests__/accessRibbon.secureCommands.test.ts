/**
 * `sprk_access_ribbon.js` 1.1.0 — task 150's Make Secure / Remove Secure in task 142's ONE Access group.
 *
 * The ribbon script is a classic Dataverse web resource, not a module, so the suite runs the REAL files the way the form
 * does: each is injected as a <script> into the jsdom window (top-level `var Spaarke` becomes a global, exactly as in the
 * browser), the shared `Spaarke.AssignedAccess` web resource first (the ribbon's library order), with only the platform
 * edges replaced — `Xrm` (WebApi, Navigation, App) and `Spaarke.BffAuth` (MSAL; its `authenticatedFetch` is the one
 * network seam, so the request the script sends is asserted as sent).
 *
 * It lives here because this is the jest project CI runs and it already transforms what it loads; the RegardingResolver
 * PCF suite tests `sprk_todo_regarding_presave.js` the same way (a web resource exercised from a package's jest).
 *
 * Pinned (task 150 UX amendment, closed acceptance (a)–(f); owner round 27):
 *  - the Make Secure confirmation copy, verbatim, as ONE constant, for each of the three tables;
 *  - enable logic: Make Secure only when NOT secure and the caller may manage access; Remove Secure only when secure and
 *    the caller may; a Read-only caller sees neither (e); a failed or masked read of sprk_issecure hides BOTH (f);
 *  - the calls: Make Secure confirms first and calls the provisioning endpoint (Cancel calls nothing); Remove Secure
 *    calls the unsecure endpoint and shows the endpoint's ProblemDetails message on refusal (F3 is the server's, (c)).
 */
import * as fs from 'fs';
import * as path from 'path';

/* eslint-disable @typescript-eslint/no-explicit-any */

const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const RIBBON_SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_access_ribbon.js');
const ASSIGNED_ACCESS_SCRIPT = path.resolve(CLIENT_ROOT, '../solutions/webresources/sprk_assignedaccess_postsave.js');

const BFF = 'https://bff.example.test';
const RECORD_ID = 'aaaaaaaa-1111-2222-3333-444444444444';

/** Owner round 27, verbatim — the copy the user must see. {record} filled per table. */
const PARAGRAPHS = (record: string) => [
  `Only the person who created this ${record} and the people it is shared with will keep access. Everyone else in your organization loses access, and external contacts keep only access granted to them directly.`,
  `Its existing documents, events, to-dos and other related records become secure too, for the same people, and its files move to the ${record}'s own secure storage. This can take a few minutes.`,
  `To remove the secure designation later, ask someone with Full Access to the ${record}, or the person who created it.`,
];

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

function inject(file: string): void {
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(file, 'utf8');
  document.head.appendChild(script);
}

interface World {
  ribbon: any;
  retrieveRecord: jest.Mock;
  openConfirmDialog: jest.Mock;
  openAlertDialog: jest.Mock;
  addGlobalNotification: jest.Mock;
  authenticatedFetch: jest.Mock;
}

function load(): World {
  delete win.Spaarke;
  const retrieveRecord = jest.fn();
  const openConfirmDialog = jest.fn();
  const openAlertDialog = jest.fn().mockResolvedValue(undefined);
  const addGlobalNotification = jest.fn().mockResolvedValue('n1');
  const authenticatedFetch = jest.fn();

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
  expect(ribbon?.VERSION).toBe('1.1.0'); // the real script ran
  return { ribbon, retrieveRecord, openConfirmDialog, openAlertDialog, addGlobalNotification, authenticatedFetch };
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

const RECORD_TYPES: Record<string, string> = {
  sprk_project: 'project',
  sprk_matter: 'matter',
  sprk_workassignment: 'workassignment',
};

/** 142's cached can-manage-access verdict for this record (Write on it, the server's own answer). */
function canManage(entityName: string, can: boolean): void {
  window.sessionStorage.setItem(
    `sprk_access_canmanage_${RECORD_TYPES[entityName]}_${RECORD_ID}`,
    can ? 'true' : 'false'
  );
}

const response = (status: number, body: unknown) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

const settle = () => new Promise(resolve => setTimeout(resolve, 0));

let consoleWarn: jest.SpyInstance;
let consoleError: jest.SpyInstance;

beforeEach(() => {
  window.sessionStorage.clear();
  consoleWarn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  consoleWarn.mockRestore();
  consoleError.mockRestore();
});

describe('Make Secure confirmation copy (owner round 27) — ONE constant, verbatim', () => {
  it.each([
    ['sprk_project', 'project'],
    ['sprk_matter', 'matter'],
    ['sprk_workassignment', 'work assignment'],
  ])('fills {record} for %s with "%s"', (entityName, record) => {
    const { ribbon } = load();

    const confirmation = ribbon.makeSecureConfirmationFor(entityName);

    expect(confirmation).toEqual({
      title: `Make this ${record} secure?`,
      text: PARAGRAPHS(record).join('\n\n'),
      confirmButtonLabel: 'Make Secure',
      cancelButtonLabel: 'Cancel',
    });
  });

  it('is the one frozen constant the dialog is built from', () => {
    const { ribbon } = load();

    expect(Object.isFrozen(ribbon.MAKE_SECURE_CONFIRMATION)).toBe(true);
    expect(ribbon.MAKE_SECURE_CONFIRMATION.title).toBe('Make this {record} secure?');
    expect(ribbon.MAKE_SECURE_CONFIRMATION.paragraphs).toEqual(PARAGRAPHS('{record}'));
    expect(ribbon.makeSecureConfirmationFor('sprk_invoice')).toBeNull();
  });
});

describe('enable rules — acceptance (e) and (f)', () => {
  it.each(Object.keys(RECORD_TYPES))(
    'on a NOT secure %s, a caller who may manage access sees Make Secure and not Remove Secure',
    async entityName => {
      const { ribbon, retrieveRecord } = load();
      canManage(entityName, true);
      retrieveRecord.mockResolvedValue({ sprk_issecure: false });

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
      expect(retrieveRecord).toHaveBeenCalledWith(entityName, RECORD_ID, '?$select=sprk_issecure');
    }
  );

  it.each(Object.keys(RECORD_TYPES))(
    'on a secure %s, a caller who may manage access sees Remove Secure and not Make Secure',
    async entityName => {
      const { ribbon, retrieveRecord } = load();
      canManage(entityName, true);
      retrieveRecord.mockResolvedValue({ sprk_issecure: true });

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(false);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
    }
  );

  it.each([true, false])('a Read-only caller sees neither command (record secure: %s)', async secure => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_workassignment', false);
    retrieveRecord.mockResolvedValue({ sprk_issecure: secure });

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_workassignment'))).toBe(false);
  });

  it('a FAILED read of sprk_issecure hides both commands', async () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_matter', true);
    retrieveRecord.mockRejectedValue(new Error('read refused'));

    expect(await ribbon.canMakeSecure(form('sprk_matter'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_matter'))).toBe(false);
  });

  it('a read that throws before it starts hides both commands', async () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_matter', true);
    retrieveRecord.mockImplementation(() => {
      throw new Error('Xrm.WebApi unavailable');
    });

    expect(await ribbon.canMakeSecure(form('sprk_matter'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_matter'))).toBe(false);
  });

  it.each([
    ['absent', {}],
    ['null', { sprk_issecure: null }],
  ])('a MASKED (%s) sprk_issecure hides both commands', async (_shape, row) => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_project', true);
    retrieveRecord.mockResolvedValue(row);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_project'))).toBe(false);
  });

  it('the Access flyout stays on 142’s rule (both secure commands need the same Write verdict)', () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_project', true);

    expect(ribbon.isAccessMenuVisible(form('sprk_project'))).toBe(true);
    expect(retrieveRecord).not.toHaveBeenCalled();
  });
});

describe('Make Secure — confirms, then calls the provisioning endpoint', () => {
  it('shows the confirmation for the record, then POSTs {recordType, recordId} to provision-project and refreshes', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, addGlobalNotification } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    authenticatedFetch.mockResolvedValue(response(200, { recordType: 'matter', childrenOnly: false }));
    const matter = form('sprk_matter');

    await ribbon.makeSecure(matter);
    await settle();

    expect(openConfirmDialog).toHaveBeenCalledTimes(1);
    expect(openConfirmDialog.mock.calls[0][0]).toEqual({
      title: 'Make this matter secure?',
      text: PARAGRAPHS('matter').join('\n\n'),
      confirmButtonLabel: 'Make Secure',
      cancelButtonLabel: 'Cancel',
    });

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init, baseUrl] = authenticatedFetch.mock.calls[0];
    expect(url).toBe(`${BFF}/api/v1/external-access/provision-project`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ recordType: 'matter', recordId: RECORD_ID });
    expect(init.headers.Authorization).toBeUndefined(); // the token is the helper's (ADR-028), never built here
    expect(baseUrl).toBe(BFF);

    expect(matter.data.refresh).toHaveBeenCalledWith(false);
    expect(matter.ui.refreshRibbon).toHaveBeenCalled();
    expect(addGlobalNotification).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'This matter is now secure.' })
    );
  });

  it('Cancel calls nothing', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: false });
    const project = form('sprk_project');

    await ribbon.makeSecure(project);
    await settle();

    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(project.data.refresh).not.toHaveBeenCalled();
  });

  it("a refusal shows the endpoint's ProblemDetails message, and the form is re-read", async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, addGlobalNotification } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    const detail =
      'Only the person who created this project can secure it this way, and you did not create it. Nothing was changed.';
    authenticatedFetch.mockResolvedValue(response(403, { detail, reasonCode: 'sdap.provision.not_record_creator' }));
    const project = form('sprk_project');

    await ribbon.makeSecure(project);
    await settle();

    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Make Secure', text: detail });
    expect(addGlobalNotification).not.toHaveBeenCalled();
    expect(project.data.refresh).toHaveBeenCalledWith(false);
  });

  it('with no silent token it makes no call and says so', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    authenticatedFetch.mockResolvedValue(null); // Spaarke.BffAuth.authenticatedFetch: no token, no request
    const project = form('sprk_project');

    await ribbon.makeSecure(project);
    await settle();

    expect(openAlertDialog).toHaveBeenCalledWith({
      title: 'Make Secure',
      text: 'Sign-in needed - reload the page and retry. Nothing was changed.',
    });
    expect(project.data.refresh).not.toHaveBeenCalled();
  });
});

describe('Remove Secure — the server decides who may (F3), the client shows its answer', () => {
  it("a Collaborate holder who is not the creator is refused: the endpoint's F3 message is shown and the form re-read", async () => {
    const { ribbon, authenticatedFetch, openAlertDialog, openConfirmDialog, addGlobalNotification } = load();
    const detail =
      'Only someone with Full Access to this matter, or the person who created it, can remove its secure designation. Nothing was changed.';
    authenticatedFetch.mockResolvedValue(response(403, { detail, reasonCode: 'sdap.unsecure.not_permitted' }));
    const matter = form('sprk_matter');

    await ribbon.removeSecure(matter);
    await settle();

    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(url).toBe(`${BFF}/api/v1/external-access/unsecure-project`);
    expect(JSON.parse(init.body)).toEqual({ recordType: 'matter', recordId: RECORD_ID });
    expect(openConfirmDialog).not.toHaveBeenCalled();
    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Remove Secure', text: detail });
    expect(addGlobalNotification).not.toHaveBeenCalled();
    expect(matter.data.refresh).toHaveBeenCalledWith(false);
  });

  it('the creator or a Full Access holder succeeds: the outcome is shown and the form refreshed', async () => {
    const { ribbon, authenticatedFetch, addGlobalNotification, openAlertDialog } = load();
    authenticatedFetch.mockResolvedValue(response(200, { recordType: 'workassignment' }));
    const assignment = form('sprk_workassignment');

    await ribbon.removeSecure(assignment);
    await settle();

    expect(openAlertDialog).not.toHaveBeenCalled();
    expect(addGlobalNotification).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'This work assignment is no longer secure.' })
    );
    expect(assignment.data.refresh).toHaveBeenCalledWith(false);
    expect(assignment.ui.refreshRibbon).toHaveBeenCalled();
  });

  it('a refusal with no ProblemDetails message shows the status, never a guess about who may unsecure', () => {
    const { ribbon } = load();

    expect(ribbon.refusalText('Remove Secure', { status: 500, body: null })).toBe(
      'Remove Secure did not complete (500). Reload the record to see its current state.'
    );
  });

  it('after a command the secure state is read again, so the rules follow the new state', async () => {
    const { ribbon, authenticatedFetch, retrieveRecord } = load();
    canManage('sprk_matter', true);
    retrieveRecord.mockResolvedValueOnce({ sprk_issecure: true }).mockResolvedValueOnce({ sprk_issecure: false });
    authenticatedFetch.mockResolvedValue(response(200, {}));
    const matter = form('sprk_matter');

    expect(await ribbon.canRemoveSecure(matter)).toBe(true);
    await ribbon.removeSecure(matter);
    await settle();

    expect(await ribbon.canMakeSecure(matter)).toBe(true);
    expect(await ribbon.canRemoveSecure(matter)).toBe(false);
    expect(retrieveRecord).toHaveBeenCalledTimes(2);
  });
});
