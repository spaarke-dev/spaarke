/**
 * @spaarke/ai-widgets — CreateAnalysisWizardWidget
 *
 * Per-type Analysis creation wizard (ai-advanced-capabilities-analysis-hub-r1
 * task 040 / spec FR-12). Renders a 3-step guided create flow as an embedded
 * workspace tab (no modal overlay), mirroring the established
 * `CreateMatterWizardWidget` embedding pattern:
 *
 *   Step 1 "Add file(s)"  — upload OR select an existing Document
 *     (`CreateRecordWizard`'s built-in file step, extended with
 *     `config.existingRecordPicker` — task 040's additive extension).
 *   Step 2 "Analysis Details" — access (owner) + associate-to (regarding
 *     lookup, rendered directly via the shared `AssociateToStep`) + name +
 *     description, all in ONE step (per spec FR-12's 3-step framing).
 *   Step 3 "Next Steps" — Send Email / Create To Do, via
 *     `config.followOnCards` (mirrors the CreateInvoiceWizard "custom next-
 *     steps card set" pattern already shipped in this shared lib).
 *
 * On Finish:
 *   1. Resolves the Step-1 document — either the shared `EntityCreationService`
 *      upload path (`uploadFilesToSpe` → `PUT /api/obo/containers/{id}/files/{path}`
 *      → `createDocumentRecords` → durable `sprk_document` + Document Profile), the
 *      SAME proven path every Create*Wizard uses (WORKSPACE-ENTITY-CREATION-GUIDE.md),
 *      OR the Step-1 existing-record pick.
 *   2. Creates the `sprk_analysis` record: name / description / work-type /
 *      access (owner) / the ADR-024 regarding resolver fields (via
 *      `applyResolverFields`) / `sprk_documentid` (ADR-007 hop — NEVER an SPE
 *      pointer). "Associate-to" inheritance is Field-Mapping-driven: before
 *      create, `applyFieldMappings(parent → sprk_analysis)` lets a configured
 *      profile enrich the payload (graceful no-op when no profile exists).
 *   3. Marks the Analysis `InProgress` ("run the analysis" — see the
 *      "Run analysis" scoping note below) and dispatches the existing
 *      `document-viewer` workspace widget (`widget_load`) to load the file
 *      to a workspace tab.
 *   4. Applies selected next-steps: "Create To Do" reuses
 *      `createTodoRegardingChild` (already Field-Mapping-driven internally —
 *      `TodoService.createTodo` calls `applyFieldMappings`, task 021); "Send
 *      Email" applies `applyFieldMappings(sprk_analysis → sprk_communication)`
 *      to let a configured profile supply subject/body, falling back to the
 *      user-entered values when no profile is configured.
 *
 * Scoping note — "run the analysis" (deviation, documented per task-execute
 * Step 8): this task's declared dependencies are 011 (client type) and 012
 * (regarding resolver) ONLY — the session↔Analysis binding + AI execution
 * plumbing (tasks 020–025, two-tier session model, fork-on-analysis) is
 * built by OTHER tasks in this project and is out of this task's scope. This
 * widget marks `sprk_analysisstatus = InProgress` and opens the source file
 * in a workspace tab (both real, verifiable outcomes using only existing,
 * already-registered surfaces) rather than inventing a new session-start
 * contract. See `projects/ai-advanced-capabilities-analysis-hub-r1/notes/
 * task-040-deviations.md`.
 *
 * REGISTRATION OWNERSHIP: per task 040's parallel-execution constraints, task
 * 030 (Analysis hub widget) owns `register-workspace-widgets.ts` and runs
 * AFTER this task. This file is therefore NOT registered here — it is
 * exported as a plain component (default + named) for the hub (030) and the
 * entry-routing task (050) to register/consume.
 *
 * React 19, NOT PCF-safe.
 *
 * @see CreateRecordWizard        — upstream wizard shell (@spaarke/ui-components)
 * @see AssociateToStep           — regarding-lookup UI (rendered inline in Step 2)
 * @see FieldMappingService       — applyFieldMappings (associate-to + next-steps)
 * @see PolymorphicResolverService — applyResolverFields (ADR-024)
 * @see ADR-007  — sprk_documentid → sprk_document SPE hop; no SPE pointers on sprk_analysis
 * @see ADR-012  — Shared component library (reuse, not fork)
 * @see ADR-021  — Fluent UI v9, no hard-coded colors
 * @see ADR-024  — Polymorphic Resolver Pattern — exactly one regarding field
 */

