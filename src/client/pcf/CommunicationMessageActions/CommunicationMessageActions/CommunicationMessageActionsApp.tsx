/**
 * CommunicationMessageActionsApp — Compose / Send / Respond accessory for the Message channel,
 * on the OOB `sprk_communication` / `sprk_communicationthread` forms (task 062, FR-12).
 *
 * §11 Component Justification (NEW control, not an extension of `CommunicationActions`):
 * (1) Existing — `CommunicationActions` already implements Reply/Forward/Send/SaveDraft/
 *     Save-to-SharePoint for Email via `<SendEmailPage/>`, and explicitly renders a "read-only"
 *     notice for every non-Email channel (`isEmail` gate) — a hard product decision from email-r4,
 *     not an oversight. (2) Extension — technically possible, but would require replacing the
 *     entire action set (no drafts/forward/SPE-archive concepts exist for a chat message) AND the
 *     composer component itself (`<TimelineComposeBox/>`, not `<SendEmailPage/>`/`EmailComposer`'s
 *     reply/forward/draft modes) inside an already-large, shipped, production email control — net
 *     new code sharing almost nothing but the outer shell, adding branching risk to a live control.
 *     (3) Cost-of-doing-nothing — FR-12 (compose/send/respond for Message, no Code Page/FCC swap)
 *     fails without SOME accessory; a small dedicated control isolates the message-specific action
 *     set/composer cleanly while reusing the SAME composer sub-component (`<TimelineComposeBox/>`,
 *     task 060) and the SAME `@spaarke/auth` boundary pattern as `CommunicationActions`.
 *
 * Two host placements (ADR-006 — form-bound via placement, not a bound field; see manifest note):
 *   - `sprk_communicationthread` — "Compose" a fresh message directly into this thread.
 *   - `sprk_communication` (Message-typed only) — "Respond": replies into that message's thread
 *     (`sprk_communicationthread` lookup) with `inReplyToMessageId` = the current record id.
 * Both dispatch through the SAME `sendCommunication()` (task 051/062, `communicationType: 'message'`)
 * — no new send contract, no client-side ACS SDK (NFR-04).
 */

import * as React from 'react';
import { makeStyles, tokens, Spinner, MessageBar, MessageBarBody, Text } from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
import {
  TimelineComposeBox,
  sendCommunication,
  type ITimelineComposeBoxProps,
  type ITimelineSendPayload,
} from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
import { resolveMessageTarget, type IResolvedMessageTarget } from './hostContext';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

// React 16 type seam: the shared lib's .d.ts is emitted against React 19 types, whose FC return
// type is incompatible with React 16's JSX element type. Cast at the boundary (same pattern as
// CommunicationActions/RegardingResolver). Runtime is unaffected — the compiled module is identical.
const TimelineComposeBoxR16 = TimelineComposeBox as unknown as React.ComponentType<ITimelineComposeBoxProps>;

const SPRK_COMMUNICATION = 'sprk_communication';

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column' },
  notice: { paddingInline: tokens.spacingHorizontalM, paddingBlock: tokens.spacingVerticalXS },
  headerRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingInline: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
  },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    whiteSpace: 'nowrap',
  },
});

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). Mirrors CommunicationActions. */
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

async function refreshHostForm(): Promise<void> {
  const xrm = getXrm();
  try {
    const refresh = xrm?.Page?.data?.refresh;
    if (typeof refresh === 'function') {
      const r = refresh.call(xrm.Page.data, false);
      if (r && typeof r.then === 'function') await r;
    }
  } catch (err) {
    console.warn('[CommunicationMessageActions] form refresh failed:', err);
  }
}

export interface ICommunicationMessageActionsAppProps {
  context: ComponentFramework.Context<IInputs>;
  readOnly: boolean;
  version: string;
}

