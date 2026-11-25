/**
 * SemanticSearchCriteriaTool.tsx — In-pane search criteria editor for the
 * SpaarkeAi Context pane (Task 095; expanded Task 099).
 *
 * Surfaced when the user selects "Semantic Search" from the Context pane's
 * Tools dropdown (ContextPaneMenu). Task 099 expands the criteria surface to
 * match the operator's screenshot from Round 6 (2026-05-22):
 *
 *   1. "Search Criteria" header.
 *   2. Four type-toggle buttons in a 2x2 grid (Documents / Matters /
 *      Projects / Invoices) — replaces the previous single-select Dropdown.
 *      The active type is the primary appearance; the rest are subtle.
 *   3. AI Search query textarea.
 *   4. Per-domain filters — the shared `FilterDropdown` (multi-select) with
 *      the same Dataverse option lists the search page uses
 *      (`useSearchFilterOptions`, #3131):
 *        - Documents domain → Document Type + File Type + Matter Type
 *        - Matters domain   → Matter Type
 *        - Projects / Invoices → none
 *   5. The shared `DateRangeFilter` (presets + From / To local dates, #3131).
 *   6. Full-width primary Search button. Launches sprk_semanticsearch via
 *      Xrm.Navigation.navigateTo with the criteria as the URL params the page
 *      reads (`parseUrlParams`).
 *
 * Persistence:
 *   Transient criteria state persists in localStorage under
 *   `spaarke:context:semantic-search-criteria` (missing or older fields fall
 *   back to "All" / "Off"; see `readPersistedCriteria`). A date preset is
 *   stored as its id and re-resolved against today on load, so "Last 30
 *   days" stays relative instead of freezing the day it was picked.
 *   This means:
 *     - The criteria survives modal open/close (operator flow: type a query,
 *       click Search, results modal opens, user closes modal — criteria are
 *       still in the pane).
 *     - The criteria survives tool switches (Quick Start → Semantic Search →
 *       Quick Start → Semantic Search shows what was last typed).
 *     - The criteria survives browser restarts (localStorage, not sessionStorage).
 *
 * Modal launch shape:
 *   Mirrors the wizard launchers in @spaarke/ui-components/WorkspaceShell/
 *   wizardLaunchers.ts — same Xrm frame-walk, same pageType:"webresource",
 *   same target:2, same 80% × 80% modal (wider than wizards because the
 *   full SemanticSearch page has its own SearchFilterPane + map/grid views).
 *
 *   data string carries the keys the SemanticSearch code page parses
 *   (src/client/code-pages/SemanticSearch/src/utils/parseUrlParams.ts):
 *     query=<encoded>&domain=<documents|matters|projects|invoices>
 *     &documentTypes=<csv>   (Documents domain; sprk_documenttype option numbers)
 *     &fileTypes=<csv>       (Documents domain; extensions)
 *     &matterTypes=<csv>     (Documents or Matters domain; sprk_mattertype_ref ids)
 *     &dateFrom=<YYYY-MM-DD>&dateTo=<YYYY-MM-DD>   (local calendar days)
 *
 *   Before #3131 this sent `documentType` / `fileType` / `matterType` /
 *   `dateRange=<preset>`, which the page never read: every filter chosen
 *   here was dropped.
 *
 * Bug-fix invariant (matches the playbook-modal pane-blank fix in Task 095):
 *   When the user clicks Search, this component does NOT change the
 *   ContextPaneController's selectedTool. The localStorage-backed selectedTool
 *   means the Context pane re-renders with this tool still selected after the
 *   modal closes — that is the uniform fix for the "pane goes blank after a
 *   modal closes" bug.
 *
 * Shared with the search page (#3131):
 *   `FilterDropdown`, `DateRangeFilter` and `useSearchFilterOptions` come from
 *   @spaarke/ui-components (`SearchFilters/`), the same controls and option
 *   lists as src/client/code-pages/SemanticSearch/src/components/SearchFilterPane.tsx.
 *   The `SearchDomain` union is replicated here: the code page's types are
 *   not a package SpaarkeAi consumes.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local — the criteria are an in-pane subset of the
 *     full SemanticSearch experience (which lives in src/client/code-pages/).
 *   - ADR-021: Fluent v9 tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *   - ADR-028: No auth surface change — criteria stay client-side until the
 *     modal launch hands off to the SemanticSearch Code Page, which has its
 *     own MSAL bootstrap.
 *
 * @see ContextPaneController — parent
 * @see ContextPaneMenu — surface that toggles this tool's visibility
 * @see wizardLaunchers — proven Xrm frame-walk + navigateTo pattern
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  shorthands,
  tokens,
  Button,
  Checkbox,
  Label,
  Textarea,
  Text,
} from '@fluentui/react-components';
import {
  Search20Regular,
  DocumentRegular,
  BriefcaseRegular,
  FolderRegular,
  ReceiptRegular,
} from '@fluentui/react-icons';
import {
  useDispatchPaneEvent,
  SEARCH_CRITERIA_RESULT_WIDGET_TYPE,
} from '@spaarke/ai-widgets';
import type { SearchCriteriaResultWidgetData } from '@spaarke/ai-widgets';
import {
  getXrm,
  FilterDropdown,
  DateRangeFilter,
  useSearchFilterOptions,
  calculatePresetRange,
  detectActivePreset,
  DATE_RANGE_PRESET_IDS,
} from '@spaarke/ui-components';
import type { DateRangeFilterValue, DateRangePresetId, FilterDropdownOption } from '@spaarke/ui-components';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Mirrors `SearchDomain` from the full SemanticSearch Code Page
 * (src/client/code-pages/SemanticSearch/src/types/index.ts). Replicated as a
 * literal union here so this file does NOT cross the package boundary (the
 * SemanticSearch types live in a separate code-page that isn't a published
 * npm package consumed by SpaarkeAi).
 */
