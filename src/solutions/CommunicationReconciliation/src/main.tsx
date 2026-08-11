/**
 * Communication Reconciliation Code Page entry point
 * (email-communication-intelligence-r2 task 062, Pillar E).
 *
 * Mirrors `src/solutions/EmailPage/src/main.tsx`: `createRoot` + host
 * `FluentProvider` (theme via `resolveCodePageTheme` /
 * `setupCodePageThemeListener`) + `AppErrorBoundary` + an async
 * `bootstrapAuth()` (`resolveRuntimeConfig` → `setRuntimeConfig` →
 * `ensureAuthInitialized`). This is mount #1 of the dual-mount Pillar E
 * reconciliation surface — mount #2 is the SpaarkeAi `communications-reconciliation`
 * workspace widget (`ReconciliationWorkspaceWidget.tsx`). BOTH mounts render the
 * SAME shared `ReconciliationWorkspace` from `@spaarke/communication-components`
 * unchanged (dual-mount parity); this file's only job is to bootstrap auth/theme
 * and supply the host-specific (Xrm-backed) adapters `ReconciliationWorkspace`
 * expects as props (ADR-012 — the shared component stays Xrm-agnostic).
 *
 * Host adapters resolved here:
 *   - `dataverseClient` — `XrmDataverseClient` (ADR-012), the real client the
 *     embedded reconciliation grid uses within the authenticated MDA session.
 *   - `authenticatedFetch` — from `services/authInit` (ADR-028); threaded into
 *     the browse reader's `.eml` render + the Fields/Tasks tabs' queue-feed and
 *     apply/dismiss BFF calls. No raw Bearer.
 *   - `resolveReview(row)` — the row's `EmailConnectionsReview` props (the reused
 *     ADR-024 single write path), built exactly like `EmailWorkspace`'s inline
 *     `EmailConnectionsReview` wiring: an Xrm.WebApi-backed `writeContext` +
 *     `pickerWebApi` (`EmailWorkspaceWebApi = IResolverWriteContext['webApi'] &
 *     IPolymorphicPickerWebApi`).
 *   - `resolveRegarding(row)` — the row's CONFIRMED regarding (NFR-10 gate),
 *     derived by the SHIPPED pure `derivePrimaryReview` over the row's
 *     `sprk_associationprovenance` + `sprk_associationstatus` + denorm regarding
 *     fields (no bespoke fetch — the same reducer the resolver uses).
 *
 * `configId` is intentionally NOT passed — `ReconciliationGrid` defaults to its
 * `NEEDS_REVIEW_CONFIG_ID` placeholder, which task 059 seeds + sets. Fail-closed
 * (NFR-07): the workspace mounts only after `bootstrapAuth()` resolves; on
 * failure it shows a retry state.
 *
 * Web resource: sprk_communicationreconciliation (see package.json build rename).
 */

import * as React from 'react';
import { createRoot } from 'react-dom/client';
import { FluentProvider, Spinner, Text, Button, makeStyles, tokens } from '@fluentui/react-components';
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
  AppErrorBoundary,
  AppInsightsService,
  XrmDataverseClient,
  getXrm,
} from '@spaarke/ui-components';
import { resolveRuntimeConfig, getAuthProvider } from '@spaarke/auth';
import {
  ReconciliationWorkspace,
  derivePrimaryReview,
  type EmailConnectionsReviewProps,
  type ReconcileRegarding,
  type EmailWorkspaceWebApi,
} from '@spaarke/communication-components';
import { setRuntimeConfig } from './config/runtimeConfig';
import { ensureAuthInitialized, authenticatedFetch } from './services/authInit';

// Initialize Application Insights so AppErrorBoundary.componentDidCatch can route
// errors to the "Failures" pane via reportClientError(). Key sourced from a
// build-time Vite env var; absent in dev → no-op. Override: VITE_APP_INSIGHTS_KEY=<key>
const _appInsightsKey: string = import.meta.env.VITE_APP_INSIGHTS_KEY ?? '';
if (_appInsightsKey) {
  AppInsightsService.initialize(_appInsightsKey);
}

const COMMUNICATION_ENTITY = 'sprk_communication';
const PRIMARY_ID_FIELD = 'sprk_communicationid';

const useStyles = makeStyles({
  centered: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    width: '100%',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalXXL,
    boxSizing: 'border-box',
  },
});

