/**
 * CreateMatterWizard.tsx
 * Main exported component for the "Create New Matter" wizard.
 *
 * This is a thin wrapper around CreateRecordWizard that provides:
 *   - AssociateToStep as step 1 (optional, links matter to a Project or Account via N:N)
 *   - Entity-specific form step (CreateRecordStep)
 *   - Finish handler (MatterService.createMatter + success screen + N:N association)
 *   - Search callbacks (contacts, organizations, users)
 *   - Email template builders
 *
 * Step sequence:
 *   1. Associate To  — optional; links to Project (sprk_project) or Account (account)
 *   2. Add file(s)   — upload documents for AI pre-fill
 *   3. Enter Info    — matter form fields
 *   4. Next Steps    — follow-on action selection
 *
 * After matter creation, if a Project association was selected, the N:N
 * sprk_Project_Matter_nn relationship is established via IDataService.
 *
 * All generic wizard mechanics (file upload, follow-on steps, state
 * management) are handled by the shared CreateRecordWizard component.
 *
 * Dependencies are injected via props -- no solution-specific imports.
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

import type { ICreateMatterFormState } from './formTypes';
import { CreateRecordStep } from './CreateRecordStep';
import {
  MatterService,
  IFollowOnActions,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
  searchMatterTypes,
  searchPracticeAreas,
  fetchAiDraftSummary,
} from './matterService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';
import type { AssociationResult } from '../AssociateToStep/types';

// ---------------------------------------------------------------------------
// Association helper
// ---------------------------------------------------------------------------

/**
 * Creates a Dataverse association between the newly created matter and the
 * record selected in AssociateToStep.
 *
 * - sprk_project: uses the N:N relationship sprk_Project_Matter_nn
 * - account: uses a direct $ref association on the matter's account lookup
 *
 * Returns a success/failure result; never throws.
 */
async function associateToRecord(
  dataService: IDataService,
  matterId: string,
  association: AssociationResult
): Promise<{ success: boolean }> {
  try {
    const { entityType, recordId } = association;

    if (entityType === 'sprk_project') {
      // N:N association via the relationship collection navigation property.
      // Dataverse REST API: POST /sprk_projects({projectId})/sprk_Project_Matter_nn/$ref
      // Body: { "@odata.id": "[base]/sprk_matters({matterId})" }
      const apiBase = '/api/data/v9.0';
      const url = `${apiBase}/sprk_projects(${recordId})/sprk_Project_Matter_nn/$ref`;
      const resp = await fetch(url, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json; odata.metadata=minimal' },
        body: JSON.stringify({ '@odata.id': `${apiBase}/sprk_matters(${matterId})` }),
      });
      if (!resp.ok) {
        const text = await resp.text().catch(() => resp.statusText);
        console.warn('[CreateMatterWizard] N:N association response not OK:', resp.status, text);
        return { success: false };
      }
      console.info(
        '[CreateMatterWizard] N:N association created:',
        `sprk_project(${recordId}) <-> sprk_matter(${matterId})`
      );
      return { success: true };
    }

    if (entityType === 'account') {
      // For account: update the matter record's account lookup via @odata.bind.
      // This is a N:1 association on the matter side.
      await dataService.updateRecord('sprk_matter', matterId, {
        'sprk_Account@odata.bind': `/accounts(${recordId})`,
      });
      console.info(
        '[CreateMatterWizard] Account association set:',
        `account(${recordId}) -> sprk_matter(${matterId})`
      );
      return { success: true };
    }

    // Unsupported entity type -- skip silently
    console.warn('[CreateMatterWizard] Unsupported association entity type:', entityType);
    return { success: true };
  } catch (err) {
    console.warn('[CreateMatterWizard] Association failed:', err instanceof Error ? err.message : err);
    return { success: false };
  }
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateMatterWizardProps {
  /** Whether the dialog is currently open. */
  open: boolean;
  /** Callback invoked when the user clicks Cancel or closes the dialog. */
  onClose: () => void;
  /** IDataService for Dataverse operations. */
  dataService: IDataService;
  /**
   * Authenticated fetch function for BFF API calls.
   * Required for AI pre-fill and summary features.
   */
  authenticatedFetch: AuthenticatedFetchFn;
  /**
   * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
   */
  bffBaseUrl: string;
  /**
   * Optional navigation service for opening entity records.
   * If provided, the success screen "View Matter" button will use this.
   */
  navigationService?: INavigationService;
  /**
   * When `embedded={true}`, the wizard relies on the Dataverse modal chrome
   * for the title bar and close button. Default: false.
   */
  embedded?: boolean;
  /**
   * Resolves the SPE container ID for file uploads.
   * Called once during the finish handler. If not provided, file uploads
   * will be skipped.
   */
  resolveSpeContainerId?: () => Promise<string>;
}