export type SearchDomain = 'documents' | 'matters' | 'projects' | 'invoices';

interface DomainDescriptor {
  id: SearchDomain;
  label: string;
  icon: React.ReactElement;
}

const DOMAINS: readonly DomainDescriptor[] = [
  { id: 'documents', label: 'Documents', icon: <DocumentRegular /> },
  { id: 'matters', label: 'Matters', icon: <BriefcaseRegular /> },
  { id: 'projects', label: 'Projects', icon: <FolderRegular /> },
  { id: 'invoices', label: 'Invoices', icon: <ReceiptRegular /> },
];

const VALID_DOMAINS: ReadonlySet<string> = new Set<SearchDomain>([
  'documents',
  'matters',
  'projects',
  'invoices',
]);

const VALID_DATE_PRESETS: ReadonlySet<string> = new Set<string>([...DATE_RANGE_PRESET_IDS, 'custom']);

/** Which filters apply to a domain — the same rule as the page's SearchFilterPane. */
export function domainFilters(domain: SearchDomain): {
  documentTypes: boolean;
  fileTypes: boolean;
  matterTypes: boolean;
} {
  return {
    documentTypes: domain === 'documents',
    fileTypes: domain === 'documents',
    matterTypes: domain === 'documents' || domain === 'matters',
  };
}

/**
 * Shape persisted in localStorage (#3131).
 *
 * `datePreset` is the source of truth for a preset range: `dateRange` is re-derived from it on
 * load, so a relative preset keeps meaning "relative to today". Only `custom` uses the stored
 * `dateRange` as typed. Filter values are Dataverse values (option numbers, record ids,
 * extensions), the ones the search page filters on.
 */
export interface PersistedCriteria {
  query: string;
  domain: SearchDomain;
  documentTypes: string[];
  fileTypes: string[];
  matterTypes: string[];
  datePreset: DateRangePresetId | 'custom';
  dateRange: DateRangeFilterValue | null;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const STORAGE_KEY = 'spaarke:context:semantic-search-criteria';

const DEFAULT_CRITERIA: PersistedCriteria = {
  query: '',
  domain: 'documents',
  documentTypes: [],
  fileTypes: [],
  matterTypes: [],
  datePreset: 'off',
  dateRange: null,
};

const SEMANTIC_SEARCH_WEBRESOURCE = 'sprk_semanticsearch';
const SEMANTIC_SEARCH_TITLE = 'Semantic Search Results';

// ---------------------------------------------------------------------------
// localStorage helpers — try/catch-wrapped for private browsing / quota
// ---------------------------------------------------------------------------

function stringArray(value: unknown): string[] | undefined {
  return Array.isArray(value) && value.every((v) => typeof v === 'string') ? (value as string[]) : undefined;
}

function dateOrNull(value: unknown): string | null {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) ? value : null;
}

