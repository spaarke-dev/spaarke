/**
 * Task 084g fix round 1: the wizards decide per CHOICE from the lists' `permissions` (review B1), keep their mode after a partial save (B2),
 * and stay read-only across ‹ › (B6). Each test fails on the code before its fix.
 */

import '@testing-library/jest-dom';
import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import {
  NOT_ALLOWED_BOTH,
  NOT_ALLOWED_CREATE,
  NOT_ALLOWED_REMOVE,
  allowedBlocker,
  noAllowedCall,
  planProcedure,
  planRule,
  planWithin,
  procedureChoiceNeeds,
  ruleChoiceNeeds,
  type ChoiceNeeds,
  type ProcedureChoice,
  type ProcedureInput,
  type RuleChoice,
  type RuleInput,
} from '../components/wizards/plan';
import type { AllowedSteps } from '../services/model';
import type { ProcedureItem, RecordProceduresView, RuleItem, WritePermissions } from '../services/recordProceduresApi';
import { FENWICK, MATTER, PROJECT, V, WRITE_ALL, mark, matterProcedures, matterRules, override, projectProcedures, projectRules, waProcedures, waRules } from './fixtures';
import { button, chooseRadio, createdBody, next, openProcedure, openRule, previewBody, radio, refusal, removedBody, renderHarness, respond, toChoices, toConfirm, typeReason } from './wizardHarness';

const NONE: WritePermissions = { assignOrExclude: false, remove: false, change: false };
const CREATE_ONLY: WritePermissions = { assignOrExclude: true, remove: false, change: true };
const REMOVE_ONLY: WritePermissions = { assignOrExclude: false, remove: true, change: true };

const titleOf = (d: HTMLElement) => document.getElementById(d.getAttribute('aria-labelledby') ?? '')?.textContent ?? null;
const routes = (writeCalls: Array<{ route: string }>) => writeCalls.map(c => c.route).filter(r => r !== 'procedure');
const withPermissions = <T extends object>(view: T, permissions: WritePermissions): T => ({ ...view, permissions });
const caption = (d: HTMLElement, choice: string) => within(d).getByTestId(`choice-${choice}`);

/** The two lists, each with these permissions. */
const lists = (permissions: WritePermissions) => ({ procedures: withPermissions(matterProcedures, permissions), rules: withPermissions(matterRules, permissions) });

async function expectReadOnly(d: HTMLElement) {
  await toChoices(d);
  expect(within(d).getByTestId('read-only-reason')).toBeInTheDocument();
  for (const r of within(d).getAllByRole('radio', { hidden: true })) expect(r).toBeDisabled();
  expect(within(d).queryByRole('button', { name: /^(Next|Save override|Remove override|Exclude rule)$/, hidden: true })).not.toBeInTheDocument();
}

