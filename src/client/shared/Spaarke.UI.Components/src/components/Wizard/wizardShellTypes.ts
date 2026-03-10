/**
 * wizardShellTypes.ts
 *
 * Generic, domain-free type definitions for the reusable WizardShell component.
 *
 * IMPORTANT: This file must have ZERO domain imports. Only React types are
 * permitted. All interfaces here are generic enough to drive any multi-step
 * wizard dialog — the domain-specific content is injected via renderContent
 * callbacks and IWizardStepConfig arrays.
 */
import type * as React from 'react';

// ---------------------------------------------------------------------------
// Step status
// ---------------------------------------------------------------------------

/** Visual status of a wizard step in the sidebar stepper. */
export type WizardStepStatus = 'pending' | 'active' | 'completed';

// ---------------------------------------------------------------------------
// Step descriptor (runtime state)
// ---------------------------------------------------------------------------

/**
 * Runtime representation of a single step displayed in the sidebar stepper.
 * This is the "state" shape — it tracks id, label, and current status.
 * Contrast with {@link IWizardStepConfig} which is the "configuration" shape
 * that also carries rendering logic and advancement predicates.
 */
export interface IWizardShellStep {
  /** Unique identifier for this step (used as React key and for step lookup). */
  id: string;
  /** Display label rendered in the sidebar stepper. */
  label: string;
  /** Current visual status of this step. */
  status: WizardStepStatus;
}

// ---------------------------------------------------------------------------
// Reducer actions (navigation only — no domain state)
// ---------------------------------------------------------------------------

/**
 * Discriminated union of actions that the WizardShell reducer handles.
 * These cover navigation and dynamic step management only. Domain-specific
 * actions (e.g., ADD_FILES, SET_FORM_VALUES) belong in the consumer's own
 * reducer, not here.
 */
export type WizardShellAction =
  | { type: 'NEXT_STEP' }
  | { type: 'PREV_STEP' }
  | { type: 'GO_TO_STEP'; stepIndex: number }
  | {
      type: 'ADD_DYNAMIC_STEP';
      /**
       * Configuration for the step to insert. Only `id` and `label` are used
       * by the reducer — the rest of the config is managed by the consumer.
       */
      config: IWizardStepConfig;
      /**
       * Optional canonical ordering of step IDs. When provided, the reducer
       * inserts dynamic steps in this order rather than appending to the end.
       * Steps not in the array are sorted after those that are.
       */
      canonicalOrder?: string[];
    }
  | {
      type: 'REMOVE_DYNAMIC_STEP';
      /** The `id` of the dynamic step to remove. */
      stepId: string;
    };

// ---------------------------------------------------------------------------
// Shell state
// ---------------------------------------------------------------------------

/**
 * Immutable state managed by the WizardShell reducer.
 * Contains only navigation and step-tracking concerns. Domain state
 * (uploaded files, form values, etc.) is managed externally by the consumer.
 */
export interface IWizardShellState {
  /** Zero-based index of the currently visible step. */
  currentStepIndex: number;
  /** Ordered step descriptors — includes both base and dynamic steps. */
  steps: IWizardShellStep[];
}

// ---------------------------------------------------------------------------
// Step configuration (provided by consumer)
// ---------------------------------------------------------------------------

/**
 * Configuration for a single wizard step, provided by the consumer.
 * This is the "definition" shape — it tells the shell how to render
 * the step content, whether the user can advance, and optional
 * per-step footer actions.
 */
export interface IWizardStepConfig {
  /** Unique identifier for this step (must match the runtime step id). */
  id: string;
  /** Display label rendered in the sidebar stepper. */
  label: string;
  /**
   * Render callback that produces the step's main content area.
   * Receives the {@link IWizardShellHandle} so the step can trigger
   * dynamic step insertion/removal if needed.
   */
  renderContent: (handle: IWizardShellHandle) => React.ReactNode;
  /**
   * Predicate that returns `true` when the user may advance past this step.
   * Called on every render to determine whether the Next/Finish button
   * is enabled.
   */
  canAdvance: () => boolean;
  /**
   * Optional predicate for the "early finish" pattern. When this returns
   * `true`, the shell treats the Next button as Finish — clicking it
   * triggers onFinish instead of advancing to the next step.
   *
   * Common use case: a "Next Steps" step where selecting 0 follow-on
   * actions means the wizard is done (no further dynamic steps needed).
   */
  isEarlyFinish?: () => boolean;
  /**
   * When `true`, a "Skip" button is shown in the footer that advances
   * to the next step without requiring `canAdvance()` to be true.
   * Intended for optional follow-on steps (e.g., Send Email, Work on Analysis)
   * that the user selected but may decide to skip during execution.
   */
  isSkippable?: boolean;
  /**
   * Optional extra actions rendered in the footer alongside the standard
   * Back/Next/Finish buttons. Use this for step-specific buttons like
   * "Reset Form" or "Preview".
   */
  footerActions?: React.ReactNode;
}

