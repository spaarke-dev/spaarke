/**
 * @spaarke/ai-widgets — AnalysisHubWidget
 *
 * The Analysis platform's home/launcher surface (ai-advanced-capabilities-analysis-hub-r1
 * task 030 / spec FR-10). Renders:
 *   1. A top row of THREE "Create new" work-type cards — Agreement Review is LIVE
 *      (actionable); Legal Research + Patent Application render "coming soon" with a
 *      visible, disabled affordance (spec Open-Question resolution, §345).
 *   2. Below the cards, the existing `sprk_analysis` records via the shared DataGrid
 *      framework (`<DataGrid configId=… />`, consumed through the canonical
 *      `DataverseEntityViewWidget` wrapper — NOT re-implemented here).
 *
 * Reuse (CLAUDE.md §11 — default to reuse, no forking):
 *   - Cards: the shared `ActionCard` primitive (`@spaarke/ui-components`, the same
 *     component `GetStartedCardsWidget` uses for its 7-card grid). This widget adds a
 *     "Coming soon" `Badge` overlay for the two disabled cards — `ActionCard` has no
 *     built-in badge slot, so the overlay composes around it rather than forking it.
 *   - Grid: `DataverseEntityViewWidget` (this same package) — the canonical "Dataverse
 *     list" wrapper that owns the Xrm frame-walk, `XrmDataverseClient` construction, and
 *     `<DataGrid configId=… />` render. The view-by-type dropdown is NOT a bespoke
 *     control — it is the DataGrid framework's OWN `ViewSelector`, driven by sibling
 *     saved queries on the `sprk_gridconfiguration` row (see
 *     `ANALYSIS_HUB_GRID_CONFIG_ID` below for the required view set).
 *   - Registration: `registerWorkspaceWidget` (`WorkspaceWidgetRegistry.ts`) — this file
 *     owns `register-workspace-widgets.ts` per the task-040/030 parallel-execution
 *     handoff (see that file's `create-analysis-wizard` registration + this task's
 *     `analysis-hub` registration).
 *
 * Card → launch wiring (scoping note, mirrors task 040's own deviation record):
 *   The Agreement Review card dispatches a `widget_load` PaneEventBus event
 *   (`workspace` channel) with `widgetType: 'create-analysis-wizard'`, opening
 *   `CreateAnalysisWizardWidget` (task 040) as a new workspace tab — the literal
 *   "launches THIS" wiring named in this task's brief. The raw `sprk_worktype` Choice
 *   value (100000000 = Agreement Review) is restated locally rather than imported from
 *   `src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts` because that file is
 *   SOLUTION-owned and depends on this shared-lib package, not the reverse (ADR-012;
 *   see `CreateAnalysisWizardData`'s own doc comment for the identical precedent).
 *   Deep service wiring (`dataService`/`authenticatedFetch`/`navigationService`/
 *   `searchUsers` — the live function objects `CreateAnalysisWizardWidget` needs to
 *   actually create a record) is explicitly OUT of this task's scope — task 050
 *   (entry routing) owns resolving those from the host and threading them through.
 *   Until then, the opened wizard tab shows its own "Connecting to workspace
 *   services…" placeholder state (an existing, graceful `CreateAnalysisWizardWidget`
 *   render branch — not a crash). See
 *   `projects/ai-advanced-capabilities-analysis-hub-r1/notes/task-030-deviations.md`.
 *
 * The two "coming soon" cards (Legal Research, Patent Application) are DISABLED,
 * CLIENT-ONLY affordances this project — no server gating (ADR-032 SCOPE note in the
 * task POML). A later project that server-gates one of these cards applies the
 * ADR-032 Null-Object kill-switch pattern then, not here.
 *
 * Row reopen (task 031, spec FR-11):
 *   Selecting a grid row (via `DataverseEntityViewWidget`'s `onRecordOpen` escape hatch —
 *   an ALREADY-EXISTING DataGrid override point, not a new one) reopens that Analysis's
 *   session IN PLACE inside this already-open three-pane surface: resolves the bound
 *   session (task 020 FK, new `GET /ai/chat/sessions/by-analysis/{id}` lookup), dispatches
 *   `conversation.session_switch` (additive ADR-030 discriminant consumed by
 *   ConversationPane's existing History-select handler) to restore the transcript, and — for
 *   a linked file — dispatches a `document-viewer` `widget_load` via the `sprk_documentid` →
 *   `sprk_document` hop (ADR-007). A 404 (no session ever bound) degrades to an inline
 *   notice, never a freshly-minted empty session. See `handleRowOpen` for the full sequence
 *   and `resolveRowDocumentInfo` for the ADR-012 restatement rationale.
 *
 * Standards:
 *   - ADR-012: shared-lib composition (ActionCard + DataverseEntityViewWidget +
 *     WorkspaceWidgetRegistry) — no fork of any of the three.
 *   - ADR-021: Fluent v9 semantic tokens only; light/dark both verified (ui-tests).
 *   - ADR-022: React 19 functional component.
 *   - ADR-032: coming-soon cards are a client-only affordance this project.
 *
 * @see ActionCard                — @spaarke/ui-components (card primitive, reused as-is)
 * @see DataverseEntityViewWidget — this package (DataGrid framework composition)
 * @see CreateAnalysisWizardWidget — this package (task 040 — the Agreement launch target)
 * @see register-workspace-widgets.ts — registration entries for both widgets
 * @see docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md
 * @see docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md
 */

