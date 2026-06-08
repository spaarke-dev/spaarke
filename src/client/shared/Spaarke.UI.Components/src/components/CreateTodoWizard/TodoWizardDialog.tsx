/**
 * TodoWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New To Do" (R3 — targets `sprk_todo`).
 *
 * Per smart-todo-decoupling-r3 spec:
 *   - FR-15: The wizard creates `sprk_todo` records (NOT `sprk_event` with `todoflag=true`).
 *   - FR-16: The AssociateToStep step is skippable (skipping creates a standalone todo
 *            with all 11 regarding lookups and 4 resolver fields null).
 *   - ADR-024: When the user selects a regarding record, `applyResolverFields` is invoked
 *              by the service layer to atomically populate the entity-specific lookup
 *              and the four resolver fields.
 *   - OS-1: No compat path — never writes a sprk_event row.
 *
 * Default export enables React.lazy() dynamic import (preserved from R1/R2).
 *
 * Dependencies are injected via props — no solution-specific imports.
 *
 * @see CreateRecordWizard — the underlying multi-step shell (reused; AssociateToStep is
 *                           prepended as wizard step 0 via the `associateToStep` config).
 * @see TodoService — the create handler (writes sprk_todo + optional resolver fields).
 * @see ./formTypes.ts — captured form state shape
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import { CreateRecordWizard, type ICreateRecordWizardConfig, type IFinishContext } from '../CreateRecordWizard';

import type { IWizardSuccessConfig } from '../Wizard/wizardShellTypes';
import { TODO_REGARDING_TARGETS } from '../AssociateToStep/types';

import { CreateTodoStep } from './CreateTodoStep';
import { TodoService } from './todoService';
import { EMPTY_TODO_FORM } from './formTypes';
import type { ICreateTodoFormState, IInitialRegarding } from './formTypes';

import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatterWizard/matterService';

import { EntityCreationService } from '../../services/EntityCreationService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
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
   * INavigationService for opening the Dataverse lookup dialog from the
   * AssociateToStep wizard step. When omitted, the AssociateToStep is
   * disabled (wizard runs without the regarding step). Most callers should
   * pass a real INavigationService so users can associate the To Do.
   */
  navigationService?: INavigationService;
  /**
   * Optional initial regarding selection (R3 FR-16). When supplied, the
   * AssociateToStep is pre-filled with this record so launch contexts that
   * already know the parent record (e.g., a Matter detail-page ribbon
   * button, the Outlook "Create To Do" ribbon) skip the user-selection step.
   * The user may still change the selection or clear it before advancing.
   *
   * Canonical launch contexts (see
   * `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md`):
   *   1. Kanban "Add To Do"                  → `undefined` (no pre-fill)
   *   2. Parent-form ribbon (Matter / etc.)  → launch record triple
   *   3. Outlook add-in "Create To Do"       → `sprk_communication` triple
   *
   * Task 031 added the prop surface; task 032 wired the pre-fill end-to-end
   * via `IAssociateToStepConfig.initialAssociation`.
   */
  initialRegarding?: IInitialRegarding;
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
  navigationService,
  initialRegarding,
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
      filesStepSubtitle: 'Upload documents to associate with this to do, or click Next to skip.',
      finishingLabel: 'Creating to do…',

      // FR-16: AssociateToStep is prepended (skippable). The CreateRecordWizard
      // shell renders the eleven-target dropdown + lookup; selection is captured
      // into context.association and passed through to onFinish.
      //
      // R3 task 032 — launch-context pre-fill (FR-16):
      // When the caller supplies `initialRegarding`, the AssociateToStep starts with
      // that record pre-selected. Three canonical launch contexts:
      //   1. Kanban "Add To Do"                  → initialRegarding = undefined (no pre-fill)
      //   2. Parent-form ribbon (Matter / etc.)  → initialRegarding = launch record
      //   3. Outlook add-in "Create To Do"       → initialRegarding = sprk_communication
      // See projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md.
      associateToStep: navigationService
        ? {
            entityTypes: TODO_REGARDING_TARGETS.slice(),
            navigationService,
            initialAssociation: initialRegarding,
          }
        : undefined,

      infoStep: {
        id: 'create-record',
        label: 'To Do Details',
        canAdvance: () => formValid,
        renderContent: _wizardFiles => (
          <CreateTodoStep
            dataService={dataService}
            onSearchUsers={handleSearchUsers}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            initialFormValues={formValues}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId: resolveSpeContainerId ? resolveSpeContainerId : () => Promise.resolve(''),

      buildEmailSubject: (entityName: string) => `New To Do: ${entityName}`,
      buildEmailBody: (fields: Record<string, string>) =>
        `Dear Colleague,\n\nA new to do item, "${fields.title || ''}", has been created.\n\n` +
        `Please review and take any necessary action.\n\n` +
        `Kind regards,\n[Your Name]`,
      getEntityName: () => formValuesRef.current.title,
      getFormFields: () => ({
        title: formValuesRef.current.title,
      }),

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const currentFormValues = formValuesRef.current;

        // ── Create sprk_todo (ADR-024: applyResolverFields runs inside TodoService when
        //    a regarding triple is supplied; skipped wizard means context.association is null)
        const todoService = new TodoService(dataService);
        const result = await todoService.createTodo(currentFormValues, context.association);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create to do');
        }

        const todoId = result.todoId!;
        const todoName = result.todoName!;
        const warnings: string[] = [];

        // ── Send email (if selected) — preserved from R1/R2 follow-on flow ────────
        if (context.selectedActions.includes('send-email') && context.followOn.emailTo.trim()) {
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
            to: context.followOn.emailTo,
            subject: context.followOn.emailSubject,
            body: context.followOn.emailBody,
            // Per FR-15: the association entity type is sprk_todo (NOT sprk_event)
            associations: [{ entityType: 'sprk_todo', entityId: todoId, entityName: todoName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        const hasWarnings = warnings.length > 0;

        return {
          icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
          title: hasWarnings ? 'To Do created with warnings' : 'To Do created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>&ldquo;{todoName}&rdquo;</span> has
              been added to your to do list
              {hasWarnings ? ', though some operations could not complete. See details below.' : '.'}
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
    [
      formValid,
      formValues,
      dataService,
      navigationService,
      initialRegarding,
      authenticatedFetch,
      bffBaseUrl,
      handleSearchContacts,
      handleSearchOrganizations,
      handleSearchUsers,
      onClose,
      resolveSpeContainerId,
    ]
  );

  // Adapt IDataService to the webApi shape that CreateRecordWizard expects
  const webApiAdapter = React.useMemo(
    () => ({
      createRecord: async (entityName: string, data: Record<string, unknown>) => {
        const id = await dataService.createRecord(entityName, data);
        return { id };
      },
      retrieveRecord: (entityName: string, id: string, options?: string) =>
        dataService.retrieveRecord(entityName, id, options),
      retrieveMultipleRecords: (entityName: string, options?: string, _maxPageSize?: number) =>
        dataService.retrieveMultipleRecords(entityName, options),
    }),
    [dataService]
  );

  return (
    <CreateRecordWizard open={open} onClose={onClose} webApi={webApiAdapter} config={config} embedded={embedded} />
  );
};

export { TodoWizardDialog };
export default TodoWizardDialog;
