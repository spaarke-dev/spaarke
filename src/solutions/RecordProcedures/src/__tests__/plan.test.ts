import { executePlan, planProcedure, planRule, procedureChoices, currentProcedureChoice, currentRuleChoice, keepBlocker, excludeBlocker, type Plan } from '../components/wizards/plan';
import type { OverridesApi, WriteFailure } from '../services/overridesApi';
import { FENWICK, MATTER, V, mark, override, procedure, rule } from './fixtures';

const parent = { ...MATTER, distance: 1 };
const ownPin = override({ rowId: 'own-pin', mode: 'Pinned', chainRecord: MATTER, pinnedVersion: V.rate1, reason: 'old' });
const ownExcl = override({ rowId: 'own-excl', mode: 'Excluded', chainRecord: MATTER, reason: 'why' });
const ownFollow = override({ rowId: 'own-follow', mode: 'FollowLatest', chainRecord: MATTER });
const parentPin = override({ rowId: 'par-pin', mode: 'Pinned', chainRecord: parent, pinnedVersion: V.rate1 });
const parentFollow = override({ rowId: 'par-follow', mode: 'FollowLatest', chainRecord: parent });

const firmProc = (overrides = [] as ReturnType<typeof override>[]) => procedure({ id: 'p', name: 'P', appliesTo: 'Firm', firm: FENWICK, overrides });
const manualProc = (overrides = [] as ReturnType<typeof override>[]) =>
  procedure({ id: 'm', name: 'M', appliesTo: 'AssignedRecordsOnly', overrides, sources: [{ kind: 'ManualOwn', winning: true, distance: 0, via: null, mode: 'FollowLatest', rowId: 'x' }] });

const kinds = (p: Plan) => p.steps.map(s => s.kind);

describe('the choices a Procedure row offers', () => {
  it('offers Follow latest and Pin for every row, and Exclude unless the Procedure comes only from this record', () => {
    expect(procedureChoices(firmProc())).toEqual(['follow', 'pin', 'exclude']);
    expect(procedureChoices(manualProc([ownFollow]))).toEqual(['follow', 'pin', 'remove']);
  });

  it('offers As set on the parent only where a parent has a row, and Remove only for a Procedure assigned here with no parent row', () => {
    expect(procedureChoices(firmProc([parentPin]))).toEqual(['parent', 'follow', 'pin', 'exclude']);
    expect(procedureChoices(manualProc([ownFollow, parentFollow]))).toEqual(['parent', 'follow', 'pin']);
    expect(procedureChoices(firmProc([ownPin]))).not.toContain('remove');
  });

  it('keeps Exclude on a row that is excluded already, even for a Procedure assigned only here', () => {
    expect(procedureChoices(manualProc([ownExcl]))).toContain('exclude');
  });

  it('never offers a Change (the type has no such choice)', () => {
    for (const p of [firmProc(), manualProc([ownFollow]), firmProc([parentPin, ownPin])]) expect(procedureChoices(p).map(String)).not.toContain('change');
  });

  it('names what the record does now', () => {
    expect(currentProcedureChoice(firmProc())).toBe('follow');
    expect(currentProcedureChoice(firmProc([ownPin]))).toBe('pin');
    expect(currentProcedureChoice(firmProc([ownExcl]))).toBe('exclude');
    expect(currentProcedureChoice(firmProc([parentPin]))).toBe('parent');
    expect(currentProcedureChoice(firmProc([parentPin, ownPin]))).toBe('pin');
  });
});

