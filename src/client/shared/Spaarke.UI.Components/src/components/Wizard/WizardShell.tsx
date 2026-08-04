/**
 * WizardShell.tsx
 *
 * Generic, domain-free wizard dialog shell.
 *
 * Layout:
 *   +------------------------------------------------------+
 *   | Title bar (ellipsized)        [Maximize/Restore] [X]  |
 *   +------------------------------------------------------+
 *   | Sidebar ~200px  |  Content area (flex: 1)             |
 *   | WizardStepper   |    - Error bar (if finishError)     |
 *   |                 |    - Success screen (if finished)    |
 *   |                 |    - Step content (renderContent)    |
 *   +------------------------------------------------------+
 *   | [Cancel]   [spinner] [custom] [Skip] [Back]  [Next]   |
 *   +------------------------------------------------------+
 *
 * The shell handles:
 *   - Navigation state via useReducer (wizardShellReducer)
 *   - Dynamic step insertion/removal via imperative handle
 *   - Finish flow with async onFinish, error display, and success screen
 *   - Layout, styles, and footer button logic
 *
 * Domain-specific content is injected via IWizardStepConfig.renderContent
 * callbacks. The shell has ZERO domain imports.
 *
 * Constraints:
 *   - Fluent v9 only: Dialog, Text, Button, Spinner, MessageBar — ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 *   - No domain-specific imports
 *
 * P6 light-first re-base (spaarke-modal-system, task 080, FR-17 — owner
 * decision §11-G): the header/footer/size TOKENS are aligned to the
 * canonical `SprkModal` standard (see `SprkModal/presets/WizardModal.tsx`,
 * the reference chrome) — ellipsized title + `ModalWindowControls` +
 * `strokeWidthThin`/`colorNeutralStroke2` borders; footer `Cancel` always
 * left, `Skip · Back · Next` right; DEFAULT size swapped from the ad-hoc
 * `95vw`/`70vh` literals to the named `wizard` size (`SIZE_SPEC.wizard` =
 * 62vw × min(74vh, 760px)). The Dialog envelope, `embedded` mode, the
 * 200px `WizardStepper` sidebar, and the `maxWidth`/`height` prop OVERRIDE
 * mechanism (v1.1.63) are UNCHANGED — this is a chrome/token alignment
 * pass only, not a full internal re-base onto `SprkModal` itself.
 */

import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  DialogContent as _DialogContent,
  DialogActions as _DialogActions,
  Button,
  MessageBar,
  MessageBarBody,
  Text,
  Spinner,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';

import { ModalWindowControls } from '../ModalWindowControls';
import { WizardStepper } from './WizardStepper';
import { WizardSuccessScreen } from './WizardSuccessScreen';
import { wizardShellReducer, buildInitialShellState } from './wizardShellReducer';
import type {
  IWizardShellProps,
  IWizardShellHandle,
  IWizardStepConfig,
  IWizardSuccessConfig,
} from './wizardShellTypes';
import { getSurfaceStyle } from '../SprkModal/sizes';

// ---------------------------------------------------------------------------
// Default size — the named `wizard` scale (spec FR-17 / design §6.4 / task 080)
// ---------------------------------------------------------------------------
// Replaces the pre-v1.1.64 ad-hoc `'95vw'`/`'70vh'` literal defaults. Sourced
// from `getSurfaceStyle('wizard')` so the numbers can never drift from the
// canonical size scale (`SIZE_SPEC.wizard` = 62vw × min(74vh, 760px)) — the
// same sourcing discipline task 061 used for `FindSimilarDialog`'s `xl`
// override. NOTE: `getSurfaceStyle`'s `.width` field is the value that plays
// WizardShell's `maxWidth` role here (the actual target size WizardShell
// clamps the DialogSurface to via its existing inline-style sizing-clamp
// bypass, v1.1.63) — NOT `.maxWidth` (SprkModal's own 96vw OUTER safety
// clamp around an explicit narrower `width`, which doesn't apply to
// WizardShell's single-value clamp architecture). WizardShell does not
// accept a `uiScale` prop (light-first scope); `uiScale` is fixed at the
// `getSurfaceStyle` default (1) here.
const WIZARD_DEFAULT_SIZE = getSurfaceStyle('wizard');
const WIZARD_DEFAULT_MAX_WIDTH = String(WIZARD_DEFAULT_SIZE.width);
const WIZARD_DEFAULT_HEIGHT = String(WIZARD_DEFAULT_SIZE.height);

