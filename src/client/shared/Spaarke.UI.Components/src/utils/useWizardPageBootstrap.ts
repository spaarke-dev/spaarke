/**
 * useWizardPageBootstrap
 * ----------------------
 * Shared bootstrap for the standalone wizard **Code Pages** (Create Event /
 * Invoice / Report Card, and future wizards launched via `navigateTo`).
 *
 * Extracts the auth + service-adapter + theme wiring that every wizard page
 * needs so the pages don't triplicate ~80 lines each. Behavior is identical to
 * the original `CreateEventWizard/src/main.tsx` bootstrap (visual-host-version-
 * update VHVU-010) — this is a DRY extraction, not new logic.
 *
 * Auth is bootstrapped exclusively through `@spaarke/auth` (ADR-028):
 * `resolveRuntimeConfig()` → `initAuth()` → `authenticatedFetch`. No MSAL is
 * instantiated here and no token is ever handled directly.
 *
 * Each page renders a loading state until `isAuthReady`, then mounts its wizard
 * passing the subset of the returned services that its wizard accepts.
 */
import * as React from 'react';
import { resolveCodePageTheme, setupCodePageThemeListener } from './themeStorage';
import { parseDataParams } from './parseDataParams';
import { createXrmDataService } from './adapters/xrmDataServiceAdapter';
import { createXrmUploadService } from './adapters/xrmUploadServiceAdapter';
import { createXrmNavigationService } from './adapters/xrmNavigationServiceAdapter';
import { resolveRuntimeConfig, initAuth, authenticatedFetch } from '@spaarke/auth';
import { cleanGuid } from '../services/PolymorphicResolverService';
import type { AssociationResult } from '../components/AssociateToStep/types';
import {
  readHandoffFromUrl,
  handoffSeed as computeHandoffSeed,
  completeHandoff as writeCompleteHandoff,
  type HandoffContext,
  type HandoffSeed,
} from '../services/surfaceHandoff';

export interface IWizardPageBootstrap {
  /** Resolved Fluent theme (light/dark) for the page's FluentProvider. */
  theme: ReturnType<typeof resolveCodePageTheme>;
  /** Parsed navigate `data` envelope params (bffBaseUrl, entityType, entityId, …). */
  params: ReturnType<typeof parseDataParams>;
  /** False until `@spaarke/auth` finishes initializing (page shows a spinner). */
  isAuthReady: boolean;
  /** BFF base URL resolved from runtime config (falls back to the `data` param). */
  bffBaseUrl: string;
  /** AAD tenant id (forwarded to the file-indexing pipeline; '' when unknown). */
  tenantId: string;
  dataService: ReturnType<typeof createXrmDataService>;
  uploadService: ReturnType<typeof createXrmUploadService>;
  navigationService: ReturnType<typeof createXrmNavigationService>;
  /** The `@spaarke/auth` authenticated fetch (bearer + 401-retry). */
  authenticatedFetch: typeof authenticatedFetch;
  /** Resolves the current user's business-unit SPE container id for uploads. */
  resolveSpeContainerId: () => Promise<string>;
  /** Closes the host `navigateTo` dialog, signalling success so the form refreshes. */
  closeDialog: () => void;
  /**
   * Pre-filled regarding association built from the launch envelope
   * (`entityType`+`entityId`), when the page was opened from a host record
   * (e.g. VisualHost's "+"). `undefined` when opened standalone.
   */
  initialAssociation?: AssociationResult;
  /**
   * `true` when `initialAssociation` is present — hides the Associate-To step
   * and pins the host record as the parent (per CreateRecordWizard config).
   */
  lockAssociation: boolean;
  /**
   * Assistant → surface hand-off context (task 012), read from THIS page's own
   * launch URL (`data=handoffId=…`). `null` when the page was opened directly
   * (not via a `surface_launch` hand-off). Present when the Assistant launched
   * this wizard pre-seeded.
   */
  handoff: HandoffContext | null;
  /**
   * The typed pre-seed payload (draftValues + resolvedLookups + fileIds), or
   * `null` when there is nothing to seed. TASK 013 maps this onto the specific
   * wizard's initial form values + resolved-dropdown pre-selects — this bootstrap
   * only surfaces the seed; the per-wizard field mapping is 013 scope.
   */
  handoffSeed: HandoffSeed | null;
  /**
   * Write the COMMITTED outcome for the active hand-off (a real record was
   * created), then close the dialog. No-op (just closes) when there is no
   * hand-off. TASK 013 wires this into each wizard's success/onFinish callback
   * with the created record id; the Assistant gates its honest "✅ Created …"
   * claim on the written outcome (P5 / task 020). Cancellation is INFERRED by
   * the orchestrator from the absence of a written result — so a plain
   * `closeDialog` (cancel) needs no result write here.
   */
  completeHandoff: (recordId?: string) => void;
}