describe('B1: each choice is decided by the calls ITS plan needs', () => {
  it('assignOrExclude true, remove false, on a Procedure with no own override: the one-call choices are enabled, and an Exclude saves with one call', async () => {
    const { writeCalls } = renderHarness({ ...lists(CREATE_ONLY), writes: respond({ assign: () => createdBody({ mode: 'Excluded' }) }) });
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await toChoices(d);
    expect(within(d).queryByTestId('read-only-reason')).not.toBeInTheDocument();
    expect(radio(d, /^Pin a version/)).toBeEnabled();
    expect(radio(d, /^Exclude here/)).toBeEnabled();
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Not on this matter');
    await toConfirm(d);
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['assign']);
  });

  it('assignOrExclude true, remove false, on a rule with no own exclusion: Exclude here is enabled and saves with one call', async () => {
    const { writeCalls } = renderHarness({ ...lists(CREATE_ONLY), writes: respond({ exclude: () => createdBody({ kind: 'Rule' }) }) });
    const d = await openRule('r-block', 'BLOCK · Block billing');
    await toChoices(d);
    expect(within(d).queryByTestId('read-only-reason')).not.toBeInTheDocument();
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'A reason');
    await toConfirm(d);
    fireEvent.click(button(d, 'Exclude rule'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['exclude']);
  });

  it('assignOrExclude true, remove false: every choice that removes the record\'s own override is disabled with the reason; with none left, read-only', async () => {
    renderHarness(lists(CREATE_ONLY));
    // The rate card has its own pin: Follow latest, Pin and Exclude each remove it first.
    const proc = await openProcedure('p-rate', 'Fenwick rate card');
    await expectReadOnly(proc);
    for (const c of ['follow', 'pin', 'exclude']) expect(caption(proc, c)).toHaveTextContent(NOT_ALLOWED_REMOVE);
    fireEvent.click(within(proc).getAllByRole('button', { name: 'Close', hidden: true }).find(b => b.textContent === 'Close')!);
    await waitFor(() => expect(screen.queryByRole('dialog', { hidden: true })).not.toBeInTheDocument());

    // VAGUE is excluded here: Run the published rule removes the exclusion; Exclude here replaces it.
    const r = await openRule('r-vague', 'VAGUE · Vague description');
    await expectReadOnly(r);
    expect(caption(r, 'keep')).toHaveTextContent(NOT_ALLOWED_REMOVE);
    expect(caption(r, 'exclude')).toHaveTextContent(NOT_ALLOWED_REMOVE);
  });

  it('assignOrExclude false, remove true, on a Procedure with its own pin: Follow latest (a removal) is enabled; Pin and Exclude say why not', async () => {
    const { writeCalls } = renderHarness({ ...lists(REMOVE_ONLY), writes: respond({ preview: () => previewBody(), remove: () => removedBody() }) });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    expect(within(d).queryByTestId('read-only-reason')).not.toBeInTheDocument();
    expect(radio(d, /^Pin a version/)).toBeDisabled();
    expect(radio(d, /^Exclude here/)).toBeDisabled();
    expect(caption(d, 'pin')).toHaveTextContent(NOT_ALLOWED_CREATE);
    expect(caption(d, 'exclude')).toHaveTextContent(NOT_ALLOWED_CREATE);
    expect(within(d).queryByLabelText(/^Reason/)).not.toBeInTheDocument();
    chooseRadio(d, /^Follow latest/);
    await toConfirm(d);
    fireEvent.click(button(d, 'Remove override'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['preview', 'remove']);
  });

  it('assignOrExclude false, remove true, on a Procedure with nothing to remove: read-only, and each create choice says why', async () => {
    const { writeCalls } = renderHarness(lists(REMOVE_ONLY));
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    // No own override, nothing to remove; every choice that makes a call creates: read-only.
    await expectReadOnly(d);
    expect(caption(d, 'exclude')).toHaveTextContent(NOT_ALLOWED_CREATE);
    expect(writeCalls).toHaveLength(0);
  });
});

