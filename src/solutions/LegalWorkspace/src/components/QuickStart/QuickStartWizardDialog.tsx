/**
 * QuickStartWizardDialog.tsx
 *
 * Config-driven wizard dialog for all Quick Start action cards.
 * Uses WizardShell + shared Playbook components.
 *
 * Portable — NO imports from GetStarted/, Shell/, or workspace-specific modules.
 */
import * as React from "react";
import { Text, tokens } from "@fluentui/react-components";
import { CheckmarkCircleFilled } from "@fluentui/react-icons";
import { WizardShell } from "../Wizard/WizardShell";
import type { IWizardStepConfig, IWizardSuccessConfig } from "../Wizard/wizardShellTypes";
import { DocumentUploadStep } from "../Playbook/DocumentUploadStep";
import { FollowUpActionsStep } from "../Playbook/FollowUpActionsStep";
import { QUICKSTART_CONFIGS } from "./quickStartConfig";
import type { IQuickStartConfig } from "./quickStartConfig";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IQuickStartWizardDialogProps {
  open: boolean;
  onClose: () => void;
  intent: string;
  /** Optional callback when user clicks "Open Analysis Workspace" in follow-up step. */
  onOpenWorkspace?: (analysisId: string) => void;
}

// ---------------------------------------------------------------------------
// QuickStartWizardDialog
// ---------------------------------------------------------------------------

const QuickStartWizardDialog: React.FC<IQuickStartWizardDialogProps> = ({
  open,
  onClose,
  intent,
  onOpenWorkspace,
}) => {
  const config: IQuickStartConfig | undefined = QUICKSTART_CONFIGS[intent];

  // --- Wizard state ---
  const [uploadedFiles, setUploadedFiles] = React.useState<File[]>([]);
  const [analysisId, setAnalysisId] = React.useState<string>("");

  // Reset state when dialog opens
  React.useEffect(() => {
    if (open) {
      setUploadedFiles([]);
      setAnalysisId("");
    }
  }, [open]);

  // Fallback if intent not found
  if (!config) {
    return null;
  }

  // --- Step configurations ---
  const stepConfigs: IWizardStepConfig[] = [
    {
      id: "upload",
      label: config.uploadLabel,
      canAdvance: () => uploadedFiles.length > 0,
      renderContent: () => (
        <DocumentUploadStep
          accept={config.acceptedFileTypes}
          multiple={config.allowMultiple}
          onFilesReady={setUploadedFiles}
        />
      ),
    },
    {
      id: "analyze",
      label: config.analyzeLabel,
      canAdvance: () => !!analysisId,
      renderContent: () => (
        <div style={{ display: "flex", flexDirection: "column", gap: "16px", padding: "16px 0" }}>
          <Text size={500} weight="semibold">
            {config.title}
          </Text>
          <Text size={300}>
            Playbook execution will create an analysis record.
            This step will be connected to the AI pipeline in a future task.
          </Text>
          <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
            Files selected: {uploadedFiles.length}
          </Text>
        </div>
      ),
    },
    {
      id: "actions",
      label: "Next Steps",
      canAdvance: () => true,
      isEarlyFinish: () => true,
      renderContent: () => (
        <FollowUpActionsStep
          analysisId={analysisId}
          availableActions={config.followUpActions}
          onOpenWorkspace={onOpenWorkspace}
        />
      ),
    },
  ];

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig | void> => {
    // For now, just close the dialog. Full execution logic will be wired in a later task.
    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: "Analysis Complete",
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          Your analysis has been created successfully.
        </Text>
      ),
      actions: null,
    };
  }, []);

  return (
    <WizardShell
      open={open}
      title={config.title}
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishLabel="Done"
      finishingLabel="Processing..."
    />
  );
};

export default QuickStartWizardDialog;
