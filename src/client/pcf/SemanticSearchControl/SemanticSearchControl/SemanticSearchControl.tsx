/**
 * SemanticSearchControl - Main component for semantic document search.
 *
 * Provides a stacked layout (FR-DOC-04/05/06):
 * - Header: Search input + document count
 * - Command bar: Associated Only · File Type · Date Range · Threshold · Mode ·
 *                Tags · view toggle (list | card)
 * - Main: Results — either ListView (default) or card ResultsList depending
 *         on the persisted view preference.
 *
 * The sidebar `FilterPanel` is no longer rendered in v1 (FR-DOC-06). The file
 * itself is kept for safe rollback should integration testing surface a
 * regression — re-enable by re-importing + rendering it inside `<div className={styles.content}>`.
 *
 * BINDING (spec FR-DOC-06): the AssociatedOnly auto-search `useEffect` below
 * MUST remain byte-identical across this refactor. Only the visible trigger
 * (sidebar Switch → command-bar Switch) changed.
 *
 * @see ADR-021 for Fluent UI v9 and design token requirements
 * @see ADR-022 React 16/17 platform boundary
 * @see spec.md FR-DOC-04 / FR-DOC-05 / FR-DOC-06
 */

import * as React from 'react';
import { useState, useCallback, useMemo, useEffect, useRef } from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Text,
  Link,
  Button,
  Tooltip,
  Toast,
  ToastTitle,
  ToastBody,
  Toaster,
  useId,
  useToastController,
} from '@fluentui/react-components';
import { Add20Regular, ArrowClockwise20Regular, Open20Regular } from '@fluentui/react-icons';
import {
  ISemanticSearchControlProps,
  SearchFilters,
  SearchResult,
  SearchScope,
  SummaryData,
} from './types';
import {
  SearchInput,
  ResultsList,
  LoadingState,
  EmptyState,
  ErrorState,
  ListView,
  CommandBar,
  BulkActionBar,
  FilePreviewDialog,
  type ListSortColumn,
  type ListSortDirection,
} from './components';
import { useSemanticSearch, useFilters, useFilterOptions, useDocumentListPrefs } from './hooks';
import { SemanticSearchApiService, NavigationService, DataverseMetadataService } from './services';
import type { TagFilterOption } from '@spaarke/ui-components/dist/types/TagFilter';
import { authenticatedFetch, resolveTenantIdSync } from '@spaarke/auth';
import { initializeAuth } from './authInit';
import { getEnvironmentVariable, getApiBaseUrl } from '../../shared/utils/environmentVariables';
import { FindSimilarDialog } from '@spaarke/ui-components/dist/components/FindSimilarDialog';
import { DocumentEmailWizard, type IDocumentEmailWizardItem } from '@spaarke/ui-components/dist/components/DocumentEmailWizard';
import { AppInsightsService } from '@spaarke/ui-components/dist/services/AppInsightsService';
import type { IDataService } from '@spaarke/ui-components/dist/types/serviceInterfaces';

/**
 * Styles using makeStyles with Fluent design tokens.
 * NO hard-coded colors - all values from tokens (ADR-021).
 */
const useStyles = makeStyles({
  // Root container
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    width: '100%',
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    fontFamily: tokens.fontFamilyBase,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    ...shorthands.overflow('hidden'),
  },

  // Compact mode root adjustments
  rootCompact: {
    maxHeight: '400px',
  },

  // Header region (search input)
  header: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.padding(tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
  },

  // Main content area (single column, no sidebar — FR-DOC-06)
  content: {
    display: 'flex',
    flex: 1,
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
  },

  // Main region (results list)
  main: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
  },

  // Footer count strip — sits at the bottom of the results area to communicate
  // total + filtered counts (FR-DOC-05 acceptance).
  footerCount: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },

  // Footer for compact mode "View all" link
  footer: {
    display: 'flex',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingHorizontalS),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
  },

  // Centered content container for states
  centeredState: {
    flex: 1,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    ...shorthands.padding(tokens.spacingHorizontalL),
  },

  // Toolbar above the list/card surface — same row hosts the bulk-action
  // group (leading edge, icon-only) and the Reload/Add buttons (trailing
  // edge). The internal <BulkActionBar> renders null at zero selection
  // so the row gracefully collapses to just Reload + Add when nothing is
  // selected (UAT request — no separate sticky bulk bar).
  // v1.1.49 — UAT Item 3: bump the horizontal gap between toolbar icons
  // from `S` to `M` so refresh / add / open-full-view + the bulk-action
  // icon group breathe. Other rules unchanged.
  emptyStateToolbar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
    ...shorthands.borderBottom(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke2),
  },
  emptyStateToolbarButton: {
    minWidth: 'auto',
    ...shorthands.padding('0px'),
  },

  // Version footer (always visible)
  versionFooter: {
    display: 'flex',
    justifyContent: 'flex-end',
    ...shorthands.padding(tokens.spacingHorizontalXS, tokens.spacingHorizontalM),
    backgroundColor: tokens.colorNeutralBackground2,
    ...shorthands.borderTop(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
});

/**
 * SemanticSearchControl main component.
 *
 * Renders the complete search UI with configurable layout based on compactMode.
 */
