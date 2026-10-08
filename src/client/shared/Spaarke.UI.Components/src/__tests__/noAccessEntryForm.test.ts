/**
 * Task 154 (unified-access-control-r2) - the No Access entry form library `sprk_noaccessentry_postsave.js` 1.1.0:
 *  - the OnSave shape check (a preview; the server's malformed-entry rule is the owner): exactly one subject and exactly
 *    one object, no half record pair, a record id the readers can match, a record type the readers evaluate;
 *  - the record id is normalised to the canonical lowercase form without braces (a braced id walled nothing);
 *  - the record picker: choosing a record type opens the platform lookup for that table only, writes the picked id in
 *    canonical form and clears the object organization; a type no reader evaluates opens nothing;
 *  - exactly one object survives a change; the name suggestion never replaces a typed name;
 *  - the post-save notice names the records an entry was not enforced on (owner N5).
 *
 * Runs the REAL script the way the form does (injected into the jsdom window), as accessRibbon.restrictedShare.test.ts
 * does; only `Xrm` is replaced.
 */
import * as fs from 'fs';
import * as path from 'path';

const SCRIPT = path.resolve(__dirname, '../../../../../solutions/webresources/sprk_noaccessentry_postsave.js');

const MATTER_TYPE_REF = 'e8547bb4-8600-f111-8407-7c1e520aa4df';
const DOCUMENT_TYPE_REF = 'f1111111-2222-3333-4444-555555555555';
const MATTER_ID = '1b4e28ba-2fa1-11d2-883f-0016d3cca427';
const ORG = { id: '{AAAAAAAA-0000-0000-0000-000000000001}', name: 'Acme LLP', entityType: 'sprk_organization' };
const CONTACT = { id: '{BBBBBBBB-0000-0000-0000-000000000002}', name: 'Pat Doe', entityType: 'contact' };

type Win = typeof window & { Spaarke?: any; Xrm?: any };
const win = window as Win;

function inject(file: string): void {
  const script = document.createElement('script');
  script.textContent = fs.readFileSync(file, 'utf8');
  document.head.appendChild(script);
}

const flush = async (): Promise<void> => {
  for (let i = 0; i < 6; i++) {
    await new Promise(resolve => setTimeout(resolve, 0));
  }
};

interface FakeAttr {
  value: unknown;
  getValue: () => unknown;
  setValue: (v: unknown) => void;
  addOnChange: (fn: (ctx: unknown) => void) => void;
  removeOnChange: (fn: (ctx: unknown) => void) => void;
  setSubmitMode: jest.Mock;
  handlers: Array<(ctx: unknown) => void>;
}

function makeForm(initial: Record<string, unknown>, saveMode = 1, saveSuccess = true) {
  const names = [
    'sprk_subjectcontact',
    'sprk_subjectorganization',
    'sprk_subjectsystemuser',
    'sprk_objectorganization',
    'sprk_objectrecordtype',
    'sprk_objectrecordid',
    'sprk_name',
  ];
  const attrs: Record<string, FakeAttr> = {};
  const controls: Record<string, any> = {};
  for (const name of names) {
    const attr: FakeAttr = {
      value: initial[name] ?? null,
      handlers: [],
      getValue: () => attr.value,
      setValue: (v: unknown) => {
        attr.value = v;
      },
      addOnChange: fn => attr.handlers.push(fn),
      removeOnChange: fn => {
        attr.handlers = attr.handlers.filter(h => h !== fn);
      },
      setSubmitMode: jest.fn(),
    };
    attrs[name] = attr;
    controls[name] = {
      setNotification: jest.fn(),
      clearNotification: jest.fn(),
      addPreSearch: jest.fn(),
      removePreSearch: jest.fn(),
      addCustomFilter: jest.fn(),
    };
  }

  const preventDefault = jest.fn();
  const formContext = {
    getAttribute: (n: string) => attrs[n] ?? null,
    getControl: (n: string) => controls[n] ?? null,
    ui: { setFormNotification: jest.fn(), clearFormNotification: jest.fn() },
    data: {
      entity: {
        onSave: [] as unknown[],
        onPostSave: [] as unknown[],
        addOnSave: jest.fn(function (this: any, fn: unknown) {
          this.onSave.push(fn);
        }),
        removeOnSave: jest.fn(function (this: any, fn: unknown) {
          this.onSave = this.onSave.filter((h: unknown) => h !== fn);
        }),
        addOnPostSave: jest.fn(function (this: any, fn: unknown) {
          this.onPostSave.push(fn);
        }),
        removeOnPostSave: jest.fn(function (this: any, fn: unknown) {
          this.onPostSave = this.onPostSave.filter((h: unknown) => h !== fn);
        }),
        getId: () => '{AAAAAAAA-1111-2222-3333-444444444444}',
      },
    },
  };
  const ctx = {
    getFormContext: () => formContext,
    getEventArgs: () => ({ preventDefault, getSaveMode: () => saveMode, getIsSaveSuccess: () => saveSuccess }),
  };
  /** Sets a field as the user would and fires its OnChange handlers. */
  const change = (name: string, value: unknown) => {
    attrs[name].value = value;
    attrs[name].handlers.forEach(h => h(ctx));
  };
  return { attrs, controls, formContext, ctx, preventDefault, change };
}

