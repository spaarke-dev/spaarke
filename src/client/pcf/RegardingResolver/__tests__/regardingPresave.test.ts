/**
 * `sprk_todo_regarding_presave.js` (v1.3.0) — CREATE-mode staging bridge tests.
 *
 * This web resource is the ONLY thing that gets the FR-26 core-ancestor stamp
 * onto a brand-new child record's INSERT. If it silently skips a stamp the row
 * saves looking perfectly correct while being invisible to everyone whose access
 * comes from that ancestor — so the failure mode is a silent under-grant, and
 * the file has a documented history of subtle staging bugs (SRFR-032/040/050).
 * Hence a test, even though it is a form script.
 *
 * The suite drives the real script (required through `module.exports`, which the
 * IIFE exposes for exactly this) against a fake `formContext` that records every
 * `setValue` in call order — because for FR-26 the ORDER is load-bearing, not
 * just the final values.
 */

/* eslint-disable @typescript-eslint/no-explicit-any, @typescript-eslint/no-require-imports */

const presave = require('../../../webresources/js/sprk_todo_regarding_presave.js');

const PENDING_GLOBAL = '__sprk_regarding_pending__';

const MATTER = '33333333-3333-3333-3333-333333333333';
const PROJECT = '44444444-4444-4444-4444-444444444444';
const COMM = 'c0000001-0000-0000-0000-000000000001';

interface SetCall {
  seq: number;
  field: string;
  value: unknown;
}

/**
 * A form whose attribute set is EXPLICIT. Anything not listed is "not on the
 * form" and `getAttribute` returns null — which is the real Dataverse behaviour
 * and the trap `sprk_regardingrecordurl` fell into for two releases (SRFR-043).
 */
function makeFormContext(fieldsOnForm: string[], formType = 1) {
  const calls: SetCall[] = [];
  let seq = 0;
  const attrs = new Map<string, { setValue: (v: unknown) => void }>();
  for (const f of fieldsOnForm) {
    attrs.set(f, {
      setValue: (v: unknown) => {
        calls.push({ seq: seq++, field: f, value: v });
      },
    });
  }
  const formContext = {
    ui: { getFormType: () => formType },
    data: { entity: { addOnSave: jest.fn() } },
    getAttribute: (name: string) => attrs.get(name) ?? null,
  };
  return { formContext, calls };
}

function execCtx(formContext: unknown) {
  return { getFormContext: () => formContext };
}

/** All lookup attributes a fully-composed `sprk_todo` CREATE form would expose. */
const FULL_FORM = [
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  'sprk_regardingrecordurl',
  'sprk_regardingrecordnumber',
  'sprk_regardingcommunication',
  'sprk_regardingmatter',
  'sprk_regardingproject',
  'sprk_regardingworkassignment',
];

function lastValueFor(calls: SetCall[], field: string): unknown {
  const hits = calls.filter(c => c.field === field);
  return hits.length > 0 ? hits[hits.length - 1].value : undefined;
}