describe('the calls a Procedure choice makes', () => {
  it('Pin creates one Pinned row with the version and the reason', () => {
    expect(planProcedure(firmProc(), 'pin', { versionId: 'v-1', reason: ' Grandfathered ', pinnable: ['v-1'] }).steps).toEqual([
      { kind: 'assign', procedureId: 'p', mode: 'Pinned', pinnedVersionId: 'v-1', reason: 'Grandfathered' },
    ]);
  });

  it('Exclude creates one Excluded row with the reason', () => {
    expect(planProcedure(firmProc(), 'exclude', { reason: 'no travel' }).steps).toEqual([{ kind: 'assign', procedureId: 'p', mode: 'Excluded', reason: 'no travel' }]);
  });

  it('a Pin needs a version AND a reason, an Exclude a reason: without them the plan is empty', () => {
    expect(planProcedure(firmProc(), 'pin', { versionId: 'v-1', reason: '  ' }).steps).toEqual([]);
    expect(planProcedure(firmProc(), 'pin', { reason: 'r' }).steps).toEqual([]);
    expect(planProcedure(firmProc(), 'exclude', { reason: '' }).steps).toEqual([]);
  });

  it('changing an existing override is a removal and then a create (the route refuses a second row)', () => {
    const p = planProcedure(firmProc([ownPin]), 'exclude', { reason: 'r' });
    expect(kinds(p)).toEqual(['remove', 'assign']);
    expect(p.steps[0]).toEqual({ kind: 'remove', overrideId: 'own-pin' });
  });

  it('the same choice again changes nothing', () => {
    expect(planProcedure(firmProc([ownPin]), 'pin', { versionId: V.rate1.id, reason: 'old', pinnable: [V.rate1.id] }).steps).toEqual([]);
    expect(planProcedure(firmProc([ownExcl]), 'exclude', { reason: 'why' }).steps).toEqual([]);
    expect(planProcedure(firmProc(), 'follow', {}).steps).toEqual([]);
    expect(planProcedure(manualProc([ownFollow]), 'follow', {}).steps).toEqual([]);
  });

  it('the same version with a new reason is a change', () => {
    expect(kinds(planProcedure(firmProc([ownPin]), 'pin', { versionId: V.rate1.id, reason: 'new reason', pinnable: [V.rate1.id] }))).toEqual(['remove', 'assign']);
  });

  it('Follow latest returns a Procedure to its source by removing the row, where the row does no work', () => {
    expect(planProcedure(firmProc([ownPin]), 'follow', {}).steps).toEqual([{ kind: 'remove', overrideId: 'own-pin' }]);
    expect(planProcedure(firmProc([ownExcl]), 'follow', {}).steps).toEqual([{ kind: 'remove', overrideId: 'own-excl' }]);
  });

  it('Follow latest keeps a FollowLatest row where it does work: a Procedure assigned only here, or one that overrides a parent pin', () => {
    expect(planProcedure(manualProc([{ ...ownPin }]), 'follow', {}).steps).toEqual([{ kind: 'remove', overrideId: 'own-pin' }, { kind: 'assign', procedureId: 'm', mode: 'FollowLatest' }]);
    expect(planProcedure(firmProc([parentPin]), 'follow', {}).steps).toEqual([{ kind: 'assign', procedureId: 'p', mode: 'FollowLatest' }]);
    // A parent that itself follows latest needs no row here.
    expect(planProcedure(firmProc([parentFollow]), 'follow', {}).steps).toEqual([]);
  });

  it('As set on the parent and Remove from this record are the removal of the record\'s own row, or nothing', () => {
    expect(planProcedure(firmProc([parentPin, ownExcl]), 'parent', {}).steps).toEqual([{ kind: 'remove', overrideId: 'own-excl' }]);
    expect(planProcedure(firmProc([parentPin]), 'parent', {}).steps).toEqual([]);
    expect(planProcedure(manualProc([ownFollow]), 'remove', {}).steps).toEqual([{ kind: 'remove', overrideId: 'own-follow' }]);
    expect(planProcedure(manualProc([]), 'remove', {}).steps).toEqual([]);
  });

  it('no plan ever calls the change route (the step kinds are remove, assign and excludeRule only)', () => {
    const all = (['parent', 'follow', 'pin', 'exclude', 'remove'] as const).flatMap(c =>
      [firmProc(), firmProc([ownPin]), firmProc([parentPin, ownExcl]), manualProc([ownFollow])].flatMap(p => planProcedure(p, c, { versionId: 'v', reason: 'r' }).steps)
    );
    for (const s of all) expect(['remove', 'assign', 'excludeRule']).toContain(s.kind);
  });
});