function load(
  options: { lookupResult?: unknown[]; typeRows?: Record<string, string | null>; missingRecord?: boolean } = {}
) {
  // The script's top-level `var Spaarke` is a non-deletable global after the first load; reset its namespace instead.
  if (win.Spaarke) {
    win.Spaarke.NoAccessEntry = undefined;
  }
  const typeRows = options.typeRows ?? { [MATTER_TYPE_REF]: 'sprk_matter', [DOCUMENT_TYPE_REF]: 'sprk_document' };
  const retrieveRecord = jest.fn((table: string, id: string) => {
    if (table === 'sprk_recordtype_ref') {
      return id in typeRows && typeRows[id] !== null
        ? Promise.resolve({ sprk_recordlogicalname: typeRows[id] })
        : Promise.reject(new Error('unreadable'));
    }
    return options.missingRecord
      ? Promise.reject({ errorCode: -2147220969, message: 'sprk_matter With Id = x Does Not Exist' })
      : Promise.resolve({ sprk_name: 'Smith v Jones' });
  });
  const lookupObjects = jest.fn().mockResolvedValue(options.lookupResult ?? []);
  win.Xrm = {
    WebApi: { retrieveRecord, retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('not used')) },
    Utility: { lookupObjects, getEntityMetadata: jest.fn().mockResolvedValue({ PrimaryNameAttribute: 'sprk_name' }) },
  };
  inject(SCRIPT);
  const api = win.Spaarke.NoAccessEntry;
  expect(api.Config.version).toBe('1.1.0'); // the real script ran
  return { api, retrieveRecord, lookupObjects };
}

const lookup = (v: { id: string; name: string; entityType: string }) => [v];
const typeLookup = (id: string, name = 'Matter') => [
  { id: `{${id.toUpperCase()}}`, name, entityType: 'sprk_recordtype_ref' },
];

describe('normalizeRecordId', () => {
  it('strips braces, trims and lower-cases a record id', () => {
    const { api } = load();
    expect(api.normalizeRecordId(`  {${MATTER_ID.toUpperCase()}} `)).toBe(MATTER_ID);
    expect(api.normalizeRecordId(MATTER_ID)).toBe(MATTER_ID);
  });

  it.each([
    ['1b4e28ba2fa111d2883f0016d3cca427'],
    [`(${MATTER_ID})`],
    ['not a record id'],
    ['00000000-0000-0000-0000-000000000000'],
    [''],
    [null],
  ])('refuses %p', raw => {
    const { api } = load();
    expect(api.normalizeRecordId(raw)).toBeNull();
  });
});

