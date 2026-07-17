/**
 * CreateRecordWizard.tsx
 * Reusable multi-step wizard for creating Dataverse records.
 *
 * Extracts ~265 LOC of duplicated boilerplate from entity-specific wizards:
 *   - File upload reducer + state
 *   - SPE container resolution
 *   - Reset-on-open
 *   - Follow-on step sync (dynamic add/remove via shellRef)
 *   - Assign Resources / Draft Summary / Send Email step rendering
 *   - Email pre-fill
 *   - Ref-based stale closure prevention
 *
 * Each entity provides only:
 *   - config.infoStep: entity-specific form (canAdvance + renderContent)
 *   - config.onFinish: record creation + success screen
 *   - Search callbacks (contacts, organizations, users)
 *
 * Steps (with optional associateToStep):
 *   [0] Associate To -- optional; only present when config.associateToStep is set
 *   [1] Add file(s)  -- always skip-able (canAdvance: true); OMITTED entirely
 *                       when config.hideFilesStep is true (task 040)
 *   [2] Entity info  -- from config.infoStep
 *   [3] Next Steps   -- follow-on action card selection
 *   [4+] Dynamic     -- Assign Resources, Draft Summary, Send Email
 *
 * Steps (without associateToStep):
 *   [0] Add file(s)  -- always skip-able (canAdvance: true); OMITTED entirely
 *                       when config.hideFilesStep is true (task 040)
 *   [1] Entity info  -- from config.infoStep
 *   [2] Next Steps   -- follow-on action card selection
 *   [3+] Dynamic     -- Assign Resources, Draft Summary, Send Email
 *
 * @see WizardShell -- underlying generic dialog shell
 * @see ADR-012 -- Shared Component Library
 */
import * as React from 'react';
import { MessageBar, MessageBarBody, Text, makeStyles, tokens } from '@fluentui/react-components';
import { PersonRegular, CalendarRegular, MailRegular } from '@fluentui/react-icons';

import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardShellHandle, IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { FileUploadZone } from '../FileUpload/FileUploadZone';
import { UploadedFileList } from '../FileUpload/UploadedFileList';
import type { IUploadedFile, IFileValidationError } from '../FileUpload/fileUploadTypes';
import type { ILookupItem } from '../../types/LookupTypes';

import type { ICreateRecordWizardProps, FollowOnActionId, AssociationResult } from './types';
import { AssociateToStep } from '../AssociateToStep/AssociateToStep';

// Shared, config-driven follow-on module (design.md §5.9). Replaces the local
// FollowOnSteps.tsx (NextStepsStep) + steps/{AssignWorkFollowOnStep,
// CreateEventFollowOnStep, SendEmailStep} copies, deleted per task 021. The card
// grid (FollowOnGrid) drives dynamic step add/remove imperatively at click time,
// so the previous `selectedActions`-diffing sync effect is no longer needed.
import {
  FollowOnGrid,
  AssignWorkFollowOnStep,
  WORK_ASSIGNMENT_PRIORITY,
  CreateEventFollowOnStep,
  SendEmailFollowOnStep,
} from '../WizardFollowOns';
import type { FollowOnCardConfig, WorkAssignmentPriorityValue } from '../WizardFollowOns';

// ---------------------------------------------------------------------------
// File reducer
// ---------------------------------------------------------------------------

interface IFileState {
  uploadedFiles: IUploadedFile[];
  validationErrors: IFileValidationError[];
}

type FileAction =
  | { type: 'ADD_FILES'; files: IUploadedFile[] }
  | { type: 'REMOVE_FILE'; fileId: string }
  | { type: 'SET_VALIDATION_ERRORS'; errors: IFileValidationError[] }
  | { type: 'CLEAR_VALIDATION_ERRORS' }
  | { type: 'RESET' };

