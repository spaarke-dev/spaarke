/**
 * EmailWorkspace.tsx
 *
 * THE single shared React 19 composition root for the Outlook-style Email
 * surface (email-communication-solution-r5, spec FR-01 / NFR-06 / Success
 * Criterion 1). Both mounts — the SpaarkeAi `email` workspace widget and the
 * standalone Email code page — render this component UNCHANGED (dual-mount
 * parity: a bug fixed here fixes both surfaces).
 *
 * Reading-pane MAIN-AREA layout (one column, top → bottom — locked owner design):
 *   1. ASSOCIATION (`renderTop`) — collapsible (COLLAPSED by default), header
 *      status dot (🔴/🟡/🟢) → the redesigned resolver (`EmailConnectionsReview`).
 *      Rendered at the VERY TOP so the status dot is the first thing seen.
 *   2. TITLE BAR (`renderHeader`) — the email Subject on its own light-gray row
 *      (`EmailReadingHeader`).
 *   3. TOOLBAR (shell's own `EmailToolbar`) — Reply / Reply All / Forward / New
 *      (icon+text, left) + a right-aligned icon-only group with tooltips: Save to
 *      SharePoint, Create Event, Create To Do, Link Invoice, Open full form. The
 *      create/save actions act on the email's resolved association.
 *   4-6. `renderBody` composes, in order:
 *      4. RECIPIENTS — From/To always; Cc/Bcc only when non-empty (`EmailRecipients`).
 *      5. ATTACHMENTS — collapsible (COLLAPSED by default), header count
 *         "Attachments (N)" → `EmailReadingAttachments`.
 *      6. RELATED TO — collapsible (COLLAPSED), confirmed associations as pills
 *         (`EmailRelatedToPills`) — confirmed-state display ONLY.
 *      7. BODY — `EmailBodyView`, placed AFTER the Related-to section.
 *   (The old Tracking trio — monitor/high-priority/access — is REMOVED from the
 *   reading pane entirely per the redesign.)
 *
 * This component owns the ONE per-selection Dataverse read shared by every slot
 * (`useEmailWorkspaceRecord` — no `$select`, so a field absent on a given
 * deployment degrades to `null`/`false`, never a throw).
 *
 * NFR-06 (binding): NO per-mount conditional branch anywhere in this file. Every
 * dependency arrives as a host-agnostic prop (ADR-012). ADR-021: Fluent v9 tokens
 * only; mounts NO `FluentProvider` of its own. ADR-022/NFR-05: `React.FC` +
 * standard hooks only, no `as React.ComponentType` cast.
 */
import * as React from 'react';
import { makeStyles, tokens } from '@fluentui/react-components';
import type { ICommunicationAssociation } from '@spaarke/ui-components';
import { EmailViewSelector, useEmailViews } from '../EmailViewSelector';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import { EmailBodyView } from '../EmailBody';
import { EmailReadingHeader, EmailReadingAttachments } from '../EmailReadingHeader';
import { EmailRecipients } from '../EmailRecipients';
import { EmailConnectionsReview, ConfirmedChip, useConnectionsReviewStyles } from '../EmailAssociationsAndTracking';
import { useEmailComposeActions } from '../EmailComposeActions';
import { derivePrimaryReview, summarizePrimaryReview, unlinkRegarding } from '../../logic/connections';
import { launchCreate, type CreateKind } from '../../logic/actions';
import { CollapsibleSection } from './CollapsibleSection';
import { COMMUNICATION_ENTITY, mapRowToEmailCardItem } from './EmailWorkspace.mapping';
import { useEmailWorkspaceRecord } from './useEmailWorkspaceRecord';
import type { EmailWorkspaceProps } from './EmailWorkspace.types';

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minWidth: 0,
    minHeight: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  viewSelectorRow: {
    display: 'flex',
    alignItems: 'center',
    flexShrink: 0,
    // Widget-style header: border + subtle elevation to match the other workspace
    // widgets' title rows (owner UAT). NOTE: in the deployed app the widget host
    // frames this further; this is the component-side approximation.
    position: 'relative',
    zIndex: 1,
    paddingInline: tokens.spacingHorizontalL,
    paddingBlock: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    boxShadow: tokens.shadow2,
  },
  body: {
    display: 'flex',
    flexGrow: 1,
    minHeight: 0,
    overflow: 'hidden',
  },
  bodyRegion: {
    display: 'flex',
    flexDirection: 'column',
  },
  recipientsWrap: {
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalXL,
  },
});

