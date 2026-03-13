/**
 * WizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New Matter".
 *
 * Provides only:
 *   - Entity-specific form step (CreateRecordStep)
 *   - Finish handler (MatterService.createMatter + success screen)
 *   - Search callbacks (contacts, organizations, users)
 *   - Email template builders
 *
 * All generic wizard mechanics (file upload, follow-on steps, state
 * management) are handled by the shared CreateRecordWizard component.
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

import type { IWizardDialogProps } from './wizardTypes';
import { CreateRecordStep } from './CreateRecordStep';
import type { ICreateMatterFormState } from './formTypes';
import {
  MatterService,
  IFollowOnActions,
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
  fetchAiDraftSummary,
} from './matterService';
import type { IWebApi } from '../../types/xrm';
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
import { navigateToEntity } from '../../utils/navigation';

// ---------------------------------------------------------------------------
// Extended props (internal — adds webApi)
// ---------------------------------------------------------------------------

export interface IWizardDialogPropsInternal extends IWizardDialogProps {
  webApi?: IWebApi;
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
// WizardDialog
// ---------------------------------------------------------------------------

export const WizardDialog: React.FC<IWizardDialogPropsInternal> = ({
  open,
  onClose,
  webApi,
}) => {
  // ── Entity-specific form state ──────────────────────────────────────────
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

  // ── Search callbacks ────────────────────────────────────────────────────
  const handleSearchContacts = React.useCallback(
    (query: string) => webApi ? searchContactsAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );
  const handleSearchOrganizations = React.useCallback(
    (query: string) => webApi ? searchOrganizationsAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );
  const handleSearchUsers = React.useCallback(
    (query: string) => webApi ? searchUsersAsLookup(webApi, query) : Promise.resolve([]),
    [webApi]
  );

  // ── Wizard config ───────────────────────────────────────────────────────
  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Matter',
      entityLabel: 'matter',
      filesStepSubtitle:
        'Upload documents for AI analysis. The AI will extract key information to pre-fill the matter form.',
      finishingLabel: 'Creating matter\u2026',

      infoStep: {
        id: 'create-record',
        label: 'Enter Info',
        canAdvance: () => step2Valid,
        renderContent: (wizardFiles) => (
          <CreateRecordStep
            webApi={webApi!}
            uploadedFileNames={wizardFiles.map((f) => f.name)}
            uploadedFiles={wizardFiles}
            onValidChange={setStep2Valid}
            onSubmit={(values) => setStep2FormValues(values)}
            initialFormValues={step2FormValues}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId: () =>
        webApi ? getSpeContainerIdFromBusinessUnit(webApi) : Promise.resolve(''),

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
          step2FormValuesRef.current.practiceAreaName
        ),

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        if (!webApi) {
          throw new Error('Dataverse connection not available. Please close and retry.');
        }

        const currentFormValues = step2FormValuesRef.current;
        const mergedFormValues = {
          ...currentFormValues,
          assignedAttorneyId: context.followOn.assignedAttorneyId || currentFormValues.assignedAttorneyId,
          assignedAttorneyName: context.followOn.assignedAttorneyName || currentFormValues.assignedAttorneyName,
          assignedParalegalId: context.followOn.assignedParalegalId || currentFormValues.assignedParalegalId,
          assignedParalegalName: context.followOn.assignedParalegalName || currentFormValues.assignedParalegalName,
          assignedOutsideCounselId: context.followOn.assignedOutsideCounselId || currentFormValues.assignedOutsideCounselId,
          assignedOutsideCounselName: context.followOn.assignedOutsideCounselName || currentFormValues.assignedOutsideCounselName,
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

        const service = new MatterService(webApi, context.speContainerId || undefined);
        const result = await service.createMatter(mergedFormValues, context.uploadedFiles, followOnActions);

        if (result.status === 'error') {
          throw new Error(result.errorMessage ?? 'An unknown error occurred.');
        }

        const matterId = result.matterId!;
        const matterName = result.matterName!;
        const hasWarnings = result.warnings.length > 0;

        const viewMatter = () => {
          navigateToEntity({ action: 'openRecord', entityName: 'sprk_matter', entityId: matterId });
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
    [step2Valid, step2FormValues, webApi, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose]
  );

  if (!webApi) return null;

  return (
    <CreateRecordWizard
      open={open}
      onClose={onClose}
      webApi={webApi}
      config={config}
    />
  );
};

export default WizardDialog;
