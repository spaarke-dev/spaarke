/**
 * Task 175 (unified-access-control-r2, owner round 84, 2026-10-09) — `sprk_accesspermission_inherited.js` 1.1.0 on the
 * Work Assignment and Project forms: a work assignment or project filed under a matter or project takes the parent's
 * sprk_issecure and sprk_accesspermission, so both are LOCKED while it has a parent ("Access permission and Secure are
 * inherited from …"); a parentless one keeps and edits its own.
 *
 * "Filed under" is the server's (SecureRootInheritance): a work assignment's typed sprk_regardingmatter /
 * sprk_regardingproject, or — on both tables — the polymorphic pair sprk_regardingrecordid (a GUID in text) +
 * sprk_regardingrecordtype (lookup to sprk_recordtype_ref) whose type's sprk_recordlogicalname is sprk_matter or
 * sprk_project. A pair whose type cannot be read does not lock.
 *
 * Runs the REAL script injected into jsdom, as accessPermissionInherited.test.ts does; only `Xrm.WebApi` and the form
 * context are replaced.
 */
import * as fs from 'fs';
import * as path from 'path';

/* eslint-disable @typescript-eslint/no-explicit-any */

const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_accesspermission_inherited.js');

const RECORD_ID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID = 'bbbbbbbb-1111-2222-3333-444444444444';
const TYPE_MATTER = 'c1c1c1c1-1111-2222-3333-444444444444';
const TYPE_INVOICE = 'c2c2c2c2-1111-2222-3333-444444444444';
const RESTRICTED = 100000002;
const STANDARD = 100000000;
const NOTIFICATION_ID = 'sprk_accesspermission_inherited';

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

/** Xrm.WebApi.retrieveRecord: the record type reads by id, the saved-filing read from `saved` (or a failure). */
function webApi(options: { types?: Record<string, string | Error>; saved?: Record<string, unknown> | Error }) {
  return jest.fn().mockImplementation((table: string, id: string) => {
    if (table === 'sprk_recordtype_ref') {
      const answer = options.types?.[id];
      if (answer === undefined || answer instanceof Error) return Promise.reject(answer ?? new Error('no such type'));
      return Promise.resolve({ sprk_recordlogicalname: answer });
    }
    if (options.saved instanceof Error) return Promise.reject(options.saved);
    return Promise.resolve(options.saved ?? {});
  });
}

function load(retrieveRecord: jest.Mock) {
  delete win.Spaarke;
  win.Xrm = { WebApi: { retrieveRecord } };
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(SCRIPT, 'utf8');
  document.head.appendChild(script);
  const ns = win.Spaarke.AccessPermissionInherited;
  expect(ns.VERSION).toBe('1.1.0'); // the real script ran
  return ns;
}

interface Control {
  disabled: boolean;
  getDisabled: () => boolean;
  setDisabled: (d: boolean) => void;
}

function control(disabled = false): Control {
  const c: Control = {
    disabled,
    getDisabled: () => c.disabled,
    setDisabled: (d: boolean) => {
      c.disabled = d;
    },
  };
  return c;
}

type Lookup = { id: string; name: string } | null;

/**
 * A form of `table`: sprk_accesspermission always; sprk_issecure when `secure` is given; the typed lookups in `lookups`;
 * the pair columns only when given (`pairId` text, `pairType` lookup, `pairName` text).
 */
