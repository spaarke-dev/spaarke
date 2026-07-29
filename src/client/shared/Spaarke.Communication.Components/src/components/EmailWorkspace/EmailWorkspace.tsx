/**
 * EmailWorkspace.tsx
 *
 * THE single shared React 19 composition root for the Outlook-style Email
 * surface (email-communication-solution-r5 task 040, spec FR-01 / NFR-06 /
 * Success Criterion 1). Both mounts — the SpaarkeAi `email` workspace widget
 * (task 041) and the standalone Email code page (task 042) — render this
 * component UNCHANGED. This is the dual-mount-parity guarantee: a bug fixed
 * here fixes both surfaces; there is no second copy of the wiring.
 *
 * Composition (Outlook-style, one-column compose-style reading pane —
 * reading-pane layout redesign):
 *   - Top: `EmailViewSelector` (031) over `sprk_communication` saved views,
 *     driving `EmailCardList` (030) via the task-031 `useEmailViews` hook.
 *   - Right pane: `EmailReadingPaneShell` (032) with its two `render*` slots
 *     filled, top → bottom:
 *       1. renderHeader → `EmailReadingHeader` — the HEADER BAND: subject
 *          (left) + compact tracking trio + demoted "Open full form" icon
 *          button (right). Rendered ABOVE the toolbar.
 *       2. (the shell's own full-width `EmailToolbar` — Reply/Reply All/
 *          Forward/New/Archive/Create — unchanged.)
 *       3-6. renderBody → this component composes, in order:
 *            - `EmailRecipients` — labeled From/To (always) + Cc/Bcc (only
 *              when present), compose-style.
 *            - `EmailBodyView` (033), fed `sprk_body` + the resolved `.eml`
 *              archive document id from this component's own per-selection
 *              record read.
 *            - Attachments section — bold "Attachments" header +
 *              `EmailReadingAttachments` (034).
 *            - "Related to" section — bold header + `EmailConnectionsReview`
 *              (035, the association resolver — confirm/change/dismiss/
 *              link-another), fed the association state from the same
 *              per-selection read; `onAssociationsChanged` re-reads it.
 *     actions → `useEmailComposeActions` (036) — Reply/Reply All/Forward/New
 *               open the ONE canonical `SendEmailDialog`/`EmailComposer`,
 *               never forked.
 *
 * This component owns the ONE per-selection Dataverse read shared by every
 * slot (`useEmailWorkspaceRecord` — no `$select`, so a field absent on a
 * given deployment degrades to `null`/`false`, never a throw). The header
 * band used to run its OWN internal `retrieveRecord(..., $select)` read —
 * that second, redundant read failed at runtime ("Could not load this email
 * header."); it has been REMOVED as part of this redesign. There is exactly
 * one per-selection Dataverse read left in this tree.
 *
 * NFR-06 (binding): NO per-mount conditional branch (widget-vs-code-page, or
 * equivalent) anywhere in this file. Every dependency arrives as a host-
 * agnostic prop (ADR-012) — `IDataverseClient`/`IDataService`/
 * `INavigationService`/`authenticatedFetch` — never a PCF platform context
 * type or a global Dataverse client reference.
 *
 * ADR-021: Fluent v9 tokens only; this component mounts NO `FluentProvider`
 * of its own — theme (light/dark) is inherited from the host. ADR-022/NFR-05:
 * `React.FC` + standard hooks only, no `as React.ComponentType` cast.
 */