// ---------------------------------------------------------------------------
// Styles (generic shell layout only)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  // Override DialogSurface — landscape orientation, resizable.
  // v1.1.63: `width` kept here as the default (1100px) for back-compat;
  // `maxWidth` and the body `height`/`min-height` are now driven by the
  // consumer-provided `maxWidth`/`height` props via inline style on
  // DialogSurface (same width-clamp-bypass pattern as SendEmailDialog
  // since v1.1.56) so the makeStyles cascade can't override them.
  surface: {
    width: '100%',
    // maxWidth + maxHeight intentionally OMITTED here — see the inline
    // `style={{ maxWidth, height, minHeight: height }}` on DialogSurface
    // below; that pattern is the only reliable way to bypass Fluent v9's
    // DialogSurface inner-content sizing under tall content.
    padding: '0px',
    resize: 'both',
    overflow: 'auto',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    // v1.1.63 — the surface must be a flex column so DialogBody can
    // flex-grow to fill the surface height. Fluent DialogSurface is
    // already display:flex by default; we just ensure direction.
    display: 'flex',
    flexDirection: 'column',
  },
  // DialogBody: remove default padding so we control layout entirely.
  // v1.1.63 — body fills the surface (flex:1) rather than hard-coding
  // a 70vh height; the consumer-controlled height on DialogSurface now
  // drives the overall vertical footprint.
  body: {
    padding: '0px',
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    overflow: 'hidden',
  },
  // Embedded mode: fills the host container (e.g., Dataverse dialog iframe)
  embeddedRoot: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    height: '100%',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  // Custom title bar (replaces DialogTitle default rendering) — re-based
  // (task 080) onto the `SprkModal` standard header tokens: uniform
  // `spacingHorizontalL` inline padding, `spacingVerticalS` block padding,
  // and the token border shorthand (was `borderBottomWidth: '1px'` —
  // ADR-021 violation removed).
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
  // Ellipsized title (task 080) — matches `SprkModal`'s title token set
  // exactly (`fontSizeBase400`/`lineHeightBase400`/`fontWeightSemibold`)
  // rather than the Fluent `Text` `size`/`weight` props, so the two shells
  // read as one system.
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
  // Footer / dialog actions — re-based (task 080) onto the `SprkModal`
  // standard footer tokens (was `borderTopWidth: '1px'` — ADR-021 violation
  // removed). `justify-content` now lives on the `footerBetween`/`footerEnd`
  // modifiers below (matches `SprkModal`'s own footer/footerBetween/
  // footerEnd/footerSlot split) so the same base class serves both the
  // normal (Cancel left / actions right) and success (actions right only)
  // footer layouts.
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
    finishingLabel = 'Processing\u2026',
    finishLabel = 'Finish',
    footerLeftExtra,
    // task 080 (spec FR-17) \u2014 sizing overrides; default to the named
    // `wizard` size (`SIZE_SPEC.wizard` = 62vw \u00d7 min(74vh, 760px)) instead
    // of the pre-v1.1.64 ad-hoc `95vw`/`70vh` literals (v1.1.63). Consumers
    // that pass an explicit `maxWidth`/`height` (e.g. FindSimilarDialog's
    // `xl` override, DocumentEmailWizard's forwarded prop) are UNCHANGED \u2014
    // only the fallback default moves.
    maxWidth = WIZARD_DEFAULT_MAX_WIDTH,
    height = WIZARD_DEFAULT_HEIGHT,
    initialStepId,
  } = props;

  const styles = useStyles();

  // ── Maximize/restore (task 030, FR-12) ─────────────────────────────────
  // Local to this shell; always starts restored on (re)open (matches the
  // established EmailComposer wrapper pattern). Reuses the EXISTING
  // consumer-driven sizing mechanism (inline style bypass of the makeStyles
  // cascade, v1.1.63) rather than introducing a parallel one: when maximized,
  // the effective maxWidth/height swap to a full-viewport target; restored,
  // they fall back to the consumer's `maxWidth`/`height` props unchanged.
  // Only offered in standard (non-embedded) mode — an `embedded` mount has no
  // independent viewport to expand into (it already fills its host
  // container), so the toggle is omitted there and only the close (×)
  // affordance renders, preserving the pre-existing embedded-mode behavior.
  const [isMaximized, setIsMaximized] = React.useState(false);
  React.useEffect(() => {
    if (!open) setIsMaximized(false);
  }, [open]);
  const effectiveMaxWidth = isMaximized ? '100vw' : maxWidth;
  const effectiveHeight = isMaximized ? '100vh' : height;

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
      // Reset reducer to initial state from current step configs
      dispatch({ type: 'GO_TO_STEP', stepIndex: 0 });

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
      } else {
        onClose();
      }
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'An unknown error occurred.';
      setFinishError(message);
    } finally {
      setIsFinishing(false);
    }
  }, [onFinish, onClose]);

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

  // ── Skip button click (advances without canAdvance check) ────────────
  const handleSkip = React.useCallback(() => {
    dispatch({ type: 'NEXT_STEP' });
  }, []);

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

  // ── Shared inner content (used by both dialog and embedded modes) ─────

  const innerContent = (
    <>
      {/* Custom title bar with close button (hidden when host provides its own chrome) */}
      {!hideTitle && (
        <div className={styles.titleBar}>
          {/* Ellipsized title (task 080) — `title` attr shows the full text
              on hover, matching `SprkModal`'s header exactly. */}
          <Text as="h1" title={title} className={styles.titleText}>
            {title}
          </Text>
          {/* Standardized window-controls cluster (task 030, FR-12; formalized
              onto the SprkModal standard header at task 080) — replaces the
              former ad hoc Close-only button. Maximize is omitted in embedded mode
              (see the state comment above); close is wired to the same unchanged
              onClose handler. */}
          <ModalWindowControls
            isMaximized={isMaximized}
            onToggleMaximize={embedded ? undefined : () => setIsMaximized(m => !m)}
            onClose={onClose}
          />
        </div>
      )}

      {/* Sidebar + content area */}
      <div className={styles.mainArea}>
        <WizardStepper steps={shellState.steps} />

        <div className={styles.contentArea}>
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

      {/* Footer — success screen shows actions in footer; normal steps show
          navigation. Re-based (task 080) onto the SprkModal standard footer
          tokens: `footerBetween` (Cancel left / actions right) for the normal
          case, `footerEnd` (actions right only) for the success screen — the
          same split SprkModal itself uses. */}
      {successConfig ? (
        <div className={mergeClasses(styles.footer, styles.footerEnd)}>
          <div className={styles.footerSlot}>{successConfig.actions}</div>
        </div>
      ) : (
        <div className={mergeClasses(styles.footer, styles.footerBetween)}>
          <div className={styles.footerSlot}>
            <Button appearance="secondary" onClick={onClose} disabled={isFinishing}>
              Cancel
            </Button>
            {footerLeftExtra}
          </div>

          <div className={styles.footerSlot}>
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
                steps). Ordered BEFORE Back (Skip · Back · Next) and styled
                `transparent`, matching the canonical `WizardModal` preset
                footer exactly (task 080 / design §6.4). */}
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
          </div>
        </div>
      )}
    </>
  );

  // ── Render ──────────────────────────────────────────────────────────────

  // Embedded mode: render directly without Dialog/DialogSurface wrapper.
  // Used when the wizard is already inside a Dataverse dialog iframe.
  if (embedded) {
    if (!open) return null;
    return (
      <div className={styles.embeddedRoot} aria-label={ariaLabel ?? title}>
        {innerContent}
      </div>
    );
  }

  // Standard mode: render inside Fluent Dialog overlay.
  //
  // v1.1.63 — DialogSurface receives an inline `style={{ maxWidth, height,
  // minHeight: height }}` so the surface honors the consumer's sizing
  // overrides. Inline style is the only reliable bypass for Fluent v9's
  // makeStyles + DialogSurface inner-content sizing (verified across
  // SendEmailDialog v1.1.56+ where the same pattern fixed the same
  // width-clamp issue). When the consumer omits the props, the defaults
  // are now the named `wizard` size (62vw × min(74vh, 760px), task 080 /
  // spec FR-17) — previously the ad-hoc 95vw/70vh literals.
  return (
    <Dialog
      open={open}
      onOpenChange={(_e, data) => {
        if (!data.open) onClose();
      }}
    >
      <DialogSurface
        className={styles.surface}
        style={{ maxWidth: effectiveMaxWidth, height: effectiveHeight, minHeight: effectiveHeight }}
        aria-label={ariaLabel ?? title}
      >
        <DialogBody className={styles.body}>{innerContent}</DialogBody>
      </DialogSurface>
    </Dialog>
  );
});

WizardShell.displayName = 'WizardShell';
