/**
 * EmailWorkspaceWidget.tsx
 *
 * Direct-widget mount for the shared `EmailWorkspace` composition root
 * (email-communication-solution-r5 task 040/041, spec FR-01 / NFR-06 /
 * Success Criterion 1). Registered as the SpaarkeAi workspace widget type
 * `email` in `register-workspace-widgets.ts`. Pattern D dual-use with the
 * `email` LegalWorkspace section (`email.registration.ts`) — BOTH mounts
 * render the SAME `EmailWorkspace` from `@spaarke/communication-components`
 * unchanged; only the host-adapter resolution differs per mount (this file
 * for the SpaarkeAi direct widget, `email.registration.ts` for the
 * LegalWorkspace section factory context).
 *
 * `EmailWorkspace` is host-agnostic (ADR-012) and declares its dependencies
 * as plain props (`dataverseClient` / `dataService` / `navigationService` /
 * `webApi` / `authenticatedFetch` / `bffBaseUrl`) rather than resolving them
 * itself — see `EmailWorkspace.types.ts` docblock ("the assembling mount
 * resolves the concrete Xrm-backed or BFF-backed adapters ... `EmailWorkspace`
 * never knows which host it runs under"). This file's ONLY job is that
 * resolution:
 *   - `dataverseClient` / `dataService` / `navigationService` — Xrm-backed
 *     adapters from `@spaarke/ui-components` (same `XrmDataverseClient` +
 *     `createXrmDataService`/`createXrmNavigationService` factories
 *     `DataGrid.tsx` / `documents.registration.ts`-family sections use).
 *   - `webApi` — the raw `Xrm.WebApi` bridge (same Xrm.WebApi-compatible
 *     shape PCF hosts pass as `context.webAPI`; see
 *     `IResolverWriteContext` docblock in `@spaarke/communication-components/logic/connections`).
 *   - `authenticatedFetch` / `bffBaseUrl` — from `useAiSession()` (this
 *     widget only ever mounts inside SpaarkeAi's `AiSessionProvider` tree,
 *     unlike the dual-registered `DataverseEntityViewWidget`, which also
 *     mounts standalone in LegalWorkspace and therefore uses an optional
 *     context read instead).
 *
 * ADR-039 / BFF §10 (Path C): surface identity stays in CODE — this file
 * introduces no server-side surface-identity endpoint or record.
 * ADR-022: React 19, NOT PCF-safe.
 */
import * as React from 'react';
import { makeStyles } from '@fluentui/react-components';
import { EmailWorkspace } from '@spaarke/communication-components';
import type { EmailWorkspaceVisibleState } from '@spaarke/communication-components';
import {
  XrmDataverseClient,
  createXrmDataService,
  createXrmNavigationService,
  createXrmEmailComposeHandlers,
  resolveCurrentUserEmail,
  searchUsersAndContacts,
  getXrm,
} from '@spaarke/ui-components';
import { useAiSession } from '../../providers/useAiSession';
import type { WorkspaceWidgetProps } from '../../types/widget-types';
import type { EmailTabWidgetData } from '../../types/WorkspaceTab';

// Bounded-height host for the DIRECT widget mount (owner UAT 2026-08-03 R5 item 1).
// ROOT CAUSE (DevTools showed the widget rendering 752×8209px): the SpaarkeAi tab/section
// content area is CONTENT-DRIVEN (WorkspaceShell rows are deliberately NOT height:100%), so
// a plain `height:100%` COLLAPSES TO AUTO and the widget grows to fit ALL its content — the
// inner list/reading-pane `overflowY:auto` never engages and the whole surface scrolls as one.
// The fix is the established full-height-widget pattern used by the Messages widget
// (`CommunicationsWorkspaceWidget`) and SmartTodo: declare an explicit viewport-based height
// FLOOR + matching CAP (`calc(100vh - 200px)` ≈ app header + tab bar + section chrome), which
// pins the widget to a DEFINITE box regardless of the content-driven parent. `height:100%`
// still wins when a host DOES constrain height (bounded tile). With a definite box,
// `EmailWorkspace.root` resolves and its two panes scroll INDEPENDENTLY.
const useStyles = makeStyles({
  host: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minWidth: 0,
    minHeight: 'calc(100vh - 200px)',
    maxHeight: 'calc(100vh - 200px)',
    overflow: 'hidden',
  },
});