describe('sprk_todo_regarding_presave (FR-26 CREATE-mode staging)', () => {
  let errorSpy: jest.SpyInstance;
  let warnSpy: jest.SpyInstance;

  beforeEach(() => {
    delete (window as any)[PENDING_GLOBAL];
    errorSpy = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'log').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
    delete (window as any)[PENDING_GLOBAL];
  });

  test('version is 1.3.0 (the ancestor-staging contract)', () => {
    expect(presave.VERSION).toBe('1.3.0');
  });

  // -------------------------------------------------------------------------
  // SET
  // -------------------------------------------------------------------------

  test('stages the core-ancestor stamp onto the form so it rides the INSERT', () => {
    const { formContext, calls } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = {
      hostEntity: 'sprk_todo',
      entityType: 'sprk_communication',
      lookupAttribute: 'sprk_regardingcommunication',
      recordId: COMM,
      recordName: 'Re: discovery',
      recordUrl: 'https://x/main.aspx?etn=sprk_communication&id=' + COMM,
      recordNumber: null,
      clearLookups: [],
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    presave.onSave(execCtx(formContext));

    // The relationship lookup AND the access edge both staged.
    expect(lastValueFor(calls, 'sprk_regardingcommunication')).toEqual([
      expect.objectContaining({ id: COMM, entityType: 'sprk_communication' }),
    ]);
    expect(lastValueFor(calls, 'sprk_regardingmatter')).toEqual([
      expect.objectContaining({ id: MATTER, entityType: 'sprk_matter' }),
    ]);
    expect(errorSpy).not.toHaveBeenCalled();
  });

  test('a stamp whose column is not on the form is an ERROR naming the column, and does not block the save', () => {
    // `sprk_regardingmatter` deliberately absent — the exact silent-under-grant
    // shape this handler must never swallow.
    const { formContext, calls } = makeFormContext([
      'sprk_regardingrecordid',
      'sprk_regardingrecordname',
      'sprk_regardingcommunication',
    ]);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_communication',
      lookupAttribute: 'sprk_regardingcommunication',
      recordId: COMM,
      recordName: 'C1',
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    expect(() => presave.onSave(execCtx(formContext))).not.toThrow();

    expect(errorSpy).toHaveBeenCalled();
    const message = String(errorSpy.mock.calls[0][0]);
    expect(message).toContain('sprk_regardingmatter');
    expect(message).toMatch(/FR-26/);
    // The rest of the staging still happened — we degrade, we do not abort.
    expect(lastValueFor(calls, 'sprk_regardingcommunication')).toBeDefined();
  });

  // -------------------------------------------------------------------------
  // REPARENT before the first save
  // -------------------------------------------------------------------------

  test('stages the clear of a previously-picked lookup (reparent before first save)', () => {
    const { formContext, calls } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_communication',
      lookupAttribute: 'sprk_regardingcommunication',
      recordId: COMM,
      recordName: 'C2',
      // The first pick had put a Project stamp on the form; the second pick's
      // ancestor is a Matter, so the Project stamp must be nulled or the INSERT
      // carries both.
      clearLookups: ['sprk_regardingproject', 'sprk_regardingworkassignment'],
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    presave.onSave(execCtx(formContext));

    expect(lastValueFor(calls, 'sprk_regardingproject')).toBeNull();
    expect(lastValueFor(calls, 'sprk_regardingworkassignment')).toBeNull();
    expect(lastValueFor(calls, 'sprk_regardingmatter')).toEqual([expect.objectContaining({ id: MATTER })]);
  });

  test('clears are applied BEFORE sets — a stamp can never be nulled by a clear', () => {
    // Defensive-ordering pin. The shared builder never emits the same column in
    // both lists, but if the order here were reversed, the ordinary case where
    // the chosen target IS a core entity (pick a Matter directly) would null the
    // very access edge it just wrote. Forcing the overlap makes the ordering
    // itself observable from a single attribute's final value.
    const { formContext, calls } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_matter',
      lookupAttribute: 'sprk_regardingmatter',
      recordId: MATTER,
      recordName: 'Smith v. Jones',
      clearLookups: ['sprk_regardingmatter', 'sprk_regardingproject'],
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    presave.onSave(execCtx(formContext));

    const matterCalls = calls.filter(c => c.field === 'sprk_regardingmatter');
    expect(matterCalls.length).toBeGreaterThanOrEqual(2);
    expect(matterCalls[0].value).toBeNull(); // clear ran first…
    // …and the stamp is what survives.
    expect(lastValueFor(calls, 'sprk_regardingmatter')).toEqual([expect.objectContaining({ id: MATTER })]);
  });

  test('a clear for a column not on the form is a warn, not an error', () => {
    const { formContext } = makeFormContext(['sprk_regardingrecordid', 'sprk_regardingmatter']);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_matter',
      lookupAttribute: 'sprk_regardingmatter',
      recordId: MATTER,
      recordName: 'X',
      clearLookups: ['sprk_regardingservicerequest'],
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    presave.onSave(execCtx(formContext));

    // Nothing was staged there to clear, so it is diagnostic only — the inverse
    // of a missing stamp column.
    expect(errorSpy).not.toHaveBeenCalled();
    expect(warnSpy.mock.calls.some(c => String(c[0]).includes('sprk_regardingservicerequest'))).toBe(true);
  });

  // -------------------------------------------------------------------------
  // Gates + hygiene
  // -------------------------------------------------------------------------

  test('UPDATE mode stages nothing (the PCF already wrote via updateRecord)', () => {
    const { formContext, calls } = makeFormContext(FULL_FORM, 2);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_matter',
      lookupAttribute: 'sprk_regardingmatter',
      recordId: MATTER,
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    presave.onSave(execCtx(formContext));
    expect(calls).toHaveLength(0);
  });

  test('read-only / disabled forms stage nothing AND drop the stale pending payload', () => {
    for (const formType of [3, 4]) {
      const { formContext, calls } = makeFormContext(FULL_FORM, formType);
      (window as any)[PENDING_GLOBAL] = {
        entityType: 'sprk_matter',
        lookupAttribute: 'sprk_regardingmatter',
        recordId: MATTER,
        ancestorStamps: [
          {
            entityType: 'sprk_matter',
            entitySet: 'sprk_matters',
            lookupAttribute: 'sprk_regardingmatter',
            recordId: MATTER,
          },
        ],
      };
      presave.onSave(execCtx(formContext));
      expect(calls).toHaveLength(0);
      expect((window as any)[PENDING_GLOBAL]).toBeUndefined();
    }
  });

  test('the pending payload is dropped after staging so a re-save cannot re-apply it', () => {
    const { formContext } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_matter',
      lookupAttribute: 'sprk_regardingmatter',
      recordId: MATTER,
      recordName: 'X',
      ancestorStamps: [
        {
          entityType: 'sprk_matter',
          entitySet: 'sprk_matters',
          lookupAttribute: 'sprk_regardingmatter',
          recordId: MATTER,
        },
      ],
    };

    presave.onSave(execCtx(formContext));
    expect((window as any)[PENDING_GLOBAL]).toBeUndefined();
  });

  test('backward compatible with a v1.2.0-shaped payload (no ancestorStamps / clearLookups)', () => {
    const { formContext, calls } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = {
      hostEntity: 'sprk_todo',
      entityType: 'sprk_matter',
      lookupAttribute: 'sprk_regardingmatter',
      recordId: MATTER,
      recordName: 'Smith v. Jones',
      recordUrl: 'https://x/main.aspx?etn=sprk_matter&id=' + MATTER,
      recordNumber: 'MAT-1',
    };

    expect(() => presave.onSave(execCtx(formContext))).not.toThrow();
    expect(lastValueFor(calls, 'sprk_regardingrecordname')).toBe('Smith v. Jones');
    expect(lastValueFor(calls, 'sprk_regardingmatter')).toEqual([expect.objectContaining({ id: MATTER })]);
    expect(errorSpy).not.toHaveBeenCalled();
  });

  test('an incomplete payload is refused before anything is staged', () => {
    const { formContext, calls } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = { recordName: 'no entityType or recordId' };

    presave.onSave(execCtx(formContext));
    expect(calls).toHaveLength(0);
  });

  test('malformed stamp entries are skipped rather than staged as garbage', () => {
    const { formContext, calls } = makeFormContext(FULL_FORM);
    (window as any)[PENDING_GLOBAL] = {
      entityType: 'sprk_matter',
      lookupAttribute: 'sprk_regardingmatter',
      recordId: MATTER,
      recordName: 'X',
      ancestorStamps: [
        null,
        { entityType: 'sprk_project' }, // no lookupAttribute / recordId
        { lookupAttribute: 'sprk_regardingproject', recordId: PROJECT, entityType: 'sprk_project' },
      ],
    };

    expect(() => presave.onSave(execCtx(formContext))).not.toThrow();
    expect(lastValueFor(calls, 'sprk_regardingproject')).toEqual([expect.objectContaining({ id: PROJECT })]);
  });

  test('onLoad registers the OnSave handler exactly once', () => {
    const { formContext } = makeFormContext(FULL_FORM);
    presave.onLoad(execCtx(formContext));
    expect(formContext.data.entity.addOnSave).toHaveBeenCalledTimes(1);
    expect(formContext.data.entity.addOnSave).toHaveBeenCalledWith(presave.onSave);
  });
});
