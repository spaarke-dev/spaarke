/**
 * CreateReportCardWizard.tsx
 * Thin wrapper around CreateRecordWizard for "Create New Report Card".
 *
 * Provides:
 *   - Entity-specific form step (CreateReportCardStep)
 *   - Finish handler (ReportCardService.createReportCard + follow-on record creation)
 *   - A custom Next Steps card set — Send Email / Add To Do / Assign Work
 *     (design.md §5.9, mirroring CreateInvoiceWizard's task 030 pattern) via
 *     `config.followOnCards`. This wizard owns all follow-on state itself
 *     (mirroring the established `WorkAssignmentWizardDialog` /
 *     `CreateInvoiceWizard` pattern of ref-backed closures).
 *   - `config.hideFilesStep: true` (task 040 addition to
 *     `ICreateRecordWizardConfig`) — owner decision: NO Add Files step for
 *     this wizard, despite `sprk_reportcard` having a `sprk_containerid`
 *     column (the wizard doesn't populate it or offer upload).
 *
 * All other generic wizard mechanics (step navigation, dialog chrome) are
 * handled by the shared CreateRecordWizard component.
 *
 * Dependencies are injected via props (no solution-specific imports):
 *   - dataService: IDataService for Dataverse operations
 *   - authenticatedFetch: MSAL-backed fetch function
 *   - bffBaseUrl: BFF API base URL
 *   - navigationService: optional INavigationService for opening records
 *
 * Default export enables React.lazy() dynamic import for bundle-size
 * optimization (same pattern as CreateEventWizard/CreateInvoiceWizard).
 *
 * NOTE on "Assign Work" follow-on: `sprk_workassignment` has NO
 * entity-specific lookup targeting `sprk_reportcard` today (confirmed via
 * live `describe('tables/sprk_workassignment')` — only regardingmatter/
 * regardingproject/regardinginvoice/regardingevent/regardingcommunication
 * exist). Rather than silently write a partial/degraded ADR-024 resolver
 * association (resolver text/URL fields with no entity-specific lookup —
 * out of spec with ADR-024's "MUST include both" rule) or add an
 * un-approved schema column, the Assign Work follow-on here creates a
 * standalone `sprk_workassignment` (still collecting all the resource
 * fields) with NO regarding link back to the Report Card. This is a
 * deliberate, documented scope boundary — only the owner-approved
 * `sprk_todo` -> `sprk_reportcard` lookup (task 040 step 1) was added.
 *
 * @see IDataService — high-level data access abstraction
 * @see INavigationService — navigation abstraction
 * @see CreateInvoiceWizard/CreateInvoiceWizard.tsx — closest structural template
 *   (Matter+Project-only associateToStep + Send Email/Add To Do/Assign Work followOnCards)
 * @see CreateWorkAssignmentWizard/workAssignmentService.ts — canonical service pattern
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled, PersonRegular, CheckmarkCircleRegular, MailRegular } from '@fluentui/react-icons';

import {
  CreateRecordWizard,
  type ICreateRecordWizardConfig,
  type IFinishContext,
  type IAssociateToStepConfig,
  type AssociationResult,
} from '../CreateRecordWizard';
import { REPORTCARD_REGARDING_TARGETS } from '../AssociateToStep/types';

import type { IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { CreateReportCardStep } from './CreateReportCardStep';
import { ReportCardService } from './reportCardService';
import { buildEmptyReportCardForm } from './formTypes';
import type { ICreateReportCardFormState } from './formTypes';

import { EntityCreationService } from '../../services/EntityCreationService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { ILookupItem } from '../../types/LookupTypes';

import type { FollowOnCardConfig, WorkAssignmentPriorityValue } from '../WizardFollowOns';
import {
  AssignWorkFollowOnStep,
  WORK_ASSIGNMENT_PRIORITY,
  AddTodoFollowOnStep,
  SendEmailFollowOnStep,
} from '../WizardFollowOns';
import { createTodoRegardingChild } from '../WizardFollowOns';
import { EMPTY_TODO_FORM } from '../CreateTodoWizard/formTypes';
import type { ICreateTodoFormState } from '../CreateTodoWizard/formTypes';

import { WorkAssignmentService } from '../CreateWorkAssignmentWizard/workAssignmentService';
import type { ICreateWorkAssignmentFormState, IAssignWorkState } from '../CreateWorkAssignmentWizard/formTypes';
import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
  searchMatterTypes,
  searchPracticeAreas,
} from '../CreateWorkAssignmentWizard/workAssignmentService';

// ---------------------------------------------------------------------------
// Pure helpers (exported for unit testing)
// ---------------------------------------------------------------------------

/**
 * Determines the `associateToStep` config for CreateReportCardWizard.
 *
 * Mirrors `resolveInvoiceAssociateToStepConfig` (CreateInvoiceWizard.tsx)
 * exactly: gated on `navigationService` being present AND either
 * `initialAssociation` being supplied or `lockAssociation` being `true`.
 *
 * Uses `REPORTCARD_REGARDING_TARGETS` (Matter + Project only — schema-enforced;
 * `sprk_reportcard` has no other entity-specific `sprk_regarding{entity}`
 * lookups per notes/field-manifests/reportcard.md).
 */