export const SemanticSearchControl: React.FC<ISemanticSearchControlProps> = ({
  context,
  notifyOutputChanged,
  onDocumentSelect,
  isDarkMode = false,
}) => {
  const styles = useStyles();

  // Auth state — initialized in useEffect below so React re-renders when auth completes.
  // Matches RelatedDocumentCount pattern: auth inside component, not PCF class.
  const [isAuthInitialized, setIsAuthInitialized] = useState(false);
  const [resolvedApiBaseUrl, setResolvedApiBaseUrl] = useState('');

  // Get control properties from context.
  // NOTE (v1.1.45): `showFilters` is intentionally read but unused — the
  // command bar always renders in non-compact mode (see render block below).
  // Kept for manifest backward compatibility; treat as deprecated.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const showFilters = context.parameters.showFilters?.raw ?? true;
  void showFilters;
  const compactMode = context.parameters.compactMode?.raw ?? false;
  const placeholder = context.parameters.placeholder?.raw ?? 'Search documents...';

  // v1.1.47 — Host-level view-mode config.
  // - `showViewToggle` (TwoOptions, default true): when false, the CommandBar
  //    hides the list/card tab group AND we lock `view` to `defaultView`
  //    (ignoring the per-(user, matter) localStorage pref so the lock is
  //    actually enforced rather than just visually hidden).
  // - `defaultView` (Enum 'list'|'card', default 'list'): initial view when
  //    no localStorage pref exists, AND the locked view when the toggle is
  //    hidden.
  // Defaults preserve v1.1.46 behavior (toggle visible, list-first initial).
  const showViewToggle = context.parameters.showViewToggle?.raw ?? true;
  const rawDefaultView = (context.parameters as unknown as { defaultView?: { raw?: string | null } }).defaultView?.raw;
  const defaultView: 'list' | 'card' = rawDefaultView === 'card' ? 'card' : 'list';
  // BFF API base URL — resolved by auth useEffect below, stored in state
  const apiBaseUrl = resolvedApiBaseUrl;

  // Auto-detect entity context from page (when on a record form)
  // Uses the record's GUID directly instead of relying on bound field
  // Note: context.page exists at runtime but isn't in @types/powerapps-component-framework
  const pageContext = (
    context as unknown as {
      page?: { entityId?: string; entityTypeName?: string };
    }
  ).page;
  const pageEntityId = pageContext?.entityId ?? null;
  const pageEntityTypeName = pageContext?.entityTypeName ?? null;

  // Page context detection — debug log removed per FR-DOC-07 (telemetry-only
  // logging in production code path; structured properties only, no PII).

  // Map Dataverse entity logical names to API entity types
  const getEntityTypeFromLogicalName = (logicalName: string | null): string | null => {
    if (!logicalName) return null;
    const mapping: Record<string, string> = {
      sprk_matter: 'matter',
      sprk_project: 'project',
      sprk_invoice: 'invoice',
      sprk_document: 'document',
      account: 'account',
      contact: 'contact',
    };
    // Return mapped name, or fall back to the raw logical name so any entity
    // form still scopes correctly (the BFF accepts arbitrary entityType values).
    return mapping[logicalName.toLowerCase()] ?? logicalName.toLowerCase();
  };

  // Determine search scope and entity context
  // Priority: 1) Page context auto-detection, 2) Configured scope parameter
  // When configured as "entity" (or legacy "matter"), auto-detect from form context.
  // When configured as "all" or "custom", use as-is.
  const configuredScope = (context.parameters.searchScope?.raw ?? 'entity') as SearchScope;
  const parameterScopeId = context.parameters.scopeId?.raw ?? null;

  // Auto-detect entity type from page context (works on any entity form)
  const detectedEntityType = getEntityTypeFromLogicalName(pageEntityTypeName);

  // Resolve final search scope:
  // - If on a record form with a page entity ID → always scope to that entity
  // - If configured as "entity" but NOT on a form → keep "entity" (will show no results
  //   rather than searching all documents unscoped)
  // - If configured as "all" or "custom" → use as-is
  // - Legacy: "matter" configured value still works (auto-detection overrides when on form)
  let searchScope: SearchScope;
  if (pageEntityId && detectedEntityType) {
    // On a record form → auto-detect overrides any configured scope
    searchScope = detectedEntityType as SearchScope;
  } else if (pageEntityId && !detectedEntityType) {
    // On a record form but entity type not mapped → use configured scope
    // (scopeId below will still be set to pageEntityId)
    searchScope = configuredScope;
  } else {
    // Not on a record form → use configured scope as-is
    // Note: "entity" scope without a scopeId will return no results (safe default)
    searchScope = configuredScope;
  }

  // Use page entityId (GUID) when on a record form, otherwise use bound parameter
  const scopeId = pageEntityId ?? parameterScopeId;

  // Scope determination — debug log removed per FR-DOC-07.

  // Query input state
  const [queryInput, setQueryInput] = useState('');
  const [hasSearched, setHasSearched] = useState(false);

  // Find Similar dialog state — URL of the web resource to show in the iframe dialog
  const [findSimilarUrl, setFindSimilarUrl] = useState<string | null>(null);

  // ── v1.1.49 — Host-level preview dialog state (Item 6) ─────────────────
  // Both list view AND card view now route preview-open through the SAME
  // host-mounted FilePreviewDialog so the navigation set (Prev/Next) is
  // shared across views. When the user picks 5 cards then clicks one
  // preview, Prev/Next walks the 5; when nothing is selected, it walks
  // the full current result set. ListView's internal preview state is
  // retained for back-compat (it now stays unused since the host owns the
  // dialog, but pulling it out would be a larger refactor of ListView's
  // public surface — leave for future cleanup).
  const [hostPreviewDocId, setHostPreviewDocId] = useState<string | null>(null);

  // Multi-document email wizard state. When open, passes the current results
  // (top N visible) into the wizard's first step where the user can deselect
  // any docs they don't want before composing.
  const [emailWizardOpen, setEmailWizardOpen] = useState(false);

  // Holds the single document item when DocumentEmailWizard is launched
  // from a row's 3-dot menu; null when launched from the bulk-toolbar
  // Email icon (in which case the wizard receives
  // `emailWizardItemsSelected` — only the CHECKED docs as of v1.1.63;
  // previously the entire result set was passed and the user had to
  // prune in step 1). The wizard's `selectedDocuments` prop reads
  // `singleDocForWizard ? [it] : emailWizardItemsSelected` so one
  // wizard instance serves both flows.
  const [singleDocForWizard, setSingleDocForWizard] = useState<IDocumentEmailWizardItem | null>(null);

  // Initialize services (memoized to prevent recreation)
  const apiService = useMemo(() => new SemanticSearchApiService(apiBaseUrl), [apiBaseUrl]);
  const navigationService = useMemo(() => new NavigationService(), []);
  // Dedicated metadata service for FR-DOC-05 (Tags filter). Note: useFilterOptions
  // below also uses a singleton DataverseMetadataService — we mount a second
  // instance here to keep the Tags fetch independently cacheable and to avoid
  // restructuring the existing useFilterOptions return shape.
  const metadataService = useMemo(() => new DataverseMetadataService(), []);

  // ── FR-DOC-04: per-(userId, matterId)-scoped UI prefs (view + pins) ─────
  // userId from PCF user settings; matterId from the resolved scopeId (the
  // entity ID of the current record form). Both are nullable during the
  // initial auth bootstrap — useDocumentListPrefs handles that gracefully.
  const userIdForPrefs =
    (context.userSettings as unknown as { userId?: string } | undefined)?.userId ?? null;
  // `isPinned` from the hook is unused at this scope — ListView consults `pinnedIds`
  // directly. Keeping the destructure simple by omitting it.
  const { view, setView, pinnedIds, togglePin, columnWidths, setColumnWidth } = useDocumentListPrefs(
    userIdForPrefs,
    pageEntityId
  );

  // v1.1.47 — Effective view: when the host hides the view-toggle group via
  // `showViewToggle === false`, lock the view to `defaultView` (ignore the
  // per-(user, matter) localStorage pref). Otherwise, use the persisted pref
  // as-is — the hook already falls back to its internal default when storage
  // is empty, but we override the hook's default with the manifest default
  // when the user has no persisted pref yet (initial mount path).
  //
  // We detect "no persisted pref" by comparing the hook's view to its own
  // internal default ('list'). When that matches AND the host has chosen
  // `defaultView='card'`, we honor the host preference. This keeps the hook
  // signature stable (back-compat for the unchanged FR-DOC-04 contract).
  const HOOK_DEFAULT_VIEW: 'list' | 'card' = 'list';
  const effectiveView: 'list' | 'card' = !showViewToggle
    ? defaultView
    : (view === HOOK_DEFAULT_VIEW && defaultView !== HOOK_DEFAULT_VIEW
        ? defaultView
        : view);

  // FR-DOC-07: wrap setView with telemetry. The CommandBar's view toggle and
  // any other caller flow through this wrapper so we observe every change.
  // v1.1.47 — when the toggle is hidden the host has locked the view; ignore
  // writes silently (the CommandBar also doesn't render the toggle, so this is
  // defense-in-depth against any other caller).
  const handleViewChange = useCallback(
    (next: 'list' | 'card') => {
      if (!showViewToggle) return;
      AppInsightsService.trackEvent('view_toggled', { value: next });
      setView(next);
    },
    [setView, showViewToggle]
  );

  // ── FR-DOC-04: list-view sort + selection state ─────────────────────────
  // Selection persists across list↔card view toggles (in-memory only, not
  // localStorage — per Owner Clarifications for v1).
  const [sortColumn, setSortColumn] = useState<ListSortColumn>('modifiedAt');
  const [sortDirection, setSortDirection] = useState<ListSortDirection>('desc');
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const handleSortChange = useCallback(
    (next: { column: ListSortColumn; direction: ListSortDirection }) => {
      setSortColumn(next.column);
      setSortDirection(next.direction);
    },
    []
  );

  // ── FR-DOC-05: Tags filter state ────────────────────────────────────────
  const [tagOptions, setTagOptions] = useState<TagFilterOption[]>([]);
  const [selectedTags, setSelectedTags] = useState<string[]>([]);

  // FR-DOC-07: wrap setSelectedTags so we emit `tag_filter_applied` on each
  // change. `tag_count` is the spec-required structured property (FR-DOC-07);
  // no PII (we never log the tag VALUES themselves).
  const handleSelectedTagsChange = useCallback((next: string[]) => {
    AppInsightsService.trackEvent('tag_filter_applied', { tag_count: next.length });
    setSelectedTags(next);
  }, []);
  useEffect(() => {
    // Fetch sprk_documenttype option set once on mount. DataverseMetadataService
    // caches per (entity, attribute) so this is cheap on subsequent mounts.
    let cancelled = false;
    void metadataService
      .getDocumentTypeOptions('sprk_document', 'sprk_documenttype')
      .then(options => {
        if (cancelled) return;
        // Map FilterOption {key,label} → TagFilterOption {value,label}.
        setTagOptions(options.map(o => ({ value: o.key, label: o.label })));
      })
      .catch(err => {
        if (!cancelled) {
          console.warn('[SemanticSearchControl] Tags filter option fetch failed:', err);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [metadataService]);

  // Auth initialization — runs once on mount inside the React component.
  // This mirrors RelatedDocumentCount's pattern: auth in useEffect with useState
  // so the component re-renders itself when auth completes, without depending on
  // notifyOutputChanged() triggering updateView() (which is not reliable for ReactControl).
  useEffect(() => {
    let cancelled = false;

    // Capture values at mount time — these don't change during the control's lifetime
    const webApi = context.webAPI;
    const manifestApiBaseUrl = context.parameters.apiBaseUrl?.raw ?? '';
    const manifestTenantId = context.parameters.tenantId?.raw ?? '';
    const manifestClientAppId = context.parameters.clientAppId?.raw ?? '';
    const manifestBffAppId = context.parameters.bffAppId?.raw ?? '';
    // FR-TEL-01: App Insights instrumentation key (manifest-property env-var pattern).
    // Initialize is idempotent — safe if the parent control already initialized.
    const appInsightsKey =
      (context.parameters as unknown as { appInsightsKey?: { raw?: string } }).appInsightsKey?.raw ?? '';
    if (appInsightsKey) {
      AppInsightsService.initialize(appInsightsKey);
    }

    let dataverseUrl: string;
    try {
      if (typeof Xrm !== 'undefined' && Xrm.Utility?.getGlobalContext) {
        dataverseUrl = Xrm.Utility.getGlobalContext().getClientUrl();
      } else {
        dataverseUrl = window.location.origin;
      }
    } catch {
      dataverseUrl = window.location.origin;
    }

    const doAuth = async () => {
      const apiBaseUrlResolved = manifestApiBaseUrl || (await getApiBaseUrl(webApi));
      const tenantId = manifestTenantId || (await getEnvironmentVariable(webApi, 'sprk_TenantId')) || '';
      const clientAppId =
        manifestClientAppId || (await getEnvironmentVariable(webApi, 'sprk_MsalClientId')) || '';
      const bffAppId =
        manifestBffAppId || (await getEnvironmentVariable(webApi, 'sprk_BffApiAppId')) || '';

      await initializeAuth(tenantId, clientAppId, bffAppId, apiBaseUrlResolved, dataverseUrl);

      if (!cancelled) {
        setResolvedApiBaseUrl(apiBaseUrlResolved);
        setIsAuthInitialized(true);
      }
    };

    doAuth().catch(err => {
      if (!cancelled) {
        console.error('[SemanticSearchControl] Auth initialization failed:', err);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []); // Run once on mount — context.webAPI and parameters are stable for the control's lifetime

  // Filter state management — declared BEFORE auth effects so useEffect can reference filters
  const { filters, setFilters, clearFilters, hasActiveFilters } = useFilters();

  // Search state management — declared BEFORE auth effects so useEffect can reference search
  const { results, totalCount, isLoading, isLoadingMore, error, hasMore, query, search, loadMore, reset } =
    useSemanticSearch(apiService, searchScope, scopeId);

  // Auto-load all documents once auth is ready (shows documents without requiring a search query).
  // search and filters intentionally omitted from deps — we only want to fire once on auth ready.
  useEffect(() => {
    if (isAuthInitialized) {
      setHasSearched(true);
      void search('', filters);
    }
  }, [isAuthInitialized]);

  // Execute search — empty query is allowed (returns all documents in scope)
  const handleSearch = useCallback(() => {
    if (!isAuthInitialized) {
      console.warn('[SemanticSearchControl] Cannot search - auth not initialized');
      return;
    }
    setHasSearched(true);
    void search(queryInput, filters);
  }, [queryInput, filters, search, isAuthInitialized]);

  // Auto-re-search when "Associated Only" flips. This toggle swaps the entire
  // backend path (Dataverse-direct vs AI Search), so the previous result set
  // is meaningless — the user expects the grid to update immediately, not to
  // hunt for an "Apply" button.
  const associatedOnlyRef = useRef<boolean | undefined>(filters.associatedOnly);
  useEffect(() => {
    if (!isAuthInitialized) {
      // Auth not ready — skip silently. Debug log removed per FR-DOC-07.
      return;
    }
    // Skip the very first render — we don't want to fire a search before the
    // user has interacted with the toggle. Only react to true value CHANGES.
    if (associatedOnlyRef.current === filters.associatedOnly) return;
    // Emit telemetry for the toggle change. Behavior (auto-re-search) is
    // unchanged; this just replaces the previous console.log line.
    AppInsightsService.trackEvent('associated_only_toggled', {
      value: !!filters.associatedOnly,
    });
    associatedOnlyRef.current = filters.associatedOnly;
    setHasSearched(true);
    void search(queryInput, filters);
  }, [filters.associatedOnly, isAuthInitialized, queryInput, search, filters]);

  // Handle filter changes - update filter state only.
  // Search is triggered explicitly via Enter key or Search button click.
  const handleFiltersChange = useCallback(
    (newFilters: SearchFilters) => {
      setFilters(newFilters);
    },
    [setFilters]
  );

  // v1.1.51 (Item 1) — Clear filters callback wired into CommandBar.
  //
  // Resets ONLY: fileTypes, dateRange, threshold, searchMode, selectedTags.
  //
  // BINDING (FR-DOC-06): preserves `filters.associatedOnly` verbatim. The
  // scope toggle is NOT a filter — it triggers the auto-search effect
  // (see lines ~509-530 above) and the spec mandates that toggle behavior
  // remain untouched. If a user has "Associated Only" selected, the Clear
  // button must not silently flip them back to "All Documents" (which
  // would re-fire the union path and surprise the user).
  //
  // We use a single batched `setFilters` call so the parent's auto-search
  // effect compares the new associatedOnly value against the ref — which
  // is unchanged — and therefore does NOT re-fire. The user must still hit
  // Search (or Enter) to apply the cleared filters; this mirrors the
  // existing handleFiltersChange contract.
  const handleClearFilters = useCallback(() => {
    setFilters({
      ...filters,
      fileTypes: [],
      dateRange: null,
      threshold: 0,
      searchMode: 'hybrid',
      // associatedOnly intentionally unchanged (FR-DOC-06)
    });
    setSelectedTags([]);
  }, [filters, setFilters]);

  // Handle retry after error
  const handleRetry = useCallback(() => {
    if (query.trim()) {
      void search(query, filters);
    }
  }, [query, filters, search]);

  // Handle result click
  const handleResultClick = useCallback(
    (result: SearchResult) => {
      onDocumentSelect(result.documentId);
      notifyOutputChanged();
    },
    [onDocumentSelect, notifyOutputChanged]
  );

  // Handle open file — mode is "web" (browser) or "desktop" (Office app).
  // Calls the BFF /api/documents/{id}/open-links endpoint on demand to get the
  // SharePoint webUrl and desktop protocol URL (ms-word://, ms-excel://, etc.)
  // since search results do not pre-populate fileUrl.
  const handleOpenFile = useCallback(
    (result: SearchResult, mode: 'web' | 'desktop') => {
      apiService
        .getOpenLinks(result.documentId)
        .then(async openLinks => {
          // Desktop protocol available (Word, Excel, PowerPoint) — open in native app
          if (openLinks.desktopUrl) {
            return window.open(openLinks.desktopUrl, '_self');
          }
          // No desktop protocol — download via BFF and let OS open with default app
          // (e.g. Adobe Acrobat for PDFs). SPE webUrl requires SharePoint permissions
          // users may not have, so we stream through the BFF's /content endpoint.
          try {
            const contentUrl = `${apiBaseUrl}/api/documents/${encodeURIComponent(result.documentId)}/content`;
            const response = await authenticatedFetch(contentUrl);
            if (response.ok) {
              const blob = await response.blob();
              const objectUrl = URL.createObjectURL(blob);
              const a = document.createElement('a');
              a.href = objectUrl;
              a.download = openLinks.fileName ?? result.name ?? 'document';
              document.body.appendChild(a);
              a.click();
              document.body.removeChild(a);
              URL.revokeObjectURL(objectUrl);
              return;
            }
          } catch (err) {
            console.error('[SemanticSearchControl] Download failed, falling back:', err);
          }
          // Final fallback to webUrl
          return window.open(openLinks.webUrl, '_blank', 'noopener,noreferrer');
        })
        .catch(err => {
          console.error('[SemanticSearchControl] Failed to open file:', err);
        });
    },
    [apiService, apiBaseUrl]
  );

  // Handle open record
  const handleOpenRecord = useCallback(
    (result: SearchResult, inModal: boolean) => {
      if (inModal) {
        void navigationService.openRecordModal(result);
      } else {
        void navigationService.openRecordNewTab(result);
      }
    },
    [navigationService]
  );

  // Handle view all navigation (when DOM cap reached)
  const handleViewAll = useCallback(() => {
    void navigationService.viewAllResults(query, searchScope, scopeId, filters);
  }, [navigationService, query, searchScope, scopeId, filters]);

  // Handle reload — re-run the current search query. Empty query is supported
  // (the hook returns all documents in scope), so this also works in the
  // initial/empty state where the user hasn't typed anything yet.
  const handleReload = useCallback(() => {
    void search(query || '', filters);
  }, [query, search, filters]);

  // Handle Find Similar — opens DocumentRelationshipViewer as an in-page iframe dialog
  const handleFindSimilar = useCallback(
    (result: SearchResult) => {
      const url = navigationService.getFindSimilarUrl(result, isDarkMode);
      if (url) setFindSimilarUrl(url);
    },
    [navigationService, isDarkMode]
  );

  // Preview URL cache — avoids redundant BFF calls for the same document
  const previewUrlCache = useRef<Map<string, string>>(new Map());

  // Handle Preview — fetches a read-only preview URL via Graph API preview endpoint
  const handlePreview = useCallback(
    async (result: SearchResult): Promise<string | null> => {
      // Return cached URL if available
      const cached = previewUrlCache.current.get(result.documentId);
      if (cached) return cached;

      try {
        const url = await apiService.getPreviewUrl(result.documentId);
        if (url) {
          previewUrlCache.current.set(result.documentId, url);
        }
        return url;
      } catch (err) {
        console.error('[SemanticSearchControl] Failed to get preview URL:', err);
        return null;
      }
    },
    [apiService]
  );

  // Summary cache — avoids redundant Dataverse calls for the same document
  const summaryCache = useRef<Map<string, SummaryData>>(new Map());

  // Handle Summary — fetches summary directly from Dataverse via PCF WebAPI
  const handleSummary = useCallback(
    async (result: SearchResult): Promise<SummaryData> => {
      // Return cached data if available
      const cached = summaryCache.current.get(result.documentId);
      if (cached) return cached;

      try {
        const record = await context.webAPI.retrieveRecord(
          'sprk_document',
          result.documentId,
          '?$select=sprk_filesummary,sprk_filetldr'
        );
        const entity = record as Record<string, unknown>;
        const data: SummaryData = {
          summary: (entity.sprk_filesummary as string) ?? null,
          tldr: (entity.sprk_filetldr as string) ?? null,
        };
        summaryCache.current.set(result.documentId, data);
        return data;
      } catch (err) {
        console.error('[SemanticSearchControl] Failed to fetch summary from Dataverse:', err);
        return { summary: null, tldr: null };
      }
    },
    [context.webAPI]
  );

  // Handle Open Viewer — opens DocumentRelationshipViewer via navigateTo
  const handleOpenViewer = useCallback(() => {
    // Use the first result's documentId (most recent) or fall back to scopeId
    const targetDocId = results.length > 0 ? results[0].documentId : null;
    if (!targetDocId) return;

    try {
      const clientUrl =
        (context as unknown as { page?: { getClientUrl?: () => string } }).page?.getClientUrl?.() ??
        window.location.origin;
      const theme = isDarkMode ? 'dark' : 'light';
      const tenantId = resolvedApiBaseUrl ? '' : ''; // tenantId resolved from auth
      let tid = '';
      try { tid = resolveTenantIdSync(); } catch { /* */ }

      const data = `documentId=${targetDocId}&tenantId=${encodeURIComponent(tid)}&theme=${theme}`;

      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm = (window as any).Xrm ?? (window.parent as any)?.Xrm;
      if (xrm?.Navigation?.navigateTo) {
        void xrm.Navigation.navigateTo(
          { pageType: 'webresource', webresourceName: 'sprk_documentrelationshipviewer', data },
          { target: 2, width: { value: 85, unit: '%' }, height: { value: 85, unit: '%' } }
        );
      }
    } catch (err) {
      console.error('[SemanticSearchControl] Failed to open viewer:', err);
    }
  }, [results, context, isDarkMode, resolvedApiBaseUrl]);

  // Handle Add Document — opens DocumentUploadWizard Code Page dialog.
  // After upload completes, always re-run the search (empty query returns all
  // documents in scope) so newly uploaded files appear even when the user is
  // in the initial state and hasn't typed a query.
  const handleAddDocument = useCallback(() => {
    void navigationService.openAddDocument(scopeId, searchScope !== 'all' ? searchScope : null, () => {
      void search(query || '', filters);
    });
  }, [navigationService, scopeId, searchScope, query, search, filters]);

  // Handle Email Documents (multi) — opens the DocumentEmailWizard with all
  // currently rendered documents. Step 1 of the wizard lets the user deselect
  // any they don't want before continuing to compose.
  const handleEmailDocuments = useCallback(() => {
    setEmailWizardOpen(true);
  }, []);

  // Adapter from PCF context.webAPI to the shared IDataService contract.
  // Built once via useMemo so its identity is stable across renders.
  const dataService: IDataService = useMemo(
    () => ({
      createRecord: (entityName, data) =>
        context.webAPI
          .createRecord(entityName, data as ComponentFramework.WebApi.Entity)
          .then(r => r.id),
      retrieveRecord: (entityName, id, options) =>
        context.webAPI.retrieveRecord(entityName, id, options ?? '') as Promise<Record<string, unknown>>,
      retrieveMultipleRecords: (entityName, options) =>
        context.webAPI
          .retrieveMultipleRecords(entityName, options ?? '')
          .then(r => ({ entities: r.entities as Record<string, unknown>[] })),
      updateRecord: (entityName, id, data) =>
        context.webAPI
          .updateRecord(entityName, id, data as ComponentFramework.WebApi.Entity)
          .then(() => undefined),
      deleteRecord: (entityName, id) =>
        context.webAPI.deleteRecord(entityName, id).then(() => undefined),
    }),
    [context.webAPI]
  );

  // Map current results into the wizard's lightweight item shape.
  // driveId + itemId are required to run AI analysis (Document Profile
  // playbook) in the wizard's Summary step — without them the wizard
  // falls back to the cached summary/tldr text.
  //
  // v1.1.63 — renamed from `emailWizardItems` → `emailWizardItemsAll`.
  // This is now the UNFILTERED mapping of every result row. The
  // `emailWizardItemsSelected` memo below filters to only the
  // user-checked rows, and the wizard receives that subset for the
  // bulk Email path (UAT round 17 — "Bulk Email should only load the
  // docs I selected, not the entire result set"). The full mapping
  // is still computed once so the selection filter is O(n) over a
  // stable identity-array.
  //
  // fileSizeBytes is intentionally omitted here — the BFF
  // SearchResult contract doesn't currently expose file size. See
  // v1.1.63 commit notes: a BFF SearchResult.fileSize addition is
  // queued as v1.1.64 + BFF redeploy to enable the > 25 MB warning
  // banner in the DocumentEmailWizard. Without it, the wizard
  // reports "size unknown" and the warning never triggers (which is
  // current behavior — no regression).
  const emailWizardItemsAll: IDocumentEmailWizardItem[] = useMemo(
    () =>
      results.map(r => ({
        documentId: r.documentId ?? '',
        name: r.name ?? '(untitled)',
        summary: r.summary ?? undefined,
        tldr: r.tldr ?? undefined,
        driveId: r.driveId ?? undefined,
        itemId: r.speFileId ?? undefined,
      })),
    [results]
  );

  // v1.1.63 — bulk Email subset: only items the user has checked.
  // Drives the wizard `selectedDocuments` prop when the wizard is
  // launched via the bulk-toolbar Email button (singleDocForWizard
  // is null). The single-doc path bypasses this memo entirely —
  // `handleEmailDocument` populates `singleDocForWizard` with the
  // one targeted row and the wizard receives `[it]`.
  const emailWizardItemsSelected: IDocumentEmailWizardItem[] = useMemo(
    () => emailWizardItemsAll.filter(i => selectedIds.has(i.documentId)),
    [emailWizardItemsAll, selectedIds]
  );

  // Handle Email Document — opens DocumentEmailWizard scoped to a single
  // document. Single-doc and bulk Email share the same wizard so the UX
  // is consistent (combined users+contacts picker, AI Summary step,
  // sprk_communication tracking, Attach Files + Send Document Links
  // toggles).
  //
  // v1.1.63 — preview stays visible behind the wizard for BOTH entry
  // points (row-menu Email AND preview-dialog Email). Previously
  // (v1.1.61) the preview-dialog path closed the preview via
  // `setHostPreviewDocId(null)`, so the wizard rendered against an
  // empty background; UAT flagged the inconsistent feel between the
  // two paths. The wizard is a Fluent v9 Dialog and renders as a
  // modal-over-modal on top of the FilePreviewDialog without conflict
  // (same pattern that already worked for row-menu Email + open preview
  // simultaneously). On wizard close (Cancel, X, or successful Send),
  // the user lands back on the preview at the same docId, which is the
  // natural mental-model continuation.
  const handleEmailDocument = useCallback(
    (result: SearchResult) => {
      // No setHostPreviewDocId(null) — preview stays open behind wizard.
      setSingleDocForWizard({
        documentId: result.documentId ?? '',
        name: result.name ?? '(untitled)',
        summary: result.summary ?? undefined,
        tldr: result.tldr ?? undefined,
        driveId: result.driveId ?? undefined,
        itemId: result.speFileId ?? undefined,
      });
      setEmailWizardOpen(true);
    },
    []
  );

  // Handle Copy Link — copies Dataverse record URL to clipboard
  const handleCopyLink = useCallback(
    (result: SearchResult) => {
      try {
        const clientUrl =
          (context as unknown as { page?: { getClientUrl?: () => string } }).page?.getClientUrl?.() ??
          window.location.origin;
        const recordUrl = `${clientUrl}/main.aspx?etn=sprk_document&id=${result.documentId}&pagetype=entityrecord`;
        void navigator.clipboard.writeText(recordUrl);
      } catch (err) {
        console.error('[SemanticSearchControl] Failed to copy link:', err);
      }
    },
    [context]
  );

  // Workspace flag tracking — local set of document IDs marked as "in workspace"
  const [workspaceSet, setWorkspaceSet] = useState<Set<string>>(new Set());

  // Load initial workspace flags from Dataverse when search results change
  useEffect(() => {
    if (results.length === 0) return;

    const documentIds = results.map(r => r.documentId);
    // Build OData filter: sprk_documentid eq 'id1' or sprk_documentid eq 'id2' ...
    const filterClauses = documentIds.map(id => `sprk_documentid eq '${id}'`);
    // Batch in groups of 50 to avoid overly long filter strings
    const batchSize = 50;
    const batches: string[][] = [];
    for (let i = 0; i < filterClauses.length; i += batchSize) {
      batches.push(filterClauses.slice(i, i + batchSize));
    }

    const loadWorkspaceFlags = async () => {
      const inWorkspaceIds = new Set<string>();
      for (const batch of batches) {
        try {
          const filter = batch.join(' or ');
          const resp = await context.webAPI.retrieveMultipleRecords(
            'sprk_document',
            `?$select=sprk_documentid,sprk_inworkspace&$filter=(${filter}) and sprk_inworkspace eq true`
          );
          for (const entity of resp.entities || []) {
            const docId = entity.sprk_documentid as string;
            if (docId) inWorkspaceIds.add(docId);
          }
        } catch (err) {
          console.warn('[SemanticSearchControl] Failed to load workspace flags:', err);
        }
      }
      setWorkspaceSet(inWorkspaceIds);
    };

    void loadWorkspaceFlags();
  }, [results, context.webAPI]);

  // Handle Toggle Workspace — toggles the sprk_inworkspace flag on the document.
  // Uses functional setState to avoid stale-closure issues with workspaceSet.
  const handleToggleWorkspace = useCallback(
    (result: SearchResult) => {
      setWorkspaceSet(prev => {
        const isCurrentlyIn = prev.has(result.documentId);
        const newFlag = !isCurrentlyIn;
        const next = new Set(prev);
        if (newFlag) {
          next.add(result.documentId);
        } else {
          next.delete(result.documentId);
        }
        // Update Dataverse (fire-and-forget with revert on failure)
        context.webAPI
          .updateRecord('sprk_document', result.documentId, {
            sprk_inworkspace: newFlag,
          })
          .catch((err: unknown) => {
            console.error('[SemanticSearchControl] Failed to toggle workspace flag:', err);
            // Revert on failure
            setWorkspaceSet(revert => {
              const reverted = new Set(revert);
              if (isCurrentlyIn) {
                reverted.add(result.documentId);
              } else {
                reverted.delete(result.documentId);
              }
              return reverted;
            });
          });
        return next;
      });
    },
    [context.webAPI]
  );

  // Check if a document is in the workspace
  const isInWorkspace = useCallback((result: SearchResult) => workspaceSet.has(result.documentId), [workspaceSet]);

  // Load file type options for the command-bar File Type filter. Document type
  // options now flow through the dedicated Tags filter (`tagOptions` state above)
  // rather than the FilterPanel sidebar (FR-DOC-05).
  const { fileTypeOptions, isLoading: filterOptionsLoading } = useFilterOptions();

  // FR-DOC-05: apply client-side OR-filter for selected tags AFTER the backend
  // returns. Tags drive a client-side filter (not a BFF query parameter) because
  // the existing /api/ai/search endpoint does not yet accept a documentType OR
  // filter list — the sidebar's documentTypes filter still flows via filters.documentTypes
  // for server-side AND behavior. The Tags filter here is the new UI surface
  // requested by FR-DOC-05 and intentionally implements OR semantics client-side.
  //
  // The BFF still handles associatedOnly + threshold server-side; tags are layered on top.

  // FR-DOC-02: optimistic doc-type overrides — applied on top of `results` so
  // the bulk Document Type → selected action reflects the user's choice
  // immediately, before the Xrm.WebApi updates complete. Cleared on Undo or
  // when the backend confirms success (no-op clear since the optimistic value
  // matches the eventual server state). Map of documentId → newDocumentType.
  const [docTypeOverrides, setDocTypeOverrides] = useState<Record<string, string>>({});

  const filteredResults = useMemo(() => {
    // Apply optimistic doc-type overrides first so the tag filter (below)
    // sees the post-edit values — otherwise an in-flight bulk doc-type change
    // could hide rows from the user mid-update.
    const overriddenResults = Object.keys(docTypeOverrides).length === 0
      ? results
      : results.map(r => {
          const override = docTypeOverrides[r.documentId];
          return override !== undefined ? { ...r, documentType: override } : r;
        });
    if (selectedTags.length === 0) return overriddenResults;
    return overriddenResults.filter(r => selectedTags.includes(r.documentType));
  }, [results, selectedTags, docTypeOverrides]);
  // For footer count UX: totalCount = unfiltered server-side total; filteredResults.length = post-tag-filter.
  const filteredTotalCount = totalCount;

  // ─────────────────────────────────────────────────────────────────────────
  // FR-DOC-02: Toaster wiring + Bulk action handlers
  // FR-DOC-07: Telemetry instrumentation for menu actions + preview dialog
  //
  // The Toaster is mounted at the PCF root (inside the FluentProvider in
  // index.ts) so portal-rendered toasts inherit the surface theme correctly
  // (`.claude/patterns/ui/fluent-v9-portal-gotcha.md`). Action handlers below
  // call `dispatchToast` from the parent's controller.
  // ─────────────────────────────────────────────────────────────────────────

  const toasterId = useId('semantic-search-toaster');
  const { dispatchToast } = useToastController(toasterId);

  const TOAST_DEFAULT_MS = 5000;

  // Telemetry-instrumented wrappers for the per-row menu actions. These are
  // passed to ListView/ResultCard/FilePreviewDialog in place of the raw
  // handlers — every menu invocation emits `three_dot_menu_action_invoked`.
  const handlePreviewTelemetry = useCallback(
    async (result: SearchResult): Promise<string | null> => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'preview' });
      AppInsightsService.trackEvent('preview_dialog_opened');
      return handlePreview(result);
    },
    [handlePreview]
  );

  const handleOpenFileTelemetry = useCallback(
    (result: SearchResult, mode: 'web' | 'desktop') => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'open_file' });
      handleOpenFile(result, mode);
    },
    [handleOpenFile]
  );

  const handleOpenRecordTelemetry = useCallback(
    (result: SearchResult, inModal: boolean) => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'open_record' });
      handleOpenRecord(result, inModal);
    },
    [handleOpenRecord]
  );

  const handleFindSimilarTelemetry = useCallback(
    (result: SearchResult) => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'find_similar' });
      handleFindSimilar(result);
    },
    [handleFindSimilar]
  );

  const handleEmailDocumentTelemetry = useCallback(
    (result: SearchResult) => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'email' });
      handleEmailDocument(result);
    },
    [handleEmailDocument]
  );

  const handleCopyLinkTelemetry = useCallback(
    (result: SearchResult) => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'copy_link' });
      handleCopyLink(result);
    },
    [handleCopyLink]
  );

  const handleToggleWorkspaceTelemetry = useCallback(
    (result: SearchResult) => {
      AppInsightsService.trackEvent('three_dot_menu_action_invoked', { action_name: 'toggle_workspace' });
      handleToggleWorkspace(result);
    },
    [handleToggleWorkspace]
  );

  // ── Bulk-action helpers ────────────────────────────────────────────────

  /** Single source of selected document objects (filtered to ids still in the
   *  results — defensive against id-stale state after re-search). */
  const selectedResults = useMemo(
    () => filteredResults.filter(r => selectedIds.has(r.documentId)),
    [filteredResults, selectedIds]
  );

  // ── v1.1.49 — Card-view selection helper (Item 1) ─────────────────────
  // Single-id toggle wrapper so each ResultCard can flip its own selection
  // through the parent-owned `selectedIds` set.
  const handleToggleCardSelect = useCallback(
    (documentId: string) => {
      setSelectedIds(prev => {
        const next = new Set(prev);
        if (next.has(documentId)) {
          next.delete(documentId);
        } else {
          next.add(documentId);
        }
        return next;
      });
    },
    []
  );

  // ── v1.1.49 — Host-level preview navigation set (Item 6) ───────────────
  // Mirrors ListView's previewNavigationSet logic so list AND card views
  // share the same Prev/Next nav semantics. Selection wins; otherwise
  // walk the full filteredResults.
  const hostPreviewNavSet = useMemo<SearchResult[]>(() => {
    if (selectedIds.size > 0) {
      return filteredResults.filter(r => selectedIds.has(r.documentId));
    }
    return filteredResults;
  }, [selectedIds, filteredResults]);

  const hostPreviewTarget = useMemo<SearchResult | null>(() => {
    if (!hostPreviewDocId) return null;
    return (
      hostPreviewNavSet.find(r => r.documentId === hostPreviewDocId) ??
      filteredResults.find(r => r.documentId === hostPreviewDocId) ??
      null
    );
  }, [hostPreviewDocId, hostPreviewNavSet, filteredResults]);

  const hostPreviewIndex = useMemo<number>(() => {
    if (!hostPreviewDocId) return -1;
    return hostPreviewNavSet.findIndex(r => r.documentId === hostPreviewDocId);
  }, [hostPreviewDocId, hostPreviewNavSet]);

  const handleHostPreviewNavigate = useCallback(
    (nextIndex: number) => {
      if (nextIndex < 0 || nextIndex >= hostPreviewNavSet.length) return;
      setHostPreviewDocId(hostPreviewNavSet[nextIndex].documentId);
    },
    [hostPreviewNavSet]
  );

  // Card-view preview-open handler — fired by ResultCard via the new
  // `onOpenPreview` prop (Item 6).
  const handleOpenHostPreview = useCallback((result: SearchResult) => {
    setHostPreviewDocId(result.documentId);
    AppInsightsService.trackEvent('preview_dialog_opened', { source: 'card' });
  }, []);

  // Toast dispatch helpers — small wrappers so handlers below stay readable.
  const showToast = useCallback(
    (
      title: string,
      body: string,
      intent: 'success' | 'info' | 'warning' | 'error',
      timeout: number = TOAST_DEFAULT_MS,
      action?: { label: string; onClick: () => void }
    ) => {
      dispatchToast(
        <Toast>
          <ToastTitle
            action={
              action
                ? (
                  <Button appearance="transparent" size="small" onClick={action.onClick}>
                    {action.label}
                  </Button>
                )
                : undefined
            }
          >
            {title}
          </ToastTitle>
          <ToastBody>{body}</ToastBody>
        </Toast>,
        { intent, timeout }
      );
    },
    [dispatchToast]
  );

  // 1. Email selected — open the existing multi-doc email wizard. The wizard
  //    handles the SPE attachment / zip pipeline; we just open it scoped to
  //    the selected subset.
  const handleBulkEmail = useCallback(() => {
    AppInsightsService.trackEvent('bulk_action_invoked', {
      action_name: 'email',
      selection_count: selectedIds.size,
    });
    setEmailWizardOpen(true);
  }, [selectedIds.size]);

  // 2. Download selected — POST /api/documents/bulk-download via authenticatedFetch.
  const handleBulkDownload = useCallback(async (): Promise<void> => {
    const ids = Array.from(selectedIds);
    AppInsightsService.trackEvent('bulk_action_invoked', {
      action_name: 'download',
      selection_count: ids.length,
    });
    try {
      const response = await authenticatedFetch(`${apiBaseUrl}/api/documents/bulk-download`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ documentIds: ids }),
      });

      if (response.status === 413) {
        showToast(
          'Too many documents',
          'Maximum 500 documents per bulk download.',
          'error'
        );
        return;
      }

      if (!response.ok) {
        // Surface BFF ProblemDetails 4xx (404 "no accessible documents", 403, 401).
        const detail = response.status === 404
          ? 'No accessible documents in the current selection.'
          : `Download failed (${response.status}).`;
        showToast('Download failed', detail, 'error', TOAST_DEFAULT_MS, {
          label: 'Retry',
          onClick: () => {
            void handleBulkDownload();
          },
        });
        return;
      }

      // Parse Content-Disposition to recover the server-generated filename
      // (`documents-{matterIdOrBulk}-{timestamp}.zip`).
      const cd = response.headers.get('content-disposition') ?? '';
      const match = cd.match(/filename="?([^";]+)"?/i);
      const filename = match ? match[1] : `documents-${Date.now()}.zip`;

      const blob = await response.blob();
      const objectUrl = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(objectUrl);

      showToast(
        'Download started',
        `${ids.length} document${ids.length !== 1 ? 's' : ''} downloaded as a zip.`,
        'success'
      );
    } catch (err) {
      // Network or other unrecoverable error — show a retry toast.
      const message = err instanceof Error ? err.message : 'Unknown error';
      showToast('Download failed', message, 'error', TOAST_DEFAULT_MS, {
        label: 'Retry',
        onClick: () => {
          void handleBulkDownload();
        },
      });
      throw err;
    }
  }, [apiBaseUrl, selectedIds, showToast]);

  // 3. Pin selected — for each id, call togglePin only when NOT currently pinned
  //    (idempotent: pinning an already-pinned doc would unpin it). Writes to
  //    localStorage per useDocumentListPrefs.
  const handleBulkPin = useCallback(() => {
    const ids = Array.from(selectedIds);
    AppInsightsService.trackEvent('bulk_action_invoked', {
      action_name: 'pin',
      selection_count: ids.length,
    });
    let pinnedCount = 0;
    for (const id of ids) {
      if (!pinnedIds.has(id)) {
        togglePin(id);
        pinnedCount += 1;
      }
    }
    showToast(
      'Pinned',
      pinnedCount > 0
        ? `${pinnedCount} document${pinnedCount !== 1 ? 's' : ''} pinned to top.`
        : 'Selected documents were already pinned.',
      'success'
    );
  }, [pinnedIds, selectedIds, togglePin, showToast]);

  // 4. Delete selected — soft-delete via Xrm.WebApi. Confirmation Dialog lives
  //    INSIDE BulkActionBar; this handler is invoked AFTER confirmation.
  const handleBulkDelete = useCallback(async (): Promise<void> => {
    const ids = Array.from(selectedIds);
    AppInsightsService.trackEvent('bulk_action_invoked', {
      action_name: 'delete',
      selection_count: ids.length,
    });
    const failures: string[] = [];
    await Promise.all(
      ids.map(async id => {
        try {
          await context.webAPI.deleteRecord('sprk_document', id);
        } catch {
          failures.push(id);
        }
      })
    );

    if (failures.length === 0) {
      showToast(
        'Deleted',
        `${ids.length} document${ids.length !== 1 ? 's' : ''} deleted.`,
        'success'
      );
      setSelectedIds(new Set());
      // Refresh the result set so deleted rows disappear.
      void search(queryInput, filters);
    } else if (failures.length < ids.length) {
      showToast(
        'Partial deletion',
        `${ids.length - failures.length} deleted; ${failures.length} could not be deleted.`,
        'warning'
      );
      // Drop the successes from the selection so the user can retry the failures.
      const stillFailing = new Set(failures);
      setSelectedIds(stillFailing);
      void search(queryInput, filters);
    } else {
      showToast(
        'Delete failed',
        `Could not delete ${failures.length} document${failures.length !== 1 ? 's' : ''}.`,
        'error',
        TOAST_DEFAULT_MS,
        {
          label: 'Retry',
          onClick: () => {
            void handleBulkDelete();
          },
        }
      );
    }
  }, [context.webAPI, filters, queryInput, search, selectedIds, showToast]);

  // 5. Document Type → selected — optimistic UI + 5s Undo toast.
  //    On apply: stash previous types, write override map immediately, fire
  //    Xrm.WebApi updates in parallel. On success: show Undo toast that
  //    reverts the override map + writes the original types back to
  //    Dataverse. On any failure: revert override map + show error toast.
  const handleBulkDocTypeChange = useCallback(
    (newType: string) => {
      const ids = Array.from(selectedIds);
      AppInsightsService.trackEvent('bulk_action_invoked', {
        action_name: 'doc_type',
        selection_count: ids.length,
      });
      // Capture originals BEFORE applying the optimistic override.
      const originals: Record<string, string> = {};
      for (const id of ids) {
        const r = filteredResults.find(x => x.documentId === id);
        originals[id] = r?.documentType ?? '';
      }

      // Apply optimistic override.
      setDocTypeOverrides(prev => {
        const next = { ...prev };
        for (const id of ids) next[id] = newType;
        return next;
      });

      // Fire Xrm.WebApi updates in parallel.
      const updatePromise = Promise.all(
        ids.map(id =>
          context.webAPI.updateRecord('sprk_document', id, {
            sprk_documenttype: newType,
          })
        )
      );

      void updatePromise
        .then(() => {
          // Success — show 5s Undo toast.
          showToast(
            'Document type updated',
            `${ids.length} document${ids.length !== 1 ? 's' : ''} set to "${newType}".`,
            'success',
            TOAST_DEFAULT_MS,
            {
              label: 'Undo',
              onClick: () => {
                // Revert the optimistic override.
                setDocTypeOverrides(prev => {
                  const next = { ...prev };
                  for (const id of ids) delete next[id];
                  return next;
                });
                // Bulk update Dataverse back to original values.
                void Promise.all(
                  ids.map(id =>
                    context.webAPI.updateRecord('sprk_document', id, {
                      sprk_documenttype: originals[id] ?? null,
                    })
                  )
                )
                  .then(() => {
                    showToast(
                      'Undo applied',
                      'Document types reverted.',
                      'info'
                    );
                    void search(queryInput, filters);
                  })
                  .catch(() => {
                    showToast(
                      'Undo failed',
                      'Could not revert document types — please try again.',
                      'error'
                    );
                  });
              },
            }
          );
          // After the toast window, schedule a quiet refresh so the backend
          // value re-takes over the optimistic override.
          window.setTimeout(() => {
            setDocTypeOverrides(prev => {
              const next = { ...prev };
              for (const id of ids) delete next[id];
              return next;
            });
            void search(queryInput, filters);
          }, TOAST_DEFAULT_MS + 250);
        })
        .catch(() => {
          // Failure — revert override + show error toast with Retry.
          setDocTypeOverrides(prev => {
            const next = { ...prev };
            for (const id of ids) delete next[id];
            return next;
          });
          showToast(
            'Update failed',
            `Could not set document type for ${ids.length} document${ids.length !== 1 ? 's' : ''}.`,
            'error',
            TOAST_DEFAULT_MS,
            {
              label: 'Retry',
              onClick: () => {
                handleBulkDocTypeChange(newType);
              },
            }
          );
        });
    },
    [context.webAPI, filteredResults, filters, queryInput, search, selectedIds, showToast]
  );

  // 6. Share link — open mailto: composer pre-populated with one
  //    "{DocName} → {DataverseRecordURL}" line per selected doc. NOT SPE
  //    files — Dataverse record URLs per spec FR-DOC-02 + Owner Clarifications.
  const handleBulkShareLink = useCallback(() => {
    const items = selectedResults;
    AppInsightsService.trackEvent('bulk_action_invoked', {
      action_name: 'share_link',
      selection_count: items.length,
    });
    if (items.length === 0) return;
    let clientUrl: string;
    try {
      clientUrl =
        (context as unknown as { page?: { getClientUrl?: () => string } }).page?.getClientUrl?.() ??
        window.location.origin;
    } catch {
      clientUrl = window.location.origin;
    }
    const lines = items.map(r => {
      const url = `${clientUrl}/main.aspx?etn=sprk_document&id=${r.documentId}&pagetype=entityrecord`;
      return `${r.name ?? '(untitled)'} → ${url}`;
    });
    const body = encodeURIComponent(
      `Sharing ${items.length} document${items.length !== 1 ? 's' : ''}:\n\n` +
        lines.join('\n')
    );
    const subject = encodeURIComponent(
      items.length === 1
        ? `Document link: ${items[0].name ?? 'document'}`
        : `${items.length} document links`
    );
    const href = `mailto:?subject=${subject}&body=${body}`;
    try {
      window.location.href = href;
    } catch {
      showToast(
        'Share link failed',
        'Could not open the email composer.',
        'error'
      );
    }
  }, [context, selectedResults, showToast]);

  // Clear handler for the bulk-action bar.
  const handleBulkClear = useCallback(() => {
    setSelectedIds(new Set());
  }, []);

  const renderMainContent = () => {
    // Auth initializing state
    if (!isAuthInitialized) {
      return (
        <div className={styles.centeredState}>
          <LoadingState count={compactMode ? 3 : 5} />
        </div>
      );
    }

    // Initial loading state (skeleton)
    if (isLoading && results.length === 0) {
      return (
        <div className={styles.centeredState}>
          <LoadingState count={compactMode ? 3 : 5} />
        </div>
      );
    }

    // Error state
    if (error) {
      return (
        <div className={styles.centeredState}>
          <ErrorState message={error.message} retryable={error.retryable} onRetry={handleRetry} />
        </div>
      );
    }

    // Empty state (after search with no results)
    if (hasSearched && results.length === 0 && !isLoading) {
      return (
        <>
          {renderActionsToolbar()}
          <div className={styles.centeredState}>
            <EmptyState query={query} hasFilters={hasActiveFilters} />
          </div>
        </>
      );
    }

    // Results — list view (FR-DOC-04 default) or card view based on persisted pref.
    // v1.1.47 — `effectiveView` honors the host's `showViewToggle` + `defaultView`
    // settings (locks the surface to a single view when the toggle is hidden).
    if (filteredResults.length > 0) {
      if (effectiveView === 'list' && !compactMode) {
        // v1.1.45 — single toolbar row hosts the bulk-action icon group
        // INLINE alongside Reload + Add. The BulkActionBar component is
        // rendered inside renderActionsToolbar() and returns null when
        // nothing is selected, so the row collapses to Reload + Add at
        // zero selection.
        return (
          <>
            {renderActionsToolbar()}
            <ListView
              results={filteredResults}
              selectedIds={selectedIds}
              onSelectionChange={setSelectedIds}
              pinnedIds={pinnedIds}
              onTogglePin={togglePin}
              sortColumn={sortColumn}
              sortDirection={sortDirection}
              onSortChange={handleSortChange}
              columnWidths={columnWidths}
              onColumnWidthChange={setColumnWidth}
              onOpenFile={handleOpenFileTelemetry}
              onOpenRecord={handleOpenRecordTelemetry}
              onFindSimilar={handleFindSimilarTelemetry}
              onPreview={handlePreviewTelemetry}
              onSummary={handleSummary}
              onEmailDocument={handleEmailDocumentTelemetry}
              onCopyLink={handleCopyLinkTelemetry}
              onToggleWorkspace={handleToggleWorkspaceTelemetry}
              isInWorkspace={isInWorkspace}
              // v1.1.50 (Item 2) — Route list-view preview through the
              // SAME host-mounted FilePreviewDialog as the card view so
              // Prev/Next nav set is shared across views.
              onOpenPreview={setHostPreviewDocId}
              // v1.1.50 (Item 1) — Lazy-load sentinel parity with the
              // card view. Same gating as ResultsList: only attach when
              // there's more to load AND we're not already loading.
              onLoadMoreSentinel={
                !filters.associatedOnly && hasMore && !isLoadingMore
                  ? loadMore
                  : undefined
              }
            />
          </>
        );
      }
      return (
        <>
          {/* Card view also surfaces the toolbar (which contains the
              BulkActionBar when ≥1 row is selected). Selection state
              persists across view toggles per FR-DOC-04 Owner
              Clarification. ResultsList still owns the inner header on
              the card surface for its own per-card affordances. */}
          {renderActionsToolbar()}
          <ResultsList
            results={filteredResults}
            isLoading={isLoading}
            isLoadingMore={isLoadingMore}
            hasMore={!filters.associatedOnly && hasMore}
            totalCount={filteredTotalCount}
            threshold={filters.threshold}
            onLoadMore={loadMore}
            onResultClick={handleResultClick}
            onOpenFile={handleOpenFileTelemetry}
            onOpenRecord={handleOpenRecordTelemetry}
            onFindSimilar={handleFindSimilarTelemetry}
            onPreview={handlePreviewTelemetry}
            onSummary={handleSummary}
            onEmailDocument={handleEmailDocumentTelemetry}
            onCopyLink={handleCopyLinkTelemetry}
            onToggleWorkspace={handleToggleWorkspaceTelemetry}
            isInWorkspace={isInWorkspace}
            onViewAll={handleViewAll}
            onReload={handleReload}
            onAddDocument={handleAddDocument}
            onOpenViewer={handleOpenViewer}
            onEmailDocuments={selectedIds.size > 0 ? handleEmailDocuments : undefined}
            // v1.1.49 — Item 1: card selection wiring (host-owned).
            selectedIds={selectedIds}
            onToggleSelect={handleToggleCardSelect}
            // v1.1.49 — Item 6: card preview routes through the SAME host-mounted
            // FilePreviewDialog as the list view, so Prev/Next nav set is shared.
            onOpenPreview={handleOpenHostPreview}
            // v1.1.49 — Item 2: the host renders the consolidated single-row
            // toolbar above ResultsList; suppress the duplicate inner toolbar.
            hideToolbar
            // v1.1.49 — Item 9: lazy-load sentinel — fires loadMore when the
            // bottom of the card grid enters the viewport.
            onLoadMoreSentinel={!filters.associatedOnly && hasMore && !isLoadingMore ? loadMore : undefined}
            compactMode={compactMode}
          />
        </>
      );
    }

    // Initial state (before first search) — show the same actions toolbar so
    // users on a record with no documents can upload without needing to search.
    return (
      <>
        {renderActionsToolbar()}
        <div className={styles.centeredState}>
          <Text>Enter a search query to find documents</Text>
        </div>
      </>
    );
  };

  // Renders the actions toolbar (selection · bulk actions · refresh · +)
  // on a single row above the list/card surface. v1.1.45 folds the bulk-
  // action affordances into this same row (UAT request — no separate
  // sticky bulk bar). When zero rows are selected the row shows only
  // Reload + Add Document; once any row is checked the bulk-action group
  // appears at the leading edge of the row.
  const renderActionsToolbar = () => (
    <div className={styles.emptyStateToolbar}>
      {/* Bulk-action group (icon-only) — renders only when ≥1 row selected.
          Internally returns null at selectionCount === 0 so we can safely
          mount it unconditionally; the leading-edge position is by design
          (mockup: "[N selected] [bulk icons] ··· [refresh] [+]"). */}
      <BulkActionBar
        selectedIds={selectedIds}
        docTypeOptions={tagOptions}
        onClear={handleBulkClear}
        onEmail={handleBulkEmail}
        onDownload={handleBulkDownload}
        onPin={handleBulkPin}
        onDelete={handleBulkDelete}
        onDocTypeChange={handleBulkDocTypeChange}
        onShareLink={handleBulkShareLink}
      />
      {/* Spacer pushes Reload/Add to the trailing edge */}
      <span style={{ flex: 1 }} aria-hidden="true" />
      <Tooltip content="Reload results" relationship="label">
        <Button
          className={styles.emptyStateToolbarButton}
          appearance="subtle"
          size="small"
          icon={<ArrowClockwise20Regular />}
          aria-label="Reload results"
          onClick={handleReload}
        />
      </Tooltip>
      {handleAddDocument && (
        <Tooltip content="Add Document" relationship="label">
          <Button
            className={styles.emptyStateToolbarButton}
            appearance="subtle"
            size="small"
            icon={<Add20Regular />}
            aria-label="Add Document"
            onClick={handleAddDocument}
          />
        </Tooltip>
      )}
      {/* v1.1.49 — UAT Items 2 & 5: "Open full viewer" button (Document
          Relationship Viewer) is rendered in BOTH list and card view
          toolbars so users always have a single, consistent path to the
          full viewer. The button is suppressed only when no document is
          available (results empty AND no first-doc fallback) so the
          underlying handler doesn't open the viewer with no docId — the
          legacy "single-document-centric requires-docId" guard. */}
      {results.length > 0 && (
        <Tooltip content="Open full viewer" relationship="label">
          <Button
            className={styles.emptyStateToolbarButton}
            appearance="subtle"
            size="small"
            icon={<Open20Regular />}
            aria-label="Open full viewer"
            onClick={handleOpenViewer}
          />
        </Tooltip>
      )}
    </div>
  );

  // Combine root styles based on mode
  const rootClassName = compactMode ? `${styles.root} ${styles.rootCompact}` : styles.root;

  return (
    <div className={rootClassName}>
      {/* Header Region: Search Input + Document Count */}
      <div className={styles.header}>
        <SearchInput
          value={queryInput}
          placeholder={placeholder}
          disabled={isLoading}
          onValueChange={setQueryInput}
          onSearch={handleSearch}
        />
        {hasSearched && !isLoading && totalCount > 0 && (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3, marginTop: '4px' }}>
            {filters.associatedOnly
              ? `${filteredResults?.length ?? 0} associated document${(filteredResults?.length ?? 0) !== 1 ? 's' : ''}`
              : `${totalCount} document${totalCount !== 1 ? 's' : ''} found`}
          </Text>
        )}
      </div>

      {/* Command Bar Region (FR-DOC-04 + FR-DOC-05 + FR-DOC-06) — the sidebar
          FilterPanel is removed in v1. The command bar hosts every filter
          (Associated Only / File Type / Date Range / Threshold / Mode / Tags)
          plus the list/card view toggle. The auto-search useEffect on
          `filters.associatedOnly` is unchanged (the binding constraint from
          spec FR-DOC-06).
          v1.1.45: the `showFilters` PCF property is intentionally NOT
          honored as a gate any more — it was a v0 legacy flag for the old
          sidebar `FilterPanel`. UAT confirmed that the command bar must
          always render in non-compact mode (otherwise the AssociatedOnly
          switch and the five filter dropdowns are invisible, which is what
          the user reported). The `showFilters` property remains in the
          manifest for backward compat but only affects nothing here. */}
      {!compactMode && (
        <CommandBar
          filters={filters}
          onFiltersChange={handleFiltersChange}
          showAssociatedOnly={searchScope !== 'all' && searchScope !== 'custom'}
          fileTypeOptions={fileTypeOptions}
          tagOptions={tagOptions}
          optionsLoading={filterOptionsLoading}
          selectedTags={selectedTags}
          onSelectedTagsChange={handleSelectedTagsChange}
          onClearFilters={handleClearFilters}
          view={effectiveView}
          onViewChange={handleViewChange}
          showViewToggle={showViewToggle}
          disabled={isLoading}
        />
      )}

      {/* Content Region: Main (single column now that the sidebar is gone) */}
      <div className={styles.content}>
        <div className={styles.main}>{renderMainContent()}</div>
      </div>

      {/* FR-DOC-05 footer count — appears when ≥1 tag selected, communicates
          filtered vs total count. When no tags selected, omitted (the in-list
          ResultsList header already shows totals). */}
      {hasSearched && !isLoading && selectedTags.length > 0 && (
        <div className={styles.footerCount}>
          <Text size={200}>
            Showing {filteredResults.length} of {filteredTotalCount} documents · filtered by{' '}
            {selectedTags.length} tag{selectedTags.length !== 1 ? 's' : ''}
          </Text>
        </div>
      )}

      {/* Footer: View All link (compact mode only) */}
      {compactMode && results.length > 0 && (
        <div className={styles.footer}>
          <Link onClick={handleViewAll}>View all {totalCount} results →</Link>
        </div>
      )}

      {/* Version Footer (always visible) */}
      <div className={styles.versionFooter}>
        <Text size={100}>v1.1.68 • Built 2026-05-29</Text>
      </div>

      {/* Host-mounted preview dialog. Single instance per PCF surface so
          list AND card views share the navigation set. The callbacks all
          close over the CURRENT target — when the user clicks Next,
          `hostPreviewDocId` flips and the closures rebind on the next
          render.

          v1.1.49 (Item 6) — card view routed through this dialog.
          v1.1.50 (Item 2) — list view also routed through this dialog
          via the new `ListView.onOpenPreview` prop. ListView no longer
          mounts its own FilePreviewDialog when wired, so Prev/Next nav
          set is shared across list and card views. */}
      {hostPreviewTarget && (
        <FilePreviewDialog
          open={!!hostPreviewTarget}
          documentName={hostPreviewTarget.name}
          documentId={hostPreviewTarget.documentId}
          documentType={hostPreviewTarget.documentType}
          createdAt={hostPreviewTarget.createdAt}
          createdBy={hostPreviewTarget.createdBy}
          onClose={() => setHostPreviewDocId(null)}
          fetchPreviewUrl={() => handlePreviewTelemetry(hostPreviewTarget)}
          onFetchSummary={() => handleSummary(hostPreviewTarget)}
          onOpenFile={mode => handleOpenFileTelemetry(hostPreviewTarget, mode)}
          onOpenRecord={() => handleOpenRecordTelemetry(hostPreviewTarget, false)}
          onEmailDocument={() => handleEmailDocumentTelemetry(hostPreviewTarget)}
          onCopyLink={() => handleCopyLinkTelemetry(hostPreviewTarget)}
          onToggleWorkspace={() => handleToggleWorkspaceTelemetry(hostPreviewTarget)}
          isInWorkspace={isInWorkspace(hostPreviewTarget)}
          onFindSimilar={() => handleFindSimilarTelemetry(hostPreviewTarget)}
          navigationTotal={hostPreviewNavSet.length}
          currentIndex={hostPreviewIndex >= 0 ? hostPreviewIndex : undefined}
          onNavigate={handleHostPreviewNavigate}
        />
      )}

      {/* Find Similar — shared iframe dialog */}
      <FindSimilarDialog open={!!findSimilarUrl} onClose={() => setFindSimilarUrl(null)} url={findSimilarUrl} />

      {/* Document Email Wizard — unified single-doc + bulk Email surface.
          When launched from a row's 3-dot menu, `handleEmailDocument`
          populates `singleDocForWizard` and the wizard receives `[it]`
          as its selected set. When launched from the bulk-toolbar
          Email icon, `singleDocForWizard` is null and the wizard
          receives `emailWizardItemsSelected` — the CHECKED docs only
          (v1.1.63, UAT round 17). Pre-v1.1.63 the wizard received the
          full result set and the user had to prune in step 1; the
          bulk-toolbar Email button is now gated on `selectedIds.size
          > 0` so launching with zero selection is impossible.
          v1.1.63 — `maxWidth='1280px'` + `height='85vh'` mirror the
          FilePreviewDialog so the wizard footprint matches when
          stacked over an open preview (Item 3 + Item 2 combine: the
          preview stays open behind the wizard for both row-menu and
          preview-launched single-doc Email). */}
      <DocumentEmailWizard
        open={emailWizardOpen}
        onClose={() => {
          setEmailWizardOpen(false);
          setSingleDocForWizard(null);
        }}
        selectedDocuments={singleDocForWizard ? [singleDocForWizard] : emailWizardItemsSelected}
        parentEntityType={searchScope === 'matter' ? 'sprk_matter'
          : searchScope === 'project' ? 'sprk_project'
          : searchScope === 'invoice' ? 'sprk_invoice'
          : undefined}
        parentEntityId={scopeId ?? undefined}
        authenticatedFetch={authenticatedFetch}
        bffBaseUrl={apiBaseUrl}
        dataService={dataService}
        // v1.1.63 — match the FilePreviewDialog footprint so the wizard
        // doesn't feel mismatched when stacked over an open preview.
        // FilePreviewDialog uses maxWidth='1280px' height='85vh' (see
        // SemanticSearchControl/components/FilePreviewDialog.tsx
        // styles.surface). Mirroring those values here removes the
        // visual size jump UAT flagged when the wizard launches with
        // the preview still open.
        maxWidth="1280px"
        height="85vh"
      />

      {/* Toaster — single instance per PCF surface (FR-DOC-02 bulk-action
          feedback + FR-DOC-07 success/error toasts). Portal-rendered;
          re-wraps theming via the FluentProvider mounted by control/index.ts
          (`.claude/patterns/ui/fluent-v9-portal-gotcha.md`). */}
      <Toaster toasterId={toasterId} position="top-end" />
    </div>
  );
};

export default SemanticSearchControl;