describe('B1 must-fire: no plan step can ever require a kind of call its choice was not said to need', () => {
  // Every fixture row, plus variants that give each row its own override, a parent's, or both.
  const own = (mode: 'FollowLatest' | 'Pinned' | 'Excluded') => override({ rowId: `own-${mode}`, mode, chainRecord: MATTER, pinnedVersion: mode === 'Pinned' ? V.rate1 : null, reason: mode === 'FollowLatest' ? null : 'r' });
  const parent = (mode: 'FollowLatest' | 'Pinned' | 'Excluded') => override({ rowId: `par-${mode}`, mode, chainRecord: { ...PROJECT, distance: 1 } });
  const baseProcedures: ProcedureItem[] = [...matterProcedures.procedures, ...projectProcedures.procedures, ...waProcedures.procedures];
  const procedureRows: ProcedureItem[] = baseProcedures.flatMap(p =>
    [p, { ...p, appliesTo: 'AssignedRecordsOnly', sources: p.sources.map(s => ({ ...s, kind: 'ManualOwn' })) }].flatMap(q => [
      q,
      ...(['FollowLatest', 'Pinned', 'Excluded'] as const).map(m => ({ ...q, overrides: [own(m)] })),
      ...(['FollowLatest', 'Pinned', 'Excluded'] as const).map(m => ({ ...q, overrides: [parent(m)] })),
      ...(['FollowLatest', 'Pinned', 'Excluded'] as const).map(m => ({ ...q, overrides: [own(m), parent('Pinned')] })),
    ])
  );
  const procedureChoices: ProcedureChoice[] = ['parent', 'follow', 'pin', 'exclude', 'remove'];
  const procedureInputs: ProcedureInput[] = [
    {},
    { reason: 'r' },
    { reason: 'another' },
    { versionId: V.rate1.id, reason: 'r', pinnable: [V.rate1.id, V.rate2.id] },
    { versionId: V.rate2.id, reason: 'x', pinnable: [V.rate1.id, V.rate2.id] },
  ];

  const ownExcl = (overrideRowId?: string) => mark('Excluded', { distance: 0, chainRecord: MATTER, reason: 'r', firm: FENWICK, overrideRowId });
  const baseRules: RuleItem[] = [...matterRules.rules, ...projectRules.rules, ...waRules.rules];
  const ruleRows: RuleItem[] = baseRules.flatMap(r => [
    r,
    { ...r, marks: [...r.marks, ownExcl('own-x')] },
    { ...r, marks: [...r.marks, ownExcl(undefined)] },
    { ...r, marks: [...r.marks, mark('Changed', { distance: 0, chainRecord: MATTER, overrideRowId: 'own-c' })] },
    { ...r, marks: [...r.marks, mark('Excluded', { distance: 1, chainRecord: { ...PROJECT, distance: 1 } })] },
    { ...r, recordRule: false, marks: [mark('BaseRetired', {}), ownExcl('own-x')] },
  ]);
  const ruleChoices: RuleChoice[] = ['parent', 'keep', 'exclude', 'changed'];
  const ruleInputs: RuleInput[] = [{}, { reason: 'r' }, { reason: 'r', firmId: FENWICK.id }, { reason: 'other', firmId: '' }];

  /** Every (row, choice, input) whose plan steps fall outside `needsOf`. */
  function violations(needsP: (p: ProcedureItem, c: ProcedureChoice) => ChoiceNeeds, needsR: (r: RuleItem, c: RuleChoice) => ChoiceNeeds): string[] {
    const out: string[] = [];
    for (const p of procedureRows)
      for (const c of procedureChoices)
        for (const i of procedureInputs) if (!planWithin(planProcedure(p, c, i), needsP(p, c))) out.push(`${p.id}/${p.overrides.map(o => o.rowId).join('+')}/${c}`);
    for (const r of ruleRows)
      for (const c of ruleChoices) for (const i of ruleInputs) if (!planWithin(planRule(r, c, i), needsR(r, c))) out.push(`${r.policyId}/${r.marks.map(m => m.kind).join('+')}/${c}`);
    return out;
  }

  it('over every fixture row and variant, every choice and every input, each plan is within what its choice needs', () => {
    expect(procedureRows.length).toBeGreaterThan(100);
    expect(ruleRows.length).toBeGreaterThan(30);
    expect(violations(procedureChoiceNeeds, ruleChoiceNeeds)).toEqual([]);
  });

  it('so, under any answers, a choice the answers allow never plans a call they do not allow', () => {
    const combos: AllowedSteps[] = [
      { remove: false, create: false },
      { remove: true, create: false },
      { remove: false, create: true },
      { remove: true, create: true },
    ];
    for (const allowed of combos) {
      for (const p of procedureRows)
        for (const c of procedureChoices) {
          if (allowedBlocker(procedureChoiceNeeds(p, c), allowed) !== null) continue;
          for (const i of procedureInputs) for (const s of planProcedure(p, c, i).steps) expect([c, s.kind, s.kind === 'remove' ? allowed.remove : allowed.create]).toEqual([c, s.kind, true]);
        }
      for (const r of ruleRows)
        for (const c of ruleChoices) {
          if (allowedBlocker(ruleChoiceNeeds(r, c), allowed) !== null) continue;
          for (const i of ruleInputs) for (const s of planRule(r, c, i).steps) expect([c, s.kind, s.kind === 'remove' ? allowed.remove : allowed.create]).toEqual([c, s.kind, true]);
        }
    }
  });

  it('control: the scan sees a wrong answer (a "needs" that forgets removals, or creates)', () => {
    expect(violations((p, c) => ({ ...procedureChoiceNeeds(p, c), remove: false }), ruleChoiceNeeds).length).toBeGreaterThan(0);
    expect(violations(procedureChoiceNeeds, (r, c) => ({ ...ruleChoiceNeeds(r, c), remove: false })).length).toBeGreaterThan(0);
    expect(violations((p, c) => ({ ...procedureChoiceNeeds(p, c), create: false }), ruleChoiceNeeds).length).toBeGreaterThan(0);
    expect(violations(procedureChoiceNeeds, (r, c) => ({ ...ruleChoiceNeeds(r, c), create: false })).length).toBeGreaterThan(0);
  });

  it('the reasons: both missing, removal missing, create missing, nothing missing', () => {
    expect(allowedBlocker({ remove: true, create: true }, { remove: false, create: false })).toBe(NOT_ALLOWED_BOTH);
    expect(allowedBlocker({ remove: true, create: true }, { remove: false, create: true })).toBe(NOT_ALLOWED_REMOVE);
    expect(allowedBlocker({ remove: true, create: true }, { remove: true, create: false })).toBe(NOT_ALLOWED_CREATE);
    expect(allowedBlocker({ remove: false, create: false }, { remove: false, create: false })).toBeNull();
  });
});

