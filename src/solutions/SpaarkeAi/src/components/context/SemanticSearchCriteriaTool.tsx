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
 *   4. Per-domain filter dropdowns (placeholder static options + TODO for
 *      the filter-options endpoint wiring):
 *        - Documents domain → Document Type + File Type
 *        - Matters domain   → Matter Type
 *        - Projects / Invoices → none today
 *   5. Date Range dropdown (Off / Last 7 / Last 30 / Last 90 / Custom).
 *      "Custom range" is acknowledged as a static option today; expanding
 *      to two date inputs is a documented follow-up.
 *   6. Full-width primary Search button. Launches sprk_semanticsearch via
 *      Xrm.Navigation.navigateTo with the criteria as URL params (same shape
 *      as Task 095 but with the new params added).
 *
 * Persistence:
 *   Transient criteria state persists in localStorage under
 *   `spaarke:context:semantic-search-criteria` (backwards-compat: missing
 *   fields read as undefined and default to "All" / "Off" on next mount).
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
 *   data string carries (Task 099 — new params marked NEW):
 *     query=<encoded>&domain=<documents|matters|projects|invoices>
 *     &dateFrom=<YYYY-MM-DD>&dateTo=<YYYY-MM-DD>   (deprecated — Task 099 replaces with dateRange preset)
 *     &documentType=<encoded>     (NEW — Documents domain)
 *     &fileType=<encoded>         (NEW — Documents domain)
 *     &matterType=<encoded>       (NEW — Matters or Documents domain)
 *     &dateRange=<off|last7|last30|last90|custom>  (NEW — replaces from/to)
 *
 *   The SemanticSearch Code Page already parses `query` + `domain` from its
 *   data envelope (see src/client/code-pages/SemanticSearch/src/index.tsx).
 *   The new params will be received but are non-breaking if the page ignores
 *   them — the user can still set them via the full filter pane on the page
 *   side.
 *
 * Bug-fix invariant (matches the playbook-modal pane-blank fix in Task 095):
 *   When the user clicks Search, this component does NOT change the
 *   ContextPaneController's selectedTool. The localStorage-backed selectedTool
 *   means the Context pane re-renders with this tool still selected after the
 *   modal closes — that is the uniform fix for the "pane goes blank after a
 *   modal closes" bug.
 *
 * Visual reference:
 *   src/client/code-pages/SemanticSearch/src/components/SearchFilterPane.tsx
 *   src/client/code-pages/SemanticSearch/src/components/SearchDomainTabs.tsx
 *   src/client/code-pages/SemanticSearch/src/components/FilterDropdown.tsx
 *   src/client/code-pages/SemanticSearch/src/components/DateRangeFilter.tsx
 *   These files are READ for shape/UX but NOT imported — the SemanticSearch
 *   types live in a separate code-page that isn't a published npm package
 *   consumed by SpaarkeAi, and importing would defeat the SpaarkeAi bundle
 *   tree-shake. The `SearchDomain` union and filter shapes are replicated
 *   locally in this file.
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
  Dropdown,
  Option,
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
type SearchDomain = 'documents' | 'matters' | 'projects' | 'invoices';

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

/**
 * Date Range preset values. Mirrors the operator's screenshot — Off (default),
 * Last 7 days, Last 30 days, Last 90 days, Custom range. "Custom range" is a
 * static option today; expanding to inline date inputs is a documented
 * follow-up (see file header).
 */
type DateRangePreset = 'off' | 'last7' | 'last30' | 'last90' | 'custom';

interface DateRangeDescriptor {
  id: DateRangePreset;
  label: string;
}

const DATE_RANGE_OPTIONS: readonly DateRangeDescriptor[] = [
  { id: 'off', label: 'Off' },
  { id: 'last7', label: 'Last 7 days' },
  { id: 'last30', label: 'Last 30 days' },
  { id: 'last90', label: 'Last 90 days' },
  { id: 'custom', label: 'Custom range' },
];

const VALID_DATE_RANGES: ReadonlySet<string> = new Set<DateRangePreset>([
  'off',
  'last7',
  'last30',
  'last90',
  'custom',
]);

/**
 * Static placeholder filter options for Documents / Matters domains.
 *
 * TODO (filter-options endpoint wiring): replace these with values fetched
 * from the BFF filter-options endpoint (see `useFilterOptions` hook in the
 * full SemanticSearch Code Page). The Search button passes the chosen
 * values via URL query params to the modal, so the modal will get real data
 * on its own; the in-pane dropdowns just need a placeholder list that
 * matches the most common values until the endpoint is wired here.
 */
const DOCUMENT_TYPE_OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'contract', label: 'Contract' },
  { value: 'nda', label: 'NDA' },
  { value: 'brief', label: 'Brief' },
  { value: 'agreement', label: 'Agreement' },
  { value: 'invoice', label: 'Invoice' },
] as const;

const FILE_TYPE_OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'pdf', label: 'PDF' },
  { value: 'docx', label: 'DOCX' },
  { value: 'xlsx', label: 'XLSX' },
  { value: 'pptx', label: 'PPTX' },
  { value: 'txt', label: 'TXT' },
] as const;