/**
 * The criteria from a parsed localStorage value, with the date range resolved against `now`.
 *
 * Older shapes (before #3131) stored single values: `documentType` / `matterType` held invented
 * placeholder ids that match no Dataverse value, so they are dropped; `fileType` held a real
 * extension and becomes a one-item list; `dateRange` held a preset id (`custom` had no dates, so
 * it reads as Off).
 */
export function normalizeCriteria(parsed: unknown, now: Date = new Date()): PersistedCriteria {
  if (typeof parsed !== 'object' || parsed === null) return { ...DEFAULT_CRITERIA };
  const obj = parsed as Record<string, unknown>;

  const legacyFileType =
    typeof obj.fileType === 'string' && obj.fileType !== 'all' ? [obj.fileType] : undefined;

  let datePreset: DateRangePresetId | 'custom' = 'off';
  let dateRange: DateRangeFilterValue | null = null;
  const presetSource = typeof obj.datePreset === 'string' ? obj.datePreset : obj.dateRange;
  if (typeof presetSource === 'string' && VALID_DATE_PRESETS.has(presetSource)) {
    datePreset = presetSource as DateRangePresetId | 'custom';
  }
  if (datePreset === 'custom') {
    const stored = obj.dateRange as Record<string, unknown> | null | undefined;
    const from = dateOrNull(stored?.from);
    const to = dateOrNull(stored?.to);
    if (from || to) {
      dateRange = { from, to };
    } else {
      datePreset = 'off';
    }
  } else if (datePreset !== 'off') {
    dateRange = calculatePresetRange(datePreset, now);
  }

  return {
    query: typeof obj.query === 'string' ? obj.query : DEFAULT_CRITERIA.query,
    domain:
      typeof obj.domain === 'string' && VALID_DOMAINS.has(obj.domain)
        ? (obj.domain as SearchDomain)
        : DEFAULT_CRITERIA.domain,
    documentTypes: stringArray(obj.documentTypes) ?? [],
    fileTypes: stringArray(obj.fileTypes) ?? legacyFileType ?? [],
    matterTypes: stringArray(obj.matterTypes) ?? [],
    datePreset,
    dateRange,
  };
}

function readPersistedCriteria(): PersistedCriteria {
  if (typeof window === 'undefined') return { ...DEFAULT_CRITERIA };
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) return { ...DEFAULT_CRITERIA };
    return normalizeCriteria(JSON.parse(raw) as unknown);
  } catch {
    return { ...DEFAULT_CRITERIA };
  }
}

function writePersistedCriteria(criteria: PersistedCriteria): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(criteria));
  } catch {
    // Best-effort persistence; silent on quota / private-mode failures.
  }
}

// ---------------------------------------------------------------------------
// Modal launch — Xrm.Navigation.navigateTo to sprk_semanticsearch
// ---------------------------------------------------------------------------

/**
 * The `data` string for the search page, in the keys its `parseUrlParams` reads. Filters are
 * sent only for the domains they apply to (the page's own rule) and only when something is
 * selected; the dates are the filter's local calendar days, which the page turns into UTC bounds
 * with `toUtcDateRangeBounds`.
 */
export function buildSearchDataParams(criteria: PersistedCriteria): string {
  const parts: string[] = [];
  const csv = (values: string[]): string => encodeURIComponent(values.join(','));
  if (criteria.query) parts.push(`query=${encodeURIComponent(criteria.query)}`);
  if (criteria.domain) parts.push(`domain=${encodeURIComponent(criteria.domain)}`);

  const applies = domainFilters(criteria.domain);
  if (applies.documentTypes && criteria.documentTypes.length > 0) {
    parts.push(`documentTypes=${csv(criteria.documentTypes)}`);
  }
  if (applies.fileTypes && criteria.fileTypes.length > 0) {
    parts.push(`fileTypes=${csv(criteria.fileTypes)}`);
  }
  if (applies.matterTypes && criteria.matterTypes.length > 0) {
    parts.push(`matterTypes=${csv(criteria.matterTypes)}`);
  }

  if (criteria.dateRange?.from) parts.push(`dateFrom=${encodeURIComponent(criteria.dateRange.from)}`);
  if (criteria.dateRange?.to) parts.push(`dateTo=${encodeURIComponent(criteria.dateRange.to)}`);

  return parts.join('&');
}

