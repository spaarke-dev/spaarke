/**
 * TodoWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New To Do".
 *
 * A To Do is a sprk_event record with sprk_todoflag=true.
 * Default export enables React.lazy() dynamic import.
 *
 * Dependencies are injected via props -- no solution-specific imports.
 * Uses IDataService (not IWebApi) for shared library portability.
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

import { CreateTodoStep } from './CreateTodoStep';
import { TodoService } from './todoService';
import { EMPTY_TODO_FORM } from './formTypes';
import type { ICreateTodoFormState } from './formTypes';

import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatterWizard/matterService';

import { EntityCreationService } from '../../services/EntityCreationService';
import type { IDataService } from '../../types/serviceInterfaces';
import type { AuthenticatedFetchFn } from '../../services/EntityCreationService';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateTodoWizardProps {
  /** Whether the dialog is currently open. */
  open: boolean;
  /** Callback invoked when the user clicks Cancel or closes the dialog. */
  onClose: () => void;
  /** IDataService for Dataverse operations. */
  dataService: IDataService;
  /**
   * Authenticated fetch function for BFF API calls.
   * Required for email send operations.
   */
  authenticatedFetch: AuthenticatedFetchFn;
  /**
   * BFF API base URL (e.g. "https://spe-api-dev-67e2xz.azurewebsites.net/api").
   */
  bffBaseUrl: string;
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
// TodoWizardDialog
// ---------------------------------------------------------------------------

const TodoWizardDialog: React.FC<ICreateTodoWizardProps> = ({
  open,
  onClose,
  dataService,
  authenticatedFetch,
  bffBaseUrl,
  embedded,
  resolveSpeContainerId,
}) => {
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
            dataService={dataService}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            initialFormValues={formValues}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId: resolveSpeContainerId
        ? resolveSpeContainerId
        : () => Promise.resolve(''),

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

        const todoService = new TodoService(dataService);
        const result = await todoService.createTodo(currentFormValues);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create to do');
        }

        const todoId = result.todoId!;
        const todoName = result.todoName!;
        const warnings: string[] = [];

        // Send email (if selected)
        if (_context.selectedActions.includes('send-email') && _context.followOn.emailTo.trim()) {
          // Wrap IDataService to adapt to IWebApiWithCreate expected by EntityCreationService
          const webApiAdapter = {
            createRecord: async (entityName: string, data: Record<string, unknown>) => {
              const id = await dataService.createRecord(entityName, data);
              return { id };
            },
            retrieveRecord: (entityName: string, id: string, options?: string) =>
              dataService.retrieveRecord(entityName, id, options),
            retrieveMultipleRecords: (entityName: string, options?: string) =>
              dataService.retrieveMultipleRecords(entityName, options),
            updateRecord: async (entityName: string, id: string, data: Record<string, unknown>) => {
              await dataService.updateRecord(entityName, id, data);
              return { id };
            },
            deleteRecord: async (entityName: string, id: string) => {
              await dataService.deleteRecord(entityName, id);
              return { id };
            },
          };
          const entityService = new EntityCreationService(webApiAdapter, authenticatedFetch, bffBaseUrl);
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
    [formValid, formValues, dataService, authenticatedFetch, bffBaseUrl, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose, resolveSpeContainerId]
  );

  // Adapt IDataService to the webApi shape that CreateRecordWizard expects
  const webApiAdapter = React.useMemo(() => ({
    createRecord: async (entityName: string, data: Record<string, unknown>) => {
      const id = await dataService.createRecord(entityName, data);
      return { id };
    },
    retrieveRecord: (entityName: string, id: string, options?: string) =>
      dataService.retrieveRecord(entityName, id, options),
    retrieveMultipleRecords: (entityName: string, options?: string, _maxPageSize?: number) =>
      dataService.retrieveMultipleRecords(entityName, options),
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

export { TodoWizardDialog };
export default TodoWizardDialog;
