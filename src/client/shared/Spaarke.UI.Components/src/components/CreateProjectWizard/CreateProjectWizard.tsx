/**
 * CreateProjectWizard.tsx
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
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *   - navigationService: optional INavigationService for opening records
 *
 * Default export enables React.lazy() dynamic import for bundle-size
 * optimization (same pattern as WizardDialog.tsx).
 *
 * @see IDataService — high-level data access abstraction
 * @see INavigationService — navigation abstraction
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import {
  CreateRecordWizard,
  type ICreateRecordWizardConfig,
  type IFinishContext,
} from '../CreateRecordWizard';

import type { IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { CreateProjectStep } from './CreateProjectStep';
import { ProjectService } from './projectService';
import { EMPTY_PROJECT_FORM } from './projectFormTypes';
import type { ICreateProjectFormState } from './projectFormTypes';

import { EntityCreationService } from '../../services/EntityCreationService';
import type { IDataService, INavigationService, IUploadService } from '../../types/serviceInterfaces';
import type { ILookupItem } from '../../types/LookupTypes';
import { provisionSecureProject } from './provisioningService';

// ---------------------------------------------------------------------------
// Search helper functions (use IDataService instead of IWebApi)
// ---------------------------------------------------------------------------

async function searchContactsAsLookup(dataService: IDataService, query: string): Promise<ILookupItem[]> {
  if (!query || query.trim().length < 2) return [];
  const safeFilter = query.trim().replace(/'/g, "''");
  const options =
    `?$select=contactid,fullname,emailaddress1` +
    `&$filter=contains(fullname,'${safeFilter}')` +
    `&$orderby=fullname asc&$top=10`;
  const result = await dataService.retrieveMultipleRecords('contact', options);
  return result.entities.map((e) => ({
    id: e['contactid'] as string,
    name: (e['fullname'] as string) + (e['emailaddress1'] ? ` (${e['emailaddress1']})` : ''),
  }));
}

async function searchOrganizationsAsLookup(dataService: IDataService, query: string): Promise<ILookupItem[]> {
  if (!query || query.trim().length < 2) return [];
  const safeFilter = query.trim().replace(/'/g, "''");
  const options =
    `?$select=sprk_organizationid,sprk_name` +
    `&$filter=contains(sprk_name,'${safeFilter}')` +
    `&$orderby=sprk_name asc&$top=10`;
  const result = await dataService.retrieveMultipleRecords('sprk_organization', options);
  return result.entities.map((e) => ({
    id: e['sprk_organizationid'] as string,
    name: e['sprk_name'] as string,
  }));
}

async function searchUsersAsLookup(dataService: IDataService, query: string): Promise<ILookupItem[]> {
  if (!query || query.trim().length < 2) return [];
  const safeFilter = query.trim().replace(/'/g, "''");
  const options =
    `?$select=systemuserid,fullname,internalemailaddress` +
    `&$filter=contains(fullname,'${safeFilter}') and isdisabled eq false` +
    `&$orderby=fullname asc&$top=10`;
  const result = await dataService.retrieveMultipleRecords('systemuser', options);
  return result.entities.map((e) => ({
    id: e['systemuserid'] as string,
    name: (e['fullname'] as string) + (e['internalemailaddress'] ? ` (${e['internalemailaddress']})` : ''),
  }));
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateProjectWizardProps {
  /** Whether the wizard dialog is open. */
  open: boolean;
  /** Called when the wizard is closed/cancelled. */
  onClose: () => void;
  /** IDataService for Dataverse entity operations. */
  dataService: IDataService;
  /** Optional IUploadService for file uploads. */
  uploadService?: IUploadService;
  /** Optional INavigationService for opening records after creation. */
  navigationService?: INavigationService;
  /** When true, hides the title bar (Dataverse modal provides chrome). */
  embedded?: boolean;
  /** MSAL-backed authenticated fetch function for BFF API calls. */
  authenticatedFetch?: typeof fetch;
  /** BFF API base URL. */
  bffBaseUrl?: string;
  /**
   * Optional callback to resolve the SPE container ID for file uploads.
   * Called when the wizard opens. If not provided, file uploads will be skipped.
   */
  resolveSpeContainerId?: () => Promise<string>;
}

// ---------------------------------------------------------------------------
// Adapter: build a webApi-shaped object from IDataService for CreateRecordWizard
// ---------------------------------------------------------------------------

function buildWebApiAdapter(dataService: IDataService) {
  return {
    retrieveMultipleRecords: async (
      entityLogicalName: string,
      options?: string,
      _maxPageSize?: number,
    ) => {
      const result = await dataService.retrieveMultipleRecords(entityLogicalName, options);
      return { entities: result.entities };
    },
    retrieveRecord: (entityLogicalName: string, id: string, options?: string) =>
      dataService.retrieveRecord(entityLogicalName, id, options),
    createRecord: async (entityLogicalName: string, data: Record<string, unknown>) => {
      const id = await dataService.createRecord(entityLogicalName, data);
      return { id };
    },
  };
}