/** Read a string field off an untyped grid row. */
function str(record: Record<string, unknown>, field: string): string | null {
  const v = record[field];
  return typeof v === 'string' ? v : v == null ? null : String(v);
}

/** Read a numeric field off an untyped grid row (option-set value). */
function num(record: Record<string, unknown>, field: string): number | null {
  const v = record[field];
  if (typeof v === 'number') return v;
  if (v == null || v === '') return null;
  const n = Number(v);
  return Number.isNaN(n) ? null : n;
}

/**
 * Builds the `EmailWorkspaceWebApi` bridge from `Xrm.WebApi` — lazily resolved
 * at call time (mirrors EmailPage's `buildXrmWebApi`) so construction never
 * throws even before Xrm is available; only individual calls do, if Xrm truly
 * never becomes available. This single object satisfies BOTH
 * `IResolverWriteContext['webApi']` (the additive regarding write) AND
 * `IPolymorphicPickerWebApi` (the embedded "Change / Link another" picker).
 */
function buildXrmWebApi(): EmailWorkspaceWebApi {
  function requireXrmWebApi() {
    const xrm = getXrm();
    if (!xrm?.WebApi) {
      throw new Error(
        'Xrm.WebApi is not available. The Reconciliation code page requires a Dataverse-hosted context.'
      );
    }
    return xrm.WebApi;
  }

  return {
    retrieveMultipleRecords: (entityLogicalName: string, query?: string, maxPageSize?: number) =>
      requireXrmWebApi().retrieveMultipleRecords(entityLogicalName, query, maxPageSize),
    retrieveRecord: (entityLogicalName: string, id: string, options?: string) =>
      requireXrmWebApi().retrieveRecord(entityLogicalName, id, options),
    updateRecord: (entityLogicalName: string, id: string, data: Record<string, unknown>) =>
      requireXrmWebApi().updateRecord(entityLogicalName, id, data),
  };
}

/**
 * Row → `EmailConnectionsReview` props (052 Related-to picker). Built exactly
 * like `EmailWorkspace`'s inline `EmailConnectionsReview` wiring — the reused
 * ADR-024 single write path. `webApi` is the Xrm.WebApi-backed bridge; both the
 * additive `writeContext` and the `pickerWebApi` point at it.
 */
function buildResolveReview(
  webApi: EmailWorkspaceWebApi,
  onChanged: () => void
): (record: Record<string, unknown>) => EmailConnectionsReviewProps {
  return record => {
    const id = str(record, PRIMARY_ID_FIELD) ?? '';
    return {
      communicationId: id,
      associationStatus: num(record, 'sprk_associationstatus'),
      associationProvenanceJson: str(record, 'sprk_associationprovenance'),
      regardingRecordName: str(record, 'sprk_regardingrecordname'),
      regardingRecordNumber: str(record, 'sprk_regardingrecordnumber'),
      regardingRecordType: str(record, 'sprk_regardingrecordtypename'),
      writeContext: {
        webApi,
        hostEntity: COMMUNICATION_ENTITY,
        hostRecordId: id,
      },
      pickerWebApi: webApi,
      onAssociationsChanged: onChanged,
    };
  };
}

/**
 * Row → CONFIRMED `ReconcileRegarding | null` (NFR-10 gate). Reuses the shipped
 * pure `derivePrimaryReview` reducer over the row's provenance JSON +
 * association status + denorm regarding fields — the SAME logic the resolver
 * body uses to decide the single confirmed primary. Only a green (Resolved)
 * primary with a resolvable typed entity + id enables the Fields/Tasks tabs;
 * anything else stays gated.
 */
function resolveRegarding(record: Record<string, unknown>): ReconcileRegarding | null {
  const model = derivePrimaryReview(
    str(record, 'sprk_associationprovenance'),
    num(record, 'sprk_associationstatus'),
    undefined,
    {
      recordName: str(record, 'sprk_regardingrecordname'),
      recordNumber: str(record, 'sprk_regardingrecordnumber'),
      entityType: null,
      recordTypeLabel: str(record, 'sprk_regardingrecordtypename'),
    }
  );
  const primary = model.state === 'confirmed' ? model.primary : undefined;
  if (primary && primary.entity && primary.targetId) {
    return { entityType: primary.entity, recordId: primary.targetId };
  }
  return null;
}

