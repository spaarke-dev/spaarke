/**
 * CreateEventWizard.tsx
 * Thin wrapper around CreateRecordWizard for "Create New Event".
 *
 * Provides only:
 *   - Entity-specific form step (CreateEventStep)
 *   - Finish handler (EventService.createEvent + EntityCreationService for email)
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
 * optimization (same pattern as CreateProjectWizard).
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
  type IAssociateToStepConfig,
  type AssociationResult,
} from '../CreateRecordWizard';
import { EVENT_REGARDING_TARGETS } from '../AssociateToStep/types';

import type { IWizardSuccessConfig } from '../Wizard/wizardShellTypes';

import { CreateEventStep } from './CreateEventStep';
import { EventService } from './eventService';
import { EMPTY_EVENT_FORM } from './formTypes';
import type { ICreateEventFormState } from './formTypes';

import { EntityCreationService } from '../../services/EntityCreationService';
import type { IDataService, INavigationService } from '../../types/serviceInterfaces';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Pure helpers (exported for unit testing — see __tests__/CreateEventWizard.associateToStep.test.ts)
// ---------------------------------------------------------------------------

/**
 * Determines the `associateToStep` config for CreateEventWizard.
 *
 * Gated MORE PRECISELY than `TodoWizardDialog`'s equivalent (which enables
 * purely on `navigationService` presence): `CreateEventWizard` is consumed by
 * BOTH the locked Visual Host launch (task 012 `initialAssociation`/
 * `lockAssociation=true` seed) AND the pre-existing standalone Code Page
 * (`src/solutions/CreateEventWizard/src/main.tsx`), which already passes a
 * `navigationService` but neither `initialAssociation` nor `lockAssociation`.
 *
 * Gating purely on `navigationService` would suddenly surface an unlocked,
 * un-seeded Associate-To step on that Code Page — a UX regression to an
 * already-shipped surface (task 015 step 7 finding). Requiring an explicit
 * signal (`initialAssociation` supplied OR `lockAssociation === true`)
 * preserves that surface's existing behavior (no Associate-To step at all)
 * while enabling the Visual Host locked-launch path this task targets.
 */
export function resolveEventAssociateToStepConfig(
  navigationService: INavigationService | undefined,
  initialAssociation: AssociationResult | undefined,
  lockAssociation: boolean | undefined
): IAssociateToStepConfig | undefined {
  if (!navigationService) return undefined;
  if (initialAssociation === undefined && !lockAssociation) return undefined;

  return {
    entityTypes: EVENT_REGARDING_TARGETS.slice(),
    navigationService,
    initialAssociation,
    lockAssociation,
  };
}

/**
 * Copies the wizard's selected/seeded regarding association
 * (`IFinishContext.association`) onto the form fields `EventService.createEvent`
 * expects (`regardingRecordId`/`regardingRecordName` — read off `formValues`
 * per its JSDoc contract; the entity type is passed as a separate argument).
 *
 * When no association is present (AssociateToStep hidden, or the user
 * skipped it), `formValues` passes through unchanged — behaviorally identical
 * to the pre-task-015 create path (no regarding parent).
 */
