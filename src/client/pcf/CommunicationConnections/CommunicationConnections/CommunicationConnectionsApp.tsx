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
  Link,
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
  Toolbar,
  ToolbarButton,
  Tooltip,
} from '@fluentui/react-components';
import { ArrowClockwiseRegular, Dismiss24Regular, Search20Regular } from '@fluentui/react-icons';
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
  groupConnectionsByAction,
  connectionTarget,
  COMMUNICATION_REGARDING_FIELDS,
  type Connection,
} from './provenance';
import { ConnectionsEditor } from './ConnectionsEditor';
import { resolveTitle } from './title';
import {
  applyRegardingSelection,
  advanceAssociationStatus,
  persistOverrideReason,
  unlinkRegarding,
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

  // ── Collapsed on-form card — RegardingResolver "RELATED RECORD" parity (task 131 / UAT R5) ──
  //
  // This card is styled to be visually + behaviorally indistinguishable from the
  // RegardingResolver PCF card that sits next to it on the sprk_communication form.
  // Row 1 = OOB-styled uppercase title (non-clickable) + refresh + lookup; Row 2 =
  // 1fr:2fr Regarding Number (Link) / Regarding Name (Text) grid with OOB field labels.
  //
  // Path-A OOB-parity exception to ADR-021 (documented, owner-approved): the `title`,
  // `fieldLabel`, and `recordName` styles use hardcoded Segoe/px/#hex literals COPIED
  // VERBATIM from RegardingResolverApp so both cards match the Dataverse OOB
  // section-header + field-label spec exactly (Segoe UI 14px/600/#242424 title;
  // 12px/400/#616161 labels). Semantic tokens are the visual target elsewhere; here
  // OOB parity is. Mirrors the RegardingResolver styles-block exception note.
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    // RegardingResolver parity: paddingTop 0 so the OOB-styled title aligns with the
    // adjacent OOB section headers; right/bottom/left preserved at spacingHorizontalS.
    paddingTop: 0,
    paddingRight: tokens.spacingHorizontalS,
    paddingBottom: tokens.spacingHorizontalS,
    paddingLeft: tokens.spacingHorizontalS,
  },
  row1: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    minHeight: '32px',
  },
  // Row-1 title — Dataverse OOB section-header parity (copied verbatim from
  // RegardingResolverApp `title`: Segoe UI 14px / weight 600 / #242424 / padding
  // '2px 0px 4px' / uppercase / letterSpacing 0). Documented Path-A exception to
  // ADR-021. This row is NOT clickable (task 131 reverts the W11 B11-4 header-opens-modal).
  title: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '14px',
    fontWeight: 600,
    color: '#242424',
    padding: '2px 0px 4px',
    letterSpacing: 0,
    textTransform: 'uppercase',
  },
  // Row-1 actions area — refresh (left) + lookup (right).
  row1Actions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  refreshToolbar: {
    paddingLeft: 0,
    paddingRight: 0,
    minHeight: 'auto',
  },
  // Flip the lookup magnifier horizontally so it matches the OOB Dataverse lookup
  // icon direction (RegardingResolver applies the same scaleX(-1) to its picker svg).
  lookupIcon: {
    transform: 'scaleX(-1)',
  },
  // Modal DialogTitle still uses the token-based section-header (ADR-021). The card
  // title above uses the OOB-parity `title` style; this stays for the modal only.
  sectionHeader: {
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase300,
    minHeight: '20px',
    paddingTop: tokens.spacingVerticalNone,
    paddingBottom: tokens.spacingVerticalM,
  },
  // Row-2 — 1fr:2fr grid (RegardingResolver parity). Number cell left (1/3),
  // Name cell right (2/3). Empty cells hide entirely (no em-dash).
  row2: {
    display: 'grid',
    gridTemplateColumns: '1fr 2fr',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXS}`,
    minHeight: '24px',
    alignItems: 'start',
  },
  numberCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  nameCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  // Row-2 field label — OOB-parity (12px / 400 / #616161 / Segoe), copied verbatim
  // from RegardingResolverApp `fieldLabel`. Documented Path-A exception to ADR-021.
  fieldLabel: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '12px',
    fontWeight: 400,
    color: '#616161',
    lineHeight: '16px',
  },
  recordNumber: {
    fontWeight: tokens.fontWeightSemibold,
  },
  // Row-2 record-name value — plain text (NOT a Link). Segoe UI 14px / 400 / #242424,
  // ellipsis on overflow. Copied verbatim from RegardingResolverApp `recordName`.
  recordName: {
    fontFamily:
      '"Segoe UI", "Segoe UI Web (West European)", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
    fontSize: '14px',
    fontWeight: 400,
    color: '#242424',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  // Wizard-standard modal footprint (~80vw × 80vh) so the review grid has room.
  // Flex column so the DialogBody fills the full surface height (B11-6) — the
  // content region grows and the footer pins to the bottom (no empty gap below).
  modalSurface: {
    width: '80vw',
    maxWidth: '1200px',
    height: '80vh',
    maxHeight: '80vh',
    display: 'flex',
    flexDirection: 'column',
  },
  // DialogBody stretches to fill the surface; it keeps its own auto/1fr/auto grid
  // (title / content / actions) so the content row grows and actions pin bottom.
  modalBody: { flexGrow: 1, minHeight: 0 },
  // Content row fills the 1fr track; inner ConnectionsEditor rail owns the scroll
  // (overflow hidden here avoids a double scrollbar).
  modalContent: { minHeight: 0, overflowY: 'hidden', display: 'flex', flexDirection: 'column' },
  // Pinned bottom footer bar holding the Save action (B11-8), right-aligned.
  modalFooter: {
    justifyContent: 'flex-end',
    borderTopWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    paddingTop: tokens.spacingVerticalM,
  },
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

/**
 * Row-1 refresh-button handler — RegardingResolver `handleRefreshInternal` parity
 * (task 131). Saves the form (commits any dirty buffered attributes) then refreshes
 * from the server (`data.refresh(true)`, parity with the OOB Refresh ribbon command).
 * Falls back to `window.location.reload()` when no MDA refresh API is available
 * (test harness / canvas app). Defensive throughout — never throws to the host form.
 */
async function handleRefreshInternal(): Promise<void> {
  const xrm = getXrm();
  try {
    const data = xrm?.Page?.data;
    const save = data?.entity?.save;
    if (typeof save === 'function') {
      const saveResult = save.call(data.entity);
      if (saveResult && typeof saveResult.then === 'function') await saveResult;
    }
    const refresh = data?.refresh;
    if (typeof refresh === 'function') {
      const refreshResult = refresh.call(data, true);
      if (refreshResult && typeof refreshResult.then === 'function') await refreshResult;
      return;
    }
    if (typeof window !== 'undefined' && typeof window.location?.reload === 'function') {
      window.location.reload();
    }
  } catch (err) {
    console.warn('[CommunicationConnections] Refresh failed:', err);
  }
}

/**
 * Resolve the entity + id the Row-2 record-number Link should open (the primary
 * regarding record). Primary source is the denormalized `sprk_regardingrecordurl`
 * (parse `etn` + `id`, mirroring RegardingResolver's buildRecordUrl round-trip);
 * fallback matches a filed association by the primary record name. Returns null
 * when no target can be resolved. Never throws.
 */
function resolvePrimaryOpenTarget(
  url: string | null,
  filed: IFiledAssociation[],
  primaryName: string | null
): { entityName: string; entityId: string } | null {
  if (typeof url === 'string' && url.length > 0) {
    try {
      const parsed = new URL(url);
      const etn = parsed.searchParams.get('etn');
      const id = parsed.searchParams.get('id');
      if (etn && id) {
        const cleanId = id.replace(/[{}]/g, '');
        if (cleanId.length > 0) return { entityName: etn, entityId: cleanId };
      }
    } catch {
      /* malformed URL — fall through to the filed-association fallback */
    }
  }
  if (primaryName) {
    const hit = filed.find(f => f.recordName === primaryName);
    if (hit?.entityType && hit?.recordId) {
      return { entityName: hit.entityType, entityId: cleanGuid(hit.recordId) };
    }
  }
  return null;
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

  // Configurable section/modal title (default "RELATED RECORDS"), B11-2.
  const title = resolveTitle(context.parameters.titleText?.raw);

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
  // The collapsed card shows the PRIMARY FILED association (number + name — see the
  // `primaryFiled` derivation below; task 132 / UAT R5). The denormalized primary
  // (number/name/url) is still read here as the fallback source when nothing is
  // filed yet (outbound draft). The modal lists EVERY filed association by reading
  // each populated typed `sprk_regarding*` lookup — so records added via "Link
  // another" (never in the engine's provenance) still appear. Re-read on a
  // `reloadKey` bump after each successful write.
  const [modalOpen, setModalOpen] = React.useState(false);
  const [reloadKey, setReloadKey] = React.useState(0);
  const [primaryDenorm, setPrimaryDenorm] = React.useState<{
    number: string | null;
    name: string | null;
    url: string | null;
  }>({
    number: null,
    name: null,
    url: null,
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
          // Captured for the Row-2 number Link open-target (task 131). Written by the
          // same synchronous outbound enrichment as number/name — the denorm fields are
          // the reliable source (provenance is a no-op for outbound).
          url: (rec['sprk_regardingrecordurl'] as string) ?? null,
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

  // ── Card source of truth: the PRIMARY FILED ASSOCIATION (task 132 / UAT R5) ──
  // The on-form card sources its Regarding Number + Name from the primary FILED
  // association — the SAME reliable path the modal's "Filed" section uses (the
  // matter's real name + provenance-derived number) — NOT the denormalized
  // sprk_regardingrecord* fields, which the INBOUND association path writes
  // backwards (the record NUMBER lands in the NAME field, number field null).
  // Merge the engine's suggested slots with the record's actually-filed typed
  // lookups, group, and take the ★ primary (the explicitly designated primaryField
  // when confirmed, else the first filed slot in SLOT_META priority order — the
  // exact `effectivePrimary` rule ConnectionsEditor uses). Falls back to the denorm
  // fields when nothing is filed yet, so an outbound draft still shows its regarding.
  const filedConnections = React.useMemo<Connection[]>(() => {
    const merged = mergeFiledConnections(
      provenance ? deriveConnections(provenance, status === AssociationStatus.Resolved) : [],
      filedAssociations
    );
    return groupConnectionsByAction(merged, [], confirmedFields, new Set<string>()).filed;
  }, [provenance, status, filedAssociations, confirmedFields]);

  const primaryFiled = React.useMemo<Connection | null>(() => {
    if (filedConnections.length === 0) return null;
    const eff =
      primaryField && filedConnections.some(c => c.field === primaryField)
        ? primaryField
        : filedConnections[0].field;
    return filedConnections.find(c => c.field === eff) ?? null;
  }, [filedConnections, primaryField]);

  // Card Name: the filed association's real record name (resolved friendly name when
  // available, else the lookup's formatted value). Card Number: the filed
  // association's provenance-derived reference number, falling back to the denorm
  // number (correct for outbound). Both fall back to the denorm fields when nothing
  // is filed yet.
  const cardName = primaryFiled
    ? resolveDisplayName(primaryFiled.entity, primaryFiled.targetId) ?? primaryFiled.targetName
    : primaryDenorm.name;
  const cardNumber = primaryFiled
    ? primaryFiled.recordNumber ?? primaryDenorm.number
    : primaryDenorm.number;

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

  // Unlink ONE filed association (null exactly that entity's typed regarding lookup —
  // additive-safe, siblings untouched; NOT a clear-and-set). Drops it from the
  // confirmed set + clears the primary designation if it was primary.
  const handleUnlink = React.useCallback(
    (conn: Connection): void => {
      void (async () => {
        setBusy(true);
        setError(null);
        try {
          const res = await unlinkRegarding(writeCtx, conn.entity);
          if (!res.success) {
            setError(res.error ?? 'Could not unlink this connection.');
            return;
          }
          setConfirmedFields(prev => {
            const nextSet = new Set(prev);
            nextSet.delete(conn.field);
            return nextSet;
          });
          setPrimaryField(prev => (prev === conn.field ? null : prev));
          await refreshForm();
          setReloadKey(k => k + 1);
        } catch (err) {
          setError(err instanceof Error ? err.message : 'Unexpected error while unlinking.');
        } finally {
          setBusy(false);
        }
      })();
    },
    [writeCtx]
  );

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

  // Row-1 refresh button — stable callback wrapping the module-level helper.
  const handleRefreshClick = React.useCallback(() => {
    void handleRefreshInternal();
  }, []);

  // Row-2 record-number Link — opens the primary regarding record in a Dataverse
  // modal (Xrm.Navigation.navigateTo, target: 2), mirroring RegardingResolver's
  // number-hyperlink. Opening a related record is a VIEW action — safe even when
  // the number/name come straight from the denormalized fields. Defensive: unresolved
  // target or missing Xrm → warn + no-op; never throws to the host form.
  const handleRecordNumberClick = React.useCallback(
    (e: React.MouseEvent) => {
      e.preventDefault();
      // Prefer the primary FILED association's entity + id (the reliable target that
      // matches the Number/Name now shown on the card); fall back to the denorm URL
      // round-trip only when nothing is filed yet.
      const target = primaryFiled
        ? { entityName: primaryFiled.entity, entityId: cleanGuid(primaryFiled.targetId) }
        : resolvePrimaryOpenTarget(primaryDenorm.url, filedAssociations, primaryDenorm.name);
      if (!target) {
        console.warn('[CommunicationConnections] Cannot open regarding record — no target resolved.');
        return;
      }
      const xrm = getXrm();
      if (typeof xrm?.Navigation?.navigateTo !== 'function') {
        console.warn('[CommunicationConnections] Xrm.Navigation.navigateTo unavailable; cannot open record.');
        return;
      }
      try {
        const result = xrm.Navigation.navigateTo(
          { pageType: 'entityrecord', entityName: target.entityName, entityId: target.entityId },
          { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } }
        );
        if (result && typeof result.catch === 'function') {
          result.catch((err: unknown) =>
            console.warn('[CommunicationConnections] navigateTo rejected:', err)
          );
        }
      } catch (err) {
        console.warn('[CommunicationConnections] navigateTo threw:', err);
      }
    },
    [primaryFiled, primaryDenorm.url, primaryDenorm.name, filedAssociations]
  );

  const hasNumber = typeof cardNumber === 'string' && cardNumber.trim().length > 0;
  const hasName = typeof cardName === 'string' && cardName.trim().length > 0;

  return (
    <div className={s.root}>
      {error && (
        <div className={s.errorWrap}>
          <MessageBar intent="error">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}

      {/* Collapsed on-form card — RegardingResolver "RELATED RECORD" parity (task 131).
          Row 1: OOB-styled uppercase title (NOT clickable) + refresh + lookup icons.
          Row 2: 1fr:2fr Regarding Number (Link) / Regarding Name (Text) grid with
          OOB field labels; empty cells hide. The lookup icon is the ONLY modal opener. */}
      <div className={s.card} data-testid="cc-card">
        <div className={s.row1} data-testid="cc-row-1">
          <Text className={s.title} data-testid="cc-title">
            {title}
          </Text>
          {!readOnly && (
            <div className={s.row1Actions}>
              <Toolbar className={s.refreshToolbar} size="small" aria-label="Connection actions">
                <Tooltip content="Refresh form" relationship="label" withArrow>
                  <ToolbarButton
                    icon={<ArrowClockwiseRegular />}
                    aria-label="Refresh form"
                    data-testid="cc-refresh"
                    onClick={handleRefreshClick}
                  />
                </Tooltip>
                <Tooltip content="Review connections" relationship="label" withArrow>
                  <ToolbarButton
                    icon={<Search20Regular className={s.lookupIcon} />}
                    aria-label="Review connections"
                    data-testid="cc-lookup"
                    onClick={() => setModalOpen(true)}
                  />
                </Tooltip>
              </Toolbar>
            </div>
          )}
        </div>

        <div className={s.row2} data-testid="cc-row-2">
          {hasNumber && (
            <div className={s.numberCell} data-testid="cc-number-cell">
              <Text className={s.fieldLabel}>Regarding Number</Text>
              <Link
                className={s.recordNumber}
                role="link"
                data-testid="cc-record-number"
                onClick={handleRecordNumberClick}
              >
                {cardNumber}
              </Link>
            </div>
          )}
          {hasName && (
            <div className={s.nameCell} data-testid="cc-name-cell">
              <Text className={s.fieldLabel}>Regarding Name</Text>
              <Text className={s.recordName} data-testid="cc-record-name">
                {cardName}
              </Text>
            </div>
          )}
        </div>

        {showVersionFooter && (
          <div className={s.footer} data-testid="cc-footer">
            <Text className={s.versionText}>v{version}</Text>
          </div>
        )}
      </div>

      {/* Review / reconcile modal — the full connections surface. */}
      <Dialog open={modalOpen} onOpenChange={(_, d) => setModalOpen(d.open)}>
        <DialogSurface className={s.modalSurface}>
          <DialogBody className={s.modalBody}>
            <DialogTitle
              className={s.sectionHeader}
              action={
                <Button
                  appearance="subtle"
                  aria-label="Close"
                  icon={<Dismiss24Regular />}
                  onClick={() => setModalOpen(false)}
                />
              }
            >
              {title}
            </DialogTitle>
            <DialogContent className={s.modalContent}>
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
                onUnlink={handleUnlink}
                onLinkAnother={handleLinkAnother}
                onCreateType={handleCreateType}
              />
            </DialogContent>
            <DialogActions className={s.modalFooter}>
              <Button appearance="primary" onClick={() => setModalOpen(false)}>
                Save
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