/** The labels of the selected values (a value with no loaded option shows as itself). */
export function selectedLabels(values: string[], options: readonly FilterDropdownOption[]): string[] {
  return values.map((v) => options.find((o) => o.value === v)?.label ?? v);
}

/**
 * Fire-and-forget Semantic Search modal launch. Catches user-cancel + dialog
 * error per the wizardLaunchers.ts precedent. Console-warns when running
 * outside the Dataverse host (Vite dev, jsdom) — same posture as
 * WorkspacePaneMenu.launchWizard.
 *
 * NOTE: We deliberately do NOT await this. The Promise that
 * navigateTo returns resolves when the modal closes — and our pane behavior
 * is identical whether we await it or not (the Context pane already shows
 * the right criteria because selectedTool is persisted in localStorage at the
 * controller level).
 */
function launchSemanticSearch(criteria: PersistedCriteria): void {
  // Shared cross-frame walker (task 081 / C-8).
  const nav = getXrm('navigation')?.Navigation;
  if (!nav?.navigateTo) {
    console.warn(
      '[SemanticSearchCriteriaTool] Xrm.Navigation.navigateTo not available — running outside Dataverse host. Search launch is a no-op.',
    );
    return;
  }
  const data = buildSearchDataParams(criteria);
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (nav.navigateTo as any)(
      {
        pageType: 'webresource',
        webresourceName: SEMANTIC_SEARCH_WEBRESOURCE,
        data,
      },
      {
        target: 2,
        width: { value: 80, unit: '%' },
        height: { value: 80, unit: '%' },
        title: SEMANTIC_SEARCH_TITLE,
      },
    ).catch?.((err: unknown) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const code = (err as any)?.errorCode;
      if (code !== 2) {
        console.warn('[SemanticSearchCriteriaTool] navigateTo error:', err);
      }
    });
  } catch (err) {
    console.warn('[SemanticSearchCriteriaTool] navigateTo threw synchronously:', err);
  }
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

// Task 106 — width cap + LEFT-aligned anchoring (Round 8 operator feedback
// 2026-05-22, revising task 103's centering decision):
//
// When the Context pane is full-width (Assistant + Workspace both collapsed
// via task 100), the Semantic Search criteria stretches across the entire
// screen, which makes a form-style surface (single-column labels + dropdowns
// + textarea) look stretched and unbalanced. Task 103 capped the criteria at
// 480px AND centered it (`marginLeft: 'auto'` + `marginRight: 'auto'`).
// Round 8 operator feedback: "the semantic search component should be left
// aligned (see screenshot)." So we KEEP the 480px cap (still prevents the
// form from stretching across the full pane on wide layouts) but FLIP the
// horizontal anchoring from CENTERED to LEFT-ALIGNED.
//
// Cap: `CRITERIA_MAX_WIDTH_PX = 480`. Picked to mirror a typical Fluent v9
// form-panel width — wide enough for two filter dropdowns to feel uncramped,
// narrow enough that the criteria doesn't span more than ~30% of a 1600px
// monitor. Left-alignment is achieved via `marginLeft: 0` + `marginRight: 'auto'`
// on the criteria CONTAINER (not the `root` which fills the pane's full
// width for background coverage). The form anchors to the pane's left edge
// (against the outer wrapper's left padding) and any empty space sits on the
// right when the pane is wider than 480px.
//
// At narrow pane widths (~400-480px) the cap doesn't activate — the criteria
// fill the pane naturally because `max-width` only constrains; it doesn't
// shrink below natural width. The visible behavior change is only at wide
// pane widths.
//
// TODO (future tools): as other Context tools are added (Search Saved, Filter
// Library, etc.) they MUST each set their own width caps using the same
// pattern. Operator explicitly flagged this: "As other tools are added we'll
// need to address responsiveness." Treat this constant as a pattern, not a
// shared singleton — each tool decides its own visual cap based on its
// content (dense table tools may not need a cap; form tools should mirror
// this 480px).
const CRITERIA_MAX_WIDTH_PX = 480;

