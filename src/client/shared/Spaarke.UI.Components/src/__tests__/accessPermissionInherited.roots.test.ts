/**
 * Task 175 (unified-access-control-r2, owner round 87, 2026-10-09) — `sprk_accesspermission_inherited.js` 1.2.0 on the
 * Work Assignment and Project forms: "the parent sets a FLOOR". A work assignment or project filed under a matter or
 * project inherits Secure and Access Permission (the most restrictive across its direct parents) and may never be looser;
 * a user may make it stricter by hand.
 *  - sprk_accesspermission stays EDITABLE: a looser pick is put back (with a warning), an equal or stricter one is kept;
 *  - sprk_issecure (where the form carries it) is disabled while the record is filed;
 *  - the info notification says inherited vs set on this record, plus "Secure is inherited from …";
 *  - a parent that cannot be read locks both (fail safe); no parent: nothing locked.
 * The four child tables keep task 173's lock (accessPermissionInherited.test.ts; one cross-check here).
 *
 * "Filed under" is the server's (SecureRootInheritance): a work assignment's typed sprk_regardingmatter /
 * sprk_regardingproject, or — on both tables — the polymorphic pair sprk_regardingrecordid (a GUID in text) +
 * sprk_regardingrecordtype (lookup to sprk_recordtype_ref) whose type's sprk_recordlogicalname is sprk_matter or
 * sprk_project.
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
const PROJECT_ID = 'dddddddd-1111-2222-3333-444444444444';
const TYPE_MATTER = 'c1c1c1c1-1111-2222-3333-444444444444';
const TYPE_INVOICE = 'c2c2c2c2-1111-2222-3333-444444444444';
const STANDARD = 100000000;
const LIMITED = 100000001;
const RESTRICTED = 100000002;
const NOTIFICATION_ID = 'sprk_accesspermission_inherited';
const WARNING_ID = 'sprk_accesspermission_floor';

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

type ParentRow = { sprk_accesspermission?: number | null; sprk_issecure?: boolean | null };

/**
 * Xrm.WebApi.retrieveRecord: the record type reads (`types`, by id), the parent reads (`parents`, by "table|id"), and
 * the record's OWN saved-filing read (its id as the form passes it, upper case) from `saved`.
 */
function webApi(options: {
  types?: Record<string, string | Error>;
  parents?: Record<string, ParentRow | Error>;
  saved?: Record<string, unknown> | Error;
}) {
  return jest.fn().mockImplementation((table: string, id: string) => {
    if (table === 'sprk_recordtype_ref') {
      const answer = options.types?.[id];
      if (answer === undefined || answer instanceof Error) return Promise.reject(answer ?? new Error('no such type'));
      return Promise.resolve({ sprk_recordlogicalname: answer });
    }
    if (id === RECORD_ID.toUpperCase()) {
      if (options.saved instanceof Error) return Promise.reject(options.saved);
      return Promise.resolve(options.saved ?? {});
    }
    const parent = options.parents?.[`${table}|${id}`];
    if (parent === undefined || parent instanceof Error) return Promise.reject(parent ?? new Error('no such parent'));
    return Promise.resolve(parent);
  });
}

function load(retrieveRecord: jest.Mock) {
  delete win.Spaarke;
  win.Xrm = { WebApi: { retrieveRecord } };
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(SCRIPT, 'utf8');
  document.head.appendChild(script);
  const ns = win.Spaarke.AccessPermissionInherited;
  expect(ns.VERSION).toBe('1.2.0'); // the real script ran
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
  const postSave: Array<() => void> = [];

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
        addOnPostSave: (fn: () => void) => postSave.push(fn),
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
    /** The record is saved (fires OnPostSave). */
    save() {
      Object.keys(dirty).forEach(k => delete dirty[k]);
      postSave.forEach(fn => fn());
    },
  };
}

const flush = async () => {
  for (let i = 0; i < 4; i++) await new Promise(resolve => setTimeout(resolve, 0));
};

/** A work assignment filed (on the form) under one matter, with the typed project lookup empty and no pair. */
function filedWorkAssignment(permission: number | null, extra: { secure?: boolean | null } = {}) {
  return form({
    table: 'sprk_workassignment',
    permission,
    ...extra,
    lookups: { sprk_regardingmatter: { id: `{${MATTER_ID.toUpperCase()}}`, name: 'PAT-176903' }, sprk_regardingproject: null },
    pairId: null,
    pairType: null,
  });
}

const parentReads = (retrieveRecord: jest.Mock) =>
  retrieveRecord.mock.calls.filter(c => c[0] !== 'sprk_recordtype_ref' && c[1] !== RECORD_ID.toUpperCase());

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

