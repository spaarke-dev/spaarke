/**
 * `sprk_access_ribbon.js` 1.5.0 — task 150's Make Secure / Remove Secure in task 142's ONE Access group.
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
 *  - the Make Secure confirmation copy (owner round 27) and the Remove Secure confirmation copy (round 33 item 2),
 *    verbatim, each ONE constant, for each of the three tables;
 *  - Make Secure sends transition "make-secure" (round 33 item 1: the server's Write-gate path);
 *  - enable logic: Make Secure only when NOT secure and the caller may manage access; Remove Secure only when secure and
 *    the caller may; a Read-only caller sees neither (e); a failed or masked read of sprk_issecure hides BOTH (f);
 *  - the calls: Make Secure confirms first and calls the provisioning endpoint (Cancel calls nothing); Remove Secure
 *    confirms first too and calls the unsecure endpoint, showing the endpoint's ProblemDetails message on refusal (F3 is
 *    the server's, (c));
 *  - never silent (round 33 items 1 and 5): a success whose `skippedPrincipals` names someone the record was not shared
 *    with (on Make Secure, its creator) shows a per-person warning from ONE constant — an unknown reason code the generic
 *    one, and logged; a person who cannot be named is "Someone" (round 40 item 3), never an empty name;
 *  - round 40 item 1 (acceptance (e) amended — Make Secure hidden on a PROVISIONED secure record): Make Secure is offered
 *    again on a record flagged secure whose transition did not finish (no container recorded, or owned by a user), from
 *    the same one read; and a Make Secure failure after the flag write that the server answers as "the same caller may
 *    call again" offers the call in place (confirm dialog, the server's message, Make Secure / Cancel);
 *  - round 46 item 4: a flagged record with a container that a TEAM owns is finished only when the server says that team
 *    is the Secure Record Owners team (can-manage-access, includeOwner=true) — a team in another business unit
 *    (reassigned outside Spaarke) is unfinished; an answer that cannot be had keeps Make Secure hidden there, and each
 *    "cannot be had" case reaches the guard it names (round 53 item 3: the fetch mock answers a real Promise); the
 *    question is asked only when the read leaves it open, and asked again after a command;
 *  - round 53 item 2: another team INSIDE the Secure Record business unit (the retired default team before task 144's
 *    migration) owns a record that is already isolated — Make Secure hidden, Remove Secure shown;
 *  - round 53 item 1: `sdap.provision.caller_rights_unverifiable` is shown in this script's own words (REFUSAL_COPY,
 *    {record} filled), verbatim.
 */
import * as fs from 'fs';
import * as path from 'path';

/* eslint-disable @typescript-eslint/no-explicit-any */

const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const RIBBON_SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_access_ribbon.js');
const ASSIGNED_ACCESS_SCRIPT = path.resolve(CLIENT_ROOT, '../solutions/webresources/sprk_assignedaccess_postsave.js');

const BFF = 'https://bff.example.test';
const RECORD_ID = 'aaaaaaaa-1111-2222-3333-444444444444';

/** Round 33 item 2, verbatim — the Remove Secure confirmation. {record} filled per table. */
const REMOVE_PARAGRAPHS = (record: string) => [
  `The ${record} and its related records return to normal access: people who can see records in its business unit will be able to see them, and the individual sharing set up while it was secure is removed.`,
  'To secure it again later, use Make Secure.',
];

/** Round 53 item 1, verbatim — `sdap.provision.caller_rights_unverifiable` in the ribbon's own words. */
const CALLER_RIGHTS_UNVERIFIABLE = (record: string) =>
  `Which access you hold on this ${record} could not be read, so securing it could not make sure you keep that access. Nothing was changed; you may try again.`;

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
  /** The page's `fetch` — the can-manage-access gate's seam (the ribbon asks it with the helper's token). */
  gateFetch: jest.Mock;
}

/** The Secure Record Owners team — what the server names as the owner of a PROVISIONED record. */
const SECURE_OWNER_TEAM = 'daec0b6f-0000-0000-0000-0000000000f1';

/** A team in ANOTHER business unit (a secure record reassigned outside Spaarke). */
const OTHER_TEAM = 'ffffffff-1111-2222-3333-444444444444';

/**
 * can-manage-access with includeOwner=true, answered about THIS record: whether the Secure Record Owners team owns it, and
 * whether its owning team owns it inside the Secure Record business unit (round 53 item 2).
 */
const ownerAnswer = (
  ownedBySecureOwnerTeam: boolean | null,
  owningTeamId: string | null = SECURE_OWNER_TEAM,
  owningTeamInSecureBusinessUnit: boolean | null = ownedBySecureOwnerTeam === true ? true : null
) =>
  ({
    ok: true,
    status: 200,
    json: async () => ({
      recordId: RECORD_ID,
      canManageAccess: true,
      owningTeamId,
      ownedBySecureOwnerTeam,
      owningTeamInSecureBusinessUnit,
    }),
  }) as unknown as Response;

