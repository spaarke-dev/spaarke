/**
 * Task 173 (unified-access-control-r2, owner rounds 81 and 84, 2026-10-08) — `sprk_accesspermission_inherited.js` 1.0.0,
 * the form library that LOCKS Access Permission on a To Do, Event, Communication or Document while it has a parent
 * ("Access permission is inherited from …"), and leaves it editable on a parentless record. The BFF writes the value;
 * the library only locks and labels.
 *
 * Runs the REAL script the way the form does (injected into the jsdom window), as accessRibbon.restrictedShare.test.ts
 * does; only `Xrm.WebApi` and the form context are replaced.
 *
 * In scope: a parent on the form locks; no parent leaves it editable; a non-parent lookup (a contact) does not lock; an
 * unsaved pick says "set when saved"; clearing the last parent unlocks and re-enables only the controls the library
 * disabled; a bound PCF's change is put back while locked; a saved parent the form does not carry is read once and
 * locks; a failed read leaves the form's own lookups deciding.
 */
import * as fs from 'fs';
import * as path from 'path';

const CLIENT_ROOT = path.resolve(__dirname, '../../../..');
const SCRIPT = path.join(CLIENT_ROOT, 'webresources/js/sprk_accesspermission_inherited.js');

const RECORD_ID = 'aaaaaaaa-1111-2222-3333-444444444444';
const RESTRICTED = 100000002;
const STANDARD = 100000000;
const LIMITED = 100000001;
const NOTIFICATION_ID = 'sprk_accesspermission_inherited';

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

