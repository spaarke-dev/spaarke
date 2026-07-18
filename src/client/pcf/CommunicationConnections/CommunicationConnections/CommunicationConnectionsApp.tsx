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
  Badge,
  Tooltip,
} from '@fluentui/react-components';
import { Open16Regular, Dismiss24Regular } from '@fluentui/react-icons';
import {
  TODO_REGARDING_CATALOG,
  cleanGuid,
  resolveRecordDisplayNameFieldName,
  type IPolymorphicWebApi,
} from '@spaarke/ui-components';
import { IInputs } from './generated/ManifestTypes';
import { AssociationStatus, type ICommunicationRecord } from './types';
import {
  parseProvenance,
  deriveConnections,
  mergeFiledConnections,
  deriveAiSuggestedTypes,
  connectionTarget,
  COMMUNICATION_REGARDING_FIELDS,
  type Connection,
} from './provenance';
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

  // Collapsed on-form card (mirrors the RegardingResolver "RELATED RECORD" look:
  // primary number + name, with an expand affordance into the review modal).
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingBlock: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
  },
  cardHeadRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  kicker: {
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  grow: { flex: 1 },
  primaryRow: { display: 'flex', alignItems: 'baseline', gap: tokens.spacingHorizontalM, minWidth: 0 },
  primaryNumber: {
    color: tokens.colorBrandForegroundLink,
    fontWeight: tokens.fontWeightSemibold,
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
  },
  primaryName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  emptyText: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase300 },
  // Wizard-standard modal footprint (~80vw × 80vh) so the review grid has room.
  modalSurface: { width: '80vw', maxWidth: '1200px', height: '80vh', maxHeight: '80vh' },
  modalBody: { height: '100%', display: 'flex', flexDirection: 'column', overflow: 'hidden' },
  // Close sits lower-left (Spaarke modal convention); the X in the title bar is the top-right close.
  modalActions: { justifyContent: 'flex-start' },
});

// Fallback primary-name field per entity, used ONLY when the `sprk_recordtype_ref`
// catalog has no `sprk_recorddisplaynamefield` row (resolveRecordDisplayNameFieldName
// returns null). The catalog is authoritative (SRFR-052); this map just guarantees
// the common targets never fall back to a raw GUID. OOB entities are certain;
// sprk_* entries follow the platform naming convention (a wrong guess simply 400s
// on $select and degrades to the GUID — no worse than before).
const PRIMARY_NAME_FALLBACK: Record<string, string> = {
  contact: 'fullname',
  account: 'name',
  sprk_matter: 'sprk_mattername',
  sprk_organization: 'sprk_name',
  sprk_project: 'sprk_name',
  sprk_invoice: 'sprk_name',
  sprk_event: 'sprk_name',
  sprk_servicerequest: 'sprk_name',
  sprk_workassignment: 'sprk_name',
};

/** Stable key for the resolved-display-name map: entity + normalized GUID. */
function displayNameKey(entity: string, id: string): string {
  return `${entity}:${cleanGuid(id)}`;
}