describe('shapeProblems', () => {
  const ok = {
    subjectCount: 1,
    hasObjectOrganization: true,
    hasRecordType: false,
    recordId: null,
    recordTypeStatus: null,
  };

  it('passes a well-formed organization entry and a well-formed record entry', () => {
    const { api } = load();
    expect(api.shapeProblems(ok)).toEqual([]);
    expect(
      api.shapeProblems({
        subjectCount: 1,
        hasObjectOrganization: false,
        hasRecordType: true,
        recordId: MATTER_ID,
        recordTypeStatus: 'allowed',
      })
    ).toEqual([]);
  });

  it.each([
    ['no subject', { ...ok, subjectCount: 0 }, 'Choose who is denied'],
    ['two subjects', { ...ok, subjectCount: 2 }, 'Name only one'],
    ['three subjects', { ...ok, subjectCount: 3 }, 'Name only one'],
    ['no object', { ...ok, hasObjectOrganization: false }, 'Choose what they are denied'],
    ['both object forms', { ...ok, hasRecordType: true, recordId: MATTER_ID, recordTypeStatus: 'allowed' }, 'not both'],
    [
      'a type without an id',
      { ...ok, hasObjectOrganization: false, hasRecordType: true, recordTypeStatus: 'allowed' },
      'no record is chosen',
    ],
    ['an id without a type', { ...ok, hasObjectOrganization: false, recordId: MATTER_ID }, 'Choose the record type'],
    [
      'an id that is not a record id',
      { ...ok, hasObjectOrganization: false, hasRecordType: true, recordId: 'abc', recordTypeStatus: 'allowed' },
      'not a record id',
    ],
    [
      'a type no reader evaluates',
      {
        ...ok,
        hasObjectOrganization: false,
        hasRecordType: true,
        recordId: MATTER_ID,
        recordTypeStatus: 'not-allowed',
      },
      'projects, matters and work assignments only',
    ],
    [
      'a type not yet verified',
      { ...ok, hasObjectOrganization: false, hasRecordType: true, recordId: MATTER_ID, recordTypeStatus: 'pending' },
      'could not be checked yet',
    ],
    [
      'an unreadable type',
      { ...ok, hasObjectOrganization: false, hasRecordType: true, recordId: MATTER_ID, recordTypeStatus: 'unreadable' },
      'could not be checked yet',
    ],
  ])('refuses %s', (_label, shape, text) => {
    const { api } = load();
    const problems: string[] = api.shapeProblems(shape);
    expect(problems.length).toBeGreaterThan(0);
    expect(problems.join(' ')).toContain(text);
  });
});

describe('onSave', () => {
  it('never blocks Deactivate, even for a malformed entry (deactivating lifts a wall)', () => {
    const { api } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT), sprk_subjectorganization: lookup(ORG) }, 5);
    api.onLoad(f.ctx);

    api.onSave(f.ctx);

    expect(f.preventDefault).not.toHaveBeenCalled();
  });

  it('still checks a Reactivate', () => {
    const { api } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT), sprk_subjectorganization: lookup(ORG) }, 6);
    api.onLoad(f.ctx);

    api.onSave(f.ctx);

    expect(f.preventDefault).toHaveBeenCalled();
  });

  it('prevents the save of an entry with two subjects and names the problem', () => {
    const { api } = load();
    const f = makeForm({
      sprk_subjectcontact: lookup(CONTACT),
      sprk_subjectorganization: lookup(ORG),
      sprk_objectorganization: lookup(ORG),
    });
    api.onLoad(f.ctx);

    api.onSave(f.ctx);

    expect(f.preventDefault).toHaveBeenCalled();
    const [text, level, id] = f.formContext.ui.setFormNotification.mock.calls.at(-1);
    expect(text).toContain('Name only one');
    expect(level).toBe('ERROR');
    expect(id).toBe('sprk_noaccess_shape');
  });

  it('prevents the save of an entry with no object', () => {
    const { api } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT) });
    api.onLoad(f.ctx);

    api.onSave(f.ctx);

    expect(f.preventDefault).toHaveBeenCalled();
  });

  it('normalises a braced, upper-case record id before the save and lets a well-formed entry save', async () => {
    const { api } = load();
    const f = makeForm({
      sprk_subjectcontact: lookup(CONTACT),
      sprk_objectrecordtype: typeLookup(MATTER_TYPE_REF),
      sprk_objectrecordid: `{${MATTER_ID.toUpperCase()}}`,
      sprk_name: 'typed by the administrator',
    });
    api.onLoad(f.ctx);
    await flush(); // the type is resolved on load

    api.onSave(f.ctx);

    expect(f.preventDefault).not.toHaveBeenCalled();
    expect(f.attrs.sprk_objectrecordid.value).toBe(MATTER_ID);
    expect(f.attrs.sprk_name.value).toBe('typed by the administrator');
  });

  it('refuses a record type no reader evaluates, even with a valid id', async () => {
    const { api } = load();
    const f = makeForm({
      sprk_subjectcontact: lookup(CONTACT),
      sprk_objectrecordtype: typeLookup(DOCUMENT_TYPE_REF, 'Document'),
      sprk_objectrecordid: MATTER_ID,
    });
    api.onLoad(f.ctx);
    await flush();

    api.onSave(f.ctx);

    expect(f.preventDefault).toHaveBeenCalled();
    expect(f.formContext.ui.setFormNotification.mock.calls.at(-1)[0]).toContain(
      'projects, matters and work assignments only'
    );
  });
});