export function resolveReportCardAssociateToStepConfig(
  navigationService: INavigationService | undefined,
  initialAssociation: AssociationResult | undefined,
  lockAssociation: boolean | undefined
): IAssociateToStepConfig | undefined {
  if (!navigationService) return undefined;
  if (initialAssociation === undefined && !lockAssociation) return undefined;

  return {
    entityTypes: REPORTCARD_REGARDING_TARGETS.slice(),
    navigationService,
    initialAssociation,
    lockAssociation,
  };
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateReportCardWizardProps {
  /** Whether the wizard dialog is open. */
  open: boolean;
  /** Called when the wizard is closed/cancelled. */
  onClose: () => void;
  /** IDataService for Dataverse entity operations. */
  dataService: IDataService;
  /** Optional INavigationService for opening records after creation. */
  navigationService?: INavigationService;
  /** When true, hides the title bar (Dataverse modal provides chrome). */
  embedded?: boolean;
  /** MSAL-backed authenticated fetch function for BFF API calls (follow-on email/todo/assign-work only — no file pipeline). */
  authenticatedFetch?: typeof fetch;
  /** BFF API base URL. */
  bffBaseUrl?: string;
  /**
   * Optional pre-filled regarding association (visual-host-create-button-r1
   * task 012/040). When supplied, the AssociateToStep starts pre-selected with
   * this record, UNLESS `lockAssociation` is also `true` (see below).
   */
  initialAssociation?: AssociationResult;
  /**
   * When `true`, the Associate-To step is hidden entirely and
   * `initialAssociation` flows straight through to `onFinish` without ever
   * showing the picker — used when launched from a Visual Host visual.
   * Defaults to `false`.
   */
  lockAssociation?: boolean;
}

// ---------------------------------------------------------------------------
// Adapter: build a webApi-shaped object from IDataService for CreateRecordWizard
// ---------------------------------------------------------------------------

function buildWebApiAdapter(dataService: IDataService) {
  return {
    retrieveMultipleRecords: async (entityLogicalName: string, options?: string, _maxPageSize?: number) => {
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
// CreateReportCardWizard
// ---------------------------------------------------------------------------

const CreateReportCardWizard: React.FC<ICreateReportCardWizardProps> = ({
  open,
  onClose,
  dataService,
  navigationService,
  embedded,
  authenticatedFetch: authFetch,
  bffBaseUrl,
  initialAssociation,
  lockAssociation,
}) => {
  // -- Entity-specific form state --------------------------------------------
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateReportCardFormState>(buildEmptyReportCardForm());
  const formValuesRef = React.useRef(formValues);
  formValuesRef.current = formValues;

  // -- Follow-on state: Assign Work -------------------------------------------
  const [assignWorkName, setAssignWorkName] = React.useState('');
  const [assignWorkDescription, setAssignWorkDescription] = React.useState('');
  const [assignWorkMatterTypeId, setAssignWorkMatterTypeId] = React.useState('');
  const [assignWorkMatterTypeName, setAssignWorkMatterTypeName] = React.useState('');
  const [assignWorkPracticeAreaId, setAssignWorkPracticeAreaId] = React.useState('');
  const [assignWorkPracticeAreaName, setAssignWorkPracticeAreaName] = React.useState('');
  const [assignWorkPriority, setAssignWorkPriority] = React.useState<WorkAssignmentPriorityValue>(
    WORK_ASSIGNMENT_PRIORITY.Normal
  );
  const [assignWorkResponseDueDate, setAssignWorkResponseDueDate] = React.useState('');
  const [assignedAttorneyId, setAssignedAttorneyId] = React.useState('');
  const [assignedAttorneyName, setAssignedAttorneyName] = React.useState('');
  const [assignedParalegalId, setAssignedParalegalId] = React.useState('');
  const [assignedParalegalName, setAssignedParalegalName] = React.useState('');
  const [assignedOutsideCounselId, setAssignedOutsideCounselId] = React.useState('');
  const [assignedOutsideCounselName, setAssignedOutsideCounselName] = React.useState('');

  // -- Follow-on state: Add To Do ---------------------------------------------
  const [todoForm, setTodoForm] = React.useState<ICreateTodoFormState>(EMPTY_TODO_FORM);

  // -- Follow-on state: Send Email --------------------------------------------
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // Reset all state on open
  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(buildEmptyReportCardForm());
      setAssignWorkName('');
      setAssignWorkDescription('');
      setAssignWorkMatterTypeId('');
      setAssignWorkMatterTypeName('');
      setAssignWorkPracticeAreaId('');
      setAssignWorkPracticeAreaName('');
      setAssignWorkPriority(WORK_ASSIGNMENT_PRIORITY.Normal);
      setAssignWorkResponseDueDate('');
      setAssignedAttorneyId('');
      setAssignedAttorneyName('');
      setAssignedParalegalId('');
      setAssignedParalegalName('');
      setAssignedOutsideCounselId('');
      setAssignedOutsideCounselName('');
      setTodoForm(EMPTY_TODO_FORM);
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
  }, [open]);

  // -- Refs for stale-closure prevention in follow-on card renderStep --------
  const assignWorkNameRef = React.useRef(assignWorkName);
  assignWorkNameRef.current = assignWorkName;
  const assignWorkDescriptionRef = React.useRef(assignWorkDescription);
  assignWorkDescriptionRef.current = assignWorkDescription;
  const assignWorkMatterTypeIdRef = React.useRef(assignWorkMatterTypeId);
  assignWorkMatterTypeIdRef.current = assignWorkMatterTypeId;
  const assignWorkMatterTypeNameRef = React.useRef(assignWorkMatterTypeName);
  assignWorkMatterTypeNameRef.current = assignWorkMatterTypeName;
  const assignWorkPracticeAreaIdRef = React.useRef(assignWorkPracticeAreaId);
  assignWorkPracticeAreaIdRef.current = assignWorkPracticeAreaId;
  const assignWorkPracticeAreaNameRef = React.useRef(assignWorkPracticeAreaName);
  assignWorkPracticeAreaNameRef.current = assignWorkPracticeAreaName;
  const assignWorkPriorityRef = React.useRef(assignWorkPriority);
  assignWorkPriorityRef.current = assignWorkPriority;
  const assignWorkResponseDueDateRef = React.useRef(assignWorkResponseDueDate);
  assignWorkResponseDueDateRef.current = assignWorkResponseDueDate;
  const assignedAttorneyIdRef = React.useRef(assignedAttorneyId);
  assignedAttorneyIdRef.current = assignedAttorneyId;
  const assignedAttorneyNameRef = React.useRef(assignedAttorneyName);
  assignedAttorneyNameRef.current = assignedAttorneyName;
  const assignedParalegalIdRef = React.useRef(assignedParalegalId);
  assignedParalegalIdRef.current = assignedParalegalId;
  const assignedParalegalNameRef = React.useRef(assignedParalegalName);
  assignedParalegalNameRef.current = assignedParalegalName;
  const assignedOutsideCounselIdRef = React.useRef(assignedOutsideCounselId);
  assignedOutsideCounselIdRef.current = assignedOutsideCounselId;
  const assignedOutsideCounselNameRef = React.useRef(assignedOutsideCounselName);
  assignedOutsideCounselNameRef.current = assignedOutsideCounselName;
  const todoFormRef = React.useRef(todoForm);
  todoFormRef.current = todoForm;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;

  // -- Search callbacks --------------------------------------------------
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

  // -- Assign Work change handlers --
  const handleMatterTypeChange = React.useCallback((item: ILookupItem | null) => {
    setAssignWorkMatterTypeId(item?.id ?? '');
    setAssignWorkMatterTypeName(item?.name ?? '');
  }, []);
  const handlePracticeAreaChange = React.useCallback((item: ILookupItem | null) => {
    setAssignWorkPracticeAreaId(item?.id ?? '');
    setAssignWorkPracticeAreaName(item?.name ?? '');
  }, []);
  const handleAttorneyChange = React.useCallback((item: ILookupItem | null) => {
    setAssignedAttorneyId(item?.id ?? '');
    setAssignedAttorneyName(item?.name ?? '');
  }, []);
  const handleParalegalChange = React.useCallback((item: ILookupItem | null) => {
    setAssignedParalegalId(item?.id ?? '');
    setAssignedParalegalName(item?.name ?? '');
  }, []);
  const handleOutsideCounselChange = React.useCallback((item: ILookupItem | null) => {
    setAssignedOutsideCounselId(item?.id ?? '');
    setAssignedOutsideCounselName(item?.name ?? '');
  }, []);

  // -- WebApi adapter for CreateRecordWizard ---------------------------------
  const webApiAdapter = React.useMemo(() => buildWebApiAdapter(dataService), [dataService]);

  // -- Custom Next Steps card set: Send Email / Add To Do / Assign Work ------
  // (design.md §5.9, task 040 — mirrors CreateInvoiceWizard's task 030 set.)
  const followOnCards: FollowOnCardConfig[] = React.useMemo(
    () => [
      {
        id: 'send-email',
        label: 'Send Notification Email',
        stepLabel: 'Send Email',
        icon: <MailRegular fontSize={28} />,
        description: 'Compose and queue an introductory email about this report card.',
        canAdvance: () =>
          emailToRef.current.trim() !== '' &&
          emailSubjectRef.current.trim() !== '' &&
          emailBodyRef.current.trim() !== '',
        renderStep: () => (
          <SendEmailFollowOnStep
            emailTo={emailToRef.current}
            onEmailToChange={setEmailTo}
            emailSubject={emailSubjectRef.current}
            onEmailSubjectChange={setEmailSubject}
            emailBody={emailBodyRef.current}
            onEmailBodyChange={setEmailBody}
            onSearchUsers={handleSearchUsers}
            infoNote="This email will be saved as a draft activity on the record. You can review and send it from there."
          />
        ),
      },
      {
        id: 'add-todo',
        label: 'Add To Do',
        stepLabel: 'Add To Do',
        icon: <CheckmarkCircleRegular fontSize={28} />,
        description: 'Create a to do linked to this report card.',
        renderStep: () => (
          <AddTodoFollowOnStep
            dataService={dataService}
            formValues={todoFormRef.current}
            onFormValuesChange={setTodoForm}
            onSearchAssignees={handleSearchContacts}
          />
        ),
      },
      {
        id: 'assign-work',
        label: 'Assign Work',
        stepLabel: 'Assign Work',
        icon: <PersonRegular fontSize={28} />,
        description: 'Create a work assignment with resources linked to this report card.',
        renderStep: () => {
          const mtVal: ILookupItem | null = assignWorkMatterTypeIdRef.current
            ? { id: assignWorkMatterTypeIdRef.current, name: assignWorkMatterTypeNameRef.current }
            : null;
          const paVal: ILookupItem | null = assignWorkPracticeAreaIdRef.current
            ? { id: assignWorkPracticeAreaIdRef.current, name: assignWorkPracticeAreaNameRef.current }
            : null;
          const attVal: ILookupItem | null = assignedAttorneyIdRef.current
            ? { id: assignedAttorneyIdRef.current, name: assignedAttorneyNameRef.current }
            : null;
          const paraVal: ILookupItem | null = assignedParalegalIdRef.current
            ? { id: assignedParalegalIdRef.current, name: assignedParalegalNameRef.current }
            : null;
          const ocVal: ILookupItem | null = assignedOutsideCounselIdRef.current
            ? { id: assignedOutsideCounselIdRef.current, name: assignedOutsideCounselNameRef.current }
            : null;

          return (
            <AssignWorkFollowOnStep
              nameValue={assignWorkNameRef.current}
              onNameChange={setAssignWorkName}
              descriptionValue={assignWorkDescriptionRef.current}
              onDescriptionChange={setAssignWorkDescription}
              matterTypeValue={mtVal}
              onMatterTypeChange={handleMatterTypeChange}
              onSearchMatterTypes={handleSearchMatterTypes}
              practiceAreaValue={paVal}
              onPracticeAreaChange={handlePracticeAreaChange}
              onSearchPracticeAreas={handleSearchPracticeAreas}
              priorityValue={assignWorkPriorityRef.current}
              onPriorityChange={setAssignWorkPriority}
              responseDueDateValue={assignWorkResponseDueDateRef.current}
              onResponseDueDateChange={setAssignWorkResponseDueDate}
              attorneyValue={attVal}
              onAttorneyChange={handleAttorneyChange}
              onSearchAttorneys={handleSearchContacts}
              paralegalValue={paraVal}
              onParalegalChange={handleParalegalChange}
              onSearchParalegals={handleSearchContacts}
              outsideCounselValue={ocVal}
              onOutsideCounselChange={handleOutsideCounselChange}
              onSearchOutsideCounsel={handleSearchOrganizations}
            />
          );
        },
      },
    ],
    [
      dataService,
      handleSearchContacts,
      handleSearchOrganizations,
      handleSearchUsers,
      handleSearchMatterTypes,
      handleSearchPracticeAreas,
      handleMatterTypeChange,
      handlePracticeAreaChange,
      handleAttorneyChange,
      handleParalegalChange,
      handleOutsideCounselChange,
    ]
  );

  // -- Wizard config ---------------------------------------------------------
  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Report Card',
      entityLabel: 'report card',

      // task 040 — see resolveReportCardAssociateToStepConfig doc comment.
      associateToStep: resolveReportCardAssociateToStepConfig(navigationService, initialAssociation, lockAssociation),

      // Owner decision: NO Add Files step for this wizard.
      hideFilesStep: true,

      infoStep: {
        id: 'create-record',
        label: 'Report Card Details',
        canAdvance: () => formValid,
        renderContent: () => (
          <CreateReportCardStep
            dataService={dataService}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            initialFormValues={formValues}
          />
        ),
      },

      // task 040: custom Send Email/Add To Do/Assign Work set — see followOnCards above.
      followOnCards,

      finishingLabel: 'Creating report card…',

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const association = context.association;

        const reportCardService = new ReportCardService(dataService, authFetch ?? fetch.bind(window), bffBaseUrl ?? '');

        const result = await reportCardService.createReportCard(formValuesRef.current, association);

        if (result.status === 'error') {
          throw new Error(result.errorMessage ?? 'Failed to create report card');
        }

        const reportCardId = result.reportCardId!;
        const reportCardName = result.reportCardName!;
        const warnings: string[] = [...result.warnings];

        // -- Follow-on: Assign Work (sprk_workassignment — NOT regarding the
        // report card; see the file-level NOTE above for why) --
        if (context.selectedActions.includes('assign-work') && assignWorkNameRef.current.trim()) {
          try {
            const waForm: ICreateWorkAssignmentFormState = {
              recordType: '',
              recordId: '',
              recordName: '',
              assignWithoutRecord: true,
              name: assignWorkNameRef.current.trim(),
              description: assignWorkDescriptionRef.current.trim(),
              matterTypeId: assignWorkMatterTypeIdRef.current,
              matterTypeName: assignWorkMatterTypeNameRef.current,
              practiceAreaId: assignWorkPracticeAreaIdRef.current,
              practiceAreaName: assignWorkPracticeAreaNameRef.current,
              priority: assignWorkPriorityRef.current,
              responseDueDate: assignWorkResponseDueDateRef.current,
            };
            const waAssignWork: IAssignWorkState = {
              assignedAttorneyId: assignedAttorneyIdRef.current,
              assignedAttorneyName: assignedAttorneyNameRef.current,
              assignedParalegalId: assignedParalegalIdRef.current,
              assignedParalegalName: assignedParalegalNameRef.current,
              assignedLawFirmId: assignedOutsideCounselIdRef.current,
              assignedLawFirmName: assignedOutsideCounselNameRef.current,
              assignedLawFirmAttorneyId: '',
              assignedLawFirmAttorneyName: '',
              notifyResources: false,
            };
            const waService = new WorkAssignmentService(dataService, authFetch ?? fetch.bind(window), bffBaseUrl ?? '');
            const waResult = await waService.createWorkAssignment(waForm, [], [], waAssignWork);
            if (waResult.status === 'error') {
              warnings.push(
                `Work assignment could not be created (${waResult.errorMessage ?? 'Unknown error'}). ` +
                  'You can create it manually from the report card record.'
              );
            } else if (waResult.warnings.length > 0) {
              warnings.push(...waResult.warnings);
            }
          } catch (err) {
            const message = err instanceof Error ? err.message : 'Unknown error';
            warnings.push(
              `Work assignment could not be created (${message}). You can create it manually from the report card record.`
            );
          }
        }

        // -- Follow-on: Add To Do (sprk_todo regarding the new Report Card, NOT the host) --
        if (context.selectedActions.includes('add-todo') && todoFormRef.current.title.trim()) {
          try {
            const todoResult = await createTodoRegardingChild(dataService, todoFormRef.current, {
              entityType: 'sprk_reportcard',
              recordId: reportCardId,
              recordName: reportCardName,
            });
            if (!todoResult.success) {
              warnings.push(
                `To do could not be created (${todoResult.errorMessage ?? 'Unknown error'}). ` +
                  'You can create it manually from the report card record.'
              );
            }
          } catch (err) {
            const message = err instanceof Error ? err.message : 'Unknown error';
            warnings.push(
              `To do could not be created (${message}). You can create it manually from the report card record.`
            );
          }
        }

        // -- Follow-on: Send Email --
        if (context.selectedActions.includes('send-email') && emailToRef.current.trim() && authFetch && bffBaseUrl) {
          const emailService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);
          const emailResult = await emailService.sendEmail({
            to: emailToRef.current,
            subject: emailSubjectRef.current,
            body: emailBodyRef.current,
            associations: [{ entityType: 'sprk_reportcard', entityId: reportCardId, entityName: reportCardName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        const hasWarnings = warnings.length > 0;

        const viewReportCard = () => {
          if (navigationService) {
            navigationService.openRecord('sprk_reportcard', reportCardId);
          }
          onClose();
        };

        return {
          icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
          title: hasWarnings ? 'Report card created with warnings' : 'Report card created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
                &ldquo;{reportCardName}&rdquo;
              </span>{' '}
              has been created
              {hasWarnings
                ? ', though some operations could not complete. See details below.'
                : ' and is ready to use.'}
            </Text>
          ),
          actions: (
            <>
              <Button appearance="primary" onClick={viewReportCard} aria-label={`View report card: ${reportCardName}`}>
                View Report Card
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
    [
      formValid,
      formValues,
      dataService,
      onClose,
      authFetch,
      bffBaseUrl,
      navigationService,
      webApiAdapter,
      initialAssociation,
      lockAssociation,
      followOnCards,
    ]
  );

  // -- Render ----------------------------------------------------------------

  return (
    <CreateRecordWizard open={open} onClose={onClose} webApi={webApiAdapter} config={config} embedded={embedded} />
  );
};

export default CreateReportCardWizard;
export { CreateReportCardWizard };