describe('the maps the .NET pin reads, and the pure helpers (task 175)', () => {
  it('ROOT_PARENT_LOOKUPS and PAIR_PARENT_TABLES are the server rule; PARENT_LOOKUPS still holds only the four child tables', () => {
    const ns = load(webApi({}));

    expect(ns.ROOT_PARENT_LOOKUPS).toEqual({
      sprk_project: {},
      sprk_workassignment: { sprk_regardingmatter: 'sprk_matter', sprk_regardingproject: 'sprk_project' },
    });
    expect(ns.PAIR_PARENT_TABLES).toEqual(['sprk_matter', 'sprk_project']);
    expect(Object.keys(ns.PARENT_LOOKUPS).sort()).toEqual(['sprk_communication', 'sprk_document', 'sprk_event', 'sprk_todo']);
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

  it('rankOf: Restricted > Limited > Standard; null and unknown values are Standard', () => {
    const ns = load(webApi({}));
    expect([STANDARD, LIMITED, RESTRICTED, null, 42].map(ns.rankOf)).toEqual([0, 1, 2, 0, 0]);
  });

  it('floorOf: the most restrictive parent sets the floor; any secure parent makes it secure', () => {
    const ns = load(webApi({}));
    expect(
      ns.floorOf([
        { name: 'M', permission: LIMITED, secure: false },
        { name: 'P', permission: RESTRICTED, secure: true },
        { name: 'Q', permission: null, secure: false },
      ])
    ).toEqual({ rank: 2, secure: true, floorNames: ['P'], secureNames: ['P'] });
  });
});

describe('sprk_accesspermission_inherited.js 1.2.0 — the floor lock on work assignment and project (task 175)', () => {
  it('a work assignment under a Restricted matter, value Restricted: editable, "inherited" notification, the parent read once', async () => {
    const retrieveRecord = webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: RESTRICTED, sprk_issecure: false } } });
    const ns = load(retrieveRecord);
    const f = filedWorkAssignment(RESTRICTED);

    ns.onLoad(f.executionContext);
    await flush();

    expect(parentReads(retrieveRecord)).toEqual([['sprk_matter', MATTER_ID, '?$select=sprk_accesspermission,sprk_issecure']]);
    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(f.submit.sprk_accesspermission).toBe('dirty');
    expect(f.notifications[NOTIFICATION_ID]).toEqual({
      text: 'Access permission Restricted is inherited from PAT-176903.',
      level: 'INFO',
    });
  });

  it('a LOOSER choice is put back to the previous value, with the warning', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: RESTRICTED, sprk_issecure: false } } }));
    const f = filedWorkAssignment(RESTRICTED);
    ns.onLoad(f.executionContext);
    await flush();

    f.set('sprk_accesspermission', LIMITED);

    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
    expect(f.notifications[WARNING_ID]).toEqual({
      text: 'Access permission cannot be lower than Restricted while this record is filed under PAT-176903.',
      level: 'WARNING',
    });
  });

  it('a STRICTER choice is kept: "set on this record (the minimum from … is …)"; the warning clears', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: false } } }));
    const f = filedWorkAssignment(LIMITED);
    ns.onLoad(f.executionContext);
    await flush();
    f.set('sprk_accesspermission', STANDARD); // looser: put back, warning shown
    expect(f.notifications[WARNING_ID]).toBeDefined();

    f.set('sprk_accesspermission', RESTRICTED);

    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
    expect(f.notifications[WARNING_ID]).toBeUndefined();
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission Restricted is set on this record (the minimum from PAT-176903 is Limited).'
    );
  });

  it('an EQUAL choice is kept: back to "inherited"', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: false } } }));
    const f = filedWorkAssignment(RESTRICTED);
    ns.onLoad(f.executionContext);
    await flush();

    f.set('sprk_accesspermission', LIMITED);

    expect(f.value('sprk_accesspermission')).toBe(LIMITED);
    expect(f.notifications[WARNING_ID]).toBeUndefined();
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission Limited is inherited from PAT-176903.');
  });

  it('the floor comes from the MOST restrictive parent (a Limited matter and a Restricted project)', async () => {
    const ns = load(
      webApi({
        parents: {
          [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: false },
          [`sprk_project|${PROJECT_ID}`]: { sprk_accesspermission: RESTRICTED, sprk_issecure: false },
        },
      })
    );
    const f = form({
      table: 'sprk_workassignment',
      permission: RESTRICTED,
      lookups: {
        sprk_regardingmatter: { id: MATTER_ID, name: 'PAT-176903' },
        sprk_regardingproject: { id: PROJECT_ID, name: 'Acme rollout' },
      },
      pairId: null,
      pairType: null,
    });
    ns.onLoad(f.executionContext);
    await flush();
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission Restricted is inherited from Acme rollout.');

    f.set('sprk_accesspermission', LIMITED);

    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
    expect(f.notifications[WARNING_ID].text).toBe(
      'Access permission cannot be lower than Restricted while this record is filed under Acme rollout.'
    );
  });

  it('a PAIR parent counts: a project whose saved pair names a secure Restricted matter', async () => {
    const retrieveRecord = webApi({
      types: { [TYPE_MATTER]: 'sprk_matter' },
      parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: RESTRICTED, sprk_issecure: true } },
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

    expect(retrieveRecord.mock.calls[0]).toEqual([
      'sprk_project',
      RECORD_ID.toUpperCase(),
      '?$select=sprk_regardingrecordid,_sprk_regardingrecordtype_value',
    ]);
    expect(retrieveRecord).toHaveBeenCalledWith('sprk_recordtype_ref', TYPE_MATTER, '?$select=sprk_recordlogicalname');
    expect(parentReads(retrieveRecord)).toEqual([['sprk_matter', MATTER_ID, '?$select=sprk_accesspermission,sprk_issecure']]);
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission Restricted is inherited from Matter. Secure is inherited from Matter.'
    );
  });

  it('a pair on the form names its parent by sprk_regardingrecordname; the type is read once', async () => {
    const retrieveRecord = webApi({
      types: { [TYPE_MATTER]: 'sprk_matter' },
      parents: {
        [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: false },
        [`sprk_matter|${PROJECT_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: false },
      },
    });
    const ns = load(retrieveRecord);
    const f = form({
      table: 'sprk_project',
      permission: LIMITED,
      pairId: MATTER_ID,
      pairType: { id: `{${TYPE_MATTER.toUpperCase()}}`, name: 'Matter' },
      pairName: 'PAT-176903',
    });
    ns.onLoad(f.executionContext);
    await flush();
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission Limited is inherited from PAT-176903.');

    f.set('sprk_regardingrecordid', PROJECT_ID); // another matter, same type
    await flush();

    expect(retrieveRecord.mock.calls.filter(c => c[0] === 'sprk_recordtype_ref')).toHaveLength(1);
    expect(parentReads(retrieveRecord).map(c => c[1])).toEqual([MATTER_ID, PROJECT_ID]);
    expect(retrieveRecord.mock.calls.filter(c => c[1] === RECORD_ID.toUpperCase())).toHaveLength(0); // all on the form
  });

  it('a pair whose type is an invoice names no parent: nothing locked, no notification, no parent read', async () => {
    const retrieveRecord = webApi({ types: { [TYPE_INVOICE]: 'sprk_invoice' } });
    const ns = load(retrieveRecord);
    const f = form({ table: 'sprk_project', permission: STANDARD, secure: false, pairId: MATTER_ID, pairType: { id: TYPE_INVOICE, name: 'Invoice' } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(parentReads(retrieveRecord)).toHaveLength(0);
  });

  it('a pair whose type cannot be read names no parent (logged)', async () => {
    const ns = load(webApi({ types: { [TYPE_MATTER]: new Error('read refused') } }));
    const f = form({ table: 'sprk_project', permission: STANDARD, secure: false, pairId: MATTER_ID, pairType: { id: TYPE_MATTER, name: 'Matter' } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(consoleWarn).toHaveBeenCalled();
  });

  it('a parent read that FAILS locks both columns (fail safe), with "could not be checked"', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: new Error('read refused') } }));
    const f = filedWorkAssignment(LIMITED, { secure: false });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.submit.sprk_accesspermission).toBe('never');
    expect(f.controls.sprk_issecure[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission and Secure could not be checked against PAT-176903, so they are locked. Reload the record to try again.'
    );
    f.set('sprk_accesspermission', STANDARD); // a bound PCF while locked
    expect(f.value('sprk_accesspermission')).toBe(LIMITED);
  });

  it('a parent whose sprk_issecure reads empty locks too (fail safe)', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: null } } }));
    const f = filedWorkAssignment(LIMITED);

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
  });

  it('a saved filing that cannot be read locks (a parent the form does not carry may be missing)', async () => {
    const ns = load(webApi({ saved: new Error('offline') }));
    const f = form({ table: 'sprk_project', permission: STANDARD });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission and Secure could not be checked against what this record is filed under, so they are locked. Reload the record to try again.'
    );
  });

  it('sprk_issecure stays DISABLED while filed (even under a non-secure floor), and a PCF change to it is put back', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: STANDARD, sprk_issecure: false } } }));
    const f = filedWorkAssignment(STANDARD, { secure: true });
    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_issecure[0].disabled).toBe(true);
    expect(f.submit.sprk_issecure).toBe('never');
    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    f.set('sprk_issecure', false);
    expect(f.value('sprk_issecure')).toBe(true);
  });

  it('Standard under a Standard, non-secure floor: no notification; under a secure Standard floor: "Secure is inherited"', async () => {
    const ns = load(webApi({}));
    expect(ns.floorNotificationText(STANDARD, ns.floorOf([{ name: 'M', permission: null, secure: false }]))).toBeNull();
    expect(ns.floorNotificationText(STANDARD, ns.floorOf([{ name: 'M', permission: STANDARD, secure: true }]))).toBe(
      'Secure is inherited from M.'
    );
  });

  it.each(['sprk_workassignment', 'sprk_project'])('a parentless %s: nothing locked, no notification', async table => {
    const retrieveRecord = webApi({ saved: { sprk_regardingrecordid: null, _sprk_regardingrecordtype_value: null } });
    const ns = load(retrieveRecord);
    const f = form({ table, permission: STANDARD, secure: true });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(false);
    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.submit.sprk_issecure).toBe('dirty');
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(parentReads(retrieveRecord)).toHaveLength(0);
    f.set('sprk_accesspermission', STANDARD); // its own value, edited freely
    expect(f.value('sprk_accesspermission')).toBe(STANDARD);
  });

  it('clearing the last parent lookup: sprk_issecure re-enabled, notifications cleared, the value stays', async () => {
    const ns = load(webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: RESTRICTED, sprk_issecure: true } } }));
    const f = filedWorkAssignment(RESTRICTED, { secure: true });
    ns.onLoad(f.executionContext);
    await flush();

    f.set('sprk_regardingmatter', null);
    await flush();

    expect(f.controls.sprk_issecure[0].disabled).toBe(false);
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
    f.set('sprk_accesspermission', STANDARD); // no floor any more
    expect(f.value('sprk_accesspermission')).toBe(STANDARD);
  });

  it('picking a stricter parent on the form reads it; a value below the new floor says it is set on save', async () => {
    const ns = load(webApi({ parents: { [`sprk_project|${PROJECT_ID}`]: { sprk_accesspermission: RESTRICTED, sprk_issecure: false } } }));
    const f = form({
      table: 'sprk_workassignment',
      permission: STANDARD,
      lookups: { sprk_regardingmatter: null, sprk_regardingproject: null },
      pairId: null,
      pairType: null,
    });
    ns.onLoad(f.executionContext);
    await flush();
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();

    f.set('sprk_regardingproject', [{ id: PROJECT_ID, name: 'Acme rollout' }]);
    await flush();

    expect(f.value('sprk_accesspermission')).toBe(STANDARD); // the server raises it on save; the form writes nothing
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission Restricted is inherited from Acme rollout. It is set when the record is saved.'
    );
    f.set('sprk_accesspermission', LIMITED); // still looser than the floor: put back
    expect(f.value('sprk_accesspermission')).toBe(STANDARD);
  });

  it('after a save the parents are read again', async () => {
    const retrieveRecord = webApi({ parents: { [`sprk_matter|${MATTER_ID}`]: { sprk_accesspermission: LIMITED, sprk_issecure: false } } });
    const ns = load(retrieveRecord);
    const f = filedWorkAssignment(LIMITED);
    ns.onLoad(f.executionContext);
    await flush();

    f.save();
    await flush();

    expect(parentReads(retrieveRecord)).toHaveLength(2);
  });

  it('the four child tables are unchanged: a To Do under a matter is LOCKED and no parent is read', async () => {
    const retrieveRecord = webApi({});
    const ns = load(retrieveRecord);
    const f = form({
      table: 'sprk_todo',
      permission: RESTRICTED,
      lookups: { sprk_regardingmatter: { id: MATTER_ID, name: 'PAT-176903' } },
    });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls.sprk_accesspermission[0].disabled).toBe(true);
    expect(f.submit.sprk_accesspermission).toBe('never');
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission is inherited from PAT-176903.');
    expect(parentReads(retrieveRecord)).toHaveLength(0);
    f.set('sprk_accesspermission', STANDARD);
    expect(f.value('sprk_accesspermission')).toBe(RESTRICTED);
  });
});