describe('the record picker', () => {
  it('opens the lookup for the chosen type only, writes the canonical id and clears the object organization', async () => {
    const { api, lookupObjects } = load({
      lookupResult: [{ id: `{${MATTER_ID.toUpperCase()}}`, name: 'Smith v Jones', entityType: 'sprk_matter' }],
    });
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT), sprk_objectorganization: lookup(ORG) });
    api.onLoad(f.ctx);

    f.change('sprk_objectrecordtype', typeLookup(MATTER_TYPE_REF));
    await flush();

    expect(lookupObjects).toHaveBeenCalledWith(
      expect.objectContaining({ entityTypes: ['sprk_matter'], allowMultiSelect: false })
    );
    expect(f.attrs.sprk_objectrecordid.value).toBe(MATTER_ID);
    expect(f.attrs.sprk_objectorganization.value).toBeNull();
    expect(f.attrs.sprk_name.value).toBe('Pat Doe – Smith v Jones');
  });

  it('opens nothing and writes no id for a type no reader evaluates', async () => {
    const { api, lookupObjects } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT), sprk_objectrecordid: MATTER_ID });
    api.onLoad(f.ctx);

    f.change('sprk_objectrecordtype', typeLookup(DOCUMENT_TYPE_REF, 'Document'));
    await flush();

    expect(lookupObjects).not.toHaveBeenCalled();
    expect(f.attrs.sprk_objectrecordid.value).toBeNull();
    expect(f.controls.sprk_objectrecordtype.setNotification).toHaveBeenCalledWith(
      expect.stringContaining('projects, matters and work assignments only'),
      expect.any(String)
    );
  });

  it('opens nothing when the record type cannot be read', async () => {
    const { api, lookupObjects } = load({ typeRows: {} });
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT) });
    api.onLoad(f.ctx);

    f.change('sprk_objectrecordtype', typeLookup(MATTER_TYPE_REF));
    await flush();

    expect(lookupObjects).not.toHaveBeenCalled();
    expect(f.controls.sprk_objectrecordtype.setNotification).toHaveBeenCalled();
  });

  it('restricts the record-type lookup to the three evaluated types', () => {
    const { api } = load();
    const f = makeForm({});
    api.onLoad(f.ctx);

    const preSearch = f.controls.sprk_objectrecordtype.addPreSearch.mock.calls[0][0];
    preSearch();

    const [fetchFilter, table] = f.controls.sprk_objectrecordtype.addCustomFilter.mock.calls[0];
    expect(table).toBe('sprk_recordtype_ref');
    for (const t of ['sprk_project', 'sprk_matter', 'sprk_workassignment']) {
      expect(fetchFilter).toContain(`<value>${t}</value>`);
    }
    expect(fetchFilter).not.toContain('sprk_document');
  });
});

describe('a record that does not exist', () => {
  it('on load, warns without blocking (the entry stays deactivatable)', async () => {
    const { api } = load({ missingRecord: true });
    const f = makeForm({
      sprk_subjectcontact: lookup(CONTACT),
      sprk_objectrecordtype: typeLookup(MATTER_TYPE_REF),
      sprk_objectrecordid: MATTER_ID,
    });

    api.onLoad(f.ctx);
    await flush();

    expect(f.controls.sprk_objectrecordid.setNotification).not.toHaveBeenCalled();
    const warning = f.formContext.ui.setFormNotification.mock.calls.find((c: unknown[]) => c[1] === 'WARNING');
    expect(warning?.[0]).toContain('walls nothing');
  });

  it('typed by hand, blocks the save with a field notification', async () => {
    const { api } = load({ missingRecord: true });
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT), sprk_objectrecordtype: typeLookup(MATTER_TYPE_REF) });
    api.onLoad(f.ctx);
    await flush();

    f.change('sprk_objectrecordid', MATTER_ID);
    await flush();

    expect(f.controls.sprk_objectrecordid.setNotification).toHaveBeenCalledWith(
      expect.stringContaining('walls nothing'),
      'sprk_noaccess_sprk_objectrecordid'
    );
  });
});

