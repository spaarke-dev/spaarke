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
import type { SprkModalDismiss, SprkModalNav } from '../SprkModal/SprkModal.types';
import type { SprkModalSize } from '../SprkModal/sizes';

// ---------------------------------------------------------------------------
// Step status
// ---------------------------------------------------------------------------

/**
 * Visual status of a wizard step in the sidebar stepper.
 *
 * `'skipped'` (ontology task 056; opt-in via `showSkippedSteps`, D-69): the user left the step with
 * **Skip** — the stepper shows it without a tick. Without the opt-in, Skip marks the step `'completed'`. A skipped step keeps that status while the user moves elsewhere; it becomes `'completed'` only if
 * the user returns to it and leaves with Next.
 */
export type WizardStepStatus = 'pending' | 'active' | 'completed' | 'skipped';

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
  /** Advance like NEXT_STEP, but mark the step being left `'skipped'` (no tick). */
  | { type: 'SKIP_STEP' }
  | { type: 'PREV_STEP' }
  | {
      type: 'GO_TO_STEP';
      stepIndex: number;
      /** When `true`, forget every `'skipped'` mark (used when the wizard is re-opened). */
      clearSkipped?: boolean;
    }
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
  /**
   * Request the shell to re-render, re-evaluating `canAdvance()` and other
   * derived values. Use this when step content changes state that affects
   * `canAdvance()` but doesn't otherwise trigger a shell re-render.
   */
  requestUpdate(): void;
  /**
   * Advance to the next step programmatically.
   * Equivalent to the user clicking Next / Skip in the footer.
   * No-op if the wizard is already on the last step or is finishing.
   */
  nextStep(): void;
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
  /**
   * Action buttons displayed in the footer (right side, where Next/Finish normally are).
   * Typically a "Done" or "Close" button.
   */
  actions: React.ReactNode;
  /**
   * Optional warning messages to display alongside the success content.
   * Used when the operation succeeded but with caveats (e.g., partial
   * follow-on action failures).
   */
  warnings?: string[];
}

// ---------------------------------------------------------------------------
// Footer override (ontology task 056)
// ---------------------------------------------------------------------------

/**
 * What a {@link IWizardShellProps.footer} override can read and do. The navigation callbacks are the
 * shell's own (Back = the standard Back; Next = advance without the `canAdvance` check; Close = `onClose`),
 * so an override can rebuild "Close · Back · Next open item" without reaching into the reducer.
 */
export interface IWizardFooterContext {
  /** `id` of the step being shown (undefined only when there are no steps). */
  currentStepId: string | undefined;
  isFirstStep: boolean;
  isLastStep: boolean;
  /** `true` while `onFinish` is pending. */
  isFinishing: boolean;
  /** The current step's `canAdvance()` result. */
  canAdvance: boolean;
  /** Go to the previous step (no-op on the first step). */
  goBack(): void;
  /** Go to the next step (no-op on the last step). */
  goNext(): void;
  /** Call `onClose`. */
  close(): void;
}

/**
 * Footer slots returned by a {@link IWizardShellProps.footer} override. A slot you supply replaces that
 * side of the standard footer; a slot you leave `undefined` keeps the standard content for that side.
 */
export interface IWizardFooterSlots {
  /** Left side — replaces Cancel + `footerLeftExtra`. */
  start?: React.ReactNode;
  /** Right side — replaces the spinner, per-step actions and Skip · Back · Next/Finish. */
  end?: React.ReactNode;
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
   * When `true`, the shell renders as a full-page layout with no modal envelope. Use it for workspace
   * tabs, full pages, and pages hosted under platform chrome (a Code Page opened via `navigateTo` —
   * pair with `hideTitle`). When `false` (default) the wizard renders inside `SprkModal` (ADR-050 as
   * amended 2026-10-07: `WizardShell` is the wizard preset). Defaults to `false`.
   */
  embedded?: boolean;
  /** Title: the `SprkModal` header title (modal), or the custom title bar (embedded). */
  title: string;
  /**
   * When `true`, hides the wizard's custom title bar. Use this when the wizard
   * is hosted inside a Dataverse dialog that already provides its own chrome
   * (title bar + close button via `navigateTo` target: 2).
   * **Embedded mode only** — the modal header is `SprkModal`'s and always shows.
   * Defaults to `false`.
   */
  hideTitle?: boolean;
  /**
   * Accessible label for the embedded root. Falls back to {@link title} if not provided.
   * **Embedded mode only** — in modal mode the dialog is named by its header title
   * (`SprkModal`'s `aria-labelledby`).
   */
  ariaLabel?: string;
  /**
   * Named modal size (modal mode only). Default `'wizard'` (62vw × min(74vh, 760px)).
   * @since ontology task 056
   */
  size?: SprkModalSize;
  /**
   * Dismiss semantics (modal mode only). Default `'explicit'`: Escape and the backdrop do NOT close
   * the wizard; × and Cancel do (ADR-050 as amended 2026-10-07).
   * @since ontology task 056
   */
  dismiss?: SprkModalDismiss;
  /**
   * The `--sprk-ui-scale` factor (modal mode only), forwarded to `SprkModal`. Pass the same value the
   * host passes to `scaleTheme`. Default 1.
   * @since ontology task 056
   */
  uiScale?: number;
  /**
   * Browse navigation ("‹ N of M ›") in the modal header, rendered by `SprkModal`'s own nav group
   * (modal mode only). Put a discard check in `nav.onBeforeNavigate`.
   * @since ontology task 056
   */
  nav?: SprkModalNav;
  /**
   * Content rendered at the top of the content area, above the step content (and the success screen),
   * on every step — e.g. the status bar of an already-decided item. Both modes.
   * @since ontology task 056
   */
  statusBar?: React.ReactNode;
  /**
   * Footer override. Called on every render while a step is shown (not on the success screen); return
   * slots to replace either side of the standard footer, or `null` / `undefined` to keep the standard
   * footer. Both modes.
   * @since ontology task 056
   */
  footer?: (context: IWizardFooterContext) => IWizardFooterSlots | null | undefined;
  /**
   * When `true`, the wizard stays open when `onFinish` resolves with nothing (the default closes it by
   * calling `onClose`). A returned success config still shows the success screen.
   * @since ontology task 056
   */
  stayOpenOnFinish?: boolean;
  /**
   * Opt-in (D-69). When `true`, a step left with **Skip** is marked `'skipped'` and the stepper shows an
   * empty dashed ring instead of a tick. Default `false`: Skip ticks the step like Next does (the
   * behaviour every existing wizard relies on). Both modes.
   * @since ontology task 056
   */
  showSkippedSteps?: boolean;
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
   * screen (the shell will call {@link onClose} automatically, unless
   * {@link stayOpenOnFinish} is set).
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
  /**
   * Extra content rendered in the footer's left side, after the Cancel button.
   * Use for additional actions like "Delete" that apply to the whole wizard.
   */
  footerLeftExtra?: React.ReactNode;
  /**
   * Optional step id to open the wizard at. When the id matches one of the
   * step configs, that step becomes 'active' and all earlier steps are marked
   * 'completed'. When absent (default) OR the id doesn't match, the wizard
   * opens at the first step.
   *
   * Use for edit flows where prior steps (template selection, section list)
   * are pre-populated from the existing record and the operator should land
   * on the working step directly.
   *
   * NOTE: `initialStepId` is captured on mount by `useReducer` — changing it
   * after mount has no effect. Consumers that need to jump between steps
   * post-mount should use the imperative `nextStep`/`prevStep` handle
   * methods.
   *
   * @since R2 UAT §3.1 (2026-07-03)
   */
  initialStepId?: string;
}