function form(options: {
  table: string;
  permission?: number | null;
  secure?: boolean | null;
  lookups?: Record<string, Lookup>;
  pairId?: string | null;
  pairType?: Lookup;
  pairName?: string | null;
  formType?: number;
}) {
  const values: Record<string, unknown> = { sprk_accesspermission: options.permission ?? null };
  const dirty: Record<string, boolean> = {};
  const onChange: Record<string, Array<() => void>> = {};
  const controls: Record<string, Control[]> = { sprk_accesspermission: [control()] };
  const submit: Record<string, string> = { sprk_accesspermission: 'dirty' };
  const present = new Set<string>(['sprk_accesspermission']);
  if (options.secure !== undefined) {
    values.sprk_issecure = options.secure;
    controls.sprk_issecure = [control()];
    submit.sprk_issecure = 'dirty';
    present.add('sprk_issecure');
  }
  for (const [name, value] of Object.entries(options.lookups ?? {})) {
    values[name] = value ? [value] : null;
    present.add(name);
  }
  if (options.pairId !== undefined) {
    values.sprk_regardingrecordid = options.pairId;
    present.add('sprk_regardingrecordid');
  }
  if (options.pairType !== undefined) {
    values.sprk_regardingrecordtype = options.pairType ? [options.pairType] : null;
    present.add('sprk_regardingrecordtype');
  }
  if (options.pairName !== undefined) {
    values.sprk_regardingrecordname = options.pairName;
    present.add('sprk_regardingrecordname');
  }
  const notifications: Record<string, { text: string; level: string }> = {};

  const attribute = (name: string) => ({
    controls: controls[name] ?? [],
    getValue: () => values[name],
    setValue: (v: unknown) => {
      values[name] = v;
    },
    getIsDirty: () => !!dirty[name],
    setSubmitMode: (m: string) => {
      submit[name] = m;
    },
    addOnChange: (fn: () => void) => (onChange[name] ??= []).push(fn),
  });
  const attributes: Record<string, ReturnType<typeof attribute>> = {};
  present.forEach(name => (attributes[name] = attribute(name)));

  const context = {
    getAttribute: (name: string) => attributes[name] ?? null,
    data: {
      entity: {
        getEntityName: () => options.table,
        getId: () => `{${RECORD_ID.toUpperCase()}}`,
        addOnPostSave: () => {},
      },
    },
    ui: {
      getFormType: () => options.formType ?? 2,
      setFormNotification: (text: string, level: string, id: string) => {
        notifications[id] = { text, level };
      },
      clearFormNotification: (id: string) => {
        delete notifications[id];
      },
    },
  };

  return {
    executionContext: { getFormContext: () => context },
    controls,
    notifications,
    submit,
    value: (name: string) => values[name],
    /** The user changes a column on the form (fires its OnChange). */
    set(name: string, value: unknown) {
      values[name] = value;
      dirty[name] = true;
      (onChange[name] ?? []).forEach(fn => fn());
    },
    /** A bound PCF (the TrackingFieldTrio pill) changes the value. */
    pcfSets(name: string, value: unknown) {
      values[name] = value;
      (onChange[name] ?? []).forEach(fn => fn());
    },
  };
}

const flush = async () => {
  for (let i = 0; i < 3; i++) await new Promise(resolve => setTimeout(resolve, 0));
};

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

describe('the maps the .NET pin reads (task 175)', () => {
  it('ROOT_PARENT_LOOKUPS and PAIR_PARENT_TABLES are the server rule; PARENT_LOOKUPS still holds only the four child tables', () => {
    const ns = load(webApi({}));

    expect(ns.ROOT_PARENT_LOOKUPS).toEqual({
      sprk_project: {},
      sprk_workassignment: { sprk_regardingmatter: 'sprk_matter', sprk_regardingproject: 'sprk_project' },
    });
    expect(ns.PAIR_PARENT_TABLES).toEqual(['sprk_matter', 'sprk_project']);
    expect(Object.keys(ns.PARENT_LOOKUPS).sort()).toEqual([
      'sprk_communication',
      'sprk_document',
      'sprk_event',
      'sprk_todo',
    ]);
  });

  it.each([
    [MATTER_ID, 'sprk_matter', true],
    [`{${MATTER_ID.toUpperCase()}}`, 'sprk_project', true],
    [MATTER_ID, 'sprk_invoice', false],
    [MATTER_ID, null, false],
    ['PAT-176903', 'sprk_matter', false],
    ['00000000-0000-0000-0000-000000000000', 'sprk_matter', false],
    [null, 'sprk_matter', false],
  ])('pairIsParent(%p, %p) = %p', (id, logicalName, expected) => {
    const ns = load(webApi({}));
    expect(ns.pairIsParent(id, logicalName)).toBe(expected);
  });
});