/**
 * @param logPrefix short tag for console diagnostics, e.g. `"CreateInvoiceWizard"`.
 */
export function useWizardPageBootstrap(logPrefix: string): IWizardPageBootstrap {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);
  const [isAuthReady, setIsAuthReady] = React.useState(false);
  const [bffBaseUrl, setBffBaseUrl] = React.useState<string>(params.bffBaseUrl || '');
  const [tenantId, setTenantId] = React.useState<string>('');

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    async function initialize(): Promise<void> {
      try {
        const config = await resolveRuntimeConfig();
        await initAuth({
          clientId: config.msalClientId,
          bffBaseUrl: config.bffBaseUrl,
          bffApiScope: config.bffOAuthScope,
          tenantId: config.tenantId || undefined,
          proactiveRefresh: true,
        });
        if (!cancelled) {
          setBffBaseUrl(config.bffBaseUrl);
          setTenantId(config.tenantId ?? '');
          setIsAuthReady(true);
        }
      } catch (err) {
        console.error(`[${logPrefix}] Failed to initialize auth:`, err);
        // Degrade gracefully: still mount the wizard; BFF-backed features will
        // 401 rather than leaving the page stuck on the spinner forever.
        if (!cancelled) setIsAuthReady(true);
      }
    }
    void initialize();
    return () => {
      cancelled = true;
    };
  }, [logPrefix]);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const uploadService = React.useMemo(() => createXrmUploadService(bffBaseUrl), [bffBaseUrl]);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  // Assistant → surface hand-off (task 012). Read once from this page's own
  // launch URL; hydrate the envelope from sessionStorage. `null` when opened
  // directly (no `handoffId` on the URL).
  const handoff = React.useMemo(() => readHandoffFromUrl(), []);
  const handoffSeed = React.useMemo(() => computeHandoffSeed(handoff), [handoff]);

  const closeDialog = React.useCallback(() => {
    navigationService.closeDialog({ confirmed: true });
  }, [navigationService]);

  // Write the committed outcome (P5 honest ack) then close. Cancellation is
  // inferred by the orchestrator from the absence of a written result, so a bare
  // `closeDialog` is the correct "user cancelled" path.
  const completeHandoff = React.useCallback(
    (recordId?: string) => {
      if (handoff?.handoffId) {
        writeCompleteHandoff(handoff.handoffId, recordId);
      }
      navigationService.closeDialog({ confirmed: true });
    },
    [handoff, navigationService]
  );

  // Build the pre-filled association from the launch envelope. The entityId is
  // an Xrm-sourced GUID, so normalize it with cleanGuid at ingestion (ADR-044 —
  // bare + lowercase; no-op on already-canonical input) so no braced/registry-
  // format GUID propagates into the wizard's create payloads.
  const initialAssociation = React.useMemo<AssociationResult | undefined>(() => {
    const entityType = params.entityType;
    const entityId = params.entityId;
    if (!entityType || !entityId) return undefined;
    return { entityType, recordId: cleanGuid(entityId), recordName: params.recordName ?? '' };
  }, [params]);
  const lockAssociation = initialAssociation !== undefined;

  const resolveSpeContainerId = React.useCallback(async (): Promise<string> => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = (window as any).Xrm ?? (window.parent as any)?.Xrm ?? (window.top as any)?.Xrm;
    if (!xrm?.WebApi?.retrieveRecord) throw new Error('Xrm.WebApi not available');
    const userId = xrm.Utility.getGlobalContext().userSettings.userId.replace(/[{}]/g, '');
    const user = await xrm.WebApi.retrieveRecord('systemuser', userId, '?$select=_businessunitid_value');
    const buId = user['_businessunitid_value'] as string;
    if (!buId) throw new Error('Could not resolve business unit');
    const bu = await xrm.WebApi.retrieveRecord('businessunit', buId, '?$select=sprk_containerid');
    return (bu['sprk_containerid'] as string) || '';
  }, []);

  return {
    theme,
    params,
    isAuthReady,
    bffBaseUrl,
    tenantId,
    dataService,
    uploadService,
    navigationService,
    authenticatedFetch,
    resolveSpeContainerId,
    closeDialog,
    initialAssociation,
    lockAssociation,
    handoff,
    handoffSeed,
    completeHandoff,
  };
}
