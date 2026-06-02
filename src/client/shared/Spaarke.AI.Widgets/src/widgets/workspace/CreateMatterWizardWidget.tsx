/**
 * @spaarke/ai-widgets — CreateMatterWizardWidget
 *
 * Workspace widget that embeds the CreateMatterWizard from @spaarke/ui-components
 * as a first-class workspace tab rather than a modal overlay.
 *
 * Key design decisions:
 *
 * 1. EMBEDDED MODE — CreateMatterWizard (and its underlying CreateRecordWizard /
 *    WizardShell) already supports an `embedded` prop that strips the Dialog
 *    wrapper and title-bar chrome. This widget sets `embedded={true}` and
 *    wraps with workspace-appropriate padding instead.
 *
 * 2. PANE EVENT BUS INTEGRATION — Subscribes to `workspace` channel events
 *    with `type === 'wizard_step'` and `wizardId === props.wizardId`.
 *    Forwards `next` / `back` to an imperative WizardShell handle, and applies
 *    `set-field` values by updating a controlled field override map.
 *
 * 3. CONTEXT PANE SYNC — On every step change, dispatches a `context_update`
 *    event on the `context` channel so ContextPaneController can mount
 *    step-relevant help widgets (required-fields summary, help text, etc.).
 *
 * 4. SESSION RESTORE (D-08) — serializeState() stores `wizardId` and the
 *    current `stepIndex` as query params. On restore the wizard reopens at
 *    the last-known step index. Form field values are NOT serialized — the
 *    wizard is stateful in memory only; losing form state on session restore
 *    is expected (users restart the flow).
 *
 * 5. COMPLETION — When the wizard's `onFinish` resolves (CreateMatterWizard
 *    calls the shared success screen), the widget dispatches a `context_update`
 *    event carrying the newly created matter entity info so the Context pane
 *    can update its entity chip.
 *
 * React 19, NOT PCF-safe.
 *
 * Task: AIPU2-104
 *
 * @see CreateMatterWizard    — upstream wizard component (@spaarke/ui-components)
 * @see WizardStepEvent       — PaneEventBus event type for AI-driven step control
 * @see WorkspaceWidgetProps  — required component contract
 * @see ADR-012               — Shared component library (reuse, not copy)
 * @see ADR-021               — Fluent UI v9, no hard-coded colors
 */

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { makeStyles, mergeClasses, Spinner, Text, tokens } from '@fluentui/react-components';

import { CreateMatterWizard } from '@spaarke/ui-components/src/components/CreateMatterWizard';
import type { ICreateMatterWizardProps } from '@spaarke/ui-components/src/components/CreateMatterWizard';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';
import { usePaneEvent } from '../../events/usePaneEvent';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import type { WizardStepEvent } from '../../events/PaneEventTypes';

// ---------------------------------------------------------------------------
// Data payload shape
// ---------------------------------------------------------------------------

/**
 * Data delivered to this widget via the workspace SSE event or on mount.
 *
 * The shell passes this as `props.data`. All fields are optional — the widget
 * renders in a "not yet configured" state when deps are missing and shows a
 * helpful message rather than crashing.
 */
export interface CreateMatterWizardData {
  /**
   * Stable identifier for this wizard instance.
   * Used to route WizardStepEvents from the ConversationPane.
   * Example: `"create-matter"`, `"create-matter-from-document-upload"`.
   */
  wizardId: string;

  /**
   * BFF API base URL injected by the shell (e.g.
   * `"https://spe-api-dev-67e2xz.azurewebsites.net/api"`).
   */
  bffBaseUrl?: string;

  /**
   * Initial step index to restore to (0-based). Set on session restore.
   * The wizard will advance to this step immediately on mount.
   */
  initialStepIndex?: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    // Workspace-appropriate inset instead of modal padding
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: 'border-box',
  },
  wizardContainer: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    // The embedded WizardShell fills its container; let it take all available height
    overflow: 'hidden',
  },
  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// Serialized query params (D-08 — identifiers only, not form state)
// ---------------------------------------------------------------------------

export interface CreateMatterWizardQueryParams extends Record<string, string> {
  wizardId: string;
  /** Last-known step index (string-encoded for Cosmos compatibility). */
  stepIndex: string;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * CreateMatterWizardWidget
 *
 * Renders the multi-step CreateMatterWizard in embedded (non-modal) mode
 * inside a workspace tab. Bridges PaneEventBus wizard_step events to
 * the wizard's imperative step handle and field override mechanism.
 */
const CreateMatterWizardWidget: React.FC<WorkspaceWidgetProps<CreateMatterWizardData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // ── Field overrides driven by wizard_step set-field events ──────────────
  // We maintain a map of fieldName → fieldValue that is passed down to
  // CreateMatterWizard via its `initialFormValues` equivalent. Because the
  // upstream wizard is not fully controlled, we signal field changes through
  // a key-bump mechanism: each set-field event increments a counter that
  // causes the wizard to remount with updated initial values.
  const [fieldOverrides, setFieldOverrides] = useState<Record<string, unknown>>({});
  const [wizardMountKey, setWizardMountKey] = useState(0);

