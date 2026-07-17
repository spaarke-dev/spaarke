/**
 * CommunicationTimelineApp — form-bound viewer for the shared
 * `<CommunicationTimeline/>` polling message timeline (task 060) on the OOB
 * `sprk_communication` / `sprk_communicationthread` forms (task 061, FR-11).
 *
 * Two host placements (ADR-006 — form-bound via placement, not a bound field;
 * mirrors `CommunicationMessageActions`):
 *   - `sprk_communicationthread` — the record IS the thread; render it directly.
 *   - `sprk_communication` — render the thread its `sprk_communicationthread`
 *     lookup (`_sprk_communicationthread_value`) points at.
 *
 * The shared `<CommunicationTimeline/>` component owns rendering the message
 * list, unread indicator, AND the compose/send box internally — it polls the
 * BFF thread-read + unread-count endpoints (task 050) and calls
 * `/api/communications/send` (task 051) on send, all via the injected
 * `authenticatedFetch`/`bffBaseUrl` (ADR-028). This host component's ONLY job
 * is resolving `threadId` from the placement + wiring auth — no compose/send
 * logic lives here (unlike `CommunicationMessageActions`, which hosts
 * `<TimelineComposeBox/>` directly for its own dedicated compose accessory).
 *
 * Deferred wiring (R1 scope — task 061 spec §5, HARD RULE 5): the shared
 * component's `onQuoteIntoEmail` / `onSearchRecipients` / `associations`
 * props are NOT wired here. They require a host-owned dialog (email-quoting
 * target, recipient-directory search, entity-association stamping) that is
 * out of scope for this form-bound viewer — a future task adds that wiring.
 */

import * as React from 'react';
import { makeStyles, tokens, Spinner, MessageBar, MessageBarBody, Text } from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
import { CommunicationTimeline, type CommunicationTimelineProps } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
import { resolveThreadId } from './hostContext';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

// React 16 type seam: the shared lib's .d.ts is emitted against newer React
// types, whose FC return type is incompatible with React 16's JSX element
// type. Cast at the boundary (same pattern as CommunicationMessageActions/
// CommunicationActions/RegardingResolver). Runtime is unaffected — the
// compiled module is identical.
const CommunicationTimelineR16 = CommunicationTimeline as unknown as React.ComponentType<CommunicationTimelineProps>;

const SPRK_COMMUNICATION = 'sprk_communication';
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

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). Mirrors CommunicationMessageActions. */
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

export interface ICommunicationTimelineAppProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationTimelineApp: React.FC<ICommunicationTimelineAppProps> = ({ context, version }) => {
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

  // Record fields only apply to the sprk_communication placement (the thread
  // placement needs no record read — hostRecordId IS the target thread id).
  const [recordLoaded, setRecordLoaded] = React.useState(hostEntityName !== SPRK_COMMUNICATION);
  const [threadLookupId, setThreadLookupId] = React.useState<string | null>(null);

  // Bootstrap @spaarke/auth once (identical pattern to CommunicationMessageActions).
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

  // Load the sprk_communication record's thread lookup when placed on that entity.
  React.useEffect(() => {
    if (hostEntityName !== SPRK_COMMUNICATION || !hostRecordId) return;
    let cancelled = false;
    void (async () => {
      try {
        // `_sprk_communicationthread_value` is the OData annotation for the
        // `sprk_communicationthread` lookup's GUID (verified as-built name —
        // see communicationTimelineApi.ts file header).
        const rec = await context.webAPI.retrieveRecord(
          SPRK_COMMUNICATION,
          hostRecordId,
          '?$select=_sprk_communicationthread_value'
        );
        if (cancelled) return;
        setThreadLookupId((rec._sprk_communicationthread_value as string | undefined) ?? null);
      } catch (err) {
        console.warn('[CommunicationTimeline] record retrieve failed:', err);
      } finally {
        if (!cancelled) setRecordLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [hostEntityName, hostRecordId, context.webAPI]);

  const threadId = React.useMemo(
    () => resolveThreadId(hostEntityName, hostRecordId, threadLookupId),
    [hostEntityName, hostRecordId, threadLookupId]
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

  if (!authReady || (hostEntityName === SPRK_COMMUNICATION && !recordLoaded)) {
    return (
      <div className={s.notice}>
        <Spinner size="tiny" label="Loading conversation…" />
      </div>
    );
  }

  if (!threadId) {
    return (
      <div className={s.notice}>
        <MessageBar intent="info">
          <MessageBarBody>
            Timeline unavailable — place this control on the sprk_communicationthread form, or on a
            sprk_communication record with a thread.
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={s.root}>
      <div className={s.content}>
        <CommunicationTimelineR16
          threadId={threadId}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          pollIntervalMs={POLL_INTERVAL_MS}
          // onQuoteIntoEmail / onSearchRecipients / associations intentionally
          // NOT wired — deferred to a host-owned dialog (out of R1 scope; see
          // file header + task 061 spec §5, HARD RULE 5).
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
