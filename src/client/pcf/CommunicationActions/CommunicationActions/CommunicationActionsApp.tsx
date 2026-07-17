/**
 * CommunicationActionsApp — action bar for the OOB sprk_communication form.
 *
 * Exposes Reply / Forward / Send / Save Draft / Save to SharePoint, hosting the
 * shared `<SendEmailPage/>` (task 020/021) in a dialog for compose/reply/forward
 * and calling the EXISTING BFF endpoints via `@spaarke/auth` `authenticatedFetch`
 * (ADR-028): `/api/communications/send` (through the composer) and the new
 * `/api/communications/{id}/archive` (task 044a). Replaces the 1,150-LOC ribbon
 * `sprk_communication_send.js` (W4 pivot).
 *
 * No re-implemented send/archival — the composer + endpoints own that. No
 * self-built credential: auth is bootstrapped once via `initializeAuth`.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Toolbar,
  ToolbarButton,
  ToolbarDivider,
  Tooltip,
  Spinner,
  Dialog,
  DialogSurface,
  DialogBody,
  MessageBar,
  MessageBarBody,
  Text,
} from '@fluentui/react-components';
import {
  ArrowReply16Regular,
  ArrowReplyAll16Regular,
  ArrowForward16Regular,
  Mail16Regular,
  CloudArrowUp16Regular,
  CalendarLtr16Regular,
  CheckmarkCircle16Regular,
  Receipt16Regular,
} from '@fluentui/react-icons';
import { authenticatedFetch } from '@spaarke/auth';
import { SendEmailPage, type ISendEmailPageProps, type EmailComposerMode } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
import { deriveComposerFields, type ComposerMode } from './composerPrefill';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

// React 16 type seam: the shared lib's .d.ts is emitted against React 19 types,
// whose FC return type is incompatible with React 16's JSX element type. Cast at
// the boundary (same pattern as RegardingResolver's PolymorphicPicker). Runtime
// is unaffected — the compiled module is identical.
const SendEmailPageR16 = SendEmailPage as unknown as React.ComponentType<ISendEmailPageProps>;

// sprk_communicationtype = Email (task-002 verified) — the only interactive channel.
const COMMUNICATION_TYPE_EMAIL = 100000000;

// "Create from this email" → target entity logical name (moved here from the
// Connections PCF — these are actions on the email, not association review).
const CREATE_TARGET_ENTITY = {
  event: 'sprk_event',
  todo: 'sprk_todo',
  invoice: 'sprk_invoice',
} as const;
type CreateKind = keyof typeof CREATE_TARGET_ENTITY;

/**
 * Parse the association provenance JSON and decide which "create from this email"
 * actions the engine's structural signals suggest (mirrors the Connections PCF's
 * deriveCreateActions). Best-effort: bad/empty JSON → no suggestions.
 */
function deriveSuggestedCreates(provenanceRaw: string | null | undefined): Set<CreateKind> {
  const out = new Set<CreateKind>();
  if (!provenanceRaw) return out;
  try {
    const doc = JSON.parse(provenanceRaw) as {
      signals?: { category?: string; obligations?: string[] }[];
    };
    for (const sig of doc.signals ?? []) {
      const obligations = sig.obligations ?? [];
      if (sig.category === 'invoice') out.add('invoice');
      if (sig.category === 'event' || obligations.includes('calendar-response')) out.add('event');
      if (obligations.includes('deadline-response') || obligations.includes('payment-review')) out.add('todo');
    }
  } catch {
    /* ignore malformed provenance */
  }
  return out;
}

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column' },
  // Compact single row to match the OOB command bar height (~32px).
  barRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingBlock: tokens.spacingVerticalXXS,
    paddingInline: tokens.spacingHorizontalXS,
    minHeight: '32px',
  },
  bar: { flexWrap: 'wrap', flexGrow: 1 },
  // Match the OOB command-bar typography — 14px, regular weight (the default
  // ToolbarButton renders heavier/larger than the native bar).
  toolbarBtn: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightRegular,
  },
  rightGroup: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS },
  // Suggested-by-engine create icons get a subtle brand tint (the icon-only ✨ cue).
  suggestedIcon: { color: tokens.colorBrandForeground1 },
  grow: { flex: 1 },
  notice: { paddingInline: tokens.spacingHorizontalM, paddingBottom: tokens.spacingVerticalXS },
  dialogSurface: { maxWidth: '900px', width: '90vw', height: '85vh', padding: 0 },
  dialogBody: { height: '100%', display: 'block' },
  versionText: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    whiteSpace: 'nowrap',
    paddingRight: tokens.spacingHorizontalS,
  },
});

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). */
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