import * as React from 'react';
import { makeStyles, tokens, Text } from '@fluentui/react-components';
import type { ICommunicationAssociation } from '@spaarke/ui-components';
import { EmailViewSelector, useEmailViews } from '../EmailViewSelector';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import { EmailBodyView } from '../EmailBody';
import { EmailReadingHeader, EmailReadingAttachments } from '../EmailReadingHeader';
import { EmailRecipients } from '../EmailRecipients';
import { EmailConnectionsReview } from '../EmailAssociationsAndTracking';
import { useEmailComposeActions } from '../EmailComposeActions';
import { COMMUNICATION_ENTITY, DEFAULT_ACCESS_PERMISSION_OPTIONS, mapRowToEmailCardItem } from './EmailWorkspace.mapping';
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
    paddingInline: tokens.spacingHorizontalL,
    paddingBlock: tokens.spacingVerticalS,
    borderBottomWidth: tokens.strokeWidthThin,
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
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
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    paddingBlock: tokens.spacingVerticalL,
    paddingInline: tokens.spacingHorizontalXL,
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
  },
  sectionTitle: {
    fontFamily: '"Segoe UI", system-ui, sans-serif',
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
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
  accessPermissionOptions = DEFAULT_ACCESS_PERMISSION_OPTIONS,
  onSearchRecipients,
  onLookupRecipients,
  recordLookupCatalog,
  onLookupRecord,
  onAddRelationship,
  dataverseUrl,
  linkAnotherCatalog,
  initialSelectedId,
}) => {
  const s = useStyles();

  // Left pane: saved-view discovery (031) + the raw rows the view's FetchXML selects.
  const { views, selectedViewId, setSelectedViewId, rows, isLoading, error } =
    useEmailViews<Record<string, unknown>>(dataverseClient);

  const cardItems = React.useMemo(() => rows.map(mapRowToEmailCardItem), [rows]);

  // Mirrors the shell's internally-owned `selectedId` (observability seam per
  // `EmailReadingPaneShell.types.ts` — does NOT control selection) so this
  // component can drive the ONE shared per-selection Dataverse read below.
  const [selectedId, setSelectedId] = React.useState<string | undefined>(initialSelectedId);

  const record = useEmailWorkspaceRecord(dataService, selectedId);

  // Parent-email "Related to" inheritance (FR — compose-wiring fix #4): the ONE
  // per-selection read already resolves the selected record's typed regarding
  // lookups as `filedAssociations`. Since Reply/Reply All/Forward are always
  // invoked on the SELECTED record, those are exactly the parent's associations
  // — map them to the composer's `ICommunicationAssociation` shape (entityUrl is
  // optional and only used for chip deep-links, so it's left unset here).
  const parentAssociations = React.useMemo<ICommunicationAssociation[]>(
    () =>
      (record.recordState?.filedAssociations ?? []).map(a => ({
        entityType: a.entityType,
        entityId: a.recordId,
        entityName: a.recordName,
      })),
    [record.recordState]
  );

  // Compose/reply/forward/new + "Open full form" (036, FR-09/FR-10/FR-15) —
  // mounts the ONE canonical `SendEmailDialog`/`EmailComposer`, never forked.
  const { actions, composerDialog, openFullForm } = useEmailComposeActions({
    authenticatedFetch,
    bffBaseUrl,
    dataService,
    navigationService,
    onSearchRecipients,
    onLookupRecipients,
    recordLookupCatalog,
    onLookupRecord,
    onAddRelationship,
    dataverseUrl,
    associations: parentAssociations,
    onSent: record.reload,
  });

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
          actions={actions}
          renderHeader={id => (
            <EmailReadingHeader
              communicationId={id}
              subject={record.recordState?.subject ?? null}
              monitor={record.recordState?.monitor ?? false}
              highPriority={record.recordState?.highPriority ?? false}
              accessPermission={record.recordState?.accessPermission ?? null}
              accessPermissionOptions={accessPermissionOptions}
              onMonitorChange={record.updateMonitor}
              onHighPriorityChange={record.updateHighPriority}
              onAccessPermissionChange={record.updateAccessPermission}
              onOpenFullForm={openFullForm}
            />
          )}
          renderBody={id => (
            <div className={s.bodyRegion}>
              <EmailRecipients
                from={record.recordState?.from ?? null}
                to={record.recordState?.to ?? null}
                cc={record.recordState?.cc ?? null}
                bcc={record.recordState?.bcc ?? null}
              />

              <EmailBodyView
                selectedId={id}
                emlDocumentId={record.emlDocumentId}
                body={record.recordState?.sprk_body ?? ''}
                recordLoadError={record.recordLoadError}
                onRetryRecord={record.retry}
                authenticatedFetch={authenticatedFetch}
              />

              <section className={s.section}>
                <Text className={s.sectionTitle}>Attachments</Text>
                <EmailReadingAttachments
                  selectedId={id}
                  dataService={dataService}
                  navigation={navigationService}
                  apiBaseUrl={bffBaseUrl}
                />
              </section>

              <section className={s.section}>
                <Text className={s.sectionTitle}>Related to</Text>
                <EmailConnectionsReview
                  communicationId={id}
                  associationStatus={record.recordState?.associationStatus ?? null}
                  associationProvenanceJson={record.recordState?.associationProvenanceJson ?? null}
                  regardingRecordName={record.recordState?.regardingRecordName ?? null}
                  filedAssociations={record.recordState?.filedAssociations ?? []}
                  writeContext={{ webApi, hostEntity: COMMUNICATION_ENTITY, hostRecordId: id }}
                  pickerWebApi={webApi}
                  linkAnotherCatalog={linkAnotherCatalog}
                  onAssociationsChanged={record.reload}
                />
              </section>
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