describe('the calls a rule choice makes', () => {
  const excluded = (distance: number, id: string | null) =>
    rule({ policyId: 'r1', code: 'R1', inForce: false, marks: [mark('Excluded', { reason: 'because', firm: null, distance, chainRecord: distance ? parent : MATTER, overrideRowId: id })] });
  const changed = (distance: number) =>
    rule({ policyId: 'rr', code: 'R1', recordRule: true, marks: [mark('Changed', { reason: 'staffing', distance, chainRecord: distance ? parent : MATTER, overrideRowId: 'chg-row', baseParameterSummary: '11 h' })] });

  it('Exclude creates one exclusion of the rule\'s policy with the reason and, when given, the firm', () => {
    const r = rule({ policyId: 'r1', code: 'R1' });
    expect(planRule(r, 'exclude', { reason: ' why ' }).steps).toEqual([{ kind: 'excludeRule', policyId: 'r1', reason: 'why', firmId: undefined }]);
    expect(planRule(r, 'exclude', { reason: 'why', firmId: 'f-1' }).steps).toEqual([{ kind: 'excludeRule', policyId: 'r1', reason: 'why', firmId: 'f-1' }]);
    expect(planRule(r, 'exclude', { reason: '' }).steps).toEqual([]);
  });

  it('O-20: a rule that names one firm is excluded for that firm only, whatever the form says', () => {
    const r = rule({ policyId: 'r1', code: 'R1', firmScope: FENWICK });
    expect(planRule(r, 'exclude', { reason: 'why', firmId: '' }).steps).toEqual([{ kind: 'excludeRule', policyId: 'r1', reason: 'why', firmId: FENWICK.id }]);
    expect(planRule(r, 'exclude', { reason: 'why', firmId: 'someone-else' }).steps).toEqual([{ kind: 'excludeRule', policyId: 'r1', reason: 'why', firmId: FENWICK.id }]);
  });

  it('Run the published rule removes the record\'s own exclusion', () => {
    expect(planRule(excluded(0, 'ex-row'), 'keep', {}).steps).toEqual([{ kind: 'remove', overrideId: 'ex-row' }]);
  });

  it('P-3: a rule a parent excluded or changed cannot be re-enabled here: Run the published rule is blocked with a reason and plans nothing', () => {
    for (const r of [excluded(1, 'par-row'), changed(1)]) {
      expect(keepBlocker(r)).toMatch(/Not available: the parent's rule (exclusion|change) applies to this record/);
      expect(planRule(r, 'keep', {}).steps).toEqual([]);
    }
    expect(currentRuleChoice(excluded(1, 'par-row'))).toBe('parent');
  });

  it('a rule changed on THIS record is run as published by removing its override row; excluding it is blocked until then', () => {
    expect(planRule(changed(0), 'keep', {}).steps).toEqual([{ kind: 'remove', overrideId: 'chg-row' }]);
    expect(excludeBlocker(changed(0))).toMatch(/Run the published rule first/);
    expect(planRule(changed(0), 'exclude', { reason: 'r' }).steps).toEqual([]);
  });

  it('a retired base rule\'s record rule names no override row, so nothing can be removed', () => {
    const r = rule({ policyId: 'rr', code: 'R1', recordRule: true, inForce: false, marks: [mark('BaseRetired', { recordRuleId: 'rr' })] });
    expect(keepBlocker(r)).toMatch(/base rule is retired/);
    expect(planRule(r, 'keep', {}).steps).toEqual([]);
  });

  it('changing the firm or the reason of an own exclusion is a removal and a create; the same values change nothing', () => {
    const r = excluded(0, 'ex-row');
    expect(planRule(r, 'exclude', { reason: 'because', firmId: '' }).steps).toEqual([]);
    expect(kinds(planRule(r, 'exclude', { reason: 'because', firmId: 'f-2' }))).toEqual(['remove', 'excludeRule']);
    expect(kinds(planRule(r, 'exclude', { reason: 'another', firmId: '' }))).toEqual(['remove', 'excludeRule']);
  });

  it('there is no change choice or step: a rule plan never calls the change route', () => {
    for (const r of [rule({ policyId: 'a', code: 'A' }), excluded(0, 'x'), changed(0)]) {
      for (const c of ['parent', 'keep', 'exclude'] as const) for (const s of planRule(r, c, { reason: 'r' }).steps) expect(['remove', 'excludeRule']).toContain(s.kind);
    }
  });
});

describe('running a plan', () => {
  const fail: WriteFailure = { status: 409, errorCode: 'assignment.duplicate', title: 'Conflict', detail: null, problems: [], signInFailed: false };
  const api = (over: Partial<OverridesApi>): OverridesApi =>
    ({
      assignProcedure: jest.fn(async () => ({ ok: true, value: {} })),
      excludeRule: jest.fn(async () => ({ ok: true, value: {} })),
      removalPreview: jest.fn(),
      removeOverride: jest.fn(async () => ({ ok: true, value: { ruleSwitchedOff: false, closeComplete: true, affectedText: null, message: null } })),
      procedureVersions: jest.fn(),
      ...over,
    }) as never;

  const plan: Plan = { steps: [{ kind: 'remove', overrideId: 'a' }, { kind: 'assign', procedureId: 'p', mode: 'Excluded', reason: 'r' }] };

  it('runs the calls in order', async () => {
    const order: string[] = [];
    const a = api({
      removeOverride: jest.fn(async () => (order.push('remove'), { ok: true, value: {} as never })) as never,
      assignProcedure: jest.fn(async () => (order.push('assign'), { ok: true, value: {} as never })) as never,
    });
    const run = await executePlan(a, 'matters', 'rec', plan);
    expect(order).toEqual(['remove', 'assign']);
    expect(run).toMatchObject({ ok: true, completed: 2 });
  });

  it('stops at the first refusal and says how far it got; a refused first call leaves completed at 0', async () => {
    const second = api({ assignProcedure: jest.fn(async () => ({ ok: false, failure: fail })) as never });
    expect(await executePlan(second, 'matters', 'rec', plan)).toMatchObject({ ok: false, completed: 1, failure: fail });

    const first = api({ removeOverride: jest.fn(async () => ({ ok: false, failure: fail })) as never });
    const run = await executePlan(first, 'matters', 'rec', plan);
    expect(run).toMatchObject({ ok: false, completed: 0 });
    expect(first.assignProcedure).not.toHaveBeenCalled();
  });
});