function fileReducer(state: IFileState, action: FileAction): IFileState {
  switch (action.type) {
    case 'ADD_FILES': {
      const existing = new Set(state.uploadedFiles.map(f => `${f.name}::${f.sizeBytes}`));
      const newFiles = action.files.filter(f => !existing.has(`${f.name}::${f.sizeBytes}`));
      return {
        ...state,
        uploadedFiles: [...state.uploadedFiles, ...newFiles],
        validationErrors: [],
      };
    }
    case 'REMOVE_FILE':
      return {
        ...state,
        uploadedFiles: state.uploadedFiles.filter(f => f.id !== action.fileId),
      };
    case 'SET_VALIDATION_ERRORS':
      return { ...state, validationErrors: action.errors };
    case 'CLEAR_VALIDATION_ERRORS':
      return { ...state, validationErrors: [] };
    case 'RESET':
      return { uploadedFiles: [], validationErrors: [] };
    default:
      return state;
  }
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  errorBar: {
    flexShrink: 0,
  },
});

// ---------------------------------------------------------------------------
// Empty search callback (for when entity does not provide search)
// ---------------------------------------------------------------------------

const EMPTY_SEARCH = () => Promise.resolve([] as ILookupItem[]);

// ---------------------------------------------------------------------------
// CreateRecordWizard
// ---------------------------------------------------------------------------

