/**
 * Task 114 (unified-access-control-r2, owner round 67, 2026-10-06) — the Restricted record and the platform's Share
 * command, in the two Access web resources:
 *  - `sprk_access_ribbon.js` 1.6.0 `isShareAllowed` (amendment 4(a)): the EnableRule appended to the platform's own form
 *    Share command hides it on a Restricted record (sprk_accesspermission = 100000002), from the form's field when the
 *    form carries it, else from ONE saved-value read; a read that fails hides it (fail closed);
 *  - `sprk_assignedaccess_postsave.js` 1.1.0: a change to that field refreshes the command bar, and the sync summary says
 *    when external users' shares were removed from a Restricted record (amendment 3) or kept as a secure record's last
 *    reader.
 *
 * Runs the REAL scripts the way the form does (injected into the jsdom window), as accessRibbon.secureCommands.test.ts
 * does; only `Xrm` and `Spaarke.BffAuth` are replaced.
 */
import * as fs from 'fs';
import * as path from 'path';


const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const RIBBON_SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_access_ribbon.js');
const ASSIGNED_ACCESS_SCRIPT = path.resolve(CLIENT_ROOT, '../solutions/webresources/sprk_assignedaccess_postsave.js');

const RECORD_ID = 'aaaaaaaa-1111-2222-3333-444444444444';
const RESTRICTED = 100000002;
const LIMITED = 100000001;

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

function inject(file: string): void {
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(file, 'utf8');
  document.head.appendChild(script);
}

function load(retrieveRecord: jest.Mock = jest.fn()) {
  delete win.Spaarke;
  win.Xrm = { WebApi: { retrieveRecord, retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('not used')) } };
  win.Spaarke = { BffAuth: { getToken: jest.fn().mockResolvedValue(null) } };
  inject(ASSIGNED_ACCESS_SCRIPT);
  inject(RIBBON_SCRIPT);
  expect(win.Spaarke.Access.Ribbon.VERSION).toBe('1.6.0'); // the real script ran
  expect(win.Spaarke.AssignedAccess.Config.version).toBe('1.1.0');
  return { ribbon: win.Spaarke.Access.Ribbon, assigned: win.Spaarke.AssignedAccess, retrieveRecord };
}

/** A form context; `permission` undefined = the field is not on the form. */
function form(permission?: number | null, id: string = RECORD_ID) {
  const onChange: Array<() => void> = [];
  const attribute =
    permission === undefined
      ? null
      : { getValue: () => permission, addOnChange: (fn: () => void) => onChange.push(fn) };
  return {
    onChange,
    context: {
      getAttribute: (name: string) => (name === 'sprk_accesspermission' ? attribute : null),
      data: {
        entity: {
          getEntityName: () => 'sprk_matter',
          getId: () => (id ? `{${id.toUpperCase()}}` : ''),
          addOnPostSave: jest.fn(),
        },
      },
      ui: { refreshRibbon: jest.fn() },
    },
  };
}

let consoleWarn: jest.SpyInstance;
let consoleError: jest.SpyInstance;

beforeEach(() => {
  consoleWarn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  consoleError = jest.spyOn(console, 'error').mockImplementation(() => {});
});

afterEach(() => {
  consoleWarn.mockRestore();
  consoleError.mockRestore();
});

describe('isShareAllowed — the platform Share command on a Restricted record (amendment 4(a))', () => {
  it('reads the form field when the form carries it: Restricted hides Share, anything else shows it', () => {
    const { ribbon, retrieveRecord } = load();

    expect(ribbon.isShareAllowed(form(RESTRICTED).context)).toBe(false);
    expect(ribbon.isShareAllowed(form(LIMITED).context)).toBe(true);
    expect(ribbon.isShareAllowed(form(100000000).context)).toBe(true);
    expect(ribbon.isShareAllowed(form(null).context)).toBe(true); // blank = Standard
    expect(retrieveRecord).not.toHaveBeenCalled();
  });

  it('without the field on the form, reads the saved value once', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({ sprk_accesspermission: RESTRICTED });
    const { ribbon } = load(retrieveRecord);

    await expect(ribbon.isShareAllowed(form(undefined).context)).resolves.toBe(false);
    expect(retrieveRecord).toHaveBeenCalledWith('sprk_matter', RECORD_ID, '?$select=sprk_accesspermission');

    retrieveRecord.mockResolvedValue({ sprk_accesspermission: null });
    await expect(ribbon.isShareAllowed(form(undefined).context)).resolves.toBe(true);
  });

  it('a read that fails hides Share (fail closed — Manage Access "+ User" is still there)', async () => {
    const { ribbon } = load(jest.fn().mockRejectedValue(new Error('403')));

    await expect(ribbon.isShareAllowed(form(undefined).context)).resolves.toBe(false);
  });

  it('an unsaved record answers true (the platform keeps Share off a new form) and makes no read', () => {
    const retrieveRecord = jest.fn();
    const { ribbon } = load(retrieveRecord);

    expect(ribbon.isShareAllowed(form(undefined, '').context)).toBe(true);
    expect(retrieveRecord).not.toHaveBeenCalled();
  });
});

