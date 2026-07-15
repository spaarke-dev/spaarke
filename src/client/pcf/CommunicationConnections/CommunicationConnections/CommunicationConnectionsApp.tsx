/**
 * CommunicationConnectionsApp — the React root for the multi-association review PCF.
 *
 * Reads the task-015 provenance + status from the control context, renders the
 * ported `ConnectionsEditor` (rail layout — the OOB form's 34% accessories
 * column), and wires the review affordances to the REAL write path:
 *   - Confirm / Accept-all / File-here → `applyRegardingSelection` (shared
 *     `applyResolverFields`, ADR-024) then advance `sprk_associationstatus` to
 *     Resolved once every review slot is confirmed.
 *   - Change → capture an override reason (feedback signal persisted into the
 *     provenance JSON; NO learning loop).
 *   - Link another → OOB `Xrm.Utility.lookupObjects` across the regarding catalog
 *     → `applyRegardingSelection`.
 *   - Create from this email → launch the target create form (Event / To Do /
 *     Invoice) via `Xrm.Navigation`. Full create-and-link defers to W5.
 *
 * No self-bootstrapped auth: the PCF reads/writes via the host `context.webAPI`
 * (ADR-022 / ADR-028). No new regarding mechanism (§11) — every write delegates
 * to the shared service through `ConnectionsWriteHandler`.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  MessageBar,
  MessageBarBody,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Textarea,
  Button,
} from '@fluentui/react-components';
import { TODO_REGARDING_CATALOG } from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { AssociationStatus, type ICommunicationRecord } from './types';
import { parseProvenance, deriveConnections, connectionTarget, type Connection, type CreateAction } from './provenance';
import { ConnectionsEditor } from './ConnectionsEditor';
import {
  applyRegardingSelection,
  advanceAssociationStatus,
  persistOverrideReason,
  type IResolverWriteContext,
  type IRegardingSelection,
} from './handlers/ConnectionsWriteHandler';

const useStyles = makeStyles({
  root: { height: '100%', width: '100%', display: 'flex', flexDirection: 'column' },
  errorWrap: { padding: tokens.spacingHorizontalM },
  footer: {
    marginTop: 'auto',
    paddingTop: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalL,
    display: 'flex',
    justifyContent: 'flex-end',
  },
  versionText: { fontSize: tokens.fontSizeBase100, color: tokens.colorNeutralForeground3 },
  dialogHint: { color: tokens.colorNeutralForeground3, paddingBottom: tokens.spacingVerticalS },
});

// The create-from-email action → target entity logical name.
const CREATE_TARGET_ENTITY: Record<CreateAction['kind'], string> = {
  event: 'sprk_event',
  todo: 'sprk_todo',
  invoice: 'sprk_invoice',
};

/** Walk window/parent frames to locate Xrm (PCF runs in an iframe). */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function getXrm(): any {
  // Cross-origin frame access can throw SecurityError; guard defensively.
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const w = window as any;
    return w.Xrm ?? w.parent?.Xrm ?? w.top?.Xrm;
  } catch {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return (window as any).Xrm;
  }
}

/** Resolve the host communication record GUID from Xrm.Page. */
function getHostRecordId(): string | undefined {
  const xrm = getXrm();
  try {
    const id = xrm?.Page?.data?.entity?.getId?.();
    if (typeof id === 'string' && id.length > 0) {
      return id.replace(/[{}]/g, '');
    }
  } catch {
    /* ignore */
  }
  return undefined;
}

/** Refresh the host form after a write so bound fields update transparently. */
async function refreshForm(): Promise<void> {
  const xrm = getXrm();
  try {
    const data = xrm?.Page?.data;
    const refresh = data?.refresh;
    if (typeof refresh === 'function') {
      const r = refresh.call(data, true);
      if (r && typeof r.then === 'function') await r;
    }
  } catch (err) {
    console.warn('[CommunicationConnections] form refresh failed:', err);
  }
}

export interface ICommunicationConnectionsAppProps {
  context: ComponentFramework.Context<IInputs>;
  readOnly: boolean;
  version: string;
}

