/**
 * wizardShellReducer.ts
 *
 * Pure reducer and initializer for the generic WizardShell navigation state.
 *
 * IMPORTANT: This file must have ZERO domain imports. It operates exclusively
 * on the generic types defined in wizardShellTypes.ts. All domain-specific
 * logic (file uploads, form values, follow-on actions) belongs in the
 * consumer's own reducer, not here.
 */
import type { IWizardShellState, IWizardShellStep, IWizardStepConfig, WizardShellAction } from './wizardShellTypes';

// ---------------------------------------------------------------------------
// Initial state builder
// ---------------------------------------------------------------------------

/**
 * Build the initial WizardShell state from an ordered array of step configs.
 *
 * The first step is marked 'active'; all subsequent steps are 'pending'.
 * Only `id`, `label`, and `status` are extracted — rendering callbacks and
 * predicates remain in the config array managed by the consumer.
 *
 * @param steps - Ordered step configurations provided by the consumer.
 * @returns A fresh IWizardShellState with currentStepIndex = 0.
 */
export function buildInitialShellState(steps: ReadonlyArray<IWizardStepConfig>): IWizardShellState {
  const shellSteps: IWizardShellStep[] = steps.map((config, index) => ({
    id: config.id,
    label: config.label,
    status: index === 0 ? 'active' : 'pending',
  }));

  return {
    currentStepIndex: 0,
    steps: shellSteps,
  };
}

// ---------------------------------------------------------------------------
// Helper: rebuild step statuses around a target index
// ---------------------------------------------------------------------------

/**
 * Return a new steps array with statuses set relative to `activeIndex`:
 *   - indices < activeIndex  -> 'completed'
 *   - index === activeIndex  -> 'active'
 *   - indices > activeIndex  -> 'pending'
 */
function rebuildStatuses(steps: IWizardShellStep[], activeIndex: number): IWizardShellStep[] {
  return steps.map((step, i) => {
    if (i < activeIndex) return { ...step, status: 'completed' };
    if (i === activeIndex) return { ...step, status: 'active' };
    return { ...step, status: 'pending' };
  });
}

// ---------------------------------------------------------------------------
// Helper: extract IWizardShellStep from IWizardStepConfig
// ---------------------------------------------------------------------------

function toShellStep(config: IWizardStepConfig): IWizardShellStep {
  return {
    id: config.id,
    label: config.label,
    status: 'pending',
  };
}

// ---------------------------------------------------------------------------
// Reducer
// ---------------------------------------------------------------------------

/**
 * Pure reducer for WizardShell navigation state.
 *
 * Handles step advancement, backward navigation, direct jump, and dynamic
 * step insertion/removal. No side effects, no domain-specific logic.
 *
 * @param state  - Current shell state.
 * @param action - One of the WizardShellAction discriminated union members.
 * @returns Updated shell state (new object if changed, same reference if no-op).
 */
export function wizardShellReducer(state: IWizardShellState, action: WizardShellAction): IWizardShellState {
  switch (action.type) {
    // ----- NEXT_STEP --------------------------------------------------------
    case 'NEXT_STEP': {
      const nextIndex = state.currentStepIndex + 1;
      if (nextIndex >= state.steps.length) return state; // already at last step

      return {
        ...state,
        currentStepIndex: nextIndex,
        steps: rebuildStatuses(state.steps, nextIndex),
      };
    }

    // ----- PREV_STEP --------------------------------------------------------
    case 'PREV_STEP': {
      const prevIndex = state.currentStepIndex - 1;
      if (prevIndex < 0) return state; // already at first step

      return {
        ...state,
        currentStepIndex: prevIndex,
        steps: rebuildStatuses(state.steps, prevIndex),
      };
    }

    // ----- GO_TO_STEP -------------------------------------------------------
    case 'GO_TO_STEP': {
      const targetIndex = Math.max(0, Math.min(action.stepIndex, state.steps.length - 1));

      return {
        ...state,
        currentStepIndex: targetIndex,
        steps: rebuildStatuses(state.steps, targetIndex),
      };
    }

    // ----- ADD_DYNAMIC_STEP -------------------------------------------------
    case 'ADD_DYNAMIC_STEP': {
      // No-op if a step with the same id already exists
      if (state.steps.some(s => s.id === action.config.id)) {
        return state;
      }

      const newStep = toShellStep(action.config);

      let updatedSteps: IWizardShellStep[];

      if (action.canonicalOrder && action.canonicalOrder.length > 0) {
        // Canonical ordering: insert the new step at the position dictated by
        // the canonical array. Steps whose IDs appear in canonicalOrder are
        // sorted by their index in that array. Steps whose IDs are NOT in the
        // array keep their relative order and come after all canonical steps.
        const allSteps = [...state.steps, newStep];

        // Build a sort key: canonical index if present, otherwise Infinity to
        // preserve original relative ordering at the end.
        const orderMap = new Map<string, number>();
        action.canonicalOrder.forEach((id, idx) => orderMap.set(id, idx));

        // Stable sort: steps in canonicalOrder are placed by their canonical
        // index; steps NOT in canonicalOrder keep their original position
        // relative to each other and relative to the canonical block.
        //
        // Strategy: walk through canonical order positions and insert steps
        // that match. Non-canonical steps stay in their original sequence.
        const canonicalSteps: IWizardShellStep[] = [];
        const nonCanonicalSteps: IWizardShellStep[] = [];

        for (const step of allSteps) {
          if (orderMap.has(step.id)) {
            canonicalSteps.push(step);
          } else {
            nonCanonicalSteps.push(step);
          }
        }

        // Sort the canonical steps by their position in the canonical array
        canonicalSteps.sort((a, b) => (orderMap.get(a.id) ?? 0) - (orderMap.get(b.id) ?? 0));

        // Merge: non-canonical steps first (these are the base steps that were
        // already present), then canonical steps in order.
        updatedSteps = [...nonCanonicalSteps, ...canonicalSteps];
      } else {
        // No canonical order: append after current steps
        updatedSteps = [...state.steps, newStep];
      }

      // Re-apply statuses relative to current step index (the active step
      // hasn't changed — we only added a pending step)
      return {
        ...state,
        steps: rebuildStatuses(updatedSteps, state.currentStepIndex),
      };
    }

    // ----- REMOVE_DYNAMIC_STEP ----------------------------------------------
    case 'REMOVE_DYNAMIC_STEP': {
      const removeIndex = state.steps.findIndex(s => s.id === action.stepId);
      if (removeIndex === -1) return state; // step not found — no-op

      const filtered = state.steps.filter(s => s.id !== action.stepId);

      let newIndex = state.currentStepIndex;

      if (removeIndex < state.currentStepIndex) {
        // Removed step was before current — shift index back by one
        newIndex = state.currentStepIndex - 1;
      } else if (removeIndex === state.currentStepIndex) {
        // Current step was removed — move to previous (or 0 if at start)
        newIndex = Math.max(0, state.currentStepIndex - 1);
      }
      // else: removed step was after current — index stays the same

      // Clamp to valid range
      newIndex = Math.min(newIndex, filtered.length - 1);

      return {
        ...state,
        currentStepIndex: newIndex,
        steps: rebuildStatuses(filtered, newIndex),
      };
    }

    default:
      return state;
  }
}