describe('exactly one object survives', () => {
  it('choosing an organization clears the record type and id', () => {
    const { api } = load();
    const f = makeForm({ sprk_objectrecordtype: typeLookup(MATTER_TYPE_REF), sprk_objectrecordid: MATTER_ID });
    api.onLoad(f.ctx);

    f.change('sprk_objectorganization', lookup(ORG));

    expect(f.attrs.sprk_objectrecordtype.value).toBeNull();
    expect(f.attrs.sprk_objectrecordid.value).toBeNull();
  });

  it('a hand-typed braced id is normalised at once and clears the organization', () => {
    const { api } = load();
    const f = makeForm({ sprk_objectorganization: lookup(ORG) });
    api.onLoad(f.ctx);

    f.change('sprk_objectrecordid', `{${MATTER_ID.toUpperCase()}}`);

    expect(f.attrs.sprk_objectrecordid.value).toBe(MATTER_ID);
    expect(f.attrs.sprk_objectorganization.value).toBeNull();
  });

  it('a hand-typed value that is not a record id gets a blocking field notification', () => {
    const { api } = load();
    const f = makeForm({});
    api.onLoad(f.ctx);

    f.change('sprk_objectrecordid', 'matter 42');

    expect(f.controls.sprk_objectrecordid.setNotification).toHaveBeenCalledWith(
      expect.stringContaining('not a record id'),
      'sprk_noaccess_sprk_objectrecordid'
    );
  });
});

describe('the name suggestion', () => {
  it('fills an empty name from the subject and the object organization (the subgrid "+ New" prefill)', () => {
    const { api } = load();
    const f = makeForm({ sprk_objectorganization: lookup(ORG) });
    api.onLoad(f.ctx);

    f.change('sprk_subjectcontact', lookup(CONTACT));

    expect(f.attrs.sprk_name.value).toBe('Pat Doe – Acme LLP');
  });

  it('never replaces a name the user typed', () => {
    const { api } = load();
    const f = makeForm({ sprk_objectorganization: lookup(ORG), sprk_name: 'Conflict wall 2026-17' });
    api.onLoad(f.ctx);

    f.change('sprk_subjectcontact', lookup(CONTACT));

    expect(f.attrs.sprk_name.value).toBe('Conflict wall 2026-17');
  });
});

describe('registration and the post-save notice', () => {
  it('registers OnSave and OnPostSave from OnLoad', () => {
    const { api } = load();
    const f = makeForm({});

    api.onLoad(f.ctx);

    expect(f.formContext.data.entity.addOnSave).toHaveBeenCalledWith(api.onSave);
    expect(f.formContext.data.entity.addOnPostSave).toHaveBeenCalledWith(api.onPostSave);
  });

  it('names each record the entry was not enforced on because the author lacks Write (owner N5)', () => {
    const { api } = load();

    const text: string = api._summarize({
      removed: [{}],
      notEnforced: [
        { recordType: 'sprk_matter', recordId: 'aaaaaaaa-1111-2222-3333-444444444444', reason: 'author-lacks-write' },
        { recordType: 'sprk_project', recordId: 'bbbbbbbb-1111-2222-3333-444444444444', reason: 'author-lacks-write' },
        { recordType: 'sprk_matter', recordId: 'cccccccc-1111-2222-3333-444444444444', reason: 'not-secure' },
      ],
    });

    expect(text).toContain('Removed 1 direct share(s)');
    expect(text).toContain('Not enforced on Matter aaaaaaaa, Project bbbbbbbb');
    expect(text).not.toContain('cccccccc');
  });
});

