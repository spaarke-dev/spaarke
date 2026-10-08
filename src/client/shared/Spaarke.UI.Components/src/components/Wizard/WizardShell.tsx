/**
 * WizardShell.tsx
 *
 * Generic, domain-free wizard engine — and the wizard preset of the canonical modal shell
 * (ADR-050 as amended 2026-10-07, D-26).
 *
 * Two render modes, one engine:
 *
 *   Modal (default, `embedded={false}`) — renders INSIDE `SprkModal`, which owns the
 *   envelope, header (‹ N of M › nav · title · maximize · ×), body and footer slots:
 *     SprkModal size={size ?? 'wizard'} dismiss={dismiss ?? 'explicit'} uiScale nav
 *       footerStart = Cancel + footerLeftExtra       footer = [spinner] [step actions] Skip · Back · Next/Finish
 *       body        = WizardStepper sidebar | statusBar + error bar + step content / success screen
 *
 *   Embedded (`embedded`) — no envelope; fills its host (workspace tab, full page, or a Code Page
 *   under `navigateTo` platform chrome with `hideTitle`). Its markup is UNCHANGED by the re-base
 *   (locked by the characterization snapshot, ontology task 056).
 *
 * The shell handles:
 *   - Navigation state via useReducer (wizardShellReducer), incl. 'skipped' steps
 *   - Dynamic step insertion/removal via imperative handle
 *   - Finish flow with async onFinish, error display, success screen, optional stay-open
 *   - Footer button logic, with an optional footer override
 *
 * Domain-specific content is injected via IWizardStepConfig.renderContent
 * callbacks. The shell has ZERO domain imports.
 *
 * Constraints:
 *   - Fluent v9 only, semantic tokens only — ZERO hardcoded colors (ADR-021)
 *   - React 16/17-safe (PCF consumers) — no React 18-only APIs
 *   - No domain-specific imports
 *
 * History: task 080 (spaarke-modal-system) aligned the header/footer TOKENS to SprkModal; ontology
 * task 056 replaced the shell's own Fluent `Dialog` with SprkModal itself (P2) — intended UAT changes:
 * Escape / backdrop no longer close a wizard (dismiss 'explicit'), the old `resize: both` surface is
 * gone, and the header title is SprkModal's `aria-labelledby` span.
 */

import * as React from 'react';
import { Button, MessageBar, MessageBarBody, Text, Spinner, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';

import { ModalWindowControls } from '../ModalWindowControls';
import { SprkModal } from '../SprkModal/SprkModal';
import { WizardStepper } from './WizardStepper';
import { WizardSuccessScreen } from './WizardSuccessScreen';
import { wizardShellReducer, buildInitialShellState } from './wizardShellReducer';
import type {
  IWizardShellProps,
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
  IWizardFooterContext,
} from './wizardShellTypes';

// ---------------------------------------------------------------------------
// Styles (generic shell layout only)
// ---------------------------------------------------------------------------
// The embedded-mode slots (embeddedRoot, titleBar, titleText, mainArea, contentArea, footer*,
// progressRow) are unchanged by the SprkModal re-base: their generated class names are part of the
// locked embedded markup. Modal-only additions get their own slots.

const useStyles = makeStyles({
  // Embedded mode: fills the host container (e.g., Dataverse dialog iframe)
  embeddedRoot: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Custom title bar (embedded mode only — in modal mode SprkModal owns the header).
  titleBar: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    flexShrink: 0,
  },
  // Ellipsized title — matches SprkModal's title token set exactly.
  titleText: {
    flex: '1 1 auto',
    minWidth: 0,
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  // Main body: sidebar + content side by side
  mainArea: {
    display: 'flex',
    flex: '1 1 auto',
    overflow: 'hidden',
  },
  // Modal mode: SprkModal's body is a block scroll container, so the sidebar + content row must fill
  // its height explicitly (the content area scrolls; the stepper stays put).
  mainAreaFill: {
    height: '100%',
  },
  // Content area (right of sidebar)
  contentArea: {
    flex: '1 1 auto',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    overflowY: 'auto',
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalL,
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
  },
  // Footer (embedded mode — in modal mode SprkModal renders the footer from the same slot content).
  footer: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    flexShrink: 0,
  },
  footerBetween: { justifyContent: 'space-between' },
  footerEnd: { justifyContent: 'flex-end' },
  footerSlot: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  // Progress indicator row (spinner + label)
  progressRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
  },
});

