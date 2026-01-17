/**
 * Scope Browser Component - Browse and manage AI analysis scopes
 *
 * Provides a tabbed interface for browsing Actions, Skills, Tools, and Knowledge scopes.
 * Supports search, filtering, ownership badges, and drag-to-canvas functionality.
 *
 * @version 1.0.0
 */

import * as React from 'react';
import { useState, useCallback, useMemo } from 'react';
import {
  TabList,
  Tab,
  SelectTabEvent,
  SelectTabData,
  Input,
  Button,
  makeStyles,
  tokens,
  shorthands,
  Spinner,
  MessageBar,
  MessageBarBody,
} from '@fluentui/react-components';
import {
  Search20Regular,
  Dismiss20Regular,
  Play20Regular,
  Lightbulb20Regular,
  Wrench20Regular,
  Book20Regular,
} from '@fluentui/react-icons';
import { ScopeList } from './ScopeList';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type ScopeType = 'actions' | 'skills' | 'tools' | 'knowledge';
export type OwnershipType = 'system' | 'customer';

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
  /** Function to fetch scopes by type */
  fetchScopes?: (type: ScopeType, searchTerm?: string) => Promise<ScopeItem[]>;
  /** Whether the component is in read-only mode */
  readOnly?: boolean;
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
  content: {
    flex: 1,
    ...shorthands.overflow('hidden'),
    display: 'flex',
    flexDirection: 'column',
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
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ScopeBrowser: React.FC<ScopeBrowserProps> = ({
  onViewScope,
  onSaveAsScope,
  onExtendScope,
  onDeleteScope,
  onDragToCanvas,
  fetchScopes = defaultFetchScopes,
  readOnly = false,
}) => {
  const styles = useStyles();

  // State
  const [selectedTab, setSelectedTab] = useState<ScopeType>('actions');
  const [searchTerm, setSearchTerm] = useState('');
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

  // Load scopes when tab changes (lazy load)
  React.useEffect(() => {
    if (!loadedTabs.has(selectedTab) && !isLoading[selectedTab]) {
      loadScopes(selectedTab);
    }
  }, [selectedTab, loadedTabs, isLoading, loadScopes]);

  // Handle tab change
  const handleTabSelect = useCallback((_: SelectTabEvent, data: SelectTabData) => {
    setSelectedTab(data.value as ScopeType);
    setSearchTerm(''); // Clear search when switching tabs
  }, []);

  // Handle search
  const handleSearch = useCallback(() => {
    loadScopes(selectedTab, searchTerm || undefined);
  }, [loadScopes, selectedTab, searchTerm]);

  // Handle search on Enter
  const handleSearchKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLInputElement>) => {
      if (e.key === 'Enter') {
        handleSearch();
      }
    },
    [handleSearch]
  );

  // Handle clear search
  const handleClearSearch = useCallback(() => {
    setSearchTerm('');
    loadScopes(selectedTab);
  }, [loadScopes, selectedTab]);

  // Current tab data
  const currentScopes = useMemo(() => scopes[selectedTab], [scopes, selectedTab]);
  const currentLoading = isLoading[selectedTab];
  const currentError = errors[selectedTab];

  return (
    <div className={styles.container}>
      {/* Header with tabs and search */}
      <div className={styles.header}>
        {/* Tabs */}
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

        {/* Search */}
        <div className={styles.searchContainer}>
          <Input
            className={styles.searchInput}
            placeholder={`Search ${TAB_CONFIG[selectedTab].label.toLowerCase()}...`}
            value={searchTerm}
            onChange={(_, data) => setSearchTerm(data.value)}
            onKeyDown={handleSearchKeyDown}
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
          <Button appearance="primary" size="small" onClick={handleSearch} disabled={currentLoading}>
            Search
          </Button>
        </div>
      </div>

      {/* Content area */}
      <div className={styles.content}>
        {/* Loading state */}
        {currentLoading && (
          <div className={styles.loadingContainer}>
            <Spinner size="medium" label={`Loading ${TAB_CONFIG[selectedTab].label.toLowerCase()}...`} />
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
        {!currentLoading && !currentError && currentScopes.length === 0 && (
          <div className={styles.emptyState}>
            {TAB_CONFIG[selectedTab].icon}
            <p>No {TAB_CONFIG[selectedTab].label.toLowerCase()} found</p>
            {searchTerm && <p>Try adjusting your search terms</p>}
          </div>
        )}

        {/* Scope list */}
        {!currentLoading && !currentError && currentScopes.length > 0 && (
          <ScopeList
            scopes={currentScopes}
            scopeType={selectedTab}
            onView={onViewScope ? (scope) => onViewScope(scope, selectedTab) : undefined}
            onSaveAs={onSaveAsScope ? (scope) => onSaveAsScope(scope, selectedTab) : undefined}
            onExtend={onExtendScope ? (scope) => onExtendScope(scope, selectedTab) : undefined}
            onDelete={onDeleteScope ? (scope) => onDeleteScope(scope, selectedTab) : undefined}
            onDragStart={onDragToCanvas ? (scope) => onDragToCanvas(scope, selectedTab) : undefined}
            readOnly={readOnly}
          />
        )}
      </div>
    </div>
  );
};

export default ScopeBrowser;
