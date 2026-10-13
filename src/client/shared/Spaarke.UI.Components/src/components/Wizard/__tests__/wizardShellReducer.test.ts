/**
 * wizardShellReducer.test.ts — characterization tests for the WizardShell navigation reducer
 * (ontology-platform-r1 task 056, P1). They lock the behaviour the eight shipped wizards rely on
 * BEFORE the shell is re-based onto SprkModal: next / prev / go-to, dynamic add / remove with
 * `canonicalOrder`, the index clamp on remove, and `initialStepId`.
 */
import { buildInitialShellState, wizardShellReducer } from '../wizardShellReducer';
import type { IWizardShellState, IWizardStepConfig } from '../wizardShellTypes';

function cfg(id: string, label = id.toUpperCase()): IWizardStepConfig {
  return { id, label, renderContent: () => null, canAdvance: () => true };
}

const ids = (s: IWizardShellState) => s.steps.map(x => x.id);
const statuses = (s: IWizardShellState) => s.steps.map(x => x.status);

describe('buildInitialShellState', () => {
  it('opens on the first step: first active, the rest pending', () => {
    const s = buildInitialShellState([cfg('a'), cfg('b'), cfg('c')]);
    expect(s.currentStepIndex).toBe(0);
    expect(ids(s)).toEqual(['a', 'b', 'c']);
    expect(s.steps.map(x => x.label)).toEqual(['A', 'B', 'C']);
    expect(statuses(s)).toEqual(['active', 'pending', 'pending']);
  });

  it('opens on initialStepId: earlier steps completed, later steps pending', () => {
    const s = buildInitialShellState([cfg('a'), cfg('b'), cfg('c')], 'b');
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['completed', 'active', 'pending']);
  });

  it('falls back to the first step when initialStepId matches no step', () => {
    const s = buildInitialShellState([cfg('a'), cfg('b')], 'nope');
    expect(s.currentStepIndex).toBe(0);
    expect(statuses(s)).toEqual(['active', 'pending']);
  });
});

describe('wizardShellReducer — navigation', () => {
  const base = () => buildInitialShellState([cfg('a'), cfg('b'), cfg('c')]);

  it('NEXT_STEP advances and marks the left step completed', () => {
    const s = wizardShellReducer(base(), { type: 'NEXT_STEP' });
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['completed', 'active', 'pending']);
  });

  it('NEXT_STEP on the last step is a no-op (same reference)', () => {
    const last = buildInitialShellState([cfg('a'), cfg('b')], 'b');
    expect(wizardShellReducer(last, { type: 'NEXT_STEP' })).toBe(last);
  });

  it('PREV_STEP goes back and resets later steps to pending', () => {
    const s1 = wizardShellReducer(wizardShellReducer(base(), { type: 'NEXT_STEP' }), { type: 'NEXT_STEP' });
    const s = wizardShellReducer(s1, { type: 'PREV_STEP' });
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['completed', 'active', 'pending']);
  });

  it('PREV_STEP on the first step is a no-op (same reference)', () => {
    const first = base();
    expect(wizardShellReducer(first, { type: 'PREV_STEP' })).toBe(first);
  });

  it('GO_TO_STEP jumps and rebuilds statuses around the target', () => {
    const s = wizardShellReducer(base(), { type: 'GO_TO_STEP', stepIndex: 2 });
    expect(s.currentStepIndex).toBe(2);
    expect(statuses(s)).toEqual(['completed', 'completed', 'active']);
  });

  it('GO_TO_STEP clamps an out-of-range index into [0, last]', () => {
    expect(wizardShellReducer(base(), { type: 'GO_TO_STEP', stepIndex: 99 }).currentStepIndex).toBe(2);
    expect(wizardShellReducer(base(), { type: 'GO_TO_STEP', stepIndex: -5 }).currentStepIndex).toBe(0);
  });
});