function load(retrieveRecord: jest.Mock = jest.fn().mockResolvedValue({})) {
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

/** A form of `table` with the access permission field and the given lookups (name → value; null = on the form, empty). */
function form(options: {
  table?: string;
  permission?: number | null;
  lookups?: Record<string, { id: string; name: string } | null>;
  controls?: Control[];
  formType?: number;
}) {
  const table = options.table ?? 'sprk_todo';
  let permission = options.permission ?? null;
  const onChange: Record<string, Array<() => void>> = {};
  const lookupValues: Record<string, Array<{ id: string; name: string }> | null> = {};
  const dirty: Record<string, boolean> = {};
  for (const [name, value] of Object.entries(options.lookups ?? {})) {
    lookupValues[name] = value ? [value] : null;
  }
  const controls = options.controls ?? [control()];
  const notifications: Record<string, { text: string; level: string }> = {};
  const postSave: Array<() => void> = [];
  let submitMode = 'dirty';

  const accessAttribute = {
    controls,
    getValue: () => permission,
    setValue: (v: number | null) => {
      permission = v;
    },
    setSubmitMode: (m: string) => {
      submitMode = m;
    },
    addOnChange: (fn: () => void) => (onChange.sprk_accesspermission ??= []).push(fn),
  };

  const context = {
    getAttribute: (name: string) => {
      if (name === 'sprk_accesspermission') return accessAttribute;
      if (!(name in lookupValues)) return null;
      return {
        getValue: () => lookupValues[name],
        getIsDirty: () => !!dirty[name],
        addOnChange: (fn: () => void) => (onChange[name] ??= []).push(fn),
      };
    },
    data: {
      entity: {
        getEntityName: () => table,
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
    submitMode: () => submitMode,
    permission: () => permission,
    /** The user picks or clears a lookup on the form (fires its OnChange). */
    pick(name: string, value: { id: string; name: string } | null) {
      lookupValues[name] = value ? [value] : null;
      dirty[name] = true;
      (onChange[name] ?? []).forEach(fn => fn());
    },
    /** A bound PCF (the TrackingFieldTrio pill) changes the value. */
    pcfSets(value: number) {
      permission = value;
      (onChange.sprk_accesspermission ?? []).forEach(fn => fn());
    },
  };
}

const flush = () => new Promise(resolve => setTimeout(resolve, 0));

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

describe('sprk_accesspermission_inherited.js — lock while the record has a parent (task 173)', () => {
  it('onLoad_ARecordFiledUnderAMatter_IsLockedWithTheInheritedFromNotification', async () => {
    const ns = load();
    const f = form({ permission: RESTRICTED, lookups: { sprk_regardingmatter: { id: '{M1}', name: 'PAT-176903' } } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls[0].disabled).toBe(true);
    expect(f.submitMode()).toBe('never');
    expect(f.notifications[NOTIFICATION_ID]).toEqual({
      text: 'Access permission is inherited from PAT-176903.',
      level: 'INFO',
    });
  });

  it('onLoad_ARecordWithNoParent_StaysEditable', async () => {
    const ns = load();
    const f = form({ permission: LIMITED, lookups: { sprk_regardingmatter: null, sprk_regardingdocument: null } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls[0].disabled).toBe(false);
    expect(f.submitMode()).toBe('dirty');
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
  });

  it('onLoad_ARecordRegardingOnlyAContact_IsNotLocked_AContactIsNotAParent', async () => {
    const ns = load();
    const f = form({ permission: LIMITED, lookups: { sprk_regardingcontact: { id: '{C1}', name: 'Ann Smith' } } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls[0].disabled).toBe(false);
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
  });

  it('pick_AParentPickedOnTheForm_LocksAndSaysTheValueIsSetOnSave_ClearingItUnlocks', async () => {
    const ns = load();
    const f = form({ permission: LIMITED, lookups: { sprk_regardingmatter: null } });
    ns.onLoad(f.executionContext);
    await flush();

    f.pick('sprk_regardingmatter', { id: '{M1}', name: 'Acme v Beta' });
    expect(f.controls[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe(
      'Access permission is inherited from Acme v Beta. It is set when the record is saved.'
    );

    f.pick('sprk_regardingmatter', null);
    expect(f.controls[0].disabled).toBe(false);
    expect(f.submitMode()).toBe('dirty');
    expect(f.notifications[NOTIFICATION_ID]).toBeUndefined();
    expect(f.permission()).toBe(LIMITED); // un-filing keeps the last value, now editable (round 81)
  });

  it('unlock_ReEnablesOnlyTheControlsTheLibraryDisabled_AReadOnlyControlStaysReadOnly', async () => {
    const ns = load();
    const readOnly = control(true);
    const editable = control(false);
    const f = form({ lookups: { sprk_regardingmatter: { id: '{M1}', name: 'M' } }, controls: [readOnly, editable] });
    ns.onLoad(f.executionContext);
    await flush();

    f.pick('sprk_regardingmatter', null);

    expect(editable.disabled).toBe(false);
    expect(readOnly.disabled).toBe(true);
  });

  it('pcfSets_ABoundControlChangesTheValueWhileLocked_TheValueIsPutBack', async () => {
    const ns = load();
    const f = form({ permission: RESTRICTED, lookups: { sprk_regardingmatter: { id: '{M1}', name: 'M' } } });
    ns.onLoad(f.executionContext);
    await flush();

    f.pcfSets(STANDARD);

    expect(f.permission()).toBe(RESTRICTED);
  });

  it('onLoad_ASavedParentTheFormDoesNotCarry_IsReadOnce_AndLocksWithItsName', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({
      _sprk_regardinginvoice_value: 'i-1',
      '_sprk_regardinginvoice_value@OData.Community.Display.V1.FormattedValue': 'INV-0042',
    });
    const ns = load(retrieveRecord);
    const f = form({ permission: STANDARD, lookups: { sprk_regardingmatter: null } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(retrieveRecord).toHaveBeenCalledTimes(1);
    const [table, id, query] = retrieveRecord.mock.calls[0];
    expect(table).toBe('sprk_todo');
    expect(id).toBe(RECORD_ID.toUpperCase());
    expect(query).toContain('_sprk_regardinginvoice_value');
    expect(query).not.toContain('_sprk_regardingmatter_value'); // on the form: the form's value decides
    expect(f.controls[0].disabled).toBe(true);
    expect(f.notifications[NOTIFICATION_ID].text).toBe('Access permission is inherited from INV-0042.');
  });

  it('onLoad_RegisteredTwiceOnTheSameForm_WiresOnce', async () => {
    const retrieveRecord = jest.fn().mockResolvedValue({});
    const ns = load(retrieveRecord);
    const f = form({ permission: RESTRICTED, lookups: { sprk_regardingmatter: { id: '{M1}', name: 'M' } } });

    ns.onLoad(f.executionContext);
    ns.onLoad(f.executionContext);
    await flush();
    f.pick('sprk_regardingmatter', null);

    expect(retrieveRecord).toHaveBeenCalledTimes(1);
    expect(f.controls[0].disabled).toBe(false); // one wiring: the unlock re-enables what the one lock disabled
  });

  it('onLoad_TheSavedParentsCannotBeRead_TheLookupsOnTheFormDecide', async () => {
    const ns = load(jest.fn().mockRejectedValue(new Error('offline')));
    const f = form({ permission: LIMITED, lookups: { sprk_regardingmatter: null } });

    ns.onLoad(f.executionContext);
    await flush();

    expect(f.controls[0].disabled).toBe(false);
    expect(consoleWarn).toHaveBeenCalled();
  });
});
