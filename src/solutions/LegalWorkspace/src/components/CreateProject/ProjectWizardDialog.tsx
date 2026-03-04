/**
 * ProjectWizardDialog.tsx
 * Single-step wizard dialog for "Create New Project".
 *
 * Much simpler than the Create Matter wizard — just ONE step (Create Record)
 * plus a success screen. No file upload step, no follow-on steps, no dynamic
 * steps. Delegates all shell concerns to WizardShell.
 *
 * Default export enables React.lazy() dynamic import for bundle-size
 * optimization (same pattern as WizardDialog.tsx).
 */
import * as React from 'react';
import { Text, Button, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';
import { WizardShell } from '../Wizard';
import type { IWizardStepConfig, IWizardSuccessConfig } from '../Wizard';
import { CreateProjectStep } from './CreateProjectStep';
import { ProjectService } from './projectService';
import { EMPTY_PROJECT_FORM } from './projectFormTypes';
import type { ICreateProjectFormState } from './projectFormTypes';
import { navigateToEntity } from '../../utils/navigation';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface IProjectWizardDialogProps {
  open: boolean;
  onClose: () => void;
  webApi: IWebApi;
}

// ---------------------------------------------------------------------------
// ProjectWizardDialog
// ---------------------------------------------------------------------------

const ProjectWizardDialog: React.FC<IProjectWizardDialogProps> = ({ open, onClose, webApi }) => {
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateProjectFormState>(EMPTY_PROJECT_FORM);
  const serviceRef = React.useRef(new ProjectService(webApi));

  // Reset on open
  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(EMPTY_PROJECT_FORM);
      serviceRef.current = new ProjectService(webApi);
    }
  }, [open, webApi]);

  const stepConfigs: IWizardStepConfig[] = React.useMemo(() => [
    {
      id: 'create-record',
      label: 'Create record',
      canAdvance: () => formValid,
      renderContent: () => (
        <CreateProjectStep
          webApi={webApi}
          onValidChange={setFormValid}
          onFormValues={setFormValues}
        />
      ),
    },
  ], [webApi, formValid]);  // formValid in deps so canAdvance closure updates

  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    const result = await serviceRef.current.createProject(formValues);
    if (!result.success) {
      throw new Error(result.errorMessage ?? 'Failed to create project');
    }
    return {
      icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
      title: 'Project created!',
      body: (
        <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
          <span style={{ color: tokens.colorBrandForeground1, fontWeight: tokens.fontWeightSemibold }}>
            &ldquo;{result.projectName}&rdquo;
          </span>{' '}
          has been created and is ready to use.
        </Text>
      ),
      actions: (
        <>
          <Button
            appearance="primary"
            onClick={() => {
              navigateToEntity({ action: 'openRecord', entityName: 'sprk_project', entityId: result.projectId });
              onClose();
            }}
          >
            View Project
          </Button>
          <Button appearance="secondary" onClick={onClose}>Close</Button>
        </>
      ),
    };
  }, [formValues, onClose]);

  return (
    <WizardShell
      open={open}
      title="Create New Project"
      ariaLabel="Create New Project"
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel="Creating project…"
      finishLabel="Create"
    />
  );
};

export default ProjectWizardDialog;