describe('task 154 verifier pass 1', () => {
  it('on load, corrects a braced stored id (dirty) and warns that the entry walls nothing until saved', async () => {
    const { api } = load();
    const f = makeForm({
      sprk_subjectcontact: lookup(CONTACT),
      sprk_objectrecordtype: typeLookup(MATTER_TYPE_REF),
      sprk_objectrecordid: `{${MATTER_ID.toUpperCase()}}`,
    });

    api.onLoad(f.ctx);
    await flush();

    expect(f.attrs.sprk_objectrecordid.value).toBe(MATTER_ID);
    const warning = f.formContext.ui.setFormNotification.mock.calls.find(
      (c: unknown[]) => c[2] === 'sprk_noaccess_storedid'
    );
    expect(warning?.[1]).toBe('WARNING');
    expect(warning?.[0]).toContain('walls nothing until it is saved');
  });

  it('on load, tidies an upper-case stored id without a warning (the access checks match it already)', async () => {
    const { api } = load();
    const f = makeForm({
      sprk_subjectcontact: lookup(CONTACT),
      sprk_objectrecordtype: typeLookup(MATTER_TYPE_REF),
      sprk_objectrecordid: MATTER_ID.toUpperCase(),
    });

    api.onLoad(f.ctx);
    await flush();

    expect(f.attrs.sprk_objectrecordid.value).toBe(MATTER_ID);
    expect(
      f.formContext.ui.setFormNotification.mock.calls.some((c: unknown[]) => c[2] === 'sprk_noaccess_storedid')
    ).toBe(false);
  });

  it('on load, clears a blank stored id and warns', async () => {
    const { api } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT), sprk_objectrecordid: '   ' });

    api.onLoad(f.ctx);
    await flush();

    expect(f.attrs.sprk_objectrecordid.value).toBeNull();
    const warning = f.formContext.ui.setFormNotification.mock.calls.find(
      (c: unknown[]) => c[2] === 'sprk_noaccess_storedid'
    );
    expect(warning?.[0]).toContain('blank');
  });

  it('readerMatches mirrors the server rule (measured Dataverse equality)', () => {
    const { api } = load();
    expect(api.readerMatches(MATTER_ID.toUpperCase())).toBe(true);
    expect(api.readerMatches(MATTER_ID + '  ')).toBe(true);
    expect(api.readerMatches(MATTER_ID + '\u3000')).toBe(true);
    expect(api.readerMatches(`{${MATTER_ID}}`)).toBe(false);
    expect(api.readerMatches(' ' + MATTER_ID)).toBe(false);
    expect(api.readerMatches(MATTER_ID + '\t')).toBe(false);
    expect(api.readerMatches(MATTER_ID + '\u00A0')).toBe(false);
  });

  it('a whitespace-only id typed by hand is stored as no id', () => {
    const { api } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT) });
    api.onLoad(f.ctx);

    f.change('sprk_objectrecordid', '   ');

    expect(f.attrs.sprk_objectrecordid.value).toBeNull();
  });

  it('a whitespace-only id on save is cleared and the save is refused for the real problem (no object)', () => {
    const { api } = load();
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT) });
    api.onLoad(f.ctx);
    f.attrs.sprk_objectrecordid.value = '  ';

    api.onSave(f.ctx);

    expect(f.attrs.sprk_objectrecordid.value).toBeNull();
    expect(f.preventDefault).toHaveBeenCalled();
    expect(f.formContext.ui.setFormNotification.mock.calls.at(-1)[0]).toContain('Choose what they are denied');
  });

  it('OnLoad firing twice (as after a save) registers every handler once: the picker opens once', async () => {
    const { api, lookupObjects } = load({
      lookupResult: [{ id: `{${MATTER_ID}}`, name: 'Smith v Jones', entityType: 'sprk_matter' }],
    });
    const f = makeForm({ sprk_subjectcontact: lookup(CONTACT) });

    api.onLoad(f.ctx);
    api.onLoad(f.ctx);
    f.change('sprk_objectrecordtype', typeLookup(MATTER_TYPE_REF));
    await flush();

    expect(lookupObjects).toHaveBeenCalledTimes(1);
    expect((f.formContext.data.entity as any).onSave).toHaveLength(1);
    expect((f.formContext.data.entity as any).onPostSave).toHaveLength(1);
    expect(f.attrs.sprk_objectrecordtype.handlers).toHaveLength(1);
    expect(f.controls.sprk_objectrecordtype.removePreSearch).toHaveBeenCalled();
  });

  it('after a FAILED save, OnPostSave neither enforces nor describes anything', async () => {
    const { api } = load();
    const fetchMock = jest.fn();
    (global as any).fetch = fetchMock;
    const f = makeForm({}, 1, false);
    api.Config.apiBaseUrl = 'https://bff.example';

    api.onPostSave(f.ctx);
    await flush();

    expect(fetchMock).not.toHaveBeenCalled();
    expect(f.formContext.ui.setFormNotification).not.toHaveBeenCalled();
  });

  it('when the sign-in helper is missing, the user sees a notice (not only the console)', async () => {
    const { api } = load();
    const fetchMock = jest.fn();
    (global as any).fetch = fetchMock;
    win.Spaarke.BffAuth = undefined;
    const f = makeForm({});
    api.Config.apiBaseUrl = 'https://bff.example';
    const consoleError = jest.spyOn(console, 'error').mockImplementation(() => undefined);

    api.onPostSave(f.ctx);
    await flush();

    consoleError.mockRestore();
    expect(fetchMock).not.toHaveBeenCalled();
    const [text, level] = f.formContext.ui.setFormNotification.mock.calls.at(-1);
    expect(level).toBe('WARNING');
    expect(text).toContain('enforced within 5 minutes');
  });
});