function load(): World {
  delete win.Spaarke;
  const retrieveRecord = jest.fn();
  const openConfirmDialog = jest.fn();
  const openAlertDialog = jest.fn().mockResolvedValue(undefined);
  const addGlobalNotification = jest.fn().mockResolvedValue('n1');
  const authenticatedFetch = jest.fn();
  // Default: the server says the Secure Record Owners team owns the record (a PROVISIONED record's owner).
  const gateFetch = jest.fn().mockResolvedValue(ownerAnswer(true));
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
  expect(ribbon?.VERSION).toBe('1.6.0'); // the real script ran
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

/** The ONE read the secure-state rules make (round 40 item 1: the flag, the container, the owning user). */
const SECURE_STATE_SELECT = '?$select=sprk_issecure,sprk_containerid,_owninguser_value';

/** A PROVISIONED secure record: owned by the Secure Record owner TEAM (no owning user), its own container recorded. */
const PROVISIONED = { sprk_issecure: true, sprk_containerid: 'b!its-own-container', _owninguser_value: null };

/** An ordinary record a user owns, no container of its own. */
const NOT_SECURE = {
  sprk_issecure: false,
  sprk_containerid: null,
  _owninguser_value: 'eeeeeeee-1111-2222-3333-444444444444',
};

const response = (status: number, body: unknown) =>
  ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;

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

describe('Remove Secure confirmation copy (round 33 item 2) — ONE constant, verbatim', () => {
  it.each([
    ['sprk_project', 'project'],
    ['sprk_matter', 'matter'],
    ['sprk_workassignment', 'work assignment'],
  ])('fills {record} for %s with "%s"', (entityName, record) => {
    const { ribbon } = load();

    expect(ribbon.removeSecureConfirmationFor(entityName)).toEqual({
      title: `Remove the secure designation from this ${record}?`,
      text: REMOVE_PARAGRAPHS(record).join('\n\n'),
      confirmButtonLabel: 'Remove Secure',
      cancelButtonLabel: 'Cancel',
    });
  });

  it('is the one frozen constant the dialog is built from', () => {
    const { ribbon } = load();

    expect(Object.isFrozen(ribbon.REMOVE_SECURE_CONFIRMATION)).toBe(true);
    expect(ribbon.REMOVE_SECURE_CONFIRMATION.title).toBe('Remove the secure designation from this {record}?');
    expect(ribbon.REMOVE_SECURE_CONFIRMATION.paragraphs).toEqual(REMOVE_PARAGRAPHS('{record}'));
    expect(ribbon.removeSecureConfirmationFor('sprk_invoice')).toBeNull();
  });
});

describe('enable rules — acceptance (e) and (f)', () => {
  it.each(Object.keys(RECORD_TYPES))(
    'on a NOT secure %s, a caller who may manage access sees Make Secure and not Remove Secure',
    async entityName => {
      const { ribbon, retrieveRecord } = load();
      canManage(entityName, true);
      retrieveRecord.mockResolvedValue(NOT_SECURE);

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(false);
      expect(retrieveRecord).toHaveBeenCalledWith(entityName, RECORD_ID, SECURE_STATE_SELECT);
      expect(retrieveRecord).toHaveBeenCalledTimes(1); // both rules share the ONE read
    }
  );

  it.each(Object.keys(RECORD_TYPES))(
    'on a PROVISIONED secure %s, a caller who may manage access sees Remove Secure and not Make Secure',
    async entityName => {
      const { ribbon, retrieveRecord } = load();
      canManage(entityName, true);
      retrieveRecord.mockResolvedValue(PROVISIONED);

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(false);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
    }
  );

  it.each([PROVISIONED, NOT_SECURE])('a Read-only caller sees neither command (%o)', async row => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_workassignment', false);
    retrieveRecord.mockResolvedValue(row);

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_workassignment'))).toBe(false);
  });

  it('a Read-only caller sees neither command on an UNFINISHED secure record either', async () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_matter', false);
    retrieveRecord.mockResolvedValue({ sprk_issecure: true, sprk_containerid: null, _owninguser_value: null });

    expect(await ribbon.canMakeSecure(form('sprk_matter'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_matter'))).toBe(false);
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

describe('round 40 item 1 — Make Secure is offered again on a secure transition that did not finish', () => {
  it.each(Object.keys(RECORD_TYPES))(
    'a %s flagged secure with NO container recorded (a failure before Step 7): Make Secure AND Remove Secure',
    async entityName => {
      const { ribbon, retrieveRecord } = load();
      canManage(entityName, true);
      // Owned by the Secure Record owner team (the move landed), no container yet: container_creation_failed and others.
      retrieveRecord.mockResolvedValue({ sprk_issecure: true, sprk_containerid: null, _owninguser_value: null });

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
      expect(retrieveRecord).toHaveBeenCalledTimes(1); // the SAME one read
    }
  );

  it.each([
    ['empty string', ''],
    ['whitespace', '   '],
    ['absent', undefined],
  ])('a container value that is %s counts as none recorded', async (_shape, containerId) => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_project', true);
    retrieveRecord.mockResolvedValue({ sprk_issecure: true, sprk_containerid: containerId, _owninguser_value: null });

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(true);
  });

  it('a record flagged secure that a USER still owns (the move never landed, or was undone) — even with its own container recorded: Make Secure', async () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_matter', true);
    // Re-securing after Remove Secure keeps the record's own container and gives it a user owner; a failure before the
    // owner move leaves exactly this.
    retrieveRecord.mockResolvedValue({
      sprk_issecure: true,
      sprk_containerid: 'b!its-own-container',
      _owninguser_value: 'eeeeeeee-1111-2222-3333-444444444444',
    });

    expect(await ribbon.canMakeSecure(form('sprk_matter'))).toBe(true);
    expect(await ribbon.canRemoveSecure(form('sprk_matter'))).toBe(true);
  });

  it('a PROVISIONED record (team-owned, its own container recorded): Make Secure stays hidden', async () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_workassignment', true);
    retrieveRecord.mockResolvedValue(PROVISIONED);

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(false);
  });

  it('an unreadable flag hides Make Secure on that shape too (the existing fail-closed rule)', async () => {
    const { ribbon, retrieveRecord } = load();
    canManage('sprk_project', true);
    retrieveRecord.mockResolvedValue({ sprk_issecure: null, sprk_containerid: null, _owninguser_value: null });

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_project'))).toBe(false);
  });
});

describe('round 46 item 4 / round 53 items 2-3 — who owns a flagged, team-owned record decides whether it is unfinished', () => {
  /** Flagged, its own container recorded, owned by a TEAM (no owning user): the read alone cannot tell which team. */
  const TEAM_OWNED_WITH_CONTAINER = {
    sprk_issecure: true,
    sprk_containerid: 'b!its-own-container',
    _owninguser_value: null,
  };

  it.each(Object.keys(RECORD_TYPES))(
    'a %s reassigned outside Spaarke to a team in another business unit: Make Secure ("finish") AND Remove Secure',
    async entityName => {
      const { ribbon, retrieveRecord, gateFetch } = load();
      canManage(entityName, true);
      retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);
      gateFetch.mockResolvedValue(ownerAnswer(false, OTHER_TEAM, false));

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(true);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
      expect(retrieveRecord).toHaveBeenCalledTimes(1);
      expect(gateFetch).toHaveBeenCalledTimes(1); // both rules share the one owner answer
      const [url, init] = gateFetch.mock.calls[0];
      expect(url).toBe(
        `${BFF}/api/v1/external-access/can-manage-access?recordType=${RECORD_TYPES[entityName]}&recordId=${RECORD_ID}&includeOwner=true`
      );
      expect(init.headers.Authorization).toBe('Bearer token-not-a-credential');
    }
  );

  it('owned by the Secure Record Owners team (the server says so): PROVISIONED — Make Secure hidden', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load();
    canManage('sprk_matter', true);
    retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);
    gateFetch.mockResolvedValue(ownerAnswer(true));

    expect(await ribbon.canMakeSecure(form('sprk_matter'))).toBe(false);
    expect(await ribbon.canRemoveSecure(form('sprk_matter'))).toBe(true);
    expect(gateFetch).toHaveBeenCalledTimes(1);
  });

  it.each(Object.keys(RECORD_TYPES))(
    'round 53 item 2 — a %s owned by ANOTHER team INSIDE the Secure Record business unit is already isolated: Make Secure hidden, Remove Secure shown',
    async entityName => {
      const { ribbon, retrieveRecord, gateFetch } = load();
      canManage(entityName, true);
      retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);
      // The retired default team, before task 144's migration: not the Secure Record Owners team, but inside its BU.
      gateFetch.mockResolvedValue(ownerAnswer(false, 'dddddddd-1111-2222-3333-4444444444de', true));

      expect(await ribbon.canMakeSecure(form(entityName))).toBe(false);
      expect(await ribbon.canRemoveSecure(form(entityName))).toBe(true);
      expect(gateFetch).toHaveBeenCalledTimes(1);
      // Told apart from "could not be told": no warning, an explanation instead.
      expect(consoleWarn).not.toHaveBeenCalled();
      expect(consoleInfo).toHaveBeenCalledWith(
        expect.stringContaining('[Access.Ribbon'),
        expect.stringContaining('isolated already')
      );
    }
  );

  it('round 53 item 2 — the owner placements are ONE frozen constant', () => {
    const { ribbon } = load();

    expect(Object.isFrozen(ribbon.OWNER_PLACEMENT)).toBe(true);
    expect(ribbon.OWNER_PLACEMENT).toEqual({
      SECURE_OWNER_TEAM: 'secure-owner-team',
      OTHER_TEAM_INSIDE: 'other-team-inside',
      OTHER_TEAM_OUTSIDE: 'other-team-outside',
    });
  });

  /**
   * Round 53 item 3: every case answers through a REAL Promise (an `async` mock), so the answer reaches the ribbon's
   * guards — never the catch by accident. Each body is a definite "another team, outside" answer EXCEPT for the one fact
   * its case names, so only the guard that case names can stop it (each pinned by a seed that removes that guard).
   */
  const ABOUT_THIS_RECORD_OUTSIDE = {
    recordId: RECORD_ID,
    canManageAccess: true,
    owningTeamId: OTHER_TEAM,
    ownedBySecureOwnerTeam: false,
    owningTeamInSecureBusinessUnit: false,
  };
  const answerWith = (status: number, body: Record<string, unknown>) =>
    ({ ok: status >= 200 && status < 300, status, json: async () => body }) as unknown as Response;
  const without = (key: string) => {
    const copy: Record<string, unknown> = { ...ABOUT_THIS_RECORD_OUTSIDE };
    delete copy[key];
    return copy;
  };

  it('the positive twin of the table below: that same answer, unaltered, offers Make Secure', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load();
    canManage('sprk_project', true);
    retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);
    gateFetch.mockImplementation(async () => answerWith(200, ABOUT_THIS_RECORD_OUTSIDE));

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(true);
  });

  it.each([
    [
      'ownedBySecureOwnerTeam is null (the server could not tell)',
      () => answerWith(200, { ...ABOUT_THIS_RECORD_OUTSIDE, ownedBySecureOwnerTeam: null }),
    ],
    ['a 200 with ownedBySecureOwnerTeam omitted', () => answerWith(200, without('ownedBySecureOwnerTeam'))],
    ['a non-200, even with an answer-shaped body', () => answerWith(500, ABOUT_THIS_RECORD_OUTSIDE)],
    [
      'an answer about another record',
      () => answerWith(200, { ...ABOUT_THIS_RECORD_OUTSIDE, recordId: 'bbbbbbbb-1111-2222-3333-444444444444' }),
    ],
    [
      'an answer that is not a delegation yes',
      () => answerWith(200, { ...ABOUT_THIS_RECORD_OUTSIDE, canManageAccess: false }),
    ],
    [
      'owningTeamInSecureBusinessUnit is null (the business unit could not be told)',
      () => answerWith(200, { ...ABOUT_THIS_RECORD_OUTSIDE, owningTeamInSecureBusinessUnit: null }),
    ],
    [
      'a 200 with owningTeamInSecureBusinessUnit omitted',
      () => answerWith(200, without('owningTeamInSecureBusinessUnit')),
    ],
    [
      'a body that is not JSON',
      () =>
        ({
          ok: true,
          status: 200,
          json: async () => {
            throw new SyntaxError('Unexpected token < in JSON');
          },
        }) as unknown as Response,
    ],
    [
      'a failed request',
      () => {
        throw new Error('network down');
      },
    ],
  ])(
    'who owns it cannot be told (%s): Make Secure stays hidden, Remove Secure follows the flag',
    async (_case, answer) => {
      const { ribbon, retrieveRecord, gateFetch } = load();
      canManage('sprk_project', true);
      retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);
      gateFetch.mockImplementation(async () => answer());

      expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
      expect(await ribbon.canRemoveSecure(form('sprk_project'))).toBe(true);
      expect(gateFetch).toHaveBeenCalledTimes(1); // asked once; the answer was read, not skipped
    }
  );

  it('with no silent token the owner question is not asked, and Make Secure stays hidden', async () => {
    const { ribbon, retrieveRecord, gateFetch } = load();
    canManage('sprk_project', true);
    win.Spaarke.BffAuth.getToken = jest.fn().mockResolvedValue(null);
    retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);

    expect(await ribbon.canMakeSecure(form('sprk_project'))).toBe(false);
    expect(gateFetch).not.toHaveBeenCalled();
  });

  it.each([
    ['not secure', NOT_SECURE],
    ['flagged, no container', { sprk_issecure: true, sprk_containerid: null, _owninguser_value: null }],
    [
      'flagged, user-owned with a container (a legacy record before task 133’s owner move)',
      {
        sprk_issecure: true,
        sprk_containerid: 'b!its-own-container',
        _owninguser_value: 'eeeeeeee-1111-2222-3333-444444444444',
      },
    ],
  ])('the read alone decides when it can (%s): no owner question is asked', async (_shape, row) => {
    const { ribbon, retrieveRecord, gateFetch } = load();
    canManage('sprk_workassignment', true);
    retrieveRecord.mockResolvedValue(row);

    expect(await ribbon.canMakeSecure(form('sprk_workassignment'))).toBe(true);
    expect(gateFetch).not.toHaveBeenCalled();
  });

  it('after Make Secure the owner is asked again (never a stale answer): the finished record hides Make Secure', async () => {
    const { ribbon, retrieveRecord, gateFetch, openConfirmDialog, authenticatedFetch } = load();
    canManage('sprk_matter', true);
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    authenticatedFetch.mockResolvedValue(response(200, { recordType: 'matter' }));
    retrieveRecord.mockResolvedValue(TEAM_OWNED_WITH_CONTAINER);
    gateFetch.mockResolvedValueOnce(ownerAnswer(false, OTHER_TEAM, false));
    const matter = form('sprk_matter');

    expect(await ribbon.canMakeSecure(matter)).toBe(true);
    await ribbon.makeSecure(matter);
    await settle();
    gateFetch.mockResolvedValueOnce(ownerAnswer(true)); // the call re-owned it to the Secure Record Owners team

    expect(await ribbon.canMakeSecure(matter)).toBe(false);
    expect(gateFetch).toHaveBeenCalledTimes(2);
    expect(retrieveRecord).toHaveBeenCalledTimes(2);
  });
});