// ---------------------------------------------------------------------------
// CreateProjectWizard
// ---------------------------------------------------------------------------

const CreateProjectWizard: React.FC<ICreateProjectWizardProps> = ({
  open,
  onClose,
  dataService,
  uploadService: _uploadService,
  navigationService,
  embedded: _embedded,
  authenticatedFetch: authFetch,
  bffBaseUrl,
  resolveSpeContainerId,
}) => {
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
    (query: string) => searchContactsAsLookup(dataService, query),
    [dataService]
  );
  const handleSearchOrganizations = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(dataService, query),
    [dataService]
  );
  const handleSearchUsers = React.useCallback(
    (query: string) => searchUsersAsLookup(dataService, query),
    [dataService]
  );

  // ── WebApi adapter for CreateRecordWizard ─────────────────────────────
  const webApiAdapter = React.useMemo(() => buildWebApiAdapter(dataService), [dataService]);

  // ── Wizard config ───────────────────────────────────────────────────────
  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Project',
      entityLabel: 'project',
      filesStepSubtitle:
        'Upload documents to associate with this project. The AI will extract key information to assist with project setup.',
      finishingLabel: formValues.isSecure
        ? 'Creating project and provisioning secure infrastructure\u2026'
        : 'Creating project\u2026',

      infoStep: {
        id: 'create-record',
        label: 'Enter Info',
        canAdvance: () => formValid,
        renderContent: (wizardFiles) => (
          <CreateProjectStep
            dataService={dataService}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            uploadedFiles={wizardFiles}
            initialFormValues={formValues}
            authenticatedFetch={authFetch}
            bffBaseUrl={bffBaseUrl}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId,

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
        const projectService = new ProjectService(dataService);
        const result = await projectService.createProject(mergedFormValues);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create project');
        }

        const projectId = result.projectId!;
        const projectName = result.projectName!;

        // 1b. Provision Secure Project infrastructure (BU, SPE container, Account)
        //     when the Secure Project toggle is enabled.
        let provisioningWarning: string | undefined;
        if (mergedFormValues.isSecure && authFetch && bffBaseUrl) {
          const provisionResult = await provisionSecureProject(
            {
              projectId,
              // Use project name as the ProjectRef when no dedicated ref field is available.
              // The BU will be named "SP-{projectName}" on the backend.
              projectRef: projectName,
            },
            authFetch,
            bffBaseUrl,
          );

          if (!provisionResult.success) {
            // Non-fatal — project record was created. Log the warning so it shows
            // on the success screen. The admin can provision manually or retry.
            provisioningWarning = provisionResult.errorMessage;
          }
        }

        // 2. Upload files to SPE + create document records
        if (context.uploadedFiles.length > 0 && context.speContainerId && authFetch && bffBaseUrl) {
          try {
            const entityService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);

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
        if (context.selectedActions.includes('send-email') && context.followOn.emailTo.trim() && authFetch && bffBaseUrl) {
          const emailService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);
          const emailResult = await emailService.sendEmail({
            to: context.followOn.emailTo,
            subject: context.followOn.emailSubject,
            body: context.followOn.emailBody,
            associations: [{ entityType: 'sprk_project', entityId: projectId, entityName: projectName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        // Include provisioning failure in warnings if present
        if (provisioningWarning) {
          warnings.push(
            `Secure Project provisioning failed: ${provisioningWarning} — ` +
            'The project record was created but the Business Unit, SPE container, and External Access Account ' +
            'may need to be provisioned manually.'
          );
        }

        const hasWarnings = warnings.length > 0;

        const viewProject = () => {
          if (navigationService) {
            navigationService.openRecord('sprk_project', projectId);
          }
          onClose();
        };

        return {
          icon: (
            <CheckmarkCircleFilled
              fontSize={64}
              style={{ color: tokens.colorPaletteGreenForeground1 }}
            />
          ),
          title: hasWarnings
            ? 'Project created with warnings'
            : mergedFormValues.isSecure
            ? 'Secure Project created!'
            : 'Project created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
                &ldquo;{projectName}&rdquo;
              </span>{' '}
              has been created
              {hasWarnings
                ? ', though some operations could not complete. See details below.'
                : mergedFormValues.isSecure
                ? ' with its Business Unit, document container, and external access account provisioned.'
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
    [formValid, formValues, dataService, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose, authFetch, bffBaseUrl, navigationService, resolveSpeContainerId, webApiAdapter]
  );

  // ── Render ──────────────────────────────────────────────────────────────

  return (
    <CreateRecordWizard
      open={open}
      onClose={onClose}
      webApi={webApiAdapter}
      config={config}
    />
  );
};

export default CreateProjectWizard;
export { CreateProjectWizard };