describe('isShareAllowedForSelection — the platform grid / subgrid Share command (follow-up)', () => {
  const OTHER = 'bbbbbbbb-1111-2222-3333-444444444444';

  it('hides Share when ANY selected row is Restricted, and reads the rows in ONE query', async () => {
    const retrieveMultiple = jest.fn().mockResolvedValue({
      entities: [
        { sprk_matterid: RECORD_ID, sprk_accesspermission: 100000000 },
        { sprk_matterid: OTHER, sprk_accesspermission: RESTRICTED },
      ],
    });
    const { ribbon } = load();
    win.Xrm.WebApi.retrieveMultipleRecords = retrieveMultiple;

    await expect(ribbon.isShareAllowedForSelection([`{${RECORD_ID.toUpperCase()}}`, OTHER], 'sprk_matter')).resolves.toBe(false);
    expect(retrieveMultiple).toHaveBeenCalledTimes(1);
    expect(retrieveMultiple.mock.calls[0][0]).toBe('sprk_matter');
    expect(retrieveMultiple.mock.calls[0][1]).toBe(
      `?$select=sprk_matterid,sprk_accesspermission&$filter=(sprk_matterid eq ${RECORD_ID} or sprk_matterid eq ${OTHER})`
    );
  });

  it('shows Share when no selected row is Restricted (blank = Standard)', async () => {
    const { ribbon } = load();
    win.Xrm.WebApi.retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [
        { sprk_matterid: RECORD_ID, sprk_accesspermission: null },
        { sprk_matterid: OTHER, sprk_accesspermission: LIMITED },
      ],
    });

    await expect(ribbon.isShareAllowedForSelection([RECORD_ID, OTHER], 'sprk_matter')).resolves.toBe(true);
  });

  it('a selected row that does not come back, or a failed read, hides Share (fail closed)', async () => {
    const { ribbon } = load();
    win.Xrm.WebApi.retrieveMultipleRecords = jest.fn().mockResolvedValue({
      entities: [{ sprk_matterid: RECORD_ID, sprk_accesspermission: 100000000 }],
    });
    await expect(ribbon.isShareAllowedForSelection([RECORD_ID, OTHER], 'sprk_matter')).resolves.toBe(false);

    win.Xrm.WebApi.retrieveMultipleRecords = jest.fn().mockRejectedValue(new Error('403'));
    await expect(ribbon.isShareAllowedForSelection([RECORD_ID], 'sprk_matter')).resolves.toBe(false);
  });

  it('nothing selected answers true with no read; a large selection is read in batches of 50', async () => {
    const { ribbon } = load();
    const retrieveMultiple = jest.fn().mockImplementation((_entity: string, query: string) => {
      const ids = Array.from(query.matchAll(/sprk_matterid eq ([0-9a-f-]{36})/g)).map(m => m[1]);
      return Promise.resolve({ entities: ids.map(id => ({ sprk_matterid: id, sprk_accesspermission: 100000000 })) });
    });
    win.Xrm.WebApi.retrieveMultipleRecords = retrieveMultiple;

    expect(ribbon.isShareAllowedForSelection([], 'sprk_matter')).toBe(true);
    expect(retrieveMultiple).not.toHaveBeenCalled();

    const many = Array.from({ length: 120 }, (_, i) => `00000000-0000-0000-0000-${String(i).padStart(12, '0')}`);
    await expect(ribbon.isShareAllowedForSelection(many, 'sprk_matter')).resolves.toBe(true);
    expect(retrieveMultiple).toHaveBeenCalledTimes(3);
  });
});

describe('assignedaccess_postsave.js 1.1.0 — task 114', () => {
  it('onLoad refreshes the command bar when Access Permission changes', () => {
    const { assigned } = load();
    win.Spaarke.AssignedAccess.getApiBaseUrl = jest.fn().mockResolvedValue('https://bff.example.test');
    const f = form(100000000);

    assigned.onLoad({ getFormContext: () => f.context });
    expect(f.onChange).toHaveLength(1);
    f.onChange[0]();

    expect(f.context.ui.refreshRibbon).toHaveBeenCalledTimes(1);
  });

  it('the summary says how many external users lost access to a Restricted record, and when nobody internal can open it', () => {
    const { assigned } = load();

    const text = assigned.summarize({
      assignedAccess: { entries: [] },
      noAccess: [],
      restrictedExternal: { outcome: 'evaluated', removed: ['u1', 'u2'], failures: [], noInternalReader: true },
    });

    expect(text).toContain('Removed access for 2 external users: this record is Restricted to internal users.');
    expect(text).toContain('Nobody internal can open this record now');
    expect(text).toContain('Ask an administrator to share it with an internal user.');
  });

  it('says when the record is OWNED by a user flagged external (an administrator reassigns it)', () => {
    const { assigned } = load();

    const text = assigned.summarize({
      assignedAccess: { entries: [] },
      noAccess: [],
      restrictedExternal: { outcome: 'evaluated', removed: [], failures: [], ownerIsExternal: 'u9' },
    });

    expect(text).toContain('owned by a user flagged as external');
    expect(text).toContain('reassign it to an internal owner');
  });

  it('says nothing about it when nothing was removed or kept (and for an older response without the field)', () => {
    const { assigned } = load();

    expect(assigned.summarize({ assignedAccess: { entries: [] }, noAccess: [] })).toBe('');
    expect(
      assigned.summarize({
        assignedAccess: { entries: [] },
        noAccess: [],
        restrictedExternal: { outcome: 'not-restricted', removed: [], failures: [], noInternalReader: false },
      })
    ).toBe('');
  });
});