/**
 * Bootstrap auth (config + MSAL + tenant ID). `ReconciliationWorkspace` waits on
 * this promise before mounting (see `Root`) so no reconcile content renders
 * without a resolved auth context (NFR-07).
 */
async function bootstrapAuth(): Promise<void> {
  const config = await resolveRuntimeConfig();
  setRuntimeConfig(config);
  await ensureAuthInitialized();

  if (!config.tenantId) {
    const tenantId = await getAuthProvider().getTenantId();
    if (tenantId) {
      setRuntimeConfig({ ...config, tenantId });
    }
  }
}

/** Loading state shown while `bootstrapAuth()` resolves config + MSAL init. */
const ReconciliationLoading: React.FC = () => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="reconciliation-page-loading">
      <Spinner size="large" label="Loading Reconciliation…" />
    </div>
  );
};
ReconciliationLoading.displayName = 'ReconciliationLoading';

/**
 * Fail-closed error state (NFR-07). Rendered instead of `ReconciliationWorkspace`
 * when `bootstrapAuth()` rejects — e.g. no resolvable auth context.
 */
const ReconciliationAuthError: React.FC<{ onRetry: () => void }> = ({ onRetry }) => {
  const s = useStyles();
  return (
    <div className={s.centered} data-testid="reconciliation-page-auth-error">
      <Text weight="semibold" size={500}>
        Unable to load Reconciliation
      </Text>
      <Text>Sign-in could not be verified for this session. Retry, or reopen this page from Spaarke navigation.</Text>
      <Button appearance="primary" onClick={onRetry}>
        Retry
      </Button>
    </div>
  );
};
ReconciliationAuthError.displayName = 'ReconciliationAuthError';

function Root() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const [ready, setReady] = React.useState(false);
  const [bootstrapError, setBootstrapError] = React.useState<unknown>(null);
  const [attempt, setAttempt] = React.useState(0);
  // Bumped after an association is confirmed so the grid re-loads and
  // `resolveRegarding` reflects the new association (the prop's documented
  // "host refreshes the grid" contract). Used as a React `key` remount signal.
  const [refreshKey, setRefreshKey] = React.useState(0);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    setBootstrapError(null);
    setReady(false);
    bootstrapAuth()
      .then(() => {
        if (!cancelled) setReady(true);
      })
      .catch(err => {
        console.warn('[CommunicationReconciliation] Auth bootstrap failed — workspace unavailable:', err);
        if (!cancelled) setBootstrapError(err);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attempt]);

  // Host-specific (Xrm-backed) adapters `ReconciliationWorkspace` expects as
  // props (ADR-012 — the shared component stays Xrm-agnostic; this mount
  // resolves the concrete implementations).
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  const webApi = React.useMemo(() => buildXrmWebApi(), []);
  const handleAssociationsChanged = React.useCallback(() => setRefreshKey(n => n + 1), []);
  const resolveReview = React.useMemo(
    () => buildResolveReview(webApi, handleAssociationsChanged),
    [webApi, handleAssociationsChanged]
  );

  const handleRetry = React.useCallback(() => setAttempt(n => n + 1), []);

  let body: React.ReactNode;
  if (bootstrapError) {
    body = <ReconciliationAuthError onRetry={handleRetry} />;
  } else if (!ready) {
    body = <ReconciliationLoading />;
  } else {
    body = (
      // configId omitted → ReconciliationGrid uses its NEEDS_REVIEW_CONFIG_ID
      // placeholder (task 059 seeds the real gridconfiguration record + sets it).
      <ReconciliationWorkspace
        key={refreshKey}
        dataverseClient={dataverseClient}
        authenticatedFetch={authenticatedFetch}
        resolveReview={resolveReview}
        resolveRegarding={resolveRegarding}
        onAssociationsChanged={handleAssociationsChanged}
      />
    );
  }

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <AppErrorBoundary surfaceName="Reconciliation">{body}</AppErrorBoundary>
    </FluentProvider>
  );
}

const rootElement = document.getElementById('root');
if (rootElement) {
  createRoot(rootElement).render(
    <React.StrictMode>
      <Root />
    </React.StrictMode>
  );
} else {
  // eslint-disable-next-line no-console
  console.error('[CommunicationReconciliation] Root element not found');
}
