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
 * Composition (Outlook-style two-pane, design Lens 2):
 *   - Top: `EmailViewSelector` (031) over `sprk_communication` saved views,
 *     driving `EmailCardList` (030) via the task-031 `useEmailViews` hook.
 *   - Right pane: `EmailReadingPaneShell` (032) with all five `render*` slots
 *     filled —
 *       renderHeader      → `EmailReadingHeader` (034) + `OpenFullFormButton`
 *                            (036, FR-15) rendered directly above it, near
 *                            the shell's own full-width toolbar.
 *       renderBody        → `EmailBodyView` (033), fed `sprk_body` + the
 *                            resolved `.eml` archive document id from this
 *                            component's own per-selection record read.
 *       renderAttachments → `EmailReadingAttachments` (034).
 *       renderConnections → `EmailConnectionsReview` (035), fed the
 *                            association state from the same per-selection
 *                            read; `onAssociationsChanged` re-reads it.
 *       renderTracking    → `EmailTrackingPanel` (035), fed tracking values
 *                            + write callbacks from the same read.
 *     actions             → `useEmailComposeActions` (036) — Reply/Reply
 *                            All/Forward/New open the ONE canonical
 *                            `SendEmailDialog`/`EmailComposer`, never forked.
 *
 * This component owns the ONE per-selection Dataverse read shared by the
 * connections/tracking/body slots (`useEmailWorkspaceRecord`) — the header
 * and attachments sub-views do their OWN internal reads (task 034's existing
 * design), so nothing here duplicates or forks that behavior.
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
import { makeStyles, tokens, Toolbar } from '@fluentui/react-components';
import { EmailViewSelector, useEmailViews } from '../EmailViewSelector';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import { EmailBodyView } from '../EmailBody';
import { EmailReadingHeader, EmailReadingAttachments } from '../EmailReadingHeader';
import { EmailConnectionsReview, EmailTrackingPanel } from '../EmailAssociationsAndTracking';
import { useEmailComposeActions, OpenFullFormButton } from '../EmailComposeActions';
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
  headerRow: {
    display: 'flex',
    flexDirection: 'column',
  },
  openFullFormRow: {
    display: 'flex',
    justifyContent: 'flex-end',
    paddingInline: tokens.spacingHorizontalXL,
    paddingTop: tokens.spacingVerticalXS,
  },
});

/** Header slot content: `OpenFullFormButton` (FR-15) rendered near the shell's toolbar, directly above `EmailReadingHeader`. */
const EmailWorkspaceHeaderRow: React.FC<{
  selectedId: string;
  dataService: EmailWorkspaceProps['dataService'];
  onOpenFullForm: (communicationId: string) => Promise<void>;
}> = ({ selectedId, dataService, onOpenFullForm }) => {
  const s = useStyles();
  return (
    <div className={s.headerRow}>
      <div className={s.openFullFormRow}>
        {/* `OpenFullFormButton` wraps a Fluent `ToolbarButton` (task 036) — hosting
            it inside its own single-button `Toolbar` matches Fluent's expected
            parent context (the shell's OWN `EmailToolbar` is a separate, sibling
            `Toolbar` instance; task 036's component is intentionally NOT edited
            here, per this task's "consume, don't modify" guardrail). */}
        <Toolbar aria-label="Record actions">
          <OpenFullFormButton communicationId={selectedId} onOpenFullForm={onOpenFullForm} />
        </Toolbar>
      </div>
      <EmailReadingHeader selectedId={selectedId} dataService={dataService} />
    </div>
  );
};
EmailWorkspaceHeaderRow.displayName = 'EmailWorkspaceHeaderRow';

export const EmailWorkspace: React.FC<EmailWorkspaceProps> = ({
  dataverseClient,
  dataService,
  navigationService,
  webApi,
  authenticatedFetch,
  bffBaseUrl,
  accessPermissionOptions = DEFAULT_ACCESS_PERMISSION_OPTIONS,
  onSearchRecipients,
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

  // Compose/reply/forward/new + "Open full form" (036, FR-09/FR-10/FR-15) —
  // mounts the ONE canonical `SendEmailDialog`/`EmailComposer`, never forked.
  const { actions, composerDialog, openFullForm } = useEmailComposeActions({
    authenticatedFetch,
    bffBaseUrl,
    dataService,
    navigationService,
    onSearchRecipients,
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
            <EmailWorkspaceHeaderRow selectedId={id} dataService={dataService} onOpenFullForm={openFullForm} />
          )}
          renderBody={id => (
            <EmailBodyView
              selectedId={id}
              emlDocumentId={record.emlDocumentId}
              body={record.recordState?.sprk_body ?? ''}
              recordLoadError={record.recordLoadError}
              onRetryRecord={record.retry}
              authenticatedFetch={authenticatedFetch}
            />
          )}
          renderAttachments={id => (
            <EmailReadingAttachments
              selectedId={id}
              dataService={dataService}
              navigation={navigationService}
              apiBaseUrl={bffBaseUrl}
            />
          )}
          renderConnections={id => (
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
          )}
          renderTracking={() => (
            <EmailTrackingPanel
              monitor={record.recordState?.monitor ?? false}
              highPriority={record.recordState?.highPriority ?? false}
              accessPermission={record.recordState?.accessPermission ?? null}
              accessPermissionOptions={accessPermissionOptions}
              onMonitorChange={record.updateMonitor}
              onHighPriorityChange={record.updateHighPriority}
              onAccessPermissionChange={record.updateAccessPermission}
            />
          )}
        />
      </div>

      {composerDialog}
    </div>
  );
};

EmailWorkspace.displayName = 'EmailWorkspace';

export default EmailWorkspace;