export const CreateRecordWizard: React.FC<ICreateRecordWizardProps> = ({ open, onClose, config, embedded }) => {
  const styles = useStyles();
  const shellRef = React.useRef<IWizardShellHandle>(null);

  // -- File state --
  const [fileState, fileDispatch] = React.useReducer(fileReducer, {
    uploadedFiles: [],
    validationErrors: [],
  });

  // -- Association state (AssociateToStep -- optional step 1) --
  // Initialised from config.associateToStep?.initialAssociation when set
  // (smart-todo-decoupling-r3 task 032 / FR-16 — launch-context pre-fill).
  const [association, setAssociation] = React.useState<AssociationResult | null>(
    config.associateToStep?.initialAssociation ?? null
  );

  // -- Follow-on step selections --
  const [selectedActions, setSelectedActions] = React.useState<FollowOnActionId[]>([]);

  // -- Assign Work state (replaces Assign Resources) --
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
  // Track whether Assign Work defaults have been applied (auto-fill from parent)
  const [assignWorkDefaultsApplied, setAssignWorkDefaultsApplied] = React.useState(false);

  // -- Create Event follow-on form state --
  const [createEventName, setCreateEventName] = React.useState('');
  const [createEventTypeId, setCreateEventTypeId] = React.useState('');
  const [createEventTypeName, setCreateEventTypeName] = React.useState('');
  const [createEventDueDate, setCreateEventDueDate] = React.useState('');
  const [createEventPriority, setCreateEventPriority] = React.useState(100000001); // Normal
  const [createEventDescription, setCreateEventDescription] = React.useState('');

  // -- Send Email state --
  const [emailTo, setEmailTo] = React.useState('');
  const [emailSubject, setEmailSubject] = React.useState('');
  const [emailBody, setEmailBody] = React.useState('');

  // -- SPE container ID --
  const [speContainerId, setSpeContainerId] = React.useState('');

  React.useEffect(() => {
    if (open && config.resolveSpeContainerId) {
      config.resolveSpeContainerId().then(id => setSpeContainerId(id));
    }
  }, [open, config]);

  // -- Reset all state on open --
  React.useEffect(() => {
    if (open) {
      fileDispatch({ type: 'RESET' });
      // FR-16: when the wizard opens (or re-opens), seed the association with
      // the launch-context pre-fill if one was provided (null otherwise).
      setAssociation(config.associateToStep?.initialAssociation ?? null);
      setSelectedActions([]);
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
      setAssignWorkDefaultsApplied(false);
      setCreateEventName('');
      setCreateEventTypeId('');
      setCreateEventTypeName('');
      setCreateEventDueDate('');
      setCreateEventPriority(100000001);
      setCreateEventDescription('');
      setEmailTo('');
      setEmailSubject('');
      setEmailBody('');
    }
    // Intentionally depends on `open` only — re-running on every `config` change
    // would clobber in-progress wizard state. The initial-association seed is read
    // from closure at the open→true transition (sufficient for FR-16 launch-context
    // pre-fill where the consumer sets the prop before opening the wizard).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  // -- Refs for stale closure prevention in dynamic step renderContent --
  const associationRef = React.useRef(association);
  associationRef.current = association;
  const createEventNameRef = React.useRef(createEventName);
  createEventNameRef.current = createEventName;
  const createEventTypeIdRef = React.useRef(createEventTypeId);
  createEventTypeIdRef.current = createEventTypeId;
  const createEventTypeNameRef = React.useRef(createEventTypeName);
  createEventTypeNameRef.current = createEventTypeName;
  const createEventDueDateRef = React.useRef(createEventDueDate);
  createEventDueDateRef.current = createEventDueDate;
  const createEventPriorityRef = React.useRef(createEventPriority);
  createEventPriorityRef.current = createEventPriority;
  const createEventDescriptionRef = React.useRef(createEventDescription);
  createEventDescriptionRef.current = createEventDescription;
  const emailToRef = React.useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = React.useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = React.useRef(emailBody);
  emailBodyRef.current = emailBody;
  const selectedActionsRef = React.useRef(selectedActions);
  selectedActionsRef.current = selectedActions;
  const fileStateRef = React.useRef(fileState);
  fileStateRef.current = fileState;
  const speContainerIdRef = React.useRef(speContainerId);
  speContainerIdRef.current = speContainerId;

  // Assign Work refs
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

  // -- Apply Assign Work defaults on first 'assign-counsel' selection --
  // Previously inline in the dynamic-step sync effect. FollowOnGrid now owns the
  // add/remove of dynamic steps, so this is the only selection-driven side effect
  // that remains: seed Matter Type / Practice Area from the parent entity form the
  // first time the Assign Work card is selected (identical behavior to before).
  React.useEffect(() => {
    if (selectedActions.includes('assign-counsel') && !assignWorkDefaultsApplied && config.getAssignWorkDefaults) {
      const defaults = config.getAssignWorkDefaults();
      if (defaults.assignWorkMatterTypeId) {
        setAssignWorkMatterTypeId(defaults.assignWorkMatterTypeId);
        setAssignWorkMatterTypeName(defaults.assignWorkMatterTypeName ?? '');
      }
      if (defaults.assignWorkPracticeAreaId) {
        setAssignWorkPracticeAreaId(defaults.assignWorkPracticeAreaId);
        setAssignWorkPracticeAreaName(defaults.assignWorkPracticeAreaName ?? '');
      }
      setAssignWorkDefaultsApplied(true);
    }
  }, [selectedActions, assignWorkDefaultsApplied, config]);

  // -- Follow-on card set (declared as config for the shared FollowOnGrid) --
  // Card ids ('assign-counsel' / 'create-event' / 'send-email') are unchanged so
  // the derived dynamic step ids ('followon-*'), the canonical sidebar order, and
  // `IFinishContext.selectedActions` values reaching the Event/Project wrappers all
  // stay byte-identical to the pre-consolidation behavior. Each `renderStep` reads
  // the wizard's own refs so its form state survives step navigation exactly as it
  // did when these bodies lived in the sync effect above.
  const followOnCards: FollowOnCardConfig[] = React.useMemo(() => {
    const searchContacts = config.searchContacts ?? EMPTY_SEARCH;
    const searchOrganizations = config.searchOrganizations ?? EMPTY_SEARCH;
    const searchUsers = config.searchUsers ?? EMPTY_SEARCH;
    const searchMatterTypes = config.searchMatterTypes ?? EMPTY_SEARCH;
    const searchPracticeAreas = config.searchPracticeAreas ?? EMPTY_SEARCH;

    return [
      {
        id: 'assign-counsel',
        label: 'Assign Work',
        stepLabel: 'Assign Work',
        icon: <PersonRegular fontSize={28} />,
        description: 'Create a work assignment with resources linked to this record.',
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
              onSearchMatterTypes={searchMatterTypes}
              practiceAreaValue={paVal}
              onPracticeAreaChange={handlePracticeAreaChange}
              onSearchPracticeAreas={searchPracticeAreas}
              priorityValue={assignWorkPriorityRef.current}
              onPriorityChange={setAssignWorkPriority}
              responseDueDateValue={assignWorkResponseDueDateRef.current}
              onResponseDueDateChange={setAssignWorkResponseDueDate}
              attorneyValue={attVal}
              onAttorneyChange={handleAttorneyChange}
              onSearchAttorneys={searchContacts}
              onAssignAttorneyToMe={config.resolveCurrentUserAssignee}
              paralegalValue={paraVal}
              onParalegalChange={handleParalegalChange}
              onSearchParalegals={searchContacts}
              outsideCounselValue={ocVal}
              onOutsideCounselChange={handleOutsideCounselChange}
              onSearchOutsideCounsel={searchOrganizations}
            />
          );
        },
      },
      {
        id: 'create-event',
        label: 'Create Event',
        stepLabel: 'Create Event',
        icon: <CalendarRegular fontSize={28} />,
        description: 'Create an event linked to this record.',
        renderStep: () => {
          if (!config.eventDataService) {
            // eventDataService not configured — show informational text
            return (
              <Text size={300} style={{ color: 'inherit' }}>
                Event creation is not configured for this wizard.
              </Text>
            );
          }
          const currentFormValues = {
            eventName: createEventNameRef.current,
            eventTypeId: createEventTypeIdRef.current,
            eventTypeName: createEventTypeNameRef.current,
            dueDate: createEventDueDateRef.current,
            priority: createEventPriorityRef.current,
            description: createEventDescriptionRef.current,
            regardingRecordId: '',
            regardingRecordName: '',
            assignedToId: '',
            assignedToName: '',
          };
          return (
            <CreateEventFollowOnStep
              dataService={config.eventDataService}
              formValues={currentFormValues}
              onFormValues={vals => {
                setCreateEventName(vals.eventName);
                setCreateEventTypeId(vals.eventTypeId);
                setCreateEventTypeName(vals.eventTypeName);
                setCreateEventDueDate(vals.dueDate);
                setCreateEventPriority(vals.priority);
                setCreateEventDescription(vals.description);
              }}
              onValidChange={() => {
                /* canAdvance is always true for follow-on */
              }}
            />
          );
        },
      },
      {
        id: 'send-email',
        label: 'Send Notification Email',
        // Sidebar step label stays 'Send Email' (was FOLLOW_ON_STEP_LABEL_MAP)
        // even though the card reads 'Send Notification Email'.
        stepLabel: 'Send Email',
        icon: <MailRegular fontSize={28} />,
        description: 'Compose and queue an introductory email to the client.',
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
            onSearchUsers={searchUsers}
            infoNote="This email will be saved as a draft activity on the record. You can review and send it from there."
          />
        ),
      },
    ];
  }, [
    config,
    handleMatterTypeChange,
    handlePracticeAreaChange,
    handleAttorneyChange,
    handleParalegalChange,
    handleOutsideCounselChange,
  ]);

  // -- Email pre-fill when send-email is selected --
  React.useEffect(() => {
    if (selectedActions.includes('send-email') && !emailSubject) {
      const entityName = config.getEntityName?.() ?? '';
      if (entityName) {
        const subject = config.buildEmailSubject ? config.buildEmailSubject(entityName) : `New Record: ${entityName}`;
        setEmailSubject(subject);

        const fields = config.getFormFields?.() ?? {};
        const body = config.buildEmailBody
          ? config.buildEmailBody(fields)
          : `Dear Client,\n\nA new record "${entityName}" has been created.\n\nKind regards`;
        setEmailBody(body);
      }
    }
  }, [selectedActions, emailSubject, config]);

  // -- File handler callbacks --
  const handleFilesAccepted = React.useCallback(
    (files: IUploadedFile[]) => fileDispatch({ type: 'ADD_FILES', files }),
    []
  );
  const handleValidationErrors = React.useCallback(
    (errors: IFileValidationError[]) => fileDispatch({ type: 'SET_VALIDATION_ERRORS', errors }),
    []
  );
  const handleRemoveFile = React.useCallback((fileId: string) => fileDispatch({ type: 'REMOVE_FILE', fileId }), []);
  const handleClearErrors = React.useCallback(() => fileDispatch({ type: 'CLEAR_VALIDATION_ERRORS' }), []);

  // -- Finish handler --
  const handleFinish = React.useCallback(async (): Promise<IWizardSuccessConfig> => {
    return config.onFinish({
      uploadedFiles: fileStateRef.current.uploadedFiles,
      speContainerId: speContainerIdRef.current,
      selectedActions: selectedActionsRef.current,
      association: associationRef.current,
      followOn: {
        // Assign Work fields
        assignWorkName: assignWorkNameRef.current,
        assignWorkDescription: assignWorkDescriptionRef.current,
        assignWorkMatterTypeId: assignWorkMatterTypeIdRef.current,
        assignWorkMatterTypeName: assignWorkMatterTypeNameRef.current,
        assignWorkPracticeAreaId: assignWorkPracticeAreaIdRef.current,
        assignWorkPracticeAreaName: assignWorkPracticeAreaNameRef.current,
        assignWorkPriority: assignWorkPriorityRef.current,
        assignWorkResponseDueDate: assignWorkResponseDueDateRef.current,
        assignedAttorneyId: assignedAttorneyIdRef.current,
        assignedAttorneyName: assignedAttorneyNameRef.current,
        assignedParalegalId: assignedParalegalIdRef.current,
        assignedParalegalName: assignedParalegalNameRef.current,
        assignedOutsideCounselId: assignedOutsideCounselIdRef.current,
        assignedOutsideCounselName: assignedOutsideCounselNameRef.current,
        // Create Event fields (form values — actual creation in onFinish)
        createEventName: createEventNameRef.current,
        createEventTypeId: createEventTypeIdRef.current,
        createEventTypeName: createEventTypeNameRef.current,
        createEventDueDate: createEventDueDateRef.current,
        createEventPriority: createEventPriorityRef.current,
        createEventDescription: createEventDescriptionRef.current,
        // Send Email fields
        emailTo: emailToRef.current,
        emailSubject: emailSubjectRef.current,
        emailBody: emailBodyRef.current,
      },
    });
  }, [config]);

  // -- Step configurations --
  const filesStepSubtitle = config.filesStepSubtitle ?? 'Upload documents for AI analysis, or click Next to skip.';

  const stepConfigs: IWizardStepConfig[] = React.useMemo(() => {
    const addFilesStep: IWizardStepConfig = {
      id: 'add-files',
      label: 'Add file(s)',
      // Grounding-optional (spaarkeai-assistant-enhancements-r1 task 014 / P6 /
      // FR-A5, ADR-039): a create flow MUST NOT require an attached document or
      // session content. `canAdvance` was previously gated on
      // `uploadedFiles.length > 0`, contradicting this file's own header
      // comment ("always skip-able (canAdvance: true)") and this step's
      // `isSkippable: true` -- Next was disabled with no file even though Skip
      // always bypassed the same requirement. Always-true removes that
      // confusing dead-end and makes "no file needed" true for both buttons.
      canAdvance: () => true,
      isSkippable: true,
      renderContent: () => (
        <>
          <div>
            <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
              Add file(s)
            </Text>
            <Text size={200} className={styles.stepSubtitle}>
              {filesStepSubtitle}
            </Text>
          </div>

          {fileState.validationErrors.length > 0 && (
            <MessageBar intent="error" className={styles.errorBar} onMouseEnter={handleClearErrors}>
              <MessageBarBody>
                {fileState.validationErrors.map((err, i) => (
                  <div key={i}>
                    <strong>{err.fileName}</strong>: {err.reason}
                  </div>
                ))}
              </MessageBarBody>
            </MessageBar>
          )}

          <FileUploadZone onFilesAccepted={handleFilesAccepted} onValidationErrors={handleValidationErrors} />

          {fileState.uploadedFiles.length > 0 && (
            <UploadedFileList files={fileState.uploadedFiles} onRemove={handleRemoveFile} />
          )}
        </>
      ),
    };

    const infoStepConfig: IWizardStepConfig = {
      id: config.infoStep.id,
      label: config.infoStep.label,
      canAdvance: config.infoStep.canAdvance,
      renderContent: () => config.infoStep.renderContent(fileStateRef.current.uploadedFiles),
    };

    const nextStepsConfig: IWizardStepConfig = {
      id: 'next-steps',
      label: 'Next Steps',
      canAdvance: () => true,
      isEarlyFinish: () => selectedActions.length === 0,
      renderContent: handle => (
        <FollowOnGrid
          // task 030: a wizard may override the built-in Assign Work/Create
          // Event/Send Email card set via config.followOnCards (e.g.
          // CreateInvoiceWizard's Send Email/Add To Do/Assign Work set). See
          // ICreateRecordWizardConfig.followOnCards doc comment.
          cards={config.followOnCards ?? followOnCards}
          selected={selectedActions}
          onSelectionChange={sel => setSelectedActions(sel as FollowOnActionId[])}
          addDynamicStep={(cfg, order) => handle.addDynamicStep(cfg, order)}
          removeDynamicStep={stepId => handle.removeDynamicStep(stepId)}
          title="Next steps"
          subtitle={
            `Optionally select follow-on actions to complete after the ${config.entityLabel} is created. ` +
            `You can skip all and handle these from the ${config.entityLabel} record.`
          }
          emptyHint={`No actions selected — click Finish to create the ${config.entityLabel} without follow-on steps.`}
        />
      ),
    };

    // task 040: `config.hideFilesStep` omits "Add file(s)" entirely (not
    // merely skippable) — see ICreateRecordWizardConfig.hideFilesStep doc
    // comment. Every other consumer leaves this undefined, so `filesSteps`
    // is `[addFilesStep]` exactly as before.
    const filesSteps: IWizardStepConfig[] = config.hideFilesStep ? [] : [addFilesStep];

    if (config.associateToStep) {
      const { entityTypes, navigationService, lockAssociation } = config.associateToStep;

      // Locked mode (design §5.5): the parent is fixed (e.g. launched from a
      // Visual Host visual). Hide the Associate-To step entirely — the seeded
      // `initialAssociation` still reaches onFinish through `associationRef`
      // (set by the reset-on-open effect above), so the created child is
      // regarding the host record without the user ever seeing the picker.
      if (lockAssociation) {
        return [...filesSteps, infoStepConfig, nextStepsConfig];
      }

      const associateStepConfig: IWizardStepConfig = {
        id: 'associate-to',
        label: 'Associate To',
        canAdvance: () => Boolean(associationRef.current), // requires a record selection; Skip button bypasses this
        isSkippable: true,
        renderContent: handle => (
          <AssociateToStep
            entityTypes={entityTypes}
            navigationService={navigationService}
            value={associationRef.current}
            onChange={setAssociation}
            onSkip={() => {
              setAssociation(null);
              handle.nextStep();
            }}
          />
        ),
      };
      return [associateStepConfig, ...filesSteps, infoStepConfig, nextStepsConfig];
    }

    return [...filesSteps, infoStepConfig, nextStepsConfig];
  }, [
    fileState.uploadedFiles,
    fileState.validationErrors,
    selectedActions,
    config,
    styles,
    filesStepSubtitle,
    followOnCards,
    handleFilesAccepted,
    handleValidationErrors,
    handleRemoveFile,
    handleClearErrors,
  ]);

  // -- Render --
  return (
    <WizardShell
      ref={shellRef}
      open={open}
      embedded={embedded}
      hideTitle={embedded}
      title={config.title}
      ariaLabel={config.title}
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishingLabel={config.finishingLabel ?? 'Creating\u2026'}
      finishLabel="Finish"
    />
  );
};

export default CreateRecordWizard;
