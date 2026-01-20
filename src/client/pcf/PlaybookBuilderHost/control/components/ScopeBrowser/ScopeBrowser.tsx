/**
 * Scope Browser Component - Browse and manage AI analysis scopes
 *
 * Provides a tabbed interface for browsing Actions, Skills, Tools, and Knowledge scopes.
 * Supports search with debounce, filtering, ownership badges, and drag-to-canvas functionality.
 * Includes a detail panel for viewing selected scope information.
 *
 * @version 1.1.0
 */

import * as React from 'react';
import { useState, useCallback, useMemo, useRef, useEffect } from 'react';
import {
  TabList,
  Tab,
  SelectTabEvent,
  SelectTabData,
  Input,
  Button,
  Dropdown,
  Option,
  makeStyles,
  tokens,
  shorthands,
  Spinner,
  MessageBar,
  MessageBarBody,
  Text,
  Divider,
  Card,
  CardHeader,
  Badge,
} from '@fluentui/react-components';
import {
  Search20Regular,
  Dismiss20Regular,
  Play20Regular,
  Lightbulb20Regular,
  Wrench20Regular,
  Book20Regular,
  Apps20Regular,
  ChevronRight20Regular,
  Calendar20Regular,
  Info20Regular,
} from '@fluentui/react-icons';
import { ScopeList } from './ScopeList';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type ScopeType = 'actions' | 'skills' | 'tools' | 'knowledge';
export type ScopeTypeFilter = ScopeType | 'all';
export type OwnershipType = 'system' | 'customer';

/** Debounce delay for search input (ms) */
const SEARCH_DEBOUNCE_MS = 300;

export interface ScopeItem {
  id: string;
  name: string;
  displayName: string;
  description: string;
  ownershipType: OwnershipType;
  isImmutable: boolean;
  parentId?: string;
  parentName?: string;
  modifiedOn: string;
  createdOn: string;
}

export interface ScopeBrowserProps {
  /** Callback when a scope is selected for viewing */
  onViewScope?: (scope: ScopeItem, type: ScopeType) => void;
  /** Callback when a scope is selected for "Save As" */
  onSaveAsScope?: (scope: ScopeItem, type: ScopeType) => void;
  /** Callback when a scope is extended */
  onExtendScope?: (scope: ScopeItem, type: ScopeType) => void;
  /** Callback when a customer scope is deleted */
  onDeleteScope?: (scope: ScopeItem, type: ScopeType) => void;
  /** Callback when a scope is dragged to canvas */
  onDragToCanvas?: (scope: ScopeItem, type: ScopeType) => void;
  /** Callback when a scope is selected for use */
  onSelectScope?: (scope: ScopeItem, type: ScopeType) => void;
  /** Function to fetch scopes by type */
  fetchScopes?: (type: ScopeType, searchTerm?: string) => Promise<ScopeItem[]>;
  /** Whether the component is in read-only mode */
  readOnly?: boolean;
  /** Whether to show the detail panel when a scope is selected */
  showDetailPanel?: boolean;
  /** Initial type filter (defaults to 'actions') */
  initialTypeFilter?: ScopeTypeFilter;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground2,
  },
  filterRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginBottom: tokens.spacingVerticalM,
  },
  filterDropdown: {
    minWidth: '140px',
  },
  tabList: {
    marginBottom: tokens.spacingVerticalM,
  },
  searchContainer: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
  },
  searchInput: {
    flex: 1,
  },
  mainContent: {
    flex: 1,
    display: 'flex',
    ...shorthands.overflow('hidden'),
  },
  content: {
    flex: 1,
    ...shorthands.overflow('hidden'),
    display: 'flex',
    flexDirection: 'column',
  },
  detailPanel: {
    width: '300px',
    ...shorthands.borderLeft('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground2,
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.overflow('hidden'),
  },
  detailPanelHeader: {
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground3,
  },
  detailPanelTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
  },
  detailPanelContent: {
    flex: 1,
    ...shorthands.overflow('auto'),
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },
  detailSection: {
    marginBottom: tokens.spacingVerticalL,
  },
  detailLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalXS,
    display: 'block',
  },
  detailValue: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    wordBreak: 'break-word',
  },
  detailBadgeRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    marginBottom: tokens.spacingVerticalS,
  },
  detailMetaRow: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalXS),
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    marginBottom: tokens.spacingVerticalXS,
  },
  systemBadge: {
    backgroundColor: tokens.colorPaletteBlueBorderActive,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  customerBadge: {
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorPaletteGreenForeground1,
  },
  emptyDetailPanel: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
    ...shorthands.padding(tokens.spacingVerticalL),
  },
  loadingContainer: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    ...shorthands.padding(tokens.spacingVerticalXXL),
  },
  errorContainer: {
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
  },
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    flex: 1,
    ...shorthands.padding(tokens.spacingVerticalXXL),
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Tab Configuration
// ─────────────────────────────────────────────────────────────────────────────