import React, { useCallback, useMemo, useRef, useState } from 'react';
import {
  Button,
  Field,
  Input,
  MessageBar,
  MessageBarBody,
  Spinner,
  Text,
  Textarea,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { CalendarCheckmarkRegular, CheckmarkCircleFilled, MailRegular } from '@fluentui/react-icons';

// NOTE: barrel imports only (not deep `@spaarke/ui-components/components/...`
// paths) — this package's jest config maps only the exact `@spaarke/
// ui-components` specifier to source (source-mode testing until dist is
// built in CI); deep subpaths do not resolve under that mapping. The barrel
// re-exports every symbol used below (components/index.ts).
import {
  ANALYSIS_REGARDING_TARGETS,
  AddTodoFollowOnStep,
  AssociateToStep,
  CreateRecordWizard,
  EMPTY_TODO_FORM,
  EntityCreationService,
  LookupField,
  SendEmailFollowOnStep,
  SprkAnalysisStatus,
  SprkAnalysisWorkType,
  applyFieldMappings,
  applyResolverFields,
  createTodoRegardingChild,
  discoverNavProps,
} from '@spaarke/ui-components';
import type {
  AssociationResult,
  EntityTypeOption,
  FollowOnCardConfig,
  ICreateRecordWizardConfig,
  ICreateTodoFormState,
  IDataService,
  IFinishContext,
  ILookupItem,
  INavigationService,
  IWizardSuccessConfig,
} from '@spaarke/ui-components';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { WidgetState } from '../../types/shared';
import { useDispatchPaneEvent } from '../../events/useDispatchPaneEvent';

// ---------------------------------------------------------------------------
// Analysis-scoped regarding targets (ADR-024) — Matter / Project / Document
// ---------------------------------------------------------------------------
// DERIVED from the canonical single-source `ANALYSIS_REGARDING_TARGETS`
// (`@spaarke/ui-components`, hoisted 2026-07-29 P2). No local catalog restatement —
// the shape adapters below just project the shared catalog into the two lookup
// maps + the AssociateToStep option array this wizard consumes.

const ANALYSIS_REGARDING_ENTITY_TYPES: readonly EntityTypeOption[] = ANALYSIS_REGARDING_TARGETS.map(t => ({
  label: t.label,
  entityType: t.entityType,
}));

const REGARDING_ENTITY_SET_BY_TYPE: Record<string, string> = Object.fromEntries(
  ANALYSIS_REGARDING_TARGETS.map(t => [t.entityType, t.entitySet])
);

const REGARDING_NAVPROP_HINT_BY_TYPE: Record<string, string> = Object.fromEntries(
  ANALYSIS_REGARDING_TARGETS.map(t => [t.entityType, t.navPropHint])
);

// ---------------------------------------------------------------------------
// Data payload shape
// ---------------------------------------------------------------------------

/**
 * Data delivered to this widget via the workspace SSE event or on mount.
 *
 * `workTypeValue`/`workTypeLabel` are deliberately RAW (Choice integer +
 * display string), not the semantic `AnalysisWorkTypeId` union from
 * `src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts` — that type lives in
 * the SpaarkeAi SOLUTION, which depends on this shared-lib package, not the
 * reverse (ADR-012). The caller (hub/entry, task 030/050, both living in
 * SpaarkeAi) resolves the semantic id to its numeric value before passing it
 * in. Defaults to Agreement Review (100000000) — the only LIVE work type
 * this project ships (Legal Research / Patent Application are hub-owned
 * "coming soon" cards, task 030 — they never reach this widget).
 */
export interface CreateAnalysisWizardData {
  /** Stable identifier for this wizard instance (PaneEventBus routing). */
  wizardId?: string;
  /** BFF API base URL. */
  bffBaseUrl?: string;
  /** Authenticated fetch injected by the shell (wraps MSAL token acquisition). */
  authenticatedFetch?: (input: RequestInfo, init?: RequestInit) => Promise<Response>;
  /** Dataverse data access (host-context create/read — Data Access Decision Criteria). */
  dataService?: IDataService;
  /** Navigation service (regarding + existing-document lookups). */
  navigationService?: INavigationService;
  /** Optional SPE container resolver (unused by this wizard's file step, kept for parity). */
  resolveSpeContainerId?: () => Promise<string>;
  /** `sprk_worktype` Choice integer value. Defaults to 100000000 (Agreement Review). */
  workTypeValue?: number;
  /** Display label for the work type (used in the wizard title). Defaults to "Agreement Review". */
  workTypeLabel?: string;
  /** Search callback for the Access (owner) lookup — searches systemuser. */
  searchUsers?: (query: string) => Promise<ILookupItem[]>;
  /** Search callback for the To Do Assignee lookup — searches contact/systemuser per host convention. */
  searchAssignees?: (query: string) => Promise<ILookupItem[]>;
  /**
   * Optional launch-context pre-fill (FR-14 2b: record→wizard launch). When
   * supplied, the regarding lookup starts pre-selected to this record; the
   * user may still change or clear it.
   */
  initialAssociation?: AssociationResult;
  /** Optional session id for correlation on the dispatched document-viewer event. */
  sessionId?: string;
  /**
   * ai-advanced-capabilities-analysis-hub-r1 (tabbed Quick Start): render mode.
   * `true` (default) = full-page embedded tab (the original task-040 behavior);
   * `false` = the wizard renders as its OWN Fluent Dialog (a MODAL) via
   * `CreateRecordWizard embedded={false}`. WorkspacePane sets `false` when it
   * hosts the wizard from the Quick Start "Agreement Review" card so the wizard
   * does NOT occupy a workspace tab — on finish it opens its result tab as usual.
   */
  embedded?: boolean;
  /**
   * ai-advanced-capabilities-analysis-hub-r1: called when the wizard closes in
   * MODAL mode (`embedded === false`) so the host can UNMOUNT it. No-op / unused
   * in embedded/tab mode (the tab persists its own completion panel). Provided by
   * WorkspacePane's Create Analysis modal host.
   */
  onRequestClose?: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
    boxSizing: 'border-box',
  },
  wizardContainer: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
    overflow: 'hidden',
  },
  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground3,
  },
  detailsStep: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalS,
  },
  associateToWrap: {
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const EMPTY_SEARCH = () => Promise.resolve([] as ILookupItem[]);

/** Adapt IDataService to the webApi shape CreateRecordWizard/PolymorphicResolverService expect. */
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
// Component
// ---------------------------------------------------------------------------

const CreateAnalysisWizardWidget: React.FC<WorkspaceWidgetProps<CreateAnalysisWizardData>> = ({
  data,
  isLoading,
  error,
  className,
}) => {
  const styles = useStyles();
  const dispatch = useDispatchPaneEvent();

  // ── Step 2 form state (name / description / access / associate-to) ──────
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [owner, setOwner] = useState<ILookupItem | null>(null);
  const [association, setAssociation] = useState<AssociationResult | null>(data?.initialAssociation ?? null);

  const nameRef = useRef(name);
  nameRef.current = name;
  const descriptionRef = useRef(description);
  descriptionRef.current = description;
  const ownerRef = useRef(owner);
  ownerRef.current = owner;
  const associationRef = useRef(association);
  associationRef.current = association;

  // ── Next-steps state (Send Email / Create To Do) ────────────────────────
  const [emailTo, setEmailTo] = useState('');
  const [emailSubject, setEmailSubject] = useState('');
  const [emailBody, setEmailBody] = useState('');
  const [todoForm, setTodoForm] = useState<ICreateTodoFormState>(EMPTY_TODO_FORM);

  const emailToRef = useRef(emailTo);
  emailToRef.current = emailTo;
  const emailSubjectRef = useRef(emailSubject);
  emailSubjectRef.current = emailSubject;
  const emailBodyRef = useRef(emailBody);
  emailBodyRef.current = emailBody;
  const todoFormRef = useRef(todoForm);
  todoFormRef.current = todoForm;

  // ── Completion state ─────────────────────────────────────────────────────
  const [isOpen, setIsOpen] = useState(true);
  const [isCompleted, setIsCompleted] = useState(false);
  const [completedName, setCompletedName] = useState('');

  const embedded = data?.embedded ?? true;
  const onRequestClose = data?.onRequestClose;

  const handleClose = useCallback(() => {
    setIsOpen(false);
    setIsCompleted(true);
    // Modal mode: ask the host (WorkspacePane) to unmount us. No-op in tab mode
    // (onRequestClose is only supplied by the modal host).
    onRequestClose?.();
  }, [onRequestClose]);

  // ── Derived config (search callbacks, webApi adapter) ───────────────────
  const dataService = data?.dataService;
  const authFetch = data?.authenticatedFetch;
  const bffBaseUrl = data?.bffBaseUrl;
  const navigationService = data?.navigationService;
  const workTypeValue = data?.workTypeValue ?? SprkAnalysisWorkType.AgreementAnalysis; // Agreement Review
  const workTypeLabel = data?.workTypeLabel ?? 'Agreement Review';

  const webApiAdapter = useMemo(() => (dataService ? buildWebApiAdapter(dataService) : null), [dataService]);
  const searchUsers = data?.searchUsers ?? EMPTY_SEARCH;
  const searchAssignees = data?.searchAssignees ?? data?.searchUsers ?? EMPTY_SEARCH;

  const handleSearchAssignees = useCallback((query: string) => searchAssignees(query), [searchAssignees]);

  // ── Next-steps card set: Send Email / Create To Do (spec FR-12) ─────────
  const followOnCards: FollowOnCardConfig[] = useMemo(
    () => [
      {
        id: 'send-email',
        label: 'Send Notification Email',
        stepLabel: 'Send Email',
        icon: <MailRegular fontSize={28} />,
        description: 'Compose and queue an introductory email about this analysis.',
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
            infoNote="This email is queued once the analysis is created. Associate-to inheritance and any configured Field Mapping profile may pre-fill the subject/body."
          />
        ),
      },
      {
        id: 'add-todo',
        label: 'Create To Do',
        stepLabel: 'Create To Do',
        icon: <CalendarCheckmarkRegular fontSize={28} />,
        description: 'Create a to do linked to this analysis.',
        renderStep: () =>
          dataService ? (
            <AddTodoFollowOnStep
              dataService={dataService}
              formValues={todoFormRef.current}
              onFormValuesChange={setTodoForm}
              onSearchAssignees={handleSearchAssignees}
            />
          ) : (
            <Text size={300}>To Do creation is not configured for this wizard.</Text>
          ),
      },
    ],
    [dataService, searchUsers, handleSearchAssignees]
  );

  // ── Wizard config ────────────────────────────────────────────────────────
  const config: ICreateRecordWizardConfig | null = useMemo(() => {
    if (!dataService || !webApiAdapter) return null;

    return {
      title: `Create ${workTypeLabel} Analysis`,
      entityLabel: 'analysis',
      filesStepSubtitle: 'Upload the document to analyze, or select an existing one.',

      // Step 1 extension (task 040): "select existing Document" alongside upload.
      existingRecordPicker: navigationService
        ? {
            navigationService,
            entityType: 'sprk_document',
            entityLabel: 'Document',
          }
        : undefined,

      // Step 2: name + description + access + associate-to, all in ONE step
      // (spec FR-12's 3-step framing — NOT CreateRecordWizard's built-in
      // separate associateToStep, which would add a 4th step before the
      // files step).
      infoStep: {
        id: 'analysis-details',
        label: 'Analysis Details',
        canAdvance: () => nameRef.current.trim() !== '',
        renderContent: () => (
          <div className={styles.detailsStep}>
            <div>
              <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
                Analysis Details
              </Text>
              <Text size={200} className={styles.stepSubtitle}>
                Set the name, description, access, and the record this analysis is regarding.
              </Text>
            </div>

            <Field label="Name" required>
              <Input
                value={name}
                onChange={(_, v) => setName(v.value)}
                placeholder="e.g. MSA Review — Acme Corp"
                aria-label="Analysis name"
              />
            </Field>

            <Field label="Description">
              <Textarea
                value={description}
                onChange={(_, v) => setDescription(v.value)}
                placeholder="Optional description"
                rows={3}
                aria-label="Analysis description"
              />
            </Field>

            <LookupField
              label="Access"
              value={owner}
              onChange={setOwner}
              onSearch={searchUsers}
              placeholder="Search for a person…"
            />

            {navigationService && (
              <div className={styles.associateToWrap}>
                <AssociateToStep
                  entityTypes={ANALYSIS_REGARDING_ENTITY_TYPES as EntityTypeOption[]}
                  navigationService={navigationService}
                  value={association}
                  onChange={setAssociation}
                  variant="compact"
                />
              </div>
            )}
          </div>
        ),
      },

      followOnCards,

      resolveSpeContainerId: data?.resolveSpeContainerId,

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const finishName = nameRef.current.trim();
        if (!finishName) {
          throw new Error('Please enter a name for the analysis.');
        }

        // -- Step 1: resolve the document (upload OR selected-existing) -----
        let documentId: string | null = null;
        let documentName = finishName;

        if (context.selectedExistingRecord) {
          documentId = context.selectedExistingRecord.recordId;
          documentName = context.selectedExistingRecord.recordName;
        } else if (context.uploadedFiles.length > 0) {
          if (!authFetch || !bffBaseUrl || !webApiAdapter) {
            throw new Error('Document upload is not available right now. Please try again shortly.');
          }
          if (!context.speContainerId) {
            throw new Error(
              'No storage container is configured for your business unit — the document could not be uploaded.'
            );
          }

          // Reuse the shared EntityCreationService — the SAME proven upload path every other
          // Create*Wizard uses (Matter/Project/Event/Invoice/WorkAssignment). See
          // docs/guides/WORKSPACE-ENTITY-CREATION-GUIDE.md §Step 4:
          //   uploadFilesToSpe  → PUT /api/obo/containers/{id}/files/{path}  (real endpoint)
          //   createDocumentRecords → durable sprk_document + Document Profile analysis (auto)
          // This REPLACES a bespoke POST /api/documents/upload call that targeted a NON-EXISTENT
          // BFF endpoint (§11 reuse violation caught in the 2026-07-29 duplicate-component audit).
          const entityService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);

          const uploadResult = await entityService.uploadFilesToSpe(context.speContainerId, context.uploadedFiles);
          if (uploadResult.uploadedFiles.length === 0) {
            const firstErr = uploadResult.errors[0]?.error ?? 'unknown error';
            throw new Error(`Document upload failed: ${firstErr}`);
          }

          // Create a STANDALONE sprk_document (empty navigationProperty → createDocumentRecords
          // skips the parent @odata.bind). The Analysis is the SUBJECT-owner via its
          // sprk_documentid lookup (ADR-007), not a parent of the document — so no child binding.
          const docResult = await entityService.createDocumentRecords('', '', '', uploadResult.uploadedFiles, {
            containerId: context.speContainerId,
          });
          documentId = docResult.createdDocumentIds[0] ?? null;
          if (!documentId) {
            const firstWarn = docResult.warnings[0] ?? 'the document record could not be created';
            throw new Error(`Document upload succeeded but ${firstWarn}.`);
          }
          documentName = context.uploadedFiles[0]?.name ?? finishName;
        }

        if (!documentId) {
          throw new Error('Please attach a document or select an existing one before finishing.');
        }

        // -- Build the sprk_analysis create payload --------------------------
        const payload: Record<string, unknown> = {
          sprk_name: finishName,
          sprk_description: descriptionRef.current.trim() || null,
          sprk_worktype: workTypeValue,
          // "Run the analysis" (scoped per this widget's header doc comment):
          // mark InProgress — the session/AI-execution binding is owned by
          // other tasks in this project (020–025).
          sprk_analysisstatus: SprkAnalysisStatus.InProgress,
          'sprk_documentid@odata.bind': `/sprk_documents(${documentId})`,
        };

        if (ownerRef.current?.id) {
          payload['ownerid@odata.bind'] = `/systemusers(${ownerRef.current.id})`;
        }

        const warnings: string[] = [];

        // -- Associate-to: ADR-024 resolver fields + Field-Mapping inheritance --
        const finishAssociation = associationRef.current;
        if (finishAssociation) {
          const entitySet = REGARDING_ENTITY_SET_BY_TYPE[finishAssociation.entityType];
          const navPropHint = REGARDING_NAVPROP_HINT_BY_TYPE[finishAssociation.entityType];
          if (entitySet) {
            try {
              const navProps = await discoverNavProps('sprk_analysis');
              await applyResolverFields(
                webApiAdapter,
                payload,
                navProps,
                finishAssociation.entityType,
                entitySet,
                finishAssociation.recordId,
                finishAssociation.recordName,
                navPropHint
              );
            } catch (err) {
              warnings.push(
                `Could not link the regarding record (${err instanceof Error ? err.message : 'unknown error'}).`
              );
            }
          }

          if (authFetch && bffBaseUrl) {
            const mappingResult = await applyFieldMappings({
              sourceEntity: finishAssociation.entityType,
              sourceId: finishAssociation.recordId,
              targetEntity: 'sprk_analysis',
              payload,
              dataService,
              authenticatedFetch: authFetch,
              bffBaseUrl,
            });
            if (mappingResult.warnings.length > 0) {
              warnings.push(...mappingResult.warnings.map(w => `[Field Mapping] ${w}`));
            }
          }
        }

        // -- Create the sprk_analysis record ---------------------------------
        const analysisId = await dataService.createRecord('sprk_analysis', payload);

        // -- Follow-on: Create To Do (Field-Mapping-driven internally via
        //    TodoService.createTodo — task 021) --------------------------------
        if (context.selectedActions.includes('add-todo') && todoFormRef.current.title.trim()) {
          try {
            const todoResult = await createTodoRegardingChild(dataService, todoFormRef.current, {
              entityType: 'sprk_analysis',
              recordId: analysisId,
              recordName: finishName,
            });
            if (!todoResult.success) {
              warnings.push(
                `To do could not be created (${todoResult.errorMessage ?? 'Unknown error'}). ` +
                  'You can create it manually from the analysis record.'
              );
            } else if (todoResult.warnings?.length) {
              warnings.push(...todoResult.warnings.map(w => `[Field Mapping] ${w}`));
            }
          } catch (err) {
            const message = err instanceof Error ? err.message : 'Unknown error';
            warnings.push(
              `To do could not be created (${message}). You can create it manually from the analysis record.`
            );
          }
        }

        // -- Follow-on: Send Email (Field-Mapping-driven subject/body,
        //    falling back to user-entered values when no profile is configured) --
        if (context.selectedActions.includes('send-email') && emailToRef.current.trim() && authFetch && bffBaseUrl) {
          let mappedSubject: string | undefined;
          let mappedBody: string | undefined;
          try {
            const emailMappingPayload: Record<string, unknown> = {};
            const emailMappingResult = await applyFieldMappings({
              sourceEntity: 'sprk_analysis',
              sourceId: analysisId,
              targetEntity: 'sprk_communication',
              payload: emailMappingPayload,
              dataService,
              authenticatedFetch: authFetch,
              bffBaseUrl,
            });
            if (typeof emailMappingPayload['subject'] === 'string') {
              mappedSubject = emailMappingPayload['subject'] as string;
            }
            if (typeof emailMappingPayload['description'] === 'string') {
              mappedBody = emailMappingPayload['description'] as string;
            }
            if (emailMappingResult.warnings.length > 0) {
              warnings.push(...emailMappingResult.warnings.map(w => `[Field Mapping] ${w}`));
            }
          } catch {
            // Graceful no-op — falls back to user-entered subject/body below.
          }

          const emailService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);
          const emailResult = await emailService.sendEmail({
            to: emailToRef.current,
            subject: mappedSubject ?? emailSubjectRef.current,
            body: mappedBody ?? emailBodyRef.current,
            associations: [{ entityType: 'sprk_analysis', entityId: analysisId, entityName: finishName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        // -- Load the file to a workspace tab (existing document-viewer widget) --
        dispatch('workspace', {
          type: 'widget_load',
          widgetType: 'document-viewer',
          widgetData: {
            documentId,
            title: documentName,
            sessionId: data?.sessionId,
          },
        });

        setCompletedName(finishName);

        const hasWarnings = warnings.length > 0;
        const viewAnalysis = () => {
          navigationService?.openRecord?.('sprk_analysis', analysisId);
          handleClose();
        };

        return {
          icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
          title: hasWarnings ? 'Analysis created with warnings' : 'Analysis created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>&ldquo;{finishName}&rdquo;</span>{' '}
              has been created and the analysis is running.
              {hasWarnings ? ' Some follow-on operations could not complete — see details below.' : ''}
            </Text>
          ),
          warnings: hasWarnings ? warnings : undefined,
          actions: (
            <Button appearance="primary" onClick={viewAnalysis} data-testid="view-analysis-button">
              View
            </Button>
          ),
        };
      },
    };
  }, [
    dataService,
    webApiAdapter,
    navigationService,
    authFetch,
    bffBaseUrl,
    workTypeValue,
    workTypeLabel,
    name,
    description,
    owner,
    association,
    searchUsers,
    followOnCards,
    styles,
    dispatch,
    data?.resolveSpeContainerId,
    data?.sessionId,
    handleClose,
  ]);

  // ── Render: modal mode (embedded === false) ──────────────────────────────
  // Render ONLY the wizard's own Fluent Dialog (a portal) — NO full-height
  // wrapper — so the host (WorkspacePane) can overlay this without a stray
  // background box and unmount on close via onRequestClose. This branch runs
  // BEFORE the loading/error/completed wrappers below so none of them flash a
  // full-height panel behind the modal. The modal host injects services
  // synchronously, so `config` is ready before the modal opens; any not-ready
  // state degrades to null (nothing rendered).
  if (!embedded) {
    if (isLoading || error || isCompleted || !config) return null;
    return (
      <CreateRecordWizard
        open={isOpen}
        onClose={handleClose}
        webApi={webApiAdapter!}
        config={config}
        embedded={false}
      />
    );
  }

  // ── Render: loading ──────────────────────────────────────────────────────
  if (isLoading) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Loading wizard..." />
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      </div>
    );
  }

  if (isCompleted) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Text size={400} weight="semibold">
            {completedName ? `Analysis "${completedName}" created` : 'Analysis created'}
          </Text>
          <Text style={{ color: tokens.colorNeutralForeground3 }}>This wizard tab can now be closed.</Text>
        </div>
      </div>
    );
  }

  if (!config) {
    return (
      <div className={mergeClasses(styles.root, className)}>
        <div className={styles.centered}>
          <Spinner size="medium" label="Connecting to workspace services..." />
        </div>
      </div>
    );
  }

  return (
    <div className={mergeClasses(styles.root, className)}>
      <div className={styles.wizardContainer}>
        <CreateRecordWizard
          open={isOpen}
          onClose={handleClose}
          webApi={webApiAdapter!}
          config={config}
          embedded={true}
        />
      </div>
    </div>
  );
};

// ---------------------------------------------------------------------------
// serializeState helper (D-08 — query params only)
// ---------------------------------------------------------------------------

/**
 * Serialize the widget's recoverable state for Cosmos DB persistence.
 * Stores only the wizardId (identifiers, not form state) — form fields are
 * NOT serialized, matching the sibling Create*WizardWidget convention.
 */
export function serializeCreateAnalysisWizardState(wizardId: string): WidgetState<CreateAnalysisWizardData> {
  return {
    widgetType: 'create-analysis-wizard',
    version: 1,
    queryParams: {
      wizardId,
    },
    timestamp: new Date().toISOString(),
  };
}

CreateAnalysisWizardWidget.displayName = 'CreateAnalysisWizardWidget';

export { CreateAnalysisWizardWidget };
export default CreateAnalysisWizardWidget;