async function refreshHostForm(): Promise<void> {
  const xrm = getXrm();
  try {
    const refresh = xrm?.Page?.data?.refresh;
    if (typeof refresh === 'function') {
      const r = refresh.call(xrm.Page.data, false);
      if (r && typeof r.then === 'function') await r;
    }
  } catch (err) {
    console.warn('[CommunicationActions] form refresh failed:', err);
  }
}

interface IRecordPrefill {
  from: string;
  to: string;
  cc: string;
  subject: string;
  body: string;
}

export interface ICommunicationActionsAppProps {
  context: ComponentFramework.Context<IInputs>;
  readOnly: boolean;
  version: string;
}

export const CommunicationActionsApp: React.FC<ICommunicationActionsAppProps> = ({ context, readOnly, version }) => {
  const s = useStyles();

  const manifestClientAppId = (context.parameters.clientAppId?.raw ?? '').trim();
  const manifestBffAppId = (context.parameters.bffAppId?.raw ?? '').trim();
  const manifestApiBaseUrl = (context.parameters.apiBaseUrl?.raw ?? '').trim();
  const showVersionFooter = context.parameters.showVersionFooter?.raw !== false;
  const channelValue = context.parameters.communicationType?.raw;
  const isEmail = channelValue == null || channelValue === COMMUNICATION_TYPE_EMAIL;

  const communicationId = React.useMemo(() => getHostRecordId(), []);
  const webApi = context.webAPI;

  const [authReady, setAuthReady] = React.useState(false);
  const [authError, setAuthError] = React.useState<string | null>(null);
  const [bffBaseUrl, setBffBaseUrl] = React.useState('');
  const [error, setError] = React.useState<string | null>(null);
  const [status, setStatus] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  const [composerMode, setComposerMode] = React.useState<ComposerMode | null>(null);
  const [prefill, setPrefill] = React.useState<IRecordPrefill | null>(null);
  // Which "create from this email" actions the engine flagged (from the provenance
  // signals) — drives the subtle ✨ brand tint on the icon-only create buttons.
  const [suggestedCreates, setSuggestedCreates] = React.useState<Set<CreateKind>>(new Set());

  // Bootstrap @spaarke/auth once. Config resolves from the PCF manifest inputs FIRST,
  // then falls back to the Dataverse environment variables (sprk_MsalClientId /
  // sprk_BffApiAppId / sprk_BffApiBaseUrl) — the same pattern SemanticSearch uses, so
  // the control works with zero form config where those env vars are set.
  React.useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const clientAppId = manifestClientAppId || (await getMsalClientId(webApi));
        const bffAppId = manifestBffAppId || (await getBffApiAppId(webApi));
        // getApiBaseUrl returns host-only (strips /api); @spaarke/auth re-adds /api.
        const baseUrl = manifestApiBaseUrl || (await getApiBaseUrl(webApi));
        if (!clientAppId || !bffAppId || !baseUrl) {
          if (!cancelled) {
            setAuthError(
              'Communication actions are not configured. Set the sprk_MsalClientId / sprk_BffApiAppId / ' +
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

  // Load the record fields the composer needs for reply/forward pre-fill.
  React.useEffect(() => {
    if (!communicationId) return;
    let cancelled = false;
    void (async () => {
      try {
        const rec = await context.webAPI.retrieveRecord(
          'sprk_communication',
          communicationId,
          '?$select=sprk_from,sprk_to,sprk_cc,sprk_subject,sprk_body,sprk_associationprovenance'
        );
        if (cancelled) return;
        setPrefill({
          from: (rec.sprk_from as string) ?? '',
          to: (rec.sprk_to as string) ?? '',
          cc: (rec.sprk_cc as string) ?? '',
          subject: (rec.sprk_subject as string) ?? '',
          body: (rec.sprk_body as string) ?? '',
        });
        setSuggestedCreates(deriveSuggestedCreates(rec.sprk_associationprovenance as string | null));
      } catch (err) {
        console.warn('[CommunicationActions] prefill retrieve failed:', err);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [communicationId, context.webAPI]);

  const openComposer = (mode: ComposerMode) => {
    setError(null);
    setStatus(null);
    setComposerMode(mode);
  };

  // Launch a "create from this email" form (Event / To Do / Invoice). R4 launches
  // the target create form; full create-and-link is the Notification-Spine project.
  const handleCreate = React.useCallback((kind: CreateKind) => {
    const xrm = getXrm();
    const entityName = CREATE_TARGET_ENTITY[kind];
    const onLaunchError = (err: unknown) =>
      console.warn('[CommunicationActions] create-from-email launch failed:', err);
    try {
      if (typeof xrm?.Navigation?.openForm === 'function') {
        Promise.resolve(xrm.Navigation.openForm({ entityName, useQuickCreateForm: true })).catch(onLaunchError);
      } else if (typeof xrm?.Navigation?.navigateTo === 'function') {
        Promise.resolve(
          xrm.Navigation.navigateTo(
            { pageType: 'entityrecord', entityName },
            { target: 2, width: { value: 60, unit: '%' }, height: { value: 80, unit: '%' } }
          )
        ).catch(onLaunchError);
      } else {
        setError('Create is unavailable in this host.');
      }
    } catch (err) {
      onLaunchError(err);
    }
  }, []);

  const handleArchive = React.useCallback(() => {
    if (!communicationId) {
      setError('Save the communication before archiving to SharePoint.');
      return;
    }
    void (async () => {
      setBusy(true);
      setError(null);
      setStatus(null);
      try {
        // Relative path — the @spaarke/auth resolver prepends /api/.
        const resp = await authenticatedFetch(`/communications/${communicationId}/archive`, { method: 'POST' });
        if (!resp.ok) {
          setError(`Save to SharePoint failed (${resp.status}).`);
          return;
        }
        const result = (await resp.json()) as { alreadyArchived?: boolean; attachmentDocumentsCreated?: number };
        setStatus(
          result.alreadyArchived
            ? 'Already saved to SharePoint.'
            : `Saved to SharePoint${result.attachmentDocumentsCreated ? ` (+${result.attachmentDocumentsCreated} attachment${result.attachmentDocumentsCreated === 1 ? '' : 's'})` : ''}.`
        );
        await refreshHostForm();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Save to SharePoint failed.');
      } finally {
        setBusy(false);
      }
    })();
  }, [communicationId]);

  // Compose/reply/replyAll/forward pre-fill derived from the loaded record (pure helper).
  const composerProps = React.useMemo<ISendEmailPageProps | null>(() => {
    if (!composerMode) return null;
    // Reply All reuses the shared 'reply' composer mode (Re:, reply chrome) but with
    // the wider recipient set; '+ New' is a blank 'compose' not tied to this record.
    const sendMode: EmailComposerMode = composerMode === 'replyAll' ? 'reply' : composerMode;
    return {
      mode: sendMode,
      communicationId: composerMode === 'compose' ? undefined : communicationId,
      authenticatedFetch,
      bffBaseUrl,
      onSent: () => {
        setComposerMode(null);
        setStatus('Sent.');
        void refreshHostForm();
      },
      onClose: () => setComposerMode(null),
      ...deriveComposerFields(composerMode, prefill),
    };
  }, [composerMode, prefill, communicationId, bffBaseUrl]);

  if (authError) {
    return (
      <div className={s.notice}>
        <MessageBar intent="error">
          <MessageBarBody>{authError}</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!isEmail) {
    return (
      <div className={s.notice}>
        <MessageBar intent="info">
          <MessageBarBody>This channel is read-only — email actions are unavailable.</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  if (!authReady) {
    return (
      <div className={s.notice}>
        <Spinner size="tiny" label="Preparing actions…" />
      </div>
    );
  }

  const disabled = readOnly || busy;
  // Compose/reply/forward pre-fill from the record; disable those actions until the
  // record has loaded so the composer never opens blank (code-review S3).
  const composeDisabled = disabled || (communicationId != null && prefill == null);

  return (
    <div className={s.root}>
      <div className={s.barRow}>
        <Toolbar size="small" className={s.bar} aria-label="Communication actions">
          {/* Left group — the email verbs (icon + label). All open the composer. */}
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<ArrowReply16Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('reply')}
          >
            Reply
          </ToolbarButton>
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<ArrowReplyAll16Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('replyAll')}
          >
            Reply All
          </ToolbarButton>
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<ArrowForward16Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('forward')}
          >
            Forward
          </ToolbarButton>
          <ToolbarButton
            className={s.toolbarBtn}
            icon={<Mail16Regular />}
            disabled={disabled}
            onClick={() => openComposer('compose')}
          >
            New
          </ToolbarButton>

          {/* Spacer pushes the record/email action icons to the far right (Outlook-web style). */}
          <div className={s.grow} />
          <ToolbarDivider />

          {/* Right group — record/email actions (icon-only, tooltip). ✨ = engine-suggested. */}
          <div className={s.rightGroup}>
            <Tooltip content="Save to SharePoint" relationship="label">
              <ToolbarButton
                icon={<CloudArrowUp16Regular />}
                aria-label="Save to SharePoint"
                disabled={disabled}
                onClick={handleArchive}
              />
            </Tooltip>
            <Tooltip
              content={suggestedCreates.has('event') ? 'Create Event (suggested from this email)' : 'Create Event'}
              relationship="label"
            >
              <ToolbarButton
                icon={<CalendarLtr16Regular className={suggestedCreates.has('event') ? s.suggestedIcon : undefined} />}
                aria-label="Create Event"
                disabled={disabled}
                onClick={() => handleCreate('event')}
              />
            </Tooltip>
            <Tooltip
              content={suggestedCreates.has('todo') ? 'Create To Do (suggested from this email)' : 'Create To Do'}
              relationship="label"
            >
              <ToolbarButton
                icon={
                  <CheckmarkCircle16Regular className={suggestedCreates.has('todo') ? s.suggestedIcon : undefined} />
                }
                aria-label="Create To Do"
                disabled={disabled}
                onClick={() => handleCreate('todo')}
              />
            </Tooltip>
            <Tooltip
              content={suggestedCreates.has('invoice') ? 'Link Invoice (suggested from this email)' : 'Link Invoice'}
              relationship="label"
            >
              <ToolbarButton
                icon={<Receipt16Regular className={suggestedCreates.has('invoice') ? s.suggestedIcon : undefined} />}
                aria-label="Link Invoice"
                disabled={disabled}
                onClick={() => handleCreate('invoice')}
              />
            </Tooltip>
          </div>
        </Toolbar>
        <div className={s.grow} />
        {showVersionFooter && <Text className={s.versionText}>v{version}</Text>}
      </div>

      {status && (
        <div className={s.notice}>
          <MessageBar intent="success">
            <MessageBarBody>{status}</MessageBarBody>
          </MessageBar>
        </div>
      )}
      {error && (
        <div className={s.notice}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      <Dialog open={composerMode !== null} onOpenChange={(_, d) => !d.open && setComposerMode(null)}>
        <DialogSurface className={s.dialogSurface}>
          <DialogBody className={s.dialogBody}>{composerProps && <SendEmailPageR16 {...composerProps} />}</DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