const TAB_CONFIG: Record<ScopeType, { label: string; icon: React.ReactElement }> = {
  actions: { label: 'Actions', icon: <Play20Regular /> },
  skills: { label: 'Skills', icon: <Lightbulb20Regular /> },
  tools: { label: 'Tools', icon: <Wrench20Regular /> },
  knowledge: { label: 'Knowledge', icon: <Book20Regular /> },
};

const TYPE_FILTER_CONFIG: Record<ScopeTypeFilter, { label: string; icon: React.ReactElement }> = {
  all: { label: 'All Types', icon: <Apps20Regular /> },
  actions: { label: 'Actions', icon: <Play20Regular /> },
  skills: { label: 'Skills', icon: <Lightbulb20Regular /> },
  tools: { label: 'Tools', icon: <Wrench20Regular /> },
  knowledge: { label: 'Knowledge', icon: <Book20Regular /> },
};

// ─────────────────────────────────────────────────────────────────────────────
// Mock Data (for development/testing)
// ─────────────────────────────────────────────────────────────────────────────

const createMockScopes = (type: ScopeType): ScopeItem[] => {
  const baseScopes: Partial<ScopeItem>[] = [
    {
      name: `SYS-${type.toUpperCase()}-001`,
      displayName: `Standard ${type.slice(0, -1)} One`,
      description: `System-provided ${type.slice(0, -1)} for common operations.`,
      ownershipType: 'system',
      isImmutable: true,
    },
    {
      name: `SYS-${type.toUpperCase()}-002`,
      displayName: `Standard ${type.slice(0, -1)} Two`,
      description: `Another system-provided ${type.slice(0, -1)}.`,
      ownershipType: 'system',
      isImmutable: true,
    },
    {
      name: `CUST-${type.toUpperCase()}-001`,
      displayName: `Custom ${type.slice(0, -1)} One`,
      description: `Customer-created ${type.slice(0, -1)} for specific needs.`,
      ownershipType: 'customer',
      isImmutable: false,
    },
  ];

  return baseScopes.map((scope, index) => ({
    id: `${type}-${index + 1}`,
    name: scope.name!,
    displayName: scope.displayName!,
    description: scope.description!,
    ownershipType: scope.ownershipType!,
    isImmutable: scope.isImmutable!,
    modifiedOn: new Date(Date.now() - index * 86400000).toISOString(),
    createdOn: new Date(Date.now() - (index + 10) * 86400000).toISOString(),
  }));
};

const defaultFetchScopes = async (type: ScopeType, searchTerm?: string): Promise<ScopeItem[]> => {
  // Simulate API delay
  await new Promise((resolve) => setTimeout(resolve, 500));

  let scopes = createMockScopes(type);

  if (searchTerm) {
    const term = searchTerm.toLowerCase();
    scopes = scopes.filter(
      (s) =>
        s.name.toLowerCase().includes(term) ||
        s.displayName.toLowerCase().includes(term) ||
        s.description.toLowerCase().includes(term)
    );
  }

  return scopes;
};