describe('R1: read-only means the answers deny a call, not that the row has none', () => {
  // A rule changed on the parent: "Run the published rule" and "Exclude here" are each refused by their own blocker (P-3), so no choice
  // makes a call. (No Procedure fixture has such a row: Pin and Exclude always create.)
  const parentChanged: RuleItem = {
    ...matterRules.rules[0],
    policyId: 'r-pchg',
    code: 'PCHG',
    name: 'Changed upstream',
    marks: [mark('Changed', { distance: 1, chainRecord: { ...PROJECT, distance: 1 }, reason: 'Project rates' })],
  };
  const withRule = (permissions: WritePermissions) => ({
    procedures: withPermissions(matterProcedures, permissions),
    rules: { ...matterRules, permissions, rules: [parentChanged, ...matterRules.rules] },
  });
  const stepLabels = (d: HTMLElement) => within(d).getAllByRole('listitem', { hidden: true }).map(li => li.textContent ?? '').join(' | ');

  async function expectNormalFrame(d: HTMLElement) {
    expect(stepLabels(d)).toMatch(/Impact/);
    expect(stepLabels(d)).toMatch(/Confirm/);
    await toChoices(d);
    expect(within(d).queryByTestId('read-only-reason')).not.toBeInTheDocument();
    expect(within(d).queryByText(NOT_ALLOWED_BOTH)).not.toBeInTheDocument();
    // The choices give their own reasons, and nothing can go on.
    expect(caption(d, 'exclude')).toHaveTextContent('Changed on the parent; change it there.');
    expect(radio(d, /^Run the published rule/)).toBeDisabled();
    expect(next(d)).toBeDisabled();
  }

  it('must-fire: full rights on a rule changed on the parent show the normal frame, not "You can view this, but not change it."', async () => {
    renderHarness(withRule(WRITE_ALL));
    await expectNormalFrame(await openRule('r-pchg', 'PCHG · Changed upstream'));
  });

  it('must-not-fire: the same row with nothing allowed is still the normal frame (there is nothing to deny)', async () => {
    renderHarness(withRule(NONE));
    await expectNormalFrame(await openRule('r-pchg', 'PCHG · Changed upstream'));
  });

  it('a row with a call-making choice and nothing allowed is still read-only', async () => {
    renderHarness(withRule(NONE));
    await expectReadOnly(await openRule('r-block', 'BLOCK · Block billing'));
  });

  it('noAllowedCall: read-only only when some choice makes a call and none of those is allowed', () => {
    const needs = (c: string): ChoiceNeeds => (c === 'none' ? { remove: false, create: false } : { remove: false, create: true });
    expect(noAllowedCall(['none'], needs, () => null)).toBe(false);
    expect(noAllowedCall(['none'], needs, () => 'blocked')).toBe(false);
    expect(noAllowedCall(['none', 'call'], needs, c => (c === 'call' ? 'blocked' : null))).toBe(true);
    expect(noAllowedCall(['none', 'call'], needs, () => null)).toBe(false);
    expect(noAllowedCall([], needs, () => null)).toBe(false);
  });
});