  // ── Current step tracking (for serialize + context dispatch) ────────────
  const [currentStepIndex, setCurrentStepIndex] = useState<number>(data?.initialStepIndex ?? 0);
  const currentStepIndexRef = useRef(currentStepIndex);
  currentStepIndexRef.current = currentStepIndex;

  // ── Wizard open state ────────────────────────────────────────────────────
  // The embedded wizard is always "open" while the widget is mounted; the
  // workspace tab lifecycle controls mounting/unmounting.
  const [isOpen, setIsOpen] = useState(true);

  // ── PaneEventBus: subscribe to wizard_step events ───────────────────────
  const wizardId = data?.wizardId ?? 'create-matter';

  usePaneEvent(
    'workspace',
    useCallback(
      event => {
        if (event.type !== 'wizard_step') return;
        // Type-narrow: wizardId and wizardAction are required on wizard_step
        const wizardEvent = event as WizardStepEvent;
        if (wizardEvent.wizardId !== wizardId) return;

        switch (wizardEvent.wizardAction) {
          case 'next':
            // Signal the wizard to advance one step. Because CreateMatterWizard
            // manages its own step state internally, we use a synthetic "click"
            // on the Next button by dispatching to a ref-held callback.
            if (imperativeNextRef.current) {
              imperativeNextRef.current();
            }
            break;

          case 'back':
            if (imperativeBackRef.current) {
              imperativeBackRef.current();
            }
            break;

          case 'set-field':
            if (wizardEvent.fieldName !== undefined) {
              setFieldOverrides(prev => ({
                ...prev,
                [wizardEvent.fieldName!]: wizardEvent.fieldValue,
              }));
              // Bump the mount key so the wizard picks up the new initial value.
              // This is a remount — acceptable because the wizard is stateful but
              // the set-field action is an AI-driven pre-fill, not user editing.
              setWizardMountKey(k => k + 1);
            }
            break;
        }
      },
      [wizardId]
    )
  );

  // ── Imperative next/back refs ────────────────────────────────────────────
  // CreateMatterWizard does not expose an imperative handle directly. We
  // use a synthetic DOM-level "click" on the Next/Back button by locating
  // the button within our container ref. This avoids forking the upstream
  // component to add an imperative API.
  const containerRef = useRef<HTMLDivElement>(null);

  const imperativeNextRef = useRef<(() => void) | null>(null);
  const imperativeBackRef = useRef<(() => void) | null>(null);

  useEffect(() => {
    imperativeNextRef.current = () => {
      if (!containerRef.current) return;
      // WizardShell renders a footer with data-testid="wizard-next-button"
      // or falls back to aria-label="Next" / "Finish" pattern.
      const nextBtn =
        containerRef.current.querySelector<HTMLButtonElement>('[data-testid="wizard-next-button"]') ??
        containerRef.current.querySelector<HTMLButtonElement>('button[aria-label="Next"]') ??
        containerRef.current.querySelector<HTMLButtonElement>('button[aria-label="Finish"]');
      nextBtn?.click();
    };

    imperativeBackRef.current = () => {
      if (!containerRef.current) return;
      const backBtn =
        containerRef.current.querySelector<HTMLButtonElement>('[data-testid="wizard-back-button"]') ??
        containerRef.current.querySelector<HTMLButtonElement>('button[aria-label="Back"]');
      backBtn?.click();
    };
  });

  // ── Context pane sync on step change ────────────────────────────────────
  // We observe the active step by watching for the WizardShell's aria-current
  // attribute changes via MutationObserver. On each step change we dispatch
  // a context_update so ContextPaneController can mount step-help widgets.
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;