// ─────────────────────────────────────────────────────────────────────────────
// Helper: Custom hook for debounced search
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Custom hook for debouncing a value.
 * Returns the debounced value that updates after the specified delay.
 */
function useDebounce<T>(value: T, delay: number): T {
  const [debouncedValue, setDebouncedValue] = useState<T>(value);

  useEffect(() => {
    const handler = setTimeout(() => {
      setDebouncedValue(value);
    }, delay);

    return () => {
      clearTimeout(handler);
    };
  }, [value, delay]);

  return debouncedValue;
}

// ─────────────────────────────────────────────────────────────────────────────
// Helper: Detail Panel Component
// ─────────────────────────────────────────────────────────────────────────────

interface ScopeDetailPanelProps {
  scope: ScopeItem | null;
  scopeType: ScopeType;
  onSelect?: (scope: ScopeItem, type: ScopeType) => void;
}

const ScopeDetailPanel: React.FC<ScopeDetailPanelProps> = ({ scope, scopeType, onSelect }) => {
  const styles = useStyles();

  // Format date for display
  const formatDate = useCallback((dateString: string) => {
    try {
      return new Date(dateString).toLocaleDateString(undefined, {
        year: 'numeric',
        month: 'long',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit',
      });
    } catch {
      return dateString;
    }
  }, []);

  if (!scope) {
    return (
      <div className={styles.detailPanel}>
        <div className={styles.emptyDetailPanel}>
          <Info20Regular />
          <Text>Select a scope to view details</Text>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.detailPanel}>
      {/* Header */}
      <div className={styles.detailPanelHeader}>
        <Text className={styles.detailPanelTitle}>{scope.displayName}</Text>
      </div>

      {/* Content */}
      <div className={styles.detailPanelContent}>
        {/* Ownership & Type Badges */}
        <div className={styles.detailBadgeRow}>
          <Badge
            appearance="filled"
            size="small"
            className={scope.ownershipType === 'system' ? styles.systemBadge : styles.customerBadge}
          >
            {scope.ownershipType === 'system' ? 'System' : 'Customer'}
          </Badge>
          <Badge appearance="outline" size="small">
            {TYPE_FILTER_CONFIG[scopeType].label}
          </Badge>
          {scope.isImmutable && (
            <Badge appearance="outline" size="small" color="warning">
              Immutable
            </Badge>
          )}
        </div>

        <Divider />

        {/* Name */}
        <div className={styles.detailSection}>
          <Text className={styles.detailLabel}>Technical Name</Text>
          <Text className={styles.detailValue}>{scope.name}</Text>
        </div>

        {/* Description */}
        <div className={styles.detailSection}>
          <Text className={styles.detailLabel}>Description</Text>
          <Text className={styles.detailValue}>{scope.description || 'No description provided'}</Text>
        </div>

        {/* Parent (if extended) */}
        {scope.parentName && (
          <div className={styles.detailSection}>
            <Text className={styles.detailLabel}>Extends</Text>
            <Text className={styles.detailValue}>{scope.parentName}</Text>
          </div>
        )}

        {/* Dates */}
        <div className={styles.detailSection}>
          <div className={styles.detailMetaRow}>
            <Calendar20Regular />
            <Text>Created: {formatDate(scope.createdOn)}</Text>
          </div>
          <div className={styles.detailMetaRow}>
            <Calendar20Regular />
            <Text>Modified: {formatDate(scope.modifiedOn)}</Text>
          </div>
        </div>

        {/* Select Button */}
        {onSelect && (
          <>
            <Divider />
            <Button
              appearance="primary"
              style={{ marginTop: tokens.spacingVerticalM }}
              onClick={() => onSelect(scope, scopeType)}
              icon={<ChevronRight20Regular />}
              iconPosition="after"
            >
              Select Scope
            </Button>
          </>
        )}
      </div>
    </div>
  );
};

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ScopeBrowser: React.FC<ScopeBrowserProps> = ({
  onViewScope,
  onSaveAsScope,
  onExtendScope,
  onDeleteScope,
  onDragToCanvas,
  onSelectScope,
  fetchScopes = defaultFetchScopes,
  readOnly = false,
  showDetailPanel = true,
  initialTypeFilter = 'actions',
}) => {
  const styles = useStyles();

  // State
  const [typeFilter, setTypeFilter] = useState<ScopeTypeFilter>(initialTypeFilter);
  const [selectedTab, setSelectedTab] = useState<ScopeType>(
    initialTypeFilter === 'all' ? 'actions' : initialTypeFilter
  );
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedScope, setSelectedScope] = useState<ScopeItem | null>(null);
  const [scopes, setScopes] = useState<Record<ScopeType, ScopeItem[]>>({
    actions: [],
    skills: [],
    tools: [],
    knowledge: [],
  });
  const [isLoading, setIsLoading] = useState<Record<ScopeType, boolean>>({
    actions: false,
    skills: false,
    tools: false,
    knowledge: false,
  });
  const [errors, setErrors] = useState<Record<ScopeType, string | null>>({
    actions: null,
    skills: null,
    tools: null,
    knowledge: null,
  });
  const [loadedTabs, setLoadedTabs] = useState<Set<ScopeType>>(new Set());

  // Debounce search term
  const debouncedSearchTerm = useDebounce(searchTerm, SEARCH_DEBOUNCE_MS);
  const searchTriggeredRef = useRef(false);

  // Load scopes for a tab (lazy loading)
  const loadScopes = useCallback(
    async (type: ScopeType, search?: string) => {
      setIsLoading((prev) => ({ ...prev, [type]: true }));
      setErrors((prev) => ({ ...prev, [type]: null }));

      try {
        const result = await fetchScopes(type, search);
        setScopes((prev) => ({ ...prev, [type]: result }));
        setLoadedTabs((prev) => new Set(prev).add(type));
      } catch (error) {
        setErrors((prev) => ({
          ...prev,
          [type]: error instanceof Error ? error.message : 'Failed to load scopes',
        }));
      } finally {
        setIsLoading((prev) => ({ ...prev, [type]: false }));
      }
    },
    [fetchScopes]
  );

  // Load all scope types (for "All" filter)
  const loadAllScopes = useCallback(
    async (search?: string) => {
      const types: ScopeType[] = ['actions', 'skills', 'tools', 'knowledge'];
      await Promise.all(types.map((type) => loadScopes(type, search)));
    },
    [loadScopes]
  );

  // Load scopes when tab changes (lazy load)
  useEffect(() => {
    if (typeFilter === 'all') {
      // Load all types when "All" is selected
      const types: ScopeType[] = ['actions', 'skills', 'tools', 'knowledge'];
      types.forEach((type) => {
        if (!loadedTabs.has(type) && !isLoading[type]) {
          loadScopes(type);
        }
      });
    } else if (!loadedTabs.has(selectedTab) && !isLoading[selectedTab]) {
      loadScopes(selectedTab);
    }
  }, [selectedTab, typeFilter, loadedTabs, isLoading, loadScopes]);

  // Handle debounced search - trigger search when debounced value changes
  useEffect(() => {
    // Skip initial render
    if (!searchTriggeredRef.current && debouncedSearchTerm === '') {
      searchTriggeredRef.current = true;
      return;
    }
    searchTriggeredRef.current = true;

    if (typeFilter === 'all') {
      loadAllScopes(debouncedSearchTerm || undefined);
    } else {
      loadScopes(selectedTab, debouncedSearchTerm || undefined);
    }
  }, [debouncedSearchTerm, typeFilter, selectedTab, loadScopes, loadAllScopes]);

  // Handle type filter change
  const handleTypeFilterChange = useCallback(
    (_: unknown, data: { optionValue?: string }) => {
      const newFilter = data.optionValue as ScopeTypeFilter;
      setTypeFilter(newFilter);
      if (newFilter !== 'all') {
        setSelectedTab(newFilter);
      }
      setSelectedScope(null);
      setSearchTerm('');
    },
    []
  );

  // Handle tab change (for non-"All" filter)
  const handleTabSelect = useCallback((_: SelectTabEvent, data: SelectTabData) => {
    setSelectedTab(data.value as ScopeType);
    setSelectedScope(null);
    setSearchTerm(''); // Clear search when switching tabs
  }, []);

  // Handle scope row click (for detail panel)
  const handleScopeClick = useCallback((scope: ScopeItem) => {
    setSelectedScope((prev) => (prev?.id === scope.id ? null : scope));
  }, []);

  // Handle clear search
  const handleClearSearch = useCallback(() => {
    setSearchTerm('');
  }, []);

  // Get scopes for display (filtered by type or all)
  const displayScopes = useMemo(() => {
    if (typeFilter === 'all') {
      // Combine all scopes with type indicator
      const allScopes: { scope: ScopeItem; type: ScopeType }[] = [];
      (Object.keys(scopes) as ScopeType[]).forEach((type) => {
        scopes[type].forEach((scope) => {
          allScopes.push({ scope, type });
        });
      });
      // Sort by modified date (newest first)
      allScopes.sort((a, b) =>
        new Date(b.scope.modifiedOn).getTime() - new Date(a.scope.modifiedOn).getTime()
      );
      return allScopes.map((item) => item.scope);
    }
    return scopes[selectedTab];
  }, [typeFilter, scopes, selectedTab]);

  // Current loading state
  const currentLoading = useMemo(() => {
    if (typeFilter === 'all') {
      return Object.values(isLoading).some((l) => l);
    }
    return isLoading[selectedTab];
  }, [typeFilter, isLoading, selectedTab]);

  // Current error state
  const currentError = useMemo(() => {
    if (typeFilter === 'all') {
      const errorTypes = (Object.keys(errors) as ScopeType[]).filter((t) => errors[t]);
      if (errorTypes.length > 0) {
        return `Failed to load: ${errorTypes.join(', ')}`;
      }
      return null;
    }
    return errors[selectedTab];
  }, [typeFilter, errors, selectedTab]);

  // Determine the scope type for the selected scope (needed for "All" mode)
  const selectedScopeType = useMemo((): ScopeType => {
    if (typeFilter === 'all' && selectedScope) {
      // Find which type the selected scope belongs to
      for (const type of Object.keys(scopes) as ScopeType[]) {
        if (scopes[type].some((s) => s.id === selectedScope.id)) {
          return type;
        }
      }
    }
    return selectedTab;
  }, [typeFilter, selectedScope, scopes, selectedTab]);

  return (
    <div className={styles.container}>
      {/* Header with filter, tabs, and search */}
      <div className={styles.header}>
        {/* Type Filter Dropdown */}
        <div className={styles.filterRow}>
          <Text style={{ fontWeight: tokens.fontWeightSemibold }}>Type:</Text>
          <Dropdown
            className={styles.filterDropdown}
            value={TYPE_FILTER_CONFIG[typeFilter].label}
            onOptionSelect={handleTypeFilterChange}
          >
            {(Object.keys(TYPE_FILTER_CONFIG) as ScopeTypeFilter[]).map((filter) => (
              <Option key={filter} value={filter}>
                {TYPE_FILTER_CONFIG[filter].label}
              </Option>
            ))}
          </Dropdown>
        </div>

        {/* Tabs (only shown when not filtering by "All") */}
        {typeFilter !== 'all' && (
          <TabList
            className={styles.tabList}
            selectedValue={selectedTab}
            onTabSelect={handleTabSelect}
            size="small"
          >
            {(Object.keys(TAB_CONFIG) as ScopeType[]).map((type) => (
              <Tab key={type} value={type} icon={TAB_CONFIG[type].icon}>
                {TAB_CONFIG[type].label}
              </Tab>
            ))}
          </TabList>
        )}

        {/* Search with debounce */}
        <div className={styles.searchContainer}>
          <Input
            className={styles.searchInput}
            placeholder={
              typeFilter === 'all'
                ? 'Search all scopes...'
                : `Search ${TAB_CONFIG[selectedTab].label.toLowerCase()}...`
            }
            value={searchTerm}
            onChange={(_, data) => setSearchTerm(data.value)}
            contentBefore={<Search20Regular />}
            contentAfter={
              searchTerm ? (
                <Button
                  appearance="subtle"
                  size="small"
                  icon={<Dismiss20Regular />}
                  onClick={handleClearSearch}
                  aria-label="Clear search"
                />
              ) : undefined
            }
          />
          {/* Show loading indicator while debouncing */}
          {searchTerm !== debouncedSearchTerm && <Spinner size="tiny" />}
        </div>
      </div>

      {/* Main content area with list and detail panel */}
      <div className={styles.mainContent}>
        {/* Content area */}
        <div className={styles.content}>
          {/* Loading state */}
          {currentLoading && (
            <div className={styles.loadingContainer}>
              <Spinner
                size="medium"
                label={
                  typeFilter === 'all'
                    ? 'Loading all scopes...'
                    : `Loading ${TAB_CONFIG[selectedTab].label.toLowerCase()}...`
                }
              />
            </div>
          )}

          {/* Error state */}
          {!currentLoading && currentError && (
            <div className={styles.errorContainer}>
              <MessageBar intent="error">
                <MessageBarBody>{currentError}</MessageBarBody>
              </MessageBar>
            </div>
          )}

          {/* Empty state */}
          {!currentLoading && !currentError && displayScopes.length === 0 && (
            <div className={styles.emptyState}>
              {typeFilter === 'all' ? <Apps20Regular /> : TAB_CONFIG[selectedTab].icon}
              <p>
                No{' '}
                {typeFilter === 'all' ? 'scopes' : TAB_CONFIG[selectedTab].label.toLowerCase()}{' '}
                found
              </p>
              {searchTerm && <p>Try adjusting your search terms</p>}
            </div>
          )}

          {/* Scope list */}
          {!currentLoading && !currentError && displayScopes.length > 0 && (
            <ScopeList
              scopes={displayScopes}
              scopeType={selectedScopeType}
              onView={
                onViewScope
                  ? (scope) => onViewScope(scope, selectedScopeType)
                  : undefined
              }
              onSaveAs={
                onSaveAsScope
                  ? (scope) => onSaveAsScope(scope, selectedScopeType)
                  : undefined
              }
              onExtend={
                onExtendScope
                  ? (scope) => onExtendScope(scope, selectedScopeType)
                  : undefined
              }
              onDelete={
                onDeleteScope
                  ? (scope) => onDeleteScope(scope, selectedScopeType)
                  : undefined
              }
              onDragStart={
                onDragToCanvas
                  ? (scope) => onDragToCanvas(scope, selectedScopeType)
                  : undefined
              }
              onRowClick={showDetailPanel ? handleScopeClick : undefined}
              selectedScopeId={selectedScope?.id}
              readOnly={readOnly}
            />
          )}
        </div>

        {/* Detail Panel */}
        {showDetailPanel && (
          <ScopeDetailPanel
            scope={selectedScope}
            scopeType={selectedScopeType}
            onSelect={onSelectScope}
          />
        )}
      </div>
    </div>
  );
};

export default ScopeBrowser;