// ---------------------------------------------------------------------------
// WizardShell (exported — forwardRef)
// ---------------------------------------------------------------------------

export const WizardShell = React.forwardRef<IWizardShellHandle, IWizardShellProps>((props, ref) => {
  const {
    open,
    embedded = false,
    title,
    hideTitle = false,
    ariaLabel,
    steps: stepConfigs,
    onClose,
    onFinish,
    finishingLabel = 'Processing…',
    finishLabel = 'Finish',
    footerLeftExtra,
    // Deprecated raw-string sizing (v1.1.63). Honoured in modal mode through SprkModal's
    // transitional `legacySize`; task 111 maps the remaining callers to named sizes.
    maxWidth,
    height,
    initialStepId,
    // ontology task 056 — additive props (modal-only where noted in wizardShellTypes)
    size = 'wizard',
    dismiss = 'explicit',
    uiScale,
    nav,
    statusBar,
    footer: footerOverride,
    stayOpenOnFinish = false,
    showSkippedSteps = false,
  } = props;

  const styles = useStyles();

  // ── Navigation state via reducer ───────────────────────────────────────
  // R2 UAT §3.1 (2026-07-03): pass `initialStepId` through the lazy-init
  // arg so the wizard opens at the operator-requested step (edit-mode flows).
  // `useReducer`'s third arg is invoked ONCE on mount; changing `initialStepId`
  // later has no effect (consumers use the imperative handle to jump instead).
  const initArgRef = React.useRef({ stepConfigs, initialStepId });
  const [shellState, dispatch] = React.useReducer(wizardShellReducer, initArgRef.current, arg =>
    buildInitialShellState(arg.stepConfigs, arg.initialStepId)
  );

  // ── Step config lookup map ─────────────────────────────────────────────
  const configMapRef = React.useRef<Map<string, IWizardStepConfig>>(new Map());

  // ── Force-update counter (for requestUpdate) ─────────────────────────
  const [, forceRender] = React.useReducer((x: number) => x + 1, 0);

  // ── Finishing flow state ───────────────────────────────────────────────
  const [isFinishing, setIsFinishing] = React.useState(false);
  const [finishError, setFinishError] = React.useState<string | null>(null);
  const [successConfig, setSuccessConfig] = React.useState<IWizardSuccessConfig | null>(null);

  // ── Imperative handle ──────────────────────────────────────────────────
  React.useImperativeHandle(
    ref,
    () => ({
      addDynamicStep(config: IWizardStepConfig, canonicalOrder?: string[]) {
        // Add config to the lookup map
        configMapRef.current.set(config.id, config);
        // Dispatch to reducer to add the step to navigation state
        dispatch({
          type: 'ADD_DYNAMIC_STEP',
          config,
          canonicalOrder,
        });
      },

      removeDynamicStep(stepId: string) {
        // Remove from lookup map
        configMapRef.current.delete(stepId);
        // Dispatch to reducer to remove the step from navigation state
        dispatch({
          type: 'REMOVE_DYNAMIC_STEP',
          stepId,
        });
      },

      requestUpdate() {
        forceRender();
      },

      nextStep() {
        dispatch({ type: 'NEXT_STEP' });
      },

      get state() {
        return shellState;
      },
    }),
    [shellState, forceRender]
  );

  // ── Reset on open (false -> true) ──────────────────────────────────────
  const prevOpenRef = React.useRef(open);
  React.useEffect(() => {
    const wasOpen = prevOpenRef.current;
    prevOpenRef.current = open;

    if (open && !wasOpen) {
      // Reset to the first step; a fresh session has nothing skipped yet.
      dispatch({ type: 'GO_TO_STEP', stepIndex: 0, clearSkipped: true });

      // Clear finishing state
      setSuccessConfig(null);
      setFinishError(null);
      setIsFinishing(false);

      // Rebuild config map from base step configs
      const newMap = new Map<string, IWizardStepConfig>();
      stepConfigs.forEach(config => {
        newMap.set(config.id, config);
      });
      configMapRef.current = newMap;
    }
  }, [open, stepConfigs]);

  // ── Sync configMapRef when base step configs change ────────────────────
  // Sync during render (not in an effect) so canAdvance() reads latest
  // configs immediately. This fixes the stale-closure bug where AI pre-fill
  // updates form validity but the Next button stays disabled until user
  // interaction triggers a re-render.
  stepConfigs.forEach(config => {
    configMapRef.current.set(config.id, config);
  });

  // ── Derived values ────────────────────────────────────────────────────
  const currentStepDef = shellState.steps[shellState.currentStepIndex];
  const currentConfig = currentStepDef ? configMapRef.current.get(currentStepDef.id) : undefined;

  const isFirstStep = shellState.currentStepIndex === 0;
  const isLastStep = shellState.currentStepIndex === shellState.steps.length - 1;

  // Early finish: the current step config says we can finish early
  const isEarlyFinish = currentConfig?.isEarlyFinish?.() ?? false;
  const showFinish = isLastStep || isEarlyFinish;

  const canAdvance = currentConfig ? currentConfig.canAdvance() : true;
  const isSkippable = currentConfig?.isSkippable ?? false;

  // ── Primary button label ──────────────────────────────────────────────
  const primaryButtonLabel: string = (() => {
    if (isFinishing) return finishingLabel;
    if (showFinish) return finishLabel;
    return 'Next';
  })();

  // ── Handle finish ─────────────────────────────────────────────────────
  const handleFinish = React.useCallback(async () => {
    setIsFinishing(true);
    setFinishError(null);

    try {
      const result = await onFinish();
      if (result) {
        setSuccessConfig(result);
      } else if (!stayOpenOnFinish) {
        onClose();
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'An unknown error occurred.';
      setFinishError(message);
    } finally {
      setIsFinishing(false);
    }
  }, [onFinish, onClose, stayOpenOnFinish]);

  // ── Primary button click ──────────────────────────────────────────────
  const handlePrimaryButtonClick = React.useCallback(() => {
    if (showFinish) {
      void handleFinish();
    } else {
      dispatch({ type: 'NEXT_STEP' });
    }
  }, [showFinish, handleFinish]);

  // ── Back button click ─────────────────────────────────────────────────
  const handleBack = React.useCallback(() => {
    dispatch({ type: 'PREV_STEP' });
  }, []);

  // ── Skip button click (advances without canAdvance check) ──
  // Default (master behaviour): the skipped step is ticked like any completed step. With
  // `showSkippedSteps` (opt-in, D-69) it is marked 'skipped' and shows the dashed ring instead.
  const handleSkip = React.useCallback(() => {
    dispatch({ type: showSkippedSteps ? 'SKIP_STEP' : 'NEXT_STEP' });
  }, [showSkippedSteps]);

  // ── Build the imperative handle for renderContent ─────────────────────
  // We need a stable-ish reference to pass into renderContent. Since
  // renderContent is called during render, we build the handle inline.
  const handle: IWizardShellHandle = React.useMemo(
    () => ({
      addDynamicStep(config: IWizardStepConfig, canonicalOrder?: string[]) {
        configMapRef.current.set(config.id, config);
        dispatch({
          type: 'ADD_DYNAMIC_STEP',
          config,
          canonicalOrder,
        });
      },
      removeDynamicStep(stepId: string) {
        configMapRef.current.delete(stepId);
        dispatch({
          type: 'REMOVE_DYNAMIC_STEP',
          stepId,
        });
      },
      requestUpdate() {
        forceRender();
      },
      nextStep() {
        dispatch({ type: 'NEXT_STEP' });
      },
      get state() {
        return shellState;
      },
    }),
    [shellState, forceRender]
  );

  // ── Body: sidebar + content area (shared by both modes) ───────────────
  const renderMainArea = (className: string) => (
    <div className={className}>
      <WizardStepper steps={shellState.steps} />

      <div className={styles.contentArea}>
        {/* Status bar slot (task 056) — above everything else in the content area */}
        {statusBar}

        {/* Finish error bar */}
        {finishError && (
          <MessageBar intent="error" role="alert">
            <MessageBarBody>{finishError}</MessageBarBody>
          </MessageBar>
        )}

        {/* Success screen replaces step content */}
        {successConfig ? <WizardSuccessScreen config={successConfig} /> : currentConfig?.renderContent(handle)}
      </div>
    </div>
  );

  // ── Footer slot content (shared by both modes) ────────────────────────
  // Standard footer: Cancel (+ footerLeftExtra) LEFT; [spinner] [step actions] Skip · Back · Next RIGHT.
  const standardFooterStart = (
    <>
      <Button appearance="secondary" onClick={onClose} disabled={isFinishing}>
        Cancel
      </Button>
      {footerLeftExtra}
    </>
  );

  const standardFooterEnd = (
    <>
      {/* In-progress spinner */}
      {isFinishing && (
        <div className={styles.progressRow}>
          <Spinner size="tiny" />
          <Text size={200}>{finishingLabel}</Text>
        </div>
      )}

      {/* Per-step custom footer actions */}
      {currentConfig?.footerActions}

      {/* Skip button — shown on skippable steps (optional follow-on
          steps). Ordered BEFORE Back (Skip · Back · Next), `transparent`. */}
      {isSkippable && !isLastStep && !isFinishing && (
        <Button appearance="transparent" onClick={handleSkip}>
          Skip
        </Button>
      )}

      {/* Back button — hidden on step 0, disabled when finishing */}
      {!isFirstStep && (
        <Button appearance="secondary" onClick={handleBack} disabled={isFinishing}>
          Back
        </Button>
      )}

      {/* Next / Finish */}
      <Button appearance="primary" onClick={handlePrimaryButtonClick} disabled={!canAdvance || isFinishing}>
        {primaryButtonLabel}
      </Button>
    </>
  );

  // Footer override (task 056): a supplied slot replaces that side; an omitted slot keeps the standard.
  // Not applied on the success screen, whose footer is the success config's own actions.
  let footerStart: React.ReactNode = standardFooterStart;
  let footerEnd: React.ReactNode = standardFooterEnd;
  if (footerOverride && !successConfig) {
    const context: IWizardFooterContext = {
      currentStepId: currentStepDef?.id,
      isFirstStep,
      isLastStep,
      isFinishing,
      canAdvance,
      goBack: () => dispatch({ type: 'PREV_STEP' }),
      goNext: () => dispatch({ type: 'NEXT_STEP' }),
      close: onClose,
    };
    const slots = footerOverride(context);
    if (slots) {
      if (slots.start !== undefined) footerStart = slots.start;
      if (slots.end !== undefined) footerEnd = slots.end;
    }
  }

  // ── Render ──────────────────────────────────────────────────────────────

  // Embedded mode: no envelope. Used for workspace tabs, full pages, and Code Pages hosted under
  // platform chrome (with hideTitle). Markup unchanged by the SprkModal re-base.
  if (embedded) {
    if (!open) return null;
    return (
      <div className={styles.embeddedRoot} aria-label={ariaLabel ?? title}>
        {/* Custom title bar with close button (hidden when host provides its own chrome) */}
        {!hideTitle && (
          <div className={styles.titleBar}>
            <Text as="h1" title={title} className={styles.titleText}>
              {title}
            </Text>
            {/* Close only — an embedded mount has no independent viewport to maximize into. */}
            <ModalWindowControls isMaximized={false} onToggleMaximize={undefined} onClose={onClose} />
          </div>
        )}

        {renderMainArea(styles.mainArea)}

        {/* Footer — success screen shows its actions right-aligned; steps show the standard split. */}
        {successConfig ? (
          <div className={mergeClasses(styles.footer, styles.footerEnd)}>
            <div className={styles.footerSlot}>{successConfig.actions}</div>
          </div>
        ) : (
          <div className={mergeClasses(styles.footer, styles.footerBetween)}>
            <div className={styles.footerSlot}>{footerStart}</div>
            <div className={styles.footerSlot}>{footerEnd}</div>
          </div>
        )}
      </div>
    );
  }

  // Modal mode: the wizard preset of SprkModal (ADR-050 as amended 2026-10-07). SprkModal owns the
  // envelope, the header (nav · title · maximize · ×), dismiss, size + uiScale, and the footer layout.
  const legacySize = maxWidth || height ? { width: maxWidth, height } : undefined;
  return (
    <SprkModal
      open={open}
      onClose={onClose}
      title={title}
      size={size}
      dismiss={dismiss}
      uiScale={uiScale}
      legacySize={legacySize}
      nav={nav}
      padded={false}
      footerStart={successConfig ? undefined : footerStart}
      footer={successConfig ? successConfig.actions : footerEnd}
    >
      {renderMainArea(mergeClasses(styles.mainArea, styles.mainAreaFill))}
    </SprkModal>
  );
});

WizardShell.displayName = 'WizardShell';