export const EmailWorkspace: React.FC<EmailWorkspaceProps> = ({
  dataverseClient,
  dataService,
  navigationService,
  webApi,
  authenticatedFetch,
  bffBaseUrl,
  onSearchRecipients,
  onLookupRecipients,
  recordLookupCatalog,
  onLookupRecord,
  onAddRelationship,
  onUploadLocalAttachment,
  dataverseUrl,
  linkAnotherCatalog,
  initialSelectedId,
}) => {
  const s = useStyles();

  // Left pane: saved-view discovery + the raw rows the view's FetchXML selects.
  const { views, selectedViewId, setSelectedViewId, rows, isLoading, error } =
    useEmailViews<Record<string, unknown>>(dataverseClient);

  const cardItems = React.useMemo(() => rows.map(mapRowToEmailCardItem), [rows]);

  // Mirrors the shell's internally-owned `selectedId` so this component can drive
  // the ONE shared per-selection Dataverse read below.
  const [selectedId, setSelectedId] = React.useState<string | undefined>(initialSelectedId);

  const record = useEmailWorkspaceRecord(dataService, selectedId);

  // Attachment count for the collapsed "Attachments (N)" header — reported by the
  // (kept-mounted) attachments view after its per-selection load; reset on change.
  const [attachmentCount, setAttachmentCount] = React.useState(0);
  React.useEffect(() => {
    setAttachmentCount(0);
  }, [selectedId]);

  // Parent-email "Related to" inheritance: the selected record's typed regarding
  // lookups are the parent's associations for Reply/Reply All/Forward.
  const parentAssociations = React.useMemo<ICommunicationAssociation[]>(
    () =>
      (record.recordState?.filedAssociations ?? []).map(a => ({
        entityType: a.entityType,
        entityId: a.recordId,
        entityName: a.recordName,
      })),
    [record.recordState]
  );

  // Compose/reply/forward/new + "Open full form" — the ONE canonical composer.
  const { actions: composeActions, composerDialog, openFullForm } = useEmailComposeActions({
    authenticatedFetch,
    bffBaseUrl,
    dataService,
    navigationService,
    onSearchRecipients,
    onLookupRecipients,
    recordLookupCatalog,
    onLookupRecord,
    onAddRelationship,
    onUploadLocalAttachment,
    dataverseUrl,
    associations: parentAssociations,
    onSent: record.reload,
  });

  // "Save to SharePoint" (archive) — mirrors the `CommunicationActions` PCF's
  // handleArchive: POST the existing archive endpoint, then re-read the record so
  // the `.eml` body + attachments reflect the archived copy. Best-effort.
  const handleSaveToSharePoint = React.useCallback(
    (communicationId: string): void => {
      void (async () => {
        try {
          const resp = await authenticatedFetch(`/communications/${communicationId}/archive`, { method: 'POST' });
          if (resp.ok) record.reload();
          else console.warn(`[EmailWorkspace] Save to SharePoint failed (${resp.status}).`);
        } catch (err) {
          console.warn('[EmailWorkspace] Save to SharePoint failed:', err);
        }
      })();
    },
    [authenticatedFetch, record.reload]
  );

  // "Create from this email" (Event / To Do / Invoice) — routes through the single
  // `launchCreate` seam (OOB `navigateTo` modal today; swappable later) so the
  // create form opens against the email's context / resolved association.
  const handleCreate = React.useCallback((kind: CreateKind): void => {
    launchCreate(kind, { onError: err => console.warn('[EmailWorkspace] create-from-email launch failed:', err) });
  }, []);

  const toolbarActions = React.useMemo(
    () => ({
      ...composeActions,
      onSaveToSharePoint: handleSaveToSharePoint,
      onCreateEvent: () => handleCreate('event'),
      onCreateTodo: () => handleCreate('todo'),
      onLinkInvoice: () => handleCreate('invoice'),
      onOpenFullForm: (communicationId: string) => {
        void openFullForm(communicationId);
      },
    }),
    [composeActions, handleSaveToSharePoint, handleCreate, openFullForm]
  );

  const filedAssociations = record.recordState?.filedAssociations ?? [];

  // Single-primary review state (🔴 requires review · 🟡 needs confirmation ·
  // 🟢 confirmed) — drives the merged "Related to" section's status dot AND the
  // confirmed chip shown in that section's header (visible while collapsed).
  const primaryModel = React.useMemo(
    () =>
      derivePrimaryReview(
        record.recordState?.associationProvenanceJson ?? null,
        record.recordState?.associationStatus ?? null,
        filedAssociations,
        {
          recordName: record.recordState?.regardingRecordName,
          recordNumber: record.recordState?.regardingRecordNumber,
        }
      ),
    [record.recordState, filedAssociations]
  );
  const associationSummary = React.useMemo(() => summarizePrimaryReview(primaryModel), [primaryModel]);
  const connStyles = useConnectionsReviewStyles();

  // Remove the confirmed primary from the header chip (nulls exactly that one
  // regarding lookup; every sibling association stays). `id` is the selected
  // communication (host record) — resolved per-selection in `renderBody`.
  const removePrimary = React.useCallback(
    (entity: string, communicationId: string): void => {
      void (async () => {
        try {
          await unlinkRegarding({ webApi, hostEntity: COMMUNICATION_ENTITY, hostRecordId: communicationId }, entity);
          record.reload();
        } catch (err) {
          console.warn('[EmailWorkspace] remove association failed:', err);
        }
      })();
    },
    [webApi, record.reload]
  );

  return (
    <div className={s.root} data-testid="email-workspace">
      <div className={s.viewSelectorRow}>
        <EmailViewSelector
          views={views}
          activeViewId={selectedViewId}
          onViewChange={setSelectedViewId}
          isLoading={isLoading}
          error={error}
        />
      </div>

      <div className={s.body}>
        <EmailReadingPaneShell
          items={cardItems}
          isLoading={isLoading}
          initialSelectedId={initialSelectedId}
          onSelectedIdChange={setSelectedId}
          actions={toolbarActions}
          renderHeader={() => <EmailReadingHeader subject={record.recordState?.subject ?? null} />}
          renderBody={id => (
            <div className={s.bodyRegion}>
              <div className={s.recipientsWrap}>
                <EmailRecipients
                  from={record.recordState?.from ?? null}
                  to={record.recordState?.to ?? null}
                  cc={record.recordState?.cc ?? null}
                  bcc={record.recordState?.bcc ?? null}
                />
              </div>

              <CollapsibleSection id="attachments" title="Attachments" count={attachmentCount} keepMounted>
                <EmailReadingAttachments
                  selectedId={id}
                  dataService={dataService}
                  navigation={navigationService}
                  apiBaseUrl={bffBaseUrl}
                  onCountChange={setAttachmentCount}
                />
              </CollapsibleSection>

              {/* MERGED "Related to" — one section that BOTH shows the primary
                  association state (dot: 🔴 requires review · 🟡 needs confirmation
                  · 🟢 confirmed) AND resolves it. Replaces the old split of a
                  top "Association" resolver + a separate read-only pills list. */}
              <CollapsibleSection
                id="related-to"
                title="Related to"
                status={{ tone: associationSummary.tone, label: associationSummary.label }}
                headerAccessory={
                  primaryModel.state === 'confirmed' && primaryModel.primary ? (
                    <ConfirmedChip
                      primary={primaryModel.primary}
                      busy={false}
                      readOnly={false}
                      onOpen={
                        primaryModel.primary.targetId && navigationService.openRecordModal
                          ? () =>
                              void navigationService.openRecordModal?.(
                                primaryModel.primary!.entity,
                                primaryModel.primary!.targetId
                              )
                          : undefined
                      }
                      onRemove={() => removePrimary(primaryModel.primary!.entity, id)}
                      s={connStyles}
                    />
                  ) : undefined
                }
                defaultOpen
              >
                <EmailConnectionsReview
                  communicationId={id}
                  associationStatus={record.recordState?.associationStatus ?? null}
                  associationProvenanceJson={record.recordState?.associationProvenanceJson ?? null}
                  regardingRecordName={record.recordState?.regardingRecordName ?? null}
                  regardingRecordNumber={record.recordState?.regardingRecordNumber ?? null}
                  filedAssociations={filedAssociations}
                  writeContext={{ webApi, hostEntity: COMMUNICATION_ENTITY, hostRecordId: id }}
                  pickerWebApi={webApi}
                  linkAnotherCatalog={linkAnotherCatalog}
                  onAssociationsChanged={record.reload}
                />
              </CollapsibleSection>

              <EmailBodyView
                selectedId={id}
                emlDocumentId={record.emlDocumentId}
                body={record.recordState?.sprk_body ?? ''}
                recordLoadError={record.recordLoadError}
                onRetryRecord={record.retry}
                authenticatedFetch={authenticatedFetch}
              />
            </div>
          )}
        />
      </div>

      {composerDialog}
    </div>
  );
};

EmailWorkspace.displayName = 'EmailWorkspace';

export default EmailWorkspace;