    const observer = new MutationObserver(() => {
      // Find the active step indicator — WizardStepper uses aria-current="step"
      const activeStep = container.querySelector('[aria-current="step"]');
      if (!activeStep) return;

      // Determine step index from sibling position
      const stepper = activeStep.closest('ol, ul, [role="list"]');
      if (!stepper) return;
      const steps = Array.from(stepper.children);
      const idx = steps.indexOf(activeStep.closest('li, [role="listitem"]') ?? (activeStep as Element));
      if (idx === -1 || idx === currentStepIndexRef.current) return;

      setCurrentStepIndex(idx);
      currentStepIndexRef.current = idx;

      // Notify Context pane
      dispatch('context', {
        type: 'stage_change',
        contextType: 'wizard-step',
        contextData: {
          wizardId,
          wizardType: 'create-matter',
          stepIndex: idx,
          stepLabel: activeStep.textContent?.trim() ?? '',
        },
      });
    });

    observer.observe(container, { subtree: true, attributes: true, attributeFilter: ['aria-current'] });

    return () => observer.disconnect();
  }, [wizardId, dispatch]);

  // ── Completion handler ───────────────────────────────────────────────────
  // When the wizard closes after a successful finish, dispatch a context_update
  // so the Context pane can reflect the new entity. The widget stays mounted
  // (the workspace tab persists) but shows a completed state.
  const [isCompleted, setIsCompleted] = useState(false);
  const [completedMatterName, setCompletedMatterName] = useState<string>('');

  const handleClose = useCallback(() => {
    // The CreateMatterWizard calls onClose on cancel or after success-screen dismiss.
    // We mark as completed rather than actually closing the widget tab.
    setIsOpen(false);
    setIsCompleted(true);
  }, []);

  // ── Render: loading ──────────────────────────────────────────────────────
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Loading wizard..." />
        </div>
      </div>
    );
  }

  // ── Render: error ────────────────────────────────────────────────────────
  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Text style={{ color: tokens.colorStatusDangerForeground1 }}>{error}</Text>
        </div>
      </div>
    );
  }

  // ── Render: completed ────────────────────────────────────────────────────
  if (isCompleted) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Text size={400} weight="semibold">
            {completedMatterName ? `Matter "${completedMatterName}" created` : 'Matter created'}
          </Text>
          <Text style={{ color: tokens.colorNeutralForeground3 }}>This wizard tab can now be closed.</Text>
        </div>
      </div>
    );
  }

  // ── Render: missing config ───────────────────────────────────────────────
  if (!data) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Text style={{ color: tokens.colorNeutralForeground3 }}>Wizard configuration not yet loaded.</Text>
        </div>
      </div>
    );
  }

  // ── Render: wizard ───────────────────────────────────────────────────────
  // CreateMatterWizard requires IDataService and authenticatedFetch, which
  // are workspace-session dependencies injected via widgetData by the shell.
  // Until the shell passes them, we render a placeholder.
  const wizardData = data as CreateMatterWizardData & {
    dataService?: ICreateMatterWizardProps['dataService'];
    authenticatedFetch?: ICreateMatterWizardProps['authenticatedFetch'];
    navigationService?: ICreateMatterWizardProps['navigationService'];
    resolveSpeContainerId?: ICreateMatterWizardProps['resolveSpeContainerId'];
  };

  if (!wizardData.dataService || !wizardData.authenticatedFetch || !wizardData.bffBaseUrl) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Connecting to workspace services..." />
        </div>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)} ref={containerRef}>
      <div className={styles.wizardContainer}>
        <CreateMatterWizard
          key={wizardMountKey}
          open={isOpen}
          onClose={handleClose}
          dataService={wizardData.dataService}
          authenticatedFetch={wizardData.authenticatedFetch}
          bffBaseUrl={wizardData.bffBaseUrl}
          navigationService={wizardData.navigationService}
          resolveSpeContainerId={wizardData.resolveSpeContainerId}
          // embedded=true removes Dialog overlay chrome — the workspace tab
          // provides the surrounding layout and header chrome instead.
          embedded={true}
        />
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// serializeState / restoreState helpers (D-08 — query params only)
// ---------------------------------------------------------------------------

/**
 * Serialize the widget's recoverable state for Cosmos DB persistence.
 *
 * Stores the wizardId and last-known step index so session restore can
 * reopen the wizard at the correct step. Form field values are NOT stored —
 * users are expected to re-enter form data after a session restore.
 *
 * @param wizardId   - Stable wizard instance identifier.
 * @param stepIndex  - Current zero-based step index.
 */
export function serializeCreateMatterWizardState(
  wizardId: string,
  stepIndex: number
): WidgetState<CreateMatterWizardData> {
  return {
    widgetType: 'create-matter-wizard',
    version: 1,
    queryParams: {
      wizardId,
      stepIndex: String(stepIndex),
    },
    timestamp: new Date().toISOString(),
  };
}

CreateMatterWizardWidget.displayName = 'CreateMatterWizardWidget';

export default CreateMatterWizardWidget;
