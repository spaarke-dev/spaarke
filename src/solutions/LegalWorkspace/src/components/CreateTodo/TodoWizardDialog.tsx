/**
 * TodoWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New To Do".
 *
 * A To Do is a sprk_event record with sprk_todoflag=true.
 * Default export enables React.lazy() dynamic import.
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

import { CreateTodoStep } from './CreateTodoStep';
import { TodoService } from './todoService';
import { EMPTY_TODO_FORM } from './formTypes';
import type { ICreateTodoFormState } from './formTypes';

import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatter/matterService';

import { EntityCreationService } from '../../services/EntityCreationService';
import { getBffBaseUrl } from '../../config/bffConfig';
import { authenticatedFetch } from '../../services/authInit';
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface ITodoWizardDialogProps {
  open: boolean;
  onClose: () => void;
  webApi: IWebApi;
}

// ---------------------------------------------------------------------------
// TodoWizardDialog
// ---------------------------------------------------------------------------

const TodoWizardDialog: React.FC<ITodoWizardDialogProps> = ({ open, onClose, webApi }) => {
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateTodoFormState>(EMPTY_TODO_FORM);
  const formValuesRef = React.useRef(formValues);
  formValuesRef.current = formValues;

  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(EMPTY_TODO_FORM);
    }
  }, [open]);

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

  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New To Do',
      entityLabel: 'to do',
      filesStepSubtitle:
        'Upload documents to associate with this to do, or click Next to skip.',
      finishingLabel: 'Creating to do\u2026',

      infoStep: {
        id: 'create-record',
        label: 'To Do Details',
        canAdvance: () => formValid,
        renderContent: (_wizardFiles) => (
          <CreateTodoStep
            webApi={webApi}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            initialFormValues={formValues}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId: () => getSpeContainerIdFromBusinessUnit(webApi),

      buildEmailSubject: (entityName: string) => `New To Do: ${entityName}`,
      buildEmailBody: (fields: Record<string, string>) =>
        `Dear Colleague,\n\nA new to do item, "${fields.title || ''}", has been created.\n\n` +
        `Please review and take any necessary action.\n\n` +
        `Kind regards,\n[Your Name]`,
      getEntityName: () => formValuesRef.current.title,
      getFormFields: () => ({
        title: formValuesRef.current.title,
      }),

      onFinish: async (_context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const currentFormValues = formValuesRef.current;

        const todoService = new TodoService(webApi);
        const result = await todoService.createTodo(currentFormValues);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create to do');
        }

        const todoId = result.todoId!;
        const todoName = result.todoName!;
        const warnings: string[] = [];

        // Send email (if selected)
        if (_context.selectedActions.includes('send-email') && _context.followOn.emailTo.trim()) {
          const entityService = new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl());
          const emailResult = await entityService.sendEmail({
            to: _context.followOn.emailTo,
            subject: _context.followOn.emailSubject,
            body: _context.followOn.emailBody,
            associations: [{ entityType: 'sprk_event', entityId: todoId, entityName: todoName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        const hasWarnings = warnings.length > 0;

        return {
          icon: (
            <CheckmarkCircleFilled
              fontSize={64}
              style={{ color: tokens.colorPaletteGreenForeground1 }}
            />
          ),
          title: hasWarnings ? 'To Do created with warnings' : 'To Do created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
                &ldquo;{todoName}&rdquo;
              </span>{' '}
              has been added to your to do list
              {hasWarnings
                ? ', though some operations could not complete. See details below.'
                : '.'}
            </Text>
          ),
          actions: (
            <Button appearance="primary" onClick={onClose}>
              Done
            </Button>
          ),
          warnings,
        };
      },
    }),
    [formValid, formValues, webApi, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose]
  );

  return (
    <CreateRecordWizard
      open={open}
      onClose={onClose}
      webApi={webApi}
      config={config}
    />
  );
};

export default TodoWizardDialog;