describe('B2: after a partial save, the wizard keeps its mode and its message', () => {
  // The re-read allows nothing (remove AND assignOrExclude false): judged afresh, the re-read row would be read-only and the shell would reset.
  // (remove:false alone no longer flips this row after the removal: its Exclude then needs only a create, B1.)
  it('Pin -> Exclude: the removal succeeds, the create is refused; the re-read allows nothing; the partial-save message stays and Finish works again', async () => {
    let removed = false;
    let assigns = 0;
    const afterRemoval: RecordProceduresView = {
      ...matterProcedures,
      permissions: NONE,
      procedures: matterProcedures.procedures.map(p => (p.id === 'p-rate' ? { ...p, overrides: [], pinned: false, newerVersion: null, sources: p.sources.filter(s => s.kind !== 'ManualOwn') } : p)),
    };
    const { writeCalls, calls } = renderHarness({
      procedures: () => (removed ? afterRemoval : matterProcedures),
      writes: respond({
        preview: () => previewBody(),
        remove: () => {
          removed = true;
          return removedBody();
        },
        assign: () => (++assigns === 1 ? refusal(409, 'Conflict', 'assignment.concurrent_edit') : createdBody({ mode: 'Excluded' })),
      }),
    });
    const d = await openProcedure('p-rate', 'Fenwick rate card');
    await toChoices(d);
    chooseRadio(d, /^Exclude here/);
    typeReason(d, 'Rates no longer apply');
    await toConfirm(d);
    const reads = calls.length;

    fireEvent.click(button(d, 'Save override'));
    await within(d).findByRole('alert', { hidden: true });
    await waitFor(() => expect(calls.length).toBe(reads + 2));
    await waitFor(() => expect(button(d, 'Save override')).toBeEnabled());
    // Still on Confirm, still saying what happened, not read-only.
    expect(within(d).getByRole('alert', { hidden: true })).toHaveTextContent("The record's earlier override was removed but the new one was not saved");
    expect(within(d).getByTestId('confirm-step')).toBeInTheDocument();
    expect(within(d).queryByTestId('read-only-reason')).not.toBeInTheDocument();
    fireEvent.click(button(d, 'Save override'));
    await within(d).findByTestId('success-body');
    expect(routes(writeCalls)).toEqual(['preview', 'remove', 'assign', 'assign']);
  });
});

describe('B6: read-only across ‹ ›', () => {
  it('› from a read-only row lands read-only on the next row', async () => {
    const { writeCalls } = renderHarness(lists(NONE));
    const d = await openProcedure('p-ocg', 'Acme Outside Counsel Guidelines');
    await expectReadOnly(d);
    fireEvent.click(button(d, 'Next record'));
    await waitFor(() => expect(titleOf(d)).toBe('Fenwick rate card'));
    expect(within(d).getByTestId('procedure-step')).toBeInTheDocument();
    fireEvent.click(next(d));
    await within(d).findByTestId('mode-step');
    expect(within(d).getByTestId('read-only-reason')).toBeInTheDocument();
    for (const r of within(d).getAllByRole('radio', { hidden: true })) expect(r).toBeDisabled();
    expect(writeCalls).toHaveLength(0);
  });
});