/** Round 40 item 1: the Make Secure failures after the flag write that the server answers "the same caller may call again". */
const RETRY_IN_PLACE = [
  'sdap.provision.secure_flag_not_set',
  'sdap.provision.shared_container_not_cleared',
  'sdap.provision.creator_share_failed',
  'sdap.provision.owner_assignment_unverified',
  'sdap.provision.container_creation_failed',
  'sdap.provision.container_not_recorded',
  'sdap.provision.children_incomplete',
  'sdap.provision.files_incomplete',
];

describe('round 40 item 1 — a retryable Make Secure failure offers the same call again, in place', () => {
  it('the list is ONE frozen constant, exactly these codes', () => {
    const { ribbon } = load();

    expect(Object.isFrozen(ribbon.MAKE_SECURE_RETRY_IN_PLACE)).toBe(true);
    expect([...ribbon.MAKE_SECURE_RETRY_IN_PLACE]).toEqual(RETRY_IN_PLACE);
  });

  it.each(RETRY_IN_PLACE)(
    '%s: a confirm dialog with the server message and Make Secure / Cancel; Make Secure repeats the call',
    async reasonCode => {
      const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, addGlobalNotification } = load();
      const detail = `The server's own words for ${reasonCode}. The same caller may call again.`;
      openConfirmDialog
        .mockResolvedValueOnce({ confirmed: true }) // the owner round 27 confirmation
        .mockResolvedValueOnce({ confirmed: true }); // the retry
      authenticatedFetch
        .mockResolvedValueOnce(response(500, { detail, reasonCode }))
        .mockResolvedValueOnce(response(200, { recordType: 'matter' }));
      const matter = form('sprk_matter');

      await ribbon.makeSecure(matter);
      await settle();

      expect(openConfirmDialog).toHaveBeenCalledTimes(2);
      expect(openConfirmDialog.mock.calls[1][0]).toEqual({
        title: 'Make Secure',
        text: detail,
        confirmButtonLabel: 'Make Secure',
        cancelButtonLabel: 'Cancel',
      });
      expect(authenticatedFetch).toHaveBeenCalledTimes(2);
      expect(JSON.parse(authenticatedFetch.mock.calls[1][1].body)).toEqual({
        recordType: 'matter',
        recordId: RECORD_ID,
        transition: 'make-secure',
      });
      expect(openAlertDialog).not.toHaveBeenCalled();
      expect(addGlobalNotification).toHaveBeenCalledWith(
        expect.objectContaining({ message: 'This matter is now secure.' })
      );
      expect(matter.data.refresh).toHaveBeenCalledTimes(2); // re-read after each call
    }
  );

  it('Cancel on the retry calls nothing more and shows nothing else', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog } = load();
    openConfirmDialog.mockResolvedValueOnce({ confirmed: true }).mockResolvedValueOnce({ confirmed: false });
    authenticatedFetch.mockResolvedValue(
      response(500, { detail: 'children remain', reasonCode: 'sdap.provision.children_incomplete' })
    );

    await ribbon.makeSecure(form('sprk_project'));
    await settle();

    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(openAlertDialog).not.toHaveBeenCalled();
  });

  it.each([
    'sdap.provision.creator_share_failed_resumable',
    'sdap.provision.cascade_children_not_restored',
    'sdap.provision.owner_assignment_failed',
    'sdap.provision.owner_assignment_not_applied',
    'sdap.provision.not_record_creator',
  ])('%s (an administrator acts first, or not retryable): the alert, no retry offered', async reasonCode => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog } = load();
    openConfirmDialog.mockResolvedValueOnce({ confirmed: true });
    authenticatedFetch.mockResolvedValue(response(500, { detail: 'an administrator acts first', reasonCode }));

    await ribbon.makeSecure(form('sprk_workassignment'));
    await settle();

    expect(openConfirmDialog).toHaveBeenCalledTimes(1); // the owner copy only
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Make Secure', text: 'an administrator acts first' });
  });

  it('Remove Secure never offers a retry in place (the list is Make Secure’s)', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog } = load();
    openConfirmDialog.mockResolvedValueOnce({ confirmed: true });
    authenticatedFetch.mockResolvedValue(
      response(500, { detail: 'partial', reasonCode: 'sdap.provision.children_incomplete' })
    );

    await ribbon.removeSecure(form('sprk_matter'));
    await settle();

    expect(openConfirmDialog).toHaveBeenCalledTimes(1);
    expect(authenticatedFetch).toHaveBeenCalledTimes(1);
    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Remove Secure', text: 'partial' });
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
    // Round 33 item 1: the transition names the Make Secure path (the server's Write gate, not the creator rule).
    expect(JSON.parse(init.body)).toEqual({ recordType: 'matter', recordId: RECORD_ID, transition: 'make-secure' });
    expect(init.headers.Authorization).toBeUndefined(); // the token is the helper's (ADR-028), never built here
    expect(baseUrl).toBe(BFF);

    expect(matter.data.refresh).toHaveBeenCalledWith(false);
    expect(matter.ui.refreshRibbon).toHaveBeenCalled();
    expect(addGlobalNotification).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'This matter is now secure.' })
    );
  });

  it('a success that skipped nobody shows only the notification', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, addGlobalNotification } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    authenticatedFetch.mockResolvedValue(response(200, { recordType: 'project', skippedPrincipals: [] }));

    await ribbon.makeSecure(form('sprk_project'));
    await settle();

    expect(addGlobalNotification).toHaveBeenCalledWith(
      expect.objectContaining({ message: 'This project is now secure.' })
    );
    expect(openAlertDialog).not.toHaveBeenCalled();
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
    openConfirmDialog.mockResolvedValueOnce({ confirmed: true });
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

  it.each([
    ['sprk_project', 'project'],
    ['sprk_matter', 'matter'],
    ['sprk_workassignment', 'work assignment'],
  ])(
    "round 53 item 1 — caller_rights_unverifiable on a %s: the ribbon's own words ({record} = %s), an alert, no in-place retry; Make Secure stays offered",
    async (entityName, record) => {
      const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, retrieveRecord } = load();
      canManage(entityName, true);
      openConfirmDialog.mockResolvedValueOnce({ confirmed: true });
      authenticatedFetch.mockResolvedValue(
        response(500, { detail: 'operator text', reasonCode: 'sdap.provision.caller_rights_unverifiable' })
      );
      retrieveRecord.mockResolvedValue(NOT_SECURE); // refused before any write: the record is unchanged
      const page = form(entityName);

      await ribbon.makeSecure(page);
      await settle();

      expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Make Secure', text: CALLER_RIGHTS_UNVERIFIABLE(record) });
      expect(openConfirmDialog).toHaveBeenCalledTimes(1); // the owner copy only: a pre-write refusal is not retried in place
      expect(authenticatedFetch).toHaveBeenCalledTimes(1);
      expect(await ribbon.canMakeSecure(page)).toBe(true); // "you may try again": the command is still there
    }
  );

  it('round 53 item 1 — REFUSAL_COPY is ONE frozen constant, verbatim, and only its codes are worded here', () => {
    const { ribbon } = load();

    expect(Object.isFrozen(ribbon.REFUSAL_COPY)).toBe(true);
    expect(ribbon.REFUSAL_COPY).toEqual({
      'sdap.provision.caller_rights_unverifiable': CALLER_RIGHTS_UNVERIFIABLE('{record}'),
    });
    expect(
      ribbon.refusalFor(
        'Make Secure',
        { status: 500, body: { detail: 'server words', reasonCode: 'sdap.provision.x' } },
        'sprk_matter'
      )
    ).toBe('server words');
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

/** Round 29 + round 33 item 5: the wizard's words with {record} — ONE constant in the script, pinned verbatim. */
const SKIPPED = {
  noAccess: (name: string, record: string) =>
    `${name} is on this ${record}'s No Access list, so the ${record} was not shared with them.`,
  unverifiable: (name: string, record: string) =>
    `Whether ${name} may access this ${record} could not be checked, so the ${record} was not shared with them. You can share it with them later from Manage Access.`,
  shareFailed: (name: string, record: string) =>
    `${name} was not given access to this ${record}. You can share it with them later from Manage Access.`,
  generic: (name: string, record: string) => `${name} was not given access to this ${record}.`,
};

const CREATOR_ID = 'cccccccc-1111-2222-3333-444444444444';

describe('Make Secure — the people it was NOT shared with are named (round 33 items 1 and 5: never silent)', () => {
  /** retrieveRecord answers the user read (systemuser fullname) from `map`; any other read is unexpected here. */
  function names(retrieveRecord: jest.Mock, map: Record<string, string | Error>): void {
    retrieveRecord.mockImplementation((entity: string, id: string, options: string) => {
      if (entity !== 'systemuser' || options !== '?$select=fullname') {
        return Promise.reject(new Error(`unexpected read ${entity} ${options}`));
      }
      const answer = map[id];
      return answer instanceof Error ? Promise.reject(answer) : Promise.resolve({ fullname: answer });
    });
  }

  it.each([
    ['sprk_project', 'project'],
    ['sprk_matter', 'matter'],
    ['sprk_workassignment', 'work assignment'],
  ])(
    'a creator on the No Access list of a %s: the success notification, then a warning naming them',
    async (entityName, record) => {
      const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, addGlobalNotification, retrieveRecord } =
        load();
      openConfirmDialog.mockResolvedValue({ confirmed: true });
      names(retrieveRecord, { [CREATOR_ID]: 'Dana Reyes' });
      authenticatedFetch.mockResolvedValue(
        response(200, {
          skippedPrincipals: [
            { systemUserId: CREATOR_ID, reasonCode: 'sdap.provision.principal_no_access', message: 'server prose' },
          ],
        })
      );

      await ribbon.makeSecure(form(entityName));
      await settle();

      expect(addGlobalNotification).toHaveBeenCalledWith(
        expect.objectContaining({ message: `This ${record} is now secure.` })
      );
      expect(openAlertDialog).toHaveBeenCalledWith({
        title: 'Make Secure',
        text: SKIPPED.noAccess('Dana Reyes', record),
      });
      expect(openAlertDialog.mock.calls[0][0].text).not.toContain('server prose');
    }
  );

  it('every skipped person is listed; a name that cannot be read is "Someone"; each reason has its own words', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, retrieveRecord } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    const unchecked = 'dddddddd-1111-2222-3333-444444444444';
    names(retrieveRecord, { [CREATOR_ID]: new Error('read refused'), [unchecked]: 'Sam Ortiz' });
    authenticatedFetch.mockResolvedValue(
      response(200, {
        skippedPrincipals: [
          { systemUserId: CREATOR_ID, reasonCode: 'sdap.provision.principal_share_failed', message: 'x' },
          { systemUserId: unchecked, reasonCode: 'sdap.provision.principal_no_access_unverifiable', message: 'x' },
        ],
      })
    );

    await ribbon.makeSecure(form('sprk_workassignment'));
    await settle();

    expect(openAlertDialog).toHaveBeenCalledTimes(1);
    expect(openAlertDialog.mock.calls[0][0]).toEqual({
      title: 'Make Secure',
      text: [
        SKIPPED.shareFailed('Someone', 'work assignment'),
        SKIPPED.unverifiable('Sam Ortiz', 'work assignment'),
      ].join('\n\n'),
    });
  });

  it.each([
    ['an empty id (no read is made)', '', undefined],
    ['an id whose user reads back with an empty name', CREATOR_ID, ''],
    ['an id whose user reads back with no name', CREATOR_ID, null],
  ])('round 40 item 3 — %s: "Someone", never an empty name', async (_case, systemUserId, fullname) => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, retrieveRecord } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    retrieveRecord.mockResolvedValue({ fullname });
    authenticatedFetch.mockResolvedValue(
      response(200, {
        skippedPrincipals: [{ systemUserId, reasonCode: 'sdap.provision.principal_share_failed', message: 'x' }],
      })
    );

    await ribbon.makeSecure(form('sprk_project'));
    await settle();

    expect(openAlertDialog).toHaveBeenCalledWith({
      title: 'Make Secure',
      text: SKIPPED.shareFailed('Someone', 'project'),
    });
    if (!systemUserId) {
      expect(retrieveRecord).not.toHaveBeenCalled();
    }
  });

  it('round 40 item 3 — a whitespace-only id is no id: "Someone", and no user read is made with it', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, retrieveRecord } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    retrieveRecord.mockResolvedValue({ fullname: 'Should Not Be Read' });
    authenticatedFetch.mockResolvedValue(
      response(200, {
        skippedPrincipals: [{ systemUserId: '   ', reasonCode: 'sdap.provision.principal_no_access', message: 'x' }],
      })
    );

    await ribbon.makeSecure(form('sprk_matter'));
    await settle();

    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Make Secure', text: SKIPPED.noAccess('Someone', 'matter') });
    expect(retrieveRecord).not.toHaveBeenCalled();
  });

  it('round 40 item 3 — the fallback is ONE constant, and describeSkippedPrincipal never fills an empty name', () => {
    const { ribbon } = load();

    expect(ribbon.UNNAMED_PERSON).toBe('Someone');
    expect(ribbon.describeSkippedPrincipal('sdap.provision.principal_no_access', '', 'sprk_matter')).toBe(
      SKIPPED.noAccess('Someone', 'matter')
    );
    expect(
      ribbon.describeSkippedPrincipal('sdap.provision.principal_no_access_unverifiable', '  ', 'sprk_matter')
    ).toBe(SKIPPED.unverifiable('Someone', 'matter'));
  });

  it('a reason code the script does not know: the generic warning, and the code logged — never dropped', async () => {
    const { ribbon, openConfirmDialog, authenticatedFetch, openAlertDialog, retrieveRecord } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    names(retrieveRecord, { [CREATOR_ID]: 'Rui Tanaka' });
    authenticatedFetch.mockResolvedValue(
      response(200, {
        skippedPrincipals: [
          { systemUserId: CREATOR_ID, reasonCode: 'sdap.provision.principal_invented_later', message: 'x' },
        ],
      })
    );

    await ribbon.makeSecure(form('sprk_matter'));
    await settle();

    expect(openAlertDialog).toHaveBeenCalledWith({
      title: 'Make Secure',
      text: SKIPPED.generic('Rui Tanaka', 'matter'),
    });
    expect(consoleError).toHaveBeenCalledWith(
      expect.stringContaining('[Access.Ribbon'),
      expect.stringContaining('for a reason this script does not know'),
      expect.objectContaining({ reasonCode: 'sdap.provision.principal_invented_later' })
    );
  });

  it('is ONE frozen constant, verbatim', () => {
    const { ribbon } = load();

    expect(Object.isFrozen(ribbon.SKIPPED_PRINCIPAL_COPY)).toBe(true);
    expect(ribbon.SKIPPED_PRINCIPAL_COPY).toEqual({
      'sdap.provision.principal_no_access': SKIPPED.noAccess('{name}', '{record}'),
      'sdap.provision.principal_no_access_unverifiable': SKIPPED.unverifiable('{name}', '{record}'),
      'sdap.provision.principal_share_failed': SKIPPED.shareFailed('{name}', '{record}'),
      // Task 114 (owner round 67, owner wording, 2026-10-06): a new key; the three texts above are unchanged.
      'sdap.provision.principal_external_on_restricted':
        "{name} is flagged as an external user and can't be given access to a Restricted record.",
    });
    expect(ribbon.SKIPPED_PRINCIPAL_GENERIC).toBe(SKIPPED.generic('{name}', '{record}'));
  });
});