// ---------------------------------------------------------------------------
// Shell handle (imperative API for step content)
// ---------------------------------------------------------------------------

/**
 * Imperative handle passed to each step's `renderContent` callback.
 * Allows step content to interact with the shell (e.g., add/remove
 * dynamic steps) without needing direct access to the reducer dispatch.
 */
export interface IWizardShellHandle {
  /**
   * Add a dynamic step to the wizard. If a step with the same `id`
   * already exists, this is a no-op.
   *
   * @param config - Full step configuration for the new dynamic step.
   * @param canonicalOrder - Optional array of step IDs defining insertion
   *   order. Dynamic steps are sorted according to their position in this
   *   array. Steps not in the array are appended after sorted ones.
   */
  addDynamicStep(config: IWizardStepConfig, canonicalOrder?: string[]): void;
  /**
   * Remove a dynamic step by its `id`. If no step with that `id` exists,
   * this is a no-op. The current step index is clamped if the removal
   * would leave it out of bounds.
   *
   * @param stepId - The unique identifier of the step to remove.
   */
  removeDynamicStep(stepId: string): void;
  /** Read-only snapshot of the current shell state. */
  readonly state: IWizardShellState;
}

// ---------------------------------------------------------------------------
// Success configuration
// ---------------------------------------------------------------------------

/**
 * Configuration for the success screen displayed after the wizard's
 * `onFinish` callback resolves. The shell replaces all step content
 * with this success view and hides the standard footer.
 */
export interface IWizardSuccessConfig {
  /** Icon or illustration displayed above the title (e.g., a checkmark). */
  icon: React.ReactNode;
  /** Primary success message (e.g., "Matter created successfully"). */
  title: string;
  /** Body content below the title — can be a string or rich JSX. */
  body: React.ReactNode;
  /** Action buttons displayed below the body (e.g., "Open Matter", "Close"). */
  actions: React.ReactNode;
  /**
   * Optional warning messages to display alongside the success content.
   * Used when the operation succeeded but with caveats (e.g., partial
   * follow-on action failures).
   */
  warnings?: string[];
}

// ---------------------------------------------------------------------------
// Shell props
// ---------------------------------------------------------------------------

/**
 * Props for the WizardShell component — the generic, reusable wizard dialog.
 * Consumers provide step configurations, an onFinish callback, and optional
 * customization for labels. The shell handles layout (sidebar stepper,
 * content area, footer), navigation, and the finishing/success flow.
 */
export interface IWizardShellProps {
  /** Whether the wizard dialog is currently open (visible). */
  open: boolean;
  /**
   * When `true`, the shell renders as a full-page layout without the Fluent
   * `<Dialog>` overlay wrapper. Use this when the wizard is already hosted
   * inside a Dataverse dialog (e.g., a Code Page opened via `navigateTo`).
   * Defaults to `false`.
   */
  embedded?: boolean;
  /** Title displayed in the wizard's custom title bar. */
  title: string;
  /**
   * Accessible label for the dialog surface. Falls back to {@link title}
   * if not provided.
   */
  ariaLabel?: string;
  /**
   * Ordered array of step configurations. The shell builds its initial
   * step list from these configs. Additional steps can be added at runtime
   * via {@link IWizardShellHandle.addDynamicStep}.
   */
  steps: IWizardStepConfig[];
  /** Callback invoked when the user clicks Cancel or the close (X) button. */
  onClose: () => void;
  /**
   * Async callback invoked when the user clicks Finish (on the last step
   * or when {@link IWizardStepConfig.isEarlyFinish} returns true).
   *
   * Return an {@link IWizardSuccessConfig} to display a success screen,
   * or return `void` / `undefined` to close the dialog without a success
   * screen (the shell will call {@link onClose} automatically).
   */
  onFinish: () => Promise<IWizardSuccessConfig | void>;
  /**
   * Label shown on the primary button while the `onFinish` promise is
   * pending. Defaults to "Processing...".
   */
  finishingLabel?: string;
  /**
   * Label shown on the primary button when on the last step (or early
   * finish). Defaults to "Finish".
   */
  finishLabel?: string;
}