export function mergeEventRegardingFromAssociation(
  formValues: ICreateEventFormState,
  association: AssociationResult | null
): ICreateEventFormState {
  if (!association) return formValues;
  return {
    ...formValues,
    regardingRecordId: association.recordId,
    regardingRecordName: association.recordName,
  };
}

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
  return result.entities.map(e => ({
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
  return result.entities.map(e => ({
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
  return result.entities.map(e => ({
    id: e['systemuserid'] as string,
    name: (e['fullname'] as string) + (e['internalemailaddress'] ? ` (${e['internalemailaddress']})` : ''),
  }));
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateEventWizardProps {
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
  /** MSAL-backed authenticated fetch function for BFF API calls. */
  authenticatedFetch?: typeof fetch;
  /** BFF API base URL. */
  bffBaseUrl?: string;
  /**
   * Optional callback to resolve the SPE container ID for file uploads.
   * Called when the wizard opens. If not provided, file uploads will be skipped.
   */
  resolveSpeContainerId?: () => Promise<string>;
  /**
   * AAD tenant ID. Forwarded to `EntityCreationService.indexUploadedFiles()` so
   * post-upload RAG indexing via `@spaarke/sdap-client` can route to the
   * correct multi-tenant index. When omitted, indexing is skipped (files still
   * upload to SPE successfully). Typically sourced from solution
   * `config.tenantId` in `main.tsx`.
   */
  tenantId?: string;
  /**
   * Optional pre-filled regarding association (visual-host-create-button-r1
   * task 012/015). When supplied, the AssociateToStep starts pre-selected with
   * this record — the user may still change it or clear it before advancing,
   * UNLESS `lockAssociation` is also `true` (see below).
   */
  initialAssociation?: AssociationResult;
  /**
   * When `true`, the Associate-To step is hidden entirely and
   * `initialAssociation` flows straight through to `onFinish` (via
   * `context.association`) without ever showing the picker — used when the
   * parent record is unambiguous, e.g. a wizard launched from a Visual Host
   * visual (design §5.5, task 012's VisualHostRoot seed).
   *
   * Defaults to `false`. See `resolveEventAssociateToStepConfig` for the full
   * gating rule (also requires `navigationService` and either this flag or
   * `initialAssociation` to be present) — this keeps the pre-existing Code
   * Page entry point (`src/solutions/CreateEventWizard/src/main.tsx`, which
   * passes `navigationService` but neither of these two props) unchanged.
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
// CreateEventWizard
// ---------------------------------------------------------------------------

const CreateEventWizard: React.FC<ICreateEventWizardProps> = ({
  open,
  onClose,
  dataService,
  navigationService,
  embedded,
  authenticatedFetch: authFetch,
  bffBaseUrl,
  resolveSpeContainerId,
  tenantId,
  initialAssociation,
  lockAssociation,
}) => {
  // -- Entity-specific form state --------------------------------------------
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateEventFormState>(EMPTY_EVENT_FORM);
  const formValuesRef = React.useRef(formValues);
  formValuesRef.current = formValues;

  // Reset form state on open
  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(EMPTY_EVENT_FORM);
    }
  }, [open]);

  // -- Search callbacks ------------------------------------------------------
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

  // -- WebApi adapter for CreateRecordWizard ---------------------------------
  const webApiAdapter = React.useMemo(() => buildWebApiAdapter(dataService), [dataService]);

  // -- Wizard config ---------------------------------------------------------
  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Event',
      entityLabel: 'event',
      filesStepSubtitle: 'Upload documents to associate with this event, or click Next to skip.',
      finishingLabel: 'Creating event\u2026',

      // visual-host-create-button-r1 task 015 -- see resolveEventAssociateToStepConfig
      // doc comment for the gating rationale (precise gate vs. TodoWizardDialog's
      // navigationService-only gate, to avoid a Code Page regression).
      associateToStep: resolveEventAssociateToStepConfig(navigationService, initialAssociation, lockAssociation),

      infoStep: {
        id: 'create-record',
        label: 'Event Details',
        canAdvance: () => formValid,
        renderContent: _wizardFiles => (
          <CreateEventStep
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

      resolveSpeContainerId,

      buildEmailSubject: (entityName: string) => `New Event: ${entityName}`,
      buildEmailBody: (fields: Record<string, string>) =>
        `Dear Colleague,\n\nA new event, "${fields.eventName || ''}", has been created.\n\n` +
        `Please review and take any necessary action.\n\n` +
        `Kind regards,\n[Your Name]`,
      getEntityName: () => formValuesRef.current.eventName,
      getFormFields: () => ({
        eventName: formValuesRef.current.eventName,
        eventTypeName: formValuesRef.current.eventTypeName,
      }),

      onFinish: async (context: IFinishContext): Promise<IWizardSuccessConfig> => {
        // visual-host-create-button-r1 task 015: copy the AssociateToStep's
        // selected/seeded regarding association (context.association -- null
        // when the step is absent/skipped) onto the fields EventService.createEvent
        // reads off formValues, then pass the entity type as the separate
        // regardingEntityName argument (ADR-024 applyResolverFields contract).
        const currentFormValues = mergeEventRegardingFromAssociation(formValuesRef.current, context.association);

        // Task 020 (spec FR-12): pass authFetch/bffBaseUrl so EventService can
        // drive the Field Mapping Framework engine (graceful no-op when the
        // host omits either, or when there's no regarding association / no
        // configured profile for the pair).
        const eventService = new EventService(dataService, authFetch, bffBaseUrl);
        const result = await eventService.createEvent(currentFormValues, context.association?.entityType);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create event');
        }

        const eventId = result.eventId!;
        const eventName = result.eventName!;
        const warnings: string[] = [...result.warnings];

        // Upload files to SPE + create document records
        if (context.uploadedFiles.length > 0 && context.speContainerId && authFetch && bffBaseUrl) {
          try {
            const entityService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);

            const uploadResult = await entityService.uploadFilesToSpe(context.speContainerId, context.uploadedFiles);

            if (uploadResult.errors.length > 0) {
              for (const err of uploadResult.errors) {
                warnings.push(`File upload failed for "${err.fileName}": ${err.error}`);
              }
            }

            if (uploadResult.uploadedFiles.length > 0) {
              const docResult = await entityService.createDocumentRecords(
                'sprk_events',
                eventId,
                'sprk_Event',
                uploadResult.uploadedFiles,
                {
                  containerId: context.speContainerId,
                  parentRecordName: eventName,
                }
              );

              if (docResult.warnings.length > 0) {
                warnings.push(...docResult.warnings);
              }

              // Trigger RAG indexing per file (canonical sync OBO path via
              // @spaarke/sdap-client.SdapApiClient.indexFile). Non-fatal.
              const indexingWarnings = await entityService.indexUploadedFiles(
                uploadResult.uploadedFiles,
                tenantId ?? '',
                {
                  entityType: 'sprk_event',
                  entityId: eventId,
                  entityName: eventName,
                },
                docResult.createdDocumentIds
              );
              if (indexingWarnings.length > 0) {
                warnings.push(...indexingWarnings);
              }
            }
          } catch (err) {
            const message = err instanceof Error ? err.message : 'File processing failed';
            warnings.push(`File pipeline error: ${message}`);
          }
        } else if (context.uploadedFiles.length > 0 && !context.speContainerId) {
          warnings.push('SPE container not configured — files were not uploaded to SharePoint Embedded.');
        }

        // Send email (if selected)
        if (
          context.selectedActions.includes('send-email') &&
          context.followOn.emailTo.trim() &&
          authFetch &&
          bffBaseUrl
        ) {
          const emailService = new EntityCreationService(webApiAdapter, authFetch, bffBaseUrl);
          const emailResult = await emailService.sendEmail({
            to: context.followOn.emailTo,
            subject: context.followOn.emailSubject,
            body: context.followOn.emailBody,
            associations: [{ entityType: 'sprk_event', entityId: eventId, entityName: eventName }],
          });
          if (!emailResult.success && emailResult.warning) warnings.push(emailResult.warning);
        }

        const hasWarnings = warnings.length > 0;

        const viewEvent = () => {
          if (navigationService) {
            navigationService.openRecord('sprk_event', eventId);
          }
          onClose();
        };

        return {
          icon: <CheckmarkCircleFilled fontSize={64} style={{ color: tokens.colorPaletteGreenForeground1 }} />,
          title: hasWarnings ? 'Event created with warnings' : 'Event created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>&ldquo;{eventName}&rdquo;</span>{' '}
              has been created
              {hasWarnings
                ? ', though some operations could not complete. See details below.'
                : ' and is ready to use.'}
            </Text>
          ),
          actions: (
            <>
              <Button appearance="primary" onClick={viewEvent} aria-label={`View event: ${eventName}`}>
                View Event
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
      handleSearchContacts,
      handleSearchOrganizations,
      handleSearchUsers,
      onClose,
      authFetch,
      bffBaseUrl,
      navigationService,
      resolveSpeContainerId,
      webApiAdapter,
      initialAssociation,
      lockAssociation,
    ]
  );

  // -- Render ----------------------------------------------------------------

  return (
    <CreateRecordWizard open={open} onClose={onClose} webApi={webApiAdapter} config={config} embedded={embedded} />
  );
};

export default CreateEventWizard;
export { CreateEventWizard };
