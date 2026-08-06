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
 *      6. RELATED TO — the merged single-primary resolver (`EmailConnectionsReview`
 *         + a confirmed `ConfirmedChip` in the header). OPEN by default while it
 *         still needs action (🔴 requires-review / 🟡 needs-confirmation), and
 *         COLLAPSED once the primary is 🟢 confirmed (owner UAT #7).
 *      7. SUBJECT + BODY — the email subject line (owner UAT #4) directly above
 *         `EmailBodyView`, both placed AFTER the Related-to section.
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
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import type { ICommunicationAssociation } from '@spaarke/ui-components';
import { EmailViewSelector, useEmailViews } from '../EmailViewSelector';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import { EmailBodyView } from '../EmailBody';
import { EmailReadingHeader, EmailReadingAttachments } from '../EmailReadingHeader';
import { EmailRecipients } from '../EmailRecipients';
import { EmailConnectionsReview, ConfirmedChip, useConnectionsReviewStyles } from '../EmailAssociationsAndTracking';
import { useEmailComposeActions } from '../EmailComposeActions';
import { derivePrimaryReview, summarizePrimaryReview, clearPrimaryRegarding } from '../../logic/connections';
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
    // owner UAT 2026-07-30 R2 item 2 — the view dropdown ("All Incoming Email ⌄")
    // IS the widget's header/title; there is no separate "Email" label. Give it the
    // elevated Fluent card look the other workspace widget headers use ("Active
    // Documents ⌄"): a `colorNeutralBackground1` surface + `shadow4` + rounded
    // corners, so the view name + chevron reads as the widget title. A small bottom
    // margin lets the rounded elevation read above the reading pane. NOTE: in the
    // deployed app the widget host frames this further; this is the component-side
    // approximation. Semantic tokens only (ADR-021).
    position: 'relative',
    zIndex: 1,
    paddingInline: tokens.spacingHorizontalL,
    paddingBlock: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow4,
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
  // Subject line shown directly ABOVE the message body (owner UAT #4) — mirrors
  // Outlook, where the subject sits right above the message. Same value as the
  // title bar; semibold. Semantic tokens only (ADR-021).
  bodySubject: {
    display: 'block',
    paddingInline: tokens.spacingHorizontalXL,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalS,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
    color: tokens.colorNeutralForeground1,
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
  onResolveShareLink,
  onListEmailTemplates,
  onRenderEmailTemplate,
  onDraftWithAi,
  aiDraftActions,
  fromMailbox,
  dataverseUrl,
  linkAnotherCatalog,
  initialSelectedId,
  hideList,
}) => {
  const s = useStyles();

  // Single-record ("form") mode: only hide the list when a record is actually
  // pre-selected (otherwise a hidden list with no selection would be a dead-end
  // "Select an email" placeholder). Default falsy → unchanged list+reading path.
  const hideListPane = Boolean(hideList) && Boolean(initialSelectedId);

  // Left pane: saved-view discovery + the raw rows the view's FetchXML selects.
  // `refetch` re-runs the active view's FetchXML for the list Refresh button
  // (owner UAT 2026-08-03 Item 2).
  const { views, selectedViewId, setSelectedViewId, rows, isLoading, error, refetch } =
    useEmailViews<Record<string, unknown>>(dataverseClient);

  const cardItems = React.useMemo(() => rows.map(mapRowToEmailCardItem), [rows]);

  // Mirrors the shell's internally-owned `selectedId` so this component can drive
  // the ONE shared per-selection Dataverse read below.
  const [selectedId, setSelectedId] = React.useState<string | undefined>(initialSelectedId);

  // owner UAT 2026-07-30 R2 item 3 — auto-select the FIRST email once the rows
  // load, so the reading pane opens on an email instead of the empty "Select an
  // email" placeholder. Fires ONCE (a ref guards re-selection on later renders),
  // only when no id is pre-selected and not in single-record `hideList` mode — it
  // never fights a user's manual selection. The resolved first id is fed to the
  // shell as its `initialSelectedId`; the shell adopts it (see its item-3 effect)
  // and notifies back through `onSelectedIdChange`, keeping this mirror in sync.
  const autoSelectedRef = React.useRef(false);
  const [autoFirstId, setAutoFirstId] = React.useState<string | undefined>(undefined);
  React.useEffect(() => {
    if (autoSelectedRef.current || hideListPane || initialSelectedId) return;
    const firstId = cardItems[0]?.id;
    if (firstId) {
      autoSelectedRef.current = true;
      setAutoFirstId(firstId);
    }
  }, [cardItems, hideListPane, initialSelectedId]);

  const effectiveInitialSelectedId = initialSelectedId ?? autoFirstId;

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
  const {
    actions: composeActions,
    composerDialog,
    openFullForm,
  } = useEmailComposeActions({
    // Record-single mode renders inside the OOB email-record modal — the
    // composer must fully cover it (UAT 2026-08-03). List mode stays floating.
    composerFullBleed: hideListPane,
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
    onResolveShareLink,
    onListEmailTemplates,
    onRenderEmailTemplate,
    onDraftWithAi,
    aiDraftActions,
    fromMailbox,
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
      // Left-list Refresh (owner UAT 2026-08-03 Item 2) — re-runs the active
      // view's FetchXML so the card list reflects newly-arrived/changed rows.
      onRefresh: refetch,
      onSaveToSharePoint: handleSaveToSharePoint,
      onCreateEvent: () => handleCreate('event'),
      onCreateTodo: () => handleCreate('todo'),
      onLinkInvoice: () => handleCreate('invoice'),
      onOpenFullForm: (communicationId: string) => {
        void openFullForm(communicationId);
      },
    }),
    [composeActions, refetch, handleSaveToSharePoint, handleCreate, openFullForm]
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
          recordTypeLabel: record.recordState?.regardingRecordType,
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
          // Fully clear the single primary (denorm fields + typed lookup + status),
          // NOT just a typed-lookup unlink — a denorm-only primary has no typed
          // lookup to null, so plain unlink silently no-oped (owner UAT item 2).
          await clearPrimaryRegarding(
            { webApi, hostEntity: COMMUNICATION_ENTITY, hostRecordId: communicationId },
            entity
          );
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
      {/* Single-record ("form") mode hides the whole list chrome — both the card list
          AND this view-selector row ("All Incoming Email") — so opening one email via
          `openEmailRecord(id, { single: true })` reads as a clean single email, not the
          list+reading workspace (owner UAT 2026-07-31). List mode renders it unchanged. */}
      {!hideListPane && (
        <div className={s.viewSelectorRow}>
          <EmailViewSelector
            views={views}
            activeViewId={selectedViewId}
            onViewChange={setSelectedViewId}
            isLoading={isLoading}
            error={error}
          />
        </div>
      )}

      <div className={s.body}>
        <EmailReadingPaneShell
          items={cardItems}
          isLoading={isLoading}
          initialSelectedId={effectiveInitialSelectedId}
          hideList={hideListPane}
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
                // Re-key on the confirmed boundary so the section adopts the
                // correct default open-state when the async record load resolves
                // (or when the user confirms): 🟢 confirmed → collapsed; 🔴/🟡 →
                // open so the user can still act (owner UAT #7).
                key={`related-to-${primaryModel.state === 'confirmed' ? 'confirmed' : 'active'}`}
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
                defaultOpen={primaryModel.state !== 'confirmed'}
              >
                <EmailConnectionsReview
                  communicationId={id}
                  associationStatus={record.recordState?.associationStatus ?? null}
                  associationProvenanceJson={record.recordState?.associationProvenanceJson ?? null}
                  regardingRecordName={record.recordState?.regardingRecordName ?? null}
                  regardingRecordNumber={record.recordState?.regardingRecordNumber ?? null}
                  regardingRecordType={record.recordState?.regardingRecordType ?? null}
                  filedAssociations={filedAssociations}
                  writeContext={{
                    webApi,
                    hostEntity: COMMUNICATION_ENTITY,
                    hostRecordId: id,
                    // FR-A4 (R-1): after a human confirms, record affinity to the BFF so the AffinityRung
                    // learns this email's signals → this record. Fire-and-forget + best-effort — the .catch
                    // swallows failures so a learning signal never affects the confirmation (mirrors the
                    // archive POST above, ADR-028 authenticatedFetch).
                    recordAffinity: (targetEntityType, targetRecordId) => {
                      void authenticatedFetch(`/communications/${id}/confirm-affinity`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ targetEntityType, targetRecordId }),
                      }).catch(() => {
                        /* best-effort learning signal — never surfaced to the user */
                      });
                    },
                  }}
                  pickerWebApi={webApi}
                  linkAnotherCatalog={linkAnotherCatalog}
                  onAssociationsChanged={record.reload}
                />
              </CollapsibleSection>

              {/* Subject line directly above the message body (owner UAT #4) —
                  same value the title bar shows; gives the reader the subject
                  right above the message, Outlook-style. */}
              <Text as="h2" className={s.bodySubject} data-testid="email-body-subject" truncate wrap={false}>
                {record.recordState?.subject || '(no subject)'}
              </Text>

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
