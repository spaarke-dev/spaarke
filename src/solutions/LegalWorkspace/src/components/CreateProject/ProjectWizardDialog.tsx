/**
 * ProjectWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New Project".
 *
 * Provides only:
 *   - Entity-specific form step (CreateProjectStep)
 *   - Finish handler (ProjectService.createProject + EntityCreationService for files)
 *   - Search callbacks (contacts, organizations, users)
 *   - Email template builders
 *
 * All generic wizard mechanics (file upload, follow-on steps, state
 * management) are handled by the shared CreateRecordWizard component.
 *
 * Default export enables React.lazy() dynamic import for bundle-size
 * optimization (same pattern as WizardDialog.tsx).
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import {
  CreateRecordWizard,
  type ICreateRecordWizardConfig,
  type IFinishContext,
} from '../../../../../client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard';

import type { IWizardSuccessConfig } from '../../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes';

import { CreateProjectStep } from './CreateProjectStep';
import { ProjectService } from './projectService';
import { EMPTY_PROJECT_FORM } from './projectFormTypes';
import type { ICreateProjectFormState } from './projectFormTypes';

import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatter/matterService';

import { EntityCreationService } from '../../services/EntityCreationService';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/authInit';
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
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
  // ── Entity-specific form state ──────────────────────────────────────────
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateProjectFormState>(EMPTY_PROJECT_FORM);
  const formValuesRef = React.useRef(formValues);
  formValuesRef.current = formValues;

  // Reset form state on open
  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(EMPTY_PROJECT_FORM);
    }
  }, [open]);

  // ── Search callbacks ────────────────────────────────────────────────────
  const handleSearchContacts = React.useCallback(
    (query: string) => searchContactsAsLookup(webApi, query),
    [webApi]
  );
  const handleSearchOrganizations = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(webApi, query),
    [webApi]
  );
  const handleSearchUsers = React.useCallback(
    (query: string) => searchUsersAsLookup(webApi, query),
    [webApi]
  );

  // ── Wizard config ───────────────────────────────────────────────────────
  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Project',
      entityLabel: 'project',
      filesStepSubtitle:
        'Upload documents to associate with this project. The AI will extract key information to assist with project setup.',
      finishingLabel: 'Creating project\u2026',

      infoStep: {
        id: 'create-record',
        label: 'Enter Info',
        canAdvance: () => formValid,
        renderContent: (wizardFiles) => (
          <CreateProjectStep
            webApi={webApi}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            uploadedFiles={wizardFiles}
            initialFormValues={formValues}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId: () => getSpeContainerIdFromBusinessUnit(webApi),

      buildEmailSubject: (entityName: string) => `New Project: ${entityName}`,
      buildEmailBody: (fields: Record<string, string>) => {
        const typeStr = fields.projectTypeName ? ` ${fields.projectTypeName.toLowerCase()}` : '';
        const areaStr = fields.practiceAreaName ? ` (${fields.practiceAreaName})` : '';
        return (
          `Dear Client,\n\n` +
          `We are pleased to confirm that your${typeStr} project, "${fields.projectName || ''}"${areaStr}, ` +
          `has been created in our legal management system.\n\n` +
          `Our team will be in touch shortly to discuss next steps and any actions required from you.\n\n` +
          `Please do not hesitate to reach out if you have any questions.\n\n` +
          `Kind regards,\n[Your Name]\n[Firm Name]`
        );
      },
      getEntityName: () => formValuesRef.current.projectName,
      getFormFields: () => ({
        projectName: formValuesRef.current.projectName,
        projectTypeName: formValuesRef.current.projectTypeName,
        practiceAreaName: formValuesRef.current.practiceAreaName,
      }),

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const warnings: string[] = [];
        const currentFormValues = formValuesRef.current;

        // Merge follow-on resource assignments back into form values
        const mergedFormValues: ICreateProjectFormState = {
          ...currentFormValues,
          assignedAttorneyId: context.followOn.assignedAttorneyId || currentFormValues.assignedAttorneyId,
          assignedAttorneyName: context.followOn.assignedAttorneyName || currentFormValues.assignedAttorneyName,
          assignedParalegalId: context.followOn.assignedParalegalId || currentFormValues.assignedParalegalId,
          assignedParalegalName: context.followOn.assignedParalegalName || currentFormValues.assignedParalegalName,
          assignedOutsideCounselId: context.followOn.assignedOutsideCounselId || currentFormValues.assignedOutsideCounselId,
          assignedOutsideCounselName: context.followOn.assignedOutsideCounselName || currentFormValues.assignedOutsideCounselName,
        };

        // 1. Create sprk_project record
        const projectService = new ProjectService(webApi);
        const result = await projectService.createProject(mergedFormValues);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create project');
        }

        const projectId = result.projectId!;
        const projectName = result.projectName!;

        // 2. Upload files to SPE + create document records
        if (context.uploadedFiles.length > 0 && context.speContainerId) {
          try {
            const entityService = new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl());

            const uploadResult = await entityService.uploadFilesToSpe(
              context.speContainerId,
              context.uploadedFiles
            );

            if (uploadResult.errors.length > 0) {
              for (const err of uploadResult.errors) {
                warnings.push(`File upload failed for "${err.fileName}": ${err.error}`);
              }
            }

            if (uploadResult.uploadedFiles.length > 0) {
              const docResult = await entityService.createDocumentRecords(
                'sprk_projects',
                projectId,
                'sprk_Project',
                uploadResult.uploadedFiles,
                {
                  containerId: context.speContainerId,
                  parentRecordName: projectName,
                }
              );

              if (docResult.warnings.length > 0) {
                warnings.push(...docResult.warnings);
              }
            }
          } catch (err) {
            const message = err instanceof Error ? err.message : 'File processing failed';
            warnings.push(`File pipeline error: ${message}`);
          }
        } else if (context.uploadedFiles.length > 0 && !context.speContainerId) {
          warnings.push('SPE container not configured — files were not uploaded to SharePoint Embedded.');
        }

        // 3. Send email (if selected)
        if (context.selectedActions.includes('send-email') && context.followOn.emailTo.trim()) {
          const emailService = new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl());
          const emailResult = await emailService.sendEmail({
            to: context.followOn.emailTo,
            subject: context.followOn.emailSubject,
            body: context.followOn.emailBody,
            associations: [{ entityType: 'sprk_project', entityId: projectId, entityName: projectName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        const hasWarnings = warnings.length > 0;

        const viewProject = () => {
          navigateToEntity({
            action: 'openRecord',
            entityName: 'sprk_project',
            entityId: projectId,
          });
          onClose();
        };

        return {
          icon: (
            <CheckmarkCircleFilled
              fontSize={64}
              style={{ color: tokens.colorPaletteGreenForeground1 }}
            />
          ),
          title: hasWarnings ? 'Project created with warnings' : 'Project created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
                &ldquo;{projectName}&rdquo;
              </span>{' '}
              has been created
              {hasWarnings
                ? ', though some operations could not complete. See details below.'
                : ' and is ready to use.'}
            </Text>
          ),
          actions: (
            <>
              <Button
                appearance="primary"
                onClick={viewProject}
                aria-label={`View project: ${projectName}`}
              >
                View Project
              </Button>
              <Button appearance="secondary" onClick={onClose}>
                Close
              </Button>
            </>
          ),
          warnings,
        };
      },
    }),
    [formValid, formValues, webApi, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose]
  );

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <CreateRecordWizard
      open={open}
      onClose={onClose}
      webApi={webApi}
      config={config}
    />
  );
};

export default ProjectWizardDialog;
