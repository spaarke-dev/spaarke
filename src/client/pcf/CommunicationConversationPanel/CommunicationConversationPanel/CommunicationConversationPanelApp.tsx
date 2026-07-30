/**
 * CommunicationConversationPanelApp — Surface 1 host (task 030 / FR-13).
 *
 * Resolves the host record's `(entityType, id)` straight from the Xrm page
 * context (the record IS the regarding target — NO second regarding mechanism,
 * NFR-06 / ADR-024), bootstraps `@spaarke/auth` (the ONLY fetch path, ADR-028),
 * reads the record's conversations via the shared `readByRegarding` wrapper,
 * and hands the bounded `buildPreviewModel` result to `<ConversationPreview/>`.
 * The header "Open" affordance opens `<ConversationModal/>`, which mounts the
 * shared `<ConversationWorkspace/>` record-filtered to this record.
 *
 * Mirrors the sibling `CommunicationTimelineRegardingApp`'s context/auth wiring;
 * the presentation (bounded preview + counter + modal launch) is this control's
 * own. Fluent v9 semantic tokens only (ADR-021).
 */

import * as React from 'react';
import { makeStyles, tokens, Spinner, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
import { readByRegarding, cleanGuid, type IRegardingReadResultDto } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
import { resolveRegardingContext } from './hostContext';
import { buildPreviewModel } from './previewModel';
import { ConversationPreview } from './ConversationPreview';
import { ConversationModal } from './ConversationModal';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

const POLL_INTERVAL_MS = 5000;

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column', minHeight: 0 },
  notice: { paddingInline: tokens.spacingHorizontalM, paddingBlock: tokens.spacingVerticalXS },
});

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). Mirrors the sibling control. */
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

/**
 * Round-7 item 11: detects the `sprk_openconversation=1` deep-link param the
 * CommunicationArrived notification appends, so clicking the MDA bell lands on the
 * record AND auto-opens this Messages modal (not a single message — far more
 * useful). The PCF runs in an iframe, so the record-form URL lives on the TOP
 * window; we read top → parent → self, each in its own try (cross-frame access can
 * throw). Best-effort: if the MDA stripped the param, this returns false and the
 * panel simply doesn't auto-open (graceful degradation — the click still landed on
 * the record).
 */
function shouldAutoOpenConversation(): boolean {
  const searches: Array<() => string | undefined> = [
    () => window.top?.location?.search,
    () => window.parent?.location?.search,
    () => window.location.search,
  ];
  for (const get of searches) {
    try {
      const search = get();
      if (search && new URLSearchParams(search).get('sprk_openconversation') === '1') return true;
    } catch {
      /* cross-frame access denied — try the next scope */
    }
  }
  return false;
}

