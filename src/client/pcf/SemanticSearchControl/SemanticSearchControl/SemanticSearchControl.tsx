/**
 * SemanticSearchControl - Main component for semantic document search.
 *
 * Provides a three-region layout:
 * - Header: Search input with search button
 * - Sidebar: Filter panel (hidden in compact mode)
 * - Main: Search results list with infinite scroll
 *
 * @see ADR-021 for Fluent UI v9 and design token requirements
 */

import * as React from 'react';
import { useState, useCallback, useMemo, useEffect, useRef } from 'react';
import { makeStyles, tokens, shorthands, Text, Link, Button, Tooltip } from '@fluentui/react-components';
import { ChevronRight20Regular } from '@fluentui/react-icons';
import { ISemanticSearchControlProps, SearchFilters, SearchResult, SearchScope, SummaryData } from './types';
import { SearchInput, FilterPanel, ResultsList, LoadingState, EmptyState, ErrorState } from './components';
import { useSemanticSearch, useFilters } from './hooks';
import { SemanticSearchApiService, NavigationService } from './services';
import { authenticatedFetch, resolveTenantIdSync } from '@spaarke/auth';
import { initializeAuth } from './authInit';
import { getEnvironmentVariable, getApiBaseUrl } from '../../shared/utils/environmentVariables';
import { SendEmailDialog, type ISendEmailPayload } from '@spaarke/ui-components/dist/components/SendEmailDialog';
import { FindSimilarDialog } from '@spaarke/ui-components/dist/components/FindSimilarDialog';
import type { ILookupItem } from '@spaarke/ui-components/dist/types/LookupTypes';

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

  // Main content area (sidebar + results)
  content: {
    display: 'flex',
    flex: 1,
    ...shorthands.overflow('hidden'),
  },

  // Sidebar region (filters) - hidden in compact mode
  sidebar: {
    width: '250px',
    flexShrink: 0,
    boxSizing: 'border-box',
    ...shorthands.padding(tokens.spacingHorizontalS),
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRight(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
    overflowY: 'auto',
    overflowX: 'hidden',
  },

  // Collapsed sidebar strip
  sidebarCollapsed: {
    width: '36px',
    flexShrink: 0,
    display: 'flex',
    alignItems: 'flex-start',
    justifyContent: 'center',
    paddingTop: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground3,
    ...shorthands.borderRight(tokens.strokeWidthThin, 'solid', tokens.colorNeutralStroke1),
  },

  // Main region (results list)
  main: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
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

  // Get control properties from context
  const showFilters = context.parameters.showFilters?.raw ?? true;
  const compactMode = context.parameters.compactMode?.raw ?? false;
  const placeholder = context.parameters.placeholder?.raw ?? 'Search documents...';
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

  // DEBUG: Log page context detection
  console.log('[SemanticSearchControl] Page context detection:', {
    pageContext,
    pageEntityId,
    pageEntityTypeName,
    fullContext: context,
  });

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

  // DEBUG: Log final scope determination
  console.log('[SemanticSearchControl] Scope determination:', {
    detectedEntityType,
    configuredScope,
    parameterScopeId,
    finalSearchScope: searchScope,
    finalScopeId: scopeId,
  });

  // Query input state
  const [queryInput, setQueryInput] = useState('');
  const [hasSearched, setHasSearched] = useState(false);

  // Find Similar dialog state — URL of the web resource to show in the iframe dialog
  const [findSimilarUrl, setFindSimilarUrl] = useState<string | null>(null);

  // Email dialog state
  const [emailDialogResult, setEmailDialogResult] = useState<SearchResult | null>(null);

  // Filter pane collapse state
  const [isFilterPaneCollapsed, setIsFilterPaneCollapsed] = useState(true);
  const handleToggleFilterPane = useCallback(() => {
    setIsFilterPaneCollapsed(prev => !prev);
  }, []);

  // Initialize services (memoized to prevent recreation)
  const apiService = useMemo(() => new SemanticSearchApiService(apiBaseUrl), [apiBaseUrl]);
  const navigationService = useMemo(() => new NavigationService(), []);

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

  // Handle filter changes - update filter state only.
  // Search is triggered explicitly via Enter key or Search button click.
  const handleFiltersChange = useCallback(
    (newFilters: SearchFilters) => {
      setFilters(newFilters);
    },
    [setFilters]
  );

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

  // Handle reload — re-run the current search query
  const handleReload = useCallback(() => {
    if (query) {
      void search(query, filters);
    }
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

  // Handle Add Document — opens DocumentUploadWizard Code Page dialog
  const handleAddDocument = useCallback(() => {
    void navigationService.openAddDocument(scopeId, searchScope !== 'all' ? searchScope : null, () => {
      // Refresh search results after upload completes
      if (query) {
        void search(query, filters);
      }
    });
  }, [navigationService, scopeId, searchScope, query, search, filters]);

  // Handle Email Document — opens SendEmailDialog with result context
  const handleEmailDocument = useCallback((result: SearchResult) => {
    setEmailDialogResult(result);
  }, []);

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

  // Email dialog: search users via Dataverse WebAPI
  const handleSearchUsers = useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      try {
        const filter = `contains(fullname,'${query.replace(/'/g, "''")}') or contains(internalemailaddress,'${query.replace(/'/g, "''")}')`;
        const result = await context.webAPI.retrieveMultipleRecords(
          'systemuser',
          `?$select=systemuserid,fullname,internalemailaddress&$filter=${filter}&$top=10`
        );
        return (result.entities || [])
          .filter((u: Record<string, unknown>) => u.internalemailaddress)
          .map((u: Record<string, unknown>) => ({
            id: u.systemuserid as string,
            name: `${u.fullname || 'Unknown'} (${u.internalemailaddress})`,
          }));
      } catch (err) {
        console.error('[SemanticSearchControl] User search failed:', err);
        return [];
      }
    },
    [context.webAPI]
  );

  // Email dialog: send email via BFF
  const handleSendEmail = useCallback(
    async (payload: ISendEmailPayload) => {
      // Extract email from "Full Name (email@example.com)" format
      const emailMatch = payload.to.name.match(/\(([^)]+@[^)]+)\)/);
      const toEmail = emailMatch ? emailMatch[1] : payload.to.name;

      const response = await authenticatedFetch(`${apiBaseUrl}/api/communications/send`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          to: [toEmail],
          subject: payload.subject,
          body: payload.body,
          bodyFormat: 'Text',
          associations: emailDialogResult
            ? [
                {
                  entityType: 'sprk_document',
                  entityId: emailDialogResult.documentId,
                },
              ]
            : [],
        }),
      });

      if (!response.ok) {
        throw new Error(`Failed to send email: ${response.status}`);
      }
    },
    [apiBaseUrl, emailDialogResult]
  );

  // Build email defaults from result context
  const emailDefaultSubject = emailDialogResult ? `Document: ${emailDialogResult.name}` : '';
  const emailDefaultBody = emailDialogResult
    ? `Dear Colleague,\n\nPlease find the following document for your review:\n\nDocument: ${emailDialogResult.name}\n\n────\n\n${emailDialogResult.summary || emailDialogResult.tldr || 'No summary available.'}\n\n────\n\nKind regards`
    : '';

  // Determine what content to show in main area
  // Apply "Associated Only" client-side filter at the component level.
  // When toggled ON, only show documents whose matterId/recordId matches the current scopeId.
  const filteredResults = useMemo(() => {
    if (!filters.associatedOnly || !scopeId) return results;
    const normalizedScopeId = scopeId.replace(/[{}]/g, '').toLowerCase();
    return results.filter(r => {
      if (r.matterId && r.matterId.replace(/[{}]/g, '').toLowerCase() === normalizedScopeId) return true;
      if (r.recordId && r.recordId.replace(/[{}]/g, '').toLowerCase() === normalizedScopeId) return true;
      return false;
    });
  }, [results, filters.associatedOnly, scopeId]);
  const filteredTotalCount = filters.associatedOnly ? filteredResults.length : totalCount;

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
        <div className={styles.centeredState}>
          <EmptyState query={query} hasFilters={hasActiveFilters} />
        </div>
      );
    }

    // Results list (uses component-level filteredResults)
    if (filteredResults.length > 0) {
      return (
        <ResultsList
          results={filteredResults}
          isLoading={isLoading}
          isLoadingMore={isLoadingMore}
          hasMore={!filters.associatedOnly && hasMore}
          totalCount={filteredTotalCount}
          threshold={filters.threshold}
          onLoadMore={loadMore}
          onResultClick={handleResultClick}
          onOpenFile={handleOpenFile}
          onOpenRecord={handleOpenRecord}
          onFindSimilar={handleFindSimilar}
          onPreview={handlePreview}
          onSummary={handleSummary}
          onEmailDocument={handleEmailDocument}
          onCopyLink={handleCopyLink}
          onToggleWorkspace={handleToggleWorkspace}
          isInWorkspace={isInWorkspace}
          onViewAll={handleViewAll}
          onReload={handleReload}
          compactMode={compactMode}
        />
      );
    }

    // Initial state (before first search)
    return (
      <div className={styles.centeredState}>
        <Text>Enter a search query to find documents</Text>
      </div>
    );
  };

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
          onAddDocument={handleAddDocument}
          onOpenViewer={handleOpenViewer}
        />
        {hasSearched && !isLoading && totalCount > 0 && (
          <Text size={200} style={{ color: tokens.colorNeutralForeground3, marginTop: '4px' }}>
            {filters.associatedOnly
              ? `${filteredResults?.length ?? 0} associated document${(filteredResults?.length ?? 0) !== 1 ? 's' : ''}`
              : `${totalCount} document${totalCount !== 1 ? 's' : ''} found`}
          </Text>
        )}
      </div>

      {/* Content Region: Sidebar + Main */}
      <div className={styles.content}>
        {/* Sidebar Region: Filters (hidden in compact mode or when disabled) */}
        {showFilters &&
          !compactMode &&
          (isFilterPaneCollapsed ? (
            <div className={styles.sidebarCollapsed}>
              <Tooltip content="Expand filters" relationship="label">
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<ChevronRight20Regular />}
                  onClick={handleToggleFilterPane}
                  aria-label="Expand filters"
                />
              </Tooltip>
            </div>
          ) : (
            <div className={styles.sidebar}>
              <FilterPanel
                filters={filters}
                searchScope={searchScope}
                scopeId={scopeId}
                onFiltersChange={handleFiltersChange}
                onApply={handleSearch}
                disabled={isLoading}
                onCollapse={handleToggleFilterPane}
              />
            </div>
          ))}

        {/* Main Region: Results */}
        <div className={styles.main}>{renderMainContent()}</div>
      </div>

      {/* Footer: View All link (compact mode only) */}
      {compactMode && results.length > 0 && (
        <div className={styles.footer}>
          <Link onClick={handleViewAll}>View all {totalCount} results →</Link>
        </div>
      )}

      {/* Version Footer (always visible) */}
      <div className={styles.versionFooter}>
        <Text size={100}>v1.1.29 • Built 2026-04-05</Text>
      </div>

      {/* Find Similar — shared iframe dialog */}
      <FindSimilarDialog open={!!findSimilarUrl} onClose={() => setFindSimilarUrl(null)} url={findSimilarUrl} />

      {/* Send Email Dialog */}
      <SendEmailDialog
        open={!!emailDialogResult}
        onClose={() => setEmailDialogResult(null)}
        defaultSubject={emailDefaultSubject}
        defaultBody={emailDefaultBody}
        onSearchUsers={handleSearchUsers}
        onSend={handleSendEmail}
      />
    </div>
  );
};

export default SemanticSearchControl;
