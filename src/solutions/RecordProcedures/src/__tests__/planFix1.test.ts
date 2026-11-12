/**
 * 084b fix round 1: the plan against the words the server sends, and the plan's own guards (items 1, 3, 4, 6 and 10 of the review).
 * Each test fails on the code before the fix.
 */

import * as fs from 'fs';
import * as path from 'path';
import { currentRuleChoice, excludeBlocker, keepBlocker, planProcedure, planRule, procedureChoices, type Plan } from '../components/wizards/plan';
import { MATTER, V, mark, override, procedure, rule } from './fixtures';

const parent = { ...MATTER, distance: 1 };
const ownPin = override({ rowId: 'own-pin', mode: 'Pinned', chainRecord: MATTER, pinnedVersion: V.rate1, reason: 'old' });
const kinds = (p: Plan) => p.steps.map(s => s.kind);

// ── The server's words, read from its source ────────────────────────────────

const REPO = path.resolve(__dirname, '../../../../..');
const SERVER = 'src/server/api/Sprk.Bff.Api/Services';
const read = (rel: string) => fs.readFileSync(path.join(REPO, rel), 'utf8');

/** The members of a C# enum, read from the server's source (so the page is tested against the words the server sends). */
function csharpEnum(file: string, name: string): string[] {
  const body = new RegExp(`enum\\s+${name}\\b[^{]*\\{([\\s\\S]*?)\\n\\}`).exec(read(file))?.[1] ?? '';
  return [...body.matchAll(/^\s*([A-Z][A-Za-z]*)\b/gm)].map(m => m[1]);
}

const RESOLVER = `${SERVER}/Signals/Billing/ProcedureResolver.cs`;
const appliesToNames = () => csharpEnum(`${SERVER}/Finance/Procedures/ProcedureService.cs`, 'ProcedureAppliesTo');

describe('item 1: the page reads the server\'s enum names, not words of its own', () => {
  it('(control) the scan reads the enums from the server source', () => {
    expect(appliesToNames()).toEqual(['AllRecords', 'Firm', 'AssignedRecordsOnly']);
    expect(csharpEnum(RESOLVER, 'ProcedureOverrideMode')).toEqual(['FollowLatest', 'Pinned', 'Excluded']);
    expect(csharpEnum(RESOLVER, 'ProcedureLevel')).toEqual(['AllRecords', 'Firm', 'ManualParent', 'ManualOwn']);
  });

  it('a Procedure is "assigned only here" exactly when the server says AssignedRecordsOnly: only then is Remove offered and Follow latest keeps the row', () => {
    for (const appliesTo of appliesToNames()) {
      const assignedOnly = appliesTo === 'AssignedRecordsOnly';
      const p = procedure({
        id: 'x',
        name: 'X',
        appliesTo,
        sources: [{ kind: 'ManualOwn', winning: true, distance: 0, via: null, mode: 'Pinned', rowId: 'own-pin' }],
        overrides: [ownPin],
      });
      expect([appliesTo, procedureChoices(p).includes('remove')]).toEqual([appliesTo, assignedOnly]);
      // Follow latest on a Procedure that only this record's row brings in must not be a bare DELETE: the Procedure would stop applying.
      expect([appliesTo, kinds(planProcedure(p, 'follow', {}))]).toEqual([appliesTo, assignedOnly ? ['remove', 'assign'] : ['remove']]);
    }
  });

  it('every enum word the wizard code compares against is a word the server sends', () => {
    const sent = new Set([
      ...appliesToNames(),
      ...csharpEnum(RESOLVER, 'ProcedureLevel'),
      ...csharpEnum(RESOLVER, 'ProcedureOverrideMode'),
      ...csharpEnum(RESOLVER, 'ProcedureGapKind'),
      // The read service's status words, version statuses, firm sources and rule-mark kinds are string literals there.
      ...[...read(`${SERVER}/Finance/Procedures/RecordProcedureReadService.cs`).matchAll(/"([A-Z][A-Za-z]+)"/g)].map(m => m[1]),
    ]);
    const dir = path.resolve(__dirname, '..');
    const files = ['components/wizards/plan.ts', 'components/wizards/ProcedureWizard.tsx', 'components/wizards/RuleWizard.tsx', 'components/wizards/words.ts'];
    const compared = files.flatMap(f =>
      [...fs.readFileSync(path.join(dir, f), 'utf8').matchAll(/\b(?:appliesTo|level|kind|mode|status|firmSource)\s*[!=]==\s*'([A-Z][A-Za-z]+)'/g)].map(m => [f, m[1]] as const)
    );
    expect(compared.length).toBeGreaterThan(8);
    for (const [f, word] of compared) expect([f, word, sent.has(word)]).toEqual([f, word, true]);
    // The word the first version of the page invented.
    expect(sent.has('Manual')).toBe(false);
  });
});

