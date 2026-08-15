/**
 * ReconciliationWorkspaceWidget.tsx
 *
 * Direct-widget mount for the shared `ReconciliationWorkspace` composition root
 * (email-communication-intelligence-r2 task 061/062, Pillar E). Registered as
 * the SpaarkeAi workspace widget type `communications-reconciliation` in
 * `register-workspace-widgets.ts`. Dual-mount with the standalone
 * `sprk_communicationreconciliation` Code Page (`src/solutions/CommunicationReconciliation`)
 * — BOTH mounts render the SAME `ReconciliationWorkspace` from
 * `@spaarke/communication-components` unchanged; only the host-adapter resolution
 * differs per mount (this file for the SpaarkeAi direct widget, the code-page
 * `main.tsx` for the standalone tab).
 *
 * `ReconciliationWorkspace` is host-agnostic (ADR-012) and declares its
 * dependencies as plain props rather than resolving them itself. This file's
 * ONLY job is that resolution (mirrors `EmailWorkspaceWidget.tsx`):
 *   - `dataverseClient` — the Xrm-backed `XrmDataverseClient` from
 *     `@spaarke/ui-components` (same factory the entity-view widgets use).
 *   - `webApi` — the raw `Xrm.WebApi` bridge (the same Xrm.WebApi-compatible
 *     shape PCF hosts pass as `context.webAPI`), used to build the row's
 *     `EmailConnectionsReview` write context + picker (052 reused write path).
 *   - `authenticatedFetch` — from `useAiSession()` (this widget only ever mounts
 *     inside SpaarkeAi's `AiSessionProvider` tree); threads BFF queue-feed /
 *     apply / dismiss / `.eml` render calls (ADR-028).
 *   - `resolveReview` / `resolveRegarding` — the row → picker-props and row →
 *     confirmed-regarding adapters; `resolveRegarding` reuses the shipped pure
 *     `derivePrimaryReview` reducer (NFR-10 gate), no bespoke fetch.
 *
 * `configId` is NOT passed — `ReconciliationGrid` defaults to its
 * `NEEDS_REVIEW_CONFIG_ID` placeholder (task 059 seeds + sets the real record).
 *
 * ADR-039 / BFF §10 (Path C): surface identity stays in CODE — no server-side
 * surface-identity endpoint/record. ADR-022: React 19, NOT PCF-safe.
 */
import * as React from 'react';
import { makeStyles } from '@fluentui/react-components';
import {
  ReconciliationWorkspace,
  derivePrimaryReview,
  type EmailConnectionsReviewProps,
  type ReconcileRegarding,
  type EmailWorkspaceWebApi,
  type CreatedRecordRef,
  type EmlSource,
} from '@spaarke/communication-components';
import { XrmDataverseClient, getXrm } from '@spaarke/ui-components';
import { useAiSession } from '../../providers/useAiSession';
import type { WorkspaceWidgetProps } from '../../types/widget-types';
import { CreateRecordChooserModal, type ChooserFileArgs } from '@spaarke/ui-components';

const COMMUNICATION_ENTITY = 'sprk_communication';
const PRIMARY_ID_FIELD = 'sprk_communicationid';