/** A regarding lookup that is actually populated on the communication (a filed association). */
export interface IFiledAssociation {
  entityType: string;
  recordId: string;
  recordName: string;
}

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

  // Resolve friendly display names for the suggested targets. The Association
  // Engine writes only the target GUID into the provenance JSON (no `targetName`),
  // so without this the review slots render a raw GUID. We resolve the per-entity
  // display-name field from the `sprk_recordtype_ref` catalog (the same SRFR-052
  // mechanism the write path uses) and read that field off each target record.
  // Best-effort: any failure leaves the slot on its GUID fallback (S-nonfatal).
  const [displayNames, setDisplayNames] = React.useState<ReadonlyMap<string, string>>(new Map());

  React.useEffect(() => {
    if (!provenance) return;
    let cancelled = false;
    const api = context.webAPI as unknown as IPolymorphicWebApi;

    // Unique (entity, id) pairs that still need a name (engine supplied none).
    const seen = new Set<string>();
    const pairs: { entity: string; id: string; key: string }[] = [];
    for (const c of provenance.candidates) {
      if (!c.targetEntity || !c.targetId || c.targetName) continue;
      const key = displayNameKey(c.targetEntity, c.targetId);
      if (seen.has(key)) continue;
      seen.add(key);
      pairs.push({ entity: c.targetEntity, id: cleanGuid(c.targetId), key });
    }
    if (pairs.length === 0) return;

    void (async () => {
      const next = new Map<string, string>();
      await Promise.all(
        pairs.map(async ({ entity, id, key }) => {
          try {
            const field = (await resolveRecordDisplayNameFieldName(api, entity)) ?? PRIMARY_NAME_FALLBACK[entity];
            if (!field) return;
            const rec = await context.webAPI.retrieveRecord(entity, id, `?$select=${field}`);
            const name = rec?.[field];
            if (typeof name === 'string' && name.trim().length > 0) next.set(key, name.trim());
          } catch (err) {
            console.warn(`[CommunicationConnections] display-name resolve failed for ${entity} ${id}:`, err);
          }
        })
      );
      if (!cancelled && next.size > 0) setDisplayNames(next);
    })();

    return () => {
      cancelled = true;
    };
  }, [provenance, context.webAPI]);

  /** Look up a resolved friendly name for a target, or undefined to use the fallback. */
  const resolveDisplayName = React.useCallback(
    (entity: string, id: string): string | undefined => displayNames.get(displayNameKey(entity, id)),
    [displayNames]
  );

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

  // ── Collapsed card + authoritative filed list ──────────────────────────────
  // The collapsed card shows the denormalized PRIMARY (number + name — the same
  // fields RegardingResolver surfaces). The modal lists EVERY filed association by
  // reading each populated typed `sprk_regarding*` lookup — so records added via
  // "Link another" (never in the engine's provenance) still appear. Re-read on a
  // `reloadKey` bump after each successful write.
  const [modalOpen, setModalOpen] = React.useState(false);
  const [reloadKey, setReloadKey] = React.useState(0);
  const [primaryDenorm, setPrimaryDenorm] = React.useState<{ number: string | null; name: string | null }>({
    number: null,
    name: null,
  });
  const [filedAssociations, setFiledAssociations] = React.useState<IFiledAssociation[]>([]);

  React.useEffect(() => {
    if (!hostRecordId) return;
    let cancelled = false;
    void (async () => {
      try {
        // Retrieve WITHOUT $select — the sprk_communication regarding lookups vary by
        // deployment and an unknown field name makes the whole $select throw (which is what
        // made the collapsed card fall back to "not filed" even when a primary was set).
        const rec = await context.webAPI.retrieveRecord('sprk_communication', hostRecordId);
        if (cancelled) return;
        setPrimaryDenorm({
          number: (rec['sprk_regardingrecordnumber'] as string) ?? null,
          name: (rec['sprk_regardingrecordname'] as string) ?? null,
        });
        // Use the COMMUNICATION regarding field names (sprk_regardingperson for Contact, …),
        // not the sprk_todo TODO_REGARDING_CATALOG names.
        const filed: IFiledAssociation[] = [];
        for (const { field, entityType } of COMMUNICATION_REGARDING_FIELDS) {
          const val = rec[`_${field}_value`];
          if (typeof val === 'string' && val) {
            const nm = rec[`_${field}_value@OData.Community.Display.V1.FormattedValue`];
            filed.push({
              entityType,
              recordId: cleanGuid(val),
              recordName: typeof nm === 'string' && nm ? nm : cleanGuid(val),
            });
          }
        }
        setFiledAssociations(filed);
      } catch (err) {
        console.warn('[CommunicationConnections] filed-association read failed:', err);
      }
    })();
    return () => {
      cancelled = true;
    };
    // Re-read when the bound association data changes too (e.g. the server auto-files
    // after the form is already open), not only on our own writes (reloadKey).
  }, [hostRecordId, context.webAPI, reloadKey, provenanceRaw, status]);

  // Full review slot set (used to decide when to advance status to Resolved).
  const reviewSlots = React.useMemo<Connection[]>(() => {
    if (!provenance) return [];
    return deriveConnections(provenance, status === AssociationStatus.Resolved).filter(c => c.status !== 'confirmed');
  }, [provenance, status]);

  const [confirmedFields, setConfirmedFields] = React.useState<Set<string>>(new Set());
  const [primaryField, setPrimaryField] = React.useState<string | null>(null);
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
        setReloadKey(k => k + 1);
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
        ? {
            entityType: chosen.targetEntity,
            recordId: chosen.targetId,
            recordName: chosen.targetName ?? chosen.targetId,
          }
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
          setReloadKey(k => k + 1);
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

  const handleLinkAnother = React.useCallback(
    (entityType?: string): void => {
      const xrm = getXrm();
      if (typeof xrm?.Utility?.lookupObjects !== 'function') {
        setError('The record picker is unavailable in this host.');
        return;
      }
      // Type-first UX: when the reviewer picked a type from the menu, scope the
      // side-pane lookup to that single entity (skips the OOB type-picker screen).
      // No type = all-types fallback.
      const entityTypes = entityType ? [entityType] : TODO_REGARDING_CATALOG.map(c => c.entityType);
      void (async () => {
        try {
          // Invoke as a METHOD on Xrm.Utility — a detached reference
          // (`const f = xrm.Utility.lookupObjects; f(...)`) loses its `this` and
          // throws "Cannot read properties of undefined (reading '_clientApiExecutor')".
          const results = await xrm.Utility.lookupObjects({
            entityTypes,
            allowMultiSelect: false,
          });
          const picked = Array.isArray(results) ? results[0] : undefined;
          if (!picked?.id || !picked?.entityType) return;
          // Use the catalog's canonical lookup attribute as the confirmed-fields key
          // (e.g. contact → sprk_regardingcontact) so status-advance tracking matches
          // the engine's slot fields.
          const field =
            TODO_REGARDING_CATALOG.find(c => c.entityType === picked.entityType)?.lookupAttribute ??
            `sprk_regarding_${picked.entityType}`;
          await fileSelection(field, {
            entityType: picked.entityType,
            recordId: String(picked.id).replace(/[{}]/g, ''),
            recordName: typeof picked.name === 'string' ? picked.name : String(picked.id),
          });
        } catch (err) {
          console.warn('[CommunicationConnections] lookupObjects failed:', err);
        }
      })();
    },
    [fileSelection]
  );

  // Launch the create form for an AI-suggested type (e.g. "Create Matter"). R4 launches
  // the quick-create form; full create-and-link is the Notification-Spine project.
  const handleCreateType = React.useCallback((entityType: string): void => {
    const xrm = getXrm();
    const onLaunchError = (err: unknown) => console.warn('[CommunicationConnections] create-type launch failed:', err);
    try {
      if (typeof xrm?.Navigation?.openForm === 'function') {
        Promise.resolve(xrm.Navigation.openForm({ entityName: entityType, useQuickCreateForm: true })).catch(
          onLaunchError
        );
      } else if (typeof xrm?.Navigation?.navigateTo === 'function') {
        Promise.resolve(
          xrm.Navigation.navigateTo(
            { pageType: 'entityrecord', entityName: entityType },
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

  const handleSetPrimary = React.useCallback(
    (conn: Connection): void => {
      // The primary owns the denormalized Regarding Record fields. Re-filing the
      // (already-confirmed) target is idempotent for its typed lookup and points
      // the denorm fields at it — additive, so sibling associations are untouched.
      setPrimaryField(conn.field);
      void fileSelection(conn.field, connectionTarget(conn));
    },
    [fileSelection]
  );

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

  const connectionsForCount = mergeFiledConnections(
    provenance ? deriveConnections(provenance, status === AssociationStatus.Resolved) : [],
    filedAssociations
  );
  // AI-suggested types (e.g. a "new Matter") are review items too — count them so the card
  // flags there's something to act on even when a contact already auto-filed.
  const aiSuggestionCount = provenance
    ? deriveAiSuggestedTypes(provenance, new Set(connectionsForCount.map(c => c.entity))).length
    : 0;
  const toReviewCount =
    connectionsForCount.filter(c => c.status !== 'confirmed' && !confirmedFields.has(c.field)).length +
    aiSuggestionCount;

  return (
    <div className={s.root}>
      {error && (
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* Collapsed on-form card — primary number + name, expand into the review modal. */}
      <div className={s.card}>
        <div className={s.cardHeadRow}>
          <Text className={s.kicker}>Related Record</Text>
          <div className={s.grow} />
          {toReviewCount > 0 && (
            <Badge appearance="tint" color="warning">
              {toReviewCount} to review
            </Badge>
          )}
          <Tooltip content="Review connections" relationship="label">
            <Button
              size="small"
              appearance="subtle"
              icon={<Open16Regular />}
              aria-label="Review connections"
              onClick={() => setModalOpen(true)}
            />
          </Tooltip>
        </div>
        {primaryDenorm.name ? (
          <div className={s.primaryRow}>
            {primaryDenorm.number && <Text className={s.primaryNumber}>{primaryDenorm.number}</Text>}
            <Text className={s.primaryName}>{primaryDenorm.name}</Text>
          </div>
        ) : (
          <Text className={s.emptyText}>
            {toReviewCount > 0
              ? `${toReviewCount} suggestion${toReviewCount === 1 ? '' : 's'} to review`
              : 'No connection filed yet'}
          </Text>
        )}
        {showVersionFooter && <Text className={s.versionText}>v{version}</Text>}
      </div>

      {/* Review / reconcile modal — the full connections surface. */}
      <Dialog open={modalOpen} onOpenChange={(_, d) => setModalOpen(d.open)}>
        <DialogSurface className={s.modalSurface}>
          <DialogBody>
            <DialogTitle
              action={
                <Button
                  appearance="subtle"
                  aria-label="Close"
                  icon={<Dismiss24Regular />}
                  onClick={() => setModalOpen(false)}
                />
              }
            >
              Connections
            </DialogTitle>
            <DialogContent className={s.modalBody}>
              <ConnectionsEditor
                record={record}
                provenance={provenance}
                layout="rail"
                readOnly={readOnly}
                busy={busy}
                confirmedFields={confirmedFields}
                primaryField={primaryField ?? undefined}
                resolveDisplayName={resolveDisplayName}
                filedAssociations={filedAssociations}
                onConfirm={handleConfirm}
                onAcceptAll={handleAcceptAll}
                onChange={handleChange}
                onSetPrimary={handleSetPrimary}
                onLinkAnother={handleLinkAnother}
                onCreateType={handleCreateType}
              />
            </DialogContent>
            <DialogActions className={s.modalActions}>
              <Button appearance="secondary" onClick={() => setModalOpen(false)}>
                Close
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

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