describe('Remove Secure — the server decides who may (F3), the client shows its answer', () => {
  it('confirms first with the round 33 copy; Cancel calls nothing', async () => {
    const { ribbon, authenticatedFetch, openConfirmDialog } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: false });
    const project = form('sprk_project');

    await ribbon.removeSecure(project);
    await settle();

    expect(openConfirmDialog).toHaveBeenCalledTimes(1);
    expect(openConfirmDialog.mock.calls[0][0]).toEqual({
      title: 'Remove the secure designation from this project?',
      text: REMOVE_PARAGRAPHS('project').join('\n\n'),
      confirmButtonLabel: 'Remove Secure',
      cancelButtonLabel: 'Cancel',
    });
    expect(authenticatedFetch).not.toHaveBeenCalled();
    expect(project.data.refresh).not.toHaveBeenCalled();
  });

  it("a Collaborate holder who is not the creator is refused: the endpoint's F3 message is shown and the form re-read", async () => {
    const { ribbon, authenticatedFetch, openAlertDialog, openConfirmDialog, addGlobalNotification } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    const detail =
      'Only someone with Full Access to this matter, or the person who created it, can remove its secure designation. Nothing was changed.';
    authenticatedFetch.mockResolvedValue(response(403, { detail, reasonCode: 'sdap.unsecure.not_permitted' }));
    const matter = form('sprk_matter');

    await ribbon.removeSecure(matter);
    await settle();

    const [url, init] = authenticatedFetch.mock.calls[0];
    expect(url).toBe(`${BFF}/api/v1/external-access/unsecure-project`);
    expect(JSON.parse(init.body)).toEqual({ recordType: 'matter', recordId: RECORD_ID });
    expect(openConfirmDialog).toHaveBeenCalledTimes(1);
    expect(openAlertDialog).toHaveBeenCalledWith({ title: 'Remove Secure', text: detail });
    expect(addGlobalNotification).not.toHaveBeenCalled();
    expect(matter.data.refresh).toHaveBeenCalledWith(false);
  });

  it('the creator or a Full Access holder succeeds: the outcome is shown and the form refreshed', async () => {
    const { ribbon, authenticatedFetch, addGlobalNotification, openAlertDialog, openConfirmDialog } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
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
    const { ribbon, authenticatedFetch, retrieveRecord, openConfirmDialog } = load();
    openConfirmDialog.mockResolvedValue({ confirmed: true });
    canManage('sprk_matter', true);
    retrieveRecord.mockResolvedValueOnce(PROVISIONED).mockResolvedValueOnce(NOT_SECURE);
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