export const CommunicationMessageActionsApp: React.FC<ICommunicationMessageActionsAppProps> = ({
  context,
  readOnly,
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
  const [status, setStatus] = React.useState<string | null>(null);
  const [isSending, setIsSending] = React.useState(false);

  // Record fields only apply to the sprk_communication placement (the thread placement needs no
  // record read — hostRecordId IS the target thread id).
  const [recordLoaded, setRecordLoaded] = React.useState(hostEntityName !== SPRK_COMMUNICATION);
  const [communicationType, setCommunicationType] = React.useState<number | null>(null);
  const [threadLookupId, setThreadLookupId] = React.useState<string | null>(null);
  const [fromEmail, setFromEmail] = React.useState<string | undefined>(undefined);

  // Bootstrap @spaarke/auth once (identical pattern to CommunicationActions).
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
              'Message actions are not configured. Set the sprk_MsalClientId / sprk_BffApiAppId / ' +
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

  // Load the sprk_communication record's channel + thread lookup when placed on that entity.
  React.useEffect(() => {
    if (hostEntityName !== SPRK_COMMUNICATION || !hostRecordId) return;
    let cancelled = false;
    void (async () => {
      try {
        // `_sprk_communicationthread_value` is the OData annotation for the `sprk_communicationthread`
        // lookup's GUID (verified as-built name — see communicationTimelineApi.ts file header).
        const rec = await context.webAPI.retrieveRecord(
          SPRK_COMMUNICATION,
          hostRecordId,
          '?$select=sprk_communicationtype,sprk_from,_sprk_communicationthread_value'
        );
        if (cancelled) return;
        // context.webAPI.retrieveRecord returns option-set columns as a raw number (no {Value} wrapper —
        // that shape only applies to Xrm.WebApi.execute/FetchXML annotations, not this API).
        const typeValue =
          typeof rec.sprk_communicationtype === 'number' ? (rec.sprk_communicationtype as number) : null;
        setCommunicationType(typeValue);
        setThreadLookupId((rec._sprk_communicationthread_value as string | undefined) ?? null);
        setFromEmail((rec.sprk_from as string | undefined) ?? undefined);
      } catch (err) {
        console.warn('[CommunicationMessageActions] record retrieve failed:', err);
      } finally {
        if (!cancelled) setRecordLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [hostEntityName, hostRecordId, context.webAPI]);

  const target: IResolvedMessageTarget = React.useMemo(
    () => resolveMessageTarget(hostEntityName, hostRecordId, communicationType, threadLookupId),
    [hostEntityName, hostRecordId, communicationType, threadLookupId]
  );

  // Errors are surfaced by TimelineComposeBox's OWN inline error text (it catches whatever this
  // callback rethrows) — mirrors CommunicationTimeline.tsx's own handleSend (task 060), which
  // rethrows for the same reason rather than duplicating a second error banner here.
  const handleSend = React.useCallback(
    async (payload: ITimelineSendPayload) => {
      setIsSending(true);
      setStatus(null);
      try {
        await sendCommunication(
          {
            to: payload.to,
            body: payload.body,
            bodyFormat: payload.bodyFormat === 'PlainText' ? 'text' : 'html',
            attachmentDocumentIds: payload.attachmentDocumentIds,
            communicationType: 'message',
            threadId: target.threadId,
            inReplyToMessageId: target.inReplyToMessageId,
          },
          { authenticatedFetch, bffBaseUrl }
        );
        setStatus(target.mode === 'respond' ? 'Reply sent.' : 'Message sent.');
        await refreshHostForm();
      } finally {
        setIsSending(false);
      }
    },
    [target.threadId, target.inReplyToMessageId, target.mode, bffBaseUrl]
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
        <Spinner size="tiny" label="Preparing message actions…" />
      </div>
    );
  }

  if (target.mode === 'unsupported') {
    return (
      <div className={s.notice}>
        <MessageBar intent="info">
          <MessageBarBody>
            Message actions are unavailable — place this control on the sprk_communicationthread form, or on a
            Message-typed sprk_communication record.
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  return (
    <div className={s.root}>
      <div className={s.headerRow}>
        <Text weight="semibold" size={200}>
          {target.mode === 'respond' ? 'Respond' : 'Compose message'}
        </Text>
        {showVersionFooter && <Text className={s.versionText}>v{version}</Text>}
      </div>

      {status && (
        <div className={s.notice}>
          <MessageBar intent="success">
            <MessageBarBody>{status}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      <TimelineComposeBoxR16
        defaultTo={fromEmail ? [fromEmail] : undefined}
        isSending={isSending}
        onSend={handleSend}
        disabled={readOnly}
      />
    </div>
  );
};
