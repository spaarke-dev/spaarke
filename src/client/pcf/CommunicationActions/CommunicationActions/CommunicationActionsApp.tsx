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
  ArrowForward16Regular,
  Send16Regular,
  Save16Regular,
  CloudArrowUp16Regular,
} from '@fluentui/react-icons';
import { authenticatedFetch } from '@spaarke/auth';
import { SendEmailPage, type ISendEmailPageProps, type EmailComposerMode } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { initializeAuth, resolveDataverseUrl } from './authInit';
import { deriveComposerFields } from './composerPrefill';
import { getMsalClientId, getBffApiAppId, getApiBaseUrl } from '../../shared/utils/environmentVariables';

// React 16 type seam: the shared lib's .d.ts is emitted against React 19 types,
// whose FC return type is incompatible with React 16's JSX element type. Cast at
// the boundary (same pattern as RegardingResolver's PolymorphicPicker). Runtime
// is unaffected — the compiled module is identical.
const SendEmailPageR16 = SendEmailPage as unknown as React.ComponentType<ISendEmailPageProps>;

// sprk_communicationtype = Email (task-002 verified) — the only interactive channel.
const COMMUNICATION_TYPE_EMAIL = 100000000;

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
  bar: { flexWrap: 'wrap' },
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

async function saveHostForm(): Promise<void> {
  const xrm = getXrm();
  try {
    const save = xrm?.Page?.data?.entity?.save;
    if (typeof save === 'function') {
      const r = save.call(xrm.Page.data.entity);
      if (r && typeof r.then === 'function') await r;
    }
  } catch (err) {
    console.warn('[CommunicationActions] form save failed:', err);
  }
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
  const [composerMode, setComposerMode] = React.useState<EmailComposerMode | null>(null);
  const [prefill, setPrefill] = React.useState<IRecordPrefill | null>(null);

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
          '?$select=sprk_from,sprk_to,sprk_subject,sprk_body'
        );
        if (cancelled) return;
        setPrefill({
          from: (rec.sprk_from as string) ?? '',
          to: (rec.sprk_to as string) ?? '',
          subject: (rec.sprk_subject as string) ?? '',
          body: (rec.sprk_body as string) ?? '',
        });
      } catch (err) {
        console.warn('[CommunicationActions] prefill retrieve failed:', err);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [communicationId, context.webAPI]);

  const openComposer = (mode: EmailComposerMode) => {
    setError(null);
    setStatus(null);
    setComposerMode(mode);
  };

  const handleSaveDraft = React.useCallback(() => {
    void (async () => {
      setBusy(true);
      setError(null);
      setStatus(null);
      try {
        await saveHostForm();
        setStatus('Draft saved.');
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not save the draft.');
      } finally {
        setBusy(false);
      }
    })();
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

  // Compose/reply/forward pre-fill derived from the loaded record (pure helper).
  const composerProps = React.useMemo<ISendEmailPageProps | null>(() => {
    if (!composerMode) return null;
    return {
      mode: composerMode,
      communicationId,
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
          <ToolbarButton
            icon={<ArrowReply16Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('reply')}
          >
            Reply
          </ToolbarButton>
          <ToolbarButton
            icon={<ArrowForward16Regular />}
            disabled={composeDisabled}
            onClick={() => openComposer('forward')}
          >
            Forward
          </ToolbarButton>
          <ToolbarButton icon={<Send16Regular />} disabled={composeDisabled} onClick={() => openComposer('draft')}>
            Send
          </ToolbarButton>
          <ToolbarDivider />
          <ToolbarButton icon={<Save16Regular />} disabled={disabled} onClick={handleSaveDraft}>
            Save Draft
          </ToolbarButton>
          <ToolbarButton icon={<CloudArrowUp16Regular />} disabled={disabled} onClick={handleArchive}>
            Save to SharePoint
          </ToolbarButton>
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
