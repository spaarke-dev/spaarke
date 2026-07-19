/**
 * CommunicationTimelineRegardingApp — form-bound viewer mounting the shared
 * `<CommunicationTimeline/>` component in **regarding mode** (task 020) on
 * the 11 ADR-024 regarding-family entity forms (task 021, FR-04).
 *
 * Placement (ADR-006 — form-bound via placement, not a bound field; mirrors
 * the sibling `CommunicationTimelineApp`): the host record IS the regarding
 * target. Unlike the R1 thread-id control (which needs a lookup indirection
 * for the `sprk_communication` placement), this variant reads the host
 * record's `(entityType, id)` straight from the Xrm page context — no
 * `context.webAPI.retrieveRecord` call is needed before rendering.
 *
 * The shared `<CommunicationTimeline mode="regarding" .../>` component owns
 * rendering the threads-as-collapsible-groups view internally — it polls the
 * BFF `by-regarding` endpoint (task 010) via the injected
 * `authenticatedFetch`/`bffBaseUrl` (ADR-028). This host component's ONLY job
 * is resolving `(entityType, id)` from context + wiring auth. Regarding mode
 * is READ-ONLY (no compose box — composing stays in thread-id mode, the
 * sibling `CommunicationTimeline` PCF); this host never wires send/compose
 * props.
 */

import * as React from 'react';
import { makeStyles, tokens, Spinner, MessageBar, MessageBarBody, Text } from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
import { CommunicationTimeline, type CommunicationTimelineProps } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
import { resolveRegardingContext } from './hostContext';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

// React 16 type seam: the shared lib's .d.ts is emitted against newer React
// types, whose FC return type is incompatible with React 16's JSX element
// type. Cast at the boundary (same pattern as CommunicationTimeline/
// CommunicationMessageActions/CommunicationActions/RegardingResolver).
// Runtime is unaffected — the compiled module is identical.
const CommunicationTimelineR16 = CommunicationTimeline as unknown as React.ComponentType<CommunicationTimelineProps>;

const POLL_INTERVAL_MS = 5000;

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column', minHeight: 0 },
  notice: { paddingInline: tokens.spacingHorizontalM, paddingBlock: tokens.spacingVerticalXS },
  content: { flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' },
  footer: {
    flexShrink: 0,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalM,
    display: 'flex',
    justifyContent: 'flex-end',
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    whiteSpace: 'nowrap',
  },
});

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). Mirrors CommunicationTimelineApp. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function getXrm(): any {
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;
    return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
  } catch {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (window as any).Xrm;
  }
}

function getHostRecordId(): string | undefined {
  const xrm = getXrm();
  try {
    const id = xrm?.Page?.data?.entity?.getId?.();
    if (typeof id === 'string' && id.length > 0) return id.replace(/[{}]/g, '');
  } catch {
    /* ignore */
  }
  return undefined;
}

function getHostEntityName(): string | undefined {
  const xrm = getXrm();
  try {
    const name = xrm?.Page?.data?.entity?.getEntityName?.();
    if (typeof name === 'string' && name.length > 0) return name;
  } catch {
    /* ignore */
  }
  return undefined;
}

export interface ICommunicationTimelineRegardingAppProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationTimelineRegardingApp: React.FC<ICommunicationTimelineRegardingAppProps> = ({
  context,
  version,
}) => {
  const s = useStyles();

  const manifestClientAppId = (context.parameters.clientAppId?.raw ?? '').trim();
  const manifestBffAppId = (context.parameters.bffAppId?.raw ?? '').trim();
  const manifestApiBaseUrl = (context.parameters.apiBaseUrl?.raw ?? '').trim();
  const showVersionFooter = context.parameters.showVersionFooter?.raw !== false;

  const hostEntityName = React.useMemo(() => getHostEntityName(), []);
  const hostRecordId = React.useMemo(() => getHostRecordId(), []);
  const webApi = context.webAPI;

  const [authReady, setAuthReady] = React.useState(false);
  const [authError, setAuthError] = React.useState<string | null>(null);
  const [bffBaseUrl, setBffBaseUrl] = React.useState('');

  // Bootstrap @spaarke/auth once (identical pattern to CommunicationTimelineApp).
  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const clientAppId = manifestClientAppId || (await getMsalClientId(webApi));
        const bffAppId = manifestBffAppId || (await getBffApiAppId(webApi));
        const baseUrl = manifestApiBaseUrl || (await getApiBaseUrl(webApi));
        if (!clientAppId || !bffAppId || !baseUrl) {
          if (!cancelled) {
            setAuthError(
              'The communication timeline is not configured. Set the sprk_MsalClientId / sprk_BffApiAppId / ' +
                'sprk_BffApiBaseUrl Dataverse environment variables (or the PCF Client App ID / BFF App ID / API URL inputs).'
            );
          }
          return;
        }
        await initializeAuth(clientAppId, bffAppId, baseUrl, resolveDataverseUrl());
        if (cancelled) return;
        setBffBaseUrl(baseUrl);
        setAuthReady(true);
      } catch (err) {
        if (!cancelled) setAuthError(err instanceof Error ? err.message : 'Authentication failed to initialize.');
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [manifestClientAppId, manifestBffAppId, manifestApiBaseUrl, webApi]);

  const regardingContext = React.useMemo(
    () => resolveRegardingContext(hostEntityName, hostRecordId),
    [hostEntityName, hostRecordId]
  );

  if (authError) {
    return (
      <div className={s.notice}>
        <MessageBar intent="error">
          <MessageBarBody>{authError}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!authReady) {
    return (
      <div className={s.notice}>
        <Spinner size="tiny" label="Loading conversations…" />
      </div>
    );
  }

  if (!regardingContext) {
    return (
      <div className={s.notice}>
        <MessageBar intent="info">
          <MessageBarBody>
            Communication timeline unavailable — place this control on a supported record form (Matter, Project,
            Invoice, Service Request, Work Assignment, Event, Budget, Analysis, Organization, Account, or Contact).
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={s.root}>
      <div className={s.content}>
        <CommunicationTimelineR16
          mode="regarding"
          entityType={regardingContext.entityType}
          id={regardingContext.id}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          pollIntervalMs={POLL_INTERVAL_MS}
        />
      </div>
      {showVersionFooter && (
        <div className={s.footer}>
          <Text className={s.versionText}>v{version}</Text>
        </div>
      )}
    </div>
  );
};
