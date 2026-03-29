/**
 * Workspace Layout Wizard - Root Application Component
 *
 * Standalone wizard dialog for creating, editing, or cloning workspace section layouts.
 * Opened via Xrm.Navigation.navigateTo as a webresource dialog.
 *
 * Wizard Steps:
 *   Step 1: Choose Layout - Select template or start blank
 *   Step 2: Configure Sections - Add/remove/reorder sections
 *   Step 3: Review & Save - Preview layout and confirm
 *
 * @see ADR-026 - Full-Page Custom Page Standard
 * @see ADR-021 - Fluent UI v9 Design System
 */

import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  Text,
  Button,
  tokens,
} from "@fluentui/react-components";
import {
  ArrowLeft24Regular,
  ArrowRight24Regular,
  Checkmark24Regular,
} from "@fluentui/react-icons";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { TemplateStep } from "./steps";
import type { LayoutTemplateId } from "@spaarke/ui-components";
import type { WizardMode } from "./main";

/** Props passed from main.tsx based on URL data parameters */
interface AppProps {
  mode: WizardMode;
  layoutId: string | null;
}

/** Wizard step definition */
interface WizardStep {
  key: string;
  title: string;
  description: string;
}

const WIZARD_STEPS: WizardStep[] = [
  {
    key: "choose-layout",
    title: "Choose Layout",
    description: "Select a layout template or start from a blank canvas.",
  },
  {
    key: "configure-sections",
    title: "Configure Sections",
    description: "Add, remove, and reorder sections in your workspace layout.",
  },
  {
    key: "review-save",
    title: "Review & Save",
    description: "Preview the final layout and save your configuration.",
  },
];

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    alignItems: "center",
    padding: "16px 24px",
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    gap: "16px",
  },
  stepIndicator: {
    display: "flex",
    alignItems: "center",
    gap: "8px",
  },
  stepDot: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorNeutralStroke1,
  },
  stepDotActive: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
  },
  stepDotCompleted: {
    width: "8px",
    height: "8px",
    borderRadius: "50%",
    backgroundColor: tokens.colorBrandBackground,
  },
  content: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    padding: "24px",
    gap: "16px",
    overflow: "auto",
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: "16px 24px",
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
  },
});

export const App: React.FC<AppProps> = ({ mode, layoutId }) => {
  const [theme, setTheme] = React.useState(resolveTheme);
  const [currentStep, setCurrentStep] = React.useState(0);
  const [selectedTemplateId, setSelectedTemplateId] =
    React.useState<LayoutTemplateId | null>(null);

  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  const step = WIZARD_STEPS[currentStep];
  const isFirstStep = currentStep === 0;
  const isLastStep = currentStep === WIZARD_STEPS.length - 1;

  /** Next button is disabled on step 1 until a template is chosen. */
  const isNextDisabled = currentStep === 0 && selectedTemplateId === null;

  const headerTitle =
    mode === "edit"
      ? "Edit Workspace Layout"
      : mode === "saveAs"
        ? "Save Layout As..."
        : "Create Workspace Layout";

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <div className={useStyles().root}>
        {/* Header with step indicator */}
        <div className={useStyles().header}>
          <Text weight="semibold" size={500}>
            {headerTitle}
          </Text>
          <div className={useStyles().stepIndicator}>
            {WIZARD_STEPS.map((s, i) => (
              <div
                key={s.key}
                className={
                  i === currentStep
                    ? useStyles().stepDotActive
                    : i < currentStep
                      ? useStyles().stepDotCompleted
                      : useStyles().stepDot
                }
              />
            ))}
          </div>
        </div>

        {/* Step content */}
        <div className={useStyles().content}>
          {currentStep === 0 ? (
            <TemplateStep
              selectedTemplateId={selectedTemplateId}
              onSelect={setSelectedTemplateId}
            />
          ) : (
            <>
              <Text size={600} weight="semibold">
                Step {currentStep + 1}: {step.title}
              </Text>
              <Text size={400} align="center">
                {step.description}
              </Text>
              {mode === "edit" && layoutId && (
                <Text
                  size={200}
                  style={{ color: tokens.colorNeutralForeground3 }}
                >
                  Editing layout: {layoutId}
                </Text>
              )}
            </>
          )}
        </div>

        {/* Footer with navigation buttons */}
        <div className={useStyles().footer}>
          <Button
            appearance="subtle"
            icon={<ArrowLeft24Regular />}
            disabled={isFirstStep}
            onClick={() => setCurrentStep((s) => s - 1)}
          >
            Back
          </Button>
          {isLastStep ? (
            <Button
              appearance="primary"
              icon={<Checkmark24Regular />}
              iconPosition="after"
            >
              Save Layout
            </Button>
          ) : (
            <Button
              appearance="primary"
              icon={<ArrowRight24Regular />}
              iconPosition="after"
              disabled={isNextDisabled}
              onClick={() => setCurrentStep((s) => s + 1)}
            >
              Next
            </Button>
          )}
        </div>
      </div>
    </FluentProvider>
  );
};