describe('sprk_accesspermission_inherited.js 1.1.0 — work assignment and project (task 175)', () => {
  it('a work assignment with a typed matter lookup on the form: locked, with the parent name', async () => {
    const ns = load(webApi({}));
    const f = form({
      table: 'sprk_workassignment',
      permission: RESTRICTED,
      lookups: { sprk_regardingmatter: { id: `{${MATTER_ID}}`, name: 'PAT-176903' }, sprk_regardingproject: null },
      pairId: null,
      pairType: null,
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.submit.sprk_accesspermission).toBe('never');
    expect(f.notifications[NOTIFICATION_ID]).toEqual({
      text: 'Access permission and Secure are inherited from PAT-176903.',
      level: 'INFO',
    });
  });

  it('a work assignment whose typed matter lookup is NOT on the form: the saved value is read once and locks', async () => {
    const retrieveRecord = webApi({
      saved: {
        _sprk_regardingmatter_value: MATTER_ID,
        '_sprk_regardingmatter_value@OData.Community.Display.V1.FormattedValue': 'PAT-176903',
        sprk_regardingrecordid: null,
        _sprk_regardingrecordtype_value: null,
      },
    });
    const ns = load(retrieveRecord);
    const f = form({ table: 'sprk_workassignment', permission: STANDARD });

    ns.onLoad(f.executionContext);
    await flush();

    expect(retrieveRecord).toHaveBeenCalledTimes(1);
    const [table, id, query] = retrieveRecord.mock.calls[0];
    expect(table).toBe('sprk_workassignment');
    expect(id).toBe(RECORD_ID.toUpperCase());
    expect(query).toBe(
      '?$select=_sprk_regardingmatter_value,_sprk_regardingproject_value,sprk_regardingrecordid,_sprk_regardingrecordtype_value'
    );
    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission and Secure are inherited from PAT-176903.');
  });

  it('a project whose saved pair names a matter (type sprk_matter): locked, named by the type when no record name is on the form', async () => {
    const retrieveRecord = webApi({
      types: { [TYPE_MATTER]: 'sprk_matter' },
      saved: {
        sprk_regardingrecordid: MATTER_ID,
        _sprk_regardingrecordtype_value: TYPE_MATTER,
        '_sprk_regardingrecordtype_value@OData.Community.Display.V1.FormattedValue': 'Matter',
      },
    });
    const ns = load(retrieveRecord);
    const f = form({ table: 'sprk_project', permission: RESTRICTED });

    ns.onLoad(f.executionContext);
    await flush();

    expect(retrieveRecord.mock.calls[0][2]).toBe('?$select=sprk_regardingrecordid,_sprk_regardingrecordtype_value');
    expect(retrieveRecord).toHaveBeenCalledWith('sprk_recordtype_ref', TYPE_MATTER, '?$select=sprk_recordlogicalname');
    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission and Secure are inherited from Matter.');
  });

  it('a project with the pair ON the form naming a matter: locked, named by sprk_regardingrecordname; the type is read once', async () => {
    const retrieveRecord = webApi({ types: { [TYPE_MATTER]: 'sprk_matter' } });
    const ns = load(retrieveRecord);
    const f = form({
      table: 'sprk_project',
      permission: RESTRICTED,
      pairId: MATTER_ID,
      pairType: { id: `{${TYPE_MATTER.toUpperCase()}}`, name: 'Matter' },
      pairName: 'PAT-176903',
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission and Secure are inherited from PAT-176903.');
    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);

    f.set('sprk_regardingrecordid', 'cccccccc-1111-2222-3333-444444444444'); // another matter, same type
    await flush();
    const typeReads = retrieveRecord.mock.calls.filter(c => c[0] === 'sprk_recordtype_ref');
    expect(typeReads).toHaveLength(1); // cached per type id for this form load
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission and Secure are inherited from PAT-176903. They are set when the record is saved.'
    );
    expect(retrieveRecord.mock.calls.filter(c => c[0] === 'sprk_project')).toHaveLength(0); // every filing column on the form
  });

  it('a project whose pair names an invoice: NOT locked (an invoice is not a parent of a project)', async () => {
    const ns = load(webApi({ types: { [TYPE_INVOICE]: 'sprk_invoice' } }));
    const f = form({
      table: 'sprk_project',
      permission: STANDARD,
      secure: false,
      pairId: MATTER_ID,
      pairType: { id: TYPE_INVOICE, name: 'Invoice' },
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
  });

  it('a pair whose type cannot be read: NOT locked, and logged (the server reverts any edit on a parented record)', async () => {
    const ns = load(webApi({ types: { [TYPE_MATTER]: new Error('read refused') } }));
    const f = form({
      table: 'sprk_workassignment',
      permission: STANDARD,
      lookups: { sprk_regardingmatter: null, sprk_regardingproject: null },
      pairId: MATTER_ID,
      pairType: { id: TYPE_MATTER, name: 'Matter' },
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(consoleWarn).toHaveBeenCalled();
  });

  it('a pair id that is not a GUID does not lock, and no type is read', async () => {
    const retrieveRecord = webApi({ types: { [TYPE_MATTER]: 'sprk_matter' } });
    const ns = load(retrieveRecord);
    const f = form({
      table: 'sprk_project',
      permission: STANDARD,
      pairId: 'PAT-176903',
      pairType: { id: TYPE_MATTER, name: 'Matter' },
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(retrieveRecord.mock.calls.filter(c => c[0] === 'sprk_recordtype_ref')).toHaveLength(0);
  });

  it.each(['sprk_workassignment', 'sprk_project'])('a parentless %s stays editable (its own value)', async table => {
    const ns = load(webApi({ saved: { sprk_regardingrecordid: null, _sprk_regardingrecordtype_value: null } }));
    const f = form({ table, permission: STANDARD, secure: true });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.submit.sprk_accesspermission).toBe('dirty');
    expect(f.submit.sprk_issecure).toBe('dirty');
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
  });

  it('sprk_issecure on the form is locked too: disabled, never submitted', async () => {
    const ns = load(webApi({}));
    const f = form({
      table: 'sprk_workassignment',
      permission: RESTRICTED,
      secure: true,
      lookups: { sprk_regardingmatter: { id: MATTER_ID, name: 'PAT-176903' }, sprk_regardingproject: null },
      pairId: null,
      pairType: null,
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_issecure[0].disabled).toBe(true);
    expect(f.submit.sprk_issecure).toBe('never');
    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.submit.sprk_accesspermission).toBe('never');
  });

  it('a change by a bound PCF while locked is put back, on both columns', async () => {
    const ns = load(webApi({}));
    const f = form({
      table: 'sprk_workassignment',
      permission: RESTRICTED,
      secure: true,
      lookups: { sprk_regardingmatter: { id: MATTER_ID, name: 'PAT-176903' }, sprk_regardingproject: null },
      pairId: null,
      pairType: null,
    });
    ns.onLoad(f.executionContext);
    await flush();

    f.pcfSets('sprk_accesspermission', STANDARD);
    f.pcfSets('sprk_issecure', false);

    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
    expect(f.value('sprk_issecure')).toBe(true);
  });

  it('clearing the last parent lookup unlocks both columns; the values stay, now editable', async () => {
    const ns = load(webApi({}));
    const f = form({
      table: 'sprk_workassignment',
      permission: RESTRICTED,
      secure: true,
      lookups: { sprk_regardingmatter: { id: MATTER_ID, name: 'PAT-176903' }, sprk_regardingproject: null },
      pairId: null,
      pairType: null,
    });
    ns.onLoad(f.executionContext);
    await flush();

    f.set('sprk_regardingmatter', null);

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.submit.sprk_accesspermission).toBe('dirty');
    expect(f.submit.sprk_issecure).toBe('dirty');
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
    expect(f.value('sprk_issecure')).toBe(true);
  });

  it('picking a project in the typed lookup locks with the pending text; the four child tables keep their own wording', async () => {
    const ns = load(webApi({}));
    const f = form({
      table: 'sprk_workassignment',
      permission: STANDARD,
      lookups: { sprk_regardingmatter: null, sprk_regardingproject: null },
      pairId: null,
      pairType: null,
    });
    ns.onLoad(f.executionContext);
    await flush();

    f.set('sprk_regardingproject', [{ id: MATTER_ID, name: 'Acme rollout' }]);

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission and Secure are inherited from Acme rollout. They are set when the record is saved.'
    );
    expect(ns.notificationText(['M'], false)).toBe('Access permission is inherited from M.');
  });

  it('a parent whose name is not known is not named (never "inherited from .")', () => {
    const ns = load(webApi({}));

    expect(ns.notificationText([''], false)).toBe('Access permission is inherited from its parent record.');
    expect(ns.notificationText(['', 'PAT-1'], false, true)).toBe(
      'Access permission and Secure are inherited from PAT-1.'
    );
  });

  it('the saved filing cannot be read: only the columns on the form decide (logged)', async () => {
    const ns = load(webApi({ saved: new Error('offline') }));
    const f = form({ table: 'sprk_project', permission: STANDARD });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(consoleWarn).toHaveBeenCalled();
  });
});