const MATTER_TYPE_OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'litigation', label: 'Litigation' },
  { value: 'transactional', label: 'Transactional' },
  { value: 'regulatory', label: 'Regulatory' },
  { value: 'advisory', label: 'Advisory' },
] as const;

/** Shape persisted in localStorage (Task 099 extended). */
interface PersistedCriteria {
  query: string;
  domain: SearchDomain;
  documentType: string;
  fileType: string;
  matterType: string;
  dateRange: DateRangePreset;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const STORAGE_KEY = 'spaarke:context:semantic-search-criteria';

const DEFAULT_CRITERIA: PersistedCriteria = {
  query: '',
  domain: 'documents',
  documentType: 'all',
  fileType: 'all',
  matterType: 'all',
  dateRange: 'off',
};

const SEMANTIC_SEARCH_WEBRESOURCE = 'sprk_semanticsearch';
const SEMANTIC_SEARCH_TITLE = 'Semantic Search Results';

// ---------------------------------------------------------------------------
// localStorage helpers — try/catch-wrapped for private browsing / quota
// ---------------------------------------------------------------------------

/**
 * Read criteria from localStorage. Backwards-compatible with the Task 095
 * shape — missing fields (`documentType`, `fileType`, `matterType`,
 * `dateRange`) read as undefined and default to "All" / "Off" on next mount.
 * The previous `dateFrom` / `dateTo` fields are silently ignored (no
 * migration needed — they were never wired to BFF; the user just re-picks
 * a Date Range preset on their next session).
 */
function readPersistedCriteria(): PersistedCriteria {
  if (typeof window === 'undefined') return { ...DEFAULT_CRITERIA };
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) return { ...DEFAULT_CRITERIA };
    const parsed = JSON.parse(raw) as unknown;
    if (typeof parsed !== 'object' || parsed === null) {
      return { ...DEFAULT_CRITERIA };
    }
    const obj = parsed as Record<string, unknown>;
    return {
      query: typeof obj.query === 'string' ? obj.query : DEFAULT_CRITERIA.query,
      domain:
        typeof obj.domain === 'string' && VALID_DOMAINS.has(obj.domain)
          ? (obj.domain as SearchDomain)
          : DEFAULT_CRITERIA.domain,
      documentType:
        typeof obj.documentType === 'string'
          ? obj.documentType
          : DEFAULT_CRITERIA.documentType,
      fileType:
        typeof obj.fileType === 'string' ? obj.fileType : DEFAULT_CRITERIA.fileType,
      matterType:
        typeof obj.matterType === 'string'
          ? obj.matterType
          : DEFAULT_CRITERIA.matterType,
      dateRange:
        typeof obj.dateRange === 'string' && VALID_DATE_RANGES.has(obj.dateRange)
          ? (obj.dateRange as DateRangePreset)
          : DEFAULT_CRITERIA.dateRange,
    };
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
// Xrm frame-walk — same shape as wizardLaunchers.ts (proven across iframes)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

function resolveXrmNavigation(): { navigateTo: (...args: unknown[]) => Promise<unknown> } | null {
  if (typeof window === 'undefined') return null;
  try {
    const w = window as any;
    const xrm = w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
    if (!xrm?.Navigation?.navigateTo) return null;
    return xrm.Navigation;
  } catch {
    return null;
  }
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Modal launch — Xrm.Navigation.navigateTo to sprk_semanticsearch
// ---------------------------------------------------------------------------

function buildSearchDataParams(criteria: PersistedCriteria): string {
  const parts: string[] = [];
  if (criteria.query) parts.push(`query=${encodeURIComponent(criteria.query)}`);
  if (criteria.domain) parts.push(`domain=${encodeURIComponent(criteria.domain)}`);

  // Task 099 — per-domain filters. Only emit when the user has selected a
  // non-"all" value AND the filter is relevant for the active domain. The
  // SemanticSearch Code Page receives these as query params and seeds its
  // own filter state; if a param is absent the page falls back to "All".
  if (criteria.domain === 'documents') {
    if (criteria.documentType && criteria.documentType !== 'all') {
      parts.push(`documentType=${encodeURIComponent(criteria.documentType)}`);
    }
    if (criteria.fileType && criteria.fileType !== 'all') {
      parts.push(`fileType=${encodeURIComponent(criteria.fileType)}`);
    }
  }
  if (
    (criteria.domain === 'matters' || criteria.domain === 'documents') &&
    criteria.matterType &&
    criteria.matterType !== 'all'
  ) {
    parts.push(`matterType=${encodeURIComponent(criteria.matterType)}`);
  }

  // Task 099 — date range preset (replaces the Task 095 from/to inputs).
  if (criteria.dateRange && criteria.dateRange !== 'off') {
    parts.push(`dateRange=${encodeURIComponent(criteria.dateRange)}`);
  }

  return parts.join('&');
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
  const nav = resolveXrmNavigation();
  if (nav === null) {
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

  const handleDocumentTypeChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      if (typeof data.optionValue === 'string') {
        updateCriteria({ documentType: data.optionValue });
      }
    },
    [updateCriteria],
  );

  const handleFileTypeChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      if (typeof data.optionValue === 'string') {
        updateCriteria({ fileType: data.optionValue });
      }
    },
    [updateCriteria],
  );

  const handleMatterTypeChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      if (typeof data.optionValue === 'string') {
        updateCriteria({ matterType: data.optionValue });
      }
    },
    [updateCriteria],
  );

  const handleDateRangeChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      if (typeof data.optionValue === 'string' && VALID_DATE_RANGES.has(data.optionValue)) {
        updateCriteria({ dateRange: data.optionValue as DateRangePreset });
      }
    },
    [updateCriteria],
  );

  const handleSearchClick = React.useCallback(() => {
    launchSemanticSearch(criteria);
  }, [criteria]);

  // Per-domain filter visibility. Mirrors SearchFilterPane.tsx logic:
  //   - documents → Document Type + File Type + Matter Type
  //   - matters   → Matter Type
  //   - projects  → (no per-domain filters today; only date range)
  //   - invoices  → (no per-domain filters today; only date range)
  const showDocumentTypeFilter = criteria.domain === 'documents';
  const showFileTypeFilter = criteria.domain === 'documents';
  const showMatterTypeFilter =
    criteria.domain === 'documents' || criteria.domain === 'matters';

  // Display labels for current filter values (used as Dropdown `value`).
  const documentTypeLabel =
    DOCUMENT_TYPE_OPTIONS.find((o) => o.value === criteria.documentType)?.label ?? 'All';
  const fileTypeLabel =
    FILE_TYPE_OPTIONS.find((o) => o.value === criteria.fileType)?.label ?? 'All';
  const matterTypeLabel =
    MATTER_TYPE_OPTIONS.find((o) => o.value === criteria.matterType)?.label ?? 'All';
  const dateRangeLabel =
    DATE_RANGE_OPTIONS.find((o) => o.id === criteria.dateRange)?.label ?? 'Off';

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

      {/* Document Type (Documents domain only) ---------------------------- */}
      {showDocumentTypeFilter && (
        <div className={styles.field}>
          <Label className={styles.fieldLabel} htmlFor="ss-criteria-document-type">
            Document Type
          </Label>
          <Dropdown
            id="ss-criteria-document-type"
            className={styles.dropdown}
            value={documentTypeLabel}
            selectedOptions={[criteria.documentType]}
            onOptionSelect={handleDocumentTypeChange}
            data-testid="semantic-search-criteria-document-type"
          >
            {DOCUMENT_TYPE_OPTIONS.map((o) => (
              <Option key={o.value} value={o.value} text={o.label}>
                {o.label}
              </Option>
            ))}
          </Dropdown>
        </div>
      )}

      {/* File Type (Documents domain only) -------------------------------- */}
      {showFileTypeFilter && (
        <div className={styles.field}>
          <Label className={styles.fieldLabel} htmlFor="ss-criteria-file-type">
            File Type
          </Label>
          <Dropdown
            id="ss-criteria-file-type"
            className={styles.dropdown}
            value={fileTypeLabel}
            selectedOptions={[criteria.fileType]}
            onOptionSelect={handleFileTypeChange}
            data-testid="semantic-search-criteria-file-type"
          >
            {FILE_TYPE_OPTIONS.map((o) => (
              <Option key={o.value} value={o.value} text={o.label}>
                {o.label}
              </Option>
            ))}
          </Dropdown>
        </div>
      )}

      {/* Matter Type (Documents + Matters domains) ------------------------ */}
      {showMatterTypeFilter && (
        <div className={styles.field}>
          <Label className={styles.fieldLabel} htmlFor="ss-criteria-matter-type">
            Matter Type
          </Label>
          <Dropdown
            id="ss-criteria-matter-type"
            className={styles.dropdown}
            value={matterTypeLabel}
            selectedOptions={[criteria.matterType]}
            onOptionSelect={handleMatterTypeChange}
            data-testid="semantic-search-criteria-matter-type"
          >
            {MATTER_TYPE_OPTIONS.map((o) => (
              <Option key={o.value} value={o.value} text={o.label}>
                {o.label}
              </Option>
            ))}
          </Dropdown>
        </div>
      )}

      {/* Date Range (all domains) ----------------------------------------- */}
      <div className={styles.field}>
        <Label className={styles.fieldLabel} htmlFor="ss-criteria-date-range">
          Date Range
        </Label>
        <Dropdown
          id="ss-criteria-date-range"
          className={styles.dropdown}
          value={dateRangeLabel}
          selectedOptions={[criteria.dateRange]}
          onOptionSelect={handleDateRangeChange}
          data-testid="semantic-search-criteria-date-range"
        >
          {DATE_RANGE_OPTIONS.map((o) => (
            <Option key={o.id} value={o.id} text={o.label}>
              {o.label}
            </Option>
          ))}
        </Dropdown>
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