const useStyles = makeStyles({
  // Outer container fills the pane width so the background + spacing
  // continue to span edge-to-edge in the wide-pane case. The inner `criteria`
  // wrapper is the one that's capped + centered.
  root: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    margin: tokens.spacingHorizontalS,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  // Task 106 — width cap container, LEFT-ALIGNED (revises task 103's centered
  // layout). `max-width: 480px` only constrains; it doesn't shrink below
  // natural width, so narrow panes are unaffected. `marginLeft: 0` +
  // `marginRight: auto` anchors the criteria to the pane's left edge (against
  // the outer `root` wrapper's left padding) instead of centering — operator
  // (Round 8, 2026-05-22): "the semantic search component should be left
  // aligned." Empty space sits on the RIGHT when the pane is wider than 480px.
  // `width: 100%` is required for `max-width` to take effect when there's no
  // explicit width.
  criteria: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    maxWidth: `${CRITERIA_MAX_WIDTH_PX}px`,
    marginLeft: 0,
    marginRight: 'auto',
    gap: tokens.spacingVerticalM,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },

  // Task 099 — domain type buttons in a 2x2 grid. The active type uses
  // appearance="primary" (brand fill); the rest use appearance="subtle"
  // (transparent with brand-on-hover). Icons from `@fluentui/react-icons`
  // v9 — Documents/Briefcase/Folder/Receipt match the operator screenshot.
  typeGrid: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalS,
  },
  typeButton: {
    justifyContent: 'flex-start',
    width: '100%',
  },

  dropdown: {
    width: '100%',
    minWidth: 'auto',
  },
  textarea: {
    width: '100%',
    minHeight: '72px',
  },

  // Task 099 — full-width primary Search button at the bottom (matches
  // operator screenshot — no longer right-aligned).
  searchButtonRow: {
    marginTop: tokens.spacingVerticalS,
  },
  searchButton: {
    width: '100%',
  },

  // R4 task 043 (W-5) — "Also add to Workspace" opt-in row sits between the
  // Date Range dropdown and the Search button. Uses the same vertical
  // spacing as the criteria fields above so the opt-in doesn't visually
  // detach from the form. Fluent v9 Checkbox carries its own label styling;
  // we only need the row container.
  addToWorkspaceRow: {
    display: 'flex',
    flexDirection: 'column',
    marginTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SemanticSearchCriteriaTool — In-pane criteria editor + Search button.
 *
 * State is local React + localStorage persistence (no PaneEventBus side
 * effects — the criteria are purely a user-input surface; nothing else in
 * the SpaarkeAi shell needs to subscribe to changes here).
 */
export const SemanticSearchCriteriaTool: React.FC = () => {
  const styles = useStyles();

  // Hydrate from localStorage on mount (read once via lazy initializer).
  const [criteria, setCriteria] = React.useState<PersistedCriteria>(() =>
    readPersistedCriteria(),
  );

  // ── R4 task 043 (W-5) — "Also add to Workspace" opt-in for the Context →
  // Workspace mount-source demo (FR-03). Defaults to UNCHECKED — the modal
  // search launch is the production path; mounting a workspace tab is opt-in
  // per the task spec's "MUST be optional" constraint.
  //
  // State is intentionally NOT persisted to localStorage — each search-click
  // re-affirms whether the user wants a tab this time. Persisting would be
  // a surprise-affecting-default after a single click.
  const [addToWorkspace, setAddToWorkspace] = React.useState<boolean>(false);

  // Dispatcher for PaneEventBus workspace channel (ADR-030).
  // Mirrors the R4 task 042 / W-4 pattern in
  // src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx
  // (`handleAttachmentReady`). Both task 042 and task 043 use the same
  // dispatch hook and the same `widget_load` event-type discriminant.
  const dispatch = useDispatchPaneEvent();

  // Mirror every change back to localStorage so a modal close (or tool switch)
  // restores the last-typed criteria.
  const updateCriteria = React.useCallback(
    (patch: Partial<PersistedCriteria>) => {
      setCriteria((prev) => {
        const next = { ...prev, ...patch };
        writePersistedCriteria(next);
        return next;
      });
    },
    [],
  );

  const handleTypeSelect = React.useCallback(
    (id: SearchDomain) => {
      updateCriteria({ domain: id });
    },
    [updateCriteria],
  );

  const handleQueryChange = React.useCallback(
    (_e: unknown, data: { value: string }) => {
      updateCriteria({ query: data.value });
    },
    [updateCriteria],
  );

  // The search page's option lists (Dataverse values), shared with it (#3131).
  const filterOptions = useSearchFilterOptions();

  const handleDocumentTypesChange = React.useCallback(
    (selected: string[]) => updateCriteria({ documentTypes: selected }),
    [updateCriteria],
  );

  const handleFileTypesChange = React.useCallback(
    (selected: string[]) => updateCriteria({ fileTypes: selected }),
    [updateCriteria],
  );

  const handleMatterTypesChange = React.useCallback(
    (selected: string[]) => updateCriteria({ matterTypes: selected }),
    [updateCriteria],
  );

  // Store which preset the range is (detected today, when it was picked) so a reload re-resolves
  // it; a typed range is 'custom' and is kept as typed.
  const handleDateRangeChange = React.useCallback(
    (value: DateRangeFilterValue | null) => {
      updateCriteria({ dateRange: value, datePreset: detectActivePreset(value) });
    },
    [updateCriteria],
  );

  /**
   * R4 task 043 (W-5) — handles the "Also add to Workspace" opt-in.
   *
   * When the checkbox is checked, this function dispatches a typed
   * `widget_load` event on the `workspace` PaneEventBus channel BEFORE
   * launching the Semantic Search modal. The Workspace pane (subscribed
   * via `usePaneEvent`) resolves the widget via WorkspaceWidgetRegistry and
   * mounts SearchCriteriaResultWidget as a new tab labeled "Search:
   * <domain>".
   *
   * Per ADR-030: payload is typed end-to-end via `SearchCriteriaResultWidgetData`
   * (NO `any` cast). The filters travel as LABELS: the widget only displays
   * them, and a tab can outlive this session's option lists.
   *
   * Per ADR-028: no auth surface — the criteria are pure user input.
   * `authenticatedFetch` is NOT used at this dispatch site because no BFF
   * call is made here (the modal search path runs its own auth bootstrap).
   *
   * Per the task spec "MUST be optional" constraint: when the checkbox is
   * UNCHECKED, no dispatch occurs and the workspace pane is unchanged.
   * After dispatch, the checkbox is reset to its default (unchecked) so
   * each search-click re-affirms the user's intent.
   */
  const dispatchSearchCriteriaWidget = React.useCallback(
    (current: PersistedCriteria): void => {
      const applies = domainFilters(current.domain);
      const widgetData: SearchCriteriaResultWidgetData = {
        query: current.query,
        domain: current.domain,
        documentTypes: applies.documentTypes
          ? selectedLabels(current.documentTypes, filterOptions.documentTypes)
          : undefined,
        fileTypes: applies.fileTypes ? selectedLabels(current.fileTypes, filterOptions.fileTypes) : undefined,
        matterTypes: applies.matterTypes
          ? selectedLabels(current.matterTypes, filterOptions.matterTypes)
          : undefined,
        dateFrom: current.dateRange?.from ?? null,
        dateTo: current.dateRange?.to ?? null,
        capturedAt: new Date().toISOString(),
      };
      // Tab label: prefer a short, query-derived label so the tab is
      // recognizable when several searches are open. Falls back to the
      // domain name if the query is empty.
      const queryPreview = current.query.trim().slice(0, 40);
      const displayName =
        queryPreview.length > 0
          ? `Search: ${queryPreview}${current.query.trim().length > 40 ? '…' : ''}`
          : `Search: ${current.domain}`;

      dispatch('workspace', {
        type: 'widget_load',
        widgetType: SEARCH_CRITERIA_RESULT_WIDGET_TYPE,
        widgetData,
        displayName,
      });
    },
    [dispatch, filterOptions.documentTypes, filterOptions.fileTypes, filterOptions.matterTypes],
  );

  const handleAddToWorkspaceChange = React.useCallback(
    (_e: unknown, data: { checked: boolean | 'mixed' }) => {
      // Fluent v9 Checkbox can emit 'mixed' for indeterminate; we never
      // surface that state, so coerce to a plain boolean.
      setAddToWorkspace(data.checked === true);
    },
    [],
  );

  const handleSearchClick = React.useCallback(() => {
    if (addToWorkspace) {
      dispatchSearchCriteriaWidget(criteria);
      // Reset the opt-in for the next search-click so the user must
      // re-affirm. This matches the "MUST be optional" task-spec invariant
      // and prevents accidental tab pile-up.
      setAddToWorkspace(false);
    }
    launchSemanticSearch(criteria);
  }, [addToWorkspace, criteria, dispatchSearchCriteriaWidget]);

  // Per-domain filter visibility: the page's SearchFilterPane rule.
  const applies = domainFilters(criteria.domain);

  return (
    <div className={styles.root} data-testid="semantic-search-criteria-tool">
      {/*
        Task 106 — `criteria` wrapper applies max-width: 480px + marginLeft: 0
        + marginRight: auto so the form is left-aligned, max-width 480px so the
        form anchors to the pane's left edge instead of stretching or centering
        on wider screens (revises task 103's centering decision per Round 8
        operator feedback). Narrow panes unaffected — max-width only constrains.
      */}
      <div className={styles.criteria}>
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={400}>
          Search Criteria
        </Text>
      </div>

      {/* Type buttons (2x2 grid) -------------------------------------------- */}
      <div className={styles.typeGrid} data-testid="semantic-search-criteria-types">
        {DOMAINS.map((d) => {
          const isActive = d.id === criteria.domain;
          return (
            <Button
              key={d.id}
              appearance={isActive ? 'primary' : 'subtle'}
              icon={d.icon}
              onClick={() => handleTypeSelect(d.id)}
              className={mergeClasses(styles.typeButton)}
              aria-pressed={isActive}
              data-testid={`semantic-search-criteria-type-${d.id}`}
            >
              {d.label}
            </Button>
          );
        })}
      </div>

      {/* AI query --------------------------------------------------------- */}
      <div className={styles.field}>
        <Label className={styles.fieldLabel} htmlFor="ss-criteria-query">
          AI Search
        </Label>
        <Textarea
          id="ss-criteria-query"
          className={styles.textarea}
          value={criteria.query}
          onChange={handleQueryChange}
          placeholder="Describe what you're looking for..."
          resize="vertical"
          data-testid="semantic-search-criteria-query"
        />
      </div>

      {/* Per-domain filters (shared FilterDropdown, the page's option lists) - */}
      {applies.documentTypes && (
        <div data-testid="semantic-search-criteria-document-type">
          <FilterDropdown
            label="Document Type"
            options={filterOptions.documentTypes}
            selectedValues={criteria.documentTypes}
            onChange={handleDocumentTypesChange}
            isLoading={filterOptions.isLoading}
          />
        </div>
      )}

      {applies.fileTypes && (
        <div data-testid="semantic-search-criteria-file-type">
          <FilterDropdown
            label="File Type"
            options={filterOptions.fileTypes}
            selectedValues={criteria.fileTypes}
            onChange={handleFileTypesChange}
            isLoading={filterOptions.isLoading}
          />
        </div>
      )}

      {applies.matterTypes && (
        <div data-testid="semantic-search-criteria-matter-type">
          <FilterDropdown
            label="Matter Type"
            options={filterOptions.matterTypes}
            selectedValues={criteria.matterTypes}
            onChange={handleMatterTypesChange}
            isLoading={filterOptions.isLoading}
          />
        </div>
      )}

      {/* Date Range (all domains; shared DateRangeFilter) ------------------ */}
      <div data-testid="semantic-search-criteria-date-range">
        <DateRangeFilter value={criteria.dateRange} onChange={handleDateRangeChange} />
      </div>

      {/* R4 task 043 (W-5) — "Also add to Workspace" opt-in checkbox.
          Renders above the Search button. Unchecked by default (the task spec
          requires this be optional; the modal search remains the production
          path). When checked, clicking Search dispatches `widget_load` on the
          workspace channel, mounting a SearchCriteriaResultWidget tab.
       */}
      <div className={styles.addToWorkspaceRow}>
        <Checkbox
          checked={addToWorkspace}
          onChange={handleAddToWorkspaceChange}
          label="Also add to Workspace"
          data-testid="semantic-search-criteria-add-to-workspace"
        />
      </div>

      {/* Search button (full-width primary) ------------------------------- */}
      <div className={styles.searchButtonRow}>
        <Button
          appearance="primary"
          icon={<Search20Regular />}
          onClick={handleSearchClick}
          className={styles.searchButton}
          data-testid="semantic-search-criteria-submit"
        >
          Search
        </Button>
      </div>
      </div>
    </div>
  );
};

SemanticSearchCriteriaTool.displayName = 'SemanticSearchCriteriaTool';
