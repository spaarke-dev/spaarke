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
import { EmailWorkspace } from '@spaarke/communication-components';
import { XrmDataverseClient, createXrmDataService, createXrmNavigationService, getXrm } from '@spaarke/ui-components';
import { useAiSession } from '../../providers/useAiSession';
import type { WorkspaceWidgetProps } from '../../types/widget-types';

/**
 * Direct workspace widget wrapper for `<EmailWorkspace />`.
 *
 * `data` / `isLoading` / `error` / `className` / `tabId` / `isActiveTab` from
 * `WorkspaceWidgetProps` are intentionally unused — the Email surface is not
 * AI-directed data; it drives its own Dataverse reads via `useEmailViews` /
 * `useEmailWorkspaceRecord` (tasks 031/040) once mounted with host adapters.
 */
export const EmailWorkspaceWidget: React.FC<WorkspaceWidgetProps> = () => {
  const { authenticatedFetch, bffBaseUrl } = useAiSession();

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

  if (!webApi) {
    // No Dataverse host available (e.g. a non-MDA dev shell). EmailWorkspace's
    // required host props cannot be resolved — fail closed rather than mount
    // a partially-wired component. Mirrors `XrmDataverseClient`'s own
    // throw-at-first-call contract for non-MDA hosts.
    return null;
  }

  return (
    <EmailWorkspace
      dataverseClient={dataverseClient}
      dataService={dataService}
      navigationService={navigationService}
      webApi={webApi}
      authenticatedFetch={authenticatedFetch}
      bffBaseUrl={bffBaseUrl}
    />
  );
};

export default EmailWorkspaceWidget;