/** OOB record open via the sanctioned Layout 1 (`Xrm.Navigation.navigateTo`, target 2, 85% × 85%). */
function openRecordViaXrm(entityType: string, id: string): void {
  const xrm = getXrm();
  try {
    void xrm?.Navigation?.navigateTo?.(
      { pageType: 'entityrecord', entityName: entityType, entityId: cleanGuid(id) },
      { target: 2, position: 1, width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' } }
    );
  } catch {
    /* ignore — non-Xrm host (e.g. test harness) */
  }
}

export interface ICommunicationConversationPanelAppProps {
  context: ComponentFramework.Context<IInputs>;
  version: string;
}

export const CommunicationConversationPanelApp: React.FC<ICommunicationConversationPanelAppProps> = ({
  context,
  version,
}) => {
  const s = useStyles();

  const manifestClientAppId = (context.parameters.clientAppId?.raw ?? '').trim();
  const manifestBffAppId = (context.parameters.bffAppId?.raw ?? '').trim();
  const manifestApiBaseUrl = (context.parameters.apiBaseUrl?.raw ?? '').trim();
  const showVersionFooter = context.parameters.showVersionFooter?.raw !== false;
  const title = (context.parameters.title?.raw ?? '').trim() || 'MESSAGES';

  const hostEntityName = React.useMemo(() => getHostEntityName(), []);
  const hostRecordId = React.useMemo(() => getHostRecordId(), []);
  const webApi = context.webAPI;
  const currentUserSystemUserId = React.useMemo(
    () => cleanGuid(context.userSettings?.userId ?? ''),
    [context.userSettings]
  );

  const [authReady, setAuthReady] = React.useState(false);
  const [authError, setAuthError] = React.useState<string | null>(null);
  const [bffBaseUrl, setBffBaseUrl] = React.useState('');

  // Bootstrap @spaarke/auth once (identical pattern to the sibling control).
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
              'The conversation panel is not configured. Set the sprk_MsalClientId / sprk_BffApiAppId / ' +
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

  const [result, setResult] = React.useState<IRegardingReadResultDto | null>(null);
  const [readError, setReadError] = React.useState<string | null>(null);
  const [modalOpen, setModalOpen] = React.useState(false);

  // Round-7 item 11: auto-open the Messages modal once when arrived via the
  // notification deep-link (`sprk_openconversation=1`). The record IS the target
  // (the PCF is on the navigated-to form), so the param's presence alone is the
  // signal — no id match needed. Guarded so it fires at most once per mount (a
  // manual close must not be undone by a re-render).
  const autoOpenedRef = React.useRef(false);
  React.useEffect(() => {
    if (autoOpenedRef.current || !authReady || !regardingContext) return;
    if (shouldAutoOpenConversation()) {
      autoOpenedRef.current = true;
      setModalOpen(true);
    }
  }, [authReady, regardingContext]);

  // Session-local "New" affordance (UAT §A3): there is no persisted per-user
  // last-seen watermark for threads (documented KNOWN LIMITATION in
  // communicationThreadListApi.ts — getThreadUnreadCount needs a `since`
  // cursor this compact preview does not own). Rather than mis-label every
  // thread as "New" on every cold load (calling the unread endpoint with no
  // `since` returns the whole readable count, not a true delta) or inventing
  // a second persisted-watermark mechanism, this establishes an in-memory
  // baseline of each thread's message count on the FIRST successful read
  // after mount, then flags a thread "New" once its count grows past that
  // baseline (or a thread appears that wasn't in the baseline) — i.e. "new
  // since I started looking at this record's preview." Resets on remount;
  // never persisted. Deliberately local to this PCF, not a shared mechanism.
  const baselineThreadCountsRef = React.useRef<Record<string, number> | null>(null);
  const [newThreadIds, setNewThreadIds] = React.useState<ReadonlySet<string>>(() => new Set());

  React.useEffect(() => {
    if (!result) return;
    const currentCounts: Record<string, number> = {};
    for (const t of result.threads) currentCounts[t.threadId] = t.count;

    if (baselineThreadCountsRef.current === null) {
      baselineThreadCountsRef.current = currentCounts;
      return;
    }

    setNewThreadIds(prev => {
      let changed = false;
      const next = new Set(prev);
      for (const [threadId, count] of Object.entries(currentCounts)) {
        const baseline = baselineThreadCountsRef.current?.[threadId];
        if ((baseline === undefined || count > baseline) && !next.has(threadId)) {
          next.add(threadId);
          changed = true;
        }
      }
      return changed ? next : prev;
    });
  }, [result]);

  // Read the record's conversations (initial + ~5s poll to keep the glance fresh).
  React.useEffect(() => {
    if (!authReady || !regardingContext) return;
    let cancelled = false;
    const client = { authenticatedFetch, bffBaseUrl };

    const load = async () => {
      try {
        const data = await readByRegarding(regardingContext.entityType, regardingContext.id, client);
        if (!cancelled) {
          setResult(data);
          setReadError(null);
        }
      } catch (err) {
        if (!cancelled) setReadError(err instanceof Error ? err.message : 'Failed to load conversations.');
      }
    };

    void load();
    const timer = setInterval(() => void load(), POLL_INTERVAL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [authReady, regardingContext, bffBaseUrl]);

  const previewModel = React.useMemo(() => (result ? buildPreviewModel(result) : null), [result]);
  const threadNames = React.useMemo<Record<string, string>>(() => {
    const map: Record<string, string> = {};
    for (const t of result?.threads ?? []) {
      if (t.name && t.name.trim().length > 0) map[t.threadId] = t.name;
    }
    return map;
  }, [result]);

  // Anti-pattern #5 (MODAL-DECISION-CRITERIA): close the Fluent v9 dialog BEFORE
  // launching the OOB navigateTo modal — never nest modals from different chrome
  // families.
  const handleOpenRecord = React.useCallback((entityType: string, id: string) => {
    setModalOpen(false);
    openRecordViaXrm(entityType, id);
  }, []);

  if (authError) {
    return (
      <div className={s.notice}>
        <MessageBar intent="error">
          <MessageBarBody>{authError}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!regardingContext) {
    return (
      <div className={s.notice}>
        <MessageBar intent="info">
          <MessageBarBody>
            Conversation panel unavailable — place this control on a supported record form (Matter, Project, Invoice,
            Service Request, Work Assignment, Event, Budget, Analysis, Organization, Account, or Contact).
          </MessageBarBody>
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

  if (readError && !previewModel) {
    return (
      <div className={s.notice}>
        <MessageBar intent="error">
          <MessageBarBody>{readError}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!previewModel) {
    return (
      <div className={s.notice}>
        <Spinner size="tiny" label="Loading conversations…" />
      </div>
    );
  }

  return (
    <div className={s.root}>
      <ConversationPreview
        model={previewModel}
        version={version}
        showVersionFooter={showVersionFooter}
        title={title}
        newThreadIds={newThreadIds}
        onOpen={() => setModalOpen(true)}
      />
      {modalOpen && (
        <ConversationModal
          open={modalOpen}
          onClose={() => setModalOpen(false)}
          entityType={regardingContext.entityType}
          id={regardingContext.id}
          authenticatedFetch={authenticatedFetch}
          bffBaseUrl={bffBaseUrl}
          currentUserSystemUserId={currentUserSystemUserId}
          threadNames={threadNames}
          onOpenRecord={handleOpenRecord}
        />
      )}
    </div>
  );
};