describe('wizardShellReducer — dynamic steps', () => {
  it('ADD_DYNAMIC_STEP without canonicalOrder appends a pending step', () => {
    const s = wizardShellReducer(buildInitialShellState([cfg('a'), cfg('b')]), {
      type: 'ADD_DYNAMIC_STEP',
      config: cfg('x'),
    });
    expect(ids(s)).toEqual(['a', 'b', 'x']);
    expect(statuses(s)).toEqual(['active', 'pending', 'pending']);
  });

  it('ADD_DYNAMIC_STEP with an id that already exists is a no-op (same reference)', () => {
    const s0 = buildInitialShellState([cfg('a'), cfg('b')]);
    expect(wizardShellReducer(s0, { type: 'ADD_DYNAMIC_STEP', config: cfg('b') })).toBe(s0);
  });

  it('ADD_DYNAMIC_STEP with canonicalOrder places follow-on steps in canonical order before a canonical "confirm"', () => {
    // The decision-wizard shape: canonicalOrder = [...followOnStepIds, 'confirm'].
    const order = ['x', 'y', 'confirm'];
    let s = buildInitialShellState([cfg('found'), cfg('next'), cfg('confirm')], 'next');
    s = wizardShellReducer(s, { type: 'ADD_DYNAMIC_STEP', config: cfg('y'), canonicalOrder: order });
    expect(ids(s)).toEqual(['found', 'next', 'y', 'confirm']);
    s = wizardShellReducer(s, { type: 'ADD_DYNAMIC_STEP', config: cfg('x'), canonicalOrder: order });
    expect(ids(s)).toEqual(['found', 'next', 'x', 'y', 'confirm']);
    // The active step does not move when steps are added after it.
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['completed', 'active', 'pending', 'pending', 'pending']);
  });

  it('REMOVE_DYNAMIC_STEP after the current step keeps the index', () => {
    const s0 = buildInitialShellState([cfg('a'), cfg('b'), cfg('c')]);
    const s = wizardShellReducer(s0, { type: 'REMOVE_DYNAMIC_STEP', stepId: 'c' });
    expect(ids(s)).toEqual(['a', 'b']);
    expect(s.currentStepIndex).toBe(0);
  });

  it('REMOVE_DYNAMIC_STEP before the current step shifts the index back by one', () => {
    const s0 = buildInitialShellState([cfg('a'), cfg('b'), cfg('c')], 'c');
    const s = wizardShellReducer(s0, { type: 'REMOVE_DYNAMIC_STEP', stepId: 'a' });
    expect(ids(s)).toEqual(['b', 'c']);
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['completed', 'active']);
  });

  it('REMOVE_DYNAMIC_STEP of the current step moves to the previous step', () => {
    const s0 = buildInitialShellState([cfg('a'), cfg('b'), cfg('c')], 'b');
    const s = wizardShellReducer(s0, { type: 'REMOVE_DYNAMIC_STEP', stepId: 'b' });
    expect(ids(s)).toEqual(['a', 'c']);
    expect(s.currentStepIndex).toBe(0);
    expect(statuses(s)).toEqual(['active', 'pending']);
  });

  it('REMOVE_DYNAMIC_STEP of the current LAST step clamps the index to the new last step', () => {
    const s0 = buildInitialShellState([cfg('a'), cfg('b'), cfg('c')], 'c');
    const s = wizardShellReducer(s0, { type: 'REMOVE_DYNAMIC_STEP', stepId: 'c' });
    expect(ids(s)).toEqual(['a', 'b']);
    expect(s.currentStepIndex).toBe(1);
    expect(statuses(s)).toEqual(['completed', 'active']);
  });

  it('REMOVE_DYNAMIC_STEP of an unknown id is a no-op (same reference)', () => {
    const s0 = buildInitialShellState([cfg('a')]);
    expect(wizardShellReducer(s0, { type: 'REMOVE_DYNAMIC_STEP', stepId: 'zzz' })).toBe(s0);
  });
});