describe('item 4: a pin the server will refuse is not planned (the removal before it would lose the pin)', () => {
  const retiredPin = override({ rowId: 'own-pin', mode: 'Pinned', chainRecord: MATTER, pinnedVersion: { ...V.rate1, status: 'Retired' }, reason: 'old' });
  const firmProc = (overrides = [] as ReturnType<typeof override>[]) => procedure({ id: 'p', name: 'P', appliesTo: 'Firm', overrides });

  it('a pin of a version that is not in the pinnable set plans nothing, with or without an own row', () => {
    expect(planProcedure(firmProc([retiredPin]), 'pin', { versionId: V.rate1.id, reason: 'new reason', pinnable: [V.rate2.id] }).steps).toEqual([]);
    expect(planProcedure(firmProc(), 'pin', { versionId: V.rate1.id, reason: 'r', pinnable: [] }).steps).toEqual([]);
    // The versions have not been read yet.
    expect(planProcedure(firmProc(), 'pin', { versionId: V.rate1.id, reason: 'r' }).steps).toEqual([]);
  });

  it('another pinnable version replaces the retired pin', () => {
    expect(kinds(planProcedure(firmProc([retiredPin]), 'pin', { versionId: V.rate2.id, reason: 'r', pinnable: [V.rate2.id] }))).toEqual(['remove', 'assign']);
  });

  it('a reason over the server\'s 2000 characters (after trimming) plans nothing, for a pin, an exclude and a rule exclude', () => {
    const long = 'x'.repeat(2001);
    const ok = 'x'.repeat(2000);
    expect(planProcedure(firmProc(), 'pin', { versionId: 'v-1', reason: long, pinnable: ['v-1'] }).steps).toEqual([]);
    expect(planProcedure(firmProc(), 'pin', { versionId: 'v-1', reason: ` ${ok} `, pinnable: ['v-1'] }).steps).toHaveLength(1);
    expect(planProcedure(firmProc([ownPin]), 'exclude', { reason: long }).steps).toEqual([]);
    expect(planProcedure(firmProc(), 'exclude', { reason: ok }).steps).toHaveLength(1);
    expect(planRule(rule({ policyId: 'r1', code: 'R1' }), 'exclude', { reason: long }).steps).toEqual([]);
    expect(planRule(rule({ policyId: 'r1', code: 'R1' }), 'exclude', { reason: ok }).steps).toHaveLength(1);
  });
});

describe('item 3: a rule changed on this record is not "as published"', () => {
  const changedHere = rule({
    policyId: 'rr',
    code: 'R1',
    recordRule: true,
    parameterSummary: '12 h',
    marks: [mark('Changed', { reason: 'staffing', distance: 0, chainRecord: MATTER, overrideRowId: 'chg-row', baseParameterSummary: '11 h' })],
  });

  it('its current state is none of the choices; planning that state changes nothing', () => {
    expect(currentRuleChoice(changedHere)).toBe('changed');
    expect(planRule(changedHere, 'changed', {}).steps).toEqual([]);
    // An unchanged rule is still the published rule.
    expect(currentRuleChoice(rule({ policyId: 'a', code: 'A' }))).toBe('keep');
  });
});

describe('items 6 and 10: the blockers say where to go', () => {
  it('a rule a parent CHANGED: Exclude points to the parent, not to "Run the published rule first" (which P-3 disables)', () => {
    const r = rule({ policyId: 'rr', code: 'R1', recordRule: true, marks: [mark('Changed', { distance: 1, chainRecord: parent, overrideRowId: 'par-row' })] });
    expect(excludeBlocker(r)).toBe('Changed on the parent; change it there.');
  });

  it('a retired base rule\'s record rule does not claim to be "removed from the change flow"', () => {
    const r = rule({ policyId: 'rr', code: 'R1', recordRule: true, inForce: false, marks: [mark('BaseRetired', { recordRuleId: 'rr' })] });
    expect(keepBlocker(r)).toContain('No override row is available here; see 084c.');
    expect(keepBlocker(r)).not.toMatch(/change flow/);
  });
});
