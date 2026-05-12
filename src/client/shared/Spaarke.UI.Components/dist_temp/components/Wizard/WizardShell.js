/**
 * WizardShell.tsx
 *
 * Generic, domain-free wizard dialog shell.
 *
 * Layout:
 *   +------------------------------------------------------+
 *   | Title bar                              [X Close]      |
 *   +------------------------------------------------------+
 *   | Sidebar ~200px  |  Content area (flex: 1)             |
 *   | WizardStepper   |    - Error bar (if finishError)     |
 *   |                 |    - Success screen (if finished)    |
 *   |                 |    - Step content (renderContent)    |
 *   +------------------------------------------------------+
 *   | [Cancel]        [spinner]  [custom] [Back]  [Next]    |
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
 */
import * as React from 'react';
import { Dialog, DialogSurface, DialogBody, Button, MessageBar, MessageBarBody, Text, Spinner, makeStyles, tokens, } from '@fluentui/react-components';
import { Dismiss24Regular } from '@fluentui/react-icons';
import { WizardStepper } from './WizardStepper';
import { WizardSuccessScreen } from './WizardSuccessScreen';
import { wizardShellReducer, buildInitialShellState } from './wizardShellReducer';
// ---------------------------------------------------------------------------
// Styles (generic shell layout only)
// ---------------------------------------------------------------------------
const useStyles = makeStyles({
    // Override DialogSurface — landscape orientation, resizable
    surface: {
        width: '1100px',
        maxWidth: '95vw',
        maxHeight: '80vh',
        padding: '0px',
        resize: 'both',
        overflow: 'auto',
        border: `1px solid ${tokens.colorNeutralStroke1}`,
    },
    // DialogBody: remove default padding so we control layout entirely
    body: {
        padding: '0px',
        display: 'flex',
        flexDirection: 'column',
        height: '70vh',
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
    // Custom title bar (replaces DialogTitle default rendering)
    titleBar: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        paddingTop: tokens.spacingVerticalL,
        paddingBottom: tokens.spacingVerticalL,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalL,
        borderBottomWidth: '1px',
        borderBottomStyle: 'solid',
        borderBottomColor: tokens.colorNeutralStroke2,
        flexShrink: 0,
    },
    titleText: {
        color: tokens.colorNeutralForeground1,
    },
    closeButton: {
        color: tokens.colorNeutralForeground3,
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
    // Footer / dialog actions
    footer: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        paddingTop: tokens.spacingVerticalM,
        paddingBottom: tokens.spacingVerticalM,
        paddingLeft: tokens.spacingHorizontalXL,
        paddingRight: tokens.spacingHorizontalL,
        borderTopWidth: '1px',
        borderTopStyle: 'solid',
        borderTopColor: tokens.colorNeutralStroke2,
        backgroundColor: tokens.colorNeutralBackground1,
        flexShrink: 0,
    },
    footerLeft: {
        display: 'flex',
        gap: tokens.spacingHorizontalS,
    },
    footerRight: {
        display: 'flex',
        gap: tokens.spacingHorizontalS,
        alignItems: 'center',
    },
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
export const WizardShell = React.forwardRef((props, ref) => {
    const { open, embedded = false, title, hideTitle = false, ariaLabel, steps: stepConfigs, onClose, onFinish, finishingLabel = 'Processing\u2026', finishLabel = 'Finish', footerLeftExtra, } = props;
    const styles = useStyles();
    // ── Navigation state via reducer ───────────────────────────────────────
    const [shellState, dispatch] = React.useReducer(wizardShellReducer, stepConfigs, buildInitialShellState);
    // ── Step config lookup map ─────────────────────────────────────────────
    const configMapRef = React.useRef(new Map());
    // ── Force-update counter (for requestUpdate) ─────────────────────────
    const [, forceRender] = React.useReducer((x) => x + 1, 0);
    // ── Finishing flow state ───────────────────────────────────────────────
    const [isFinishing, setIsFinishing] = React.useState(false);
    const [finishError, setFinishError] = React.useState(null);
    const [successConfig, setSuccessConfig] = React.useState(null);
    // ── Imperative handle ──────────────────────────────────────────────────
    React.useImperativeHandle(ref, () => ({
        addDynamicStep(config, canonicalOrder) {
            // Add config to the lookup map
            configMapRef.current.set(config.id, config);
            // Dispatch to reducer to add the step to navigation state
            dispatch({
                type: 'ADD_DYNAMIC_STEP',
                config,
                canonicalOrder,
            });
        },
        removeDynamicStep(stepId) {
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
    }), [shellState, forceRender]);
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
            const newMap = new Map();
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
    const primaryButtonLabel = (() => {
        if (isFinishing)
            return finishingLabel;
        if (showFinish)
            return finishLabel;
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
            }
            else {
                onClose();
            }
        }
        catch (err) {
            const message = err instanceof Error ? err.message : 'An unknown error occurred.';
            setFinishError(message);
        }
        finally {
            setIsFinishing(false);
        }
    }, [onFinish, onClose]);
    // ── Primary button click ──────────────────────────────────────────────
    const handlePrimaryButtonClick = React.useCallback(() => {
        if (showFinish) {
            void handleFinish();
        }
        else {
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
    const handle = React.useMemo(() => ({
        addDynamicStep(config, canonicalOrder) {
            configMapRef.current.set(config.id, config);
            dispatch({
                type: 'ADD_DYNAMIC_STEP',
                config,
                canonicalOrder,
            });
        },
        removeDynamicStep(stepId) {
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
    }), [shellState, forceRender]);
    // ── Shared inner content (used by both dialog and embedded modes) ─────
    const innerContent = (React.createElement(React.Fragment, null,
        !hideTitle && (React.createElement("div", { className: styles.titleBar },
            React.createElement(Text, { as: "h1", size: 500, weight: "semibold", className: styles.titleText }, title),
            React.createElement(Button, { appearance: "subtle", size: "small", icon: React.createElement(Dismiss24Regular, null), className: styles.closeButton, onClick: onClose, "aria-label": "Close dialog" }))),
        React.createElement("div", { className: styles.mainArea },
            React.createElement(WizardStepper, { steps: shellState.steps }),
            React.createElement("div", { className: styles.contentArea },
                finishError && (React.createElement(MessageBar, { intent: "error", role: "alert" },
                    React.createElement(MessageBarBody, null, finishError))),
                successConfig ? React.createElement(WizardSuccessScreen, { config: successConfig }) : currentConfig?.renderContent(handle))),
        successConfig ? (React.createElement("div", { className: styles.footer },
            React.createElement("div", { className: styles.footerLeft }),
            React.createElement("div", { className: styles.footerRight }, successConfig.actions))) : (React.createElement("div", { className: styles.footer },
            React.createElement("div", { className: styles.footerLeft },
                React.createElement(Button, { appearance: "secondary", onClick: onClose, disabled: isFinishing }, "Cancel"),
                footerLeftExtra),
            React.createElement("div", { className: styles.footerRight },
                isFinishing && (React.createElement("div", { className: styles.progressRow },
                    React.createElement(Spinner, { size: "tiny" }),
                    React.createElement(Text, { size: 200 }, finishingLabel))),
                currentConfig?.footerActions,
                !isFirstStep && (React.createElement(Button, { appearance: "secondary", onClick: handleBack, disabled: isFinishing }, "Back")),
                isSkippable && !isLastStep && !isFinishing && (React.createElement(Button, { appearance: "secondary", onClick: handleSkip }, "Skip")),
                React.createElement(Button, { appearance: "primary", onClick: handlePrimaryButtonClick, disabled: !canAdvance || isFinishing }, primaryButtonLabel))))));
    // ── Render ──────────────────────────────────────────────────────────────
    // Embedded mode: render directly without Dialog/DialogSurface wrapper.
    // Used when the wizard is already inside a Dataverse dialog iframe.
    if (embedded) {
        if (!open)
            return null;
        return (React.createElement("div", { className: styles.embeddedRoot, "aria-label": ariaLabel ?? title }, innerContent));
    }
    // Standard mode: render inside Fluent Dialog overlay.
    return (React.createElement(Dialog, { open: open, onOpenChange: (_e, data) => {
            if (!data.open)
                onClose();
        } },
        React.createElement(DialogSurface, { className: styles.surface, "aria-label": ariaLabel ?? title },
            React.createElement(DialogBody, { className: styles.body }, innerContent))));
});
WizardShell.displayName = 'WizardShell';
//# sourceMappingURL=WizardShell.js.map