// ---------------------------------------------------------------------------
// Empty form state
// ---------------------------------------------------------------------------

const EMPTY_FORM_STATE: ICreateMatterFormState = {
  matterTypeId: '',
  matterTypeName: '',
  practiceAreaId: '',
  practiceAreaName: '',
  matterName: '',
  assignedAttorneyId: '',
  assignedAttorneyName: '',
  assignedParalegalId: '',
  assignedParalegalName: '',
  assignedOutsideCounselId: '',
  assignedOutsideCounselName: '',
  summary: '',
};

// ---------------------------------------------------------------------------
// CreateMatterWizard
// ---------------------------------------------------------------------------

export const CreateMatterWizard: React.FC<ICreateMatterWizardProps> = ({
  open,
  onClose,
  dataService,
  authenticatedFetch,
  bffBaseUrl,
  navigationService,
  embedded,
  resolveSpeContainerId,
}) => {
  // -- Entity-specific form state --
  const [step2Valid, setStep2Valid] = React.useState(false);
  const [step2FormValues, setStep2FormValues] = React.useState<ICreateMatterFormState>(EMPTY_FORM_STATE);
  const step2FormValuesRef = React.useRef(step2FormValues);
  step2FormValuesRef.current = step2FormValues;

  // Reset form state on open
  React.useEffect(() => {
    if (open) {
      setStep2Valid(false);
      setStep2FormValues(EMPTY_FORM_STATE);
    }
  }, [open]);

  // -- Search callbacks --
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
  const handleSearchMatterTypes = React.useCallback(
    (query: string) => searchMatterTypes(dataService, query),
    [dataService]
  );
  const handleSearchPracticeAreas = React.useCallback(
    (query: string) => searchPracticeAreas(dataService, query),
    [dataService]
  );

  // -- Wizard config --
  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Matter',
      entityLabel: 'matter',
      filesStepSubtitle:
        'Upload documents for AI analysis. The AI will extract key information to pre-fill the matter form.',
      finishingLabel: 'Creating matter\u2026',

      // Associate To step — optional step 1.
      // Requires navigationService (for the Dataverse lookup dialog).
      // Allows linking the new matter to a Project or Account before creation.
      ...(navigationService
        ? {
            associateToStep: {
              entityTypes: [
                { label: 'Project', entityType: 'sprk_project' },
                { label: 'Account', entityType: 'account' },
              ],
              navigationService,
            },
          }
        : {}),

      infoStep: {
        id: 'create-record',
        label: 'Enter Info',
        canAdvance: () => step2Valid,
        renderContent: (wizardFiles) => (
          <CreateRecordStep
            dataService={dataService}
            uploadedFileNames={wizardFiles.map((f) => f.name)}
            uploadedFiles={wizardFiles}
            onValidChange={setStep2Valid}
            onSubmit={(values) => setStep2FormValues(values)}
            initialFormValues={step2FormValues}
            authenticatedFetch={authenticatedFetch}
            bffBaseUrl={bffBaseUrl}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,
      searchMatterTypes: handleSearchMatterTypes,
      searchPracticeAreas: handleSearchPracticeAreas,

      getAssignWorkDefaults: () => ({
        assignWorkMatterTypeId: step2FormValuesRef.current.matterTypeId,
        assignWorkMatterTypeName: step2FormValuesRef.current.matterTypeName,
        assignWorkPracticeAreaId: step2FormValuesRef.current.practiceAreaId,
        assignWorkPracticeAreaName: step2FormValuesRef.current.practiceAreaName,
      }),

      resolveSpeContainerId: resolveSpeContainerId
        ? resolveSpeContainerId
        : () => Promise.resolve(''),

      buildEmailSubject: (entityName: string) => `New Matter: ${entityName}`,
      buildEmailBody: (fields: Record<string, string>) => {
        const typeStr = fields.matterTypeName ? ` ${fields.matterTypeName.toLowerCase()}` : '';
        const areaStr = fields.practiceAreaName ? ` (${fields.practiceAreaName})` : '';
        return (
          `Dear Client,\n\n` +
          `We are pleased to confirm that your${typeStr} matter, "${fields.matterName || ''}"${areaStr}, ` +
          `has been created in our legal management system.\n\n` +
          `Our team will be in touch shortly to discuss next steps and any actions required from you.\n\n` +
          `Please do not hesitate to reach out if you have any questions.\n\n` +
          `Kind regards,\n[Your Name]\n[Firm Name]`
        );
      },
      getEntityName: () => step2FormValuesRef.current.matterName,
      getFormFields: () => ({
        matterName: step2FormValuesRef.current.matterName,
        matterTypeName: step2FormValuesRef.current.matterTypeName,
        practiceAreaName: step2FormValuesRef.current.practiceAreaName,
      }),

      fetchAiSummary: () =>
        fetchAiDraftSummary(
          step2FormValuesRef.current.matterName,
          step2FormValuesRef.current.matterTypeName,
          step2FormValuesRef.current.practiceAreaName,
          authenticatedFetch,
          bffBaseUrl
        ),

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const currentFormValues = step2FormValuesRef.current;
        const mergedFormValues = {
          ...currentFormValues,
        };

        const followOnActions: IFollowOnActions = {};
        if (context.selectedActions.includes('draft-summary')) {
          const allEmails = [
            ...context.followOn.recipients.map((r) => r.email).filter(Boolean),
            ...context.followOn.ccRecipients.map((r) => r.email).filter(Boolean),
          ];
          followOnActions.draftSummary = { recipientEmails: allEmails };
        }
        if (context.selectedActions.includes('send-email') && context.followOn.emailTo.trim()) {
          followOnActions.sendEmail = {
            to: context.followOn.emailTo.trim(),
            subject: context.followOn.emailSubject,
            body: context.followOn.emailBody,
          };
        }

        const service = new MatterService(
          dataService,
          authenticatedFetch,
          bffBaseUrl,
          context.speContainerId || undefined
        );
        const result = await service.createMatter(mergedFormValues, context.uploadedFiles, followOnActions);

        if (result.status === 'error') {
          throw new Error(result.errorMessage ?? 'An unknown error occurred.');
        }

        const matterId = result.matterId!;
        const matterName = result.matterName!;

        // -- Create Work Assignment (sprk_workassignment) --
        // When the user selected "Assign Work" follow-on and entered a name,
        // create the work assignment record linked to the matter via N:1.
        if (context.selectedActions.includes('assign-counsel') && context.followOn.assignWorkName.trim()) {
          try {
            const workAssignmentPayload: Record<string, unknown> = {
              sprk_name: context.followOn.assignWorkName.trim(),
              sprk_priority: context.followOn.assignWorkPriority,
            };
            if (context.followOn.assignWorkDescription.trim()) {
              workAssignmentPayload['sprk_description'] = context.followOn.assignWorkDescription.trim();
            }
            if (context.followOn.assignWorkResponseDueDate) {
              workAssignmentPayload['sprk_responseduedate'] = context.followOn.assignWorkResponseDueDate;
            }
            // N:1 link to parent matter via relationship
            workAssignmentPayload['sprk_workassignment_RegardingMatter_sprk_matter_n1@odata.bind'] =
              `/sprk_matters(${matterId})`;
            // Classification lookups
            if (context.followOn.assignWorkMatterTypeId) {
              workAssignmentPayload['sprk_MatterType@odata.bind'] =
                `/sprk_mattertype_refs(${context.followOn.assignWorkMatterTypeId})`;
            }
            if (context.followOn.assignWorkPracticeAreaId) {
              workAssignmentPayload['sprk_PracticeArea@odata.bind'] =
                `/sprk_practicearea_refs(${context.followOn.assignWorkPracticeAreaId})`;
            }
            // Resource lookups
            if (context.followOn.assignedAttorneyId) {
              workAssignmentPayload['sprk_AssignedAttorney@odata.bind'] =
                `/contacts(${context.followOn.assignedAttorneyId})`;
            }
            if (context.followOn.assignedParalegalId) {
              workAssignmentPayload['sprk_AssignedParalegal@odata.bind'] =
                `/contacts(${context.followOn.assignedParalegalId})`;
            }
            if (context.followOn.assignedOutsideCounselId) {
              workAssignmentPayload['sprk_AssignedOutsideCounsel@odata.bind'] =
                `/sprk_organizations(${context.followOn.assignedOutsideCounselId})`;
            }
            await dataService.createRecord('sprk_workassignment', workAssignmentPayload);
            console.info('[CreateMatterWizard] Work assignment created and linked to matter:', matterId);
          } catch (err) {
            const message = err instanceof Error ? err.message : 'Unknown error';
            result.warnings.push(
              `Work assignment could not be created (${message}). ` +
              'You can create it manually from the matter record.'
            );
          }
        }

        // -- Wire N:N association (sprk_Project_Matter_nn) --
        // If the user selected an association in step 1, create the link now.
        // This is a non-blocking operation -- failure produces a warning, not an error.
        if (context.association?.recordId) {
          const assocResult = await associateToRecord(
            dataService,
            matterId,
            context.association
          );
          if (!assocResult.success) {
            result.warnings.push(
              `Matter created, but could not link to "${context.association.recordName}". ` +
              'You can associate them manually from the matter record.'
            );
          }
        }

        const hasWarnings = result.warnings.length > 0;

        const viewMatter = () => {
          if (navigationService) {
            navigationService.openRecord('sprk_matter', matterId);
          }
          onClose();
        };

        return {
          icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
          title: hasWarnings ? 'Matter created with warnings' : 'Matter created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>&ldquo;{matterName}&rdquo;</span>{' '}
              has been created{hasWarnings ? ', though some follow-on actions could not complete. See details below.' : ' and is ready to use.'}
            </Text>
          ),
          actions: (
            <>
              <Button appearance="primary" onClick={viewMatter} aria-label={`View matter: ${matterName}`}>View Matter</Button>
              <Button appearance="secondary" onClick={onClose}>Close</Button>
            </>
          ),
          warnings: result.warnings,
        };
      },
    }),
    [step2Valid, step2FormValues, dataService, authenticatedFetch, bffBaseUrl, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, handleSearchMatterTypes, handleSearchPracticeAreas, onClose, navigationService, resolveSpeContainerId]
  );

  // Adapt IDataService to the IWebApi shape that CreateRecordWizard expects
  const webApiAdapter = React.useMemo(() => ({
    createRecord: async (entityName: string, data: Record<string, unknown>) => {
      const id = await dataService.createRecord(entityName, data);
      return { id };
    },
    retrieveRecord: (entityName: string, id: string, options?: string) =>
      dataService.retrieveRecord(entityName, id, options),
    retrieveMultipleRecords: (entityName: string, options?: string, maxPageSize?: number) =>
      dataService.retrieveMultipleRecords(entityName, options),
    updateRecord: async (entityName: string, id: string, data: Record<string, unknown>) => {
      await dataService.updateRecord(entityName, id, data);
      return { id };
    },
    deleteRecord: async (entityName: string, id: string) => {
      await dataService.deleteRecord(entityName, id);
      return { id };
    },
  }), [dataService]);

  return (
    <CreateRecordWizard
      open={open}
      onClose={onClose}
      webApi={webApiAdapter}
      config={config}
      embedded={embedded}
    />
  );
};

export default CreateMatterWizard;