import * as React from 'react';
import { useCallback, useMemo, useState } from 'react';
import { Badge, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { DocumentAddRegular, DocumentMultipleRegular, DocumentSearchRegular } from '@fluentui/react-icons';
import type { FluentIcon } from '@fluentui/react-icons';

import { ActionCard } from '@spaarke/ui-components';
import type { ActionCardProps, AssociationResult } from '@spaarke/ui-components';
import { buildBffApiUrl } from '@spaarke/auth';

import type { WorkspaceWidgetProps } from '../../types/widget-types';
import { useOptionalDispatchPaneEvent } from '../../events/useDispatchPaneEvent';
import { AiSessionContext } from '../../providers/AiSessionProvider';
import { DataverseEntityViewWidget } from './DataverseEntityViewWidget';
import type { CreateAnalysisWizardData } from './CreateAnalysisWizardWidget';
import type { DocumentViewerWidgetData } from './DocumentViewerWidget';

// ---------------------------------------------------------------------------
// sprk_worktype — raw Choice value, restated locally (ADR-012; see file header)
// ---------------------------------------------------------------------------

/**
 * `sprk_worktype` — SprkAnalysisWorkType.AgreementAnalysis (task 011,
 * `src/solutions/SpaarkeAi/src/types/sprkAnalysis.ts`). Restated as a local raw
 * constant — this shared-lib widget cannot import the SpaarkeAi-solution-owned enum
 * (ADR-012: dependency direction is solution → shared lib, never the reverse). Mirrors
 * `CreateAnalysisWizardWidget`'s identical `DEFAULT_WORK_TYPE_VALUE` restatement.
 */
const WORK_TYPE_AGREEMENT_REVIEW = 100000000;

/**
 * Regarding entity types the Analysis wizard can pre-set (mirrors the wizard's
 * own `ANALYSIS_REGARDING_ENTITY_TYPES` — Matter / Project / Document; ADR-024).
 * The hub only pre-sets regarding=parent (entry case 2b) when the launch's record
 * context is one of these; any other host entity opens the hub unforced.
 */
const SUPPORTED_REGARDING_ENTITY_TYPES: ReadonlySet<string> = new Set([
  'sprk_matter',
  'sprk_project',
  'sprk_document',
]);

// ---------------------------------------------------------------------------
// Grid configuration — sprk_gridconfiguration for the hub's Analysis grid
// ---------------------------------------------------------------------------

/**
 * GUID of the `sprk_gridconfiguration` row driving the hub's Analysis grid.
 *
 * **DEPLOYMENT REQUIREMENT** (mirrors the `ENTITY_VIEW_CONFIG_IDS` pattern in
 * `register-workspace-widgets.ts`): before this widget lists real data, an operator
 * MUST create ONE `sprk_gridconfiguration` row over `sprk_analysis` with sibling saved
 * queries — the DataGrid framework's OWN `ViewSelector` renders the "view by type"
 * dropdown automatically once multiple saved queries exist for the entity:
 *   - "All Analyses" (default — no filter; the config's own `source.savedQueryId`)
 *   - "Agreement Review Analyses" (`sprk_worktype eq 100000000`)
 *   - "Legal Research Analyses" (`sprk_worktype eq 100000001`)
 *   - "Patent Application Analyses" (`sprk_worktype eq 100000002`)
 *
 * Replace this placeholder with the real GUID once seeded. Handed to task 071
 * (deploy) — see
 * `projects/ai-advanced-capabilities-analysis-hub-r1/notes/hub-grid-config-deployment.md`
 * for the full operator recipe. Until seeded, `DataGrid`'s own invalid-config guard
 * (via `DataverseEntityViewWidget` → `fetchConfigRecord`) renders a clear empty state —
 * no production crash.
 */
const ANALYSIS_HUB_GRID_CONFIG_ID = '00000000-0000-0000-0000-000000000000';

/** Widget-type string passed to the nested `DataverseEntityViewWidget` for debug/sub-routing only. */
const ANALYSIS_HUB_GRID_WIDGET_TYPE = 'analysis-hub-grid';

/**
 * Max characters for a reopened-analysis workspace-tab title (owner UX, 2026-07-29:
 * "the tab does not have much real estate; we can try a concat of the name"). The
 * tab strip is narrow, so the Analysis `sprk_name` is truncated with an ellipsis for
 * the visible tab label; the full name is preserved as the tab's hover title/tooltip
 * by the WorkspaceTabManager (it uses the same `displayName` for both, and the tab
 * chrome truncates visually — this cap keeps the tab from over-claiming strip width).
 */
const TAB_TITLE_MAX_CHARS = 24;

/**
 * Truncate an Analysis name to {@link TAB_TITLE_MAX_CHARS} for a workspace-tab label,
 * appending a single-character ellipsis when clipped. Falls back to 'Analysis' for an
 * empty/missing name so the tab is never blank.
 */
function toTabTitle(name: string | undefined): string {
  const trimmed = (name ?? '').trim();
  if (trimmed.length === 0) return 'Analysis';
  if (trimmed.length <= TAB_TITLE_MAX_CHARS) return trimmed;
  return `${trimmed.slice(0, TAB_TITLE_MAX_CHARS - 1)}…`;
}

// ---------------------------------------------------------------------------
// Row reopen — session lookup + file resolution (task 031, spec FR-11)
// ---------------------------------------------------------------------------

/**
 * Mirrors the BFF's `AnalysisSessionSummary` record returned by
 * `GET /api/ai/chat/sessions/by-analysis/{analysisId}` (task 031). camelCase per the
 * endpoint's `JsonNamingPolicy.CamelCase` serializer options.
 */
interface AnalysisSessionLookupResponse {
  sessionId: string;
  messageCount: number;
  isArchived: boolean;
  createdOn: string | null;
}

/**
 * Reads the `sprk_documentid` → `sprk_document` file-hop fields directly off the grid row
 * (ADR-007) — one field access, no SPE pointer is ever read off `sprk_analysis`.
 *
 * Restated locally rather than imported from
 * `src/solutions/SpaarkeAi/src/services/analysisFileResolution.ts` — that module is
 * SpaarkeAi-solution-owned and this shared-lib widget cannot import it (ADR-012:
 * dependency direction is solution → shared lib, never the reverse — see the identical
 * `WORK_TYPE_AGREEMENT_REVIEW` restatement above for the established precedent). Returns
 * `null` when the Analysis has no linked document — callers MUST treat that as "no file",
 * never fabricate a pointer.
 */
function resolveRowDocumentInfo(
  record: Record<string, unknown>
): { documentId: string; documentName: string } | null {
  const documentId = record['_sprk_documentid_value'];
  if (typeof documentId !== 'string' || documentId.length === 0) {
    return null;
  }

  const formattedName = record['_sprk_documentid_value@OData.Community.Display.V1.FormattedValue'];
  const rowName = record['sprk_name'];
  const documentName =
    typeof formattedName === 'string' && formattedName.length > 0
      ? formattedName
      : typeof rowName === 'string' && rowName.length > 0
        ? rowName
        : 'Document';

  return { documentId, documentName };
}

// ---------------------------------------------------------------------------
// Data contract
// ---------------------------------------------------------------------------

export interface AnalysisHubWidgetData {
  /**
   * Override for the `sprk_gridconfiguration` GUID driving the Analysis grid. Optional —
   * defaults to {@link ANALYSIS_HUB_GRID_CONFIG_ID}. Present so a future dispatcher (or
   * test) can point the hub at an alternate config record without a code change.
   */
  configId?: string;
}

// ---------------------------------------------------------------------------
// Card definitions
// ---------------------------------------------------------------------------

type HubCardId = 'agreement-review' | 'legal-research' | 'patent-application';

interface HubCardDefinition {
  id: HubCardId;
  label: string;
  description: string;
  icon: FluentIcon;
  /** When true the card renders disabled with a "Coming soon" badge affordance. */
  comingSoon: boolean;
}

/**
 * Exactly three cards ship this project (spec Open-Question resolution, §345). Do NOT
 * add or remove cards here — see the task POML's "card set" constraint.
 */
const HUB_CARDS: readonly HubCardDefinition[] = Object.freeze([
  {
    id: 'agreement-review',
    label: 'Agreement Review',
    description: 'Analyze a contract or agreement document.',
    icon: DocumentAddRegular,
    comingSoon: false,
  },
  {
    id: 'legal-research',
    label: 'Legal Research',
    description: 'Legal research analysis — coming soon.',
    icon: DocumentSearchRegular,
    comingSoon: true,
  },
  {
    id: 'patent-application',
    label: 'Patent Application',
    description: 'Patent application analysis — coming soon.',
    icon: DocumentMultipleRegular,
    comingSoon: true,
  },
]);

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    minHeight: 0,
    width: '100%',
    minWidth: 0,
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  cardsRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingHorizontalM,
    flexShrink: 0,
  },
  cardWrapper: {
    position: 'relative',
    display: 'flex',
    flex: '1 1 0',
    minWidth: '120px',
  },
  comingSoonBadge: {
    position: 'absolute',
    top: tokens.spacingVerticalXS,
    right: tokens.spacingHorizontalXS,
    pointerEvents: 'none',
  },
  gridSection: {
    flex: 1,
    minHeight: 0,
    minWidth: 0,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  sectionLabel: {
    padding: tokens.spacingHorizontalM,
    paddingBottom: 0,
    color: tokens.colorNeutralForeground3,
  },
  reopenNotice: {
    padding: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: 0,
    color: tokens.colorPaletteRedForeground1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * AnalysisHubWidget — the Analysis platform's home/launcher workspace tab.
 *
 * @example
 * ```tsx
 * // Resolved via WorkspaceWidgetRegistry under 'analysis-hub':
 * <AnalysisHubWidget data={{}} widgetType="analysis-hub" />
 * ```
 */
export const AnalysisHubWidget: React.FC<WorkspaceWidgetProps<AnalysisHubWidgetData>> = ({ data, className }) => {
  const styles = useStyles();
  // Pattern D dual-use (docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md §4):
  // this widget renders as BOTH a Direct workspace widget (SpaarkeAi shell — providers
  // present) AND a LegalWorkspace `analysis` section (bus/session may be absent in a
  // non-SpaarkeAi host / MDA / dev). Read BOTH the PaneEventBus and the AI session
  // OPTIONALLY so the widget degrades gracefully instead of throwing at render —
  // mirroring DataverseEntityViewWidget's optional `useContext(AiSessionContext)` read.
  const dispatch = useOptionalDispatchPaneEvent();
  const session = React.useContext(AiSessionContext);
  const bffBaseUrl = session?.bffBaseUrl;
  const authenticatedFetch = session?.authenticatedFetch;
  const entityContext = session?.entityContext;

  const gridConfigId = data?.configId ?? ANALYSIS_HUB_GRID_CONFIG_ID;

  // Entry case 2b (new-in-record, spec §12 / FR-14): when the hub is opened in a
  // record context (a Matter/Project modal launched via openSpaarkeAi — task 050
  // host routing + task 052 ribbon), pre-set the wizard's regarding lookup to that
  // parent record. In entry case 2a (new-in-workspace) there is no record context,
  // so `initialAssociation` is undefined and the wizard opens with an empty regarding
  // — only 2b forces the parent (project constraint: do not cross-wire the cases).
  // entityContext carries no display name, so the record id is used as the label
  // fallback (the wizard shows it pre-selected; the user may change or clear it).
  const initialAssociation = useMemo<AssociationResult | undefined>(() => {
    const entityType = entityContext?.entityType;
    const recordId = entityContext?.entityId;
    if (!entityType || !recordId) return undefined;
    if (!SUPPORTED_REGARDING_ENTITY_TYPES.has(entityType)) return undefined;
    return { entityType, recordId, recordName: recordId };
  }, [entityContext?.entityType, entityContext?.entityId]);

  // Transient status line for the row-reopen flow (task 031) — cleared on every new attempt,
  // only ever set on a graceful degrade (no session bound / lookup failure). Never blocks the
  // grid; purely informational.
  const [reopenNotice, setReopenNotice] = useState<string | null>(null);

  const handleAgreementReviewClick = useCallback(() => {
    dispatch('workspace', {
      type: 'widget_load',
      widgetType: 'create-analysis-wizard',
      widgetData: {
        workTypeValue: WORK_TYPE_AGREEMENT_REVIEW,
        workTypeLabel: 'Agreement Review',
        // 2b regarding pre-set (undefined in 2a → no forced regarding). The deep
        // Dataverse services (dataService/navigationService/searchUsers/
        // authenticatedFetch/bffBaseUrl) are injected by the SpaarkeAi shell
        // (WorkspacePane) at widget_load time — see task 050 thread 1.
        ...(initialAssociation ? { initialAssociation } : {}),
      } as CreateAnalysisWizardData,
      displayName: 'Create Agreement Review Analysis',
    });
  }, [dispatch, initialAssociation]);

  // Stable per-card click handler map. Only the actionable (non-comingSoon) card gets a
  // real handler — disabled ActionCard instances already ignore onClick internally, but
  // omitting it here keeps the intent explicit (no dead click handler on a disabled card).
  const cardHandlers = useMemo<Partial<Record<HubCardId, () => void>>>(
    () => ({
      'agreement-review': handleAgreementReviewClick,
    }),
    [handleAgreementReviewClick]
  );

  /**
   * Row-open handler — task 031 (FR-11): reopens an existing Analysis's session in the
   * ALREADY-OPEN three-pane surface, IN PLACE (this widget is itself a workspace tab inside
   * the three-pane it is rehydrating — there is no separate surface to navigate to).
   *
   * Sequence:
   *   1. Resolve the Analysis's bound session via the task-020 FK
   *      (`GET /ai/chat/sessions/by-analysis/{analysisId}`, task 031). A 404 (no session was
   *      EVER bound to this Analysis) is a graceful no-op — the escalation trigger for this
   *      task is explicit that reopening MUST NOT mint an empty session to paper over a gap.
   *   2. Dispatch `conversation.session_switch` (additive ADR-030 discriminant, reuses the
   *      channel's existing `sessionId` field — no new field needed). ConversationPane's
   *      EXISTING History-select handler (`handleSelectHistorySession`) adopts the id and
   *      remounts SprkChat; SprkChat's own `resumeSession()` + `loadHistory()` mount-effect
   *      restores the transcript. This is TTL-safe: `ChatSessionManager.GetSessionAsync`
   *      (task 025 hardening) already falls back Redis → Cosmos → Dataverse on a stale/expired
   *      hot-cache entry — no second restore mechanism is built here (ADR-040: consume the
   *      seam, don't rebuild it).
   *   3. The SAME session-id change independently drives WorkspacePane's existing
   *      chatSessionId-keyed `/tabs` restore effect — the task-025 seam that restores
   *      review/findings widget state (e.g. AnalysisEditorWidget edit-state, FindingsWidget).
   *   4. If the Analysis has a linked file (`sprk_documentid` → `sprk_document`, ADR-007),
   *      dispatch a `document-viewer` `widget_load` with a live `fetchPreviewUrl` closure —
   *      the SAME BFF call shape `analysisFileResolution.ts` uses, restated locally (see that
   *      function's doc comment for the ADR-012 boundary rationale). No SPE pointer is ever
   *      read off `sprk_analysis`.
   *
   * Both PaneEventBus dispatches (workspace + conversation channels) are the canonical
   * mount-signalling mechanism (ADR-030) — no bespoke open path is invented.
   */
  const handleRowOpen = useCallback(
    (recordId: string, record: Record<string, unknown>): void => {
      setReopenNotice(null);

      // Dual-use guard: reopening resolves the bound session over the BFF, which
      // requires the AI session context (authenticatedFetch + bffBaseUrl). In a
      // bus-less / session-less host (standalone LegalWorkspace section / MDA / dev)
      // that context is absent — degrade with a notice rather than crash.
      if (!bffBaseUrl || !authenticatedFetch) {
        setReopenNotice('Reopening an analysis is available in the Assistant workspace.');
        return;
      }

      void (async (): Promise<void> => {
        let sessionLookup: AnalysisSessionLookupResponse;
        try {
          const lookupUrl = buildBffApiUrl(
            bffBaseUrl,
            `/ai/chat/sessions/by-analysis/${encodeURIComponent(recordId)}`
          );
          const response = await authenticatedFetch(lookupUrl, { headers: { Accept: 'application/json' } });

          if (response.status === 404) {
            setReopenNotice('This analysis has no chat session yet.');
            return;
          }
          if (!response.ok) {
            setReopenNotice('Could not reopen this analysis — please try again.');
            return;
          }
          sessionLookup = (await response.json()) as AnalysisSessionLookupResponse;
        } catch {
          setReopenNotice('Could not reopen this analysis — please try again.');
          return;
        }

        // Steps 2 + 3 — switch the already-open three-pane to this session, in place.
        dispatch('conversation', {
          type: 'session_switch',
          sessionId: sessionLookup.sessionId,
        });

        // Step 4 — restore the linked file, if any (ADR-007 hop; graceful no-file no-op).
        const documentInfo = resolveRowDocumentInfo(record);
        if (documentInfo) {
          const widgetData: DocumentViewerWidgetData = {
            filename: documentInfo.documentName,
            contentType: 'application/octet-stream',
            textContent: '',
            documentId: documentInfo.documentId,
            fetchPreviewUrl: async (): Promise<string | null> => {
              try {
                const previewUrlEndpoint = buildBffApiUrl(
                  bffBaseUrl,
                  `/documents/${encodeURIComponent(documentInfo.documentId)}/preview-url`
                );
                const previewResponse = await authenticatedFetch(previewUrlEndpoint);
                if (!previewResponse.ok) return null;
                const previewData = (await previewResponse.json()) as { previewUrl?: string | null };
                return previewData.previewUrl ?? null;
              } catch {
                return null;
              }
            },
          };

          // Owner UX (2026-07-29): the reopened analysis surfaces as a workspace tab
          // titled with the analysis's own name (truncated for the narrow tab strip),
          // NOT the raw filename. The viewer still shows the linked document; only the
          // tab label reflects the analysis identity.
          dispatch('workspace', {
            type: 'widget_load',
            widgetType: 'document-viewer',
            widgetData,
            displayName: toTabTitle(record['sprk_name'] as string | undefined),
          });
        }
      })();
    },
    [authenticatedFetch, bffBaseUrl, dispatch]
  );

  return (
    <div className={mergeClasses(styles.root, className)} data-testid="analysis-hub-widget">
      <div className={styles.cardsRow} role="group" aria-label="Create new analysis">
        {HUB_CARDS.map(card => (
          <div key={card.id} className={styles.cardWrapper} data-testid={`analysis-hub-card-${card.id}`}>
            <ActionCard
              // Type assertion is intentional and load-bearing — see the identical
              // precedent + rationale in GetStartedCardsWidget.tsx: this package pins
              // its own copy of `@fluentui/react-icons`, structurally identical to but
              // nominally distinct from `@spaarke/ui-components`'s copy.
              icon={card.icon as ActionCardProps['icon']}
              label={card.label}
              ariaLabel={
                card.comingSoon
                  ? `${card.label} — coming soon, not yet available`
                  : `${card.label} — ${card.description}`
              }
              onClick={cardHandlers[card.id]}
              disabled={card.comingSoon}
            />
            {card.comingSoon && (
              <Badge
                className={styles.comingSoonBadge}
                appearance="tint"
                color="informative"
                size="small"
                data-testid={`analysis-hub-coming-soon-badge-${card.id}`}
              >
                Coming soon
              </Badge>
            )}
          </div>
        ))}
      </div>

      <div className={styles.gridSection}>
        <Text size={200} weight="semibold" className={styles.sectionLabel}>
          Analyses
        </Text>
        {reopenNotice && (
          <Text
            size={200}
            className={styles.reopenNotice}
            role="status"
            data-testid="analysis-hub-reopen-notice"
          >
            {reopenNotice}
          </Text>
        )}
        <DataverseEntityViewWidget
          data={{ configId: gridConfigId, onRecordOpen: handleRowOpen }}
          widgetType={ANALYSIS_HUB_GRID_WIDGET_TYPE}
        />
      </div>
    </div>
  );
};

AnalysisHubWidget.displayName = 'AnalysisHubWidget';

export default AnalysisHubWidget;