export const CommunicationConnectionsApp: React.FC<ICommunicationConnectionsAppProps> = ({
  context,
  readOnly,
  version,
}) => {
  const s = useStyles();

  const hostEntity = (context.parameters.entity?.raw ?? 'sprk_communication').trim() || 'sprk_communication';
  const provenanceRaw = context.parameters.associationProvenance?.raw ?? null;
  const statusRaw = context.parameters.associationStatus?.raw;
  const status = typeof statusRaw === 'number' ? statusRaw : statusRaw != null ? Number(statusRaw) : null;

  const showVersionFooterRaw = context.parameters.showVersionFooter?.raw;
  const showVersionFooter = showVersionFooterRaw !== false;

  const provenance = React.useMemo(() => parseProvenance(provenanceRaw), [provenanceRaw]);

  // Host record GUID is stable for the lifetime of a review surface (the record
  // always exists) — capture once (S5).
  const hostRecordId = React.useMemo(() => getHostRecordId(), []);

  const record = React.useMemo<ICommunicationRecord>(
    () => ({
      sprk_communicationid: hostRecordId ?? '',
      sprk_associationstatus: status,
      sprk_associationprovenance: provenanceRaw,
    }),
    [hostRecordId, status, provenanceRaw]
  );

  const writeCtx = React.useMemo<IResolverWriteContext>(
    () => ({
      webApi: context.webAPI as unknown as IResolverWriteContext['webApi'],
      hostEntity,
      hostRecordId,
    }),
    [context.webAPI, hostEntity, hostRecordId]
  );

  // Full review slot set (used to decide when to advance status to Resolved).
  const reviewSlots = React.useMemo<Connection[]>(() => {
    if (!provenance) return [];
    return deriveConnections(provenance, status === AssociationStatus.Resolved).filter(c => c.status !== 'confirmed');
  }, [provenance, status]);

  const [confirmedFields, setConfirmedFields] = React.useState<Set<string>>(new Set());
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Override-reason dialog state.
  const [overrideConn, setOverrideConn] = React.useState<Connection | null>(null);
  const [overrideReason, setOverrideReason] = React.useState('');

  /** Advance status to Resolved when every review slot is now confirmed. */
  const maybeAdvanceStatus = React.useCallback(
    async (nextConfirmed: Set<string>): Promise<void> => {
      if (reviewSlots.length === 0) return;
      const allConfirmed = reviewSlots.every(c => nextConfirmed.has(c.field));
      if (!allConfirmed) return;
      const res = await advanceAssociationStatus(writeCtx);
      if (!res.success) setError(res.error ?? 'Could not update the association status.');
    },
    [reviewSlots, writeCtx]
  );

  const fileSelection = React.useCallback(
    async (field: string, selection: IRegardingSelection): Promise<void> => {
      setBusy(true);
      setError(null);
      try {
        const res = await applyRegardingSelection(writeCtx, selection);
        if (!res.success) {
          setError(res.error ?? 'Could not file this connection.');
          return;
        }
        const next = new Set(confirmedFields).add(field);
        setConfirmedFields(next);
        await maybeAdvanceStatus(next);
        await refreshForm();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unexpected error while filing.');
      } finally {
        setBusy(false);
      }
    },
    [writeCtx, confirmedFields, maybeAdvanceStatus]
  );

  const handleConfirm = React.useCallback(
    (conn: Connection, chosen?: { targetEntity: string; targetId: string; targetName?: string }): void => {
      const selection: IRegardingSelection = chosen
        ? { entityType: chosen.targetEntity, recordId: chosen.targetId, recordName: chosen.targetName ?? chosen.targetId }
        : connectionTarget(conn);
      void fileSelection(conn.field, selection);
    },
    [fileSelection]
  );

  const handleAcceptAll = React.useCallback(
    (conns: Connection[]): void => {
      void (async () => {
        setBusy(true);
        setError(null);
        const next = new Set(confirmedFields);
        // Writes are additive (all typed lookups survive), but the denormalized
        // primary follows the LAST write. `conns` arrive in SLOT_META priority
        // order (Matter first); file in REVERSE so the highest-priority slot is
        // written last and owns the primary denorm fields (mirrors the engine's
        // priority-first primary selection).
        const ordered = [...conns].reverse();
        try {
          for (const conn of ordered) {
            const res = await applyRegardingSelection(writeCtx, connectionTarget(conn));
            if (!res.success) {
              setError(res.error ?? `Could not file ${conn.slotLabel}.`);
              // Persist the successes so far so the UI reflects reality (W1).
              setConfirmedFields(next);
              return;
            }
            next.add(conn.field);
          }
          setConfirmedFields(next);
          await maybeAdvanceStatus(next);
          await refreshForm();
        } catch (err) {
          setConfirmedFields(next);
          setError(err instanceof Error ? err.message : 'Unexpected error while filing.');
        } finally {
          setBusy(false);
        }
      })();
    },
    [writeCtx, confirmedFields, maybeAdvanceStatus]
  );

  const handleLinkAnother = React.useCallback((): void => {
    const xrm = getXrm();
    const lookup = xrm?.Utility?.lookupObjects;
    if (typeof lookup !== 'function') {
      setError('The record picker is unavailable in this host.');
      return;
    }
    void (async () => {
      try {
        const results = await lookup({
          entityTypes: TODO_REGARDING_CATALOG.map(c => c.entityType),
          allowMultiSelect: false,
        });
        const picked = Array.isArray(results) ? results[0] : undefined;
        if (!picked?.id || !picked?.entityType) return;
        await fileSelection(`sprk_regarding_${picked.entityType}`, {
          entityType: picked.entityType,
          recordId: String(picked.id).replace(/[{}]/g, ''),
          recordName: typeof picked.name === 'string' ? picked.name : String(picked.id),
        });
      } catch (err) {
        console.warn('[CommunicationConnections] lookupObjects failed:', err);
      }
    })();
  }, [fileSelection]);

  const handleCreate = React.useCallback((action: CreateAction): void => {
    const xrm = getXrm();
    const entityName = CREATE_TARGET_ENTITY[action.kind];
    const onLaunchError = (err: unknown) =>
      console.warn('[CommunicationConnections] create-from-email launch failed:', err);
    try {
      // R4: launch the target create form. Full create-and-link defers to W5.
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

  const handleChange = React.useCallback((conn: Connection): void => {
    setOverrideConn(conn);
    setOverrideReason('');
  }, []);

  const submitOverride = React.useCallback((): void => {
    const conn = overrideConn;
    if (!conn) return;
    const reason = overrideReason.trim();
    setOverrideConn(null);
    if (!reason || !provenanceRaw) return;
    void (async () => {
      const res = await persistOverrideReason(writeCtx, provenanceRaw, conn.field, reason, new Date().toISOString());
      if (!res.success) {
        setError(res.error ?? 'Could not save the override reason.');
        return;
      }
      await refreshForm();
    })();
  }, [overrideConn, overrideReason, provenanceRaw, writeCtx]);

  return (
    <div className={s.root}>
      {error && (
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      <ConnectionsEditor
        record={record}
        provenance={provenance}
        layout="rail"
        readOnly={readOnly}
        busy={busy}
        confirmedFields={confirmedFields}
        onConfirm={handleConfirm}
        onAcceptAll={handleAcceptAll}
        onChange={handleChange}
        onLinkAnother={handleLinkAnother}
        onCreate={handleCreate}
      />

      {showVersionFooter && (
        <div className={s.footer}>
          <Text className={s.versionText}>v{version} • Built 2026-07-15</Text>
        </div>
      )}

      <Dialog open={overrideConn !== null} onOpenChange={(_, d) => !d.open && setOverrideConn(null)}>
        <DialogSurface>
          <DialogBody>
            <DialogTitle>Change {overrideConn?.slotLabel} connection</DialogTitle>
            <DialogContent>
              <Text block className={s.dialogHint}>
                Tell us why the suggested {overrideConn?.slotLabel} isn&apos;t right (captured as a feedback signal).
                Use “Link another record…” to file a different one.
              </Text>
              <Textarea
                value={overrideReason}
                onChange={(_, d) => setOverrideReason(d.value)}
                placeholder="e.g. wrong matter — this belongs to the Henderson engagement"
                resize="vertical"
              />
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={() => setOverrideConn(null)}>
                Cancel
              </Button>
              <Button appearance="primary" disabled={!overrideReason.trim()} onClick={submitOverride}>
                Save reason
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