// Bounded-height host for the DIRECT widget mount — same full-height-widget
// pattern used by `EmailWorkspaceWidget` / `CommunicationsWorkspaceWidget`: the
// SpaarkeAi tab/section content area is CONTENT-DRIVEN, so a plain `height:100%`
// collapses to auto. Declare an explicit viewport-based FLOOR + matching CAP so
// the widget pins to a DEFINITE box and the grid + browse shell scroll within it.
const useStyles = makeStyles({
  host: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    minWidth: 0,
    minHeight: 'calc(100vh - 200px)',
    maxHeight: 'calc(100vh - 200px)',
    overflow: 'hidden',
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
 * Row → CONFIRMED `ReconcileRegarding | null` (NFR-10 gate). Reuses the shipped
 * pure `derivePrimaryReview` reducer — the SAME logic the resolver body uses to
 * decide the single confirmed primary. Only a green (Resolved) primary with a
 * resolvable typed entity + id enables the Fields/Tasks tabs.
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
 * Direct workspace widget wrapper for `<ReconciliationWorkspace />`.
 *
 * `data` / `isLoading` / `error` / `className` / `isActiveTab` from
 * `WorkspaceWidgetProps` are intentionally unused — the reconciliation surface is
 * not AI-directed data; it drives its own Dataverse reads via the embedded
 * `ReconciliationGrid` once mounted with host adapters.
 */
export const ReconciliationWorkspaceWidget: React.FC<WorkspaceWidgetProps> = () => {
  const styles = useStyles();
  const { authenticatedFetch, bffBaseUrl, chatSessionId, setChatSessionId } = useAiSession();

  // Bumped after an association is confirmed so the grid re-loads and
  // `resolveRegarding` reflects the new association (the prop's documented
  // "host refreshes the grid" contract). Used as a React `key` remount signal.
  const [refreshKey, setRefreshKey] = React.useState(0);
  const handleAssociationsChanged = React.useCallback(() => setRefreshKey(n => n + 1), []);

  // ── Task 064 (E1b/E1c): "New record" → full Quick Start menu → confirmable candidate ──
  // The chooser (shared `CreateRecordChooserModal`, reusing `GetStartedCardsWidget` +
  // `launchSurface`) opens on "New record"; a committed record resolves the launcher
  // promise, and `ReconciliationWorkspace` files it via the shared confirm path (NFR-10).
  const [chooserOpen, setChooserOpen] = React.useState(false);
  const [chooserFileArgs, setChooserFileArgs] = React.useState<ChooserFileArgs | undefined>(undefined);
  const chooserResolveRef = React.useRef<((ref: CreatedRecordRef | null) => void) | null>(null);

  // E1c: materialize the reconciled email's archived `.eml` into a chat session so the
  // launched wizard's AI-prepopulate step opens pre-seeded. Best-effort (NFR-04): any
  // failure → undefined and the wizard opens un-seeded (E1b still confirms).
  const buildEmlFileArgs = React.useCallback(
    async (emlSource?: EmlSource): Promise<ChooserFileArgs | undefined> => {
      const idBody = emlSource?.communicationId
        ? { communicationId: emlSource.communicationId }
        : emlSource?.documentId
          ? { documentId: emlSource.documentId }
          : null;
      if (!idBody || !bffBaseUrl) return undefined;
      try {
        let sid = chatSessionId;
        if (!sid) {
          const created = await authenticatedFetch(`${bffBaseUrl}/api/ai/chat/sessions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({}),
          });
          if (!created.ok) return undefined;
          sid = ((await created.json()) as { sessionId?: string }).sessionId ?? null;
          if (sid) setChatSessionId(sid);
        }
        if (!sid) return undefined;
        const res = await authenticatedFetch(
          `${bffBaseUrl}/api/ai/chat/sessions/${encodeURIComponent(sid)}/documents/from-document`,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(idBody),
          }
        );
        if (!res.ok) return undefined;
        const { fileId, fileName } = (await res.json()) as { fileId: string; fileName: string };
        return {
          fileIds: [fileId],
          source: { sessionId: sid },
          provenance: { sourceFiles: fileName ? [fileName] : [] },
        };
      } catch {
        return undefined;
      }
    },
    [authenticatedFetch, bffBaseUrl, chatSessionId, setChatSessionId]
  );

  const onLaunchCreateRecord = React.useCallback(
    async (ctx: { emlSource?: EmlSource }): Promise<CreatedRecordRef | null> => {
      const fileArgs = await buildEmlFileArgs(ctx.emlSource);
      return new Promise<CreatedRecordRef | null>(resolve => {
        chooserResolveRef.current = resolve;
        setChooserFileArgs(fileArgs);
        setChooserOpen(true);
      });
    },
    [buildEmlFileArgs]
  );

  // E1c: pass the row's `sprk_communicationid` (ALWAYS present) — the BFF resolves that
  // communication's archived `.eml` sprk_document server-side (via the sprk_communication
  // lookup + sprk_isemailarchive), so no grid-column / client-side document lookup is needed.
  // Best-effort: a communication with no archived .eml simply opens the wizard un-seeded.
  const resolveEmlSource = React.useCallback((record: Record<string, unknown>): EmlSource | undefined => {
    const communicationId = str(record, PRIMARY_ID_FIELD);
    return communicationId ? { communicationId } : undefined;
  }, []);

  const handleChooserResult = React.useCallback((ref: CreatedRecordRef | null): void => {
    chooserResolveRef.current?.(ref);
    chooserResolveRef.current = null;
    setChooserOpen(false);
  }, []);

  const handleChooserClose = React.useCallback((): void => setChooserOpen(false), []);

  // One Xrm-backed adapter set per mount (matches `XrmDataverseClient`'s
  // "instantiate once" guidance — see DataGrid.tsx's defaultClientRef pattern).
  const dataverseClient = React.useMemo(() => new XrmDataverseClient(), []);
  // Xrm.WebApi-compatible bridge for the associations additive-write path (052)
  // + the embedded PolymorphicPicker — the same object shape a PCF host passes as
  // `context.webAPI`. Satisfies BOTH `IResolverWriteContext['webApi']` and
  // `IPolymorphicPickerWebApi` (= `EmailWorkspaceWebApi`).
  const webApi = React.useMemo(() => getXrm()?.WebApi as EmailWorkspaceWebApi | undefined, []);

  const resolveReview = React.useMemo(() => {
    if (!webApi) return undefined;
    return (record: Record<string, unknown>): EmailConnectionsReviewProps => {
      const id = str(record, PRIMARY_ID_FIELD) ?? '';
      return {
        communicationId: id,
        associationStatus: num(record, 'sprk_associationstatus'),
        associationProvenanceJson: str(record, 'sprk_associationprovenance'),
        regardingRecordName: str(record, 'sprk_regardingrecordname'),
        regardingRecordNumber: str(record, 'sprk_regardingrecordnumber'),
        regardingRecordType: str(record, 'sprk_regardingrecordtypename'),
        writeContext: { webApi, hostEntity: COMMUNICATION_ENTITY, hostRecordId: id },
        pickerWebApi: webApi,
        onAssociationsChanged: handleAssociationsChanged,
      };
    };
  }, [webApi, handleAssociationsChanged]);

  if (!webApi) {
    // No Dataverse host available (e.g. a non-MDA dev shell). The reconciliation
    // write path cannot be resolved — fail closed rather than mount a
    // partially-wired component (mirrors `EmailWorkspaceWidget`).
    return null;
  }

  return (
    <div className={styles.host} data-testid="reconciliation-widget-scroll-host">
      <ReconciliationWorkspace
        key={refreshKey}
        dataverseClient={dataverseClient}
        authenticatedFetch={authenticatedFetch}
        resolveReview={resolveReview}
        resolveRegarding={resolveRegarding}
        onAssociationsChanged={handleAssociationsChanged}
        onLaunchCreateRecord={onLaunchCreateRecord}
        resolveEmlSource={resolveEmlSource}
      />
      <CreateRecordChooserModal
        open={chooserOpen}
        bffBaseUrl={bffBaseUrl ?? ''}
        fileArgs={chooserFileArgs}
        onResult={handleChooserResult}
        onClose={handleChooserClose}
      />
    </div>
  );
};

export default ReconciliationWorkspaceWidget;