/**
 * Direct workspace widget wrapper for `<EmailWorkspace />`.
 *
 * `data` / `isLoading` / `error` / `className` / `isActiveTab` from
 * `WorkspaceWidgetProps` are intentionally unused — the Email surface is not
 * AI-directed data; it drives its own Dataverse reads via `useEmailViews` /
 * `useEmailWorkspaceRecord` (tasks 031/040) once mounted with host adapters.
 *
 * `tabId` + `onDataChange` ARE used (task 042b, FR-C1 "Path 1: persisted Email
 * carrier"): when this widget is mounted AS a workspace TAB, the selected
 * email's compact shape is persisted into the tab's `WorkspaceTab.widgetData`
 * as an `EmailTabWidgetData` — see `handleVisibleEmailChange` below.
 */
export const EmailWorkspaceWidget: React.FC<WorkspaceWidgetProps> = ({ tabId, onDataChange }) => {
  const styles = useStyles();
  const { authenticatedFetch, bffBaseUrl } = useAiSession();

  // FR-C1 (task 042b): persist the selected email's COMPACT shape into this
  // tab's `WorkspaceTab.widgetData`. This rides the EXISTING widget self-update
  // seam (`WorkspaceWidgetProps.onDataChange` → `widget_update` PaneEventBus
  // event → `WorkspaceTabManager.updateTab` → `PATCH /tabs` write-through, the
  // task-025 AnalysisEditor persistence path) — NO new persistence mechanism.
  // Both the server `TryDeriveVisibleState` and the client registry
  // `getVisibleState('email')` then read the Email shape from `widgetData`.
  // `emlDocumentId` is persisted as an on-demand `eml-render` fetch handle
  // (FR-C4); it is deliberately excluded from the agent-visible derivation.
  //
  // spaarkeai-assistant-enhancements-r3 task 012 (FR-05) walk-back:
  //   1. `snippet` (the sole CONTENT-bearing field, R2 FR-C1) is now OMITTED —
  //      the Assistant-visible persisted carrier is id/label ONLY (ADR-015).
  //   2. The patch additionally carries a TRANSIENT `communicationId` field —
  //      NOT part of `EmailTabWidgetData` / the BFF contract. This widget lives
  //      in the shared `Spaarke.AI.Widgets` package and per ADR-012 MUST NOT
  //      import the SpaarkeAi-solution active-item conduit directly, so it rides
  //      this EXISTING `onDataChange` emit instead (§11 — redirect, not a new
  //      selection model). The SpaarkeAi host (`WorkspacePane.handleTabDataChange`
  //      → `deriveEmailActiveItemFromPatch`) reads `communicationId` to publish
  //      the conduit id handle, then STRIPS it before persisting — the field
  //      never reaches the server / the `EmailTabWidgetData` shape on the wire.
  //   3. On deselect (`state === null`) the widget now ALSO signals
  //      `{ communicationId: null }` (clear-only — no `kind`/subject/etc.) so
  //      the host clears the active-item conduit WITHOUT clobbering the last
  //      persisted Email carrier (same "leave it intact" contract as before).
  //
  // Gated on `tabId` + `onDataChange` so the population fires ONLY for the tab
  // mount (both are supplied by the host tab manager); the standalone code-page
  // mount and isolated renders supply neither → no-op.
  const handleVisibleEmailChange = React.useCallback(
    (state: EmailWorkspaceVisibleState | null): void => {
      if (!onDataChange || !tabId) return;
      if (!state) {
        // Deselect — active-item clear signal ONLY; the host strips this before any
        // persisted-widgetData merge (see docblock above).
        onDataChange({ communicationId: null });
        return;
      }
      const widgetData: EmailTabWidgetData & { communicationId: string } = {
        kind: 'Email',
        emlDocumentId: state.emlDocumentId,
        subject: state.subject,
        from: state.from,
        date: state.date,
        ...(state.threadId ? { threadId: state.threadId } : {}),
        // `snippet` (content) intentionally OMITTED — task 012 walk-back of the R2 FR-C1 carrier.
        communicationId: state.communicationId,
      };
      onDataChange(widgetData);
    },
    [onDataChange, tabId]
  );

  // Stable across the widget's lifetime — one Xrm-backed adapter set per
  // mount, matching `XrmDataverseClient`'s own "instantiate once" guidance
  // (see DataGrid.tsx's defaultClientRef pattern).
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  const dataService = React.useMemo(() => createXrmDataService(), []);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);
  // Xrm.WebApi-compatible bridge for the associations additive-write path
  // (035) + the embedded PolymorphicPicker (035) — the same object shape a
  // PCF host passes as `context.webAPI`.
  const webApi = React.useMemo(() => getXrm()?.WebApi, []);

  // Composer parity wiring (compose-wiring fixes #1/#2/#3/#5): recipient
  // typeahead + Xrm-backed advanced-lookup handlers + Dataverse URL for
  // attachment deep-links. Same shared factory the code-page mount uses, so both
  // mounts stay in parity (NFR-06).
  const handleSearchRecipients = React.useCallback(
    (query: string) => searchUsersAndContacts(dataService, query),
    [dataService]
  );
  // Pass auth + BFF URL so the factory also builds `onUploadLocalAttachment` (item 9b):
  // new-file attachments upload to the deployment SPE container (resolved from the
  // user's BU) and become governed `sprk_document`s.
  const composeHandlers = React.useMemo(
    () => createXrmEmailComposeHandlers({ authenticatedFetch, bffBaseUrl: bffBaseUrl ?? undefined }),
    [authenticatedFetch, bffBaseUrl]
  );
  const dataverseUrl = React.useMemo(() => getXrm()?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '', []);

  // Signed-in user's mailbox address for the compose "From:" row (item 3). Resolved once
  // via Xrm; the email surface defaults From to send-as this user (switchable to shared).
  const [fromMailbox, setFromMailbox] = React.useState<string | undefined>();
  React.useEffect(() => {
    let cancelled = false;
    void resolveCurrentUserEmail().then(email => {
      if (!cancelled) setFromMailbox(email);
    });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!webApi) {
    // No Dataverse host available (e.g. a non-MDA dev shell). EmailWorkspace's
    // required host props cannot be resolved — fail closed rather than mount
    // a partially-wired component. Mirrors `XrmDataverseClient`'s own
    // throw-at-first-call contract for non-MDA hosts.
    return null;
  }

  return (
    <div className={styles.host} data-testid="email-widget-scroll-host">
      <EmailWorkspace
        dataverseClient={dataverseClient}
        dataService={dataService}
        navigationService={navigationService}
        webApi={webApi}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={bffBaseUrl}
        onSearchRecipients={handleSearchRecipients}
        onLookupRecipients={composeHandlers.onLookupRecipients}
        recordLookupCatalog={composeHandlers.recordLookupCatalog}
        onLookupRecord={composeHandlers.onLookupRecord}
        onAddRelationship={composeHandlers.onAddRelationship}
        onUploadLocalAttachment={composeHandlers.onUploadLocalAttachment}
        onResolveShareLink={composeHandlers.onResolveShareLink}
        onListEmailTemplates={composeHandlers.onListEmailTemplates}
        onRenderEmailTemplate={composeHandlers.onRenderEmailTemplate}
        onDraftWithAi={composeHandlers.onDraftWithAi}
        fromMailbox={fromMailbox}
        dataverseUrl={dataverseUrl}
        onVisibleEmailChange={handleVisibleEmailChange}
      />
    </div>
  );
};

export default EmailWorkspaceWidget;
